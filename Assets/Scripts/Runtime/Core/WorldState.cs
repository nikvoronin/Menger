using System;
using System.Collections.Generic;
using UnityEngine;

namespace Menger.Core
{
    public enum FlipKind { None, Descended, Ascended, Sibling }

    /// <summary>
    /// The recursive-frame model: current region (address + context cube), the logic->display mapping, the single active
    /// transition lock, and frame reassignment. A frame flip is a pure coordinate change: the display mapping is composed so
    /// that every point keeps its display position, and nothing else (ship velocity, orientation) is touched. After a flip the
    /// logic origin is rebased to display zero (floating origin) so display coordinates never drift.
    /// </summary>
    public sealed class WorldState
    {
        const float AcquireEps = 0.02f;
        // A lock is dropped only when the world scale is ~1 to within ~1e-3 units at the far end of the cube; flips happen only on the
        // exact plateau (e = +-1), where the mapping is exact. Otherwise a tiny scale residue times a huge pivot distance becomes a visible shift.
        readonly float ReleaseEps;

        readonly MengerSettings cfg;
        readonly TransitionZone zone;
        readonly Dictionary<RegionAddress, Entrance[]> catalog = new Dictionary<RegionAddress, Entrance[]>();

        V3d origin;            // display position of the logic origin while scale == 1 (no lock)
        V3d lockPivotD;        // display position of the pivot (fixed while locked)
        Vector3 lockPivotL;    // pivot in the current frame's logic coordinates
        Vector3 prevShip;
        bool hasPrev;

        public RegionAddress Frame { get; private set; }
        public ContextRoot Context { get; private set; }
        public Entrance[] Entrances { get; private set; }
        public bool HasLock { get; private set; }
        public Entrance Lock { get; private set; }
        public float Exponent { get; private set; }
        public int FlipCount { get; private set; }
        /// <summary>Diagnostics/tests: the frame and logic->display map that were current just before the last flip.</summary>
        public RegionAddress LastFlipFrameBefore { get; private set; }
        public Affine LastFlipMapBefore { get; private set; }
        public TransitionZone Zone => zone;
        public MengerSettings Settings => cfg;

        public WorldState(MengerSettings settings, RegionAddress start = null)
        {
            cfg = settings;
            ReleaseEps = 2e-3f / settings.rootSize;
            zone = new TransitionZone(settings);
            SetFrame(start ?? RegionAddress.Root);
        }

        // ---- mapping -------------------------------------------------------------------------------------------------

        /// <summary>Current frame logic -> display.</summary>
        public Affine Map
        {
            get
            {
                if (!HasLock) return new Affine(1.0, origin);
                double a = Math.Pow(3.0, Exponent);
                return new Affine(a, lockPivotD - a * new V3d(lockPivotL));
            }
        }

        public float Scale => (float)Map.Scale;
        /// <summary>Integer depth plus transition phase: continuous across frame reassignment.</summary>
        public float ContinuousDepth => Frame.Depth + (HasLock ? Exponent : 0f);

        public Vector3 DisplayToLogic(Vector3 d) => Map.Inverse(new V3d(d)).ToVector3();
        public Vector3 LogicToDisplay(Vector3 x) => Map.Apply(new V3d(x)).ToVector3();

        public Entrance[] GetEntrances(RegionAddress frame)
        {
            if (!catalog.TryGetValue(frame, out var list))
            {
                if (catalog.Count > 96) catalog.Clear();
                list = EntranceCatalog.Build(frame, frame.GetContext(cfg.contextAncestors, cfg.rootSize), cfg.rootSize, cfg.shipRadius, Mathf.Round(cfg.pivotDepthCells * cfg.rootSize / 3f)); // whole units: pivots of parent and child frames then convert exactly in float
                catalog[frame] = list;
            }
            return list;
        }

        void SetFrame(RegionAddress frame)
        {
            Frame = frame;
            Context = frame.GetContext(cfg.contextAncestors, cfg.rootSize);
            Entrances = GetEntrances(frame);
        }

        /// <summary>Diagnostic teleport: make <paramref name="frame"/> current with its centre at display zero.</summary>
        public void Reset(RegionAddress frame)
        {
            HasLock = false; Exponent = 0; origin = V3d.Zero; hasPrev = false;
            SetFrame(frame);
        }

        // ---- per-frame update ----------------------------------------------------------------------------------------

        /// <summary>
        /// Advance the transition state from the ship's new display position. <paramref name="ship"/> and the returned
        /// <paramref name="rebaseShift"/> follow floating-origin rebasing: callers subtract the shift from every display position they hold.
        /// </summary>
        public FlipKind Update(ref Vector3 ship, out Vector3 rebaseShift)
        {
            var total = FlipKind.None;
            var shift = V3d.Zero;
            for (int guard = 0; guard < 4; guard++)
            {
                var kind = Step(ship);
                if (kind == FlipKind.None) break;
                total = kind;
                FlipCount++;
                // display position of the new logic origin, rounded to float first so world and ship are shifted by exactly the same amount
                var b = new V3d(Map.Offset.ToVector3());
                origin -= b; lockPivotD -= b; shift += b;
                ship -= b.ToVector3(); prevShip = ship;
            }
            prevShip = ship; hasPrev = true;
            rebaseShift = shift.ToVector3();
            return total;
        }

        /// <summary>
        /// The player was moved from <paramref name="fromDisplay"/> to the surface point <paramref name="groundDisplay"/> by a teleport (the aim click).
        /// A teleport straight THROUGH a mouth ends the way a flight through it does: through a child's mouth into its tunnel the frame becomes that
        /// child and the world scales x3 about the point; through the frame's own exit mouth the way back is taken (x1/3). Any other teleport keeps
        /// the region and returns a pending scale transition to x1, again about the point. <paramref name="ship"/> follows the floating-origin rebase.
        /// </summary>
        public FlipKind Teleported(ref Vector3 ship, Vector3 fromDisplay, Vector3 groundDisplay)
        {
            hasPrev = false;
            var pD = new V3d(groundDisplay);
            Vector3 x = DisplayToLogic(groundDisplay), a = DisplayToLogic(fromDisplay);
            var xv = new V3d(x);
            if (!FindMouth(a, x, out var pick))
            {
                if (HasLock) { origin = pD - xv; HasLock = false; Exponent = 0f; }
                return FlipKind.None;
            }
            Lock = pick; lockPivotL = pick.Pivot; HasLock = true;
            Exponent = pick.Reverse ? -1f : 1f;                  // the end of the transition: the plateau where a flip is exact
            lockPivotD = pD - Math.Pow(3.0, Exponent) * (xv - new V3d(lockPivotL)); // the map that keeps the point where it is
            var kind = pick.Reverse ? Ascend() : Descend();
            FlipCount++;
            var b = new V3d(Map.Offset.ToVector3());
            origin -= b; lockPivotD -= b;
            ship -= b.ToVector3(); prevShip = ship;
            return kind;
        }

        /// <summary>
        /// The player moved by themselves (walked, jumped, fell) from <paramref name="fromDisplay"/> to <paramref name="ship"/>. If that step went through a
        /// mouth of this frame - into a child's tunnel, or out through the exit - the region is reassigned and the world scales x3 / x1/3 about
        /// <paramref name="anchorDisplay"/> (where the player touches the surface, or their centre in the air), exactly as a teleport through a mouth does.
        /// This covers the paths a smooth transition cannot: along the walls, where the centred approach zone is never entered. A transition that is
        /// already in progress is left alone to finish smoothly.
        /// </summary>
        public FlipKind CrossedMouth(ref Vector3 ship, Vector3 fromDisplay, Vector3 anchorDisplay)
        {
            if (HasLock) return FlipKind.None;
            if (!FindMouth(DisplayToLogic(fromDisplay), DisplayToLogic(ship), out _)) return FlipKind.None;
            return Teleported(ref ship, fromDisplay, anchorDisplay);
        }

        /// <summary>The mouth of this frame that the line a -> x goes through (a outside, x in its tunnel; or out through the exit), if any. Frame logic coordinates.</summary>
        bool FindMouth(Vector3 a, Vector3 x, out Entrance pick)
        {
            float half = cfg.rootSize * 0.5f;
            pick = default; bool found = false; float best = float.MaxValue;
            foreach (var en in Entrances)
            {
                bool inside;
                if (en.Reverse) inside = Mathf.Abs(x.x) > half + 0.01f || Mathf.Abs(x.y) > half + 0.01f || Mathf.Abs(x.z) > half + 0.01f;
                else
                {
                    Vector3 d = x - en.CubeCenter;
                    float h = en.CubeSize * 0.5f - 0.01f;
                    inside = Mathf.Abs(d.x) < h && Mathf.Abs(d.y) < h && Mathf.Abs(d.z) < h;
                }
                if (!inside || !PassesThroughMouth(en, a, x)) continue;
                float dist = (en.MouthCenter - a).sqrMagnitude;
                if (dist < best) { best = dist; pick = en; found = true; }
            }
            return found;
        }

        /// <summary>The straight line a -> x (frame logic coordinates) crosses the mouth's opening: inwards for a child's mouth, outwards for the exit.</summary>
        bool PassesThroughMouth(in Entrance en, Vector3 a, Vector3 x)
        {
            Vector3 n = en.NormalF, m = en.MouthCenter;
            float da = Vector3.Dot(a - m, n), dx = Vector3.Dot(x - m, n);
            if (en.Reverse ? !(da < 0f && dx > 0f) : !(da > 0f && dx < 0f)) return false;
            Vector3 c = a + (x - a) * (da / (da - dx)) - m;   // where the line meets the mouth plane
            c -= n * Vector3.Dot(c, n);
            float lim = en.MouthHalfWidth + cfg.shipRadius / Mathf.Max(1e-3f, Scale); // the opening, with the player's size as margin
            return Mathf.Max(Mathf.Abs(c.x), Mathf.Abs(c.y), Mathf.Abs(c.z)) <= lim;
        }

        FlipKind Step(Vector3 ship)
        {
            if (!HasLock) TryAcquire(ship);
            if (HasLock)
            {
                float e = zone.Exponent(Lock, (new V3d(ship) - lockPivotD).ToVector3());
                Exponent = e;
                if (!Lock.Reverse && e >= 1f) return Descend();
                if (Lock.Reverse && e <= -1f) return Ascend();
                if (Mathf.Abs(e) < ReleaseEps) Release();
                return FlipKind.None;
            }
            return TrySibling(ship);
        }

        void TryAcquire(Vector3 ship)
        {
            if (!hasPrev) return;
            float best = 0; int bestIndex = -1;
            for (int i = 0; i < Entrances.Length; i++)
            {
                var en = Entrances[i];
                var pivot = origin + new V3d(en.Pivot);
                float cur = zone.Exponent(en, (new V3d(ship) - pivot).ToVector3());
                if (Mathf.Abs(cur) <= ReleaseEps) continue;
                // lock only where the exponent was ~0 a moment ago, so the world scale never jumps on acquisition
                float prev = zone.Exponent(en, (new V3d(prevShip) - pivot).ToVector3());
                if (Mathf.Abs(prev) > AcquireEps) continue;
                if (Mathf.Abs(cur) > Mathf.Abs(best)) { best = cur; bestIndex = i; }
            }
            if (bestIndex < 0) return;
            Lock = Entrances[bestIndex];
            HasLock = true;
            lockPivotL = Lock.Pivot;
            lockPivotD = origin + new V3d(lockPivotL);
            Exponent = best;
        }

        void Release()
        {
            origin = lockPivotD - new V3d(lockPivotL); // scale is 1 here
            HasLock = false; Exponent = 0;
        }

        FlipKind Descend()
        {
            var old = Lock;
            LastFlipFrameBefore = Frame; LastFlipMapBefore = Map;
            SetFrame(old.Target);
            // the mouth we came through is this frame's reverse mouth on the same side
            if (!TryRelock(Entrances, e => e.Reverse && e.DirectionIndex == old.DirectionIndex, Exponent - 1f)) HasLock = false;
            return FlipKind.Descended;
        }

        FlipKind Ascend()
        {
            var oldFrame = Frame;
            var old = Lock;
            LastFlipFrameBefore = Frame; LastFlipMapBefore = Map;
            SetFrame(old.Target);
            if (!TryRelock(Entrances, e => !e.Reverse && e.ChildIndex == oldFrame.ChildIndex && e.DirectionIndex == old.DirectionIndex, Exponent + 1f)) HasLock = false;
            return FlipKind.Ascended;
        }

        bool TryRelock(Entrance[] list, Func<Entrance, bool> match, float exponent)
        {
            foreach (var e in list)
            {
                if (!match(e)) continue;
                Lock = e; lockPivotL = e.Pivot; Exponent = exponent; HasLock = true;
                return true;
            }
            return false;
        }

        FlipKind TrySibling(Vector3 ship)
        {
            if (Frame.Depth == 0) return FlipKind.None;
            float size = cfg.rootSize;
            Vector3 x = (new V3d(ship) - origin).ToVector3();
            float half = size * 0.5f;
            if (Mathf.Abs(x.x) <= half && Mathf.Abs(x.y) <= half && Mathf.Abs(x.z) <= half) return FlipKind.None;
            var o = new Vector3Int(Mathf.RoundToInt(x.x / size), Mathf.RoundToInt(x.y / size), Mathf.RoundToInt(x.z / size));
            if (o == Vector3Int.zero) return FlipKind.None;
            int idx = MengerCells.IndexOf(Frame.Offset + o);
            if (idx < 0) return FlipKind.None; // parent's empty cell (or beyond it): stay in this frame, geometry comes from the context
            LastFlipFrameBefore = Frame; LastFlipMapBefore = Map;
            origin += new V3d(o.x * size, o.y * size, o.z * size);
            SetFrame(Frame.Parent.Child(idx));
            return FlipKind.Sibling;
        }

        // ---- collision field in display space -------------------------------------------------------------------------

        /// <summary>Push a display-space sphere out of the sponge. The geometry may be moving (scale transition); only position changes.</summary>
        public bool Collide(ref Vector3 pos, float radius, out Vector3 normal)
        {
            var m = Map;
            float a = (float)m.Scale;
            float rL = radius / a;
            float leaf = CollisionWorld.LeafSize(rL);
            Vector3 p = m.Inverse(new V3d(pos)).ToVector3();
            float d = CollisionWorld.Closest(Context, p, rL, leaf, out var q);
            normal = Vector3.zero;
            if (d >= rL) return false;
            if (d > 1e-3f)
            {
                normal = (p - q) / d;
                p += normal * (rL - d);
            }
            else if (CollisionWorld.Escape(Context, p, rL, leaf, out var delta))
            {
                normal = delta.normalized;
                p += delta;
            }
            else return false;
            pos = m.Apply(new V3d(p)).ToVector3();
            return true;
        }

        /// <summary>How far a display position is outside the root cube, in root-region units (max-norm from its faces; <= 0 inside), whatever region is current.</summary>
        public float DistanceOutsideRoot(Vector3 display)
        {
            Vector3 x = RegionAddress.Convert(Frame, RegionAddress.Root, cfg.rootSize).Apply(new V3d(DisplayToLogic(display))).ToVector3();
            return Mathf.Max(Mathf.Abs(x.x), Mathf.Abs(x.y), Mathf.Abs(x.z)) - cfg.rootSize * 0.5f;
        }

        /// <summary>Distance (display units) from a display position to the nearest solid, up to <paramref name="maxDist"/>.</summary>
        public float Clearance(Vector3 pos, float maxDist)
        {
            var m = Map;
            float a = (float)m.Scale;
            float leaf = CollisionWorld.LeafSize(cfg.shipRadius / a);
            return a * CollisionWorld.Closest(Context, m.Inverse(new V3d(pos)).ToVector3(), maxDist / a, leaf, out _);
        }

        /// <summary>Distance (display units) from a display position to the first solid along an axis-aligned unit direction, +Infinity if none within maxDist.</summary>
        public float AxisRay(Vector3 pos, Vector3 dir, float maxDist)
        {
            var m = Map;
            float a = (float)m.Scale;
            float leaf = CollisionWorld.LeafSize(cfg.shipRadius / a);
            return a * CollisionWorld.RayDistance(Context, m.Inverse(new V3d(pos)).ToVector3(), dir, maxDist / a, leaf);
        }

        /// <summary>First surface hit by a display-space ray: hit point and outward face normal (display space; the map is a uniform scale, so normals are unchanged).</summary>
        public bool RayHit(Vector3 fromDisplay, Vector3 dir, float maxDist, out Vector3 point, out Vector3 normal)
        {
            var m = Map;
            float a = (float)m.Scale;
            float leaf = CollisionWorld.LeafSize(cfg.shipRadius / a);
            bool hit = CollisionWorld.RayHit(Context, m.Inverse(new V3d(fromDisplay)).ToVector3(), dir, maxDist / a, leaf, out var p, out normal);
            point = hit ? m.Apply(new V3d(p)).ToVector3() : default;
            return hit;
        }

        public bool RayBlocked(Vector3 fromDisplay, Vector3 toDisplay)
        {
            var m = Map;
            float leaf = CollisionWorld.LeafSize(cfg.shipRadius / (float)m.Scale);
            return CollisionWorld.RayBlocked(Context, m.Inverse(new V3d(fromDisplay)).ToVector3(), m.Inverse(new V3d(toDisplay)).ToVector3(), leaf);
        }
    }
}
