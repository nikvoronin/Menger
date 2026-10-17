using System.Collections.Generic;
using System.Linq;
using Menger.Core;
using NUnit.Framework;
using UnityEngine;

namespace Menger.Tests
{
    public class GeometryTests
    {
        static MengerSettings Cfg()
        {
            var c = ScriptableObject.CreateInstance<MengerSettings>();
            c.approachStartCells = 0.567f; // the long approach zone these tests were written for; shipped default is shorter
            c.rootSize = 900f; // these tests use coordinates of the original 900 cube; DefaultScale tests cover the shipped size
            return c;
        }
        static ContextRoot RootCtx(float size = 900f) => new ContextRoot(Vector3.zero, size);

        [Test]
        public void EachSubdivisionKeepsTwentyCells_RemovingCentreAndFaceCentres()
        {
            Assert.AreEqual(20, MengerCells.Offsets.Length);
            int removed = 0;
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                bool isCentre = x == 0 && y == 0 && z == 0;
                bool isFaceCentre = Mathf.Abs(x) + Mathf.Abs(y) + Mathf.Abs(z) == 1;
                if (!MengerCells.IsRetained(x, y, z)) { removed++; Assert.IsTrue(isCentre || isFaceCentre); }
                else Assert.IsFalse(isCentre || isFaceCentre);
            }
            Assert.AreEqual(7, removed);
            for (int i = 0; i < 20; i++) Assert.AreEqual(i, MengerCells.IndexOf(MengerCells.Offsets[i]));
        }

        [Test]
        public void ChildEnlargedByThree_ReproducesTheRootMotif()
        {
            var root = RootCtx();
            var rng = new System.Random(1);
            float cell = 300f;
            for (int i = 0; i < MengerCells.ChildCount; i++)
            {
                var o = MengerCells.Offsets[i];
                var c = new Vector3(o.x, o.y, o.z) * cell;
                for (int k = 0; k < 400; k++)
                {
                    var local = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * cell * 0.999f;
                    bool inParent = MengerOccupancy.IsSolidPoint(root, c + local, 900f / 81f);   // 4 levels
                    bool enlarged = MengerOccupancy.IsSolidPoint(root, local * 3f, 900f / 27f);  // 3 levels, x3
                    Assert.AreEqual(inParent, enlarged, $"child {i}");
                }
            }
        }

        [Test]
        public void ParentChildAddressing_RoundTripsWithinTolerance()
        {
            var a = RegionAddress.Root;
            var rng = new System.Random(3);
            for (int i = 0; i < 40; i++) a = a.Child(rng.Next(20));
            Assert.AreEqual(40, a.Depth);
            Assert.AreSame(a, a.Parent.Child(a.ChildIndex)); // identity is stable
            var up = RegionAddress.Convert(a, a.Parent.Parent.Parent, 900);
            var down = RegionAddress.Convert(a.Parent.Parent.Parent, a, 900);
            var p = new V3d(12.5, -77.25, 301.0);
            Assert.Less((down.Apply(up.Apply(p)) - p).Magnitude, 1e-9);

            // siblings differ by a pure translation of (offset difference) * rootSize
            var s1 = a.Parent.Child(0); var s2 = a.Parent.Child(19);
            var conv = RegionAddress.Convert(s1, s2, 900);
            Assert.AreEqual(1.0, conv.Scale, 1e-12);
            var d = MengerCells.Offsets[0] - MengerCells.Offsets[19];
            Assert.Less((conv.Offset - new V3d(d.x * 900, d.y * 900, d.z * 900)).Magnitude, 1e-9);
        }

        [Test]
        public void ContextOfChildMatchesContextOfParent_OverTheSharedArea()
        {
            var cfg = Cfg();
            var parent = RegionAddress.Root;
            foreach (int i in new[] { 4, 11, 0, 19, 7, 13 }) parent = parent.Child(i);
            var rng = new System.Random(5);
            for (int c = 0; c < MengerCells.ChildCount; c++)
            {
                var child = parent.Child(c);
                var ctxP = parent.GetContext(cfg.contextAncestors, cfg.rootSize);
                var ctxC = child.GetContext(cfg.contextAncestors, cfg.rootSize);
                var conv = RegionAddress.Convert(child, parent, cfg.rootSize);
                for (int k = 0; k < 150; k++)
                {
                    var pc = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 2700f * 0.999f; // inside the parent's cube
                    var pp = conv.Apply(new V3d(pc)).ToVector3();
                    Assert.AreEqual(MengerOccupancy.IsSolidPoint(ctxP, pp, 3.5f / 3f), MengerOccupancy.IsSolidPoint(ctxC, pc, 3.5f), $"child {c} point {pc}");
                }
            }
        }

        [Test]
        public void Entrances_AreDeterministic_IndependentOfLoadOrder()
        {
            var cfg = Cfg();
            var w1 = new WorldState(cfg);
            var w2 = new WorldState(cfg);
            var a = RegionAddress.Root.Child(3).Child(9);
            var b = RegionAddress.Root.Child(5).Child(2);
            var l1a = w1.GetEntrances(a); var l1b = w1.GetEntrances(b);
            var l2b = w2.GetEntrances(b); var l2a = w2.GetEntrances(a); // opposite order
            Assert.AreEqual(l1a.Select(e => e.Key).ToArray(), l2a.Select(e => e.Key).ToArray());
            Assert.AreEqual(l1b.Select(e => e.Key).ToArray(), l2b.Select(e => e.Key).ToArray());
        }

        [Test]
        public void RootEntrances_AllSeventyTwoSecondOrderMouths_AreAccessible()
        {
            var cfg = Cfg();
            var w = new WorldState(cfg);
            var forward = w.Entrances.Where(e => !e.Reverse).ToArray();
            Assert.AreEqual(72, forward.Length); // 8 corner cubes x 3 + 12 edge cubes x 4
            Assert.AreEqual(0, w.Entrances.Count(e => e.Reverse)); // the root has no parent
            float leaf = CollisionWorld.LeafSize(cfg.shipRadius);
            foreach (var e in forward)
            {
                Assert.IsTrue(EntranceCatalog.IsAccessible(e, w.Context, cfg.shipRadius, leaf), e.ToString());
                Assert.IsFalse(MengerOccupancy.IsSolidPoint(w.Context, e.MouthCenter, 1f), "mouth centre must be empty");
                Assert.IsFalse(MengerOccupancy.IsSolidPoint(w.Context, e.CubeCenter, 1f), "tunnel centre must be empty");
            }
            // main tunnels are NOT entrances: the mid-point of each main tunnel is not the mouth of any descriptor
            foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
                Assert.IsFalse(forward.Any(e => (e.MouthCenter - axis * 150f).sqrMagnitude < 1f));
        }

        [Test]
        public void ChildFrame_HasReverseMouthsOnlyWhereItOpensToEmptySpace()
        {
            var cfg = Cfg();
            var w = new WorldState(cfg);
            foreach (var e in w.Entrances.Where(x => !x.Reverse).Take(10))
            {
                var child = w.GetEntrances(e.Target);
                var back = child.Where(x => x.Reverse).ToArray();
                Assert.IsTrue(back.Any(x => x.DirectionIndex == e.DirectionIndex), "the mouth we entered through must be a reverse mouth");
                foreach (var r in back) Assert.AreEqual(w.Frame, r.Target);
            }
        }

        [Test]
        public void TransitionExponent_IsBoundedContinuousMonotoneAndLateralAware()
        {
            var cfg = Cfg();
            var w = new WorldState(cfg);
            var z = w.Zone;
            var fwd = w.Entrances.First(e => !e.Reverse);
            var n = fwd.NormalF;
            float prev = 0; float maxStep = 0;
            for (float h = z.H0 + 60f; h > -150f; h -= 0.5f)
            {
                Vector3 rel = n * h;
                float e = z.Exponent(fwd, rel);
                Assert.That(e, Is.InRange(0f, 1f));
                Assert.GreaterOrEqual(e + 1e-6f, prev, "forward exponent grows monotonically towards the tunnel");
                maxStep = Mathf.Max(maxStep, Mathf.Abs(e - prev));
                prev = e;
            }
            Assert.Less(maxStep, 0.015f, "continuous: no jump per 0.5 units");
            Assert.AreEqual(0f, z.Exponent(fwd, n * (z.H0 + 1)), 0f, "outside the approach zone nothing happens");
            Assert.AreEqual(1f, z.Exponent(fwd, n * (z.H1 - 5)), 1e-6f);
            // off the corridor: no growth at all
            Vector3 lateral = fwd.Axis == 0 ? Vector3.up : Vector3.right;
            Assert.AreEqual(0f, z.Exponent(fwd, n * z.H1 + lateral * (z.FwdOut + 1f)), 0f);
            Assert.AreEqual(0f, z.Exponent(fwd, -n * 500f), 0f, "behind the tunnel (far side) is not the entrance");

            // reverse mouth: only shrinks, and 0 deep inside
            var rev = new Entrance(RegionAddress.Root.Child(0), true, -1, fwd.DirectionIndex, Vector3.zero, 900f, Vector3.zero);
            prev = 0;
            for (float h = z.H1 - 80f; h < z.OuterStart; h += 0.5f)
            {
                float e = z.Exponent(rev, n * h);
                Assert.That(e, Is.InRange(-1f, 0f));
                Assert.LessOrEqual(e - 1e-6f, prev);
                prev = e;
            }
            Assert.AreEqual(-1f, z.Exponent(rev, n * (z.H0 + 10f)), 1e-6f);
            Assert.AreEqual(0f, z.Exponent(rev, n * (z.OuterStart + z.OuterLen + 1f)), 0f, "far outside the mouth nothing happens");
            Assert.AreEqual(0f, z.Exponent(rev, n * (z.H1 - 5f)), 0f);
        }

        [Test]
        public void Fog_IsMonotoneBoundedBelowBySafeStoppingDistance()
        {
            var cfg = Cfg();
            float prev = float.MaxValue;
            for (float d = 0; d < 60; d += 0.25f)
            {
                float v = FogModel.Visibility(cfg, d);
                Assert.LessOrEqual(v, prev + 1e-3f);
                Assert.Greater(v, FogModel.StoppingDistance(cfg) * cfg.safetyFactor - 1e-3f);
                prev = v;
            }
            Assert.Greater(FogModel.Visibility(cfg, 10000), 0f);
        }
    }
}
