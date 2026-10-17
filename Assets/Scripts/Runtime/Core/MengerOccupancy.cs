using UnityEngine;

namespace Menger.Core
{
    /// <summary>Pure occupancy of the (modified) Menger sponge: a point lies in the sponge iff every cell on its descent path is retained.</summary>
    public static class MengerOccupancy
    {
        /// <summary>
        /// True if <paramref name="p"/> is inside a retained cell at every level down to cells of size ~<paramref name="stopSize"/>.
        /// Outside the context cube is empty space.
        /// </summary>
        public static bool IsSolidPoint(in ContextRoot root, Vector3 p, float stopSize)
        {
            float h = root.Size * 0.5f;
            Vector3 c = root.Center;
            if (Mathf.Abs(p.x - c.x) > h || Mathf.Abs(p.y - c.y) > h || Mathf.Abs(p.z - c.z) > h) return false;
            float size = root.Size;
            while (size > stopSize * 1.5f)
            {
                float cs = size / 3f;
                int ix = Mathf.Clamp(Mathf.FloorToInt((p.x - c.x) / cs + 1.5f), 0, 2) - 1;
                int iy = Mathf.Clamp(Mathf.FloorToInt((p.y - c.y) / cs + 1.5f), 0, 2) - 1;
                int iz = Mathf.Clamp(Mathf.FloorToInt((p.z - c.z) / cs + 1.5f), 0, 2) - 1;
                if (!MengerCells.IsRetained(ix, iy, iz)) return false;
                c += new Vector3(ix, iy, iz) * cs;
                size = cs;
            }
            return true;
        }

        /// <summary>Whether the cell of the given size centred at <paramref name="cellCenter"/> is retained (coarse test).</summary>
        public static bool IsCellRetained(in ContextRoot root, Vector3 cellCenter, float cellSize) =>
            IsSolidPoint(root, cellCenter, cellSize);
    }
}
