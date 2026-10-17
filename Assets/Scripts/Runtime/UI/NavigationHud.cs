using System.Collections.Generic;
using Menger.Core;
using UnityEngine;

namespace Menger.UI
{
    /// <summary>
    /// Screen-edge triangles pointing at the nearest transition entrances that are not currently visible in the frame.
    /// Consumes the world's entrance descriptors; never computes its own list. IMGUI keeps it dependency-free.
    /// </summary>
    public sealed class NavigationHud : MonoBehaviour
    {
        MengerSettings cfg;
        WorldState world;
        Camera cam;
        Transform ship;
        readonly HudTargets targets = new HudTargets();
        readonly Dictionary<int, float> alpha = new Dictionary<int, float>();
        readonly Dictionary<int, Vector2> lastDir = new Dictionary<int, Vector2>();
        readonly List<(int key, Vector2 pos, float angle, float a)> draw = new List<(int, Vector2, float, float)>();
        Texture2D triangle;
        float nextTargets;                                    // target choice and the (expensive) line-of-sight test run 10 times a second
        readonly Dictionary<int, bool> blocked = new Dictionary<int, bool>();
        readonly HashSet<int> wanted = new HashSet<int>();
        readonly List<int> stale = new List<int>();
        public float Visibility = 1000f;
        public int VisibleArrowCount => draw.Count;
        public bool showHelp = true;

        public void Init(MengerSettings s, WorldState w, Camera c, Transform shipTransform)
        {
            cfg = s; world = w; cam = c; ship = shipTransform;
            triangle = MakeTriangle(64);
        }

        void OnDestroy() { if (triangle != null) Destroy(triangle); }

        void Update()
        {
            if (world == null) return;
            draw.Clear();

            Vector3 shipLogic = world.DisplayToLogic(ship.position);
            float scale = world.Scale;
            float range = (Visibility + cfg.targetRangeMargin) / scale; // logic units
            bool tick = Time.unscaledTime >= nextTargets;
            if (tick)
            {
                nextTargets = Time.unscaledTime + 0.1f;
                targets.Update(world.Entrances, e => (e.MouthCenter - shipLogic).magnitude, range, cfg.maxArrows, cfg.arrowSwapHysteresis, exits: true); // arrows lead to the exits: the mouths back to the parent region
            }

            var screen = new Vector2(Screen.width, Screen.height);
            Vector3 camLogic = world.DisplayToLogic(cam.transform.position);
            float fade = world.HasLock ? 1f - Mathf.Abs(world.Exponent) : 1f;
            wanted.Clear();

            foreach (int key in targets.Active)
            {
                Entrance en = default; bool found = false;
                foreach (var e in world.Entrances) if (e.Reverse && e.Key == key) { en = e; found = true; break; }
                if (!found) continue;

                Vector3 mouthLogic = en.MouthCenter;
                Vector3 camSpace = cam.transform.InverseTransformPoint(world.LogicToDisplay(mouthLogic));
                // visible = inside the frame (with a stable zone: hysteresis) and not behind a wall
                float margin = alpha.TryGetValue(key, out var a0) && a0 > 0.5f ? -12f : 12f; // arrow shown -> must be clearly in frame to hide
                bool inFrame = HudMath.InFrame(camSpace, cam.fieldOfView, screen, cfg.arrowInset + margin);
                if (tick || !blocked.TryGetValue(key, out bool isBlocked)) { isBlocked = world.RayBlocked(cam.transform.position, world.LogicToDisplay(mouthLogic)); blocked[key] = isBlocked; }
                bool visible = inFrame && !isBlocked;

                float target = visible ? 0f : 1f;
                float cur = alpha.TryGetValue(key, out var a) ? a : 0f;
                cur = Mathf.MoveTowards(cur, target, Time.deltaTime * 6f);
                alpha[key] = cur;
                wanted.Add(key);
                if (cur <= 0.01f) continue;

                lastDir.TryGetValue(key, out var prev);
                HudMath.EdgeArrow(camSpace, screen, cfg.arrowInset, prev, out var pos, out var angle);
                lastDir[key] = pos.normalized;
                draw.Add((key, pos, angle, cur * fade));
            }
            // forget state of targets that left the list
            stale.Clear();
            foreach (var k in alpha.Keys) if (!wanted.Contains(k)) stale.Add(k);
            foreach (var k in stale) { alpha.Remove(k); lastDir.Remove(k); blocked.Remove(k); }
        }

        void OnGUI()
        {
            if (world == null) return;
            var old = GUI.color;
            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            foreach (var (key, pos, angle, a) in draw)
            {
                Vector2 p = new Vector2(c.x + pos.x, c.y - pos.y); // GUI y points down
                var col = cfg.reverseOutline; col.a = a * 0.9f; // the exits are outlined in the same colour
                GUI.color = col;
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(angle, p);
                GUI.DrawTexture(new Rect(p.x - 7, p.y - 7, 14, 14), triangle);
                GUI.matrix = m;
            }
            GUI.color = old;
            GUI.DrawTexture(new Rect(c.x - 2, c.y - 2, 4, 4), Texture2D.whiteTexture); // view centre: where the aim ring is cast

            if (showHelp)
            {
                GUI.Label(new Rect(12, Screen.height - 74, 760, 70),
                    "Mouse look | WASD walk | Space jump (clears tunnels) | click: go to the ringed point | Esc free cursor, click to capture\n" +
                    "F1 overlay | F2 help | R back to the start (diagnostic) | F10 quit");
            }
        }

        static Texture2D MakeTriangle(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // upward-pointing triangle: apex at top, base at bottom
                float v = y / (float)(n - 1), u = x / (float)(n - 1);
                float half = 0.5f * (1f - v); // half width shrinks towards the apex (v=1)
                bool inside = Mathf.Abs(u - 0.5f) <= half && v < 0.98f;
                px[y * n + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }
    }
}
