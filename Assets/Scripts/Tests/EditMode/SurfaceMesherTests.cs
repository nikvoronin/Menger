using System.Collections.Generic;
using System.Linq;
using Menger.Core;
using Menger.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace Menger.Tests
{
    /// <summary>The Burst chunk mesher against the managed reference mesher (<see cref="ChunkMeshBuilder"/>), which stays as the oracle.</summary>
    public class SurfaceMesherTests
    {
        struct Q { public int D, Level; public Vector3 Min, Max; }

        static readonly ContextRoot Root = new ContextRoot(Vector3.zero, 900f);

        static List<Q> QuadsOf(MeshBuild b)
        {
            var list = new List<Q>();
            for (int i = 0; i < b.Count; i++)
            {
                var v = b.VerticesOf(i);
                for (int k = 0; k < v.Length; k += 4) list.Add(Quad(new[] { P(v[k]), P(v[k + 1]), P(v[k + 2]), P(v[k + 3]) }, v[k].Color.g, v[k].Color.r / 16));
            }
            return list;
        }

        static List<Q> QuadsOf(SurfaceMeshData d)
        {
            var list = new List<Q>();
            for (int k = 0; k < d.Vertices.Count; k += 4)
            {
                var n = d.Normals[k];
                int dir = 0;
                for (int i = 0; i < 6; i++) if (new Vector3(MengerCells.Directions[i].x, MengerCells.Directions[i].y, MengerCells.Directions[i].z) == n) dir = i;
                list.Add(Quad(new[] { d.Vertices[k], d.Vertices[k + 1], d.Vertices[k + 2], d.Vertices[k + 3] }, dir, d.Colors[k].r / 16));
            }
            return list;
        }

        static Vector3 P(SurfaceVertex v) => new Vector3(v.X, v.Y, v.Z);

        static Q Quad(Vector3[] p, int d, int level)
        {
            var min = p[0]; var max = p[0];
            foreach (var x in p) { min = Vector3.Min(min, x); max = Vector3.Max(max, x); }
            return new Q { D = d, Level = level, Min = min, Max = max };
        }

        static float Plane(in Q q) => q.D >> 1 == 0 ? q.Min.x : q.D >> 1 == 1 ? q.Min.y : q.Min.z;
        static float Area(in Q q) { var s = q.Max - q.Min; return q.D >> 1 == 0 ? s.y * s.z : q.D >> 1 == 1 ? s.x * s.z : s.x * s.y; }

        static MeshBuild Build(Vector3 cam, LodParams lod, bool greedy, float chunk = 100f)
        {
            var b = MeshBuild.Schedule(Root, cam, lod, greedy, chunk);
            b.Complete();
            return b;
        }

        static Dictionary<(int, int, int), float> AreaByGroup(List<Q> quads)
        {
            var d = new Dictionary<(int, int, int), float>();
            foreach (var q in quads)
            {
                var key = (q.D, Mathf.RoundToInt(Plane(q) * 10f), q.Level);
                d[key] = (d.TryGetValue(key, out var a) ? a : 0f) + Area(q);
            }
            return d;
        }

        // the unit squares a quad covers, on a lattice of the given unit (for the coarse test configuration)
        static HashSet<(int, int, int, int, int)> Cells(List<Q> quads, float unit)
        {
            var set = new HashSet<(int, int, int, int, int)>();
            foreach (var q in quads)
            {
                int axis = q.D >> 1;
                int a1 = (axis + ((q.D & 1) == 0 ? 1 : 2)) % 3, a2 = (axis + ((q.D & 1) == 0 ? 2 : 1)) % 3;
                int plane = Mathf.RoundToInt((Plane(q) + 450f) / unit);
                int u0 = Mathf.RoundToInt((q.Min[a1] + 450f) / unit), u1 = Mathf.RoundToInt((q.Max[a1] + 450f) / unit);
                int v0 = Mathf.RoundToInt((q.Min[a2] + 450f) / unit), v1 = Mathf.RoundToInt((q.Max[a2] + 450f) / unit);
                for (int u = u0; u < u1; u++) for (int v = v0; v < v1; v++) Assert.IsTrue(set.Add((q.D, q.Level, plane, u, v)), "a cell covered twice");
            }
            return set;
        }

        static readonly LodParams Coarse = new LodParams { Ratio = 0f, MinLeaf = 100f, MaxExpandDistance = 1e9f };
        static readonly LodParams Mixed = new LodParams { Ratio = 0.25f, MinLeaf = 3.5f, MaxExpandDistance = 5000f };

        [Test]
        public void WithoutMerging_EveryFaceOfTheReferenceMesherIsThere()
        {
            var reference = ChunkMeshBuilder.Build(Root, Vector3.zero, Coarse);
            using var b = Build(Vector3.zero, Coarse, false);
            var quads = QuadsOf(b);
            Assert.AreEqual(reference.Faces, quads.Count);
            float unit = 900f / 27f;
            var refCells = Cells(QuadsOf(reference), unit); var newCells = Cells(quads, unit);
            Assert.IsTrue(refCells.SetEquals(newCells), "same faces, same levels");
            int leaves = 0, faces = 0;
            for (int i = 0; i < b.Count; i++) { leaves += b.Out(i).Leaves; faces += b.Out(i).Faces; }
            Assert.AreEqual(reference.Leaves, leaves);
            Assert.AreEqual(reference.Faces, faces);
        }

        [Test]
        public void GreedyMerging_CoversExactlyTheSameCells_WithFewerQuads()
        {
            var reference = ChunkMeshBuilder.Build(Root, Vector3.zero, Coarse);
            using var b = Build(Vector3.zero, Coarse, true, 900f); // one chunk: neighbouring cubes are merged (chunks of one cube each would have nothing to merge)
            var quads = QuadsOf(b);
            float unit = 900f / 27f;
            Assert.IsTrue(Cells(QuadsOf(reference), unit).SetEquals(Cells(quads, unit)), "a merged rectangle covers exactly the cells of its faces, once");
            Assert.Less(quads.Count, reference.Faces);
            Debug.Log($"greedy merge, uniform level 2: {reference.Faces} faces -> {quads.Count} quads");
        }

        [Test]
        public void MixedLod_GreedyAndPlain_HaveTheReferenceSurfaceArea_AndNoOverlap()
        {
            var cam = new Vector3(0, 0, -440);
            var reference = QuadsOf(ChunkMeshBuilder.Build(Root, cam, Mixed));
            using var plain = Build(cam, Mixed, false);
            using var greedy = Build(cam, Mixed, true);
            var refArea = AreaByGroup(reference);
            foreach (var mesh in new[] { QuadsOf(plain), QuadsOf(greedy) })
            {
                var area = AreaByGroup(mesh);
                Assert.AreEqual(refArea.Count, area.Count, "same (direction, plane, level) groups");
                foreach (var kv in refArea) Assert.AreEqual(kv.Value, area[kv.Key], kv.Value * 1e-4f + 1e-2f, $"area of {kv.Key}");
            }
            Assert.AreEqual(reference.Count, QuadsOf(plain).Count);
            var merged = QuadsOf(greedy);
            Assert.Less(merged.Count, reference.Count);
            // no two merged quads overlap on the same plane
            foreach (var g in merged.GroupBy(q => (q.D, Mathf.RoundToInt(Plane(q) * 10f))))
            {
                var l = g.ToList();
                for (int i = 0; i < l.Count; i++)
                    for (int j = i + 1; j < l.Count; j++)
                    {
                        int axis = l[i].D >> 1; int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                        bool overlap = l[i].Min[a1] < l[j].Max[a1] - 1e-3f && l[j].Min[a1] < l[i].Max[a1] - 1e-3f && l[i].Min[a2] < l[j].Max[a2] - 1e-3f && l[j].Min[a2] < l[i].Max[a2] - 1e-3f;
                        Assert.IsFalse(overlap, $"quads overlap on plane {g.Key}");
                    }
            }
            Debug.Log($"mixed LOD at the spawn: reference {reference.Count} faces -> greedy {merged.Count} quads ({100f * merged.Count / reference.Count:0}%)");
        }

        [Test]
        public void Chunks_StayInsideTheirCells_AreDeterministic_AndCountedOnce()
        {
            var cam = new Vector3(10, 20, -300);
            using var a = Build(cam, Mixed, true);
            using var b = Build(cam, Mixed, true);
            Assert.AreEqual(a.Count, b.Count);
            Assert.Greater(a.Count, 10, "the context is cut into many chunks");
            var seen = new HashSet<(long, long, long, long)>();
            for (int i = 0; i < a.Count; i++)
            {
                var r = a.Root(i);
                Assert.IsTrue(seen.Add((r.X, r.Y, r.Z, r.Size)), "each chunk cell once");
                Assert.AreEqual(a.Out(i).Hash, b.Out(i).Hash, "same input, same chunk geometry");
                ref var o = ref a.Out(i);
                foreach (var v in a.VerticesOf(i))
                {
                    Assert.GreaterOrEqual(v.X, o.MinX - 1e-3f); Assert.LessOrEqual(v.X, o.MaxX + 1e-3f);
                    Assert.GreaterOrEqual(v.Y, o.MinY - 1e-3f); Assert.LessOrEqual(v.Y, o.MaxY + 1e-3f);
                    Assert.GreaterOrEqual(v.Z, o.MinZ - 1e-3f); Assert.LessOrEqual(v.Z, o.MaxZ + 1e-3f);
                }
                var idx = a.IndicesOf(i);
                Assert.AreEqual(a.VerticesOf(i).Length / 4 * 6, idx.Length);
            }
            // a camera move of a few units changes few chunks: the rest are not uploaded again
            using var moved = Build(cam + new Vector3(4, 0, 0), Mixed, true);
            var before = new Dictionary<(long, long, long, long), uint>();
            for (int i = 0; i < a.Count; i++) { var r = a.Root(i); before[(r.X, r.Y, r.Z, r.Size)] = a.Out(i).Hash; }
            int same = 0;
            for (int i = 0; i < moved.Count; i++) { var r = moved.Root(i); if (before.TryGetValue((r.X, r.Y, r.Z, r.Size), out var h) && h == moved.Out(i).Hash) same++; }
            Debug.Log($"after a 4 unit move: {same} of {moved.Count} chunks identical");
            Assert.Greater(same, moved.Count / 2);
        }

        [Test]
        public void Timing_BurstChunksVersusTheManagedReference()
        {
            var cam = new Vector3(0, 0, -440);
            ChunkMeshBuilder.Build(Root, cam, Mixed); using (Build(cam, Mixed, true)) { } // warm up (JIT, Burst)
            var t = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 5; i++) ChunkMeshBuilder.Build(Root, cam, Mixed);
            double managed = t.Elapsed.TotalMilliseconds / 5;
            t.Restart();
            for (int i = 0; i < 5; i++) { using var b = Build(cam, Mixed, true); }
            double burst = t.Elapsed.TotalMilliseconds / 5;
            Debug.Log($"mesh build, spawn view: managed reference {managed:0.0} ms, Burst chunks + greedy {burst:0.0} ms");
            Assert.Less(burst, managed, "the Burst mesher must be faster than the managed one");
        }
    }
}
