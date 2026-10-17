using Menger.Core;
using UnityEngine;

namespace Menger.Rendering
{
    /// <summary>Square frame along each opening's boundary (four thin bars); the opening itself stays empty. 32 vertices per entrance.</summary>
    public static class OutlineMeshBuilder
    {
        public const int VerticesPerEntrance = 32;

        public static void Build(Entrance[] entrances, Color forward, Color reverse, out Vector3[] vertices, out int[] triangles, out Color32[] colors, out Vector3[] normals, out Vector2[] thickness)
        {
            vertices = new Vector3[entrances.Length * VerticesPerEntrance];
            colors = new Color32[vertices.Length];
            // normals = direction in which the shader may fatten a bar (across its thin sides); uv.x = the bar's real thickness
            normals = new Vector3[vertices.Length];
            thickness = new Vector2[vertices.Length];
            triangles = new int[entrances.Length * 4 * 36];
            int vi = 0, ti = 0;
            for (int i = 0; i < entrances.Length; i++)
            {
                var e = entrances[i];
                Color32 col = e.Reverse ? reverse : forward;
                Vector3 n = e.NormalF;
                Vector3 t1 = Vector3.zero, t2 = Vector3.zero;
                switch (e.Axis)
                {
                    case 0: t1 = Vector3.up; t2 = Vector3.forward; break;
                    case 1: t1 = Vector3.forward; t2 = Vector3.right; break;
                    default: t1 = Vector3.right; t2 = Vector3.up; break;
                }
                float w = e.MouthHalfWidth, t = w * 0.08f;
                Vector3 m = e.MouthCenter;
                for (int side = 0; side < 4; side++)
                {
                    float s = (side & 1) == 0 ? 1f : -1f;
                    bool alongT1 = side < 2;
                    Vector3 center = alongT1 ? m + t2 * (s * w) : m + t1 * (s * w);
                    Vector3 ex = alongT1 ? t1 * (w + t * 0.5f) : t1 * (t * 0.5f);
                    Vector3 ey = alongT1 ? t2 * (t * 0.5f) : t2 * (w - t * 0.5f);
                    Vector3 ez = n * (t * 0.5f);
                    int b = vi;
                    for (int c = 0; c < 8; c++)
                    {
                        float sx = (c & 1) == 0 ? -1 : 1, sy = (c & 2) == 0 ? -1 : 1, sz = (c & 4) == 0 ? -1 : 1;
                        vertices[vi] = center + ex * sx + ey * sy + ez * sz;
                        Vector3 thin = alongT1 ? ey.normalized * sy + ez.normalized * sz : ex.normalized * sx + ez.normalized * sz;
                        normals[vi] = thin.normalized;
                        thickness[vi] = new Vector2(t, 0f);
                        colors[vi++] = col;
                    }
                    // winding does not matter: the glow shader is Cull Off
                    int[] quads = { 0, 1, 3, 2, 4, 6, 7, 5, 0, 4, 5, 1, 2, 3, 7, 6, 0, 2, 6, 4, 1, 5, 7, 3 };
                    for (int q = 0; q < 6; q++)
                    {
                        int a = b + quads[q * 4], bb = b + quads[q * 4 + 1], c2 = b + quads[q * 4 + 2], d = b + quads[q * 4 + 3];
                        triangles[ti++] = a; triangles[ti++] = bb; triangles[ti++] = c2;
                        triangles[ti++] = a; triangles[ti++] = c2; triangles[ti++] = d;
                    }
                }
            }
        }

        public static void SetIntensity(Color32[] colors, int entranceIndex, float intensity)
        {
            byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(intensity * 255f), 0, 255);
            int start = entranceIndex * VerticesPerEntrance;
            for (int i = 0; i < VerticesPerEntrance; i++) { var c = colors[start + i]; c.a = a; colors[start + i] = c; }
        }
    }
}
