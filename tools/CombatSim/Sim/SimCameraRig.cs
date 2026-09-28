using System;
using System.Collections.Generic;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // Mirror of Camera/LockOnController.cs (Update, order -50) and Camera/ThirdPersonCameraRig.cs (LateUpdate,
    // order 100). Lock-on picks and breaks with LockOnSelector, flicks with StickFlickDetector, the orbit with
    // OrbitCameraModel, walls with a sphere cast along -Forward. Screen shake is not modelled (it only rotates
    // the rendered view by up to ShakeMaxAngle x strength and never touches Yaw/Pitch).
    public sealed class SimCameraRig
    {
        readonly SimWorld world;
        public readonly CameraTuning CameraTuning;
        public readonly LockOnTuning LockOnTuning;
        public readonly OrbitCameraModel Orbit;
        public readonly LockOnSelector Selector;
        public readonly StickFlickDetector Flick;
        readonly List<LockOnCandidate> candidates = new List<LockOnCandidate>(16);
        readonly List<SimFighter> candidateFighters = new List<SimFighter>(16);
        float switchCooldown;
        bool snapPending = true;

        public SimFighter Target { get; private set; }
        public bool IsLocked => Target != null;
        public Vector3 CameraPosition { get; private set; }
        public Vector3 CameraForward { get; private set; } = new Vector3(0f, 0f, 1f);
        public float LastProbe { get; private set; }
        public LockOnBreakReason LastBreak { get; private set; }
        public int TargetChanges;
        public event Action<SimFighter, SimFighter> TargetChanged;

        public SimCameraRig(SimWorld world, CameraTuning cameraTuning = null, LockOnTuning lockOnTuning = null)
        {
            this.world = world;
            CameraTuning = cameraTuning ?? new CameraTuning();
            LockOnTuning = lockOnTuning ?? new LockOnTuning();
            Orbit = new OrbitCameraModel(CameraTuning);
            Selector = new LockOnSelector(LockOnTuning);
            Flick = new StickFlickDetector(LockOnTuning);
        }

        public void ClearLock()
        {
            SetTarget(null);
        }

        public void ForceLock(SimFighter target)
        {
            SetTarget(target);
        }

        // ThirdPersonCameraRig.Start / SnapBehindTarget.
        public void SnapBehindPlayer()
        {
            SimPlayer p = world.Player;
            if (p == null) return;
            Orbit.Snap(p.Feet, p.Yaw, CameraTuning.DefaultPitch);
            Orbit.UpdateDistance(ProbeWalls(), 0f);
            Apply();
            snapPending = false;
        }

        // ---------------------------------------------------------------- LockOnController.Update
        public void UpdateLockOn(in PlayerInputFrame input, float realDt, float gameDt)
        {
            if (switchCooldown > 0f) switchCooldown = Math.Max(0f, switchCooldown - realDt);
            SimPlayer me = world.Player;
            if (me == null || !me.IsAlive)
            {
                if (Target != null) ClearLock();
                Flick.Reset();
                return;
            }
            Vector3 eye = me.Feet + new Vector3(0f, CameraTuning.PivotHeight, 0f);
            if (Target != null) CheckLock(me, eye, gameDt);
            if (input.LockOn.Pressed)
            {
                if (IsLocked) ClearLock();
                else if (!TryAcquire(me, eye)) Orbit.BeginRecenter(me.Yaw);
            }
            if (IsLocked)
            {
                Vector2 stick = input.LookIsMouse ? Vector2.Zero : input.Look;
                int direction = Flick.Update(stick, realDt);
                if (direction == 0) direction = input.SwitchTargetDelta;
                if (direction != 0 && switchCooldown <= 0f) TrySwitch(me, eye, direction);
            }
            else
            {
                Flick.Reset();
            }
        }

        void CheckLock(SimPlayer me, Vector3 eye, float gameDt)
        {
            SimFighter current = Target;
            LockOnBreakReason reason;
            if (current == null || !current.Active || !current.IsAlive) reason = LockOnBreakReason.TargetDead;
            else
            {
                Vector3 aim = current.AimPoint;
                bool visible = HasLineOfSight(eye, aim);
                reason = Selector.UpdateBreak(true, visible, Vector3.Distance(eye, aim), gameDt);
            }
            if (reason == LockOnBreakReason.None) return;
            LastBreak = reason;
            if (reason == LockOnBreakReason.TargetDead && LockOnTuning.AutoRetargetOnKill && TryAcquire(me, eye)) return;
            ClearLock();
        }

        bool TryAcquire(SimPlayer me, Vector3 eye)
        {
            Gather(me, eye);
            int index = Selector.PickInitial(candidates, eye, CameraPosition, CameraForward);
            if (index < 0) return false;
            SetTarget(candidateFighters[index]);
            return true;
        }

        void TrySwitch(SimPlayer me, Vector3 eye, int direction)
        {
            SimFighter current = Target;
            if (current == null) return;
            Gather(me, eye);
            int index = Selector.PickNext(candidates, current.Id, current.AimPoint, direction, eye, CameraPosition, Orbit.Yaw);
            if (index < 0) return;
            SetTarget(candidateFighters[index]);
            switchCooldown = Math.Max(0f, LockOnTuning.SwitchCooldown);
        }

        void Gather(SimPlayer me, Vector3 eye)
        {
            candidates.Clear();
            candidateFighters.Clear();
            float range = Math.Max(0f, LockOnTuning.AcquireRange);
            IReadOnlyList<SimFighter> all = world.Fighters;
            for (int i = 0; i < all.Count; i++)
            {
                SimFighter f = all[i];
                if (f == null || f == me || f.Team == me.Team || !f.IsAlive || !f.Active) continue;
                Vector3 aim = f.AimPoint;
                if (Vector3.DistanceSquared(aim, eye) > range * range) continue;
                candidates.Add(new LockOnCandidate { Id = f.Id, Point = aim, IsAlive = true, HasLineOfSight = HasLineOfSight(eye, aim) });
                candidateFighters.Add(f);
            }
        }

        bool HasLineOfSight(Vector3 eye, Vector3 aim)
        {
            if (!world.Level.IsBlocked(eye, aim)) return true;
            return !world.Level.IsBlocked(CameraPosition, aim);
        }

        void SetTarget(SimFighter next)
        {
            if (ReferenceEquals(Target, next)) return;
            SimFighter previous = Target;
            Target = next;
            Selector.ResetBreakTimer();
            Flick.Reset();
            TargetChanges++;
            TargetChanged?.Invoke(previous, next);
            world.Record(-1, "lock", next != null ? next.Name + "#" + next.Id : "none");
        }

        // ---------------------------------------------------------------- ThirdPersonCameraRig.LateUpdate
        public void LateUpdate(in PlayerInputFrame input, float realDt, float gameDt)
        {
            SimPlayer p = world.Player;
            if (p == null) return;
            if (snapPending) SnapBehindPlayer();
            var camInput = new OrbitCameraInput { Look = input.Look, LookIsMouse = input.LookIsMouse };
            if (Target != null)
            {
                camInput.HasLockTarget = true;
                camInput.LockTargetPoint = Target.AimPoint;
            }
            Orbit.UpdatePivot(p.Feet, gameDt);
            Orbit.UpdateOrientation(in camInput, realDt);
            LastProbe = ProbeWalls();
            Orbit.UpdateDistance(LastProbe, realDt);
            Apply();
        }

        void Apply()
        {
            CameraPosition = Orbit.CameraPosition;
            CameraForward = Orbit.Forward;
        }

        float ProbeWalls()
        {
            float length = Orbit.DesiredDistance;
            if (!(length > 0f)) return 0f;
            Vector3 pivot = Orbit.Pivot;
            Vector3 back = -Orbit.Forward;
            float radius = Math.Max(0f, CameraTuning.CollisionRadius);
            if (radius <= 0.001f) return RayDistance(pivot, back, length);
            if (world.Level.IsOverlapping(pivot, radius))
            {
                float d = RayDistance(pivot, back, length);
                return d < length ? Math.Max(0f, d - radius) : length;
            }
            return world.Level.SphereCast(pivot, radius, back, length, out float hit) ? hit : length;
        }

        float RayDistance(Vector3 from, Vector3 dir, float length)
        {
            float best = length;
            Vector3 to = from + dir * length;
            for (int i = 0; i < world.Level.Solids.Count; i++)
            {
                Aabb b = world.Level.Solids[i];
                if (b.OverlapsSphere(from, 0f)) continue; // a ray starting inside a collider doesn't hit it
                if (b.Segment(from, to, out float t) && t * length < best) best = t * length;
            }
            return best;
        }

        // Where a world point lands on a 16:9 screen with this vertical FOV: x, y in -1..1 (0 = centre), z < 0 = behind.
        public Vector3 ScreenPoint(Vector3 worldPoint, float aspect = 16f / 9f)
        {
            Vector3 f = Orbit.Forward;
            Vector3 right = Directions.RightFromYaw(Orbit.Yaw);
            Vector3 up = Vector3.Cross(f, right);   // left-handed: forward x right = up
            if (up.Y < 0f) up = -up;
            Vector3 d = worldPoint - CameraPosition;
            float z = Vector3.Dot(d, f);
            float halfV = (float)Math.Tan(CameraTuning.BaseFov * 0.5f * Directions.Deg2Rad);
            float halfH = halfV * aspect;
            if (z <= 1e-4f) return new Vector3(0f, 0f, -1f);
            return new Vector3(Vector3.Dot(d, right) / (z * halfH), Vector3.Dot(d, up) / (z * halfV), z);
        }
    }
}
