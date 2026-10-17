using Menger.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Menger.Flight
{
    /// <summary>Reads the Input System actions, runs <see cref="WalkerDynamics"/>, keeps the world state in sync and tracks what the player is looking at.</summary>
    public sealed class WalkerController : MonoBehaviour, IWalkWorld
    {
        public MengerSettings settings;
        [System.NonSerialized] public WorldState world;
        [System.NonSerialized] public WalkerDynamics dynamics;

        /// <summary>The surface point under the view centre (display space) and its outward normal; valid when <see cref="HasAim"/>.</summary>
        public bool HasAim { get; private set; }
        public Vector3 AimPoint { get; private set; }
        public Vector3 AimNormal { get; private set; }
        /// <summary>The player got far away from the sponge (<see cref="MengerSettings.escapeDistance"/>): the owner puts them back at the start.</summary>
        public System.Action Lost;

        InputActionMap map;
        InputAction look, move, strafe, jump, teleport;
        bool wasLocked;

        void OnEnable()
        {
            // Plain InputActions: bindings can be overridden/rebound through the Input System API.
            map = new InputActionMap("Walker");
            look = map.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            move = map.AddAction("Move", InputActionType.Value);
            move.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/w").With("Negative", "<Keyboard>/s");
            strafe = map.AddAction("Strafe", InputActionType.Value);
            strafe.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/d").With("Negative", "<Keyboard>/a");
            jump = map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            teleport = map.AddAction("TeleportToAim", InputActionType.Button, "<Mouse>/leftButton");
            map.Enable();
        }

        void OnDisable() => map?.Disable();

        public void Init(MengerSettings s, WorldState w, Vector3 position, Vector3 up, Vector3 heading)
        {
            settings = s; world = w;
            dynamics = new WalkerDynamics(s);
            Place(position, up, heading);
        }

        /// <summary>Stand at a display position on the face with the given outward normal (the walker snaps to the face).</summary>
        public void Place(Vector3 position, Vector3 up, Vector3 heading)
        {
            dynamics.Place(position, up, heading);
            dynamics.Settle(this);
            Apply();
            UpdateAim();
        }

        public bool Collide(ref Vector3 position, float radius, out Vector3 normal) => world.Collide(ref position, radius, out normal);
        public float AxisRay(Vector3 position, Vector3 direction, float maxDist) => world.AxisRay(position, direction, maxDist);

        /// <summary>Left click: stand on the aimed surface point.</summary>
        public bool TeleportToAim()
        {
            if (!HasAim) return false;
            Vector3 from = dynamics.Position;
            bool ok = dynamics.TeleportTo(AimPoint, AimNormal, dynamics.ViewDirection, this);
            if (!ok) return false;
            // a teleport through a tunnel mouth is the end of a transition: reassign the region and scale the world about the player's feet
            world.Teleported(ref dynamics.Position, from, dynamics.Position - dynamics.Up * settings.shipRadius);
            dynamics.Settle(this);
            Apply(); UpdateAim();
            return true;
        }

        void Update()
        {
            if (world == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f) return;

            WalkInput input = default;
            bool tele = false;
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            if (locked)
            {
                input.Look = look.ReadValue<Vector2>() * settings.mouseSensitivity;
                input.Forward = move.ReadValue<float>();
                input.Strafe = strafe.ReadValue<float>();
                input.Jump = jump.IsPressed();
                tele = teleport.WasPressedThisFrame() && wasLocked; // the click that captures the cursor is not a teleport
            }

            wasLocked = locked;
            Vector3 before = dynamics.Position;
            dynamics.Step(dt, input, this);
            // through a tunnel mouth on foot, in a jump or in a fall: the same region change and x3 / x1/3 as a click through it
            Vector3 anchor = dynamics.Airborne ? dynamics.Position : dynamics.Position - dynamics.Up * settings.shipRadius;
            world.CrossedMouth(ref dynamics.Position, before, anchor);
            if (tele) TeleportToAim();

            // geometry may have moved (scale transition): update it from the player's new position, then settle on the new surfaces
            world.Update(ref dynamics.Position, out _);
            if (!dynamics.Airborne) dynamics.SettleOrFall(this);
            if (world.DistanceOutsideRoot(dynamics.Position) > settings.escapeDistance) Lost?.Invoke(); // far out in space: back to the start
            Apply();
            UpdateAim();
        }

        void Apply() => transform.SetPositionAndRotation(dynamics.Position, dynamics.Rotation);

        Vector3 aimFrom; Quaternion aimRot; float aimScale; int aimFlips = -1;

        void UpdateAim()
        {
            // the ray is a sphere trace through the sponge: repeat it only when the view or the world has changed
            if (aimFlips == world.FlipCount && aimFrom == dynamics.Position && aimRot == dynamics.Rotation && aimScale == world.Scale) return;
            aimFrom = dynamics.Position; aimRot = dynamics.Rotation; aimScale = world.Scale; aimFlips = world.FlipCount;
            HasAim = world.RayHit(dynamics.Position, dynamics.ViewDirection, settings.aimRange, out var p, out var n);
            if (HasAim) { AimPoint = p; AimNormal = n; }
        }
    }
}
