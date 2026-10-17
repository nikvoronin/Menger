using UnityEngine;

namespace Menger.Rendering
{
    /// <summary>
    /// The sponge's detail texture, painted in code: R = albedo variation, G = roughness variation (both with mean 0.5), tileable.
    /// It is sampled in the surface shader by the frame's own logic coordinates, with two octaves (x3 apart) cross-faded by the
    /// zoom phase, so a frame flip (logic coordinates x3) re-uses exactly the same texels and the pattern never pops.
    /// </summary>
    public static class SurfaceDetail
    {
        public static Texture2D Build(int size, int seed, float contrast)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true, true) { name = "SurfaceDetail", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            tex.SetPixels32(Pixels(size, seed, contrast));
            tex.Apply(true);
            return tex;
        }

        public static Color32[] Pixels(int size, int seed, float contrast)
        {
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    float albedo = Level(Fbm(u, v, seed), contrast);
                    float rough = Level(Fbm(u, v, seed + 101), contrast);
                    px[y * size + x] = new Color32(To8(albedo), To8(rough), 128, 255);
                }
            return px;
        }

        /// <summary>The two octaves (scale factors on the texture coordinates) and the blend weight of the second one for a zoom phase.</summary>
        public static void Octaves(float phase, out float scaleA, out float scaleB, out float t)
        {
            float k = Mathf.Floor(phase);
            t = phase - k;
            scaleA = Mathf.Pow(3f, k);
            scaleB = scaleA * 3f;
        }

        static float Level(float n, float contrast) => Mathf.Clamp01(0.5f + (n - 0.5f) * contrast);
        static byte To8(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        // Periodic fractal value noise: lattice coordinates wrap at the octave's period, so the texture tiles seamlessly.
        static float Fbm(float u, float v, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int period = 4;
            for (int o = 0; o < 5; o++, period *= 2, amp *= 0.55f) { sum += amp * Noise(u * period, v * period, period, seed + o * 17); norm += amp; }
            return sum / norm;
        }

        static float Noise(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int x0 = Wrap(xi, period), x1 = Wrap(xi + 1, period), y0 = Wrap(yi, period), y1 = Wrap(yi + 1, period);
            return Mathf.Lerp(Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx), Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx), fy);
        }

        static int Wrap(int i, int period) => ((i % period) + period) % period;

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }
    }
}
