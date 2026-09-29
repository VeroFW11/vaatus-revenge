using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Dodging, guarding, deflecting and taking hits.
    //
    // I-FRAMES: during part of a dodge, hits pass through you ("invincibility frames"). A new dodge's
    //   i-frames can never start sooner than Dodge.ChainIFrameGap after the previous dodge's ended, however
    //   the numbers are tuned, so spamming dodge always leaves you open for a moment.
    // PERFECT DODGE: once per dodge, a strike landing within Dodge.PerfectWindow of the dodge starting, while the
    //   i-frames are up, earns Momentum (more when you dashed toward the attacker), a counter window and a slow-motion
    //   event. With PerfectRule = WouldHaveLanded, a strike that misses because you dashed away still counts if its
    //   arc covered where you started: the enemy side reports every strike with NotifyEnemyStrike.
    // GUARD: blocks hits from the front arc for stamina; not enough stamina = guard break (stagger).
    // DEFLECT: a guard press within Guard.DeflectWindow before a parryable hit lands deflects it for free and
    //   the attacker is told HitOutcome.Parried. A press that deflects nothing locks deflecting out briefly.
    //   A quick tap still counts: the guard stays up for at least the deflect window.
    // INCOMING HIT ORDER: dead/own team/already hit by this attack -> ignored; i-frames -> evade; guarding and
    //   the hit is from the front and blockable -> deflect or block; otherwise a clean hit (health, poise, death).
    public sealed partial class PlayerCombatModel
    {
        Vector3 dodgeDirection;
        float dodgeDistance;
        bool dodgeIsBackstep;
        double dodgeStartClock;
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
        }

        // ---------------------------------------------------------------- dodge

        void TryDodge(in PlayerWorldState world)
        {
            if (!CanDefensiveCancel(true) || !stamina.CanAct) return;
            buffer.Clear();
            StartDodge(world);
        }

        void StartDodge(in PlayerWorldState world)
        {
            DodgeProfile dodge = Dodge;
            ExitAction(true);
            state = PlayerState.Dodging;

            Vector3 stick = Directions.CameraRelative(moveStick, world.CameraYaw);
            float stickLength = stick.Length();
            dodgeIsBackstep = stickLength <= Math.Max(tuning.StickDeadzone, Epsilon);
            dodgeDirection = dodgeIsBackstep ? -Forward : stick / stickLength;
            dodgeDistance = Math.Max(0f, dodgeIsBackstep ? dodge.BackstepDistance : dodge.Distance);
            // Not locked on: face where you dash. Locked on: keep facing the target (a strafe dodge).
            if (!dodgeIsBackstep && !world.HasLockTarget) facingYaw = Directions.YawOf(dodgeDirection, facingYaw);

            SpendStamina(dodge.StaminaCost);
            dodgeStartClock = clock;
            dodgeStartPosition = world.Position;
            dodgeStartRadius = Math.Max(0f, world.SelfRadius);
            iFrameStart = Math.Max(clock + dodge.IFrameStart, lastIFrameEnd + Math.Max(0f, dodge.ChainIFrameGap));
            iFrameEnd = clock + dodge.IFrameEnd;
            perfectRewardGiven = false;
            moveVelocity = Vector3.Zero;
            action.Begin();
            Emit(new PlayerEvent
            {
                Type = PlayerEventType.DodgeStarted, Direction = dodgeDirection, Amount = dodgeDistance, IsBackstep = dodgeIsBackstep
            });
        }

        void UpdateDodge()
        {
            action.MarkChecked();
            if (action.Time >= Dodge.TotalDuration) FinishAction();
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
            // Still wanted? Guard held, or a tap whose deflect window (timed from the real press) is still open.
            if (!guardHeld && clock - pressTime > Guard.DeflectWindow + Epsilon) return;
            EnterGuard(true, pressTime);
        }

        // pressed = a guard press (opens the deflect window, timed from pressClock) rather than a held button.
        void EnterGuard(bool pressed, double pressClock)
        {
            if (state != PlayerState.Guarding)
            {
                ExitAction(true);
                state = PlayerState.Guarding;
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
            if (state == PlayerState.Guarding && !hit.Unblockable && IsFromFront(hit.Direction, forward, guard.ArcDegrees))
            {
                if (hit.Parryable && deflectArmed && clock - deflectPressClock <= guard.DeflectWindow + Epsilon)
                {
                    deflectCaught = true;
                    momentum.Gain(guard.DeflectMomentumGain, MomentumRules);
                    Emit(new PlayerEvent { Type = PlayerEventType.Deflected, Amount = guard.DeflectMomentumGain, Direction = hit.Direction });
                    return new HitResult { Outcome = HitOutcome.Parried };
                }
                float cost = Math.Max(0f, hit.GuardStaminaDamage);
                if (stamina.Current >= cost)
                {
                    SpendStamina(cost);
                    Emit(new PlayerEvent { Type = PlayerEventType.Blocked, Amount = cost, Direction = hit.Direction });
                    return new HitResult { Outcome = HitOutcome.Blocked };
                }
                SpendStamina(cost);
                Emit(new PlayerEvent { Type = PlayerEventType.GuardBroken, Duration = guard.GuardBreakStagger, Direction = hit.Direction });
                Stagger(guard.GuardBreakStagger);
                return new HitResult { Outcome = HitOutcome.GuardBroken, PoiseBroken = true };
            }

            float damage = Math.Max(0f, hit.Damage);
            health -= damage;
            var result = new HitResult { Outcome = HitOutcome.Hit, DamageDealt = damage };
            Emit(new PlayerEvent { Type = PlayerEventType.Damaged, Amount = damage, Direction = hit.Direction });
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
            momentum.Gain(gain, MomentumRules);
            counterWindowUntil = clock + Math.Max(0f, dodge.CounterWindow);
            Emit(new PlayerEvent
            {
                Type = PlayerEventType.PerfectDodge, TimeScale = dodge.PerfectSlowMoScale, Duration = dodge.PerfectSlowMoDuration,
                Amount = gain
            });
        }

        // Hyper armour: from HyperArmorFrom until the active frames end, poise can't break (you trade hits).
        bool HasHyperArmor => state == PlayerState.Attacking && currentMove != null && currentMove.HyperArmor
            && action.Time >= currentMove.HyperArmorFrom && action.Time < currentMove.ActiveEnd;

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
