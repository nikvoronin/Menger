using UnityEngine;

namespace Menger.Flight
{
    /// <summary>First-person view: the camera sits at the centre of the ship's collision sphere and inherits its full rotation (no horizon, constant FOV).</summary>
    public sealed class FirstPersonCamera : MonoBehaviour
    {
        Transform body;

        public void Init(MengerSettings s, Transform bodyTransform)
        {
            body = bodyTransform;
            var cam = GetComponent<Camera>();
            cam.fieldOfView = s.cameraFov; // constant: never changed by transitions
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 30000f;
            cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = s.background;
        }

        void LateUpdate()
        {
            if (body != null) transform.SetPositionAndRotation(body.position, body.rotation);
        }
    }
}
