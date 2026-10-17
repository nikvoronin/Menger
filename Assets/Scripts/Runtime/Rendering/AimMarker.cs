using Menger.Flight;
using UnityEngine;

namespace Menger.Rendering
{
    /// <summary>A ring lying on the surface point the player is looking at (the point a click takes them to). Reuses the additive glow material.</summary>
    public sealed class AimMarker : MonoBehaviour
    {
        WalkerController player;
        Transform eye;
        MeshRenderer rend;

        public void Init(WalkerController p, Transform camera, Material glow)
        {
            player = p; eye = camera;
            var mf = gameObject.AddComponent<MeshFilter>();
            rend = gameObject.AddComponent<MeshRenderer>();
            rend.sharedMaterial = glow;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mf.sharedMesh = MakeRing(32, 0.65f, 1f, new Color(1f, 0.85f, 0.2f, 1f));
        }

        void LateUpdate()
        {
            bool show = player != null && player.HasAim;
            rend.enabled = show;
            if (!show) return;
            float dist = (player.AimPoint - eye.position).magnitude;
            float radius = Mathf.Clamp(dist * 0.02f, 1.2f, 60f); // roughly constant on screen, never smaller than the player's footprint
            Vector3 n = player.AimNormal;
            Vector3 helper = Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up;
            transform.SetPositionAndRotation(player.AimPoint + n * (radius * 0.1f + 0.03f), Quaternion.LookRotation(n, helper));
            transform.localScale = Vector3.one * radius;
        }

        // ring in the local XY plane, facing +Z; thickness 1000 keeps the glow shader from fattening it
        static Mesh MakeRing(int segments, float inner, float outer, Color color)
        {
            var v = new Vector3[segments * 2]; var tri = new int[segments * 6];
            var cols = new Color[v.Length]; var nrm = new Vector3[v.Length]; var uv = new Vector2[v.Length];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v[i * 2] = dir * inner; v[i * 2 + 1] = dir * outer;
                cols[i * 2] = cols[i * 2 + 1] = color; nrm[i * 2] = nrm[i * 2 + 1] = Vector3.forward; uv[i * 2] = uv[i * 2 + 1] = new Vector2(1000f, 0f);
                int j = (i + 1) % segments, k = i * 6;
                tri[k] = i * 2; tri[k + 1] = i * 2 + 1; tri[k + 2] = j * 2;
                tri[k + 3] = i * 2 + 1; tri[k + 4] = j * 2 + 1; tri[k + 5] = j * 2;
            }
            var m = new Mesh { name = "AimRing", vertices = v, triangles = tri, colors = cols, normals = nrm, uv = uv };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return m;
        }
    }
}
