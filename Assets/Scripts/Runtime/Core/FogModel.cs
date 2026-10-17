using UnityEngine;

namespace Menger.Core
{
    /// <summary>Uniform-fog visibility as a continuous, monotonically decreasing function of continuous depth, bounded below by physics.</summary>
    public static class FogModel
    {
        /// <summary>Distance covered while reacting at walking speed (the walker stops instantly).</summary>
        public static float StoppingDistance(MengerSettings s) => s.walkSpeed * s.reactionTime;

        public static float MinimumSafeVisibility(MengerSettings s) => Mathf.Max(s.minVisibility, s.safetyFactor * StoppingDistance(s));

        public static float Visibility(MengerSettings s, float continuousDepth)
        {
            float vmin = MinimumSafeVisibility(s);
            float vbase = Mathf.Max(s.baseVisibility, vmin);
            return vmin + (vbase - vmin) * Mathf.Exp(-s.depthDecay * Mathf.Max(0f, continuousDepth));
        }

        /// <summary>Exponential fog density giving ~5% remaining contrast at the visibility distance.</summary>
        public static float Density(float visibility) => 3f / visibility;
    }
}
