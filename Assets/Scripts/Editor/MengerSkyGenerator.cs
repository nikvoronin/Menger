using System.IO;
using UnityEditor;
using UnityEngine;

namespace Menger.EditorTools
{
    /// <summary>
    /// Paints the equirectangular sky image: a star field, a dusty Milky Way band, faint nebulae and a gallery of galaxies
    /// (large spirals seen at different inclinations, an elliptical, many small smudges). Deterministic; no external assets.
    /// </summary>
    public static class MengerSkyGenerator
    {
        public const string Path = "Assets/Menger/GalaxySky.png";
        const int W = 4096, H = 2048;

        [MenuItem("Menger/Generate Galaxy Sky")]
        public static void GenerateMenu() => Generate(true);

        public static Texture2D Generate(bool force)
        {
            if (!force && File.Exists(Path)) return AssetDatabase.LoadAssetAtPath<Texture2D>(Path);
            Directory.CreateDirectory("Assets/Menger");
            var img = new float[W * H * 3];
            var rng = new System.Random(20260101);

            // directions of the pixel grid
            var sinLon = new float[W]; var cosLon = new float[W];
            for (int x = 0; x < W; x++) { float lon = (x + 0.5f) / W * Mathf.PI * 2f; sinLon[x] = Mathf.Sin(lon); cosLon[x] = Mathf.Cos(lon); }
            Vector3 Dir(int x, int y)
            {
                float lat = (0.5f - (y + 0.5f) / H) * Mathf.PI;
                float c = Mathf.Cos(lat);
                return new Vector3(c * sinLon[x], Mathf.Sin(lat), c * cosLon[x]);
            }

            // ---- Milky Way band and nebulae --------------------------------------------------------------------------------
            Vector3 bandAxis = new Vector3(0.35f, 0.88f, 0.31f).normalized;
            Vector3 bandRef = Vector3.Cross(bandAxis, Vector3.right).normalized;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Vector3 d = Dir(x, y);
                    float lat = Vector3.Dot(d, bandAxis);                       // sine of the galactic latitude
                    float along = Mathf.Atan2(Vector3.Dot(d, Vector3.Cross(bandAxis, bandRef)), Vector3.Dot(d, bandRef));
                    float core = Mathf.Exp(-Mathf.Pow(along / 1.6f, 2f));         // brighter towards the galactic centre
                    float width = 0.11f + 0.05f * core;
                    float cloud = FBm(d * 3.2f, 5);
                    float band = Mathf.Exp(-(lat * lat) / (width * width)) * (0.05f + 1.1f * Mathf.Pow(cloud, 1.6f)) * (0.3f + 0.7f * core);
                    float dust = Smooth(0.52f, 0.72f, FBm(d * 7f + new Vector3(9.1f, 3.3f, 5.7f), 5)) * Mathf.Exp(-(lat * lat) / (0.05f * 0.05f)); // dark lanes along the plane
                    band *= 1f - 0.85f * dust;
                    float neb = Mathf.Pow(Mathf.Clamp01(FBm(d * 2.1f + new Vector3(2f, 7f, 1f), 4) - 0.52f) * 3f, 2f) * 0.35f;
                    float neb2 = Mathf.Pow(Mathf.Clamp01(FBm(d * 2.6f + new Vector3(8f, 1f, 4f), 4) - 0.55f) * 3f, 2f) * 0.3f;
                    int i = (y * W + x) * 3;
                    img[i] = 0.0005f + band * 0.30f + neb * 0.16f + neb2 * 0.04f;
                    img[i + 1] = 0.0006f + band * 0.25f + neb * 0.03f + neb2 * 0.06f;
                    img[i + 2] = 0.0012f + band * 0.21f + neb * 0.06f + neb2 * 0.16f;
                }

            // ---- stars ------------------------------------------------------------------------------------------------------
            for (int s = 0; s < 14000; s++)
            {
                Vector3 d = RandomDir(rng);
                // many more stars near the galactic plane
                if (rng.NextDouble() > 0.35 + 0.65 * Mathf.Exp(-Mathf.Pow(Vector3.Dot(d, bandAxis) / 0.2f, 2f))) continue;
                float m = Mathf.Pow((float)rng.NextDouble(), 7f);                    // few bright, many faint
                SplatStar(img, d, 0.05f + 4.5f * m, 0.55f + 1.1f * m, StarColor(rng));
            }
            for (int s = 0; s < 70; s++) SplatStar(img, RandomDir(rng), 5f + 6f * (float)rng.NextDouble(), 1.6f + (float)rng.NextDouble(), StarColor(rng));

            // ---- galaxies ---------------------------------------------------------------------------------------------------
            // radius (rad), inclination (deg, 0 = face on), kind: 0 spiral, 1 elliptical
            var gallery = new (float radius, float incl, int kind)[]
            {
                (0.150f, 52f, 0), (0.115f, 8f, 0), (0.100f, 78f, 0), (0.085f, 35f, 1), (0.070f, 62f, 0),
                (0.060f, 20f, 0), (0.050f, 70f, 0), (0.045f, 0f, 0), (0.040f, 48f, 1), (0.035f, 84f, 0),
            };
            var used = new System.Collections.Generic.List<Vector3>();
            foreach (var g in gallery)
            {
                Vector3 c = RandomDir(rng);
                for (int tries = 0; tries < 60; tries++)
                {
                    c = RandomDir(rng);
                    bool ok = Mathf.Abs(c.y) < 0.8f && Mathf.Abs(Vector3.Dot(c, bandAxis)) > 0.22f; // clear of the poles and of the Milky Way
                    foreach (var u in used) if (Vector3.Dot(u, c) > 0.97f) ok = false;
                    if (ok) break;
                }
                used.Add(c);
                PaintGalaxy(img, c, g.radius, g.incl, (float)rng.NextDouble() * 360f, g.kind, rng);
            }
            for (int s = 0; s < 120; s++)
            {
                Vector3 c = RandomDir(rng);
                if (Mathf.Abs(c.y) > 0.85f) continue;
                PaintGalaxy(img, c, 0.004f + 0.014f * Mathf.Pow((float)rng.NextDouble(), 2f), (float)rng.NextDouble() * 85f, (float)rng.NextDouble() * 360f, rng.NextDouble() < 0.35 ? 1 : 0, rng);
            }

            // ---- tone map and write -----------------------------------------------------------------------------------------
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = (y * W + x) * 3;
                    px[(H - 1 - y) * W + x] = new Color32(Encode(img[i]), Encode(img[i + 1]), Encode(img[i + 2]), 255); // texture row 0 is the bottom
                }
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false, false);
            tex.SetPixels32(px);
            File.WriteAllBytes(Path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(Path);
            imp.sRGBTexture = true;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.maxTextureSize = 4096;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            Debug.Log("Menger: galaxy sky written to " + Path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(Path);
        }

        // ---- painting -------------------------------------------------------------------------------------------------------

        static void PaintGalaxy(float[] img, Vector3 c, float radius, float inclDeg, float posAngleDeg, int kind, System.Random rng)
        {
            Vector3 e1 = Vector3.Cross(c, Vector3.up).normalized, e2 = Vector3.Cross(c, e1);
            float pa = posAngleDeg * Mathf.Deg2Rad, cp = Mathf.Cos(pa), sp = Mathf.Sin(pa);
            float stretch = 1f / Mathf.Max(0.12f, Mathf.Cos(inclDeg * Mathf.Deg2Rad));
            float seed = (float)rng.NextDouble() * 50f;
            float handed = rng.NextDouble() < 0.5 ? 1f : -1f;
            float armTurns = 1.6f + (float)rng.NextDouble() * 1.4f;
            float window = radius * 1.7f;
            float lat0 = Mathf.Asin(c.y), lon0 = Mathf.Atan2(c.x, c.z);
            int y0 = Mathf.Max(0, Mathf.FloorToInt((0.5f - (lat0 + window) / Mathf.PI) * H)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt((0.5f - (lat0 - window) / Mathf.PI) * H));
            float lonSpan = window / Mathf.Max(0.2f, Mathf.Cos(lat0));
            int xc = Mathf.RoundToInt((lon0 / (Mathf.PI * 2f) + (lon0 < 0 ? 1f : 0f)) * W);
            int xs = Mathf.CeilToInt(lonSpan / (Mathf.PI * 2f) * W);
            float cosWindow = Mathf.Cos(window);
            for (int y = y0; y <= y1; y++)
                for (int xx = xc - xs; xx <= xc + xs; xx++)
                {
                    int x = ((xx % W) + W) % W;
                    float lat = (0.5f - (y + 0.5f) / H) * Mathf.PI, lon = (x + 0.5f) / W * Mathf.PI * 2f;
                    float cl = Mathf.Cos(lat);
                    var d = new Vector3(cl * Mathf.Sin(lon), Mathf.Sin(lat), cl * Mathf.Cos(lon));
                    float dc = Vector3.Dot(d, c);
                    if (dc < cosWindow) continue;
                    float gx = Vector3.Dot(d, e1) / dc, gy = Vector3.Dot(d, e2) / dc;      // gnomonic tangent-plane coordinates (radians)
                    float X = (gx * cp + gy * sp) / radius, Y = (-gx * sp + gy * cp) / radius * stretch; // galaxy plane, in units of the radius
                    float r = Mathf.Sqrt(X * X + Y * Y);
                    if (r > 1.7f) continue;
                    float theta = Mathf.Atan2(Y, X);
                    float edge = Mathf.Exp(-Mathf.Pow(r / 1.05f, 4f));                      // soft outer cut-off
                    Vector3 col; float v;
                    if (kind == 1)
                    {
                        v = Mathf.Exp(-r * 4.2f) * 2.2f * edge;
                        col = new Vector3(1f, 0.83f, 0.6f) * v;
                    }
                    else
                    {
                        float phase = theta * handed - armTurns * Mathf.Log(r + 0.08f) * 2f;
                        float arms = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(2f * phase), 1.6f);
                        float clump = FBm(new Vector3(X * 5f + seed, Y * 5f * 1f + seed, seed * 0.3f), 3);
                        float disk = Mathf.Exp(-r * 2.6f) * (0.25f + 1.0f * arms) * (0.55f + 0.9f * clump);
                        float bulge = Mathf.Exp(-Mathf.Pow(r / 0.14f, 2f)) * 2.4f;
                        float hii = Mathf.Pow(Mathf.Clamp01(clump - 0.58f) * 5f, 2f) * arms * Mathf.Exp(-r * 1.5f);
                        Vector3 armCol = new Vector3(0.62f, 0.76f, 1f), coreCol = new Vector3(1f, 0.82f, 0.58f), pink = new Vector3(1f, 0.45f, 0.62f);
                        // a dark dust lane on the near side of inclined disks
                        float lane = 1f - 0.6f * Mathf.Exp(-Mathf.Pow(Y / (0.09f * stretch), 2f)) * Mathf.Clamp01(inclDeg / 60f) * (r < 1f ? 1f : 0f);
                        col = (armCol * disk * 0.95f + coreCol * bulge + pink * hii * 0.9f) * edge * lane;
                    }
                    int i = (y * W + x) * 3;
                    img[i] += col.x; img[i + 1] += col.y; img[i + 2] += col.z;
                }
        }

        static void SplatStar(float[] img, Vector3 d, float brightness, float sigma, Vector3 color)
        {
            float lat = Mathf.Asin(d.y), lon = Mathf.Atan2(d.x, d.z); if (lon < 0) lon += Mathf.PI * 2f;
            float fy = (0.5f - lat / Mathf.PI) * H - 0.5f, fx = lon / (Mathf.PI * 2f) * W - 0.5f;
            float sx = sigma / Mathf.Max(0.12f, Mathf.Cos(lat)), sy = sigma;               // equirectangular stretch near the poles
            int rx = Mathf.CeilToInt(sx * 3f), ry = Mathf.CeilToInt(sy * 3f);
            for (int y = Mathf.Max(0, Mathf.FloorToInt(fy) - ry); y <= Mathf.Min(H - 1, Mathf.CeilToInt(fy) + ry); y++)
                for (int xx = Mathf.FloorToInt(fx) - rx; xx <= Mathf.CeilToInt(fx) + rx; xx++)
                {
                    float dx = (xx - fx) / sx, dy = (y - fy) / sy;
                    float w = Mathf.Exp(-(dx * dx + dy * dy) * 0.5f) / (sx * sy) * sigma * sigma;
                    int x = ((xx % W) + W) % W;
                    int i = (y * W + x) * 3;
                    img[i] += color.x * brightness * w; img[i + 1] += color.y * brightness * w; img[i + 2] += color.z * brightness * w;
                }
        }

        static Vector3 StarColor(System.Random rng)
        {
            double t = rng.NextDouble();
            if (t < 0.25) return new Vector3(0.65f, 0.78f, 1f);      // hot, blue-white
            if (t < 0.6) return new Vector3(1f, 0.97f, 0.92f);        // white
            if (t < 0.85) return new Vector3(1f, 0.9f, 0.68f);        // yellow
            return new Vector3(1f, 0.68f, 0.45f);                     // orange
        }

        static Vector3 RandomDir(System.Random rng)
        {
            float z = (float)(rng.NextDouble() * 2 - 1), a = (float)(rng.NextDouble() * Mathf.PI * 2);
            float r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
        }

        static byte Encode(float linear)
        {
            float t = 1f - Mathf.Exp(-linear * 1.35f);                                       // soft highlight roll-off
            return (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Pow(t, 1f / 2.2f) * 255f), 0, 255);
        }

        // ---- noise ----------------------------------------------------------------------------------------------------------

        static float Smooth(float a, float b, float x) { x = Mathf.Clamp01((x - a) / (b - a)); return x * x * (3f - 2f * x); }

        static float FBm(Vector3 p, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++) { sum += amp * Noise(p); norm += amp; p *= 2.03f; amp *= 0.5f; }
            return sum / norm;
        }

        static float Noise(Vector3 p)
        {
            int xi = Mathf.FloorToInt(p.x), yi = Mathf.FloorToInt(p.y), zi = Mathf.FloorToInt(p.z);
            float fx = p.x - xi, fy = p.y - yi, fz = p.z - zi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            float x00 = Mathf.Lerp(Hash(xi, yi, zi), Hash(xi + 1, yi, zi), fx), x10 = Mathf.Lerp(Hash(xi, yi + 1, zi), Hash(xi + 1, yi + 1, zi), fx);
            float x01 = Mathf.Lerp(Hash(xi, yi, zi + 1), Hash(xi + 1, yi, zi + 1), fx), x11 = Mathf.Lerp(Hash(xi, yi + 1, zi + 1), Hash(xi + 1, yi + 1, zi + 1), fx);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
        }

        static float Hash(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }
    }
}
