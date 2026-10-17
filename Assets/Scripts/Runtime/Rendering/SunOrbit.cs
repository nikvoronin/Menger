using UnityEngine;

namespace Menger.Rendering
{
    /// <summary>Turns the directional light slowly round a great circle of the sky: the sponge shading and every shadow follow it.</summary>
    public sealed class SunOrbit : MonoBehaviour
    {
        Transform sun;
        Quaternion start;
        Vector3 axis;
        float degreesPerSecond;
        float angle;

        public void Init(Light light, float periodSeconds)
        {
            sun = light.transform;
            start = sun.rotation;
            // the axis is perpendicular to the sun direction, so the sun passes over, round and under the sponge
            Vector3 toSun = -sun.forward;
            axis = Vector3.Cross(toSun, Mathf.Abs(toSun.x) < 0.9f ? Vector3.right : Vector3.forward).normalized;
            degreesPerSecond = periodSeconds > 0f ? 360f / periodSeconds : 0f;
        }

        /// <summary>The unit vector towards the sun after turning <paramref name="degrees"/> about <paramref name="axis"/>.</summary>
        public static Vector3 Direction(Vector3 toSunAtStart, Vector3 axis, float degrees) => Quaternion.AngleAxis(degrees, axis) * toSunAtStart;

        void Update()
        {
            if (sun == null || degreesPerSecond == 0f) return;
            angle = Mathf.Repeat(angle + degreesPerSecond * Time.deltaTime, 360f);
            sun.rotation = Quaternion.AngleAxis(angle, axis) * start;
        }
    }
}
