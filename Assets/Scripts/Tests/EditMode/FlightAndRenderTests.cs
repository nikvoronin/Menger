using System.Collections.Generic;
using System.Linq;
using Menger.Core;
using Menger.Flight;
using Menger.Rendering;
using Menger.UI;
using NUnit.Framework;
using UnityEngine;

namespace Menger.Tests
{
    public class FlightAndRenderTests
    {
        static MengerSettings Cfg()
        {
            var c = ScriptableObject.CreateInstance<MengerSettings>();
            c.approachStartCells = 0.567f; // the long approach zone these tests were written for; shipped default is shorter
            c.rootSize = 900f; // these tests use coordinates of the original 900 cube; DefaultScale tests cover the shipped size
            return c;
        }

        sealed class Adapter : IWalkWorld
        {
            readonly WorldState w;
            public Adapter(WorldState world) { w = world; }
            public bool Collide(ref Vector3 p, float r, out Vector3 n) => w.Collide(ref p, r, out n);
            public float AxisRay(Vector3 p, Vector3 d, float max) => w.AxisRay(p, d, max);
        }

        // spots in the 900 cube: the outer top face of a corner cube (y = 450, x and z in [-450,-150]) and a floor patch of the z tunnel (y = -150)
        // (the faces are Sierpinski-carpet-like: both spots sit on solid strips, away from the holes)
        static readonly Vector3 TopFace = new Vector3(-417, 455, -423);
        static readonly Vector3 EdgeSpot = new Vector3(-440, 455, -423); // 10 units from the face edge x = -450
        static readonly Vector3 TunnelFloor = new Vector3(-138, -145, 250);

        static WalkerDynamics Walker(MengerSettings cfg, Vector3 pos, Vector3 up, Vector3 heading, IWalkWorld col)
        {
            var d = new WalkerDynamics(cfg);
            d.Place(pos, up, heading);
            Assert.IsTrue(d.Settle(col), "spot must have a face underfoot");
            return d;
        }

        [Test]
        public void Walker_StandsOneRadiusAboveTheFace_AndSnapsToIt()
        {
            var cfg = Cfg();
            var col = new Adapter(new WorldState(cfg));
            var d = Walker(cfg, TopFace + Vector3.up * 3f, Vector3.up, Vector3.right, col);
            Assert.AreEqual(450f + cfg.shipRadius, d.Position.y, 0.01f);
        }

        [Test]
        public void Walker_NeverPenetrates_AndRidesTheFaceWhileItHasOne_OverRandomWalks()
        {
            var cfg = Cfg();
            var w = new WorldState(cfg);
            var col = new Adapter(w);
            float leaf = CollisionWorld.LeafSize(cfg.shipRadius);
            int contacts = 0;
            foreach (var start in new[] { TopFace, TunnelFloor })
            {
                var d = Walker(cfg, start, Vector3.up, Vector3.right, col);
                var rng = new System.Random(5);
                for (int i = 0; i < 4000; i++)
                {
                    var input = new WalkInput
                    {
                        Forward = rng.Next(3) - 1, Strafe = rng.Next(3) - 1,
                        Look = new Vector2((float)rng.NextDouble() * 20f - 10f, (float)rng.NextDouble() * 6f - 3f),
                    };
                    d.Step(1f / 60f, input, col);
                    contacts += d.LastContactCount;
                    float clearance = CollisionWorld.Closest(w.Context, d.Position, 100f, leaf, out _);
                    Assert.GreaterOrEqual(clearance, cfg.shipRadius * 0.98f, $"penetration at step {i} from {start}");
                    if (!d.Airborne)
                    {
                        float ground = w.AxisRay(d.Position, -d.Up, 3f * cfg.shipRadius);
                        Assert.AreEqual(cfg.shipRadius, ground, 0.05f, $"standing but not on the face at step {i} from {start}");
                    }
                    Assert.AreEqual(0f, Vector3.Dot(d.Heading, d.Up), 1e-3f, "heading stays tangent to the face");
                }
            }
            Assert.Greater(contacts, 20, "the walks should actually brush against walls");
        }

        [Test]
        public void Walker_WalkingOffAnEdge_Falls_WithNoLimitOfItsOwn()
        {
            var cfg = Cfg();
            var col = new Adapter(new WorldState(cfg));
            var d = Walker(cfg, EdgeSpot, Vector3.up, Vector3.left, col); // the top face ends at x = -450, 10 units ahead
            for (int i = 0; i < 40; i++) d.Step(1f / 60f, new WalkInput { Forward = 1f }, col);
            Assert.IsTrue(d.Airborne, "walked off the edge: nothing underfoot, no invisible wall");
            Assert.Less(d.Position.x, -450f);
            Assert.AreEqual(Vector3.up, d.Up, "gravity is still the old face's");
            for (int i = 0; i < 60 * 40; i++) d.Step(1f / 60f, default, col); // 40 s of falling into empty space
            Assert.IsTrue(d.Airborne);
            Assert.Less(d.Position.y, 450f - 2000f, "still falling far below the sponge: the controller, not the dynamics, decides when to send the player back");
        }

        [Test]
        public void Walker_MouseLookAndWasd_AreFpsStyle()
        {
            var cfg = Cfg();
            var col = new Adapter(new WorldState(cfg));
            var d = Walker(cfg, TopFace, Vector3.up, Vector3.right, col);
            var p0 = d.Position;
            for (int i = 0; i < 30; i++) d.Step(1f / 60f, new WalkInput { Forward = 1f }, col);
            Assert.AreEqual(cfg.walkSpeed * 0.5f, (d.Position - p0).x, 0.3f, "W walks along the heading");
            p0 = d.Position;
            for (int i = 0; i < 30; i++) d.Step(1f / 60f, new WalkInput { Forward = -1f }, col);
            Assert.AreEqual(-cfg.walkSpeed * 0.5f, (d.Position - p0).x, 0.3f, "S walks back");
            p0 = d.Position;
            for (int i = 0; i < 30; i++) d.Step(1f / 60f, new WalkInput { Strafe = 1f }, col);
            Assert.AreEqual(-cfg.walkSpeed * 0.5f, (d.Position - p0).z, 0.3f, "D strafes to the right (-z when facing +x)");
            d.Step(1f / 60f, new WalkInput { Look = new Vector2(90f, 0f) }, col);
            Assert.Less((d.Heading - Vector3.back).magnitude, 1e-3f, "mouse right turns the heading right");
            d.Step(1f / 60f, new WalkInput { Look = new Vector2(0f, 500f) }, col);
            Assert.AreEqual(89f, d.Pitch, 1e-3f, "pitch is clamped");
            Assert.Greater(Vector3.Dot(d.Rotation * Vector3.forward, Vector3.up), 0.99f, "looking up");
        }

        [Test]
        public void Jump_RisesAndLandsOnTheSameSpot_InTheExpectedTime()
        {
            var cfg = Cfg();
            var col = new Adapter(new WorldState(cfg));
            var d = Walker(cfg, TopFace, Vector3.up, Vector3.right, col);
            d.Step(1f / 60f, new WalkInput { Jump = true }, col);
            Assert.IsTrue(d.Airborne);
            float peak = 0f; int frames = 1;
            while (d.Airborne && frames < 1000) { d.Step(1f / 60f, default, col); peak = Mathf.Max(peak, d.Position.y - TopFace.y); frames++; }
            Assert.IsFalse(d.Airborne);
            Assert.AreEqual(2f * cfg.jumpSpeed / cfg.gravity, frames / 60f, 0.1f);
            Assert.AreEqual(cfg.jumpSpeed * cfg.jumpSpeed / (2f * cfg.gravity), peak, 1f);
            Assert.AreEqual(TopFace.y, d.Position.y, 0.05f);
            Assert.AreEqual(TopFace.x, d.Position.x, 0.01f);
        }

        [Test]
        public void Jump_CrossesATunnel_WhereWalkingDropsIntoIt()
        {
            var cfg = Cfg();
            var col = new Adapter(new WorldState(cfg));
            // a 33-wide carpet hole spans x in [-414,-384] at z = -399; the strip behind it starts at x = -384
            var start = new Vector3(-424, 455, -399);
            var walk = Walker(cfg, start, Vector3.up, Vector3.right, col);
            for (int i = 0; i < 180; i++) walk.Step(1f / 60f, new WalkInput { Forward = 1f }, col);
            Assert.Less(walk.Position.y, 450f, "walking into the hole: the walker falls in (and is not on the top face any more)");

            var d = Walker(cfg, start, Vector3.up, Vector3.right, col);
            d.Step(1f / 60f, new WalkInput { Forward = 1f, Jump = true }, col);
            for (int i = 0; i < 100; i++) d.Step(1f / 60f, new WalkInput { Forward = 1f }, col); // 50 units forward in the air
            int guard = 0;
            while (d.Airborne && guard++ < 1000) d.Step(1f / 60f, default, col);
            Assert.IsFalse(d.Airborne);
            Assert.Greater(d.Position.x, -384f, "landed beyond the hole");
            Assert.AreEqual(455f, d.Position.y, 0.05f);
        }

        [Test]
        public void DistanceOutsideRoot_IsMeasuredInRootUnits_InAnyRegion()
        {
            var cfg = Cfg();
            var w = new WorldState(cfg);
            Assert.Less(w.DistanceOutsideRoot(new Vector3(100, -200, 300)), 0f, "inside");
            Assert.AreEqual(550f, w.DistanceOutsideRoot(new Vector3(0, 0, -1000)), 1e-3f);
            Assert.AreEqual(1550f, w.DistanceOutsideRoot(new Vector3(0, -2000, 100)), 1e-3f);
            // a child region is a third of the size in root units: the same display point is a third as far out
            var child = new WorldState(cfg, RegionAddress.Root.Child(4));
            Vector3 p = new Vector3(0, 0, -1000);
            Assert.Less(child.DistanceOutsideRoot(p), 550f / 2f);
            Vector3 root = RegionAddress.Convert(child.Frame, RegionAddress.Root, 900.0).Apply(new V3d(p)).ToVector3();
            Assert.AreEqual(Mathf.Max(Mathf.Abs(root.x), Mathf.Abs(root.y), Mathf.Abs(root.z)) - 450f, child.DistanceOutsideRoot(p), 1e-3f);
        }

        [Test]
        public void Jump_OverATunnelMouth_DoesNotScaleTheWorld_ButFallingIntoItDoes()
        {
            var cfg = ScriptableObject.CreateInstance<MengerSettings>(); // shipped zone
            cfg.rootSize = 900f;
            var w = new WorldState(cfg);
            var col = new Adapter(w);
            // the 100-wide mouth in the top face of the corner cube: x, z in [-350,-250]; jump right across it
            var d = new WalkerDynamics(cfg);
            d.Place(new Vector3(-352, 455, -300), Vector3.up, Vector3.right);
            Assert.IsTrue(d.Settle(col));
            w.Update(ref d.Position, out _);
            float worst = 0f; int guard = 0;
            d.Step(1f / 60f, new WalkInput { Forward = 1f, Jump = true }, col);
            for (int i = 0; i < 2000 && d.Airborne; i++, guard++)
            {
                d.Step(1f / 60f, new WalkInput { Forward = 1f }, col); // the whole jump: about 138 units
                w.Update(ref d.Position, out _);
                worst = Mathf.Max(worst, Mathf.Abs(w.Scale - 1f));
                Assert.IsFalse(w.HasLock, $"step {i}");
            }
            Assert.IsFalse(d.Airborne, "landed on the far side");
            Assert.Greater(d.Position.x, -250f);
            Assert.Less(worst, 1e-4f);
            Assert.AreEqual(0, w.Frame.Depth);

            // straight down into the mouth: the descent happens
            var w2 = new WorldState(cfg);
            Vector3 p = new Vector3(-300, 470, -300);
            w2.Update(ref p, out _);
            for (int i = 0; i < 2000 && w2.Frame.Depth == 0; i++) { p += Vector3.down * 0.5f; w2.Update(ref p, out _); }
            Assert.AreEqual(1, w2.Frame.Depth);
        }

        [Test]
        public void RayHit_FindsFacesAndTheirNormals()
        {
            var w = new WorldState(Cfg());
            Assert.IsTrue(w.RayHit(TopFace, Vector3.down, 100f, out var p, out var n));
            Assert.AreEqual(450f, p.y, 0.06f); Assert.AreEqual(Vector3.up, n);
            Assert.IsTrue(w.RayHit(TopFace, new Vector3(1, -1, 0).normalized, 100f, out p, out n));
            Assert.AreEqual(450f, p.y, 0.06f); Assert.AreEqual(TopFace.x + 5f, p.x, 0.1f); Assert.AreEqual(Vector3.up, n);
            Assert.IsTrue(w.RayHit(new Vector3(140, -145, 300), Vector3.right, 100f, out p, out n), "tunnel wall");
            Assert.AreEqual(150f, p.x, 0.06f); Assert.AreEqual(Vector3.left, n);
            Assert.IsFalse(w.RayHit(TopFace, Vector3.up, 1000f, out _, out _), "open sky");
            // almost parallel to the face: still reports the floor ahead
            Assert.IsTrue(w.RayHit(TopFace, new Vector3(1f, -0.02f, 0f).normalized, 3000f, out p, out n));
            Assert.AreEqual(450f, p.y, 1f); Assert.AreEqual(Vector3.up, n);
        }

        [Test]
        public void TeleportTo_StandsOnTheTarget_WithTheViewParallelToItsPlane()
        {
            var cfg = Cfg();
            var col = new Adapter(new WorldState(cfg));
            var d = Walker(cfg, TopFace, Vector3.up, Vector3.right, col);
            d.Pitch = -30f;
            Assert.IsTrue(d.TeleportTo(new Vector3(-300, 450, -423), Vector3.up, new Vector3(1, -0.5f, 0), col));
            Assert.Less((d.Position - new Vector3(-300, 455, -423)).magnitude, 0.06f);
            Assert.AreEqual(Vector3.up, d.Up);
            Assert.AreEqual(0f, d.Pitch);
            Assert.Less((d.Heading - Vector3.right).magnitude, 1e-3f, "keeps looking the same way along the new face");
            Assert.AreEqual(0f, Vector3.Dot(d.ViewDirection, d.Up), 1e-4f, "line of sight parallel to the plane");

            // onto a wall: its normal becomes up
            var e = Walker(cfg, new Vector3(140, -145, 300), Vector3.up, Vector3.right, col);
            Assert.IsTrue(e.TeleportTo(new Vector3(150, -145, 300), Vector3.left, new Vector3(1, 0.3f, 0.5f), col));
            Assert.AreEqual(Vector3.left, e.Up);
            Assert.Less((e.Position - new Vector3(145, -145, 300)).magnitude, 0.06f);
            Assert.AreEqual(0f, Vector3.Dot(e.ViewDirection, e.Up), 1e-4f);
            Assert.AreEqual(0f, Vector3.Dot(e.Heading, e.Up), 1e-4f);
            Assert.Greater(e.Heading.z, 0.5f, "the part of the old view that lies in the wall's plane is kept");
        }

        [Test]
        public void TeleportTo_AHoleOrOpenSpace_IsRefusedAndChangesNothing()
        {
            var cfg = Cfg();
            var col = new Adapter(new WorldState(cfg));
            var d = Walker(cfg, TopFace, Vector3.up, Vector3.right, col);
            var p = d.Position;
            Assert.IsFalse(d.TeleportTo(new Vector3(-399, 450, -399), Vector3.up, Vector3.right, col), "centre of the hole: no footing");
            Assert.AreEqual(p, d.Position);
            Assert.AreEqual(Vector3.up, d.Up);
        }

        [Test]
        public void ScalingGeometry_NeverTrapsOrDropsTheWalker()
        {
            // a walker on a face next to a tunnel mouth stays on it while the world update runs every frame
            var cfg = Cfg();
            var w = new WorldState(cfg);
            var col = new Adapter(w);
            var d = Walker(cfg, TopFace, Vector3.up, Vector3.right, col);
            for (int i = 0; i < 1200; i++)
            {
                d.Step(1f / 60f, new WalkInput { Forward = 1f, Look = new Vector2(i % 240 < 120 ? 0.3f : -0.3f, 0f) }, col);
                w.Update(ref d.Position, out _);
                if (!d.Airborne) d.SettleOrFall(col);
                if (d.Airborne) break; // walked off the face: from here on it is a fall, not a walk
                Assert.AreEqual(0, w.Frame.Depth);
                Assert.IsFalse(w.HasLock, $"walking on a face must not trigger a scale transition (step {i})");
            }
        }

        [Test]
        public void SunOrbit_TurnsRoundAGreatCircle_AndComesBack()
        {
            var toSun = new Vector3(0.35f, 0.8f, 0.5f).normalized;
            var axis = Vector3.Cross(toSun, Vector3.right).normalized;
            Assert.AreEqual(1f, SunOrbit.Direction(toSun, axis, 77f).magnitude, 1e-5f);
            Assert.Less((SunOrbit.Direction(toSun, axis, 180f) + toSun).magnitude, 1e-5f, "half an orbit: the sun is on the other side");
            Assert.Less((SunOrbit.Direction(toSun, axis, 360f) - toSun).magnitude, 1e-5f);
        }

        // ---- chunk mesh -------------------------------------------------------------------------------------------------

        [Test]
        public void MeshBuilder_UniformLevel_EmitsExactlyTheExposedFaces_WithOutwardWinding()
        {
            var root = new ContextRoot(Vector3.zero, 900f);
            var data = ChunkMeshBuilder.Build(root, Vector3.zero, new LodParams { Ratio = 0f, MinLeaf = 100f, MaxExpandDistance = 1e9f });
            Assert.AreEqual(400, data.Leaves);

            int expected = 0;
            foreach (var o1 in MengerCells.Offsets)
            foreach (var o2 in MengerCells.Offsets)
            {
                var c = (new Vector3(o1.x, o1.y, o1.z) * 300f) + new Vector3(o2.x, o2.y, o2.z) * 100f;
                foreach (var d in MengerCells.Directions)
                    if (!MengerOccupancy.IsSolidPoint(root, c + new Vector3(d.x, d.y, d.z) * 100f, 100f)) expected++;
            }
            Assert.AreEqual(expected, data.Faces);
            Assert.AreEqual(data.Faces * 4, data.Vertices.Count);

            var seen = new HashSet<(int, int, int, int)>();
            for (int t = 0; t < data.Triangles.Count; t += 3)
            {
                Vector3 a = data.Vertices[data.Triangles[t]], b = data.Vertices[data.Triangles[t + 1]], c = data.Vertices[data.Triangles[t + 2]];
                Vector3 geometric = Vector3.Cross(b - a, c - a).normalized;
                Assert.Greater(Vector3.Dot(geometric, data.Normals[data.Triangles[t]]), 0.99f, "winding faces the stored normal");
            }
            for (int i = 0; i < data.Vertices.Count; i += 4)
            {
                var centre = (data.Vertices[i] + data.Vertices[i + 2]) * 0.5f;
                Assert.IsTrue(seen.Add((Mathf.RoundToInt(centre.x * 10), Mathf.RoundToInt(centre.y * 10), Mathf.RoundToInt(centre.z * 10), Mathf.RoundToInt(Vector3.Dot(data.Normals[i], new Vector3(1, 3, 9)) * 100))), "duplicate face");
            }
        }

        [Test]
        public void MeshBuilder_MixedLod_CapsTunnelMouthsAtRefinementBorders()
        {
            var root = new ContextRoot(Vector3.zero, 900f);
            var lod = new LodParams { Ratio = 0.25f, MinLeaf = 3.5f, MaxExpandDistance = 5000f };
            var data = ChunkMeshBuilder.Build(root, new Vector3(0, 0, -440), lod);
            Debug.Log($"mixed-LOD build: leaves {data.Leaves}, faces {data.Faces}, tris {data.Triangles.Count / 3}, {data.BuildMilliseconds:0.0} ms (single worker thread)");
            Assert.Greater(data.Leaves, 500);
            Assert.Less(data.Triangles.Count / 3, 600000);
            // every emitted face belongs to a retained cell of its own size (it is a face of a real cube of that level)
            int floating = 0;
            for (int i = 0; i < data.Vertices.Count; i += 4)
            {
                var centre = (data.Vertices[i] + data.Vertices[i + 2]) * 0.5f;
                var n = data.Normals[i];
                float size = (data.Vertices[i + 1] - data.Vertices[i]).magnitude;
                if (!MengerOccupancy.IsCellRetained(root, centre - n * (size * 0.5f), size)) floating++;
            }
            Assert.AreEqual(0, floating, "no face may float in empty space");
        }

        [Test]
        public void MeshBuilder_DeterministicForTheSameInputs()
        {
            var root = new ContextRoot(Vector3.zero, 900f);
            var lod = new LodParams { Ratio = 0.2f, MinLeaf = 3.5f, MaxExpandDistance = 3000f };
            var a = ChunkMeshBuilder.Build(root, new Vector3(10, 20, -300), lod);
            var b = ChunkMeshBuilder.Build(root, new Vector3(10, 20, -300), lod);
            Assert.AreEqual(a.Faces, b.Faces);
            Assert.IsTrue(a.Vertices.SequenceEqual(b.Vertices));
        }

        [Test]
        public void RayBlocked_SeesWallsAndFlyThroughTunnels()
        {
            var cfg = Cfg();
            var w = new WorldState(cfg);
            Assert.IsFalse(w.RayBlocked(new Vector3(0, 0, -400), new Vector3(0, 0, 400)), "main tunnel along z is open");
            Assert.IsFalse(w.RayBlocked(new Vector3(-400, 0, 0), new Vector3(400, 0, 0)), "main tunnel along x is open");
            Assert.IsTrue(w.RayBlocked(Vector3.zero, new Vector3(300, 300, 0)), "the edge cube (1,1,0) is solid");
            Assert.IsTrue(w.RayBlocked(new Vector3(0, 0, -100), new Vector3(0, 0, 500)) == false, "open ray out of the sponge");
            Assert.IsFalse(w.RayBlocked(new Vector3(10, 10, -600), new Vector3(10, 10, -500)), "open space outside");
            Assert.IsTrue(w.RayBlocked(new Vector3(440, 440, -600), new Vector3(440, 440, 0)), "solid corner column");
        }

        // ---- HUD ---------------------------------------------------------------------------------------------------------

        [Test]
        public void EdgeArrow_PointsTheRightWay_AtAnyRoll_AndBehindTheCamera()
        {
            var screen = new Vector2(1920, 1080);
            float inset = 48;
            for (int roll = 0; roll < 360; roll += 30)
            {
                // a target to the camera's right, rolled with the camera
                var cam = Quaternion.Euler(0, 0, roll) * new Vector3(1, 0, 0.2f);
                HudMath.EdgeArrow(cam, screen, inset, Vector2.zero, out var pos, out var angle);
                float expectedAngle = Mathf.Atan2(cam.x, cam.y) * Mathf.Rad2Deg;
                Assert.AreEqual(Mathf.DeltaAngle(expectedAngle, angle), 0f, 0.01f);
                Assert.LessOrEqual(Mathf.Abs(pos.x), screen.x * 0.5f - inset + 0.01f);
                Assert.LessOrEqual(Mathf.Abs(pos.y), screen.y * 0.5f - inset + 0.01f);
                Assert.IsTrue(Mathf.Approximately(Mathf.Abs(pos.x), screen.x * 0.5f - inset) || Mathf.Approximately(Mathf.Abs(pos.y), screen.y * 0.5f - inset), "on the inset rectangle");
            }
            // behind the camera, above: arrow still points up (screen-space direction), never mirrored
            HudMath.EdgeArrow(new Vector3(0.001f, 5f, -20f), screen, inset, Vector2.zero, out var p2, out var a2);
            Assert.Greater(p2.y, 0f);
            // dead behind: keeps the previous direction instead of jumping
            HudMath.EdgeArrow(new Vector3(0, 0, -10f), screen, inset, new Vector2(1, 0), out var p3, out _);
            Assert.Greater(p3.x, 0f);
            Assert.IsFalse(HudMath.InFrame(new Vector3(0, 0, -5f), 70f, screen, 0f));
            Assert.IsTrue(HudMath.InFrame(new Vector3(0, 0, 50f), 70f, screen, 48f));
            Assert.IsFalse(HudMath.InFrame(new Vector3(100, 0, 50f), 70f, screen, 48f));
        }

        [Test]
        public void HudTargets_OnlyExits_LimitedCount_WithSwapHysteresis()
        {
            var cfg = Cfg();
            Assert.AreEqual(0, new WorldState(cfg).Entrances.Count(e => e.Reverse), "the root has no exit");
            var w = new WorldState(cfg, RegionAddress.Root.Child(4));
            Assert.IsTrue(w.Entrances.Any(e => !e.Reverse));
            var t = new HudTargets();
            Vector3 here = Vector3.zero;
            t.Update(w.Entrances, e => (e.MouthCenter - here).magnitude, 5000, 3, 0.8f, exits: true);
            Assert.AreEqual(3, t.Active.Count);
            foreach (int key in t.Active) Assert.IsTrue(w.Entrances.Any(e => e.Key == key && e.Reverse));
            var before = t.Active.ToArray();
            // tiny change in distance must not swap arrows
            t.Update(w.Entrances, e => (e.MouthCenter - (here + new Vector3(0.5f, 0, 0))).magnitude, 5000, 3, 0.8f, exits: true);
            Assert.AreEqual(before, t.Active.ToArray());
            // range limit
            t.Update(w.Entrances, e => 99999f, 100, 3, 0.8f, exits: true);
            Assert.AreEqual(0, t.Active.Count);
            // the descent mouths are still available to a caller that asks for them
            t.Update(w.Entrances, e => (e.MouthCenter - here).magnitude, 5000, 3, 0.8f, exits: false);
            foreach (int key in t.Active) Assert.IsTrue(w.Entrances.Any(e => e.Key == key && !e.Reverse));
        }
    }
}
