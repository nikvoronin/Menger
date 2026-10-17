using UnityEngine;

namespace Menger.Core
{
    /// <summary>Double-precision vector for frame bookkeeping (origins, pivots); geometry queries stay in float.</summary>
    public readonly struct V3d
    {
        public readonly double X, Y, Z;
        public static readonly V3d Zero = new V3d(0, 0, 0);

        public V3d(double x, double y, double z) { X = x; Y = y; Z = z; }
        public V3d(Vector3 v) : this(v.x, v.y, v.z) { }

        public static V3d operator +(V3d a, V3d b) => new V3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3d operator -(V3d a, V3d b) => new V3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3d operator *(V3d a, double s) => new V3d(a.X * s, a.Y * s, a.Z * s);
        public static V3d operator *(double s, V3d a) => a * s;
        public static V3d operator /(V3d a, double s) => new V3d(a.X / s, a.Y / s, a.Z / s);

        public double Magnitude => System.Math.Sqrt(X * X + Y * Y + Z * Z);
        public Vector3 ToVector3() => new Vector3((float)X, (float)Y, (float)Z);
        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }

    /// <summary>Uniform-scale similarity y = Scale * x + Offset. Maps between logic frames and to display space.</summary>
    public readonly struct Affine
    {
        public readonly double Scale;
        public readonly V3d Offset;
        public static readonly Affine Identity = new Affine(1, V3d.Zero);

        public Affine(double scale, V3d offset) { Scale = scale; Offset = offset; }

        public V3d Apply(V3d x) => Scale * x + Offset;
        public V3d Inverse(V3d y) => (y - Offset) / Scale;

        /// <summary>The map x -> this(inner(x)).</summary>
        public Affine After(in Affine inner) => new Affine(Scale * inner.Scale, Scale * inner.Offset + Offset);
    }
}
