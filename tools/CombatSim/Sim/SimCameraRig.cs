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
            TuningOverrides.ApplyCamera(CameraTuning);
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
            ApplyCollision(0f);
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
        const float ProbeSkin = 0.02f;   // ThirdPersonCameraRig.ProbeSkin

        public void LateUpdate(in PlayerInputFrame input, float realDt, float gameDt)
        {
            SimPlayer p = world.Player;
            if (p == null) return;
            if (snapPending) SnapBehindPlayer();
            // ThirdPersonCameraRig (60cb8ee): held, not pressed; the model swaps after CameraTuning.ShoulderSwapHoldTime.
            var camInput = new OrbitCameraInput { Look = input.Look, LookIsMouse = input.LookIsMouse, SwapShoulderHeld = input.SwapShoulder.Held };
            if (Target != null)
            {
                camInput.HasLockTarget = true;
                camInput.LockTargetPoint = Target.AimPoint;
            }
            if (CameraTuning.CombatPullback > 0f || CameraTuning.CombatPullbackHeight > 0f)
            {
                camInput.HasFoe = FindNearestFoe(p, out float foeDistance);
                camInput.NearestFoeDistance = foeDistance;
            }
            Orbit.UpdatePivot(p.Feet, gameDt);
            Orbit.UpdateOrientation(in camInput, realDt);
            ApplyCollision(realDt);
            Apply();
        }

        // Mirror of ThirdPersonCameraRig.ApplyCollision: up from the pivot, out to the shoulder, back from it.
        void ApplyCollision(float realDt)
        {
            float radius = Math.Max(0f, CameraTuning.CollisionRadius);
            Orbit.UpdateLift(Probe(Orbit.Pivot, new Vector3(0f, 1f, 0f), Orbit.DesiredLift, radius), realDt);
            Vector3 lifted = Orbit.LiftedPivot;
            Vector3 right = Orbit.Right;
            float reach = Orbit.ShoulderReach;
            LastFreeRight = Probe(lifted, right, reach, radius);
            LastFreeLeft = Probe(lifted, -right, reach, radius);
            // Round 2 (report 02, NEW-03): the offset must also fit at the camera's end.
            Vector3 cameraEnd = Orbit.CameraEndCentre;
            float freeRightAtCamera = Probe(cameraEnd, right, reach, radius);
            float freeLeftAtCamera = Probe(cameraEnd, -right, reach, radius);
            Orbit.UpdateShoulder(LastFreeRight, LastFreeLeft, freeRightAtCamera, freeLeftAtCamera, realDt);
            Vector3 shoulder = Orbit.ShoulderPoint, back = -Orbit.Forward;
            LastProbe = Probe(shoulder, back, Orbit.DesiredDistance, radius);
            LastCameraFree = CameraFree(shoulder, back, Orbit.Distance, radius);
            float lookAhead = CameraTuning.CollisionLookAhead > 0f
                ? LookAhead(shoulder, back, Orbit.DesiredDistance, radius + CameraTuning.CollisionLookAhead)
                : float.PositiveInfinity;
            Orbit.UpdateDistance(LastProbe, LastCameraFree, lookAhead, realDt);
        }

        // ThirdPersonCameraRig.LookAhead: the fatter probe; says nothing (open) when it starts overlapping a wall.
        float LookAhead(Vector3 shoulder, Vector3 back, float length, float fatRadius)
        {
            if (!(length > 0f) || !(fatRadius > 0f)) return float.PositiveInfinity;
            if (world.Level.IsOverlapping(shoulder, fatRadius)) return float.PositiveInfinity;
            return world.Level.SphereCast(shoulder, fatRadius, back, length, out float hit) ? Math.Max(0f, hit - ProbeSkin) : float.PositiveInfinity;
        }

        // ThirdPersonCameraRig.CameraFree: how far the camera, left where it is, could slide toward the shoulder point.
        float CameraFree(Vector3 shoulder, Vector3 back, float distance, float radius)
        {
            if (!(distance > 0f)) return 0f;
            Vector3 position = shoulder + back * distance;
            if (world.Level.IsOverlapping(position, Math.Max(radius, 0.001f))) return 0f;
            return Probe(position, -back, distance, radius);
        }

        public float LastCameraFree { get; private set; }

        public float LastFreeRight { get; private set; }
        public float LastFreeLeft { get; private set; }

        float Probe(Vector3 origin, Vector3 direction, float length, float radius)
        {
            if (!(length > 0f)) return 0f;
            if (radius <= 0.001f)
            {
                float d = RayDistance(origin, direction, length);
                return d < length ? Math.Max(0f, d - ProbeSkin) : length;
            }
            if (world.Level.IsOverlapping(origin, radius))
            {
                float d = RayDistance(origin, direction, length);
                return d < length ? Math.Max(0f, d - radius - ProbeSkin) : length;
            }
            return world.Level.SphereCast(origin, radius, direction, length, out float hit) ? Math.Max(0f, hit - ProbeSkin) : length;
        }

        bool FindNearestFoe(SimPlayer p, out float distance)
        {
            distance = float.PositiveInfinity;
            float best = float.PositiveInfinity;
            IReadOnlyList<SimFighter> all = world.Fighters;
            for (int i = 0; i < all.Count; i++)
            {
                SimFighter f = all[i];
                if (f == p || f.Team == p.Team || !f.IsAlive || !f.Active) continue;
                float sq = Vector3.DistanceSquared(f.Feet, p.Feet);
                if (sq < best) best = sq;
            }
            if (float.IsPositiveInfinity(best)) return false;
            distance = (float)Math.Sqrt(best);
            return true;
        }

        void Apply()
        {
            CameraPosition = Orbit.CameraPosition;
            CameraForward = Orbit.Forward;
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
