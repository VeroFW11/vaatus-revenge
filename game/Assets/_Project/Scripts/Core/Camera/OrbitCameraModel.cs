using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // What the orbit camera needs from the outside world each frame, besides where the player is.
    public struct OrbitCameraInput
    {
        public Vector2 Look;            // PlayerInputFrame.Look: stick -1..1, or mouse pixels this frame
        public bool LookIsMouse;        // PlayerInputFrame.LookIsMouse
        public bool HasLockTarget;      // locked on? Look is then ignored and the camera frames the target
        public Vector3 LockTargetPoint; // the lock-on target's aim point, world space
    }

    // The third-person camera's rules in plain C#: orbit angles, free look, lock-on framing, pivot follow and
    // collision distance. The Unity rig (ThirdPersonCameraRig) feeds it and copies the result onto a Camera;
    // keeping the maths here means it can be unit-tested and used by the headless harness.
    //
    // The camera orbits a pivot at the player's neck: it sits Distance metres behind the pivot along the look
    // direction given by Yaw and Pitch, so the player always stays at the centre of the screen.
    //
    // Call once per frame, in this order:
    //   1. UpdatePivot(playerFeet, gameDeltaTime)    - follow the player
    //   2. UpdateOrientation(input, realDeltaTime)   - free look or lock-on framing
    //   3. probe for walls from Pivot along -Forward, up to DesiredDistance
    //   4. UpdateDistance(freeDistance, realDeltaTime)
    // then place the camera at CameraPosition looking along Forward.
    public sealed class OrbitCameraModel
    {
        // A frame hitch (loading, a breakpoint) never turns the camera further than this much stick time,
        // otherwise a held stick would jump the view a long way in one frame.
        const float MaxStickStep = 0.1f;
        // A critically damped spring (Smooth.Damp) gets about 98% of the way in three smooth times.
        const float SmoothTimesToSettle = 3f;
        // Recentring ends once this close; the last sliver is invisible.
        const float RecenterDoneDegrees = 0.1f;
        // Hard pitch limit whatever the tuning says: looking exactly straight up or down is degenerate.
        const float PitchLimit = 89f;

        CameraTuning tuning;
        bool hasPivot;
        bool locked;
        bool recentering;
        float recenterYaw;
        float yawVelocity;
        float pitchVelocity;
        float desiredDistanceVelocity;
        float distanceVelocity;
        Vector3 pivotVelocity;

        public OrbitCameraModel(CameraTuning tuning)
        {
            Tuning = tuning;
            Pitch = ClampPitch(this.tuning.DefaultPitch);
            DesiredDistance = WantedDistance();
            Distance = DesiredDistance;
        }

        // Swappable at any time (e.g. a different tuning asset); the camera keeps its current framing.
        public CameraTuning Tuning
        {
            get { return tuning; }
            set { tuning = value ?? new CameraTuning(); }
        }

        public float Yaw { get; private set; }             // degrees, (-180, 180], clockwise from +Z
        public float Pitch { get; private set; }           // degrees, positive looks down
        public Vector3 Pivot { get; private set; }         // the point the camera orbits (player's neck, smoothed)
        public float DesiredDistance { get; private set; } // where the camera would like to sit, before walls
        public float Distance { get; private set; }        // where it actually sits after collision
        public bool IsLockedOn => locked;
        public bool IsRecentering => recentering;
        public bool HasPivot => hasPivot;

        public Vector3 Forward => Directions.FromYawPitch(Yaw, Pitch);
        public Vector3 CameraPosition => Pivot - Forward * Distance;

        // Cuts straight to a framing with no smoothing: game start, respawn, a new follow target.
        public void Snap(Vector3 followPosition, float yaw, float pitch)
        {
            if (CameraMath.IsFinite(yaw)) Yaw = Angles.Wrap180(yaw);
            if (CameraMath.IsFinite(pitch)) Pitch = ClampPitch(pitch);
            yawVelocity = 0f;
            pitchVelocity = 0f;
            recentering = false;
            SnapPivot(followPosition);
            DesiredDistance = WantedDistance();
            desiredDistanceVelocity = 0f;
            // Starts fully out; the next UpdateDistance pulls it in instantly if a wall is in the way.
            Distance = DesiredDistance;
            distanceVelocity = 0f;
        }

        // Moves the pivot onto the player without smoothing, keeping the camera angles (teleports).
        public void SnapPivot(Vector3 followPosition)
        {
            Vector3 target = PivotTarget(followPosition);
            if (!CameraMath.IsFinite(target)) return;
            Pivot = target;
            pivotVelocity = Vector3.Zero;
            hasPivot = true;
        }

        // Smoothly swings the camera behind the player (to the given yaw) and back to the default pitch.
        // Any look input cancels it, so the player is never fighting the camera.
        public void BeginRecenter(float yaw)
        {
            if (!CameraMath.IsFinite(yaw)) return;
            recenterYaw = Angles.Wrap180(yaw);
            recentering = true;
            yawVelocity = 0f;
            pitchVelocity = 0f;
        }

        public void CancelRecenter()
        {
            recentering = false;
        }

        // Step 1. followPosition is the player's feet. Uses GAME time: the player moves in game time, so
        // following in game time means the pivot freezes with the player during hitstop instead of drifting,
        // and trails by the same distance in slow motion as at full speed.
        public void UpdatePivot(Vector3 followPosition, float gameDeltaTime)
        {
            Vector3 target = PivotTarget(followPosition);
            if (!CameraMath.IsFinite(target)) return;

            float snapDistance = Math.Max(0f, tuning.TeleportSnapDistance);
            if (!hasPivot || (snapDistance > 0f && Vector3.DistanceSquared(target, Pivot) > snapDistance * snapDistance))
            {
                SnapPivot(followPosition);
                return;
            }

            float dt = CameraMath.SafeDeltaTime(gameDeltaTime);
            if (dt <= 0f) return;

            // Horizontal and vertical are smoothed separately: tight horizontally so the player stays centred,
            // looser vertically so jumps and steps don't bob the view.
            float vx = pivotVelocity.X, vy = pivotVelocity.Y, vz = pivotVelocity.Z;
            float x = Smooth.Damp(Pivot.X, target.X, ref vx, tuning.PivotHorizontalSmoothTime, dt);
            float z = Smooth.Damp(Pivot.Z, target.Z, ref vz, tuning.PivotHorizontalSmoothTime, dt);
            float y = Smooth.Damp(Pivot.Y, target.Y, ref vy, tuning.PivotVerticalSmoothTime, dt);

            // Safety net: cap how far the pivot may trail, so a long fall can't leave the player off screen.
            // Normal jumps, dodges and lunges stay inside the cap, because hitting it stops the smoothing dead.
            float lagX = x - target.X;
            float lagZ = z - target.Z;
            float lag = (float)Math.Sqrt(lagX * lagX + lagZ * lagZ);
            float maxLag = Math.Max(0f, tuning.PivotMaxHorizontalLag);
            if (lag > maxLag)
            {
                float keep = maxLag / lag;
                x = target.X + lagX * keep;
                z = target.Z + lagZ * keep;
            }
            float maxVerticalLag = Math.Max(0f, tuning.PivotMaxVerticalLag);
            y = target.Y + Angles.Clamp(y - target.Y, -maxVerticalLag, maxVerticalLag);

            Pivot = new Vector3(x, y, z);
            pivotVelocity = new Vector3(vx, vy, vz);
        }

        // Step 2. Uses REAL (unscaled) time so the camera stays responsive during hitstop and slow motion.
        public void UpdateOrientation(in OrbitCameraInput input, float realDeltaTime)
        {
            float dt = CameraMath.SafeDeltaTime(realDeltaTime);
            bool lockNow = input.HasLockTarget && CameraMath.IsFinite(input.LockTargetPoint);
            if (lockNow != locked)
            {
                locked = lockNow;
                yawVelocity = 0f;
                pitchVelocity = 0f;
                recentering = false;
            }

            if (locked)
            {
                // While locked on, the right stick switches targets (LockOnController), so look input is ignored.
                FrameLockTarget(input.LockTargetPoint, dt);
            }
            else if (ApplyFreeLook(input.Look, input.LookIsMouse, dt))
            {
                recentering = false;
            }
            else if (recentering)
            {
                StepRecenter(dt);
            }

            DesiredDistance = Smooth.Damp(DesiredDistance, WantedDistance(), ref desiredDistanceVelocity,
                tuning.DistanceSmoothTime, dt);
        }

        // Step 4. maxDistance is how far the camera can go back from the Pivot before touching a wall this
        // frame (DesiredDistance or more when nothing is in the way).
        public void UpdateDistance(float maxDistance, float realDeltaTime)
        {
            float dt = CameraMath.SafeDeltaTime(realDeltaTime);
            float limit = DesiredDistance;
            if (CameraMath.IsFinite(maxDistance)) limit = Angles.Clamp(maxDistance, 0f, DesiredDistance);

            if (limit <= Distance)
            {
                // Pull in instantly: a camera that eased in would spend several frames inside the wall.
                Distance = limit;
                distanceVelocity = 0f;
            }
            else
            {
                // Ease back out, so the view doesn't pop every time a pillar stops blocking it.
                float smoothTime = Math.Max(0f, tuning.CollisionEaseOutTime) / SmoothTimesToSettle;
                Distance = Math.Min(limit, Smooth.Damp(Distance, limit, ref distanceVelocity, smoothTime, dt));
            }
        }

        // Radial response curve for a stick: magnitude^exponent, same direction. Small tilts turn slowly for
        // precise aiming while full tilt still gives full speed. Applied to the whole stick (not per axis) so
        // diagonals aren't slowed down.
        public static Vector2 ApplyResponseCurve(Vector2 stick, float exponent)
        {
            float magnitude = stick.Length();
            if (!(magnitude > 1e-5f) || !CameraMath.IsFinite(magnitude)) return Vector2.Zero;
            float power = exponent > 0f && CameraMath.IsFinite(exponent) ? exponent : 1f;
            float curved = (float)Math.Pow(Math.Min(1f, magnitude), power);
            return stick * (curved / magnitude);
        }

        bool ApplyFreeLook(Vector2 look, bool isMouse, float dt)
        {
            if (!CameraMath.IsFinite(look)) return false;

            float yawDelta;
            float pitchDelta;
            if (isMouse)
            {
                // A mouse delta is already "how far the hand moved this frame". Multiplying it by deltaTime
                // would make the camera turn slower at high frame rates and faster at low ones.
                yawDelta = look.X * tuning.MouseDegreesPerPixel;
                pitchDelta = -look.Y * tuning.MouseDegreesPerPixel;
            }
            else
            {
                // A stick is a speed: how far it's tilted says how fast to turn, so it IS scaled by time.
                Vector2 curved = ApplyResponseCurve(look, tuning.StickResponseExponent);
                float step = Math.Min(dt, MaxStickStep);
                yawDelta = curved.X * tuning.StickYawSpeed * step;
                pitchDelta = -curved.Y * tuning.StickPitchSpeed * step;
            }
            // Pushing up (stick or mouse) looks up, which is a smaller pitch.
            if (tuning.InvertY) pitchDelta = -pitchDelta;
            if (yawDelta == 0f && pitchDelta == 0f) return false;

            Yaw = Angles.Wrap180(Yaw + yawDelta);
            Pitch = ClampPitch(Pitch + pitchDelta);
            return true;
        }

        void FrameLockTarget(Vector3 targetPoint, float dt)
        {
            Vector3 toTarget = targetPoint - Pivot;

            // Yaw: swing round to face the target, always the short way (DampAngle). A target (almost) straight
            // above or below the pivot has no meaningful direction, so hold the yaw rather than spin.
            float deadZone = Math.Max(1e-3f, tuning.LockOnYawDeadZone);
            if (CameraMath.HorizontalLength(toTarget) > deadZone)
            {
                float targetYaw = Directions.YawOf(toTarget, Yaw);
                Yaw = Angles.Wrap180(Smooth.DampAngle(Yaw, targetYaw, ref yawVelocity, tuning.LockOnYawSmoothTime, dt));
            }
            else
            {
                yawVelocity = 0f;
            }

            // Pitch: rest at LockOnPitch, but tilt just enough that the target stays within the framing limits
            // above/below the screen centre. Measured from the pivot, which errs on the side of tilting a little
            // early (the camera sits behind the pivot, so the target really appears slightly nearer the centre).
            float pitchToTarget = Directions.PitchOf(toTarget);
            float above = Math.Max(0f, tuning.LockOnFramingAbove);
            float below = Math.Max(0f, tuning.LockOnFramingBelow);
            float desired = ClampPitch(Angles.Clamp(tuning.LockOnPitch, pitchToTarget - below, pitchToTarget + above));
            Pitch = ClampPitch(Smooth.Damp(Pitch, desired, ref pitchVelocity, tuning.LockOnPitchSmoothTime, dt));
        }

        void StepRecenter(float dt)
        {
            float targetPitch = ClampPitch(tuning.DefaultPitch);
            Yaw = Angles.Wrap180(Smooth.DampAngle(Yaw, recenterYaw, ref yawVelocity, tuning.RecenterSmoothTime, dt));
            Pitch = ClampPitch(Smooth.Damp(Pitch, targetPitch, ref pitchVelocity, tuning.RecenterSmoothTime, dt));

            if (Math.Abs(Angles.Delta(Yaw, recenterYaw)) < RecenterDoneDegrees && Math.Abs(Pitch - targetPitch) < RecenterDoneDegrees)
            {
                Yaw = recenterYaw;
                Pitch = targetPitch;
                yawVelocity = 0f;
                pitchVelocity = 0f;
                recentering = false;
            }
        }

        float WantedDistance()
        {
            float wanted = locked ? tuning.LockedDistance : tuning.FreeDistance;
            float floor = Math.Max(0f, tuning.MinDistance);
            return CameraMath.IsFinite(wanted) ? Math.Max(floor, wanted) : floor;
        }

        Vector3 PivotTarget(Vector3 followPosition)
        {
            return followPosition + new Vector3(0f, tuning.PivotHeight, 0f);
        }

        float ClampPitch(float pitch)
        {
            float low = Angles.Clamp(Math.Min(tuning.MinPitch, tuning.MaxPitch), -PitchLimit, PitchLimit);
            float high = Angles.Clamp(Math.Max(tuning.MinPitch, tuning.MaxPitch), -PitchLimit, PitchLimit);
            if (!CameraMath.IsFinite(pitch)) pitch = 0f;
            return Angles.Clamp(pitch, low, high);
        }
    }
}
