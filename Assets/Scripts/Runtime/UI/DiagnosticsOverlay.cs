using Menger.Core;
using Menger.Flight;
using Menger.Rendering;
using UnityEngine;

namespace Menger.UI
{
    /// <summary>Debug overlay: performance, region address, transition state, chunk/mesh counters, fog vs. stopping distance.</summary>
    public sealed class DiagnosticsOverlay : MonoBehaviour
    {
        MengerSettings cfg;
        WorldState world;
        WalkerController ship;
        WorldRenderer worldRenderer;
        NavigationHud hud;
        public bool visible = true;
        public float Visibility;
        float smoothDt = 0.016f, worstDt;
        float worstTimer;
        string text = "";
        float nextText;

        public void Init(MengerSettings s, WorldState w, WalkerController sh, WorldRenderer r, NavigationHud h)
        {
            cfg = s; world = w; ship = sh; worldRenderer = r; hud = h; visible = s.showOverlay;
        }

        void Update()
        {
            if (world == null) return;
            float dt = Time.unscaledDeltaTime;
            smoothDt = Mathf.Lerp(smoothDt, dt, 0.05f);
            worstDt = Mathf.Max(worstDt, dt);
            worstTimer += dt;
            if (worstTimer > 3f) { worstTimer = 0; worstDt = dt; }

            if (!visible || Time.unscaledTime < nextText) return;
            nextText = Time.unscaledTime + 0.25f;
            float stop = FogModel.StoppingDistance(cfg);
            string lockText = world.HasLock ? $"{(world.Lock.Reverse ? "reverse" : "forward")} {world.Lock}" : "none";
            string sel = worldRenderer.SelectedEntranceKey >= 0 ? worldRenderer.SelectedEntranceKey.ToString() : "-";
            int forward = 0; foreach (var e in world.Entrances) if (!e.Reverse) forward++;
            text =
                $"FPS {1f / smoothDt:0}  frame {smoothDt * 1000f:0.0} ms (worst {worstDt * 1000f:0.0})\n" +
                $"region {world.Frame}  depth {world.Frame.Depth}  continuousDepth {world.ContinuousDepth:0.000}  flips {world.FlipCount}\n" +
                $"scale x{world.Scale:0.000}  exponent {world.Exponent:+0.000;-0.000;0.000}  lock {lockText}\n" +
                $"entrances: {forward} forward, {world.Entrances.Length - forward} reverse  nearest key {sel}\n" +
                $"walking speed {ship.dynamics.Speed:0.0}  up {ship.dynamics.Up}  {(ship.dynamics.Airborne ? "airborne" : "grounded")}  aim {(ship.HasAim ? ship.AimPoint.ToString() : "-")}  contacts {ship.dynamics.LastContactCount}\n" +
                $"mesh: leaves {worldRenderer.Leaves}  faces {worldRenderer.Faces} -> quads {worldRenderer.Quads}  tris {worldRenderer.Triangles}  chunks {worldRenderer.Chunks} (uploaded {worldRenderer.ChunksUploaded})  maxLevel {worldRenderer.MaxLevel}  " +
                $"build {worldRenderer.LastBuildMilliseconds:0.0} ms  applied {worldRenderer.BuildsApplied}  {(worldRenderer.BuildInFlight ? "[building]" : "")}\n" +
                $"outlines/colliders: analytic collision (no collider objects)  arrows {hud.VisibleArrowCount}\n" +
                $"fog distance {Visibility:0}  stopping distance {stop:0}  margin x{Visibility / stop:0.0}";
        }

        void OnGUI()
        {
            if (!visible || world == null) return;
            GUI.Box(new Rect(8, 8, 980, 150), GUIContent.none);
            GUI.Label(new Rect(14, 10, 970, 146), text);
        }
    }
}
