using System.Collections.Generic;
using Menger.Core;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace Menger.Rendering
{
    /// <summary>
    /// Owns the sponge surface (a set of chunk meshes) and the entrance outlines. The surface is built by Burst jobs (<see cref="MeshBuild"/>)
    /// in a frame's logic coordinates and placed every frame through (display map) x (frame conversion), so the world scale animates on the
    /// transform alone and a frame flip never moves or rebuilds what is on screen: the old chunks keep being drawn under the composed map until
    /// the new frame's chunks are ready. Only chunks whose geometry changed are uploaded again; chunk objects are pooled.
    /// </summary>
    public sealed class WorldRenderer : MonoBehaviour
    {
        public Material surfaceMaterial, glowMaterial;
        public float visibility = 1000f;

        MengerSettings cfg;
        WorldState world;
        Camera cam;

        sealed class Chunk
        {
            public GameObject go;
            public Mesh mesh;
            public uint hash;
            public bool used;
        }
        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
        };

        GameObject surfaceGo;
        readonly Dictionary<(int, int, int, int), Chunk> chunks = new Dictionary<(int, int, int, int), Chunk>();
        readonly Stack<Chunk> pool = new Stack<Chunk>();
        readonly List<(int, int, int, int)> retire = new List<(int, int, int, int)>();
        RegionAddress surfaceFrame;
        MeshBuild pending;
        RegionAddress pendingFrame;
        Vector3 lastBuildCam;
        bool hasMesh;

        Material lookMaterial;           // instance of surfaceMaterial, re-tuned from the settings when they change
        Texture2D detailTexture;
        int detailKey = int.MinValue, lookKey;
        static readonly int AmbientId = Shader.PropertyToID("_Ambient"), KeyId = Shader.PropertyToID("_Key"), FillId = Shader.PropertyToID("_Fill"),
            HueBaseId = Shader.PropertyToID("_HueBase"), HueStepId = Shader.PropertyToID("_HueStep"), SatId = Shader.PropertyToID("_Sat"), ValId = Shader.PropertyToID("_Val"),
            DetailTexId = Shader.PropertyToID("_DetailTex"), DetailTileId = Shader.PropertyToID("_DetailTile"), DetailStrengthId = Shader.PropertyToID("_DetailStrength"),
            RoughBaseId = Shader.PropertyToID("_RoughBase"), RoughVarId = Shader.PropertyToID("_RoughVar"), SpecId = Shader.PropertyToID("_SpecStrength"), ReflectId = Shader.PropertyToID("_Reflect"), PhaseId = Shader.PropertyToID("_Phase");

        sealed class Outline
        {
            public GameObject go;
            public Mesh mesh;
            public Entrance[] entrances;
            public Color32[] colors;
            public int lastSignature = int.MinValue;
        }
        readonly Dictionary<RegionAddress, Outline> outlines = new Dictionary<RegionAddress, Outline>();
        readonly List<RegionAddress> removeScratch = new List<RegionAddress>();

        /// <summary>Of the last applied build: cube leaves, visible faces before merging, quads after, chunks drawn, chunks uploaded.</summary>
        public int Leaves, Faces, Quads, Triangles, MaxLevel, Chunks, ChunksUploaded, BuildsApplied;
        public double LastBuildMilliseconds;
        public bool BuildInFlight => pending != null;
        public RegionAddress SurfaceFrame => surfaceFrame;
        public int SelectedEntranceKey { get; private set; } = -1;

        public void Init(MengerSettings settings, WorldState w, Camera camera)
        {
            cfg = settings; world = w; cam = camera;
            surfaceGo = new GameObject("SpongeSurface");
            surfaceGo.transform.SetParent(transform, false);
            surfaceGo.SetActive(false);
            lookMaterial = new Material(surfaceMaterial) { name = "SurfaceMaterial (runtime)" };
            ApplyLook();
        }

        void OnDestroy()
        {
            if (pending != null) { pending.Dispose(); pending = null; }
            foreach (var c in chunks.Values) if (c.mesh != null) Destroy(c.mesh);
            foreach (var c in pool) if (c.mesh != null) Destroy(c.mesh);
            if (lookMaterial != null) Destroy(lookMaterial);
            if (detailTexture != null) Destroy(detailTexture);
            foreach (var o in outlines.Values) { if (o.mesh != null) Destroy(o.mesh); }
        }

        void LateUpdate()
        {
            if (world == null) return;
            PollBuild();
            MaybeStartBuild();
            PlaceSurface();
            ApplyLook();
            UpdateOutlines();
        }

        // ---- material -----------------------------------------------------------------------------------------------------

        static int Mix(int h, float f) => unchecked(h * 31 + System.BitConverter.SingleToInt32Bits(f));

        /// <summary>Pushes the surface-material settings into the material (only when they changed) and the zoom phase (every frame).</summary>
        void ApplyLook()
        {
            int key = 17;
            key = Mix(key, cfg.surfaceAmbient); key = Mix(key, cfg.surfaceSunLight); key = Mix(key, cfg.surfaceFillLight);
            key = Mix(key, cfg.surfaceHueBase); key = Mix(key, cfg.surfaceHueStep); key = Mix(key, cfg.surfaceSaturation); key = Mix(key, cfg.surfaceBrightness);
            key = Mix(key, cfg.rootSize); key = Mix(key, cfg.detailTileDivisions); key = Mix(key, cfg.detailStrength);
            key = Mix(key, cfg.roughness); key = Mix(key, cfg.roughnessVariation); key = Mix(key, cfg.specularStrength); key = Mix(key, cfg.skyReflection);
            key = Mix(key, cfg.detailSeed); key = Mix(key, cfg.detailResolution); key = Mix(key, cfg.detailContrast);
            var m = lookMaterial;
            if (key != lookKey || detailKey == int.MinValue)
            {
                lookKey = key;
                // with no brightness or roughness variation the texture has no effect: do not paint it
                bool textured = cfg.detailStrength > 0f || cfg.roughnessVariation > 0f;
                int tkey = textured ? cfg.detailSeed * 7919 + cfg.detailResolution * 31 + Mathf.RoundToInt(cfg.detailContrast * 1000f) : -1;
                if (tkey != detailKey)
                {
                    if (detailTexture != null) Destroy(detailTexture);
                    detailTexture = textured ? SurfaceDetail.Build(Mathf.Clamp(cfg.detailResolution, 16, 2048), cfg.detailSeed, cfg.detailContrast) : null;
                    detailKey = tkey;
                }
                m.SetTexture(DetailTexId, detailTexture != null ? detailTexture : Texture2D.grayTexture);
                m.SetFloat(AmbientId, cfg.surfaceAmbient); m.SetFloat(KeyId, cfg.surfaceSunLight); m.SetFloat(FillId, cfg.surfaceFillLight);
                m.SetFloat(HueBaseId, cfg.surfaceHueBase); m.SetFloat(HueStepId, cfg.surfaceHueStep);
                m.SetFloat(SatId, cfg.surfaceSaturation); m.SetFloat(ValId, cfg.surfaceBrightness);
                m.SetFloat(DetailTileId, cfg.rootSize / Mathf.Max(1, cfg.detailTileDivisions));
                m.SetFloat(DetailStrengthId, cfg.detailStrength);
                m.SetFloat(RoughBaseId, cfg.roughness); m.SetFloat(RoughVarId, cfg.roughnessVariation); m.SetFloat(SpecId, cfg.specularStrength); m.SetFloat(ReflectId, cfg.skyReflection);
            }
            // phase: continuous depth in the displayed meshes' own frame (the parent's mesh drawn while the child is current has phase 1 + e)
            if (hasMesh) m.SetFloat(PhaseId, world.ContinuousDepth - surfaceFrame.Depth);
        }

        // ---- surface -----------------------------------------------------------------------------------------------------

        void PollBuild()
        {
            if (pending == null || !pending.IsCompleted) return;
            var build = pending; var frame = pendingFrame;
            pending = null;
            try
            {
                build.Complete();
                if (frame == world.Frame) Apply(build, frame); // otherwise the frame changed meanwhile: a fresh build is started next
            }
            finally { build.Dispose(); }
        }

        void Apply(MeshBuild b, RegionAddress frame)
        {
            foreach (var c in chunks.Values) c.used = false;
            int leaves = 0, faces = 0, quads = 0, maxLevel = 0, uploaded = 0;
            for (int i = 0; i < b.Count; i++)
            {
                ref ChunkOut o = ref b.Out(i);
                leaves += o.Leaves; faces += o.Faces; quads += o.Quads;
                if (o.MaxLevel > maxLevel) maxLevel = o.MaxLevel;
                if (o.Quads == 0) continue;
                var r = b.Root(i);
                var key = ((int)r.X, (int)r.Y, (int)r.Z, (int)r.Size);
                if (!chunks.TryGetValue(key, out var chunk)) { chunk = pool.Count > 0 ? pool.Pop() : NewChunk(); chunk.hash = 0; chunks[key] = chunk; }
                chunk.used = true;
                if (chunk.hash != o.Hash) { Upload(chunk, ref o); chunk.hash = o.Hash; uploaded++; }
                if (!chunk.go.activeSelf) chunk.go.SetActive(true);
            }
            retire.Clear();
            foreach (var kv in chunks) if (!kv.Value.used) retire.Add(kv.Key);
            foreach (var key in retire)
            {
                var chunk = chunks[key];
                chunks.Remove(key);
                chunk.go.SetActive(false); chunk.hash = 0;
                pool.Push(chunk);
            }
            surfaceFrame = frame;
            hasMesh = true;
            Leaves = leaves; Faces = faces; Quads = quads; Triangles = quads * 2; MaxLevel = maxLevel; Chunks = chunks.Count; ChunksUploaded = uploaded;
            LastBuildMilliseconds = b.Milliseconds;
            BuildsApplied++;
        }

        Chunk NewChunk()
        {
            var c = new Chunk { go = new GameObject("Chunk") };
            c.go.transform.SetParent(surfaceGo.transform, false);
            c.mesh = new Mesh { name = "Chunk", indexFormat = IndexFormat.UInt32 };
            c.mesh.MarkDynamic();
            c.go.AddComponent<MeshFilter>().sharedMesh = c.mesh;
            var r = c.go.AddComponent<MeshRenderer>();
            r.sharedMaterial = lookMaterial;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            return c;
        }

        unsafe void Upload(Chunk c, ref ChunkOut o)
        {
            var m = c.mesh;
            int vc = o.Vertices.Length, ic = o.Indices.Length;
            var va = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<SurfaceVertex>(o.Vertices.Ptr, vc, Allocator.None);
            var ia = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<uint>(o.Indices.Ptr, ic, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safety = AtomicSafetyHandle.Create();
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref va, safety);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref ia, safety);
#endif
            m.SetVertexBufferParams(vc, Layout);
            m.SetVertexBufferData(va, 0, 0, vc);
            m.SetIndexBufferParams(ic, IndexFormat.UInt32);
            m.SetIndexBufferData(ia, 0, 0, ic);
            m.subMeshCount = 1;
            m.SetSubMesh(0, new SubMeshDescriptor(0, ic, MeshTopology.Triangles), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(safety);
#endif
            var min = new Vector3(o.MinX, o.MinY, o.MinZ); var max = new Vector3(o.MaxX, o.MaxY, o.MaxZ);
            m.bounds = new Bounds((min + max) * 0.5f, max - min);
        }

        void MaybeStartBuild()
        {
            if (pending != null) return;
            Vector3 camLogic = world.DisplayToLogic(cam.transform.position);
            bool need = !hasMesh || surfaceFrame != world.Frame || (camLogic - lastBuildCam).magnitude > cfg.rebuildDistance;
            if (!need) return;
            var lod = new LodParams { Ratio = cfg.lodRatio, MinLeaf = cfg.minLeafSize, MaxExpandDistance = visibility * 1.3f / Mathf.Max(world.Scale, 0.01f) };
            pendingFrame = world.Frame;
            lastBuildCam = camLogic;
            pending = MeshBuild.Schedule(world.Context, camLogic, lod, cfg.greedyMerge, cfg.rootSize / MeshBuild.ChunkDivisions);
        }

        void PlaceSurface()
        {
            bool show = hasMesh && Mathf.Abs(surfaceFrame.Depth - world.Frame.Depth) <= 3;
            if (surfaceGo.activeSelf != show) surfaceGo.SetActive(show);
            if (show) Place(surfaceGo.transform, surfaceFrame);
        }

        void Place(Transform t, RegionAddress frame)
        {
            var total = world.Map.After(RegionAddress.Convert(frame, world.Frame, cfg.rootSize));
            t.localScale = Vector3.one * (float)total.Scale;
            t.position = total.Offset.ToVector3();
        }

        // ---- outlines ----------------------------------------------------------------------------------------------------

        void UpdateOutlines()
        {
            RegionAddress other = world.HasLock ? world.Lock.Target : null;

            removeScratch.Clear();
            foreach (var kv in outlines)
                if (kv.Key != world.Frame && kv.Key != other) removeScratch.Add(kv.Key);
            foreach (var k in removeScratch) { Destroy(outlines[k].mesh); Destroy(outlines[k].go); outlines.Remove(k); }

            float e = world.HasLock ? Mathf.Abs(world.Exponent) : 0f;
            Vector3 focusLogic = world.DisplayToLogic(cam.transform.position);

            // the "selected" mouth: nearest forward entrance of the current frame
            int selected = -1; float bestD = float.MaxValue;
            foreach (var en in world.Entrances)
            {
                if (en.Reverse) continue;
                float d = (en.MouthCenter - focusLogic).sqrMagnitude;
                if (d < bestD) { bestD = d; selected = en.Key; }
            }
            SelectedEntranceKey = selected;

            Touch(world.Frame, 1f - e, selected);
            if (other != null) Touch(other, e, -1);
        }

        void Touch(RegionAddress frame, float fade, int selectedKey)
        {
            if (!outlines.TryGetValue(frame, out var o))
            {
                o = new Outline { entrances = world.GetEntrances(frame) };
                OutlineMeshBuilder.Build(o.entrances, cfg.forwardOutline, cfg.reverseOutline, out var v, out var t, out o.colors, out var nrm, out var thick);
                o.mesh = new Mesh { name = "Outline " + frame };
                o.mesh.SetVertices(v);
                o.mesh.SetNormals(nrm);
                o.mesh.SetUVs(0, thick);
                o.mesh.SetColors(o.colors);
                o.mesh.SetTriangles(t, 0, true);
                o.go = new GameObject("Outline " + frame);
                o.go.transform.SetParent(transform, false);
                o.go.AddComponent<MeshFilter>().sharedMesh = o.mesh;
                var r = o.go.AddComponent<MeshRenderer>();
                r.sharedMaterial = glowMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                outlines[frame] = o;
            }
            Place(o.go.transform, frame);

            int signature = selectedKey * 131 + Mathf.RoundToInt(fade * 64f);
            if (signature == o.lastSignature) return;
            o.lastSignature = signature;
            for (int i = 0; i < o.entrances.Length; i++)
            {
                var en = o.entrances[i];
                float baseI = en.Reverse ? 0.5f : (en.Key == selectedKey ? 1f : 0.6f);
                OutlineMeshBuilder.SetIntensity(o.colors, i, baseI * fade);
            }
            o.mesh.SetColors(o.colors);
        }
    }
}
