using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // What the orbit camera needs from the outside world each frame, besides where the player is.
    public struct OrbitCameraInput
    {
        public Vector2 Look;             // PlayerInputFrame.Look: stick -1..1, or mouse pixels this frame
        public bool LookIsMouse;         // PlayerInputFrame.LookIsMouse
        public bool HasLockTarget;       // locked on? Look is then ignored and the camera frames the target
        public Vector3 LockTargetPoint;  // the lock-on target's aim point, world space
        public bool SwapShoulder;        // swap shoulders right now (a script or menu; no hold needed)
        public bool SwapShoulderHeld;    // PlayerInputFrame.SwapShoulder.Held: swaps after ShoulderSwapHoldTime, once per hold
        public bool HasFoe;              // is any living foe around? (for the combat pull-back)
        public float NearestFoeDistance; // metres from the player to the nearest living foe, when HasFoe
    }

    // The third-person camera's rules in plain C#: orbit angles, free look, lock-on framing, pivot follow,
    // over-the-shoulder offset, combat pull-back and collision. The Unity rig (ThirdPersonCameraRig) feeds it and
    // copies the result onto a Camera; keeping the maths here means it can be unit-tested and used by the
    // headless harness.
    //
    // The camera orbits a pivot at the player's neck. It looks past one shoulder (like Marvel's Spider-Man 2): the
    // ShoulderPoint is the pivot moved ShoulderOffset metres to the camera's right (negative = left) and Lift metres
    // up, and the camera sits Distance metres behind that point along the look direction given by Yaw and Pitch.
    // So the fighter stays at a fixed spot to one side of the screen. With a zero offset and no lift the shoulder
    // point IS the pivot: the classic centred framing.
    //
    // Call once per frame, in this order:
    //   1. UpdatePivot(playerFeet, gameDeltaTime)         - follow the player
    //   2. UpdateOrientation(input, realDeltaTime)        - free look or lock-on framing, shoulder side, combat framing,
    //                                                       and the rise over the head when a wall is close behind
    //   3. UpdateLift(freeUp, realDeltaTime)              - optional: room above Pivot, up to DesiredLift
    //   4. UpdateShoulder(freeRight, freeLeft, [freeRightAtCamera, freeLeftAtCamera,] realDt)
    //                                                     - optional: room either side of LiftedPivot (and of
    //                                                       CameraEndCentre), up to ShoulderReach
    //   5. UpdateDistance(freeDistance, [cameraFree,] realDeltaTime)
    //                                                     - room behind ShoulderPoint along -Forward, up to DesiredDistance
    //                                                       (and how far the camera could slide in from where it is)
    // then place the camera at CameraPosition looking along Forward. Steps 3 and 4 are optional: skip them and the
    // camera simply ignores walls at the side and above.
    //
    // WALLS (report 02, NEW-03). Three rules keep the view steady near pillars and wall ends:
    //   - The camera never sits inside level geometry: if its sphere touches something, it moves in front of it on
    //     the same frame.
    //   - Something passing between the camera and the player (a pillar edge while orbiting, a roof edge) that the
    //     camera itself isn't touching is ignored for OcclusionGraceTime. Most of those clear by themselves, so the
    //     camera doesn't hop in and out; one that stays is cut in front of.
    //   - Closer than MinCollisionDistance, the camera rises and tilts down over the player's head instead of
    //     sliding into it (a pillar at your back). The shoulder offset also yields to walls at the camera's end, so
    //     walking past the end of a wall doesn't swing the camera into the wall's end face.
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
        // Offsets smaller than this count as "centred" (no shoulder, so nothing to swap).
        const float CentredOffset = 1e-4f;
        // Probe results this small count as "touching".
        const float CollisionEpsilon = 1e-3f;

        CameraTuning tuning;
        bool hasPivot;
        bool locked;
        bool recentering;
        float recenterYaw;
        float yawVelocity;
        float pitchVelocity;
        float baseDistance;          // the free/locked distance, smoothed (DesiredDistance adds the combat pull-back)
        float desiredDistanceVelocity;
        float distanceVelocity;
        Vector3 pivotVelocity;

        // Shoulder: which side the player picked, whether a wall swapped it for now, and the offset itself.
        int chosenSide = 1;          // +1 = the side the tuning's offset points to, -1 = the other one (swap button)
        float swapHoldTime;          // how long the swap button has been held
        bool swapHoldUsed;           // this hold already swapped; release to swap again
        bool autoSwapped;
        float swapBackTimer;
        float wantedOffset;          // eased towards side x size; walls not considered yet
        float wantedOffsetVelocity;
        bool shoulderProbed;         // UpdateShoulder has been called at least once, so walls are known
        float shoulderRoom;          // free space on the offset's side, eased like the distance (in fast, out slowly)
        float shoulderRoomVelocity;
        float probedOffset;          // the offset after walls

        // Combat framing: 0 = normal, 1 = fully pulled back.
        float combatBlend;
        float combatVelocity;
        float combatHold;            // seconds left before the view may narrow again
        bool liftProbed;
        float probedLift;
        float liftVelocity;

        // Walls behind the camera: the rise over the head, and how long a pillar has been blocking the view.
        float risePitch;             // degrees added to Pitch (looking further down from higher up)
        float riseVelocity;
        float roomBehind = float.PositiveInfinity; // horizontal room behind the shoulder point, from the last probe
        float occludedTime;
        float yawRate;               // degrees per second the view turned last frame (real time), for the swing sweep

        public OrbitCameraModel(CameraTuning tuning)
        {
            Tuning = tuning;
            Pitch = ClampPitch(this.tuning.DefaultPitch);
            ResetFraming();
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
        public float Distance { get; private set; }        // where it actually sits after collision (from ShoulderPoint)
        public bool IsLockedOn => locked;
        public bool IsRecentering => recentering;
        public bool HasPivot => hasPivot;

        // The pitch the camera actually looks at: the player's Pitch plus the rise over the head near walls. Use this
        // (or Forward) to aim the rendered camera; Pitch alone is the player's own choice.
        public float ViewPitch => ClampTotalPitch(Pitch + risePitch);
        // The direction the camera looks.
        public Vector3 Forward => Directions.FromYawPitch(Yaw, ViewPitch);
        // Degrees the camera currently tilts down extra because a wall is close behind (0 in the open).
        public float CollisionRise => risePitch;
        // True while something blocks the view but not the camera (it waits, then glides in front: UpdateDistance).
        public bool IsWaitingOutOcclusion => occludedTime > 0f;
        // Flat right-hand direction of the camera: the direction the shoulder offset is measured along.
        public Vector3 Right => Directions.RightFromYaw(Yaw);

        // How far round the view is expected to swing in CollisionSweepTime at its current turn rate (degrees, signed,
        // capped at CollisionSweepMaxAngle). 0 when it isn't turning or the sweep is off.
        public float SweepYawDelta
        {
            get
            {
                float time = Math.Max(0f, Finite(tuning.CollisionSweepTime));
                float cap = Angles.Clamp(Finite(tuning.CollisionSweepMaxAngle), 0f, 180f);
                return Angles.Clamp(Finite(yawRate) * time, -cap, cap);
            }
        }

        // True while the camera is expected to move noticeably over CollisionSweepTime (turning at least a degree or
        // travelling at least 10 cm): only then are the sweep probes worth casting.
        public bool IsSweeping
        {
            get
            {
                if (Math.Abs(SweepYawDelta) >= 1f) return true;
                float time = Math.Max(0f, Finite(tuning.CollisionSweepTime));
                float travel = (float)Math.Sqrt(pivotVelocity.X * pivotVelocity.X + pivotVelocity.Z * pivotVelocity.Z) * time;
                return CameraMath.IsFinite(travel) && travel >= 0.1f;
            }
        }

        // Where the shoulder point will be 'share' (0..1) of the way through CollisionSweepTime if the player keeps moving
        // as the pivot is now (a dodge or sprint past a pillar carries the camera into it just like a swing does).
        public Vector3 SweepOrigin(float share)
        {
            float s = CameraMath.IsFinite(share) ? Angles.Clamp(share, 0f, 1f) : 0f;
            Vector3 move = new Vector3(pivotVelocity.X, 0f, pivotVelocity.Z) * (Math.Max(0f, Finite(tuning.CollisionSweepTime)) * s);
            return CameraMath.IsFinite(move) ? ShoulderPoint + move : ShoulderPoint;
        }

        // The direction from ShoulderPoint back to where the camera would sit 'share' (0..1) of the way along that swing.
        // The rig probes along a few of these (ThirdPersonCameraRig.SweepProbe) and passes the shortest to UpdateDistance.
        public Vector3 SweepBack(float share)
        {
            float s = CameraMath.IsFinite(share) ? Angles.Clamp(share, 0f, 1f) : 0f;
            return -Directions.FromYawPitch(Angles.Wrap180(Yaw + SweepYawDelta * s), ViewPitch);
        }

        // Shoulder offset in metres along Right (+ = over the right shoulder, fighter left of centre), before walls...
        public float DesiredShoulderOffset => wantedOffset;
        // ...and after walls (the same as DesiredShoulderOffset if UpdateShoulder is never called).
        public float ShoulderOffset => shoulderProbed ? probedOffset : wantedOffset;
        // +1 = over the right shoulder, -1 = the left, 0 = centred. Includes a swap caused by a wall.
        public int ShoulderSide
        {
            get
            {
                float target = TargetOffset();
                return target > 0f ? 1 : (target < 0f ? -1 : 0);
            }
        }
        public bool IsShoulderAutoSwapped => autoSwapped;
        // How far to probe either side of LiftedPivot for UpdateShoulder: the largest offset the tuning can ask for,
        // or the current one if that's bigger (the offset is still easing down after a tuning change).
        public float ShoulderReach => Math.Max(Math.Abs(wantedOffset),
            Math.Max(Math.Abs(Finite(tuning.ShoulderOffset)), Math.Abs(Finite(tuning.LockedShoulderOffset))));

        // Combat framing, 0 (none) to 1 (fully pulled back), and the height it adds before/after ceilings.
        public float CombatFraming => combatBlend;
        public float DesiredLift => combatBlend * Math.Max(0f, Finite(tuning.CombatPullbackHeight));
        public float Lift => liftProbed ? probedLift : DesiredLift;

        public Vector3 LiftedPivot => Pivot + new Vector3(0f, Lift, 0f);
        // The point the camera looks past: the centre of the screen always passes through it.
        public Vector3 ShoulderPoint => LiftedPivot + Right * ShoulderOffset;
        public Vector3 CameraPosition => ShoulderPoint - Forward * Distance;
        // Where the camera would be with no shoulder offset: the rig probes sideways from here too (UpdateShoulder),
        // so the offset also fits at the camera's end, not just at the player's.
        public Vector3 CameraEndCentre => LiftedPivot - Forward * Distance;

        // Cuts straight to a framing with no smoothing: game start, respawn, a new follow target.
        // Keeps the player's chosen shoulder.
        public void Snap(Vector3 followPosition, float yaw, float pitch)
        {
            if (CameraMath.IsFinite(yaw)) Yaw = Angles.Wrap180(yaw);
            if (CameraMath.IsFinite(pitch)) Pitch = ClampPitch(pitch);
            yawVelocity = 0f;
            pitchVelocity = 0f;
            yawRate = 0f;
            recentering = false;
            SnapPivot(followPosition);
            ResetFraming();
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

        // Moves the camera to the other shoulder (smoothly, over ShoulderSmoothTime). This becomes the player's
        // choice, so it also cancels a temporary swap a wall caused.
        public void SwapShoulder()
        {
            chosenSide = -chosenSide;
            autoSwapped = false;
            swapBackTimer = 0f;
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

            // Horizontal and vertical are smoothed separately: tight horizontally so the player stays put on screen,
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
            float yawBefore = Yaw;
            if (input.SwapShoulder) SwapShoulder();
            UpdateSwapHold(input.SwapShoulderHeld, dt);

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

            // Slide towards the current shoulder and offset size (swaps and lock changes ease over; never a jump).
            wantedOffset = Smooth.Damp(wantedOffset, TargetOffset(), ref wantedOffsetVelocity, tuning.ShoulderSmoothTime, dt);

            UpdateCombatFraming(input, dt);
            baseDistance = Smooth.Damp(baseDistance, WantedDistance(), ref desiredDistanceVelocity, tuning.DistanceSmoothTime, dt);
            DesiredDistance = baseDistance + combatBlend * Math.Max(0f, Finite(tuning.CombatPullback));
            UpdateRise(dt);
            if (dt > 0f) yawRate = Angles.Delta(yawBefore, Yaw) / dt;   // paused frames keep the last rate
            if (!CameraMath.IsFinite(yawRate)) yawRate = 0f;
        }

        // Step 3 (optional). freeUp: how far the camera's collision sphere can rise from Pivot before touching a
        // ceiling (probe up to DesiredLift; infinity = nothing there). A ceiling wins instantly, the room comes
        // back gently.
        public void UpdateLift(float freeUp, float realDeltaTime)
        {
            float dt = CameraMath.SafeDeltaTime(realDeltaTime);
            float wanted = DesiredLift;
            float limit = CameraMath.IsFinite(freeUp) ? Angles.Clamp(freeUp, 0f, wanted) : wanted;
            probedLift = EaseToLimit(probedLift, limit, ref liftVelocity, dt);
            liftProbed = true;
        }

        // Step 4 (optional). freeRight / freeLeft: how far the collision sphere can move from LiftedPivot along
        // Right / -Right before touching a wall (probe up to ShoulderReach; infinity = open). The offset shrinks
        // instantly when its side is blocked, so the camera never ends up inside or behind the wall, and (if
        // AutoSwapShoulderWhenBlocked) swaps to the other shoulder until the blocked side has cleared.
        public void UpdateShoulder(float freeRight, float freeLeft, float realDeltaTime)
        {
            UpdateShoulder(freeRight, freeLeft, float.PositiveInfinity, float.PositiveInfinity, realDeltaTime);
        }

        // Same, plus the room either side of CameraEndCentre (probe up to ShoulderReach). The offset has to fit at both
        // ends: walking beside a wall, the camera trails a few metres behind you, so when your shoulder clears the
        // wall's end the camera is still alongside it. Easing the offset out then would drag the camera into the wall's
        // end face and snap it in by metres (report 02, NEW-03). Auto-swap still looks only at the player's end.
        public void UpdateShoulder(float freeRight, float freeLeft, float freeRightAtCamera, float freeLeftAtCamera, float realDeltaTime)
        {
            float dt = CameraMath.SafeDeltaTime(realDeltaTime);
            float reach = ShoulderReach;
            float right = CameraMath.IsFinite(freeRight) ? Angles.Clamp(freeRight, 0f, reach) : reach;
            float left = CameraMath.IsFinite(freeLeft) ? Angles.Clamp(freeLeft, 0f, reach) : reach;
            UpdateAutoSwap(right, left, dt);
            if (CameraMath.IsFinite(freeRightAtCamera)) right = Math.Min(right, Math.Max(0f, freeRightAtCamera));
            if (CameraMath.IsFinite(freeLeftAtCamera)) left = Math.Min(left, Math.Max(0f, freeLeftAtCamera));

            // The room is tracked for whichever side the offset is on right now. Crossing sides can't pop,
            // because the offset is (almost) zero at the moment it crosses.
            float side = wantedOffset < 0f ? -1f : 1f;
            shoulderRoom = EaseToLimit(shoulderRoom, side > 0f ? right : left, ref shoulderRoomVelocity, dt);
            probedOffset = side * Math.Min(Math.Abs(wantedOffset), shoulderRoom);
            shoulderProbed = true;
        }

        // Step 5. maxDistance is how far the camera can go back from the ShoulderPoint before touching a wall this
        // frame (DesiredDistance or more when nothing is in the way). This version treats every wall as touching the
        // camera: it pulls in on the same frame.
        public void UpdateDistance(float maxDistance, float realDeltaTime)
        {
            UpdateDistance(maxDistance, 0f, realDeltaTime);
        }

        // cameraFree: how far the camera's sphere could slide from where it is now (ShoulderPoint - Forward * Distance,
        // with this frame's angles) toward ShoulderPoint before touching geometry; 0 = it already touches something.
        public void UpdateDistance(float maxDistance, float cameraFree, float realDeltaTime)
        {
            UpdateDistance(maxDistance, cameraFree, float.PositiveInfinity, realDeltaTime);
        }

        // lookAheadDistance: the same probe as maxDistance with a fatter sphere (CollisionRadius + CollisionLookAhead).
        // What it hits isn't touching the camera yet but soon may be (a pillar sliding in from the side as you strafe),
        // so the camera starts gliding in early and usually never has to jump (report 02, NEW-03). Three cases:
        //   - a wall touches the camera itself: in front of it on this frame (the camera never sits inside geometry);
        //   - a wall is between the camera and the player but not touching the camera (only the VIEW is blocked): wait
        //     OcclusionGraceTime (many pillar edges clear by themselves), then glide in over about CollisionPullInTime,
        //     never past the wall's far side. Only the wall's own thickness is ever skipped in one frame;
        //   - only the look-ahead probe is blocked: glide in, nothing is in the way.
        public void UpdateDistance(float maxDistance, float cameraFree, float lookAheadDistance, float realDeltaTime)
        {
            UpdateDistance(maxDistance, cameraFree, lookAheadDistance, float.PositiveInfinity, realDeltaTime);
        }

        // sweepDistance: the shortest probe along the coming swing (SweepBack), from ShoulderPoint with the normal radius.
        // It works like the look-ahead: while the view swings round, a pillar face the swing is about to sweep into the
        // camera starts the glide in early, so the camera is already in front of it when it arrives instead of
        // jumping 2 m in one frame (report 02, round 3). It never brings the camera closer than CollisionSweepMinDistance
        // on its own, and it feeds the rise over the head, which then also starts early.
        public void UpdateDistance(float maxDistance, float cameraFree, float lookAheadDistance, float sweepDistance, float realDeltaTime)
        {
            float dt = CameraMath.SafeDeltaTime(realDeltaTime);
            float limit = DesiredDistance;
            if (CameraMath.IsFinite(maxDistance)) limit = Angles.Clamp(maxDistance, 0f, DesiredDistance);
            // The look-ahead only brings the camera in early; it never takes it closer than MinCollisionDistance (in a
            // tight spot the fat probe grazes everything, and only real contact should bring the camera that close).
            float early = limit;
            if (CameraMath.IsFinite(lookAheadDistance))
            {
                float floor = Math.Min(limit, Math.Max(0f, Finite(tuning.MinCollisionDistance)));
                early = Math.Max(floor, Math.Min(limit, lookAheadDistance));
            }
            // The swing sweep is a real contact that is about to happen (same radius as the camera), so it may start the
            // glide closer than the look-ahead may: down to CollisionSweepMinDistance.
            if (CameraMath.IsFinite(sweepDistance))
            {
                float floor = Math.Min(limit, Math.Max(0f, Finite(tuning.CollisionSweepMinDistance)));
                early = Math.Min(early, Math.Max(floor, Math.Min(limit, sweepDistance)));
            }
            RememberRoomBehind(early);

            bool touching = !(CameraMath.IsFinite(cameraFree) && cameraFree > CollisionEpsilon);
            if (limit < Distance && touching)
            {
                occludedTime = 0f;
                distanceVelocity = 0f;
                Distance = limit;
                return;
            }
            if (early >= Distance)
            {
                occludedTime = 0f;
                Distance = EaseToLimit(Distance, early, ref distanceVelocity, dt);   // open space: ease back out
                return;
            }
            bool viewBlocked = limit < Distance;
            float farSide = viewBlocked ? Distance - cameraFree : 0f;   // the camera can't come closer than this without entering the wall
            if (!viewBlocked) occludedTime = 0f;
            else if (occludedTime < Math.Max(0f, Finite(tuning.OcclusionGraceTime)))
            {
                occludedTime += dt;     // hold still for a moment
                distanceVelocity = 0f;
                KeepWithinDesired(farSide, limit);
                return;
            }
            // Glide in, but never into the wall: at most up to its far side. Once there, the next frame's probe sees the
            // camera touching it and moves it in front.
            float smoothTime = Math.Max(0f, Finite(tuning.CollisionPullInTime)) / SmoothTimesToSettle;
            float glided = Smooth.Damp(Distance, early, ref distanceVelocity, smoothTime, dt);
            Distance = Math.Min(Distance, Math.Max(glided, farSide));
            KeepWithinDesired(farSide, limit);
        }

        // While waiting or gliding, the wanted distance may shrink under the camera (e.g. locking off). Follow it in if
        // that doesn't take the camera into the wall; otherwise move in front of the wall now.
        void KeepWithinDesired(float farSide, float limit)
        {
            if (Distance <= DesiredDistance) return;
            if (DesiredDistance >= farSide)
            {
                Distance = DesiredDistance;
                return;
            }
            Distance = limit;
            distanceVelocity = 0f;
            occludedTime = 0f;
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
            float horizontal = CameraMath.HorizontalLength(toTarget);

            // Yaw: swing round to face the target, always the short way (DampAngle). A target (almost) straight
            // above or below the pivot has no meaningful direction, so hold the yaw rather than spin.
            float deadZone = Math.Max(1e-3f, tuning.LockOnYawDeadZone);
            if (horizontal > deadZone)
            {
                // Aim from the shoulder point, not the pivot: turning a little extra puts the target on the centre
                // line while the fighter stays off to the side, so both are framed.
                float targetYaw = Directions.YawOf(toTarget, Yaw) - ShoulderAimCorrection(horizontal);
                TurnYawTowards(targetYaw, dt);
            }
            else
            {
                yawVelocity = 0f;
            }

            // Pitch: rest at LockOnPitch, but tilt just enough that the target stays within the framing limits
            // above/below the screen centre. Measured from the shoulder point (the centre of the screen passes
            // through it), which errs on the side of tilting a little early (the camera sits behind that point,
            // so the target really appears slightly nearer the centre).
            float pitchToTarget = Directions.PitchOf(targetPoint - ShoulderPoint);
            float above = Math.Max(0f, tuning.LockOnFramingAbove);
            float below = Math.Max(0f, tuning.LockOnFramingBelow);
            float desired = ClampPitch(Angles.Clamp(tuning.LockOnPitch, pitchToTarget - below, pitchToTarget + above));
            Pitch = ClampPitch(Smooth.Damp(Pitch, desired, ref pitchVelocity, tuning.LockOnPitchSmoothTime, dt));
        }

        // Swings the yaw towards targetYaw like a spring (always the short way), but never faster than
        // LockOnMaxYawSpeed. A target passing overhead flips its direction by about 180 degrees in an instant, and
        // an uncapped spring would whip the view round by 18 degrees in one frame. The spring's own speed limit
        // keeps the swing smooth; the clamp after it makes the limit a guarantee.
        void TurnYawTowards(float targetYaw, float dt)
        {
            float maxSpeed = tuning.LockOnMaxYawSpeed > 0f && CameraMath.IsFinite(tuning.LockOnMaxYawSpeed)
                ? tuning.LockOnMaxYawSpeed
                : float.PositiveInfinity;
            float before = Yaw;
            // DampAngle never overshoots, so its result is a plain step from 'before' (no wrap to undo).
            float step = Smooth.DampAngle(before, targetYaw, ref yawVelocity, tuning.LockOnYawSmoothTime, dt, maxSpeed) - before;
            if (dt > 0f && !float.IsPositiveInfinity(maxSpeed) && Math.Abs(step) > maxSpeed * dt)
            {
                step = step > 0f ? maxSpeed * dt : -maxSpeed * dt;
                yawVelocity = step / dt;
            }
            Yaw = Angles.Wrap180(before + step);
        }

        // The swap button has to be held for ShoulderSwapHoldTime, and each hold swaps once. L3 is the left stick,
        // which the player is pushing hard while sprinting or dodging, so a plain click swapped sides by accident.
        // Real time, like all input handling.
        void UpdateSwapHold(bool held, float dt)
        {
            if (!held)
            {
                swapHoldTime = 0f;
                swapHoldUsed = false;
                return;
            }
            if (swapHoldUsed) return;
            swapHoldTime += dt;
            if (swapHoldTime >= Math.Max(0f, Finite(tuning.ShoulderSwapHoldTime)))
            {
                SwapShoulder();
                swapHoldUsed = true;
            }
        }

        // Degrees to turn left (right for a left shoulder) so the target sits on the screen's centre line even
        // though the camera looks past a shoulder: sin(angle) = offset / horizontal distance. Capped, because up
        // close the angle grows fast and would swing the camera round as the enemy steps in and out.
        float ShoulderAimCorrection(float horizontalDistance)
        {
            float offset = ShoulderOffset;
            if (offset == 0f) return 0f;
            float maxAngle = Angles.Clamp(Finite(tuning.LockOnMaxShoulderAim), 0f, PitchLimit) * Directions.Deg2Rad;
            float limit = (float)Math.Sin(maxAngle);
            float sine = Angles.Clamp(offset / horizontalDistance, -limit, limit);
            return (float)Math.Asin(sine) * Directions.Rad2Deg;
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

        // Spider-Man 2 widens the view in fights. A foe inside the radius starts it; it keeps going for
        // CombatFramingReleaseDelay after the last one leaves, then eases back. Not while locked on: the locked
        // distance and offset do the framing then.
        void UpdateCombatFraming(in OrbitCameraInput input, float dt)
        {
            bool foeClose = !locked && input.HasFoe && CameraMath.IsFinite(input.NearestFoeDistance)
                            && input.NearestFoeDistance <= Math.Max(0f, Finite(tuning.CombatFramingRadius));
            if (foeClose) combatHold = Math.Max(0f, Finite(tuning.CombatFramingReleaseDelay));
            else combatHold = Math.Max(0f, combatHold - dt);

            float target = !locked && (foeClose || combatHold > 0f) ? 1f : 0f;
            combatBlend = Angles.Clamp(Smooth.Damp(combatBlend, target, ref combatVelocity, tuning.CombatFramingSmoothTime, dt), 0f, 1f);
        }

        // A wall pressed against the shoulder side swaps to the other shoulder for as long as it's there, with
        // hysteresis: swap when less than ShoulderBlockedFraction of the offset fits; swap back only once the
        // original side has fully cleared for ShoulderSwapBackDelay (or at once if the new side is even tighter).
        void UpdateAutoSwap(float freeRight, float freeLeft, float dt)
        {
            float size = Math.Abs(BaseOffset());
            if (!tuning.AutoSwapShoulderWhenBlocked || size < CentredOffset)
            {
                autoSwapped = false;
                swapBackTimer = 0f;
                return;
            }

            float preferred = PreferredSide();
            float freePreferred = preferred > 0f ? freeRight : freeLeft;
            float freeOther = preferred > 0f ? freeLeft : freeRight;
            float blocked = Angles.Clamp(Finite(tuning.ShoulderBlockedFraction), 0f, 1f) * size;

            if (!autoSwapped)
            {
                if (freePreferred < blocked && freeOther >= blocked && freeOther > freePreferred)
                {
                    autoSwapped = true;
                    swapBackTimer = 0f;
                }
                return;
            }

            bool preferredClear = freePreferred >= size - 1e-3f;
            swapBackTimer = preferredClear ? swapBackTimer + dt : 0f;
            bool clearLongEnough = preferredClear && swapBackTimer >= Math.Max(0f, Finite(tuning.ShoulderSwapBackDelay));
            bool swappedSideTighter = freeOther < blocked && freePreferred > freeOther;
            if (clearLongEnough || swappedSideTighter)
            {
                autoSwapped = false;
                swapBackTimer = 0f;
            }
        }

        // Horizontal room behind the shoulder point, measured along this frame's look direction. A wall is (nearly)
        // vertical, so this is the same whatever the pitch, which is what lets UpdateRise pick a pitch that fits.
        // In the open it's a lower bound (the probe didn't hit anything), so the rise only ever comes down there.
        void RememberRoomBehind(float limit)
        {
            float cos = (float)Math.Cos(ViewPitch * Directions.Deg2Rad);
            roomBehind = Math.Max(0f, limit) * Math.Max(0f, cos);
        }

        // Rise over the head: with a wall close behind, tilting the look further down moves the camera up and over
        // the player instead of forward into them. The pitch that keeps MinCollisionDistance of room along the view
        // is acos(room / MinCollisionDistance); it eases there (CollisionRiseSmoothTime) and back to 0 in the open.
        // Real time, like the rest of the camera.
        void UpdateRise(float dt)
        {
            float target = 0f;
            float minDistance = Math.Min(Math.Max(0f, Finite(tuning.MinCollisionDistance)), DesiredDistance);
            float maxTotal = Angles.Clamp(Finite(tuning.MaxCollisionRisePitch), -PitchLimit, PitchLimit);
            float basePitch = ClampPitch(Pitch);
            if (minDistance > CollisionEpsilon && CameraMath.IsFinite(roomBehind) && roomBehind < minDistance && maxTotal > basePitch)
            {
                float needed = (float)Math.Acos(Angles.Clamp(roomBehind / minDistance, 0f, 1f)) * Directions.Rad2Deg;
                target = Angles.Clamp(needed - basePitch, 0f, maxTotal - basePitch);
            }
            // Turning the view moves a far camera a long way, so the turn rate is capped by how fast the camera itself
            // may move (CollisionRiseMaxSpeed): quick up close, where it matters, gentle while the camera is still far.
            float maxSpeed = Finite(tuning.CollisionRiseMaxSpeed) > 0f
                ? tuning.CollisionRiseMaxSpeed / Math.Max(Distance, CollisionEpsilon) * Directions.Rad2Deg
                : float.PositiveInfinity;
            float before = risePitch;
            float next = Smooth.Damp(risePitch, target, ref riseVelocity, tuning.CollisionRiseSmoothTime, dt, maxSpeed);
            if (dt > 0f && !float.IsPositiveInfinity(maxSpeed) && Math.Abs(next - before) > maxSpeed * dt)
            {
                next = before + (next > before ? maxSpeed * dt : -maxSpeed * dt);
                riseVelocity = (next - before) / dt;
            }
            risePitch = Math.Max(0f, next);
            if (!CameraMath.IsFinite(risePitch)) { risePitch = 0f; riseVelocity = 0f; }
        }

        float ClampTotalPitch(float pitch)
        {
            return CameraMath.IsFinite(pitch) ? Angles.Clamp(pitch, -PitchLimit, PitchLimit) : 0f;
        }

        // Walls win instantly (pull in: easing in would spend frames inside the wall); open space comes back
        // gently (ease out, so the view doesn't pop every time a pillar stops blocking it).
        float EaseToLimit(float current, float limit, ref float velocity, float dt)
        {
            if (limit <= current)
            {
                velocity = 0f;
                return limit;
            }
            float smoothTime = Math.Max(0f, tuning.CollisionEaseOutTime) / SmoothTimesToSettle;
            return Math.Min(limit, Smooth.Damp(current, limit, ref velocity, smoothTime, dt));
        }

        // Offset from the tuning for the current state (locked or not), with its sign: + = right shoulder.
        float BaseOffset()
        {
            return Finite(locked ? tuning.LockedShoulderOffset : tuning.ShoulderOffset);
        }

        // The shoulder the player picked: the tuning's side, flipped by each press of the swap button.
        float PreferredSide()
        {
            return chosenSide * (BaseOffset() < 0f ? -1f : 1f);
        }

        // Where the offset is heading: the picked shoulder (or the other one while a wall has swapped it), sized
        // for the current state.
        float TargetOffset()
        {
            float side = PreferredSide() * (autoSwapped ? -1f : 1f);
            return side * Math.Abs(BaseOffset());
        }

        // Everything that eases (distance, shoulder, combat framing) jumps straight to its resting value.
        void ResetFraming()
        {
            baseDistance = WantedDistance();
            desiredDistanceVelocity = 0f;
            combatBlend = 0f;
            combatVelocity = 0f;
            combatHold = 0f;
            DesiredDistance = baseDistance;
            // Starts fully out; the next UpdateDistance pulls it in instantly if a wall is in the way.
            Distance = DesiredDistance;
            distanceVelocity = 0f;

            autoSwapped = false;
            swapBackTimer = 0f;
            wantedOffset = TargetOffset();
            wantedOffsetVelocity = 0f;
            shoulderRoom = ShoulderReach;
            shoulderRoomVelocity = 0f;
            probedOffset = wantedOffset;
            probedLift = 0f;
            liftVelocity = 0f;
            risePitch = 0f;
            riseVelocity = 0f;
            roomBehind = float.PositiveInfinity;
            occludedTime = 0f;
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

        static float Finite(float value)
        {
            return CameraMath.IsFinite(value) ? value : 0f;
        }
    }
}
