using UnityEngine;

namespace Menger.Core
{
    /// <summary>The pure 3x3x3 subdivision: 20 retained cells with stable indices; the centre and 6 face centres are removed.</summary>
    public static class MengerCells
    {
        public const int ChildCount = 20;

        /// <summary>Child index -> cell offset in {-1,0,1}^3 (lexicographic order, stable forever).</summary>
        public static readonly Vector3Int[] Offsets = new Vector3Int[ChildCount];
        static readonly int[] indexByCell = new int[27];

        public static readonly Vector3Int[] Directions =
        {
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
        };

        static MengerCells()
        {
            int n = 0;
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                int key = (x + 1) * 9 + (y + 1) * 3 + (z + 1);
                if (IsRetained(x, y, z)) { Offsets[n] = new Vector3Int(x, y, z); indexByCell[key] = n++; }
                else indexByCell[key] = -1;
            }
        }

        /// <summary>Removed cells have at least two zero coordinates (face centres and the centre).</summary>
        public static bool IsRetained(int x, int y, int z) =>
            (x == 0 ? 1 : 0) + (y == 0 ? 1 : 0) + (z == 0 ? 1 : 0) <= 1;

        public static bool IsRetained(Vector3Int o) => IsRetained(o.x, o.y, o.z);

        /// <summary>Child index of a retained offset, or -1 for a removed or out-of-range cell.</summary>
        public static int IndexOf(Vector3Int o)
        {
            if (o.x < -1 || o.x > 1 || o.y < -1 || o.y > 1 || o.z < -1 || o.z > 1) return -1;
            return indexByCell[(o.x + 1) * 9 + (o.y + 1) * 3 + (o.z + 1)];
        }
    }
}
