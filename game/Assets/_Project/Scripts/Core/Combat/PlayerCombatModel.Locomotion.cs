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
            }
            else
            {
                airTime += dt;
            }

            if (state == PlayerState.Airborne && grounded)
            {
                float impactSpeed = Math.Max(0f, -verticalVelocity);
                state = PlayerState.Locomotion;
                Emit(new PlayerEvent { Type = PlayerEventType.Landed, Amount = impactSpeed });
            }
            else if (IsFree && !OnGroundish)
            {
                ExitAction(false);   // ends a sprint or guard with its event
                state = PlayerState.Airborne;
                airSpeedCap = Math.Max(tuning.RunSpeed, Directions.Flatten(moveVelocity).Length());
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
            // window); here a held guard just raises the guard from walking or sprinting.
            if (guardHeld && (state == PlayerState.Locomotion || state == PlayerState.Sprinting) && OnGroundish)
            {
                EnterGuard(false, clock);
            }
            if (state == PlayerState.Guarding && !guardHeld && clock >= guardHeldUntil)
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
                    if (tracking)
                    {
                        float aimYaw = AttackAimYaw(world, hasStick, stickDir, lockYaw);
                        facingYaw = LocomotionRules.Turn(facingYaw, aimYaw, currentMove.TrackingTurnRate, dt);
                    }
                    break;
                }
                case PlayerState.Dodging:
                    moveVelocity = Vector3.Zero;
                    if (locked) facingYaw = LocomotionRules.Turn(facingYaw, lockYaw, tuning.TurnRate, dt);
                    break;
                default:   // Plunging, Staggered, Dead: no control
                    moveVelocity = Vector3.Zero;
                    break;
            }
            displacement += actionStep + knockback.Step(dt);

            if (state == PlayerState.Plunging && !plungeLanded) verticalVelocity = plungeFalling ? -Plunge.FallSpeed : 0f;
            else if (grounded) verticalVelocity = -tuning.GroundStickSpeed;   // hug the ground (slopes, steps)
            else if (!jumpedThisTick) verticalVelocity = LocomotionRules.ApplyGravity(verticalVelocity, tuning.Gravity, tuning.MaxFallSpeed, dt);

            lastVelocity = Directions.Flatten(moveVelocity) + displacement / dt + new Vector3(0f, verticalVelocity, 0f);
        }

        float GroundSpeed(float stickLength, bool locked)
        {
            if (stickLength <= tuning.StickDeadzone) return 0f;
            if (state == PlayerState.Sprinting) return tuning.SprintSpeed;
            float fast = locked ? tuning.LockOnStrafeSpeed : tuning.RunSpeed;
            return stickLength < tuning.WalkStickThreshold ? tuning.WalkSpeed : fast;
        }

        // Lock target, else soft-lock candidate, else the stick, else keep facing.
        float AttackAimYaw(in PlayerWorldState world, bool hasStick, Vector3 stickDir, float lockYaw)
        {
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
                float before = MotionCurves.WindowProgress(action.PreviousTime, 0f, dodge.Duration, dodge.DashEaseOut);
                float after = MotionCurves.WindowProgress(action.Time, 0f, dodge.Duration, dodge.DashEaseOut);
                return dodgeDirection * (dodgeDistance * (after - before));
            }
            return state == PlayerState.Attacking ? LungeStep(world) : Vector3.Zero;
        }

        // The attack's forward step for this frame (linear over the lunge window), stopping short of the target.
        Vector3 LungeStep(in PlayerWorldState world)
        {
            MoveData move = currentMove;
            if (!(move.LungeDistance > 0f)) return Vector3.Zero;
            float end = move.ActiveEnd;
            float start = move.LungeTime > 0f ? Math.Max(0f, end - move.LungeTime) : 0f;
            float before = MotionCurves.WindowProgress(action.PreviousTime, start, end, 0f);
            float after = MotionCurves.WindowProgress(action.Time, start, end, 0f);
            return LimitApproach(Forward * (move.LungeDistance * (after - before)), world);
        }

        // Removes the part of a step that would carry us closer than LungeStopGap to the target's body.
        Vector3 LimitApproach(Vector3 step, in PlayerWorldState world)
        {
            Vector3 target;
            float targetRadius;
            if (world.HasLockTarget)
            {
                target = world.LockTargetPosition;
                targetRadius = world.LockTargetRadius;
            }
            else if (world.HasSoftTarget)
            {
                target = world.SoftTargetPosition;
                targetRadius = world.SoftTargetRadius;
            }
            else
            {
                return step;
            }

            Vector3 toTarget = Directions.Flatten(target - world.Position);
            float distance = toTarget.Length();
            if (distance < 1e-4f) return Vector3.Zero;   // standing on it: don't lunge at all
            Vector3 dir = toTarget / distance;
            float along = Vector3.Dot(step, dir);
            if (along <= 0f) return step;
            float allowed = Math.Max(0f, distance - Math.Max(0f, world.SelfRadius) - Math.Max(0f, targetRadius) - tuning.LungeStopGap);
            return along <= allowed ? step : step - dir * (along - allowed);
        }

        // Backing away from the fight drains Momentum (Northern Shaolin rewards pressing forward): away from the
        // lock target, or when not locked on, away from the nearest enemy within BackOffRadius.
        void UpdateMomentum(float dt, in PlayerWorldState world)
        {
            MomentumSettings rules = MomentumRules;
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
            momentum.Tick(dt, rules, backingOff);
        }
    }
}
