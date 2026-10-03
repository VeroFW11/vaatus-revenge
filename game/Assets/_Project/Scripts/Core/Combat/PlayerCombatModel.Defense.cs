using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Dodging, guarding, deflecting and taking hits.
    //
    // DODGE (Spider-Man 2 style): you never turn your back on the fight. The focus target, first match wins: the lock
    //   target, the attacker of the strike about to land (danger sense, within AutoEvadeLookahead), the soft-lock target,
    //   the nearest enemy within FocusRadius. With one, you snap to face it and keep turning to it (FocusTurnRate), and the
    //   stick against the direction to it picks the kind (DodgeKind): toward = SlipIn (stops SlipInStopGap from its body),
    //   away = EvadeOut, sideways = SideSlip (round it), neutral = AutoEvade (a strike about to land: a side-step across
    //   its path, away from the other enemies) or Backstep. With nobody near, you dash along the stick facing the dash
    //   (Traverse); the same from a run (Fluid's hold-B sprint starts with a dodge) when nobody is locked, no strike is
    //   coming and the stick isn't pointed at the foe (PlayerTuning.RunDodgeKeepsHeading). A stick held as the dodge ends carries you on at RunSpeed x ExitSpeedCarry. ChainMax dodges in a row
    //   (each starting within ChainLink of the last one ending) are followed by ChainCooldown with no dodge
    //   (DodgeChainLimited). Dodging never drops the string (see .Rhythm): X late in a dodge is the dodge strike.
    // I-FRAMES: during part of a dodge, hits pass through you ("invincibility frames"). A new dodge's
    //   i-frames can never start sooner than Dodge.ChainIFrameGap after the previous dodge's ended, however
    //   the numbers are tuned, so spamming dodge always leaves you open for a moment.
    // PERFECT DODGE: once per dodge, a strike landing within Dodge.PerfectWindow of the dodge starting, while the
    //   i-frames are up, earns Momentum (more when you dashed toward the attacker), a counter window and a slow-motion
    //   event. With PerfectRule = WouldHaveLanded, a strike that misses because you dashed away still counts if its
    //   arc covered where you started: the enemy side reports every strike with NotifyEnemyStrike.
    // GUARD (Guard.Style = BlockAndParry only): blocks hits from the front arc for stamina; not enough stamina =
    //   guard break (stagger).
    // DEFLECT (a parry, both styles): a guard press within Guard.DeflectWindow before a parryable hit lands
    //   deflects it for free and the attacker is told HitOutcome.Parried. A press that deflects nothing locks
    //   deflecting out briefly. A quick tap still counts: the guard stays up for at least the deflect window.
    // PARRY ONLY (Guard.Style = ParryOnly): a press opens the deflect window over Guard.ParryArcDegrees and the
    //   stance drops by itself when it closes, held or not. Anything the parry doesn't catch hits you cleanly.
    // INCOMING HIT ORDER: dead/own team/already hit by this attack -> ignored; i-frames -> evade; guarding and
    //   the hit is from the front and blockable -> deflect or block; otherwise a clean hit (health, poise, death).
    public sealed partial class PlayerCombatModel
    {
        Vector3 dodgeDirection;
        float dodgeDistance;
        bool dodgeIsBackstep;
        DodgeKind dodgeKind;
        double dodgeStartClock;
        bool dodgeHasFocus;           // the dodge keeps turning to dodgeFocus
        FocusTarget dodgeFocus;
        int dodgeChainCount;          // dodges in the current chain
        double dodgeChainCooldownUntil = double.NegativeInfinity;
        double lastDodgeEndClock = double.NegativeInfinity;
        bool lastDodgeWasAir;
        float sideSlipRadius;         // a side-slip circles its focus at this distance (centre to centre)...
        float sideSlipSign;           // ...this way round (+1 = the offset's yaw grows)
        bool dodgeFromRun;            // B went down while running (PlayerTuning.RunDodgeKeepsHeading)

        // Whom a dodge faces (see the top of the file). Kept by source, so a dodge keeps tracking the same enemy.
        enum FocusSource { None, Lock, Threat, Soft, Nearest }

        struct FocusTarget
        {
            public FocusSource Source;
            public Vector3 Position;
            public float Radius;
            public int AttackerId;    // Threat: whose strike
        }
        double iFrameStart = double.NegativeInfinity;
        double iFrameEnd = double.NegativeInfinity;
        double lastIFrameEnd = double.NegativeInfinity;
        bool perfectRewardGiven;
        Vector3 dodgeStartPosition;    // where the dodge began: "would the strike have landed?" is asked here
        float dodgeStartRadius;

        double guardHeldUntil;                         // a tapped guard stays up at least this long
        bool deflectArmed;
        bool deflectCaught;
        double deflectPressClock = double.NegativeInfinity;
        double deflectLockoutUntil = double.NegativeInfinity;

        public bool IsInvulnerable => state == PlayerState.Dodging && clock >= iFrameStart && clock < iFrameEnd;
        public bool IsDeflectWindowOpen => state == PlayerState.Guarding && deflectArmed
            && clock - deflectPressClock <= Guard.DeflectWindow + Epsilon;
        public float DeflectLockoutRemaining => clock < deflectLockoutUntil ? (float)(deflectLockoutUntil - clock) : 0f;
        public float DodgeTime => state == PlayerState.Dodging ? action.Time : 0f;

        void ResetDefense()
        {
            iFrameStart = double.NegativeInfinity;
            iFrameEnd = double.NegativeInfinity;
            lastIFrameEnd = double.NegativeInfinity;
            perfectRewardGiven = false;
            guardHeldUntil = double.NegativeInfinity;
            deflectArmed = false;
            deflectCaught = false;
            deflectPressClock = double.NegativeInfinity;
            deflectLockoutUntil = double.NegativeInfinity;
            Array.Clear(recentHitsTaken, 0, recentHitsTaken.Length);
            dodgeKind = DodgeKind.Traverse;
            dodgeHasFocus = false;
            dodgeChainCount = 0;
            dodgeChainCooldownUntil = double.NegativeInfinity;
            lastDodgeEndClock = double.NegativeInfinity;
            lastDodgeWasAir = false;
        }

        // ---------------------------------------------------------------- dodge

        // The dodge the next dodge will use (the active element's; a running dodge keeps its own, see Dodge).
        DodgeProfile NextDodge => moveSet.Dodge ?? FallbackDodge;

        void TryDodge(in PlayerWorldState world)
        {
            if (clock < dodgeChainCooldownUntil)
            {
                buffer.Clear();                              // the breather: a press during it is dropped, not saved for its end
                return;
            }
            if (!CanDodgeAgain()) return;                   // the chain is full: the press waits for this dodge to end
            if (Aloft)
            {
                TryAirDash(world);
                return;
            }
            if (!CanDefensiveCancel(true) || !HasDodgeStamina()) return;
            buffer.Clear();
            StartDodge(world, false);
        }

        // Dodge in the air: a dash with no gravity, AirDashesPerJump times before landing.
        void TryAirDash(in PlayerWorldState world)
        {
            bool allowed = state == PlayerState.Airborne
                || (state == PlayerState.Attacking && action.Time >= currentMove.DodgeCancelAt)
                || (state == PlayerState.Dodging && action.Time >= Dodge.NextDodgeAt);
            AerialSettings aerial = moveSet.Aerial ?? FallbackAerial;
            if (!allowed || airDashesUsed >= Math.Max(0, aerial.AirDashesPerJump))
            {
                if (state == PlayerState.Airborne) buffer.Clear();   // no dash left: don't save it for the landing
                return;
            }
            if (!HasDodgeStamina()) return;
            buffer.Clear();
            airDashesUsed++;
            StartDodge(world, true);
        }

        // Fluid: an empty bar still dodges (you just can't attack). Punishing: no stamina, no dodge.
        bool HasDodgeStamina()
        {
            return !NextDodge.RequiresStamina || stamina.CanAct;
        }

        // ChainMax dodges in a row, then ChainCooldown before the next.
        bool CanDodgeAgain()
        {
            if (clock < dodgeChainCooldownUntil) return false;
            return !(state == PlayerState.Dodging && dodgeChainCount >= Math.Max(1, Dodge.ChainMax));
        }

        void StartDodge(in PlayerWorldState world, bool inAir)
        {
            bool chained = state == PlayerState.Dodging || clock - lastDodgeEndClock <= NextDodge.ChainLink + 1e-6;
            // Running when B went down (see PlayerTuning.RunDodgeKeepsHeading): read before the action resets anything.
            dodgeFromRun = !inAir && (state == PlayerState.Locomotion || state == PlayerState.Sprinting) && grounded
                           && Directions.Flatten(moveVelocity).Length() >= Math.Max(0f, tuning.RunDodgeMinSpeed);
            ExitAction(true);
            state = PlayerState.Dodging;
            DodgeProfile dodge = Dodge;                      // the active element's (actionSet was just set to it)
            dodgeChainCount = chained ? dodgeChainCount + 1 : 1;
            dodgeInAir = inAir;
            HoldStringIfLive(true);

            PlanDodge(world, dodge, inAir);
            dodgeIsBackstep = dodgeKind == DodgeKind.Backstep;

            SpendStamina(dodge.StaminaCost);
            dodgeStartClock = clock;
            dodgeStartPosition = world.Position;
            dodgeStartRadius = Math.Max(0f, world.SelfRadius);
            iFrameStart = Math.Max(clock + (inAir ? 0f : dodge.IFrameStart), lastIFrameEnd + Math.Max(0f, dodge.ChainIFrameGap));
            iFrameEnd = clock + (inAir ? Aerial.AirDashIFrameEnd : dodge.IFrameEnd);
            if (inAir) verticalVelocity = 0f;
            perfectRewardGiven = false;
            moveVelocity = Vector3.Zero;
            action.Begin();
            Vector3 forward = Forward;
            Vector3 right = Directions.FromYaw(facingYaw + 90f);
            Emit(new PlayerEvent
            {
                Type = PlayerEventType.DodgeStarted, Direction = dodgeDirection, Amount = dodgeDistance, IsBackstep = dodgeIsBackstep,
                InAir = inAir, DodgeKind = dodgeKind, Element = actionSet.Element,
                LocalDirection = new Vector3(Vector3.Dot(dodgeDirection, right), 0f, Vector3.Dot(dodgeDirection, forward))
            });
        }

        // The dodge's kind, direction, distance and facing (see the top of the file).
        void PlanDodge(in PlayerWorldState world, DodgeProfile dodge, bool inAir)
        {
            Vector3 stick = Directions.CameraRelative(moveStick, world.CameraYaw);
            float stickLength = stick.Length();
            bool hasStick = stickLength > Math.Max(tuning.StickDeadzone, Epsilon);
            Vector3 stickDir = hasStick ? stick / stickLength : Vector3.Zero;
            dodgeHasFocus = TryGetFocusTarget(world, out dodgeFocus);
            // A dodge from a run, with nobody locked and no strike coming (the focus is only the soft / nearest target) and
            // the stick not pointed at that foe: a plain dash along the stick, as with nobody near (round 7, J7-05).
            if (dodgeHasFocus && dodgeFromRun && hasStick && tuning.RunDodgeKeepsHeading
                && (dodgeFocus.Source == FocusSource.Soft || dodgeFocus.Source == FocusSource.Nearest)
                && AngleBetween(stickDir, Directions.Flatten(dodgeFocus.Position - world.Position)) > tuning.RunDodgeSlipInAngle)
            {
                dodgeHasFocus = false;
            }
            Vector3 toFocus = Vector3.Zero;
            if (dodgeHasFocus)
            {
                facingYaw = YawTowards(world.Position, dodgeFocus.Position);   // never turn your back on the fight
                toFocus = Forward;
            }

            if (inAir)
            {
                dodgeKind = DodgeKind.AirDash;
                dodgeDirection = hasStick ? stickDir : Forward;
                dodgeDistance = Math.Max(0f, Aerial.AirDashDistance);
                if (!dodgeHasFocus) facingYaw = Directions.YawOf(dodgeDirection, facingYaw);
                return;
            }
            if (!dodgeHasFocus)
            {
                // Nobody to face: dash where you push (facing the dash), or a short hop back keeping your facing.
                dodgeKind = hasStick ? DodgeKind.Traverse : DodgeKind.Backstep;
                dodgeDirection = hasStick ? stickDir : -Forward;
                dodgeDistance = Math.Max(0f, hasStick ? dodge.EvadeOutDistance : dodge.BackstepDistance);
                if (hasStick) facingYaw = Directions.YawOf(dodgeDirection, facingYaw);
                return;
            }
            if (hasStick)
            {
                float angle = AngleBetween(stickDir, toFocus);
                if (angle <= dodge.SlipInAngle)
                {
                    dodgeKind = DodgeKind.SlipIn;
                    dodgeDirection = toFocus;
                    float gap = Directions.Flatten(dodgeFocus.Position - world.Position).Length() - Math.Max(0f, world.SelfRadius)
                        - Math.Max(0f, dodgeFocus.Radius) - Math.Max(0f, dodge.SlipInStopGap);
                    dodgeDistance = Angles.Clamp(gap, 0f, Math.Max(0f, dodge.SlipInMaxDistance));
                }
                else if (angle >= dodge.EvadeOutAngle)
                {
                    dodgeKind = DodgeKind.EvadeOut;
                    dodgeDirection = stickDir;
                    dodgeDistance = Math.Max(0f, dodge.EvadeOutDistance);
                }
                else
                {
                    // Round the target: a circle about it at the distance you started (never closer than a slip-in stops),
                    // on the side the stick points, at most SideSlipMaxDegrees round (see CircleStep).
                    dodgeKind = DodgeKind.SideSlip;
                    Vector3 side = Directions.FromYaw(facingYaw + 90f);
                    dodgeDirection = Vector3.Dot(stickDir, side) >= 0f ? side : -side;
                    dodgeDistance = Math.Max(0f, dodge.SideSlipDistance);
                    Vector3 offset = Directions.Flatten(world.Position - dodgeFocus.Position);
                    float closest = Math.Max(0f, world.SelfRadius) + Math.Max(0f, dodgeFocus.Radius) + Math.Max(0f, dodge.SlipInStopGap);
                    sideSlipRadius = Math.Max(offset.Length(), Math.Max(closest, 0.1f));
                    Vector3 aroundPlus = Directions.FromYaw(Directions.YawOf(offset, facingYaw + 180f) + 90f);
                    sideSlipSign = Vector3.Dot(dodgeDirection, aroundPlus) >= 0f ? 1f : -1f;
                    if (dodge.SideSlipMaxDegrees > 0f)
                        dodgeDistance = Math.Min(dodgeDistance, sideSlipRadius * dodge.SideSlipMaxDegrees * Directions.Deg2Rad);
                }
                return;
            }
            int threat = dodge.AutoEvadeOnNeutral ? MostImminentThreat(DangerRules.AutoEvadeLookahead) : -1;
            if (threat >= 0)
            {
                dodgeKind = DodgeKind.AutoEvade;
                dodgeDirection = AutoEvadeDirection(world, threats[threat]);
                dodgeDistance = Math.Max(0f, dodge.SideSlipDistance);
                return;
            }
            dodgeKind = DodgeKind.Backstep;
            dodgeDirection = -Forward;
            dodgeDistance = Math.Max(0f, dodge.BackstepDistance);
        }

        // Out of the strike's path: perpendicular to it, on the side away from the other enemies around (their middle);
        // with nobody else around, to the camera's right (deterministic).
        Vector3 AutoEvadeDirection(in PlayerWorldState world, in IncomingStrike strike)
        {
            Vector3 along = Directions.SafeNormalize(Directions.Flatten(strike.StrikeForward),
                Directions.SafeNormalize(Directions.Flatten(world.Position - strike.AttackerFeet), Forward));
            var side = new Vector3(along.Z, 0f, -along.X);    // along turned 90 degrees clockwise (to its right)
            Vector3 others = Vector3.Zero;
            int count = 0;
            float radius = NextDodge.FocusRadius;
            for (int i = 0; i < threatCount; i++)
            {
                if (threats[i].AttackerId == strike.AttackerId || IsSameAttacker(threats, i)) continue;
                if (Directions.Flatten(threats[i].AttackerFeet - world.Position).Length() > radius) continue;
                others += threats[i].AttackerFeet;
                count++;
            }
            if (world.HasNearestEnemy && Directions.Flatten(world.NearestEnemyPosition - strike.AttackerFeet).Length() > SameEnemyDistance
                && Directions.Flatten(world.NearestEnemyPosition - world.Position).Length() <= radius)
            {
                others += world.NearestEnemyPosition;
                count++;
            }
            float away = 0f;
            if (count > 0) away = -Vector3.Dot(Directions.Flatten(others / count - world.Position), side);
            if (Math.Abs(away) < 1e-3f) away = Vector3.Dot(Directions.FromYaw(world.CameraYaw + 90f), side);   // tie: camera right
            return away >= 0f ? side : -side;
        }

        // Positions closer than this belong to the same enemy (a threat's registered feet vs the nearest enemy's).
        const float SameEnemyDistance = 0.75f;

        // An earlier entry already counted this attacker (several strikes of one attack).
        static bool IsSameAttacker(IncomingStrike[] list, int index)
        {
            for (int i = 0; i < index; i++)
            {
                if (list[i].AttackerId == list[index].AttackerId) return true;
            }
            return false;
        }

        static float AngleBetween(Vector3 a, Vector3 b)
        {
            float cos = Vector3.Dot(Directions.SafeNormalize(a, Vector3.Zero), Directions.SafeNormalize(b, Vector3.Zero));
            return (float)Math.Acos(Angles.Clamp(cos, -1f, 1f)) * Directions.Rad2Deg;
        }

        // The dodge's focus (see the top of the file). Threat focus uses where the attacker stood at its wind-up.
        bool TryGetFocusTarget(in PlayerWorldState world, out FocusTarget focus)
        {
            focus = default;
            DodgeProfile dodge = NextDodge;
            if (world.HasLockTarget)
            {
                focus = new FocusTarget { Source = FocusSource.Lock, Position = world.LockTargetPosition, Radius = world.LockTargetRadius };
                return true;
            }
            int threat = MostImminentThreat(DangerRules.AutoEvadeLookahead);
            if (threat >= 0)
            {
                focus = new FocusTarget
                {
                    Source = FocusSource.Threat, Position = threats[threat].AttackerFeet, AttackerId = threats[threat].AttackerId,
                    Radius = RadiusNear(world, threats[threat].AttackerFeet)
                };
                return true;
            }
            if (world.HasSoftTarget)
            {
                focus = new FocusTarget { Source = FocusSource.Soft, Position = world.SoftTargetPosition, Radius = world.SoftTargetRadius };
                return true;
            }
            if (world.HasNearestEnemy && Directions.Flatten(world.NearestEnemyPosition - world.Position).Length() <= dodge.FocusRadius)
            {
                focus = new FocusTarget { Source = FocusSource.Nearest, Position = world.NearestEnemyPosition, Radius = world.NearestEnemyRadius };
                return true;
            }
            return false;
        }

        // Where the dodge's focus is now (the same enemy it started on; its last known place if it's gone).
        Vector3 TrackFocus(in PlayerWorldState world)
        {
            switch (dodgeFocus.Source)
            {
                case FocusSource.Lock:
                    if (world.HasLockTarget) dodgeFocus.Position = world.LockTargetPosition;
                    break;
                case FocusSource.Soft:
                    if (world.HasSoftTarget && Directions.Flatten(world.SoftTargetPosition - dodgeFocus.Position).Length() <= SameEnemyDistance * 2f)
                        dodgeFocus.Position = world.SoftTargetPosition;
                    break;
                case FocusSource.Nearest:
                case FocusSource.Threat:
                    if (world.HasNearestEnemy && Directions.Flatten(world.NearestEnemyPosition - dodgeFocus.Position).Length() <= SameEnemyDistance * 2f)
                        dodgeFocus.Position = world.NearestEnemyPosition;
                    break;
            }
            return dodgeFocus.Position;
        }

        // A body radius for an enemy known only by position: the lock / soft / nearest target's if it's one of them, else
        // ours (enemies are human-sized).
        float RadiusNear(in PlayerWorldState world, Vector3 feet)
        {
            if (world.HasSoftTarget && Directions.Flatten(world.SoftTargetPosition - feet).Length() <= SameEnemyDistance) return world.SoftTargetRadius;
            if (world.HasNearestEnemy && Directions.Flatten(world.NearestEnemyPosition - feet).Length() <= SameEnemyDistance
                && world.NearestEnemyRadius > 0f) return world.NearestEnemyRadius;
            return Math.Max(0f, world.SelfRadius);
        }

        void UpdateDodge(in PlayerWorldState world)
        {
            action.MarkChecked();
            if (action.Time < (dodgeInAir ? Aerial.AirDashDuration : Dodge.TotalDuration)) return;
            float carry = Math.Max(0f, Dodge.ExitSpeedCarry);
            bool inAir = dodgeInAir;
            Vector3 tail = DashExitVelocity();
            // The dash ended part-way through this frame: the carried speed only moves us for the rest of it.
            float end = dodgeInAir ? Aerial.AirDashDuration : Dodge.TotalDuration;
            float advanced = action.Time - action.PreviousTime;
            exitVelocityShare = advanced > 1e-6f ? Angles.Clamp((action.Time - end) / advanced, 0f, 1f) : 1f;
            FinishAction();
            Vector3 stick = Directions.CameraRelative(moveStick, world.CameraYaw);
            float stickLength = stick.Length();
            bool hasStick = stickLength > Math.Max(tuning.StickDeadzone, Epsilon);
            if (inAir)
            {
                // An air dash flows on into the jump's drift (gravity takes over) instead of stopping dead in mid-air.
                if (state != PlayerState.Airborne) return;
                float cap = Math.Max(tuning.RunSpeed, airSpeedCap);
                float speed = Math.Min(tail.Length(), cap);
                Vector3 dir = Directions.SafeNormalize(tail, hasStick ? stick / stickLength : Vector3.Zero);
                moveVelocity = dir * speed;
                airSpeedCap = Math.Max(airSpeedCap, Math.Max(tuning.RunSpeed, speed));
                return;
            }
            if (state != PlayerState.Locomotion) return;
            // Leaving with the stick held: keep running instead of accelerating again from a standstill. Either way the
            // dash's own end speed carries on and locomotion brakes it (Deceleration): no dead stop.
            Vector3 run = hasStick && carry > 0f ? stick / stickLength * (tuning.RunSpeed * carry) : Vector3.Zero;
            moveVelocity = tail.LengthSquared() > run.LengthSquared() ? tail : run;
        }

        // The dash's speed as it ends (its eased curve's last slope), capped at SprintSpeed. A slip-in stops at its gap.
        Vector3 DashExitVelocity()
        {
            if (dodgeKind == DodgeKind.SlipIn) return Vector3.Zero;
            DodgeProfile dodge = Dodge;
            if (!dodgeInAir && dodge.EndRecovery > 0f) return Vector3.Zero;   // it already stood still for its recovery
            float duration = dodgeInAir ? Aerial.AirDashDuration : dodge.Duration;
            if (!(duration > 0f)) return Vector3.Zero;
            float speed = (1f - Angles.Clamp(dodge.DashEaseOut, 0f, 1f)) * dodgeDistance / duration;
            speed = Math.Min(speed, Math.Max(tuning.RunSpeed, tuning.SprintSpeed));
            return Directions.Flatten(dodgeDirection) * speed;
        }

        // A side-slip's step this frame: the dash's length laid along the circle round the focus (it may have moved),
        // easing any change of radius back to sideSlipRadius. dodgeDirection follows the circle's tangent.
        Vector3 CircleStep(Vector3 dash, Vector3 focus, in PlayerWorldState world)
        {
            float length = dash.Length();
            Vector3 offset = Directions.Flatten(world.Position - focus);
            float radius = offset.Length();
            if (length < 1e-6f || radius < 1e-3f || !(sideSlipRadius > 0f)) return dash;
            float yaw = Directions.YawOf(offset, 0f) + sideSlipSign * (length / sideSlipRadius) * Directions.Rad2Deg;
            float newRadius = radius + Angles.Clamp(sideSlipRadius - radius, -length, length);
            Vector3 next = Directions.FromYaw(yaw) * newRadius;
            dodgeDirection = Directions.FromYaw(yaw + sideSlipSign * 90f);
            return next - offset;
        }

        // When a dodge ends (finished or cut short): the chain's breather starts after ChainMax dodges in a row.
        void EndDodgeChain()
        {
            lastDodgeEndClock = clock;
            lastDodgeWasAir = dodgeInAir;
            DodgeProfile dodge = Dodge;
            if (dodgeChainCount < Math.Max(1, dodge.ChainMax)) return;
            dodgeChainCount = 0;
            float cooldown = Math.Max(0f, dodge.ChainCooldown);
            if (!(cooldown > 0f)) return;
            dodgeChainCooldownUntil = clock + cooldown;
            Emit(new PlayerEvent { Type = PlayerEventType.DodgeChainLimited, Duration = cooldown });
        }

        // Called whenever a dodge ends (finished or cut short): remembers when its i-frames really ended.
        void EndDodgeIFrames()
        {
            if (clock >= iFrameStart && iFrameEnd > iFrameStart) lastIFrameEnd = Math.Min(iFrameEnd, clock);
            if (iFrameEnd > clock) iFrameEnd = clock;
        }

        // ---------------------------------------------------------------- guard and deflect

        bool CanGuardNow()
        {
            return CanDefensiveCancel(false) && state != PlayerState.Charging;
        }

        // A buffered guard press, run at the earliest point guarding is allowed.
        void TryGuardPress()
        {
            if (!CanGuardNow()) return;
            double pressTime = buffer.PressTime;
            buffer.Clear();
            // Still wanted? Guard held (a blocking element), or a press whose deflect window (timed from the real
            // press) is still open.
            bool holdCounts = guardHeld && !Guard.IsParryOnly;
            if (!holdCounts && clock - pressTime > Guard.DeflectWindow + Epsilon) return;
            EnterGuard(true, pressTime);
        }

        // pressed = a guard press (opens the deflect window, timed from pressClock) rather than a held button.
        void EnterGuard(bool pressed, double pressClock)
        {
            if (state != PlayerState.Guarding)
            {
                ExitAction(true);
                state = PlayerState.Guarding;
                HoldStringIfLive(false);
                Emit(PlayerEventType.GuardStarted);
            }
            if (!pressed) return;
            GuardSettings guard = Guard;
            guardHeldUntil = Math.Max(guardHeldUntil, pressClock + guard.DeflectWindow);
            bool windowLeft = clock - pressClock <= guard.DeflectWindow + Epsilon;
            if (!deflectArmed && windowLeft && clock >= deflectLockoutUntil)
            {
                deflectArmed = true;
                deflectCaught = false;
                deflectPressClock = pressClock;
            }
        }

        // A deflect window that closes without catching anything starts the lockout (anti-mash).
        void UpdateDeflectWindow()
        {
            GuardSettings guard = Guard;
            if (!deflectArmed || clock - deflectPressClock <= guard.DeflectWindow + Epsilon) return;
            deflectArmed = false;
            if (deflectCaught) return;
            deflectLockoutUntil = deflectPressClock + guard.DeflectWindow + Math.Max(0f, guard.DeflectWhiffLockout);
            Emit(new PlayerEvent { Type = PlayerEventType.DeflectWhiffed, Duration = Math.Max(0f, guard.DeflectWhiffLockout) });
        }

        // ---------------------------------------------------------------- incoming hits

        // facing = the body's forward in the world (transform.forward); Zero = use the model's facing.
        public HitResult ReceiveHit(in DamageInfo hit, Vector3 facing)
        {
            if (state == PlayerState.Dead || hit.SourceTeam == Team.Player) return HitResult.Ignored;
            if (hit.AttackId != 0 && Array.IndexOf(recentHitsTaken, hit.AttackId) >= 0) return HitResult.Ignored;

            if (IsInvulnerable) return Evade(hit);

            // Evaded hits aren't remembered, so a lingering hitbox can still catch you after your i-frames.
            if (hit.AttackId != 0)
            {
                recentHitsTaken[recentHitCursor] = hit.AttackId;
                recentHitCursor = (recentHitCursor + 1) % recentHitsTaken.Length;
            }

            Vector3 forward = Directions.Flatten(facing).LengthSquared() > 1e-6f ? Directions.Flatten(facing) : Forward;
            GuardSettings guard = Guard;
            bool parryOnly = guard.IsParryOnly;
            float arc = parryOnly ? guard.ParryArcDegrees : guard.ArcDegrees;
            if (state == PlayerState.Guarding && !hit.Unblockable && IsFromFront(hit.Direction, forward, arc))
            {
                if (hit.Parryable && deflectArmed && clock - deflectPressClock <= guard.DeflectWindow + Epsilon)
                {
                    deflectCaught = true;
                    MeterOf(actionSet.Element).Gain(guard.DeflectMomentumGain, MomentumRulesOf(actionSet.Element));
                    RefreshCombo();
                    Emit(new PlayerEvent
                    {
                        Type = PlayerEventType.Deflected, Amount = guard.DeflectMomentumGain, Direction = hit.Direction, Element = actionSet.Element
                    });
                    return new HitResult { Outcome = HitOutcome.Parried };
                }
            }
            if (state == PlayerState.Guarding && !parryOnly && !hit.Unblockable && IsFromFront(hit.Direction, forward, arc))
            {
                float cost = Math.Max(0f, hit.GuardStaminaDamage);
                if (stamina.Current >= cost)
                {
                    SpendStamina(cost);
                    Emit(new PlayerEvent { Type = PlayerEventType.Blocked, Amount = cost, Direction = hit.Direction });
                    return new HitResult { Outcome = HitOutcome.Blocked };
                }
                SpendStamina(cost);
                Emit(new PlayerEvent { Type = PlayerEventType.GuardBroken, Duration = guard.GuardBreakStagger, Direction = hit.Direction });
                EndCombo(ComboEndReason.TookHit);
                Stagger(guard.GuardBreakStagger);
                return new HitResult { Outcome = HitOutcome.GuardBroken, PoiseBroken = true };
            }

            float damage = Math.Max(0f, hit.Damage);
            health -= damage;
            var result = new HitResult { Outcome = HitOutcome.Hit, DamageDealt = damage };
            Emit(new PlayerEvent { Type = PlayerEventType.Damaged, Amount = damage, Direction = hit.Direction });
            EndCombo(ComboEndReason.TookHit);         // also forgets the string (a running string move plays on)
            if (health <= 0f)
            {
                result.Killed = true;
                Die();
                return result;
            }
            if (!HasHyperArmor && poise.Damage(hit.PoiseDamage, tuning.MaxPoise))
            {
                result.PoiseBroken = true;
                Stagger(tuning.StaggerDuration);
                knockback.Start(hit.Direction, hit.Knockback, tuning.KnockbackTime);   // only a hit that staggers pushes you
            }
            return result;
        }

        HitResult Evade(in DamageInfo hit)
        {
            if (!CanStillEarnPerfect()) return new HitResult { Outcome = HitOutcome.Evaded };
            AwardPerfectDodge(-hit.Direction);
            return new HitResult { Outcome = HitOutcome.PerfectEvade };
        }

        // The enemy side calls this when an enemy strike's active frames begin (AttackActiveStart), whether or not
        // it touches the player. If you started a dodge within PerfectWindow before it, your i-frames are up, and
        // the strike's arc covers where you started, that's a perfect dodge in whatever direction you dashed: the
        // hit "would have landed". Returns true when it awarded one. (PerfectRule = SwingMustReachYou ignores it.)
        public bool NotifyEnemyStrike(Vector3 origin, Vector3 forward, MoveData move, Vector3 attackerFeet)
        {
            if (move == null || Dodge.PerfectRule != PerfectDodgeRule.WouldHaveLanded) return false;
            if (!IsInvulnerable || !CanStillEarnPerfect()) return false;
            // Unknown body height: treat the body as tall, so only a strike from far below misses it.
            float height = bodyHeight > 0f ? bodyHeight : float.MaxValue * 0.5f;
            if (!HitGeometry.InArc(origin, forward, move.Range, move.ArcDegrees, move.VerticalReach,
                    dodgeStartPosition, height, dodgeStartRadius)) return false;
            AwardPerfectDodge(attackerFeet - dodgeStartPosition);
            return true;
        }

        bool CanStillEarnPerfect()
        {
            DodgeProfile dodge = Dodge;
            return state == PlayerState.Dodging && dodge.PerfectDodgeEnabled && !perfectRewardGiven
                && clock - dodgeStartClock <= dodge.PerfectWindow + Epsilon;
        }

        // toAttacker: direction from the player to the attacker (any length; Zero if unknown).
        void AwardPerfectDodge(Vector3 toAttacker)
        {
            DodgeProfile dodge = Dodge;
            perfectRewardGiven = true;
            float gain = dodge.PerfectMomentumGain;
            // Fire's forward pressure: dashing into or through the attack earns a little more.
            Vector3 flat = Directions.Flatten(toAttacker);
            if (!dodgeIsBackstep && flat.LengthSquared() > 1e-8f)
            {
                float cos = Vector3.Dot(dodgeDirection, Vector3.Normalize(flat));
                float angle = (float)Math.Acos(Angles.Clamp(cos, -1f, 1f)) * Directions.Rad2Deg;
                if (angle <= dodge.PerfectTowardMaxAngle) gain += Math.Max(0f, dodge.PerfectTowardBonus);
            }
            MeterOf(actionSet.Element).Gain(gain, MomentumRulesOf(actionSet.Element));
            counterWindowUntil = clock + Math.Max(0f, dodge.CounterWindow);
            RefreshCombo();
            // The string waits at least until the counter window closes: the counter is its next hit.
            if (stringNext >= 0) stringMemoryUntil = Math.Max(stringMemoryUntil, counterWindowUntil);
            Emit(new PlayerEvent
            {
                Type = PlayerEventType.PerfectDodge, TimeScale = dodge.PerfectSlowMoScale, Duration = dodge.PerfectSlowMoDuration,
                Amount = gain, Element = actionSet.Element, InAir = !grounded
            });
        }

        // Hyper armour: from HyperArmorFrom until the active frames end, poise can't break (you trade hits).
        // Earth's on-beat perk armours the move from its start.
        bool HasHyperArmor => state == PlayerState.Attacking && currentMove != null && action.Time < currentMove.ActiveEnd
            && ((currentMove.HyperArmor && action.Time >= currentMove.HyperArmorFrom) || moveOnBeatArmor);

        // hitDirection points attacker -> defender. Unknown direction (Zero) counts as frontal.
        static bool IsFromFront(Vector3 hitDirection, Vector3 forward, float arcDegrees)
        {
            Vector3 toAttacker = -Directions.Flatten(hitDirection);
            if (toAttacker.LengthSquared() < 1e-8f) return true;
            Vector3 f = Directions.SafeNormalize(Directions.Flatten(forward), new Vector3(0f, 0f, 1f));
            float cos = Vector3.Dot(f, Vector3.Normalize(toAttacker));
            float angle = (float)Math.Acos(Angles.Clamp(cos, -1f, 1f)) * Directions.Rad2Deg;
            return angle <= arcDegrees * 0.5f + Epsilon;
        }
    }
}
