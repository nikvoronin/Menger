using System.Collections.Generic;
using Menger.Core;
using UnityEngine;

namespace Menger.Rendering
{
    public sealed class SurfaceMeshData
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Color32> Colors = new List<Color32>();
        public readonly List<int> Triangles = new List<int>();
        public int Leaves, Faces, MaxLevel;
        public double BuildMilliseconds;
    }

    public struct LodParams
    {
        public float Ratio;          // expand while size > Ratio * distance
        public float MinLeaf;        // never subdivide below this size
        public float MaxExpandDistance; // do not refine beyond the fog range
    }

    /// <summary>
    /// Logical cubes -> renderable surface. Adaptive recursive LOD by projected size (size/distance): a cell is either expanded
    /// into its 20 children or drawn as an exact solid cube of that level. Faces between solid cubes are never emitted.
    /// At a LOD border a coarse cube facing a refined neighbour emits its whole face, which becomes the cap of the neighbour's
    /// tunnel mouths, so the border has neither holes nor duplicate surfaces. Cells live on an exact integer lattice, so shared
    /// vertices are bit-identical floats (no cracks). Pure managed code: safe on worker threads.
    /// </summary>
    public static class ChunkMeshBuilder
    {
        enum Neighbor { Void, Coarse, Expanded }

        public static SurfaceMeshData Build(in ContextRoot root, Vector3 camera, in LodParams lod)
        {
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            var b = new Builder(root, camera, lod);
            b.Visit(0, 0, 0, b.Total, 0);
            b.Data.BuildMilliseconds = t0.Elapsed.TotalMilliseconds;
            return b.Data;
        }

        sealed class Builder
        {
            readonly ContextRoot root;
            readonly Vector3 cam;
            readonly LodParams lod;
            readonly double unit, minX, minY, minZ;
            readonly double[] mins, tmp = new double[3];
            public readonly long Total;
            public readonly SurfaceMeshData Data = new SurfaceMeshData();

            public Builder(in ContextRoot r, Vector3 camera, in LodParams l)
            {
                root = r; cam = camera; lod = l;
                // lattice fine enough that every cell down to MinLeaf is an exact integer number of lattice units
                long total = 1;
                double size = r.Size;
                while (size / total > l.MinLeaf / 3.0 && total < 1594323L) total *= 3; // 3^13
                Total = total;
                unit = r.Size / (double)total;
                minX = r.Center.x - r.Size * 0.5; minY = r.Center.y - r.Size * 0.5; minZ = r.Center.z - r.Size * 0.5;
                mins = new[] { minX, minY, minZ };
            }

            bool Expand(long x, long y, long z, long size)
            {
                if (size == Total) return true;
                double sz = size * unit;
                if (sz <= lod.MinLeaf) return false;
                double h = sz * 0.5;
                float cx = (float)(minX + x * unit + h), cy = (float)(minY + y * unit + h), cz = (float)(minZ + z * unit + h);
                float dx = Mathf.Max(Mathf.Abs(cam.x - cx) - (float)h, 0f), dy = Mathf.Max(Mathf.Abs(cam.y - cy) - (float)h, 0f), dz = Mathf.Max(Mathf.Abs(cam.z - cz) - (float)h, 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                return d < lod.MaxExpandDistance && sz > lod.Ratio * d;
            }

            public void Visit(long x, long y, long z, long size, int level)
            {
                if (Expand(x, y, z, size))
                {
                    long cs = size / 3;
                    foreach (var o in MengerCells.Offsets) Visit(x + (o.x + 1) * cs, y + (o.y + 1) * cs, z + (o.z + 1) * cs, cs, level + 1);
                }
                else EmitCube(x, y, z, size, level);
            }

            Neighbor StateOf(long nx, long ny, long nz, long size)
            {
                if (nx < 0 || ny < 0 || nz < 0 || nx >= Total || ny >= Total || nz >= Total) return Neighbor.Void;
                long cx = 0, cy = 0, cz = 0, cs = Total;
                while (true)
                {
                    if (cs <= size) return Expand(cx, cy, cz, cs) ? Neighbor.Expanded : Neighbor.Coarse;
                    if (!Expand(cx, cy, cz, cs)) return Neighbor.Coarse;
                    long s3 = cs / 3;
                    int ix = (int)((nx - cx) / s3), iy = (int)((ny - cy) / s3), iz = (int)((nz - cz) / s3);
                    if (!MengerCells.IsRetained(ix - 1, iy - 1, iz - 1)) return Neighbor.Void;
                    cx += ix * s3; cy += iy * s3; cz += iz * s3;
                    cs = s3;
                }
            }

            void EmitCube(long x, long y, long z, long size, int level)
            {
                Data.Leaves++;
                if (level > Data.MaxLevel) Data.MaxLevel = level;
                Color32 color = LevelColor(level);
                for (int d = 0; d < 6; d++)
                {
                    var dir = MengerCells.Directions[d];
                    // hidden only against another solid cube; a refined neighbour needs this face as the cap of its tunnels
                    if (StateOf(x + dir.x * size, y + dir.y * size, z + dir.z * size, size) == Neighbor.Coarse) continue;
                    EmitFace(x, y, z, size, d >> 1, dir.x + dir.y + dir.z, new Vector3(dir.x, dir.y, dir.z), color);
                }
            }

            void EmitFace(long x, long y, long z, long size, int axis, int sign, Vector3 normal, Color32 color)
            {
                int a1 = (axis + (sign > 0 ? 1 : 2)) % 3, a2 = (axis + (sign > 0 ? 2 : 1)) % 3; // a1 x a2 = normal
                long plane = Pick(axis, x, y, z) + (sign > 0 ? size : 0);
                long l1 = Pick(a1, x, y, z), h1 = l1 + size, l2 = Pick(a2, x, y, z), h2 = l2 + size;
                int i0 = Data.Vertices.Count;
                Data.Vertices.Add(Point(axis, plane, a1, l1, a2, l2));
                Data.Vertices.Add(Point(axis, plane, a1, h1, a2, l2));
                Data.Vertices.Add(Point(axis, plane, a1, h1, a2, h2));
                Data.Vertices.Add(Point(axis, plane, a1, l1, a2, h2));
                for (int k = 0; k < 4; k++) { Data.Normals.Add(normal); Data.Colors.Add(color); }
                Data.Triangles.Add(i0); Data.Triangles.Add(i0 + 1); Data.Triangles.Add(i0 + 2);
                Data.Triangles.Add(i0); Data.Triangles.Add(i0 + 2); Data.Triangles.Add(i0 + 3);
                Data.Faces++;
            }

            static long Pick(int axis, long x, long y, long z) => axis == 0 ? x : axis == 1 ? y : z;

            Vector3 Point(int axisA, long a, int axisB, long b, int axisC, long c)
            {
                tmp[axisA] = mins[axisA] + a * unit;
                tmp[axisB] = mins[axisB] + b * unit;
                tmp[axisC] = mins[axisC] + c * unit;
                return new Vector3((float)tmp[0], (float)tmp[1], (float)tmp[2]);
            }

            // the recursion level, 16 per level in the red channel; the surface shader maps it to the palette from MengerSettings
            static Color32 LevelColor(int level) => new Color32((byte)Mathf.Clamp(level * 16, 0, 240), 0, 0, 255);
        }
    }
}
