using UnityEngine;

namespace Menger
{
    /// <summary>All tunable prototype parameters. None of them is a canonical game-design limit.</summary>
    [CreateAssetMenu(menuName = "Menger/Settings", fileName = "MengerSettings")]
    public sealed class MengerSettings : ScriptableObject
    {
        [Header("World")]
        public float rootSize = 900f;
        [Tooltip("How many parent levels are modelled around the current region (bounded cost; older ancestors are void).")]
        [Range(1, 5)] public int contextAncestors = 3;

        [Header("Player (display units; never depends on depth or scale)")]
        [Tooltip("The player is a sphere resting on a face: its radius is also the eye height above the face.")]
        public float shipRadius = 5f;
        public float walkSpeed = 30f;
        [Tooltip("Space: take-off speed along the surface normal. Jump length is about 2 * jumpSpeed / gravity * walkSpeed, height jumpSpeed^2 / (2 * gravity); the defaults clear a 100-unit tunnel.")]
        public float jumpSpeed = 30f;
        public float gravity = 13f;
        [Tooltip("The player is put back at the start when they are this far outside the root cube (measured from its faces, in root-region units, whatever region they are in). A fall has no other limit.")]
        public float escapeDistance = 900f;
        [Tooltip("Click-teleport / the aim ring reach, display units.")]
        public float aimRange = 3000f;
        [Tooltip("Mouse look, degrees per pixel of mouse movement.")]
        public float mouseSensitivity = 0.12f;

        [Header("Camera (first person, at the player's centre)")]
        public float cameraFov = 15f;

        [Header("Scale transition (fractions of a child cell = rootSize/3)")]
        [Tooltip("The scaling pivot lies this far inside the entrance plane, on the tunnel axis.")]
        public float pivotDepthCells = 0.1f;
        [Tooltip("Player distance from the pivot where growth starts. 0.14 cell = 12 units above the mouth plane: a jump over a mouth (peak 30 above the face) does not scale the world, falling into it does. The flight-era value was 0.567.")]
        public float approachStartCells = 0.14f;
        [Tooltip("Ship distance from the pivot where growth is complete (x3, inside the mouth) and the frame is reassigned.")]
        public float approachEndCells = 0f;
        [Tooltip("Lateral offset (fraction of the mouth half-width) up to which the transition runs at full strength.")]
        [Range(0.3f, 0.95f)] public float lateralFullFraction = 0.75f;
        [Tooltip("Lateral offset beyond which the transition is off. Keep it below 1 - shipRadius/halfWidth so the walls never squeeze the hull.")]
        [Range(0.4f, 0.98f)] public float lateralZeroFraction = 0.9f;

        [Header("Adaptive LOD (surface meshes)")]
        [Tooltip("A cell is expanded while size > ratio * distance to it.")]
        public float lodRatio = 0.2f;
        public float minLeafSize = 4f;
        [Tooltip("Rebuild the mesh after the camera moved this far (spatial hysteresis).")]
        public float rebuildDistance = 12f;

        [Tooltip("Merge equal coplanar faces into big rectangles (fewer triangles). Off = one quad per face, as the reference mesher.")]
        public bool greedyMerge = true;

        [Header("Fog (uniform, driven by continuous depth)")]
        public float baseVisibility = 2000f;
        public float minVisibility = 600f;
        [Tooltip("Visibility = min + (base-min) * exp(-decay * continuousDepth).")]
        public float depthDecay = 0.12f;
        [Tooltip("Visibility is never below safetyFactor * stoppingDistance.")]
        public float safetyFactor = 2.5f;
        public float reactionTime = 0.35f;

        [Header("HUD")]
        [Range(1, 6)] public int maxArrows = 3;
        public float arrowInset = 48f;
        public float targetRangeMargin = 100f;
        [Tooltip("A non-selected target replaces a selected one only if closer than this fraction of its distance.")]
        public float arrowSwapHysteresis = 0.8f;

        [Header("Surface material (applied live to the sponge, Play mode included)")]
        [Tooltip("Cubes are tinted by recursion level: hue = hueBase + hueStep * level.")]
        [Range(0f, 1f)] public float surfaceHueBase = 0.58f;
        [Range(-0.3f, 0.3f)] public float surfaceHueStep = 0.045f;
        [Range(0f, 1f)] public float surfaceSaturation = 0f;
        [Range(0f, 1f)] public float surfaceBrightness = 0.92f;
        [Tooltip("Light on faces the sun does not reach.")]
        [Range(0f, 1f)] public float surfaceAmbient = 0.3f;
        [Range(0f, 2f)] public float surfaceSunLight = 0.65f;
        [Range(0f, 1f)] public float surfaceFillLight = 0.25f;

        [Header("Surface detail texture (painted in code at start; seed / contrast / resolution rebuild it live)")]
        [Tooltip("How much the texture darkens and lightens the colour.")]
        [Range(0f, 1f)] public float detailStrength = 0f;
        [Tooltip("The texture repeats every rootSize / this many logic units. Keep it an integer: that is what makes it seamless across region changes.")]
        [Min(1)] public int detailTileDivisions = 27;
        public int detailSeed = 7;
        [Range(0.2f, 2f)] public float detailContrast = 1.8f;
        [Range(64, 1024)] public int detailResolution = 512;

        [Header("Roughness and highlight")]
        [Tooltip("0 = polished, 1 = chalk.")]
        [Range(0f, 1f)] public float roughness = 0.08f;
        [Tooltip("How far the texture moves the roughness up and down.")]
        [Range(0f, 1f)] public float roughnessVariation = 0f;
        [Range(0f, 2f)] public float specularStrength = 1.5f;
        [Tooltip("How much of the sky the faces mirror (stronger at grazing angles).")]
        [Range(0f, 2f)] public float skyReflection = 0.8f;

        [Header("Sun")]
        [Tooltip("Seconds for the sun to go once round its orbit; 0 keeps it still.")]
        public float sunOrbitSeconds = 240f;

        [Header("Colors")]
        [ColorUsage(false, true)] public Color forwardOutline = new Color(0.1f, 0.9f, 0.85f);
        [ColorUsage(false, true)] public Color reverseOutline = new Color(1f, 0.62f, 0.12f);
        public Color background = new Color(0.02f, 0.03f, 0.05f);
        public float outlineGlow = 2.2f;

        [Header("Diagnostics")]
        public bool showOverlay = true;
    }
}
