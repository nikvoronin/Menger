using UnityEngine;

namespace Menger.Core
{
    /// <summary>
    /// Exact analytic collision against the sponge: nearest-point queries by branch-and-bound over cubic cells.
    /// Independent of any rendered mesh or LOD, so visual LOD can never change what is passable. Cells smaller than the
    /// leaf size are treated as solid cubes (their tunnels are narrower than the ship).
    /// </summary>
    public static class CollisionWorld
    {
        /// <summary>A cell's tunnels are size/3 wide; below 1.5 radii the ship cannot enter, so the cell is a solid leaf.</summary>
        public static float LeafSize(float radius) => radius * 4.5f;

        /// <summary>Distance from p to the nearest solid, or <paramref name="maxDist"/> if nothing is closer. 0 means p is inside solid.</summary>
        public static float Closest(in ContextRoot root, Vector3 p, float maxDist, float leaf, out Vector3 nearest)
        {
            float best2 = maxDist * maxDist;
            Vector3 best = p;
            Visit(root.Center, root.Size, p, leaf, ref best2, ref best);
            nearest = best;
            return Mathf.Sqrt(best2);
        }

        static void Visit(Vector3 c, float size, Vector3 p, float leaf, ref float best2, ref Vector3 best)
        {
            float h = size * 0.5f;
            var q = new Vector3(Mathf.Clamp(p.x, c.x - h, c.x + h), Mathf.Clamp(p.y, c.y - h, c.y + h), Mathf.Clamp(p.z, c.z - h, c.z + h));
            float d2 = (p - q).sqrMagnitude;
            if (d2 >= best2) return;
            if (size <= leaf) { best2 = d2; best = q; return; }
            float cs = size / 3f;
            var offsets = MengerCells.Offsets;
            for (int i = 0; i < offsets.Length; i++)
            {
                var o = offsets[i];
                Visit(new Vector3(c.x + o.x * cs, c.y + o.y * cs, c.z + o.z * cs), cs, p, leaf, ref best2, ref best);
            }
        }

        /// <summary>Sphere-traces the segment a-b; true if it enters solid.</summary>
        public static bool RayBlocked(in ContextRoot root, Vector3 a, Vector3 b, float leaf)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return false;
            return !float.IsInfinity(RayDistance(root, a, d / len, len, leaf));
        }

        /// <summary>Sphere-traces from a along the unit direction d; distance to the first solid within maxLen (the exact face plane when d is an axis), else +Infinity.</summary>
        public static float RayDistance(in ContextRoot root, Vector3 a, Vector3 d, float maxLen, float leaf)
        {
            float t = 0, lastReach = 0; // lastReach: the previous point's distance plus its clearance, a hit never nearer than this (exact for axis rays)
            for (int i = 0; i < 160 && t < maxLen; i++)
            {
                float dist = Closest(root, a + d * t, maxLen - t, leaf, out var q);
                if (dist >= maxLen - t) return float.PositiveInfinity;
                if (dist < 0.05f) return i == 0 ? Vector3.Dot(q - a, d) : lastReach;
                lastReach = t + dist;
                t += dist * 0.999f + 0.02f;
            }
            return float.PositiveInfinity;
        }

        /// <summary>First surface hit by the ray a + t*d (d unit, t up to maxLen): the point on it and the face's outward axis-aligned normal. False if the ray starts inside solid or hits nothing.</summary>
        public static bool RayHit(in ContextRoot root, Vector3 a, Vector3 d, float maxLen, float leaf, out Vector3 point, out Vector3 normal)
        {
            point = default; normal = default;
            float t = 0, lastDist = 0; Vector3 lastOff = default, lastQ = default;
            for (int i = 0; i < 400 && t < maxLen; i++)
            {
                Vector3 p = a + d * t;
                float dist = Closest(root, p, maxLen - t, leaf, out var q);
                if (dist >= maxLen - t) return false;
                Vector3 off = p - q;
                if (dist < 0.05f)
                {
                    if (off.sqrMagnitude < 1e-10f) return false; // started inside solid
                    point = q; normal = DominantAxis(off); return true;
                }
                lastDist = dist; lastOff = off; lastQ = q;
                t += Mathf.Max(dist * 0.999f, 0.01f); // never steps past a surface: distance to the nearest solid is 1-Lipschitz
            }
            // a ray almost parallel to a face converges slowly: accept a near miss
            if (lastDist < 1f && lastOff.sqrMagnitude > 1e-10f) { point = lastQ; normal = DominantAxis(lastOff); return true; }
            return false;
        }

        static Vector3 DominantAxis(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(v.x), 0, 0);
            return ay >= az ? new Vector3(0, Mathf.Sign(v.y), 0) : new Vector3(0, 0, Mathf.Sign(v.z));
        }

        /// <summary>For a point inside solid: the shortest axis-aligned move that reaches free space with the given clearance.</summary>
        public static bool Escape(in ContextRoot root, Vector3 p, float radius, float leaf, out Vector3 delta)
        {
            float bestT = float.MaxValue;
            delta = Vector3.zero;
            float step = Mathf.Max(radius * 0.25f, 0.05f);
            foreach (var d in MengerCells.Directions)
            {
                var dir = new Vector3(d.x, d.y, d.z);
                for (float t = step; t < leaf * 4f && t < bestT; t += step)
                {
                    if (Closest(root, p + dir * t, radius, leaf, out _) >= radius)
                    {
                        bestT = t;
                        delta = dir * t;
                        break;
                    }
                }
            }
            return bestT < float.MaxValue;
        }
    }
}
