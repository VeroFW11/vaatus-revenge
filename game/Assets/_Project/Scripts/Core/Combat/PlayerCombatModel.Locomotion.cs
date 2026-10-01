using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Walking, running, sprinting, jumping, falling, facing and the final velocity.
    //
    // STATE CHANGES HANDLED HERE:
    //   Locomotion <-> Sprinting : Dodge button held (see TapHoldResolver) while the stick is pushed.
    //   Locomotion/Sprinting -> Guarding : guard held; Guarding -> Locomotion : guard released (after the
    //                              minimum deflect time for a tap).
    //   free -> Airborne : jump, or walking off a ledge once coyote time runs out.
    //   Airborne -> Locomotion : landing (Landed event).
    // Movement is camera-relative: pushing up walks away from the camera whatever the character faces.
    public sealed partial class PlayerCombatModel
    {
        float facingYaw;
        Vector3 moveVelocity;         // horizontal locomotion velocity, kept between frames for acceleration
        float verticalVelocity;
        Vector3 lastVelocity;         // what the last Tick returned
        bool grounded;
        float airTime;                // seconds since we last had ground under us (coyote time)
        bool jumpedSinceGrounded;
        bool jumpedThisTick;
        float airSpeedCap;            // air control steers toward this speed (keeps a sprint jump's speed)
        float sprintTime;
        double sprintEndedClock = double.NegativeInfinity;
        float lastSprintDuration;
        bool sprintExhausted;         // ran dry while sprinting: sprint returns at SprintResumeStamina
        PushMotion knockback;
        Vector3 actionStep;           // this frame's dash or lunge movement, in metres (see AdvanceAction)
        float exitVelocityShare = 1f; // share of this frame the locomotion velocity moves us (a dash that ended mid-frame)
        bool orbiting;                // the running attack's lunge curves round its target (MoveData.OrbitDegrees)
        Vector3 orbitStartDirection;  // flat, target -> us, when the attack started
        float orbitStartRadius;
        float orbitEndRadius;
        float orbitSign;              // +1 or -1: which way round

        void ResetLocomotion(float newFacingYaw)
        {
            facingYaw = Angles.Wrap180(newFacingYaw);
            moveVelocity = Vector3.Zero;
            verticalVelocity = 0f;
            lastVelocity = Vector3.Zero;
            grounded = true;
            airTime = 0f;
            jumpedSinceGrounded = false;
            jumpedThisTick = false;
            airSpeedCap = tuning.RunSpeed;
            sprintTime = 0f;
            sprintEndedClock = double.NegativeInfinity;
            lastSprintDuration = 0f;
            sprintExhausted = false;
            knockback.Stop();
            actionStep = Vector3.Zero;
        }

        // True when the stick is in its dead zone. With a neutral stick the soft lock looks all round, not just ahead
        // (a counter straight out of a dodge still finds the attacker, report 03 V-09).
        public bool IsStickNeutral(Vector2 move)
        {
            return Directions.CameraRelative(move, 0f).Length() <= Math.Max(tuning.StickDeadzone, Epsilon);
        }

        // The direction the player is aiming right now: the stick (camera-relative) if pushed, else the
        // facing. The Unity side uses it to pick the soft-lock candidate (see SoftLockSelector).
        public float GetAimYaw(Vector2 move, float cameraYaw)
        {
            Vector3 stick = Directions.CameraRelative(move, cameraYaw);
            return stick.Length() > Math.Max(tuning.StickDeadzone, Epsilon) ? Directions.YawOf(stick, facingYaw) : facingYaw;
        }

        // ---------------------------------------------------------------- ground, jump, fall

        void UpdateGrounding(float dt, in PlayerWorldState world)
        {
            jumpedThisTick = false;
            // Still rising from a jump never counts as grounded, so brushing a slope can't cut a jump short.
            grounded = world.Grounded && verticalVelocity <= 0f;
            if (grounded)
            {
                airTime = 0f;
                jumpedSinceGrounded = false;
                airAttacksUsed = 0;                  // touching down refills the air string and the air dash
                airDashesUsed = 0;
            }
            else
            {
                airTime += dt;
            }

            if (state == PlayerState.Airborne && grounded)
            {
                float impactSpeed = Math.Max(0f, -verticalVelocity);
                state = PlayerState.Locomotion;
                OnLandedForString();
                Emit(new PlayerEvent { Type = PlayerEventType.Landed, Amount = impactSpeed });
            }
            else if (IsFree && !OnGroundish)
            {
                ExitAction(false);   // ends a sprint or guard with its event
                state = PlayerState.Airborne;
                HoldStringIfLive(false);
                airSpeedCap = Math.Max(tuning.RunSpeed, Directions.Flatten(moveVelocity).Length());
            }
            else if (grounded && stringNext >= 0 && stringBranch == ComboBranch.Air && state != PlayerState.Attacking)
            {
                ClearStringMemory();   // back on the ground: the air string is over
            }
        }

        void TryJump()
        {
            if (!CanDefensiveCancel(false) || !OnGroundish || jumpedSinceGrounded) return;
            buffer.Clear();
            StartJump();
        }

        void StartJump()
        {
            // Keep the speed you had: a running jump flies further, a jump out of a dash keeps some of it.
            Vector3 carry = Directions.Flatten(IsFree ? moveVelocity : lastVelocity);
            ExitAction(true);
            state = PlayerState.Airborne;
            HoldStringIfLive(false);   // a jump keeps the ground string (an air attack starts the air string instead)
            float speed = carry.Length();
            float maxCarry = Math.Max(tuning.RunSpeed, tuning.SprintSpeed);
            moveVelocity = speed > maxCarry ? carry * (maxCarry / speed) : carry;
            airSpeedCap = Math.Max(tuning.RunSpeed, moveVelocity.Length());
            verticalVelocity = LocomotionRules.JumpSpeed(tuning.JumpHeight, tuning.Gravity);
            grounded = false;
            jumpedSinceGrounded = true;
            jumpedThisTick = true;
            Emit(PlayerEventType.Jumped);
        }

        // ---------------------------------------------------------------- held buttons: guard and sprint

        void UpdateHeldStates(float dt)
        {
            if (state == PlayerState.Dead) return;

            // A guard press is a buffered command (it can cut an attack or dodge short and opens the deflect
            // window); here a held guard just raises the guard from walking or sprinting. Parry-only elements
            // have no held guard: the parry stance drops by itself when its window ends.
            bool parryOnly = Guard.IsParryOnly;
            if (!parryOnly && guardHeld && (state == PlayerState.Locomotion || state == PlayerState.Sprinting) && OnGroundish)
            {
                EnterGuard(false, clock);
            }
            if (state == PlayerState.Guarding && (parryOnly || !guardHeld) && clock >= guardHeldUntil)
            {
                ExitAction(false);
                state = PlayerState.Locomotion;
            }

            if (sprintExhausted && stamina.Current >= tuning.SprintResumeStamina) sprintExhausted = false;
            bool drains = tuning.SprintStaminaDrain > 0f;
            bool wantsSprint = dodgeButton.WantsSprint(tuning.DodgeTrigger, tuning.TapHoldThreshold)
                && moveStick.Length() > tuning.StickDeadzone && OnGroundish && !sprintExhausted
                && (!drains || stamina.CanAct);
            if (state == PlayerState.Locomotion && wantsSprint)
            {
                state = PlayerState.Sprinting;
                sprintTime = 0f;
                Emit(PlayerEventType.SprintStarted);
            }
            else if (state == PlayerState.Sprinting && !wantsSprint)
            {
                ExitAction(false);
                state = PlayerState.Locomotion;
            }

            if (state != PlayerState.Sprinting) return;
            sprintTime += dt;
            if (!drains) return;
            stamina.Drain(tuning.SprintStaminaDrain, dt, tuning.StaminaRegenDelay, tuning.EmptyStaminaRegenDelay);
            if (stamina.CanAct) return;
            sprintExhausted = true;
            ExitAction(false);
            state = PlayerState.Locomotion;
        }

        // ---------------------------------------------------------------- velocity and facing

        void ComputeMotion(float dt, in PlayerWorldState world)
        {
            Vector3 stick = Directions.CameraRelative(moveStick, world.CameraYaw);
            float stickLength = stick.Length();
            bool hasStick = stickLength > Math.Max(tuning.StickDeadzone, Epsilon);   // never divide by a zero stick
            Vector3 stickDir = hasStick ? stick / stickLength : Vector3.Zero;
            bool locked = world.HasLockTarget;
            float lockYaw = locked ? YawTowards(world.Position, world.LockTargetPosition) : facingYaw;
            Vector3 displacement = Vector3.Zero;   // action-driven movement this frame, in metres

            switch (state)
            {
                case PlayerState.Locomotion:
                case PlayerState.Sprinting:
                case PlayerState.Guarding:
                case PlayerState.Healing:
                {
                    float speed = GroundSpeed(stickLength, locked);
                    if (state == PlayerState.Guarding) speed *= Guard.MoveSpeedMultiplier;
                    else if (state == PlayerState.Healing) speed *= tuning.HealMoveMultiplier;
                    moveVelocity = LocomotionRules.Accelerate(moveVelocity, stickDir * speed, tuning.Acceleration, tuning.Deceleration, dt);
                    // Locked on you face the target and strafe; sprinting breaks that so you can run freely.
                    if (locked && state != PlayerState.Sprinting) facingYaw = LocomotionRules.Turn(facingYaw, lockYaw, tuning.TurnRate, dt);
                    else if (hasStick) facingYaw = LocomotionRules.Turn(facingYaw, Directions.YawOf(stickDir, facingYaw), tuning.TurnRate, dt);
                    break;
                }
                case PlayerState.Airborne:
                {
                    // Air control: steer a little. No stick = keep your momentum.
                    float air = Angles.Clamp(tuning.AirControl, 0f, 1f);
                    if (hasStick)
                    {
                        moveVelocity = LocomotionRules.Accelerate(moveVelocity, stickDir * airSpeedCap,
                            tuning.Acceleration * air, tuning.Deceleration * air, dt);
                    }
                    if (locked) facingYaw = LocomotionRules.Turn(facingYaw, lockYaw, tuning.TurnRate, dt);
                    else if (hasStick) facingYaw = LocomotionRules.Turn(facingYaw, Directions.YawOf(stickDir, facingYaw), tuning.TurnRate, dt);
                    break;
                }
                case PlayerState.Attacking:
                case PlayerState.Charging:
                {
                    // Attack tracking: turn toward the target (or stick) during startup, then commit.
                    moveVelocity = Vector3.Zero;
                    bool tracking = state == PlayerState.Charging || action.Time < currentMove.Startup;
                    if (orbiting && TryGetLungeTarget(world, out Vector3 orbitTarget, out _))
                    {
                        facingYaw = YawTowards(world.Position + actionStep, orbitTarget);   // circling: always face it
                    }
                    else if (tracking)
                    {
                        float aimYaw = AttackAimYaw(world, hasStick, stickDir, lockYaw);
                        facingYaw = LocomotionRules.Turn(facingYaw, aimYaw, currentMove.TrackingTurnRate, dt);
                    }
                    break;
                }
                case PlayerState.Dodging:
                    moveVelocity = Vector3.Zero;
                    // Spider-Man 2: keep facing the enemy you dodged around (Traverse and a focus-less backstep keep theirs).
                    if (dodgeHasFocus)
                    {
                        float focusYaw = YawTowards(world.Position + actionStep, TrackFocus(world));
                        facingYaw = LocomotionRules.Turn(facingYaw, focusYaw, Dodge.FocusTurnRate, dt);
                    }
                    break;
                default:   // Plunging, Staggered, Dead: no control
                    moveVelocity = Vector3.Zero;
                    break;
            }
            displacement += actionStep + knockback.Step(dt);

            if (TryZipVertical(world, dt, out float zipVertical)) verticalVelocity = zipVertical;
            else if (state == PlayerState.Plunging && !plungeLanded) verticalVelocity = plungeFalling ? -Plunge.FallSpeed : 0f;
            else if (state == PlayerState.Dodging && dodgeInAir) verticalVelocity = 0f;   // an air dash is flat
            else if (grounded) verticalVelocity = -tuning.GroundStickSpeed;   // hug the ground (slopes, steps)
            else if (!jumpedThisTick)
            {
                // Hanging in the air while an air strike runs (Spider-Man's air combos), normal gravity otherwise.
                bool aerialMove = state == PlayerState.Attacking && (attackKind == PlayerAttackKind.Air
                    || attackKind == PlayerAttackKind.Launcher || attackKind == PlayerAttackKind.ZipStrike);
                float gravityScale = aerialMove ? Angles.Clamp(Aerial.AirAttackGravityScale, 0f, 1f) : 1f;
                verticalVelocity = LocomotionRules.ApplyGravity(verticalVelocity, tuning.Gravity * gravityScale, tuning.MaxFallSpeed, dt);
            }

            lastVelocity = Directions.Flatten(moveVelocity) * exitVelocityShare + displacement / dt + new Vector3(0f, verticalVelocity, 0f);
            exitVelocityShare = 1f;
        }

        float GroundSpeed(float stickLength, bool locked)
        {
            if (stickLength <= tuning.StickDeadzone) return 0f;
            if (state == PlayerState.Sprinting) return tuning.SprintSpeed;
            float fast = locked ? tuning.LockOnStrafeSpeed : tuning.RunSpeed;
            return stickLength < tuning.WalkStickThreshold ? tuning.WalkSpeed : fast;
        }

        // Zip strike: its target. Otherwise lock target, else soft-lock candidate, else the stick, else keep facing.
        float AttackAimYaw(in PlayerWorldState world, bool hasStick, Vector3 stickDir, float lockYaw)
        {
            if (attackKind == PlayerAttackKind.ZipStrike && world.HasZipTarget) return YawTowards(world.Position, world.ZipTargetPosition);
            if (lungeHoming && TryGetLungeTarget(world, out Vector3 homingTarget, out _)) return YawTowards(world.Position, homingTarget);
            if (world.HasLockTarget) return lockYaw;
            if (world.HasSoftTarget) return YawTowards(world.Position, world.SoftTargetPosition);
            if (hasStick) return Directions.YawOf(stickDir, facingYaw);
            return facingYaw;
        }

        float YawTowards(Vector3 from, Vector3 to)
        {
            return Directions.YawOf(Directions.Flatten(to - from), facingYaw);   // on top of it: keep facing
        }

        // Movement the running action itself causes this frame: the dash of a dodge or an attack's lunge.
        Vector3 ActionStep(in PlayerWorldState world)
        {
            if (state == PlayerState.Dodging)
            {
                DodgeProfile dodge = Dodge;
                float duration = dodgeInAir ? Aerial.AirDashDuration : dodge.Duration;
                float before = MotionCurves.WindowProgress(action.PreviousTime, 0f, duration, dodge.DashEaseOut);
                float after = MotionCurves.WindowProgress(action.Time, 0f, duration, dodge.DashEaseOut);
                Vector3 dash = dodgeDirection * (dodgeDistance * (after - before));
                // A slip-in stops SlipInStopGap from the target's body, even if it stepped toward you.
                if (dodgeKind == DodgeKind.SlipIn && dodgeHasFocus)
                {
                    dash = LimitApproachTo(dash, TrackFocus(world), dodgeFocus.Radius, dodge.SlipInStopGap, world);
                }
                // A side-slip circles the focus (Spider-Man 2: you side-step round them, keeping your distance).
                else if (dodgeKind == DodgeKind.SideSlip && dodgeHasFocus && !dodgeInAir)
                {
                    dash = CircleStep(dash, TrackFocus(world), world);
                }
                return dash;
            }
            return state == PlayerState.Attacking ? LungeStep(world) : Vector3.Zero;
        }

        // The attack's forward step for this frame (linear over the lunge window), stopping short of the target.
        Vector3 LungeStep(in PlayerWorldState world)
        {
            MoveData move = currentMove;
            if (!(lungeDistance > 0f)) return Vector3.Zero;
            // A zip strike's dash and an arriving lunge (PlanArrival) arrive as the strike goes active (so they never
            // connect from metres away); every other lunge carries on through the active frames.
            bool arrives = attackKind == PlayerAttackKind.ZipStrike || lungeArrives;
            if (orbiting)
            {
                // Circling goes on through the active frames; an arriving one has closed in by the strike.
                float turnBefore = MotionCurves.WindowProgress(action.PreviousTime, 0f, move.ActiveEnd, 0f);
                float turnAfter = MotionCurves.WindowProgress(action.Time, 0f, move.ActiveEnd, 0f);
                float closeEnd = arrives ? move.ActiveStart : move.ActiveEnd;
                return OrbitStep(world, turnBefore, turnAfter,
                    MotionCurves.WindowProgress(action.PreviousTime, 0f, closeEnd, 0f), MotionCurves.WindowProgress(action.Time, 0f, closeEnd, 0f));
            }
            float end = arrives ? move.ActiveStart : move.ActiveEnd;
            float start = !lungeArrives && move.LungeTime > 0f ? Math.Max(0f, end - move.LungeTime) : 0f;
            float before = MotionCurves.WindowProgress(action.PreviousTime, start, end, 0f);
            float after = MotionCurves.WindowProgress(action.Time, start, end, 0f);
            return LimitApproach(LungeDirection(world) * (lungeDistance * (after - before)), world);
        }

        // Air's circling strikes (MoveData.OrbitDegrees): the lunge curves round the target, ending OrbitDegrees round it,
        // closing in by up to the lunge distance (never nearer than LungeStopGap from its body). Which way round: the
        // side the stick points to; with the stick neutral, the camera's right (deterministic).
        void PlanOrbit(MoveData move, in PlayerWorldState world)
        {
            orbiting = false;
            if (!(move.OrbitDegrees > 0f) || !TryGetLungeTarget(world, out Vector3 target, out float targetRadius)) return;
            Vector3 offset = Directions.Flatten(world.Position - target);
            float radius = offset.Length();
            if (radius < 1e-3f) return;
            orbiting = true;
            orbitStartDirection = offset / radius;
            orbitStartRadius = radius;
            float closest = Math.Max(0f, world.SelfRadius) + Math.Max(0f, targetRadius) + Math.Max(0f, tuning.LungeStopGap);
            orbitEndRadius = radius > closest ? Math.Max(closest, radius - Math.Max(0f, lungeDistance)) : radius;
            // Our right while facing the target is the side the stick (or the camera's right) points to.
            Vector3 ourRight = new Vector3(-orbitStartDirection.Z, 0f, orbitStartDirection.X);   // -offset turned 90 degrees clockwise
            Vector3 stick = Directions.CameraRelative(moveStick, world.CameraYaw);
            Vector3 wanted = stick.Length() > Math.Max(tuning.StickDeadzone, Epsilon) ? stick : Directions.FromYaw(world.CameraYaw + 90f);
            float goRight = Vector3.Dot(wanted, ourRight) >= 0f ? 1f : -1f;
            // Turning the offset by +degrees (clockwise from above) moves us to our left as we face the target.
            orbitSign = -goRight;
        }

        // before/after: how far round; closeBefore/closeAfter: how far in (the radius), each 0..1.
        Vector3 OrbitStep(in PlayerWorldState world, float before, float after, float closeBefore, float closeAfter)
        {
            float degrees = currentMove.OrbitDegrees * orbitSign;
            Vector3 from = OrbitOffset(degrees * before, closeBefore);
            Vector3 to = OrbitOffset(degrees * after, closeAfter);
            return to - from;
        }

        Vector3 OrbitOffset(float degrees, float progress)
        {
            float yaw = Directions.YawOf(orbitStartDirection, 0f) + degrees;
            float radius = orbitStartRadius + (orbitEndRadius - orbitStartRadius) * progress;
            return Directions.FromYaw(yaw) * radius;
        }

        // Launching yourself: a launcher's rise, or an air strike's small lift. Leaves the ground this frame.
        void ApplySelfLift(float speed)
        {
            verticalVelocity = speed;
            grounded = false;
            jumpedThisTick = true;                   // this frame's gravity mustn't eat the lift
            jumpedSinceGrounded = true;              // no coyote jump off the back of a launcher
        }

        // During a zip strike's dash, rise or fall to arrive level with the target (a juggled enemy up in the air).
        bool TryZipVertical(in PlayerWorldState world, float dt, out float vertical)
        {
            vertical = 0f;
            if (state != PlayerState.Attacking || attackKind != PlayerAttackKind.ZipStrike || !lungeHoming || !world.HasZipTarget) return false;
            float remaining = currentMove.ActiveStart - action.Time;   // arrive level as the kick goes active
            if (remaining <= 0f)
            {
                // The frame it arrives: stop climbing or dropping, then hang like any air strike.
                if (action.PreviousTime >= currentMove.ActiveStart) return false;
                vertical = 0f;
                return !grounded;
            }
            float dy = world.ZipTargetPosition.Y - world.Position.Y;
            if (grounded && dy <= 0.05f) return false;   // level ground: stay on it
            vertical = Angles.Clamp(dy / Math.Max(remaining, dt), -25f, 25f);
            if (vertical > 0f)
            {
                grounded = false;
                jumpedSinceGrounded = true;
            }
            return true;
        }

        // Straight at the target for a homing lunge (so turning during startup doesn't curve the path), else forward.
        Vector3 LungeDirection(in PlayerWorldState world)
        {
            if (!lungeHoming || !TryGetLungeTarget(world, out Vector3 target, out _)) return Forward;
            return Directions.SafeNormalize(Directions.Flatten(target - world.Position), Forward);
        }

        // Whom the running attack's lunge closes on: the zip target for a zip strike, else lock, else soft lock. A
        // homing lunge sticks with the target it started on: if the soft lock drops it or switches to someone else far
        // from it mid-lunge (the stick let go during startup, report 03 V-08), it keeps going where that target was.
        bool TryGetLungeTarget(in PlayerWorldState world, out Vector3 target, out float targetRadius)
        {
            bool found = TryGetWorldLungeTarget(world, out target, out targetRadius);
            if (!lungeHoming || state != PlayerState.Attacking) return found;
            const float SameTargetDistance = 1.5f;
            if (found && Directions.Flatten(target - lungeTargetFeet).Length() <= SameTargetDistance)
            {
                lungeTargetFeet = target;                // still the same one: follow it
                lungeTargetRadius = targetRadius;
                return true;
            }
            target = lungeTargetFeet;
            targetRadius = lungeTargetRadius;
            return true;
        }

        bool TryGetWorldLungeTarget(in PlayerWorldState world, out Vector3 target, out float targetRadius)
        {
            if (attackKind == PlayerAttackKind.ZipStrike && world.HasZipTarget)
            {
                target = world.ZipTargetPosition;
                targetRadius = world.ZipTargetRadius;
                return true;
            }
            if (world.HasLockTarget)
            {
                target = world.LockTargetPosition;
                targetRadius = world.LockTargetRadius;
                return true;
            }
            if (world.HasSoftTarget)
            {
                target = world.SoftTargetPosition;
                targetRadius = world.SoftTargetRadius;
                return true;
            }
            target = Vector3.Zero;
            targetRadius = 0f;
            return false;
        }

        // Removes the part of a step that would carry us closer than LungeStopGap to the target's body.
        Vector3 LimitApproach(Vector3 step, in PlayerWorldState world)
        {
            if (!TryGetLungeTarget(world, out Vector3 target, out float targetRadius)) return step;
            return LimitApproachTo(step, target, targetRadius, tuning.LungeStopGap, world);
        }

        // Removes the part of a step that would carry us closer than 'gap' to a body at 'target'.
        Vector3 LimitApproachTo(Vector3 step, Vector3 target, float targetRadius, float gap, in PlayerWorldState world)
        {
            Vector3 toTarget = Directions.Flatten(target - world.Position);
            float distance = toTarget.Length();
            if (distance < 1e-4f) return Vector3.Zero;   // standing on it: don't lunge at all
            Vector3 dir = toTarget / distance;
            float along = Vector3.Dot(step, dir);
            if (along <= 0f) return step;
            float allowed = Math.Max(0f, distance - Math.Max(0f, world.SelfRadius) - Math.Max(0f, targetRadius) - gap);
            return along <= allowed ? step : step - dir * (along - allowed);
        }

        // Backing away from the fight drains Momentum (Northern Shaolin rewards pressing forward): away from the
        // lock target, or when not locked on, away from the nearest enemy within BackOffRadius.
        void UpdateMomentum(float dt, in PlayerWorldState world)
        {
            // Every element's meter ticks with its own settings (an element without a meter just stays at zero).
            for (ElementId element = ElementId.Fire; element <= ElementId.Air; element++)
            {
                MomentumSettings rules = MomentumRulesOf(element);
                MeterOf(element).Tick(dt, rules, IsBackingOff(rules, world));
            }
        }

        bool IsBackingOff(MomentumSettings rules, in PlayerWorldState world)
        {
            bool backingOff = false;
            bool hasThreat = world.HasLockTarget;
            Vector3 threat = world.LockTargetPosition;
            if (!hasThreat && world.HasNearestEnemy
                && Directions.Flatten(world.NearestEnemyPosition - world.Position).Length() <= rules.BackOffRadius)
            {
                hasThreat = true;
                threat = world.NearestEnemyPosition;
            }
            if (hasThreat)
            {
                Vector3 toThreat = Directions.Flatten(threat - world.Position);
                float distance = toThreat.Length();
                if (distance > 1e-4f)
                {
                    float awaySpeed = -Vector3.Dot(Directions.Flatten(lastVelocity), toThreat / distance);
                    backingOff = awaySpeed > rules.BackOffSpeedThreshold;
                }
            }
            return backingOff;
        }
    }
}
