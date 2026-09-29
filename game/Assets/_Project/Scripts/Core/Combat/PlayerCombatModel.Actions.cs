using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Attacks, charge, plunge, skill, heal and stagger: starting them, running their frame data and
    // ending them cleanly.
    //
    // WHEN CAN A BUFFERED PRESS RUN? ("the earliest legal point")
    //   Light / Heavy / Skill / ZipStrike: when free (Locomotion, Sprinting, Guarding); during an attack from its
    //     ChainCancelAt; during a dodge from Dodge.AttackCancelAt; after a plunge landing from its ChainCancelAt.
    //     In the air, Light/Heavy become the plunge once you've been off the ground for Plunge.MinAirTime
    //     (earlier presses are dropped, not saved for later). Skill waits for the ground.
    //   Dodge: when free on the ground; during an attack from DodgeCancelAt; during a dodge from NextDodgeAt;
    //     while charging if Charge.CanDodgeCancelCharge; after a plunge landing from its DodgeCancelAt.
    //   Jump: like Dodge (a dodge allows it from AttackCancelAt), and only with ground under you or within coyote time.
    //   Guard (the press): like Dodge, but not out of a charge or in the air. Held guard raises it when free; a
    //     buffered press only raises it if guard is still held or its deflect window (timed from the real press)
    //     hasn't run out.
    //   Heal: only when free on the ground (drinking is a committed action, never a cancel).
    //   Nothing runs while staggered, healing, mid-plunge, or dead; presses there still wait in the buffer.
    // LIGHT CHAIN: a light press arriving (or still buffered) while the current light move is inside its combo
    //   window is queued as the next move and fires at the cancel point (or is dropped once it's older than
    //   QueuedPressMaxAge). A press after the window restarts the chain from the first move. After the last
    //   move the chain loops.
    // FREE-FLOW LUNGE: a light attack aimed at a target (lock-on, else soft lock) that's out of reach lunges further
    //   than the move's own LungeDistance, by up to PlayerTuning.GapCloseDistance, stopping short of the target.
    // ZIP STRIKE: needs PlayerWorldState.HasZipTarget; without one the press is dropped and costs nothing. The dash
    //   covers the whole gap to the target during the move's startup and active frames. Not in the air.
    // LAUNCHER: keep holding the attack press that started a ground string for AerialSettings.LauncherHoldTime and the
    //   string's move turns into the launcher (Spider-Man's hold-square): the hit throws the target up and SelfLift
    //   carries you after it when the strike lands.
    // AIR STRING: attack while in the air (jumping, launched, after a zip) walks the AirChain, which does not loop and is
    //   capped at AerialSettings.AirAttacksPerJump strikes before touching down. Air strikes lift you (SelfLift) and
    //   gravity is scaled by AirAttackGravityScale while one runs, so you hang as you strike. Heavy in the air = plunge.
    // AIR DASH: dodge in the air, up to AirDashesPerJump times before landing: a flat dash with no gravity.
    // ABILITIES: AbilityNorth / AbilityEast run like the skill (grounded, from the usual cancel points).
    // SPRINT ATTACK: light while sprinting for SprintAttackMinSprintTime, or within SprintAttackGrace after such
    //   a sprint ends while still moving at full running speed (strafe speed when locked on).
    public sealed partial class PlayerCombatModel
    {
        void TryRunBufferedCommand(in PlayerWorldState world)
        {
            if (state == PlayerState.Dead)
            {
                buffer.Clear();
                return;
            }
            TryLauncherHold(world);
            switch (buffer.Command)
            {
                case PlayerCommand.Light: TryLight(world); break;
                case PlayerCommand.AbilityNorth: TryAbility(moveSet.AbilityNorth, world); break;
                case PlayerCommand.AbilityEast: TryAbility(moveSet.AbilityEast, world); break;
                case PlayerCommand.Heavy: TryHeavy(world); break;
                case PlayerCommand.Skill: TrySkill(world); break;
                case PlayerCommand.Dodge: TryDodge(world); break;
                case PlayerCommand.Jump: TryJump(); break;
                case PlayerCommand.Heal: TryHeal(); break;
                case PlayerCommand.Guard: TryGuardPress(); break;
                case PlayerCommand.ZipStrike: TryZipStrike(world); break;
            }
        }

        bool IsFree => state == PlayerState.Locomotion || state == PlayerState.Sprinting || state == PlayerState.Guarding;
        bool OnGroundish => grounded || airTime <= tuning.CoyoteTime;
        float PlungeTimeSinceLanding => plungeLanded ? action.Time - landingTime : -1f;

        bool CanStartAttack()
        {
            switch (state)
            {
                case PlayerState.Locomotion:
                case PlayerState.Sprinting:
                case PlayerState.Guarding:
                    return true;
                case PlayerState.Attacking:
                    return action.Time >= currentMove.ChainCancelAt;
                case PlayerState.Dodging:
                    return action.Time >= Dodge.AttackCancelAt;
                case PlayerState.Plunging:
                    return plungeLanded && PlungeTimeSinceLanding >= currentMove.ChainCancelAt;
                default:
                    return false;
            }
        }

        // Dodge, jump and guard can cut other actions short from these points.
        bool CanDefensiveCancel(bool isDodge)
        {
            switch (state)
            {
                case PlayerState.Locomotion:
                case PlayerState.Sprinting:
                case PlayerState.Guarding:
                    return OnGroundish;
                case PlayerState.Attacking:
                    return action.Time >= currentMove.DodgeCancelAt;
                case PlayerState.Dodging:
                    return action.Time >= (isDodge ? Dodge.NextDodgeAt : Dodge.AttackCancelAt);
                case PlayerState.Charging:
                    return isDodge && Charge.CanDodgeCancelCharge;
                case PlayerState.Plunging:
                    return plungeLanded && PlungeTimeSinceLanding >= currentMove.DodgeCancelAt;
                default:
                    return false;
            }
        }

        // In the air for the purpose of choosing moves: airborne, or attacking / dashing with no ground under us.
        bool Aloft => state == PlayerState.Airborne
            || (!grounded && (state == PlayerState.Attacking || state == PlayerState.Dodging || state == PlayerState.Plunging));

        void TryLight(in PlayerWorldState world)
        {
            if (Aloft)
            {
                TryAirAttack(world);
                return;
            }
            if (!CanStartAttack()) return;

            int next = IsFree && clock <= chainGraceUntil + 1e-6 && chainGraceKind == PlayerAttackKind.Light ? chainGraceNext : 0;
            if (state == PlayerState.Attacking && attackKind == PlayerAttackKind.Light)
            {
                // Still inside (or before) the combo window: wait, the window decides chain vs restart.
                if (!buffer.Locked && action.Time <= currentMove.ComboWindowEnd) return;
                if (buffer.Locked) next = (chainIndex + 1) % Math.Max(1, ChainLength);
            }
            if (!stamina.CanAct) return;

            if (moveSet.SprintAttack != null && WantsSprintAttack(world))
            {
                buffer.Clear();
                StartAttack(moveSet.SprintAttack, PlayerAttackKind.Sprint, -1, ChargeTier.None, ConsumeCounterWindow(), true, world);
                return;
            }
            if (ChainLength == 0 || moveSet.LightChain[next] == null)
            {
                buffer.Clear();
                return;
            }
            buffer.Clear();
            StartAttack(moveSet.LightChain[next], PlayerAttackKind.Light, next, ChargeTier.None, ConsumeCounterWindow(), true, world);
        }

        bool WantsSprintAttack(in PlayerWorldState world)
        {
            float minSprint = moveSet.SprintAttackMinSprintTime;
            if (state == PlayerState.Sprinting) return sprintTime >= minSprint;
            if (!IsFree || lastSprintDuration < minSprint || clock - sprintEndedClock > moveSet.SprintAttackGrace + Epsilon) return false;
            // Still carrying the sprint: at least full running speed. (Deceleration settles exactly on run speed, so a
            // strict "faster than" would shrink the grace to the few frames it takes to slow down.)
            float fullSpeed = world.HasLockTarget ? tuning.LockOnStrafeSpeed : tuning.RunSpeed;
            return Directions.Flatten(moveVelocity).Length() >= fullSpeed - Epsilon;
        }

        void TryAirAttack(in PlayerWorldState world)
        {
            MoveData[] chain = moveSet.AirChain;
            if (chain == null || chain.Length == 0)
            {
                buffer.Clear();
                return;
            }
            int next = 0;
            if (state == PlayerState.Attacking && attackKind == PlayerAttackKind.Air)
            {
                if (!CanStartAttack()) return;
                // Still inside (or before) the combo window: wait, the window decides.
                if (!buffer.Locked && action.Time <= currentMove.ComboWindowEnd) return;
                next = buffer.Locked ? chainIndex + 1 : 0;
            }
            else if (state == PlayerState.Airborne)
            {
                if (clock <= chainGraceUntil + 1e-6 && chainGraceKind == PlayerAttackKind.Air) next = chainGraceNext;
            }
            else if (state == PlayerState.Dodging)
            {
                return;                                      // after the air dash (the press waits in the buffer)
            }
            else if (!CanStartAttack())
            {
                return;
            }
            if (next >= chain.Length || chain[next] == null || airAttacksUsed >= Math.Max(0, Aerial.AirAttacksPerJump))
            {
                buffer.Clear();                              // the air string is spent until you land
                return;
            }
            if (!stamina.CanAct) return;
            buffer.Clear();
            airAttacksUsed++;
            StartAttack(chain[next], PlayerAttackKind.Air, next, ChargeTier.None, ConsumeCounterWindow(), true, world);
        }

        // Holding the press that started a ground string's move turns it into the launcher.
        void TryLauncherHold(in PlayerWorldState world)
        {
            if (state != PlayerState.Attacking || attackKind != PlayerAttackKind.Light || moveSet.Launcher == null) return;
            if (!lightHeld || !grounded) return;
            if (clock - lightPressClock < Aerial.LauncherHoldTime - Epsilon) return;
            // The held press must be the one that started this move (buffered presses count from a little earlier).
            if (lightPressClock < attackStartClock - tuning.InputBufferWindow - Epsilon) return;
            if (!stamina.CanAct) return;
            if (buffer.Command == PlayerCommand.Light) buffer.Clear();
            StartAttack(moveSet.Launcher, PlayerAttackKind.Launcher, -1, ChargeTier.None, isCounter || ConsumeCounterWindow(), true, world);
            lightPressClock = double.NegativeInfinity;       // one launcher per hold
        }

        void TryAbility(MoveData move, in PlayerWorldState world)
        {
            if (Aloft || !CanStartAttack() || !stamina.CanAct) return;   // grounded only: waits in the buffer for the landing
            buffer.Clear();
            if (move != null) StartAttack(move, PlayerAttackKind.Ability, -1, ChargeTier.None, ConsumeCounterWindow(), true, world);
        }

        void TryHeavy(in PlayerWorldState world)
        {
            if (Aloft)
            {
                TryPlunge(world);
                return;
            }
            if (!CanStartAttack() || !stamina.CanAct) return;
            buffer.Clear();
            if (moveSet.Heavy != null) StartCharge(world);
        }

        void TrySkill(in PlayerWorldState world)
        {
            if (Aloft || !CanStartAttack() || !stamina.CanAct) return;   // grounded only: waits in the buffer for the landing
            buffer.Clear();
            if (moveSet.Skill != null) StartAttack(moveSet.Skill, PlayerAttackKind.Skill, -1, ChargeTier.None, ConsumeCounterWindow(), true, world);
        }

        void TryZipStrike(in PlayerWorldState world)
        {
            bool canStart = state == PlayerState.Airborne || CanStartAttack();   // a zip works from the air too
            if (!canStart || !stamina.CanAct) return;
            buffer.Clear();
            if (moveSet.ZipStrike == null || !world.HasZipTarget) return;
            StartAttack(moveSet.ZipStrike, PlayerAttackKind.ZipStrike, -1, ChargeTier.None, ConsumeCounterWindow(), true, world);
        }

        void TryPlunge(in PlayerWorldState world)
        {
            // Mid air strike or air dash: wait for its cancel point (the press stays buffered).
            if (state == PlayerState.Dodging) return;
            if (state == PlayerState.Attacking && !CanStartAttack()) return;
            // Too soon after leaving the ground: the press does nothing, and isn't saved to plunge later either.
            if (airTime < Plunge.MinAirTime)
            {
                buffer.Clear();
                return;
            }
            if (!stamina.CanAct) return;
            buffer.Clear();
            if (moveSet.PlungeAttack != null) StartPlunge(world);
        }

        void TryHeal()
        {
            if (!IsFree || !OnGroundish) return;
            buffer.Clear();
            if (healCharges <= 0)
            {
                Emit(PlayerEventType.HealFailed);
                return;
            }
            ExitAction(true);
            state = PlayerState.Healing;
            healApplied = false;
            action.Begin();
            Emit(PlayerEventType.HealStarted);
        }

        // After a perfect dodge, the first attack started inside the window is a counter (bonus damage).
        bool ConsumeCounterWindow()
        {
            if (clock > counterWindowUntil) return false;
            counterWindowUntil = double.NegativeInfinity;
            return true;
        }

        // ---------------------------------------------------------------- attacks

        void StartAttack(MoveData move, PlayerAttackKind kind, int chain, ChargeTier tier, bool counter, bool payStamina,
            in PlayerWorldState world)
        {
            ExitAction(true);
            state = PlayerState.Attacking;
            currentMove = move;
            attackKind = kind;
            chainIndex = chain;
            chargeTier = tier;
            isCounter = counter;
            activeOpen = false;
            if (payStamina) SpendStamina(move.StaminaCost);
            attackStartClock = clock;
            lungeDistance = PlanLunge(move, kind, world);
            // Air strikes lift you as they start (you hang while striking); the launcher lifts you when its kick lands.
            // An air strike stalls you: your rise is replaced by its own small lift (or none, over a standing foe), so a
            // jump's speed can't carry you sky-high under the lighter air-strike gravity.
            if (kind == PlayerAttackKind.Air && !grounded) verticalVelocity = Math.Min(verticalVelocity, 0f);
            if (move.SelfLift > 0f && kind != PlayerAttackKind.Launcher && !AboveGroundedTarget(world)) ApplySelfLift(move.SelfLift);
            currentAttackId = CombatIds.Next();
            RememberAttack(currentAttackId, tier == ChargeTier.FaJin ? Charge.FaJinMomentumGain : move.MomentumGain);
            moveVelocity = Vector3.Zero;
            action.Begin();
            EmitMoveEvent(PlayerEventType.AttackStarted);
            UpdateAttack(world);   // moments at time 0 (a move with no startup is active at once)
        }

        // How far this attack travels forward. LimitApproach still stops every step short of the target's body.
        float PlanLunge(MoveData move, PlayerAttackKind kind, in PlayerWorldState world)
        {
            float own = Math.Max(0f, move.LungeDistance);
            lungeHoming = false;
            if (kind == PlayerAttackKind.ZipStrike)
            {
                lungeHoming = world.HasZipTarget;
                if (world.HasZipTarget)
                {
                    lungeTargetFeet = world.ZipTargetPosition;
                    lungeTargetRadius = world.ZipTargetRadius;
                }
                return world.HasZipTarget ? GapTo(world.ZipTargetPosition, world.ZipTargetRadius, world) : 0f;
            }
            if ((kind != PlayerAttackKind.Light && kind != PlayerAttackKind.Air) || !(tuning.GapCloseDistance > 0f)) return own;
            float gap;
            if (world.HasLockTarget)
            {
                gap = GapTo(world.LockTargetPosition, world.LockTargetRadius, world);
                lungeTargetFeet = world.LockTargetPosition;
                lungeTargetRadius = world.LockTargetRadius;
            }
            else if (world.HasSoftTarget)
            {
                gap = GapTo(world.SoftTargetPosition, world.SoftTargetRadius, world);
                lungeTargetFeet = world.SoftTargetPosition;
                lungeTargetRadius = world.SoftTargetRadius;
            }
            else return own;
            // Only a stretched lunge homes in; a normal step keeps following the facing as it always has.
            lungeHoming = gap > own;
            return Math.Max(own, Math.Min(own + tuning.GapCloseDistance, gap));
        }

        // An air strike only lifts you when there's something up here to hit: against a foe standing on the ground (you
        // jumped at it), rising above its head would be silly (report 03, V-10). No target at all: it lifts (air practice).
        bool AboveGroundedTarget(in PlayerWorldState world)
        {
            Vector3 target;
            if (world.HasLockTarget) target = world.LockTargetPosition;
            else if (world.HasSoftTarget) target = world.SoftTargetPosition;
            else return false;
            return world.Position.Y > target.Y + Aerial.AirLiftMaxHeightAboveTarget;
        }

        // Flat distance we could travel toward a target before reaching LungeStopGap from its body.
        float GapTo(Vector3 target, float targetRadius, in PlayerWorldState world)
        {
            float distance = Directions.Flatten(target - world.Position).Length();
            return Math.Max(0f, distance - Math.Max(0f, world.SelfRadius) - Math.Max(0f, targetRadius) - tuning.LungeStopGap);
        }

        void UpdateAttack(in PlayerWorldState world)
        {
            MoveData move = currentMove;
            if (action.Crossed(move.Startup))
            {
                if (move.LaunchesProjectile) LaunchProjectile(world);
                else OpenActive(world);
            }
            if (activeOpen && action.Crossed(move.ActiveEnd)) CloseActive();
            action.MarkChecked();

            // A light press inside the combo window is accepted as the chain follow-up (see top of file).
            bool stringMove = attackKind == PlayerAttackKind.Light || attackKind == PlayerAttackKind.Air;
            if (stringMove && buffer.Command == PlayerCommand.Light && !buffer.Locked
                && action.Time >= move.ComboWindowStart && action.Time <= move.ComboWindowEnd)
            {
                buffer.Lock();
            }

            if (action.Time >= move.TotalDuration)
            {
                // If the combo window reaches past the end of the move (e.g. its recovery was tuned shorter),
                // a light press shortly after it ends, or one already queued, still continues the chain.
                PlayerAttackKind kindEnded = attackKind;
                bool wasLight = kindEnded == PlayerAttackKind.Light || kindEnded == PlayerAttackKind.Air;
                bool queued = buffer.Locked && buffer.Command == PlayerCommand.Light;
                // The ground string loops; the air string doesn't (TryAirAttack refuses an index past its end).
                int next = kindEnded == PlayerAttackKind.Air ? chainIndex + 1 : (chainIndex + 1) % Math.Max(1, ChainLength);
                float grace = Math.Max(0f, move.ComboWindowEnd - move.TotalDuration);
                FinishAction();
                if (wasLight && (grace > 0f || queued))
                {
                    chainGraceUntil = clock + grace;
                    chainGraceNext = next;
                    chainGraceKind = kindEnded;
                }
            }
        }

        void OpenActive(in PlayerWorldState world)
        {
            activeOpen = true;
            if (attackKind == PlayerAttackKind.Launcher && currentMove.SelfLift > 0f) ApplySelfLift(currentMove.SelfLift);
            Emit(new PlayerEvent
            {
                Type = PlayerEventType.AttackActiveStart, Move = currentMove, AttackKind = attackKind, AttackId = currentAttackId,
                Origin = GetStrikeOrigin(world.Position), Direction = Forward, ChargeTier = chargeTier, IsCounter = isCounter
            });
        }

        void CloseActive()
        {
            activeOpen = false;
            EmitMoveEvent(PlayerEventType.AttackActiveEnd);
        }

        // Projectiles fly along the committed facing, pitched up or down toward the target's chest.
        void LaunchProjectile(in PlayerWorldState world)
        {
            Vector3 origin = GetStrikeOrigin(world.Position);
            Vector3 direction = Forward;
            if (TryGetAimTarget(world, out Vector3 aimPoint))
            {
                float pitch = Directions.PitchOf(aimPoint - origin);
                direction = Directions.FromYawPitch(facingYaw, pitch);
            }
            Emit(new PlayerEvent
            {
                Type = PlayerEventType.ProjectileLaunched, Move = currentMove, AttackKind = attackKind, AttackId = currentAttackId,
                Origin = origin, Direction = direction, IsCounter = isCounter
            });
        }

        bool TryGetAimTarget(in PlayerWorldState world, out Vector3 aimPoint)
        {
            if (world.HasLockTarget)
            {
                aimPoint = world.LockTargetAimPoint != Vector3.Zero ? world.LockTargetAimPoint : world.LockTargetPosition;
                return true;
            }
            if (world.HasSoftTarget)
            {
                aimPoint = world.SoftTargetAimPoint != Vector3.Zero ? world.SoftTargetAimPoint : world.SoftTargetPosition;
                return true;
            }
            aimPoint = Vector3.Zero;
            return false;
        }

        // ---------------------------------------------------------------- charged heavy

        void StartCharge(in PlayerWorldState world)
        {
            ExitAction(true);
            state = PlayerState.Charging;
            currentMove = moveSet.Heavy;
            attackKind = PlayerAttackKind.Heavy;
            chainIndex = -1;
            chargeTier = ChargeTier.None;
            chargeTime = HeldHeavyCredit();
            sweetSpotAnnounced = false;
            readyCueAnnounced = false;
            isCounter = ConsumeCounterWindow();   // committing to the heavy inside the window makes it the counter
            currentAttackId = 0;
            SpendStamina(currentMove.StaminaCost);
            moveVelocity = Vector3.Zero;
            action.Begin();
            EmitMoveEvent(PlayerEventType.ChargeStarted);
            UpdateCharge(world);                  // a tap already released: quick heavy straight away
        }

        // A heavy pressed during another move starts charging only at that move's cancel point. If the button has
        // been held all along, that time counts, capped just before the ready cue so the cue still shows.
        float HeldHeavyCredit()
        {
            if (!heavyHeld) return 0f;
            ChargeSettings charge = Charge;
            float held = (float)(realClock - heavyPressRealClock);
            float cap = Math.Max(0f, charge.SweetSpotStart - charge.ReadyCueLead - Epsilon);
            return Angles.Clamp(held, 0f, cap);
        }

        // The charge clock is advanced (on real time) by AdvanceAction before this runs.
        void UpdateCharge(in PlayerWorldState world)
        {
            ChargeSettings charge = Charge;
            if (!readyCueAnnounced && chargeTime >= charge.SweetSpotStart - charge.ReadyCueLead)
            {
                readyCueAnnounced = true;
                EmitMoveEvent(PlayerEventType.ChargeReadyCue);
            }
            if (!sweetSpotAnnounced && chargeTime >= charge.SweetSpotStart)
            {
                sweetSpotAnnounced = true;
                EmitMoveEvent(PlayerEventType.ChargeSweetSpot);
            }
            action.MarkChecked();
            if (!heavyHeld || chargeTime >= charge.MaxChargeTime) ReleaseCharge(world);
        }

        void ReleaseCharge(in PlayerWorldState world)
        {
            ChargeTier tier = EvaluateChargeTier(chargeTime);
            releasingCharge = true;
            StartAttack(currentMove, PlayerAttackKind.Heavy, -1, tier, isCounter, false, world);
            releasingCharge = false;
        }

        ChargeTier EvaluateChargeTier(float time)
        {
            ChargeSettings charge = Charge;
            if (time < charge.QuickReleaseTime) return ChargeTier.Quick;
            if (time < charge.SweetSpotStart) return ChargeTier.Partial;
            if (time <= charge.SweetSpotEnd + Epsilon) return ChargeTier.FaJin;
            return ChargeTier.Charged;
        }

        // ---------------------------------------------------------------- plunge (jump attack)

        void StartPlunge(in PlayerWorldState world)
        {
            ExitAction(true);
            state = PlayerState.Plunging;
            currentMove = moveSet.PlungeAttack;
            attackKind = PlayerAttackKind.Plunge;
            chainIndex = -1;
            chargeTier = ChargeTier.None;
            isCounter = ConsumeCounterWindow();
            plungeFalling = false;
            plungeLanded = false;
            landingTime = 0f;
            SpendStamina(currentMove.StaminaCost);
            currentAttackId = CombatIds.Next();
            RememberAttack(currentAttackId, currentMove.MomentumGain);
            moveVelocity = Vector3.Zero;
            action.Begin();
            EmitMoveEvent(PlayerEventType.AttackStarted);
            UpdatePlunge(world);
        }

        void UpdatePlunge(in PlayerWorldState world)
        {
            PlungeSettings plunge = Plunge;
            if (!plungeFalling && action.Time >= plunge.HangTime) plungeFalling = true;
            if (plungeFalling && !plungeLanded && grounded)
            {
                plungeLanded = true;
                landingTime = action.Time;
                Emit(new PlayerEvent
                {
                    Type = PlayerEventType.PlungeImpact, Move = currentMove, AttackKind = attackKind, AttackId = currentAttackId,
                    Origin = world.Position, Radius = plunge.RingRadius, IsCounter = isCounter
                });
                Emit(new PlayerEvent { Type = PlayerEventType.Landed, Amount = plunge.FallSpeed });
            }
            action.MarkChecked();

            if (plungeLanded && PlungeTimeSinceLanding >= currentMove.Recovery)
            {
                FinishAction();
            }
            else if (!plungeLanded && plungeFalling && action.Time - plunge.HangTime > plunge.MaxFallTime)
            {
                // Safety net: never landed (a gap in the floor, a steep slope). Drop back to normal falling.
                FinishAction();
            }
        }

        // ---------------------------------------------------------------- heal, stagger, death

        void UpdateHeal()
        {
            float applyAt = Math.Min(tuning.HealApplyTime, tuning.HealDuration);
            if (!healApplied && action.Crossed(applyAt))
            {
                healApplied = true;
                if (healCharges > 0)
                {
                    healCharges--;
                    float before = health;
                    health = Math.Min(MaxHealth, health + Math.Max(0f, tuning.HealAmount));
                    Emit(new PlayerEvent { Type = PlayerEventType.HealApplied, Amount = health - before });
                }
            }
            action.MarkChecked();
            if (action.Time >= tuning.HealDuration) FinishAction();
        }

        void Stagger(float duration)
        {
            if (state == PlayerState.Dead) return;
            ExitAction(true);
            state = PlayerState.Staggered;
            staggerDuration = Math.Max(0f, duration);
            moveVelocity = Vector3.Zero;
            buffer.Clear();
            action.Begin();
            Emit(new PlayerEvent { Type = PlayerEventType.Staggered, Duration = staggerDuration });
        }

        void UpdateStagger()
        {
            action.MarkChecked();
            if (action.Time >= staggerDuration) FinishAction();
        }

        void Die()
        {
            ExitAction(true);
            state = PlayerState.Dead;
            health = 0f;
            moveVelocity = Vector3.Zero;
            knockback.Stop();
            buffer.Clear();
            Emit(PlayerEventType.Died);
        }

        // ---------------------------------------------------------------- running and ending actions

        void AdvanceAction(float dt, in PlayerWorldState world)
        {
            actionStep = Vector3.Zero;
            if (!action.IsRunning) return;
            action.Advance(dt);
            // Worked out before the action gets a chance to end this frame, so its last slice of movement
            // (the end of a dash or lunge) is never lost, whatever the frame rate.
            actionStep = ActionStep(world);
            switch (state)
            {
                case PlayerState.Attacking: UpdateAttack(world); break;
                case PlayerState.Charging:
                    chargeTime += frameRealDt;   // real time: a practised hold isn't thrown off by slow motion
                    UpdateCharge(world);
                    break;
                case PlayerState.Plunging: UpdatePlunge(world); break;
                case PlayerState.Dodging: UpdateDodge(); break;
                case PlayerState.Healing: UpdateHeal(); break;
                case PlayerState.Staggered: UpdateStagger(); break;
            }
        }

        void FinishAction()
        {
            ExitAction(false);
            EnterFreeState();
        }

        void EnterFreeState()
        {
            if (state == PlayerState.Dead) return;
            state = OnGroundish ? PlayerState.Locomotion : PlayerState.Airborne;
        }

        // Leaves the current state tidily, whatever the reason: every "start" event gets its "end" event
        // (an open active window always reports AttackActiveEnd), so the Unity side can never be left with a
        // lingering hitbox, glow or i-frame look.
        void ExitAction(bool interrupted)
        {
            switch (state)
            {
                case PlayerState.Attacking:
                    if (activeOpen) CloseActive();
                    EmitMoveEvent(PlayerEventType.AttackEnded);
                    buffer.Unlock();
                    break;
                case PlayerState.Charging:
                    if (!releasingCharge) EmitMoveEvent(PlayerEventType.ChargeCancelled);
                    break;
                case PlayerState.Plunging:
                    EmitMoveEvent(PlayerEventType.AttackEnded);
                    break;
                case PlayerState.Dodging:
                    EndDodgeIFrames();
                    Emit(PlayerEventType.DodgeEnded);
                    break;
                case PlayerState.Healing:
                    if (!healApplied) Emit(PlayerEventType.HealInterrupted);
                    break;
                case PlayerState.Staggered:
                    Emit(PlayerEventType.StaggerEnded);
                    break;
                case PlayerState.Guarding:
                    Emit(PlayerEventType.GuardEnded);
                    break;
                case PlayerState.Sprinting:
                    Emit(PlayerEventType.SprintEnded);
                    sprintEndedClock = clock;         // for the sprint attack's grace period
                    lastSprintDuration = sprintTime;
                    sprintTime = 0f;
                    break;
            }
            action.Stop();
            activeOpen = false;
            chainGraceUntil = double.NegativeInfinity;
            if (!releasingCharge)
            {
                currentMove = null;
                attackKind = PlayerAttackKind.None;
                chargeTier = ChargeTier.None;
                isCounter = false;
            }
            chainIndex = -1;
            if (interrupted && state == PlayerState.Charging) chargeTime = 0f;
        }
    }
}
