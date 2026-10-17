using System.Collections.Generic;
using UnityEngine;

namespace Menger.Core
{
    /// <summary>A cube (the root sponge's or any recursive child's) in the unit frame: size = root size, centre = origin.</summary>
    public readonly struct ContextRoot
    {
        public readonly Vector3 Center;
        public readonly float Size;
        public ContextRoot(Vector3 center, float size) { Center = center; Size = size; }
    }

    /// <summary>
    /// Identity of a recursive region: the sequence of child indices from the root. Nodes are interned per parent,
    /// so reference equality is identity and the path grows dynamically (no depth limit, no accumulated float scale).
    /// Child frames are never rotated: a child's axes equal its parent's.
    /// </summary>
    public sealed class RegionAddress
    {
        public static readonly RegionAddress Root = new RegionAddress(null, -1);

        RegionAddress[] children;
        public RegionAddress Parent { get; }
        public int ChildIndex { get; }
        public int Depth { get; }

        RegionAddress(RegionAddress parent, int childIndex)
        {
            Parent = parent;
            ChildIndex = childIndex;
            Depth = parent == null ? 0 : parent.Depth + 1;
        }

        /// <summary>Offset of this cell inside its parent, in units of this cell's size.</summary>
        public Vector3Int Offset => MengerCells.Offsets[ChildIndex];

        public RegionAddress Child(int index)
        {
            children ??= new RegionAddress[MengerCells.ChildCount];
            return children[index] ??= new RegionAddress(this, index);
        }

        /// <summary>
        /// The cube <paramref name="ancestors"/> levels up (or the true root), in this frame's logic coordinates.
        /// Only the last few indices are used, so the cost and float range are independent of total depth.
        /// </summary>
        public ContextRoot GetContext(int ancestors, float rootSize)
        {
            double cx = 0, cy = 0, cz = 0, size = rootSize;
            var a = this;
            for (int n = 0; a.Parent != null && n < ancestors; n++)
            {
                var o = a.Offset;
                cx -= o.x * size; cy -= o.y * size; cz -= o.z * size;
                size *= 3;
                a = a.Parent;
            }
            return new ContextRoot(new Vector3((float)cx, (float)cy, (float)cz), (float)size);
        }

        /// <summary>Child logic coords -> parent logic coords: x_P = c + x_C / 3 (c = child cube centre in the parent).</summary>
        public static Affine ChildToParent(RegionAddress child, double rootSize)
        {
            var o = child.Offset;
            double c = rootSize / 3.0;
            return new Affine(1.0 / 3.0, new V3d(o.x * c, o.y * c, o.z * c));
        }

        /// <summary>Parent logic coords -> child logic coords: x_C = 3 (x_P - c).</summary>
        public static Affine ParentToChild(RegionAddress child, double rootSize)
        {
            var o = child.Offset;
            double c = rootSize / 3.0;
            return new Affine(3.0, new V3d(-3 * o.x * c, -3 * o.y * c, -3 * o.z * c));
        }

        /// <summary>Coordinate conversion between any two frames (goes up to the common ancestor, then down).</summary>
        public static Affine Convert(RegionAddress from, RegionAddress to, double rootSize)
        {
            if (from == to) return Affine.Identity;
            var up = Affine.Identity;
            var down = new List<RegionAddress>();
            RegionAddress a = from, b = to;
            while (a.Depth > b.Depth) { up = ChildToParent(a, rootSize).After(up); a = a.Parent; }
            while (b.Depth > a.Depth) { down.Add(b); b = b.Parent; }
            while (a != b)
            {
                up = ChildToParent(a, rootSize).After(up); a = a.Parent;
                down.Add(b); b = b.Parent;
            }
            var result = up;
            for (int i = down.Count - 1; i >= 0; i--) result = ParentToChild(down[i], rootSize).After(result);
            return result;
        }

        /// <summary>Child indices from the root to this region (root excluded).</summary>
        public int[] Path()
        {
            var path = new int[Depth];
            for (var a = this; a.Parent != null; a = a.Parent) path[a.Depth - 1] = a.ChildIndex;
            return path;
        }

        public override string ToString()
        {
            if (Depth == 0) return "root";
            var p = Path();
            int from = Mathf.Max(0, p.Length - 10);
            // (string.Join(sep, int[], start, count) would bind to the params object[] overload and print "System.Int32[]/..")
            return (from > 0 ? $"..({from})/" : "") + string.Join("/", new System.ArraySegment<int>(p, from, p.Length - from));
        }
    }
}
