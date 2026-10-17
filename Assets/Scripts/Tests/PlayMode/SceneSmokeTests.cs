using System.Collections;
using System.IO;
using System.Linq;
using Menger.Core;
using Menger.Flight;
using Menger.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Menger.Tests
{
    /// <summary>Runs the real demo scene: wiring, async mesh builds, walking and edge transfer through the full runtime stack.</summary>
    public class SceneSmokeTests
    {
        MengerGame game;
        WorldRenderer worldRenderer;

        IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("MengerPrototype");
            yield return null;
            game = Object.FindAnyObjectByType<MengerGame>();
            Assert.IsNotNull(game, "MengerGame in scene");
            yield return null;
            worldRenderer = Object.FindAnyObjectByType<WorldRenderer>();
            Assert.IsNotNull(worldRenderer);
        }

        IEnumerator WaitForMesh()
        {
            // settled = a mesh exists for the current frame and no rebuild is pending
            float t0 = Time.realtimeSinceStartup;
            yield return null;
            while (Time.realtimeSinceStartup - t0 < 20f && (worldRenderer.BuildsApplied == 0 || worldRenderer.SurfaceFrame != game.World.Frame || worldRenderer.BuildInFlight)) yield return null;
            Assert.Greater(worldRenderer.BuildsApplied, 0, "an async mesh build must complete");
            Assert.AreSame(game.World.Frame, worldRenderer.SurfaceFrame);
        }

        float GroundGap() => game.World.AxisRay(game.Player.transform.position, -game.Player.dynamics.Up, 3f * game.settings.shipRadius);

        [UnityTest]
        public IEnumerator Scene_Runs_BuildsSurface_AndShowsOutlines()
        {
            yield return Load();
            yield return WaitForMesh();
            Assert.Greater(worldRenderer.Triangles, 1000);
            Assert.AreEqual(0, game.World.Frame.Depth);
            Assert.AreEqual(72, game.World.Entrances.Count(e => !e.Reverse));
            Assert.Greater(game.Visibility, 0f);
            Assert.IsTrue(RenderSettings.fog);
        }

        [UnityTest]
        public IEnumerator Player_StartsOnTheSurface_CameraFollowsIt_Jumps_AndETeleportsToTheAim()
        {
            yield return Load();
            yield return WaitForMesh();
            var p = game.Player;
            var eye = Camera.main.transform;
            yield return null;
            Assert.AreEqual(game.settings.shipRadius, GroundGap(), 0.05f, "spawned standing on a face");
            Assert.AreEqual(Vector3.up, p.dynamics.Up);
            Assert.Less((eye.position - p.transform.position).magnitude, 1e-3f, "camera is at the player's centre");

            // walk forward and back along the solid strip (scripted input through the same dynamics the keyboard drives) and stay on the surface
            for (int i = 0; i < 120; i++)
            {
                p.dynamics.Step(1f / 60f, new WalkInput { Forward = i < 60 ? 1f : -1f }, p);
                yield return null;
                Assert.AreEqual(game.settings.shipRadius, GroundGap(), 0.1f, $"frame {i}");
            }

            // jump: leaves the surface, comes back to it
            p.dynamics.Step(1f / 60f, new WalkInput { Jump = true }, p);
            Assert.IsTrue(p.dynamics.Airborne);
            float t0 = Time.realtimeSinceStartup;
            while (p.dynamics.Airborne && Time.realtimeSinceStartup - t0 < 10f) yield return null;
            Assert.IsFalse(p.dynamics.Airborne, "landed");
            Assert.AreEqual(game.settings.shipRadius, GroundGap(), 0.1f);

            // look at a wall of the tunnel and press E: stand on it, line of sight parallel to it
            p.Place(new Vector3(140, -145, 300), Vector3.up, Vector3.right);
            yield return null;
            Assert.IsTrue(p.HasAim, "the view ray hits the wall");
            Assert.AreEqual(Vector3.left, p.AimNormal);
            Assert.IsTrue(p.TeleportToAim());
            yield return null;
            yield return null;
            Assert.AreEqual(Vector3.left, p.dynamics.Up);
            Assert.AreEqual(game.settings.shipRadius, GroundGap(), 0.1f);
            Assert.Greater(Vector3.Dot(eye.up, Vector3.left), 0.99f, "the camera's up is the new face's normal");
            Assert.AreEqual(0f, Vector3.Dot(eye.forward, Vector3.left), 1e-3f, "line of sight parallel to the new plane");
        }

        [UnityTest]
        public IEnumerator ClickTeleportIntoATunnel_ChangesTheRegionAndScale()
        {
            yield return Load();
            yield return WaitForMesh();
            var p = game.Player;
            var w = game.World;
            // from the rim of the big mouth look down at the far wall of its shaft, click
            p.Place(new Vector3(-240, 455, -300), Vector3.up, Vector3.left);
            p.dynamics.Pitch = -12f;
            yield return null;
            Assert.IsTrue(p.HasAim);
            Assert.Less(p.AimPoint.y, 445f, "the aim is on the far wall inside the tunnel");
            Assert.IsTrue(p.TeleportToAim());
            Assert.AreEqual(1, w.Frame.Depth, "teleporting into a tunnel descends, as flying through its mouth does");
            Assert.AreEqual(3.0, w.Map.Scale * System.Math.Pow(3.0, w.Frame.Depth), 1e-6, "and scales the world x3");
            yield return WaitForMesh();
            yield return null;
            Assert.AreEqual(game.settings.shipRadius, GroundGap(), 0.1f, "standing on the wall of the (now x3) tunnel");
            Assert.AreEqual(0f, Vector3.Dot(Camera.main.transform.forward, p.dynamics.Up), 1e-3f);
        }

        [UnityTest]
        public IEnumerator FallingThroughATunnel_ReturnsThePlayerToTheStart()
        {
            yield return Load();
            yield return WaitForMesh();
            var p = game.Player;
            float oldLimit = game.settings.escapeDistance, oldGravity = game.settings.gravity;
            game.settings.escapeDistance = 60f; // the tunnel runs through the whole sponge: nothing catches the player, so they leave it at the bottom
            game.settings.gravity = 80f;        // and fall faster than usual to keep the test short
            try
            {
                p.Place(new Vector3(-340, 455, -300), Vector3.up, Vector3.right); // beside the 100-wide mouth, a step from its edge
                yield return null;
                p.dynamics.Step(1f / 60f, new WalkInput { Forward = 1f, Jump = true }, p);
                float t0 = Time.realtimeSinceStartup;
                bool left = false, enteredChild = false;
                int frames = 0;
                while (Time.realtimeSinceStartup - t0 < 60f)
                {
                    yield return null;
                    if (frames++ < 20 && p.dynamics.Airborne) p.dynamics.Step(Time.deltaTime, new WalkInput { Forward = 1f }, p); // keys held for a moment: over the edge
                    if (game.World.Frame.Depth == 1) enteredChild = true; // falling in goes through the mouth: the region changes
                    if (p.dynamics.Position.y < 420f) left = true; // dropped into the tunnel (they are sent back once far outside the sponge)
                    if (left && p.dynamics.Position.y > 400f && !p.dynamics.Airborne) break; // and was put back
                }
                Assert.IsTrue(left, "fell into the tunnel");
                Assert.IsTrue(enteredChild, "and scaled/descended on the way in");
                Assert.IsFalse(p.dynamics.Airborne);
                Assert.AreEqual(0, game.World.Frame.Depth, "back in the root region");
                Assert.AreEqual(game.settings.shipRadius, GroundGap(), 0.1f);
                Assert.AreEqual(1f, game.World.Scale, 1e-4f);
            }
            finally { game.settings.escapeDistance = oldLimit; game.settings.gravity = oldGravity; }
        }

        [UnityTest]
        public IEnumerator PerformanceReport_FramesAndChunkCounters()
        {
            yield return Load();
            yield return WaitForMesh();
            Debug.Log($"PERF machine: {SystemInfo.processorType} x{SystemInfo.processorCount}, {SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height}");
            var p = game.Player;
            float sum = 0, worst = 0; int n = 0; int builds0 = worldRenderer.BuildsApplied;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 6f)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime; sum += dt; worst = Mathf.Max(worst, dt); n++;
                p.dynamics.Step(dt, new WalkInput { Forward = 1f, Look = new Vector2(0.8f, 0f) }, p);
            }
            Debug.Log($"PERF frames {n}: avg {sum / n * 1000f:0.00} ms, worst {worst * 1000f:0.00} ms; mesh builds applied {worldRenderer.BuildsApplied - builds0}; " +
                      $"leaves {worldRenderer.Leaves}, faces {worldRenderer.Faces}, tris {worldRenderer.Triangles}, last build {worldRenderer.LastBuildMilliseconds:0.0} ms (worker thread)");
            Assert.Less(sum / n, 0.1f);
        }

        [UnityTest]
        public IEnumerator Screenshots_WhenRequested()
        {
            string dir = System.Environment.GetEnvironmentVariable("MENGER_SHOTS");
            if (string.IsNullOrEmpty(dir)) { Assert.Ignore("set MENGER_SHOTS to a directory to capture screenshots"); yield break; }
            Directory.CreateDirectory(dir);
            LogAssert.ignoreFailingMessages = true; // editor-only resource warnings of the host project
            yield return Load();
            yield return WaitForMesh();
            var cam = Camera.main;
            var p = game.Player;

            IEnumerator Shot(string name)
            {
                yield return new WaitForSeconds(0.5f);
                var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
                var prev = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prev;
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                Object.Destroy(rt); Object.Destroy(tex);
            }

            yield return Shot("01_spawn_on_top_face");
            p.Place(new Vector3(-440, 455, -423), Vector3.up, Vector3.left);
            p.dynamics.Pitch = -20f;
            yield return Shot("02_looking_at_the_edge");
            p.Place(new Vector3(140, -145, 300), Vector3.up, Vector3.right);
            yield return null;
            p.TeleportToAim();
            yield return Shot("03_on_the_tunnel_wall");
        }
    }
}
