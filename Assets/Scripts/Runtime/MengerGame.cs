using Menger.Core;
using Menger.Flight;
using Menger.Rendering;
using Menger.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Menger
{
    /// <summary>Wires the prototype together at start-up and applies depth-driven fog. Also hosts the diagnostic-only debug keys.</summary>
    public sealed class MengerGame : MonoBehaviour
    {
        public MengerSettings settings;
        public Material surfaceMaterial, glowMaterial, skyMaterial;

        public WorldState World { get; private set; }
        public WalkerController Player { get; private set; }
        public float Visibility { get; private set; }

        Camera cam;
        WorldRenderer worldRenderer;
        NavigationHud hud;
        DiagnosticsOverlay diag;

        // on the outer top face of a corner cube of the root, on a solid strip between the carpet holes, facing along it towards +x
        static readonly Vector3 SpawnLogic = new Vector3(-417, 455, -423);

        void Start()
        {
            if (settings == null) settings = ScriptableObject.CreateInstance<MengerSettings>();
            World = new WorldState(settings);

            if (skyMaterial != null) RenderSettings.skybox = skyMaterial;
            cam = Camera.main;
            if (cam == null) cam = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" }.GetComponent<Camera>();

            Player = new GameObject("Player").AddComponent<WalkerController>();
            Player.Init(settings, World, SpawnLogic, Vector3.up, Vector3.right);

            var eye = cam.gameObject.GetComponent<FirstPersonCamera>() ?? cam.gameObject.AddComponent<FirstPersonCamera>();
            eye.Init(settings, Player.transform);

            worldRenderer = new GameObject("World").AddComponent<WorldRenderer>();
            worldRenderer.surfaceMaterial = surfaceMaterial;
            worldRenderer.glowMaterial = glowMaterial;
            worldRenderer.Init(settings, World, cam);

            foreach (var l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional) { gameObject.AddComponent<SunOrbit>().Init(l, settings.sunOrbitSeconds); break; }
            new GameObject("AimMarker").AddComponent<AimMarker>().Init(Player, cam.transform, glowMaterial);
            Player.Lost += Respawn;

            hud = gameObject.AddComponent<NavigationHud>();
            hud.Init(settings, World, cam, Player.transform);
            diag = gameObject.AddComponent<DiagnosticsOverlay>();
            diag.Init(settings, World, Player, worldRenderer, hud);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            Cursor.lockState = CursorLockMode.Locked;
            ApplyFog();
        }

        void Update()
        {
            ApplyFog();
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f10Key.wasPressedThisFrame) { Quit(); return; }
            if (kb.f1Key.wasPressedThisFrame) diag.visible = !diag.visible;
            if (kb.f2Key.wasPressedThisFrame) hud.showHelp = !hud.showHelp;
            if (kb.escapeKey.wasPressedThisFrame) Cursor.lockState = CursorLockMode.None;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) Cursor.lockState = CursorLockMode.Locked;
            if (kb.rKey.wasPressedThisFrame) Respawn();
        }

        void ApplyFog()
        {
            Visibility = FogModel.Visibility(settings, World.ContinuousDepth);
            RenderSettings.fogDensity = FogModel.Density(Visibility);
            RenderSettings.fogColor = settings.background;
            cam.backgroundColor = settings.background;
            worldRenderer.visibility = Visibility;
            hud.Visibility = Visibility;
            diag.Visibility = Visibility;
        }

        // ---- diagnostic-only helpers: they are not a substitute for in-game travel -------------------------------------

        /// <summary>F10: leave the game (in the Editor: stop Play mode).</summary>
        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Respawn()
        {
            World.Reset(RegionAddress.Root);
            Player.dynamics.Pitch = 0f;
            Player.Place(SpawnLogic, Vector3.up, Vector3.right);
        }
    }
}
