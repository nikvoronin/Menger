using UnityEngine;

namespace Menger.Core
{
    /// <summary>
    /// The scale exponent e of the active entrance, a pure function of the ship's DISPLAY position relative to the pivot
    /// (the tunnel cube's centre): world scale = 3^e. Forward e is in [0,1] (parent frame: grows x1 -> x3), reverse e is in [-1,0]
    /// (child frame: shrinks x1 -> x1/3). No time integration, so stopping stops the change and reversing reverses it.
    /// </summary>
    public readonly struct TransitionZone
    {
        public readonly float H0, H1;                 // axial: e starts at H0, full at H1 (distance from pivot, display units)
        public readonly float FarFade0, FarFade1;     // forward: fade out beyond the tunnel's far end
        public readonly float OuterStart, OuterLen;   // reverse: fade out far outside the mouth
        public readonly float FwdIn, FwdOut, RevIn, RevOut; // lateral weights (display units)

        public TransitionZone(MengerSettings s)
        {
            float cell = s.rootSize / 3f;
            H0 = cell * s.approachStartCells;
            H1 = cell * s.approachEndCells;
            FarFade0 = -cell;
            FarFade1 = -cell * 0.667f;
            OuterStart = H0 + cell * 0.133f;
            OuterLen = cell * 0.5f;
            float fwdHalf = s.rootSize / 18f, revHalf = s.rootSize / 6f;
            FwdIn = fwdHalf * s.lateralFullFraction; FwdOut = fwdHalf * s.lateralZeroFraction;
            RevIn = revHalf * s.lateralFullFraction; RevOut = revHalf * s.lateralZeroFraction;
        }

        /// <summary>C2-ish smooth step: zero first and second derivative at both ends.</summary>
        public static float Smoother(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        static float Lateral(float l, float full, float zero) => 1f - Smoother((Mathf.Abs(l) - full) / (zero - full));

        /// <summary>e for an entrance given the ship's display position relative to the entrance's pivot.</summary>
        public float Exponent(in Entrance e, Vector3 rel)
        {
            var n = e.NormalF;
            float h = Vector3.Dot(rel, n);
            float l1, l2;
            switch (e.Axis)
            {
                case 0: l1 = rel.y; l2 = rel.z; break;
                case 1: l1 = rel.z; l2 = rel.x; break;
                default: l1 = rel.x; l2 = rel.y; break;
            }
            float f = Smoother((H0 - h) / (H0 - H1));
            if (!e.Reverse)
            {
                float w = Lateral(l1, FwdIn, FwdOut) * Lateral(l2, FwdIn, FwdOut);
                float far = Smoother((h - FarFade0) / (FarFade1 - FarFade0));
                return f * w * far;
            }
            else
            {
                float w = Lateral(l1, RevIn, RevOut) * Lateral(l2, RevIn, RevOut);
                float outer = 1f - Smoother((h - OuterStart) / OuterLen);
                return -(1f - f) * w * outer;
            }
        }
    }
}
