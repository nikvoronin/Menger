using Menger.Core;
using Menger.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace Menger.Tests
{
    public class SurfaceDetailTests
    {
        [Test]
        public void Texture_IsTileable_Deterministic_AndCentredOnHalf()
        {
            const int n = 128;
            var a = SurfaceDetail.Pixels(n, 7, 1f);
            var b = SurfaceDetail.Pixels(n, 7, 1f);
            Assert.AreEqual(a, b, "same seed, same texture");
            Assert.AreNotEqual(a, SurfaceDetail.Pixels(n, 8, 1f), "another seed, another texture");

            double sumR = 0, sumG = 0; int worstSeam = 0, worstStep = 0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = a[y * n + x];
                    sumR += p.r; sumG += p.g;
                    // the seam (last column next to the first, last row next to the first) is no rougher than the interior
                    var right = a[y * n + (x + 1) % n]; var up = a[((y + 1) % n) * n + x];
                    int step = Mathf.Max(Mathf.Abs(p.r - right.r), Mathf.Abs(p.r - up.r));
                    if (x == n - 1 || y == n - 1) worstSeam = Mathf.Max(worstSeam, step); else worstStep = Mathf.Max(worstStep, step);
                }
            Assert.LessOrEqual(worstSeam, worstStep + 2, "no visible seam when tiled");
            Assert.AreEqual(128.0, sumR / (n * n), 12.0, "mean albedo variation is neutral");
            Assert.AreEqual(128.0, sumG / (n * n), 12.0, "mean roughness variation is neutral");
            Assert.Greater(worstStep, 0, "not flat");
        }

        [Test]
        public void Contrast_ScalesTheVariation()
        {
            Color32[] soft = SurfaceDetail.Pixels(64, 7, 0.4f), hard = SurfaceDetail.Pixels(64, 7, 1.6f);
            int Range(Color32[] px) { int lo = 255, hi = 0; foreach (var p in px) { lo = Mathf.Min(lo, p.r); hi = Mathf.Max(hi, p.r); } return hi - lo; }
            Assert.Greater(Range(hard), Range(soft) * 2);
        }

        [Test]
        public void ZoomPhase_PicksTheSameTexelsBeforeAndAfterAFrameFlip()
        {
            // a point on a face, seen by the parent mesh at phase 1 (the end of the descent) and by the child mesh at phase 0
            var parent = RegionAddress.Root;
            var child = RegionAddress.Root.Child(4);
            var conv = RegionAddress.Convert(parent, child, 900.0);
            float tile = 900f / 27f;
            var rng = new System.Random(3);
            for (int i = 0; i < 200; i++)
            {
                var p = new Vector3((float)(rng.NextDouble() * 300 - 150), (float)(rng.NextDouble() * 300 - 150), (float)(rng.NextDouble() * 300 - 150));
                var q = conv.Apply(new V3d(p)).ToVector3();
                SurfaceDetail.Octaves(1f, out var a1, out var b1, out var t1);
                SurfaceDetail.Octaves(0f, out var a0, out var b0, out var t0);
                Assert.AreEqual(0f, t1, 1e-6f); Assert.AreEqual(0f, t0, 1e-6f);
                foreach (var (up, uq) in new[] { (p.z, q.z), (p.y, q.y), (p.x, q.x) })
                {
                    float parentCoord = Mathf.Repeat(up / tile * a1, 1f), childCoord = Mathf.Repeat(uq / tile * a0, 1f);
                    float diff = Mathf.Abs(parentCoord - childCoord); diff = Mathf.Min(diff, 1f - diff);
                    Assert.Less(diff, 2e-3f, $"point {p}");
                }
            }
        }

        [Test]
        public void ZoomPhase_BlendsLinearlyBetweenOctavesThatAreThreeApart()
        {
            SurfaceDetail.Octaves(0.25f, out var a, out var b, out var t);
            Assert.AreEqual(1f, a); Assert.AreEqual(3f, b); Assert.AreEqual(0.25f, t, 1e-6f);
            SurfaceDetail.Octaves(1.5f, out a, out b, out t);
            Assert.AreEqual(3f, a, 1e-5f); Assert.AreEqual(9f, b, 1e-5f); Assert.AreEqual(0.5f, t, 1e-6f);
            SurfaceDetail.Octaves(-0.5f, out a, out b, out t);
            Assert.AreEqual(1f / 3f, a, 1e-6f); Assert.AreEqual(1f, b, 1e-6f); Assert.AreEqual(0.5f, t, 1e-6f);
        }

        [Test]
        public void Settings_Defaults_AreGlossyWhiteWithoutTexture()
        {
            var cfg = ScriptableObject.CreateInstance<MengerSettings>();
            Assert.AreEqual(0f, cfg.surfaceSaturation, "white, not tinted"); Assert.Greater(cfg.surfaceBrightness, 0.85f, "bright");
            Assert.AreEqual(0f, cfg.detailStrength); Assert.AreEqual(0f, cfg.roughnessVariation, "no texture");
            Assert.Less(cfg.roughness, 0.15f, "glossy");
            Assert.Greater(cfg.specularStrength, 0.5f, "a highlight");
            float tile = cfg.rootSize / cfg.detailTileDivisions;
            Assert.AreEqual(cfg.detailTileDivisions, cfg.rootSize / tile, 1e-3f, "a whole number of tiles per root cube: the pattern lattices of parent and child frames line up");
        }
    }
}
