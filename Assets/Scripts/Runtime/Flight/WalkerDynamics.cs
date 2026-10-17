using UnityEngine;

namespace Menger.Flight
{
    public struct WalkInput
    {
        public float Forward;  // -1..1 (W/S)
        public float Strafe;   // -1..1 (D/A)
        public Vector2 Look;   // mouse look this frame, degrees (x = yaw right, y = pitch up)
        public bool Jump;      // Space (held: jumps again on landing)
    }

    public interface IWalkWorld
    {
        /// <summary>Push the display-space sphere out of solid geometry; returns true (and the contact normal) if it was touching.</summary>
        bool Collide(ref Vector3 position, float radius, out Vector3 normal);
        /// <summary>Distance from a display position to the first solid along an axis-aligned unit direction, +Infinity if none within maxDist.</summary>
        float AxisRay(Vector3 position, Vector3 direction, float maxDist);
    }

    /// <summary>
    /// Surface walker: the player is a sphere that rests on one face of the sponge. "Up" is that face's outward normal (one of the
    /// six axes), the camera sits at the sphere centre (one radius above the face) and movement is FPS-style in the face plane. Walking
    /// off a face edge is allowed: with nothing underfoot the walker falls along -Up, with no limit of its own (the controller sends them back to the start when they get too far from the sponge). Space jumps
    /// (ballistic flight along -Up with full air control, so tunnels can be jumped over);
    /// <see cref="TeleportTo"/> puts the player on any surface point, standing on it with the view parallel to its plane.
    /// Knows nothing about recursive depth or world scale: every parameter is in display units.
    /// </summary>
    public sealed class WalkerDynamics
    {
        const float MaxPitch = 89f;
        readonly MengerSettings cfg;
        public Vector3 Position;          // sphere centre
        public Vector3 Up = Vector3.up;   // outward normal of the current face
        public Vector3 Heading = Vector3.forward; // unit, tangent to the face
        public float Pitch;               // degrees, + looks up
        public float Speed;               // last step's ground speed, for diagnostics
        public int LastContactCount;
        public bool Airborne { get; private set; }
        float vertical;                   // speed along Up

        public WalkerDynamics(MengerSettings settings) { cfg = settings; }

        public Quaternion Rotation
        {
            get
            {
                Vector3 right = Vector3.Cross(Up, Heading);
                return Quaternion.LookRotation(Quaternion.AngleAxis(-Pitch, right) * Heading, Up);
            }
        }

        public Vector3 ViewDirection => Rotation * Vector3.forward;

        public void Place(Vector3 position, Vector3 up, Vector3 heading, float pitch = 0f)
        {
            Position = position; Up = up; Pitch = pitch;
            Airborne = false; vertical = 0f;
            Heading = Tangent(heading);
        }

        Vector3 Tangent(Vector3 v) => Tangent(v, Up);

        static Vector3 Tangent(Vector3 v, Vector3 up)
        {
            v -= up * Vector3.Dot(v, up);
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.Cross(up, Mathf.Abs(up.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
        }

        public void Step(float dt, in WalkInput input, IWalkWorld world)
        {
            Heading = Tangent(Quaternion.AngleAxis(input.Look.x, Up) * Heading);
            Pitch = Mathf.Clamp(Pitch + input.Look.y, -MaxPitch, MaxPitch);

            Vector3 wish = Vector3.ClampMagnitude(Heading * input.Forward + Vector3.Cross(Up, Heading) * input.Strafe, 1f) * cfg.walkSpeed;
            Speed = wish.magnitude;
            if (!Airborne && input.Jump) { Airborne = true; vertical = cfg.jumpSpeed; }
            if (Airborne) { Fly(dt, wish, world); return; }

            // never move more than a fraction of the radius between contact checks
            float r = cfg.shipRadius;
            Vector3 delta = wish * dt;
            int steps = Mathf.Clamp(Mathf.CeilToInt(delta.magnitude / (r * 0.4f)), 1, 64);
            LastContactCount = 0;
            for (int i = 0; i < steps && !Airborne; i++)
            {
                Position += delta / steps;
                SettleOrFall(world); // walls push the sphere out; with no face underfoot it starts to fall
            }
        }

        /// <summary>Ballistic flight: gravity along -Up, the horizontal velocity follows the keys, walls push the sphere out, any face underneath catches it.</summary>
        void Fly(float dt, Vector3 wish, IWalkWorld world)
        {
            float r = cfg.shipRadius;
            float fastest = Mathf.Max(wish.magnitude, Mathf.Abs(vertical) + cfg.gravity * dt);
            int steps = Mathf.Clamp(Mathf.CeilToInt(fastest * dt / (r * 0.4f)), 1, 64);
            float h = dt / steps;
            LastContactCount = 0;
            for (int i = 0; i < steps; i++)
            {
                vertical -= cfg.gravity * h;
                Position += (wish + Up * vertical) * h;
                for (int pass = 0; world != null && pass < 3 && world.Collide(ref Position, r, out var n); pass++)
                {
                    LastContactCount++;
                    if (Vector3.Dot(n, Up) < -0.7f && vertical > 0f) vertical = 0f; // bumped a ceiling
                }
                if (vertical <= 0f && world != null)
                {
                    float ground = world.AxisRay(Position, -Up, r + 0.3f);
                    if (!float.IsInfinity(ground)) { Position += Up * (r - ground); Airborne = false; vertical = 0f; return; }
                }
            }
        }

        /// <summary>Resolve wall contacts and snap to the face under the player. False if nothing is underfoot.</summary>
        public bool Settle(IWalkWorld world)
        {
            float r = cfg.shipRadius;
            for (int pass = 0; world != null && pass < 3 && world.Collide(ref Position, r, out _); pass++) LastContactCount++;
            if (world == null) return true;
            float ground = world.AxisRay(Position, -Up, 3f * r);
            if (float.IsInfinity(ground)) return false;
            Position += Up * (r - ground);
            return true;
        }

        /// <summary><see cref="Settle"/>, and when there is no face underfoot start falling (from rest along -Up).</summary>
        public bool SettleOrFall(IWalkWorld world)
        {
            if (Settle(world)) return true;
            Airborne = true; vertical = 0f;
            return false;
        }

        /// <summary>
        /// Left click: stand on the surface point <paramref name="point"/> whose outward normal is <paramref name="normal"/>. That face's normal becomes "up",
        /// the pitch is zeroed so the line of sight is parallel to the new plane, and the heading keeps the direction the player was
        /// looking (<paramref name="view"/>) as far as the new plane allows. False (nothing changes) if there is no footing there.
        /// </summary>
        public bool TeleportTo(Vector3 point, Vector3 normal, Vector3 view, IWalkWorld world)
        {
            float r = cfg.shipRadius;
            Vector3 up = normal.normalized;
            Vector3 heading = view - up * Vector3.Dot(view, up);
            if (heading.sqrMagnitude < 0.01f) heading = Heading; // looking straight at (or away from) the face: keep the old heading
            heading = Tangent(heading, up);
            Vector3 t1 = Tangent(Vector3.Cross(up, Mathf.Abs(up.y) > 0.9f ? Vector3.right : Vector3.up), up), t2 = Vector3.Cross(up, t1);

            var saved = (Position, Up, Heading, Pitch, Airborne, vertical);
            Vector3 target = point + up * r;
            // aiming right at an edge leaves no footing under the centre: try a radius further in
            foreach (var off in new[] { Vector3.zero, t1 * r, -t1 * r, t2 * r, -t2 * r })
            {
                Place(target + off, up, heading);
                if (Settle(world)) return true;
            }
            (Position, Up, Heading, Pitch, Airborne, vertical) = saved;
            return false;
        }
    }
}
