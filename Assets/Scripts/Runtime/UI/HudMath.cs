using System.Collections.Generic;
using Menger.Core;
using UnityEngine;

namespace Menger.UI
{
    public static class HudMath
    {
        /// <summary>
        /// Edge arrow for a target given in CAMERA space (x right, y up, z forward), so it is correct at any roll and behind the camera.
        /// Returns the arrow position (pixels from the screen centre, y up) on the inset rectangle and its rotation (degrees, 0 = up, clockwise).
        /// </summary>
        public static void EdgeArrow(Vector3 camSpace, Vector2 screenSize, float inset, Vector2 previousDir, out Vector2 position, out float angle)
        {
            Vector2 dir = new Vector2(camSpace.x, camSpace.y);
            if (dir.sqrMagnitude < 1e-6f) dir = previousDir.sqrMagnitude > 0f ? previousDir : Vector2.down; // dead ahead/behind: keep the last direction
            dir.Normalize();
            float hx = screenSize.x * 0.5f - inset, hy = screenSize.y * 0.5f - inset;
            float sx = Mathf.Abs(dir.x) > 1e-5f ? hx / Mathf.Abs(dir.x) : float.MaxValue;
            float sy = Mathf.Abs(dir.y) > 1e-5f ? hy / Mathf.Abs(dir.y) : float.MaxValue;
            position = dir * Mathf.Min(sx, sy);
            angle = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
        }

        /// <summary>True if the camera-space point projects inside the screen rectangle shrunk by <paramref name="margin"/> pixels (negative grows it).</summary>
        public static bool InFrame(Vector3 camSpace, float verticalFovDeg, Vector2 screenSize, float margin)
        {
            if (camSpace.z <= 0.01f) return false;
            float f = 1f / Mathf.Tan(verticalFovDeg * 0.5f * Mathf.Deg2Rad);
            float px = camSpace.x / camSpace.z * f * screenSize.y * 0.5f;
            float py = camSpace.y / camSpace.z * f * screenSize.y * 0.5f;
            return Mathf.Abs(px) < screenSize.x * 0.5f - margin && Mathf.Abs(py) < screenSize.y * 0.5f - margin;
        }
    }

    /// <summary>Picks up to N nearest mouths of the current frame with swap hysteresis: the exits (reverse mouths, the way back to the parent region) or the descent mouths.</summary>
    public sealed class HudTargets
    {
        readonly List<int> active = new List<int>();
        public IReadOnlyList<int> Active => active;

        /// <param name="entrances">current frame's descriptors</param>
        /// <param name="distanceOf">distance of entrance i from the ship</param>
        public void Update(Entrance[] entrances, System.Func<Entrance, float> distanceOf, float range, int max, float swapRatio, bool exits)
        {
            var byKey = new Dictionary<int, (Entrance e, float d)>();
            foreach (var e in entrances)
            {
                if (e.Reverse != exits) continue;
                float d = distanceOf(e);
                if (d <= range) byKey[e.Key] = (e, d);
            }
            active.RemoveAll(k => !byKey.ContainsKey(k));

            var candidates = new List<KeyValuePair<int, (Entrance e, float d)>>(byKey);
            candidates.Sort((a, b) => a.Value.d.CompareTo(b.Value.d));
            foreach (var c in candidates)
            {
                if (active.Contains(c.Key)) continue;
                if (active.Count < max) { active.Add(c.Key); continue; }
                int farthest = 0;
                for (int i = 1; i < active.Count; i++) if (byKey[active[i]].d > byKey[active[farthest]].d) farthest = i;
                if (c.Value.d < byKey[active[farthest]].d * swapRatio) active[farthest] = c.Key;
            }
            // trim if max shrank
            while (active.Count > max) active.RemoveAt(active.Count - 1);
        }
    }
}
