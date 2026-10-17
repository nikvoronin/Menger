using System.Collections.Generic;
using System.Linq;
using Menger.Core;
using Menger.Flight;
using NUnit.Framework;
using UnityEngine;

namespace Menger.Tests
{
    public class TransitionTests
    {
        const float Step = 1.5f;

        static MengerSettings Cfg()
        {
            var c = ScriptableObject.CreateInstance<MengerSettings>();
            c.approachStartCells = 0.567f; // the long approach zone these tests were written for; shipped default is shorter
            c.rootSize = 900f; // these tests use coordinates of the original 900 cube; DefaultScale tests cover the shipped size
            return c;
        }
        static double EffectiveScale(WorldState w) => w.Map.Scale * System.Math.Pow(3.0, w.Frame.Depth);

        /// <summary>Fly a straight line in display space; check continuity of scale, depth and landmarks at every step.</summary>
        static FlipKind FlyUntilFlip(WorldState w, ref Vector3 ship, Vector3 dir, float step, Vector3 landmarkLogicInFrame, List<string> log)
        {
            var cfg = w.Settings;
            for (int i = 0; i < 1200; i++)
            {
                double scaleBefore = EffectiveScale(w);
                float depthBefore = w.ContinuousDepth;
                var frameBefore = w.Frame;
                ship += dir * step;
                var kind = w.Update(ref ship, out var shift);
                double scaleAfter = EffectiveScale(w);
                Assert.Less(System.Math.Abs(scaleAfter / scaleBefore - 1.0), 0.05, $"world scale jumped at step {i} ({kind})");
                Assert.Less(System.Math.Abs(w.ContinuousDepth - depthBefore), 0.06f, $"continuous depth (fog) jumped at step {i}");
                var p = ship;
                Assert.IsFalse(w.Collide(ref p, cfg.shipRadius, out _), $"clear route must never touch geometry (step {i}, scale {w.Scale})");
                if (kind != FlipKind.None)
                {
                    // the same physical landmark must keep its display position through the coordinate reassignment
                    var before = (w.LastFlipMapBefore.Apply(new V3d(landmarkLogicInFrame)) - new V3d(shift));
                    var conv = RegionAddress.Convert(w.LastFlipFrameBefore, w.Frame, cfg.rootSize);
                    var after = w.Map.Apply(conv.Apply(new V3d(landmarkLogicInFrame)));
                    Assert.Less((before - after).Magnitude, 1e-3, "frame reassignment moved the world");
                    log?.Add($"{kind} at step {i}");
                    return kind;
                }
            }
            Assert.Fail($"no flip happened (frame {w.Frame}, lock {w.HasLock} {w.Lock}, e {w.Exponent}, ship {ship})");
            return FlipKind.None;
        }

        [Test]
        public void Teleport_ThroughAMouth_DescendsAndScales_AndOutThroughTheExitAscends_OtherwiseNothing()
        {
            var cfg = Cfg();
            var w = new WorldState(cfg);
            var en = w.Entrances.First(e => !e.Reverse);
            Vector3 n = en.NormalF;
            Vector3 t1 = Mathf.Abs(n.y) > 0.9f ? Vector3.right : Vector3.up;
            float cell = cfg.rootSize / 3f;
            // a point on the wall of the tunnel, 80 units inside the mouth, seen from 60 units in front of the mouth centre
            Vector3 x = en.CubeCenter + n * (cell * 0.5f - 80f) + t1 * (cell / 6f);
            Vector3 from = en.MouthCenter + n * 60f;
            Vector3 ground = w.LogicToDisplay(x), fromD = w.LogicToDisplay(from);

            // same target, but the line does not go through the opening (from beside the cube, through its solid wall): nothing happens
            Vector3 side = en.CubeCenter + n * (cell * 0.5f - 80f) - t1 * (cell * 0.5f + 60f);
            Vector3 sh = ground;
            Assert.AreEqual(FlipKind.None, w.Teleported(ref sh, w.LogicToDisplay(side), ground));
            Assert.AreEqual(0, w.Frame.Depth);
            Assert.AreEqual(1f, w.Scale, 1e-6f);
            // from inside the tunnel's own cube: no mouth crossed either
            sh = ground;
            Assert.AreEqual(FlipKind.None, w.Teleported(ref sh, w.LogicToDisplay(x - n * 10f), ground));
            Assert.AreEqual(0, w.Frame.Depth);

            Vector3 ship = ground; Vector3 shipBefore = ship;
            Assert.AreEqual(FlipKind.Descended, w.Teleported(ref ship, fromD, ground));
            Assert.AreSame(en.Target, w.Frame);
            Assert.AreEqual(1, w.Frame.Depth);
            Assert.AreEqual(3.0, EffectiveScale(w), 1e-9, "the world is x3 about the point");
            // the point did not move on screen (up to the floating-origin shift)
            var conv = RegionAddress.Convert(w.LastFlipFrameBefore, w.Frame, cfg.rootSize);
            Vector3 shift = shipBefore - ship;
            Vector3 after = w.Map.Apply(conv.Apply(new V3d(x))).ToVector3();
            Assert.Less((after - (ground - shift)).magnitude, 1e-2f);

            // the way back: from inside the child, through its exit mouth, to a point outside its cube
            var rev = w.Entrances.Where(e => e.Reverse).ToArray();
            Assert.IsNotEmpty(rev);
            Vector3 g2 = w.LogicToDisplay(rev[0].MouthCenter + rev[0].NormalF * 30f);
            Vector3 f2 = w.LogicToDisplay(rev[0].MouthCenter - rev[0].NormalF * 60f);
            Vector3 ship2 = g2;
            Assert.AreEqual(FlipKind.Ascended, w.Teleported(ref ship2, f2, g2));
            Assert.AreEqual(0, w.Frame.Depth);
            Assert.AreEqual(1.0, EffectiveScale(w), 1e-9);

            // a teleport that crosses no mouth drops a pending transition: scale 1 about the point
            var w3 = new WorldState(cfg);
            Vector3 p3 = en.Pivot + n * (w3.Zone.H0 + 30f);
            w3.Update(ref p3, out _);
            for (int i = 0; i < 90; i++) { p3 -= n * 1.5f; w3.Update(ref p3, out _); }
            Assert.IsTrue(w3.HasLock);
            Assert.Greater(w3.Scale, 1.01f);
            Vector3 g3 = w3.LogicToDisplay(new Vector3(-440, 450, -440));
            Vector3 s3 = g3;
            Assert.AreEqual(FlipKind.None, w3.Teleported(ref s3, p3, g3));
            Assert.IsFalse(w3.HasLock);
            Assert.AreEqual(1f, w3.Scale, 1e-6f);
            Assert.Less((w3.LogicToDisplay(new Vector3(-440, 450, -440)) - g3).magnitude, 1e-3f, "the point stays put");
        }

        [Test]
        public void WalkingOrFallingThroughAMouth_ScalesToo_EvenAlongTheWall_ButNotWhileASmoothTransitionRuns()
        {
            var cfg = Cfg();
            var w = new WorldState(cfg);
            var en = w.Entrances.First(e => !e.Reverse);
            Vector3 n = en.NormalF;
            Vector3 t1 = Mathf.Abs(n.y) > 0.9f ? Vector3.right : Vector3.up;
            float wall = en.MouthHalfWidth - cfg.shipRadius; // hugging the wall, where the centred approach zone has no effect

            // a step along the outside of the mouth does not go through it
            Vector3 a = w.LogicToDisplay(en.MouthCenter + n * 20f + t1 * wall), b = w.LogicToDisplay(en.MouthCenter + n * 5f + t1 * wall + t1 * 200f);
            Vector3 ship = b;
            Assert.AreEqual(FlipKind.None, w.CrossedMouth(ref ship, a, b));
            Assert.AreEqual(0, w.Frame.Depth);

            // falling in along the wall: the descent happens at once, about the player
            Vector3 from = w.LogicToDisplay(en.MouthCenter + n * 3f + t1 * wall);
            Vector3 to = w.LogicToDisplay(en.MouthCenter - n * 4f + t1 * wall);
            ship = to;
            Assert.AreEqual(FlipKind.Descended, w.CrossedMouth(ref ship, from, to));
            Assert.AreEqual(1, w.Frame.Depth);
            Assert.AreEqual(3.0, EffectiveScale(w), 1e-9);

            w.Update(ref ship, out _); // the next frame releases the lock the flip left behind
            Assert.IsFalse(w.HasLock);
            // and out through the exit of the child, along its wall
            var rev = w.Entrances.First(e => e.Reverse);
            Vector3 rn = rev.NormalF;
            Vector3 rt = Mathf.Abs(rn.y) > 0.9f ? Vector3.right : Vector3.up;
            float rwall = rev.MouthHalfWidth - cfg.shipRadius;
            from = w.LogicToDisplay(rev.MouthCenter - rn * 4f + rt * rwall);
            to = w.LogicToDisplay(rev.MouthCenter + rn * 3f + rt * rwall);
            ship = to;
            Assert.AreEqual(FlipKind.Ascended, w.CrossedMouth(ref ship, from, to));
            Assert.AreEqual(0, w.Frame.Depth);
            Assert.AreEqual(1.0, EffectiveScale(w), 1e-9);

            // while a smooth transition holds the lock it is left to finish
            var w2 = new WorldState(cfg);
            var en2 = w2.Entrances.First(e => !e.Reverse);
            Vector3 p = en2.Pivot + en2.NormalF * (w2.Zone.H0 + 30f);
            w2.Update(ref p, out _);
            for (int i = 0; i < 90; i++) { p -= en2.NormalF * 1.5f; w2.Update(ref p, out _); }
            Assert.IsTrue(w2.HasLock);
            Vector3 s2 = p;
            Assert.AreEqual(FlipKind.None, w2.CrossedMouth(ref s2, p + en2.NormalF * 5f, p));
            Assert.IsTrue(w2.HasLock);
        }

        [Test]
        public void StoppedMidTransition_ScaleStaysPut_AndReversingRestoresIt()
        {
            var cfg = Cfg();
            var w = new WorldState(cfg);
            var en = w.Entrances.First(e => !e.Reverse);
            Vector3 n = en.NormalF;
            Vector3 ship = en.Pivot + n * (w.Zone.H0 + 30f);
            w.Update(ref ship, out _);
            var scales = new List<float>();
            while (w.Zone.Exponent(en, ship - en.Pivot) < 0.5f) { ship -= n * 0.5f; w.Update(ref ship, out _); }
            float mid = w.Scale;
            Assert.AreEqual(Mathf.Pow(3f, 0.5f), mid, 0.2f);
            Assert.IsTrue(w.HasLock);
            for (int i = 0; i < 500; i++) w.Update(ref ship, out _); // standing still: nothing changes (no time integration)
            Assert.AreEqual(mid, w.Scale, 1e-6f);
            while (w.HasLock && ship.magnitude < 5000) { ship += n * 0.5f; w.Update(ref ship, out _); }
            Assert.IsFalse(w.HasLock);
            Assert.AreEqual(1f, w.Scale, 1e-6f);
            Assert.AreEqual(RegionAddress.Root, w.Frame);
            Assert.Less(w.Map.Offset.Magnitude, 1e-2);
        }

        [Test]
        public void MainTunnels_NeverTriggerScaling()
        {
            var cfg = Cfg();
            var w = new WorldState(cfg);
            foreach (var dir in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                Vector3 ship = -dir * 440f;
                w.Update(ref ship, out _);
                for (int i = 0; i < 600; i++)
                {
                    ship += dir * 1.5f;
                    w.Update(ref ship, out _);
                    Assert.IsFalse(w.HasLock, $"main tunnel {dir} at {ship}");
                    Assert.AreEqual(0, w.Frame.Depth);
                }
            }
        }

        [Test]
        public void SingleDescentAndReturn_IsSeamless_AndRestoresTheRegion()
        {
            var cfg = Cfg();
            var w = new WorldState(cfg);
            var en = w.Entrances.First(e => !e.Reverse);
            Vector3 n = en.NormalF;
            Vector3 ship = en.Pivot + n * (w.Zone.H0 + 30f);
            w.Update(ref ship, out _);

            var log = new List<string>();
            var landmark = en.MouthCenter;
            Assert.AreEqual(FlipKind.Descended, FlyUntilFlip(w, ref ship, -n, Step, landmark, log));
            Assert.AreEqual(1, w.Frame.Depth);
            Assert.AreSame(en.Target, w.Frame);

            // the tunnel is now the child's main tunnel: world scale is back to x1, the lock releases without a jump
            w.Update(ref ship, out _);
            Assert.IsFalse(w.HasLock);
            Assert.AreEqual(1f, w.Scale, 1e-6f);

            // go back out through the same mouth
            var child = w.Frame;
            Assert.AreEqual(FlipKind.Ascended, FlyUntilFlip(w, ref ship, n, Step, Vector3.zero, log));
            Assert.AreSame(RegionAddress.Root, w.Frame);
            for (int i = 0; i < 400 && w.HasLock; i++) { ship += n * Step; w.Update(ref ship, out _); }
            Assert.IsFalse(w.HasLock);
            Assert.AreEqual(1f, w.Scale, 1e-6f);
            Assert.Less(w.Map.Offset.Magnitude, 1e-2);
            Assert.IsNotEmpty(log);
        }

        [Test]
        public void DefaultScale_DescentAndReturn_AreSeamlessToo()
        {
            var cfg = ScriptableObject.CreateInstance<MengerSettings>(); // shipped rootSize
            Assert.AreEqual(900f, cfg.rootSize);
            var w = new WorldState(cfg);
            Assert.AreEqual(72, w.Entrances.Count(e => !e.Reverse));
            var en = w.Entrances.First(e => !e.Reverse);
            Vector3 n = en.NormalF;
            Vector3 ship = en.Pivot + n * (w.Zone.H0 + 30f);
            w.Update(ref ship, out _);
            const float big = 0.5f; // the shipped zone is only ~42 units long
            Assert.AreEqual(FlipKind.Descended, FlyUntilFlip(w, ref ship, -n, big, en.MouthCenter, null));
            w.Update(ref ship, out _);
            Assert.AreEqual(1f, w.Scale, 1e-6f);
            Assert.AreEqual(FlipKind.Ascended, FlyUntilFlip(w, ref ship, n, big, Vector3.zero, null));
            for (int i = 0; i < 800 && w.HasLock; i++) { ship += n * big; w.Update(ref ship, out _); }
            Assert.IsFalse(w.HasLock);
            Assert.Less(w.Map.Offset.Magnitude, 1e-2);
            // the main tunnel axes still never trigger scaling at this size
            foreach (var dir in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                ship = -dir * 430f; w.Update(ref ship, out _);
                for (int i = 0; i < 600; i++) { ship += dir * 1.5f; w.Update(ref ship, out _); Assert.IsFalse(w.HasLock, $"{dir} at {ship}"); }
            }
        }

        [Test]
        public void RepeatedRoundTrips_AccumulateNoDrift()
        {
            var cfg = Cfg();
            var w = new WorldState(cfg);
            var en = w.Entrances.First(e => !e.Reverse);
            Vector3 n = en.NormalF;
            double worst = 0;
            for (int cycle = 0; cycle < 60; cycle++)
            {
                Vector3 ship = en.Pivot + n * (w.Zone.H0 + 30f);
                w.Update(ref ship, out _);
                Assert.AreEqual(FlipKind.Descended, FlyUntilFlip(w, ref ship, -n, Step, en.MouthCenter, null));
                w.Update(ref ship, out _);
                Assert.AreEqual(FlipKind.Ascended, FlyUntilFlip(w, ref ship, n, Step, Vector3.zero, null));
                for (int i = 0; i < 400 && w.HasLock; i++) { ship += n * Step; w.Update(ref ship, out _); }
                Assert.IsFalse(w.HasLock);
                worst = System.Math.Max(worst, w.Map.Offset.Magnitude);
                // keep the ship's display position bounded like the game does (floating origin)
                Assert.Less(ship.magnitude, 2000f);
            }
            Assert.Less(worst, 1e-2, "origin drift after 60 round trips");
        }

        [Test]
        public void TwelveConsecutiveDescents_ThenBackOutTheSameChain()
        {
            var cfg = Cfg();
            var w = new WorldState(cfg);
            var chain = new List<RegionAddress> { w.Frame };
            var directions = new List<int>();

            for (int level = 0; level < 12; level++)
            {
                var forward = w.Entrances.Where(e => !e.Reverse).ToArray();
                var en = forward[(level * 7 + 3) % forward.Length];
                Vector3 n = en.NormalF;
                Vector3 ship = w.LogicToDisplay(en.Pivot + n * (w.Zone.H0 + 30f)); // diagnostic placement only
                w.Update(ref ship, out _);
                Assert.AreEqual(FlipKind.Descended, FlyUntilFlip(w, ref ship, -n, Step, en.MouthCenter, null), $"level {level}");
                w.Update(ref ship, out _);
                chain.Add(w.Frame);
                directions.Add(en.DirectionIndex);
                Assert.AreEqual(level + 1, w.Frame.Depth);
                Assert.AreEqual(1f, w.Scale, 1e-6f, "scale resets to x1 by coordinate reassignment, not by shrinking");
                Assert.IsTrue(w.Entrances.Any(e => !e.Reverse), "the child region offers its own transitions");
            }

            for (int level = 11; level >= 0; level--)
            {
                var back = w.Entrances.First(e => e.Reverse && e.DirectionIndex == directions[level]);
                Vector3 n = back.NormalF;
                Vector3 ship = w.LogicToDisplay(n * (w.Zone.H1 - 10f));
                w.Update(ref ship, out _);
                Assert.AreEqual(FlipKind.Ascended, FlyUntilFlip(w, ref ship, n, Step, Vector3.zero, null), $"back at level {level}");
                Assert.AreSame(chain[level], w.Frame, "returning restores the identical region");
                for (int i = 0; i < 400 && w.HasLock; i++) { ship += n * Step; w.Update(ref ship, out _); }
            }
            Assert.AreEqual(0, w.Frame.Depth);
        }

        [Test]
        public void DeepDescent_HasNoFixedLimit_AndStaysNumericallyStable()
        {
            var cfg = Cfg();
            var w = new WorldState(cfg);
            for (int level = 0; level < 400; level++)
            {
                var forward = w.Entrances.Where(e => !e.Reverse).ToArray();
                var en = forward[(level * 5 + 1) % forward.Length];
                Vector3 n = en.NormalF;
                Vector3 ship = w.LogicToDisplay(en.Pivot + n * (w.Zone.H0 + 30f));
                w.Update(ref ship, out _);
                Assert.AreEqual(FlipKind.Descended, FlyUntilFlip(w, ref ship, -n, Step, en.MouthCenter, null));
                w.Update(ref ship, out _);
                Assert.Less(ship.magnitude, 1500f, "floating origin keeps display coordinates small at depth " + level);
            }
            Assert.AreEqual(400, w.Frame.Depth);
            Assert.AreEqual(400, w.Frame.Path().Length);
        }

        [Test]
        public void FlyingThroughASiblingBoundary_SwitchesRegionWithoutScaling()
        {
            var cfg = Cfg();
            var q = RegionAddress.Root.Child(MengerCells.IndexOf(new Vector3Int(1, 1, 0)));
            var w = new WorldState(cfg, q);
            Vector3 ship = Vector3.zero;
            w.Update(ref ship, out _);
            var kind = FlyUntilFlip(w, ref ship, Vector3.forward, Step, Vector3.zero, null);
            Assert.AreEqual(FlipKind.Sibling, kind);
            Assert.AreSame(RegionAddress.Root.Child(MengerCells.IndexOf(new Vector3Int(1, 1, 1))), w.Frame);
            Assert.AreEqual(1f, w.Scale, 0f);
            Assert.IsFalse(w.HasLock);
            // and back again
            Assert.AreEqual(FlipKind.Sibling, FlyUntilFlip(w, ref ship, Vector3.back, Step, Vector3.zero, null));
            Assert.AreSame(q, w.Frame);
        }

        [Test]
        public void ReverseMouth_OnlyWhereTheChildOpensToEmptySpace_SoNotEveryTunnelReturns()
        {
            var cfg = Cfg();
            var w = new WorldState(cfg);
            var en = w.Entrances.First(e => !e.Reverse);
            var child = w.GetEntrances(en.Target);
            int reverse = child.Count(e => e.Reverse);
            Assert.Greater(reverse, 0);
            Assert.Less(reverse, 6 + 1);
            // a retained neighbour on the other side of the child's cube means that face is NOT a way back to the parent
            for (int d = 0; d < 6; d++)
            {
                var dir = MengerCells.Directions[d];
                var neighbourOffset = en.Target.Offset + dir;
                bool neighbourRetained = MengerCells.IsRetained(neighbourOffset) && MengerCells.IndexOf(neighbourOffset) >= 0;
                Assert.AreEqual(!neighbourRetained, child.Any(e => e.Reverse && e.DirectionIndex == d), $"direction {d}");
            }
        }
    }
}
