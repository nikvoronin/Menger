using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Menger.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace Menger.Rendering
{
    /// <summary>One vertex of a surface chunk: position (frame logic coordinates) and a colour that carries data: r = recursion level * 16, g = face direction (index into <see cref="MengerCells.Directions"/>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SurfaceVertex { public float X, Y, Z; public Color32 Color; }

    /// <summary>Minimal growable unmanaged array for job code (no safety system).</summary>
    public unsafe struct Buf<T> where T : unmanaged
    {
        public T* Ptr;
        public int Length, Capacity;

        public void Add(in T value)
        {
            if (Length == Capacity) Grow();
            Ptr[Length++] = value;
        }

        void Grow()
        {
            int cap = Capacity < 16 ? 16 : Capacity * 2;
            var p = (T*)UnsafeUtility.Malloc((long)cap * sizeof(T), 16, Allocator.Persistent);
            if (Ptr != null) { UnsafeUtility.MemCpy(p, Ptr, (long)Length * sizeof(T)); UnsafeUtility.Free(Ptr, Allocator.Persistent); }
            Ptr = p; Capacity = cap;
        }

        public void Dispose()
        {
            if (Ptr != null) UnsafeUtility.Free(Ptr, Allocator.Persistent);
            this = default;
        }
    }

    /// <summary>A cell that is meshed on its own: a cube of exactly the chunk size, or a bigger cube that stays one coarse leaf.</summary>
    public struct ChunkRoot { public long X, Y, Z, Size; public int Level; }

    /// <summary>What one chunk job produced. The vertex/index memory belongs to the <see cref="MeshBuild"/>.</summary>
    public struct ChunkOut
    {
        public Buf<SurfaceVertex> Vertices;
        public Buf<uint> Indices;
        public int Leaves, Faces, Quads, MaxLevel;
        public uint Hash;        // of the geometry: an unchanged chunk is not uploaded again
        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
    }

    public struct MeshParams
    {
        public double MinX, MinY, MinZ, Unit;
        public long Total, ChunkSize;
        public float CamX, CamY, CamZ, MinLeaf, Ratio, MaxExpand;
        public byte Greedy;
    }

    struct Node { public long X, Y, Z, Size; public int Level; }

    /// <summary>A visible face (or, after merging, a rectangle of equal faces). G packs direction(3) | size index(4) | level(4) | plane(21), K is the sort key within a group.</summary>
    struct Item { public uint G; public ulong K; public int U, V, Nu, Nv; }

    /// <summary>The cube tree on its integer lattice: occupancy, adaptive subdivision, neighbour state. Exactly the rules of <see cref="ChunkMeshBuilder"/>, in unmanaged code.</summary>
    static unsafe class Lattice
    {
        public const int Void = 0, Coarse = 1, Expanded = 2;

        public static bool Retained(int x, int y, int z) => ((x == 0 ? 1 : 0) + (y == 0 ? 1 : 0) + (z == 0 ? 1 : 0)) <= 1;

        public static long Pow3(int j) { long r = 1; for (int i = 0; i < j; i++) r *= 3; return r; }
        public static int SizeIndex(long size) { int j = 0; while (size > 1) { size /= 3; j++; } return j; }

        public static bool Expand(in MeshParams p, long x, long y, long z, long size)
        {
            if (size == p.Total) return true;
            double sz = size * p.Unit;
            if (sz <= p.MinLeaf) return false;
            double h = sz * 0.5;
            float cx = (float)(p.MinX + x * p.Unit + h), cy = (float)(p.MinY + y * p.Unit + h), cz = (float)(p.MinZ + z * p.Unit + h);
            float dx = Math.Max(Math.Abs(p.CamX - cx) - (float)h, 0f), dy = Math.Max(Math.Abs(p.CamY - cy) - (float)h, 0f), dz = Math.Max(Math.Abs(p.CamZ - cz) - (float)h, 0f);
            float d = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return d < p.MaxExpand && sz > p.Ratio * d;
        }

        public static int StateOf(in MeshParams p, long nx, long ny, long nz, long size)
        {
            if (nx < 0 || ny < 0 || nz < 0 || nx >= p.Total || ny >= p.Total || nz >= p.Total) return Void;
            long cx = 0, cy = 0, cz = 0, cs = p.Total;
            while (true)
            {
                if (cs <= size) return Expand(p, cx, cy, cz, cs) ? Expanded : Coarse;
                if (!Expand(p, cx, cy, cz, cs)) return Coarse;
                long s3 = cs / 3;
                int ix = (int)((nx - cx) / s3), iy = (int)((ny - cy) / s3), iz = (int)((nz - cz) / s3);
                if (!Retained(ix - 1, iy - 1, iz - 1)) return Void;
                cx += ix * s3; cy += iy * s3; cz += iz * s3;
                cs = s3;
            }
        }

        public static int PushChildren(Node* stack, int sp, in Node n)
        {
            long cs = n.Size / 3;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!Retained(dx, dy, dz)) continue;
                stack[sp++] = new Node { X = n.X + (dx + 1) * cs, Y = n.Y + (dy + 1) * cs, Z = n.Z + (dz + 1) * cs, Size = cs, Level = n.Level + 1 };
            }
            return sp;
        }
    }

    /// <summary>Walks the cube tree down to chunk-sized cells (or coarse leaves bigger than a chunk) and lists them.</summary>
    [BurstCompile(CompileSynchronously = true)]
    unsafe struct CollectChunksJob : IJob
    {
        public MeshParams P;
        [NativeDisableUnsafePtrRestriction] public Buf<ChunkRoot>* Roots;

        public void Execute()
        {
            Node* stack = stackalloc Node[400];
            int sp = 0;
            stack[sp++] = new Node { Size = P.Total };
            while (sp > 0)
            {
                Node n = stack[--sp];
                if (n.Size > P.ChunkSize && Lattice.Expand(P, n.X, n.Y, n.Z, n.Size)) sp = Lattice.PushChildren(stack, sp, n);
                else Roots->Add(new ChunkRoot { X = n.X, Y = n.Y, Z = n.Z, Size = n.Size, Level = n.Level });
            }
        }
    }

    /// <summary>Meshes one chunk: leaves -> visible faces -> (greedy) rectangles -> vertices and indices.</summary>
    [BurstCompile(CompileSynchronously = true)]
    unsafe struct BuildChunkJob : IJobParallelFor
    {
        public MeshParams P;
        [NativeDisableUnsafePtrRestriction] public ChunkRoot* Roots;
        [NativeDisableUnsafePtrRestriction] public ChunkOut* Outs;

        public void Execute(int index)
        {
            ChunkRoot r = Roots[index];
            ChunkOut o = default;
            Buf<Item> faces = default;
            Node* stack = stackalloc Node[400];
            int sp = 0;
            stack[sp++] = new Node { X = r.X, Y = r.Y, Z = r.Z, Size = r.Size, Level = r.Level };
            while (sp > 0)
            {
                Node n = stack[--sp];
                if (Lattice.Expand(P, n.X, n.Y, n.Z, n.Size)) { sp = Lattice.PushChildren(stack, sp, n); continue; }
                o.Leaves++;
                if (n.Level > o.MaxLevel) o.MaxLevel = n.Level;
                int j = Lattice.SizeIndex(n.Size);
                for (int d = 0; d < 6; d++)
                {
                    int axis = d >> 1, sign = (d & 1) == 0 ? 1 : -1;
                    long dx = axis == 0 ? sign : 0, dy = axis == 1 ? sign : 0, dz = axis == 2 ? sign : 0;
                    // hidden only against another solid cube; a refined neighbour needs this face as the cap of its tunnels
                    if (Lattice.StateOf(P, n.X + dx * n.Size, n.Y + dy * n.Size, n.Z + dz * n.Size, n.Size) == Lattice.Coarse) continue;
                    int a1 = (axis + (sign > 0 ? 1 : 2)) % 3, a2 = (axis + (sign > 0 ? 2 : 1)) % 3; // a1 x a2 = normal
                    long plane = Pick(axis, n) + (sign > 0 ? n.Size : 0);
                    long u = Pick(a1, n), v = Pick(a2, n);
                    uint g = ((uint)d << 29) | ((uint)j << 25) | ((uint)Math.Min(n.Level, 15) << 21) | (uint)plane;
                    faces.Add(new Item { G = g, K = ((ulong)(uint)v << 21) | (uint)u, U = (int)u, V = (int)v, Nu = 1, Nv = 1 });
                    o.Faces++;
                }
            }

            Buf<Item> runs = default, merged = default;
            Item* rects = faces.Ptr;
            int rectCount = faces.Length;
            if (P.Greedy != 0 && faces.Length > 1)
            {
                Sort(faces.Ptr, faces.Length);
                // 1) runs of equal neighbours along u
                int i = 0;
                while (i < faces.Length)
                {
                    Item f = faces.Ptr[i];
                    long size = Lattice.Pow3((int)((f.G >> 25) & 15));
                    int nu = 1, k = i + 1;
                    while (k < faces.Length && faces.Ptr[k].G == f.G && faces.Ptr[k].V == f.V && faces.Ptr[k].U == f.U + nu * size) { nu++; k++; }
                    f.Nu = nu;
                    f.K = ((ulong)(uint)f.U << 42) | ((ulong)(uint)nu << 21) | (uint)f.V;
                    runs.Add(f);
                    i = k;
                }
                // 2) stack equal runs along v
                Sort(runs.Ptr, runs.Length);
                for (int q = 0; q < runs.Length; q++)
                {
                    Item cur = runs.Ptr[q];
                    if (merged.Length > 0)
                    {
                        Item* last = &merged.Ptr[merged.Length - 1];
                        long size = Lattice.Pow3((int)((cur.G >> 25) & 15));
                        if (last->G == cur.G && last->U == cur.U && last->Nu == cur.Nu && cur.V == last->V + last->Nv * size) { last->Nv++; continue; }
                    }
                    merged.Add(cur);
                }
                rects = merged.Ptr; rectCount = merged.Length;
            }

            for (int q = 0; q < rectCount; q++) EmitRect(ref o, rects[q]);
            o.Quads = rectCount;
            o.MinX = (float)(P.MinX + r.X * P.Unit); o.MinY = (float)(P.MinY + r.Y * P.Unit); o.MinZ = (float)(P.MinZ + r.Z * P.Unit);
            double s = r.Size * P.Unit;
            o.MaxX = (float)(P.MinX + r.X * P.Unit + s); o.MaxY = (float)(P.MinY + r.Y * P.Unit + s); o.MaxZ = (float)(P.MinZ + r.Z * P.Unit + s);
            o.Hash = Hash(o);
            faces.Dispose(); runs.Dispose(); merged.Dispose();
            Outs[index] = o;
        }

        static long Pick(int axis, in Node n) => axis == 0 ? n.X : axis == 1 ? n.Y : n.Z;

        void EmitRect(ref ChunkOut o, in Item it)
        {
            int d = (int)(it.G >> 29), j = (int)((it.G >> 25) & 15), level = (int)((it.G >> 21) & 15);
            long plane = it.G & 0x1FFFFF, size = Lattice.Pow3(j);
            int axis = d >> 1, sign = (d & 1) == 0 ? 1 : -1;
            int a1 = (axis + (sign > 0 ? 1 : 2)) % 3, a2 = (axis + (sign > 0 ? 2 : 1)) % 3;
            long l1 = it.U, h1 = l1 + it.Nu * size, l2 = it.V, h2 = l2 + it.Nv * size;
            var color = new Color32((byte)(level * 16), (byte)d, 0, 255);
            uint i0 = (uint)o.Vertices.Length;
            o.Vertices.Add(Vert(axis, plane, a1, l1, a2, l2, color));
            o.Vertices.Add(Vert(axis, plane, a1, h1, a2, l2, color));
            o.Vertices.Add(Vert(axis, plane, a1, h1, a2, h2, color));
            o.Vertices.Add(Vert(axis, plane, a1, l1, a2, h2, color));
            o.Indices.Add(i0); o.Indices.Add(i0 + 1); o.Indices.Add(i0 + 2);
            o.Indices.Add(i0); o.Indices.Add(i0 + 2); o.Indices.Add(i0 + 3);
        }

        SurfaceVertex Vert(int axis, long plane, int a1, long c1, int a2, long c2, Color32 color)
        {
            double* c = stackalloc double[3];
            c[axis] = Min(axis) + plane * P.Unit;
            c[a1] = Min(a1) + c1 * P.Unit;
            c[a2] = Min(a2) + c2 * P.Unit;
            return new SurfaceVertex { X = (float)c[0], Y = (float)c[1], Z = (float)c[2], Color = color };
        }

        double Min(int axis) => axis == 0 ? P.MinX : axis == 1 ? P.MinY : P.MinZ;

        static uint Hash(in ChunkOut o)
        {
            uint h = 2166136261u;
            uint* v = (uint*)o.Vertices.Ptr;
            for (int i = 0; i < o.Vertices.Length * 4; i++) h = (h ^ v[i]) * 16777619u;
            for (int i = 0; i < o.Indices.Length; i++) h = (h ^ o.Indices.Ptr[i]) * 16777619u;
            return h | 1u;
        }

        static bool Less(in Item x, in Item y) => x.G != y.G ? x.G < y.G : x.K < y.K;

        // stable bottom-up merge sort
        static void Sort(Item* a, int n)
        {
            if (n < 2) return;
            Item* tmp = (Item*)UnsafeUtility.Malloc((long)n * sizeof(Item), 16, Allocator.Persistent);
            Item* src = a, dst = tmp;
            for (int w = 1; w < n; w <<= 1)
            {
                for (int lo = 0; lo < n; lo += 2 * w)
                {
                    int mid = Math.Min(lo + w, n), hi = Math.Min(lo + 2 * w, n), i = lo, j = mid, k = lo;
                    while (i < mid && j < hi) dst[k++] = Less(src[j], src[i]) ? src[j++] : src[i++];
                    while (i < mid) dst[k++] = src[i++];
                    while (j < hi) dst[k++] = src[j++];
                }
                Item* t = src; src = dst; dst = t;
            }
            if (src != a) UnsafeUtility.MemCpy(a, src, (long)n * sizeof(Item));
            UnsafeUtility.Free(tmp, Allocator.Persistent);
        }
    }

    /// <summary>
    /// The surface mesher of the game: the same rules as <see cref="ChunkMeshBuilder"/> (adaptive LOD cube tree, a face is dropped only between two
    /// solid cubes, coarse faces cap the tunnels of refined neighbours), but in Burst jobs and in pieces. The tree is cut into chunks (cubes of
    /// rootSize / <see cref="ChunkDivisions"/>); all chunks are built in parallel from one camera snapshot, so their borders always agree; in each
    /// chunk equal coplanar faces are merged greedily into rectangles. Output is raw vertex/index memory per chunk (16 bytes per vertex, no normals).
    /// </summary>
    public sealed unsafe class MeshBuild : IDisposable
    {
        public const int ChunkDivisions = 3;

        MeshParams p;
        Buf<ChunkRoot>* roots;
        ChunkOut* outs;
        int count;
        JobHandle handle;
        readonly Stopwatch clock = Stopwatch.StartNew();
        bool completed, disposed;

        public int Count => count;
        public bool IsCompleted => handle.IsCompleted;
        /// <summary>Wall time from scheduling to the moment the result was taken (includes waiting to be polled).</summary>
        public double Milliseconds { get; private set; }
        public double Unit => p.Unit;
        public ChunkRoot Root(int i) => roots->Ptr[i];
        public ref ChunkOut Out(int i) => ref outs[i];

        public static MeshBuild Schedule(in ContextRoot root, Vector3 camera, in LodParams lod, bool greedy, float chunkWorldSize)
        {
            var b = new MeshBuild { p = MakeParams(root, camera, lod, greedy, chunkWorldSize) };
            b.roots = (Buf<ChunkRoot>*)UnsafeUtility.Malloc(sizeof(Buf<ChunkRoot>), 16, Allocator.Persistent);
            *b.roots = default;
            // the chunk cells are found first (a few hundred nodes: cheap, done in place), then every chunk is a parallel job
            new CollectChunksJob { P = b.p, Roots = b.roots }.Run();
            b.count = b.roots->Length;
            long bytes = (long)Math.Max(1, b.count) * sizeof(ChunkOut);
            b.outs = (ChunkOut*)UnsafeUtility.Malloc(bytes, 16, Allocator.Persistent);
            UnsafeUtility.MemClear(b.outs, bytes);
            b.handle = new BuildChunkJob { P = b.p, Roots = b.roots->Ptr, Outs = b.outs }.Schedule(b.count, 1);
            return b;
        }

        static MeshParams MakeParams(in ContextRoot r, Vector3 camera, in LodParams l, bool greedy, float chunkWorldSize)
        {
            int m = Mathf.Max(0, Mathf.RoundToInt(Mathf.Log(r.Size / chunkWorldSize, 3f)));
            long min3 = 1; for (int i = 0; i < m; i++) min3 *= 3;
            // a lattice fine enough that every cell down to MinLeaf is a whole number of lattice units, and a whole number of chunks
            long total = 1;
            double size = r.Size;
            while ((size / total > l.MinLeaf / 3.0 || total < min3) && total < 1594323L) total *= 3; // 3^13
            return new MeshParams
            {
                Total = total, ChunkSize = total / min3, Unit = r.Size / (double)total,
                MinX = r.Center.x - r.Size * 0.5, MinY = r.Center.y - r.Size * 0.5, MinZ = r.Center.z - r.Size * 0.5,
                CamX = camera.x, CamY = camera.y, CamZ = camera.z, MinLeaf = l.MinLeaf, Ratio = l.Ratio, MaxExpand = l.MaxExpandDistance,
                Greedy = (byte)(greedy ? 1 : 0),
            };
        }

        public void Complete()
        {
            if (completed) return;
            handle.Complete();
            Milliseconds = clock.Elapsed.TotalMilliseconds;
            completed = true;
        }

        /// <summary>Safe copies for tests and tools.</summary>
        public SurfaceVertex[] VerticesOf(int i)
        {
            var a = new SurfaceVertex[outs[i].Vertices.Length];
            for (int k = 0; k < a.Length; k++) a[k] = outs[i].Vertices.Ptr[k];
            return a;
        }

        public uint[] IndicesOf(int i)
        {
            var a = new uint[outs[i].Indices.Length];
            for (int k = 0; k < a.Length; k++) a[k] = outs[i].Indices.Ptr[k];
            return a;
        }

        public void Dispose()
        {
            if (disposed) return;
            Complete();
            for (int i = 0; i < count; i++) { outs[i].Vertices.Dispose(); outs[i].Indices.Dispose(); }
            UnsafeUtility.Free(outs, Allocator.Persistent);
            roots->Dispose();
            UnsafeUtility.Free(roots, Allocator.Persistent);
            disposed = true;
        }
    }
}
