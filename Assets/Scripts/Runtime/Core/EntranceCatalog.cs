using System.Collections.Generic;
using UnityEngine;

namespace Menger.Core
{
    /// <summary>
    /// Stable descriptor of one transition mouth. Forward: the face-centre (second-order) tunnel of a level-1 cube that opens
    /// onto empty space. Reverse: the frame cube's own face-centre mouth that opens onto empty space (the way back to the parent).
    /// The outline, the HUD and the scale-transition controller all consume this same descriptor.
    /// </summary>
    public readonly struct Entrance
    {
        public readonly RegionAddress Frame;
        public readonly bool Reverse;
        public readonly int ChildIndex;       // forward only
        public readonly int DirectionIndex;   // index into MengerCells.Directions
        public readonly Vector3 CubeCenter;   // centre of the cube the mouth belongs to (child cube, or the frame cube = origin)
        public readonly float CubeSize;       // 300 for forward, 900 for reverse (frame units)
        public readonly Vector3 Pivot;        // scaling pivot: on the tunnel axis, a little inside the entrance plane

        public Entrance(RegionAddress frame, bool reverse, int childIndex, int directionIndex, Vector3 cubeCenter, float cubeSize, Vector3 pivot)
        {
            Frame = frame; Reverse = reverse; ChildIndex = childIndex; DirectionIndex = directionIndex;
            CubeCenter = cubeCenter; CubeSize = cubeSize; Pivot = pivot;
        }

        public Vector3Int Normal => MengerCells.Directions[DirectionIndex];
        public Vector3 NormalF { get { var n = Normal; return new Vector3(n.x, n.y, n.z); } }
        public int Axis => DirectionIndex >> 1;
        public Vector3 MouthCenter => CubeCenter + NormalF * (CubeSize * 0.5f);
        public float MouthHalfWidth => CubeSize / 6f;
        public RegionAddress Target => Reverse ? Frame.Parent : Frame.Child(ChildIndex);
        public int Key => (Reverse ? 1000 : 0) + (Reverse ? 0 : ChildIndex * 6) + DirectionIndex;
        public override string ToString() => Reverse ? $"reverse@{Frame}#{DirectionIndex}" : $"{Frame}>{ChildIndex}#{DirectionIndex}";
    }

    public static class EntranceCatalog
    {
        /// <summary>All accessible forward mouths plus (for depth &gt; 0) the reverse mouths of this frame.</summary>
        public static Entrance[] Build(RegionAddress frame, in ContextRoot ctx, float rootSize, float shipRadius, float pivotDepth)
        {
            var list = new List<Entrance>();
            float cell = rootSize / 3f;
            float leaf = CollisionWorld.LeafSize(shipRadius);

            for (int i = 0; i < MengerCells.ChildCount; i++)
            {
                var o = MengerCells.Offsets[i];
                var center = new Vector3(o.x, o.y, o.z) * cell;
                for (int d = 0; d < 6; d++)
                {
                    var n = MengerCells.Directions[d];
                    var nf = new Vector3(n.x, n.y, n.z);
                    // the mouth is an entrance only where it opens onto empty space (a main tunnel or the outside)
                    if (MengerOccupancy.IsCellRetained(ctx, center + nf * cell, cell)) continue;
                    var e = new Entrance(frame, false, i, d, center, cell, center + nf * (cell * 0.5f - pivotDepth));
                    if (IsAccessible(e, ctx, shipRadius, leaf)) list.Add(e);
                }
            }

            if (frame.Depth > 0)
            {
                for (int d = 0; d < 6; d++)
                {
                    var n = MengerCells.Directions[d];
                    var nf = new Vector3(n.x, n.y, n.z);
                    if (MengerOccupancy.IsCellRetained(ctx, nf * rootSize, rootSize)) continue;
                    // the child-frame image of the forward pivot: x3 about the child cube centre
                    list.Add(new Entrance(frame, true, -1, d, Vector3.zero, rootSize, nf * (rootSize * 0.5f - 3f * pivotDepth)));
                }
            }
            return list.ToArray();
        }

        /// <summary>The ship's collision sphere fits along the whole approach, mouth and tunnel up to the cube centre.</summary>
        public static bool IsAccessible(in Entrance e, in ContextRoot ctx, float shipRadius, float leaf)
        {
            float cell = e.CubeSize;
            int steps = 12;
            Vector3 from = e.CubeCenter + e.NormalF * cell;
            Vector3 to = e.CubeCenter;
            for (int s = 0; s <= steps; s++)
            {
                var p = Vector3.Lerp(from, to, s / (float)steps);
                if (CollisionWorld.Closest(ctx, p, shipRadius, leaf, out _) < shipRadius) return false;
            }
            return true;
        }
    }
}
