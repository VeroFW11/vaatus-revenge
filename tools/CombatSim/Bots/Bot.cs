using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // An enemy attack the bot has noticed: when its strikes land (game time) and when the human saw it (real time).
    public sealed class Threat
    {
        public SimEnemy Enemy;
        public EnemyAttackData Attack;
        public double TelegraphGameTime;
        public double SeenRealTime;      // telegraph + reaction time: before this the human can't respond
        public bool Handled;
        public int HitsPlanned;

        public double StrikeTime(int hit) => TelegraphGameTime + Attack.Move.Startup + hit * Math.Max(0f, Attack.HitInterval);
        public int HitCount => Math.Max(1, Attack.HitCount);
        public bool IsRanged => Attack.Move.LaunchesProjectile;
    }

    // Base class for bot players. Each frame: NextPad() -> Session.Step(pad). The bot reads the world after the
    // previous frame (one frame of input lag, like a person) and acts with human reaction times and timing noise.
    public abstract class Bot
    {
        protected Session S;
        protected SimWorld W => S.World;
        protected PlayerCombatModel M => S.Model;
        protected SimPlayer P => S.Player;
        protected Human H;
        protected readonly ButtonScheduler Buttons = new ButtonScheduler();
        protected readonly List<Threat> Threats = new List<Threat>();
        protected Vector3 StickWorld;          // desired movement direction (world), magnitude 0..1
        protected Vector3 dodgeDirection;
        readonly List<(double at, Vector3 dir)> dodgePlans = new List<(double, Vector3)>();   // game times
        readonly List<double> guardPlans = new List<double>();                                // game times
        readonly List<double> executedDodges = new List<double>();                            // plan times already pressed
        double guardHoldUntil = -1;            // real time
        double nextAttackAt;                   // real time
        int pressesLeft;
        protected bool UseLockOn = true;
        public string Name { get; protected set; } = "bot";
        public int DodgesPlanned, GuardTapsPlanned;

        public virtual void Attach(Session session, int seed)
        {
            S = session;
            H = new Human(seed);
            W.EnemyEvent += OnEnemyEvent;
        }

        protected double RealNow => W.RealTime;
        protected double GameNow => W.GameTime;

        void OnEnemyEvent(SimEnemy enemy, EnemyEvent e)
        {
            if (e.Type != EnemyEventType.TelegraphStarted || e.Attack == null || e.Move == null) return;
            if (enemy.Tuning.Archetype == EnemyArchetype.Dummy && !enemy.Brain.IsAggro) return;
            Threats.Add(new Threat
            {
                Enemy = enemy, Attack = e.Attack, TelegraphGameTime = W.GameTime,
                SeenRealTime = W.RealTime + H.Reaction()
            });
        }

        public Pad NextPad()
        {
            var pad = new Pad();
            // A wind-up the enemy abandoned (e.g. cut short by its break-out counter) is no longer a threat: a person sees
            // the glow change. Without this the bot dodged strikes that never came.
            Threats.RemoveAll(t => GameNow > t.StrikeTime(t.HitCount - 1) + 1.5 || !t.Enemy.IsAlive || t.Enemy.Brain.State != EnemyState.Attacking
                                   || t.Enemy.Brain.CurrentAttack != t.Attack);
            StickWorld = Vector3.Zero;
            if (M.IsAlive)
            {
                ManageLockOn(ref pad);
                Think(ref pad);
                RunPlans(ref pad);
            }
            Buttons.Apply(ref pad, RealNow);
            pad.Move = Session.StickFor(StickWorld, W.CameraYaw, Math.Min(1f, StickWorld.Length()));
            return pad;
        }

        protected abstract void Think(ref Pad pad);

        // ---------------------------------------------------------------- helpers
        protected IEnumerable<SimEnemy> LivingEnemies => W.Enemies.Where(e => e.IsAlive && e.Tuning.Archetype != EnemyArchetype.Dummy);

        protected SimEnemy Nearest()
        {
            SimEnemy best = null;
            float bd = float.MaxValue;
            foreach (SimEnemy e in LivingEnemies)
            {
                float d = Dist(e);
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        protected float Dist(SimFighter e) => Directions.Flatten(e.Feet - P.Feet).Length();
        protected Vector3 DirTo(SimFighter e) => Directions.SafeNormalize(Directions.Flatten(e.Feet - P.Feet), P.Forward);

        protected SimEnemy Target
        {
            get
            {
                SimFighter t = W.LockOn != null ? W.LockOn.Target : null;
                return t as SimEnemy ?? Nearest();
            }
        }

        // Lock onto the nearest melee threat, else the nearest enemy. Humans switch targets with flicks; the bot
        // just picks (the lock-on selection rules are measured separately in the camera scenario).
        void ManageLockOn(ref Pad pad)
        {
            if (!UseLockOn || W.LockOn == null) return;
            SimEnemy want = Nearest();
            if (want == null) { if (W.LockOn.IsLocked) W.LockOn.ClearLock(); return; }
            SimFighter cur = W.LockOn.Target;
            if (cur == null || !cur.IsAlive || (cur != want && Dist(cur) > Dist(want) + 3f)) W.LockOn.ForceLock(want);
        }

        protected void MoveToward(SimFighter e, float wanted, float strafe = 0f)
        {
            Vector3 to = DirTo(e);
            float d = Dist(e);
            Vector3 side = Directions.RightFromYaw(Directions.YawOf(to)) * strafe;
            if (d > wanted + 0.3f) StickWorld = to + side * 0.3f;
            else if (d < wanted - 0.5f) StickWorld = -to * 0.6f + side;
            else StickWorld = side;
        }

        protected void Tap(Button b, double delay = 0)
        {
            Buttons.Press(b, RealNow + delay, H.PressLength());
        }

        protected bool Busy => dodgePlans.Count > 0 || guardPlans.Count > 0;

        protected void PlanDodge(double gameTime, Vector3 worldDirection)
        {
            // Two plans closer than a dodge can repeat merge into the earlier one; a threat already dodged isn't re-planned.
            for (int i = 0; i < dodgePlans.Count; i++) if (Math.Abs(dodgePlans[i].at - gameTime) < 0.2) return;
            for (int i = 0; i < executedDodges.Count; i++) if (Math.Abs(executedDodges[i] - gameTime) < 0.2) return;
            dodgePlans.Add((gameTime, worldDirection));
            dodgePlans.Sort((a, b) => a.at.CompareTo(b.at));
            DodgesPlanned++;
        }

        protected void PlanGuardTap(double gameTime)
        {
            for (int i = 0; i < guardPlans.Count; i++) if (Math.Abs(guardPlans[i] - gameTime) < 0.1) return;
            guardPlans.Add(gameTime);
            guardPlans.Sort();
            GuardTapsPlanned++;
        }

        protected void HoldGuard(double seconds)
        {
            guardHoldUntil = Math.Max(guardHoldUntil, RealNow + seconds);
        }

        void RunPlans(ref Pad pad)
        {
            bool onRelease = M.Tuning.DodgeTrigger == DodgeTrigger.OnRelease;
            double pressLead = onRelease ? 0.08 + 1.5 / 60.0 : 0.0;   // on-release dodges fire when the button comes up (+ input lag)
            if (dodgePlans.Count > 0 && GameNow >= dodgePlans[0].at - pressLead && !Buttons.IsScheduled(Button.Dodge, RealNow))
            {
                executedDodges.Add(dodgePlans[0].at);
                if (executedDodges.Count > 16) executedDodges.RemoveAt(0);
                Buttons.Press(Button.Dodge, RealNow, onRelease ? 0.08 : H.PressLength());
                dodgeDirection = dodgePlans[0].dir;
                StickWorld = dodgeDirection;
                heldDodgeStick = RealNow + 0.12;
                dodgePlans.RemoveAt(0);
            }
            if (RealNow < heldDodgeStick) StickWorld = dodgeDirection;
            bool guardHeld = RealNow < guardHoldUntil;
            if (guardPlans.Count > 0 && GameNow >= guardPlans[0] - 1.0 / 60.0 * 2)
            {
                if (GameNow >= guardPlans[0])
                {
                    Buttons.Press(Button.Guard, RealNow, 0.05);
                    guardPlans.RemoveAt(0);
                    guardHeld = false;
                }
                else guardHeld = false;   // release briefly so the next press is a fresh one (a deflect needs a press)
            }
            if (guardHeld) pad.Guard = true;
        }

        double heldDodgeStick;

        // Light attack strings: 'count' taps at a human mashing cadence.
        protected void AttackString(int count)
        {
            if (RealNow < nextAttackAt) return;
            if (pressesLeft <= 0) pressesLeft = count;
            Tap(Button.Light);
            pressesLeft--;
            nextAttackAt = RealNow + (pressesLeft > 0 ? H.Range(0.11f, 0.17f) : H.Range(0.35f, 0.7f));
        }

        protected bool InReachOf(Threat t, float margin)
        {
            MoveData m = t.Attack.Move;
            if (t.IsRanged) return true;
            return Dist(t.Enemy) <= t.Attack.MaxRange + 0.4f + m.LungeDistance + margin;
        }

        // A sideways (or back) direction away from an attacker, random side per threat.
        protected Vector3 SideStep(SimEnemy attacker, float backShare = 0f)
        {
            Vector3 to = DirTo(attacker);
            Vector3 side = Directions.RightFromYaw(Directions.YawOf(to)) * (H.Chance(0.5f) ? 1f : -1f);
            return Directions.SafeNormalize(side - to * backShare, side);
        }

        protected bool TargetOpen(SimEnemy e)
        {
            if (e == null) return false;
            EnemyBrain b = e.Brain;
            return b.State == EnemyState.Staggered || b.Phase == AttackPhase.Recovery;
        }

        protected bool ThreatSoon(double withinSeconds, float margin = 0.8f)
        {
            foreach (Threat t in Threats)
            {
                if (RealNow < t.SeenRealTime) continue;
                for (int h = 0; h < t.HitCount; h++)
                {
                    double dt = t.StrikeTime(h) - GameNow;
                    if (dt >= -0.05 && dt <= withinSeconds && InReachOf(t, margin)) return true;
                }
            }
            return false;
        }

        protected void MaybeHeal(float belowShare)
        {
            if (M.HealCharges <= 0 || M.Health > M.MaxHealth * belowShare || M.State != PlayerState.Locomotion) return;
            SimEnemy n = Nearest();
            if (n != null && Dist(n) < 5f && !TargetOpen(n)) return;
            if (ThreatSoon(1.2, 2f)) return;
            Tap(Button.Heal);
        }
    }

    // ---------------------------------------------------------------- the bots

    // Presses light as fast as a person mashing, walks at the nearest enemy, ignores telegraphs.
    public sealed class MasherBot : Bot
    {
        public MasherBot() { Name = "masher"; }
        double nextPress, nextPanic;

        public override void Attach(Session s, int seed)
        {
            base.Attach(s, seed);
            W.PlayerEvent += e =>
            {
                if (e.Type == PlayerEventType.Damaged && H.Chance(0.3f)) nextPanic = RealNow + H.Reaction();
            };
        }

        protected override void Think(ref Pad pad)
        {
            SimEnemy t = Target;
            if (t == null) return;
            MoveToward(t, 1.6f);
            if (RealNow >= nextPress && Dist(t) < 3.5f)
            {
                Tap(Button.Light);
                nextPress = RealNow + H.Range(0.12f, 0.2f);
                if (H.Chance(0.04f)) Tap(Button.Heavy, 0.05);
            }
            if (nextPanic > 0 && RealNow >= nextPanic)
            {
                nextPanic = 0;
                dodgeDirection = SideStep(t, 0.5f);
                PlanDodge(GameNow, dodgeDirection);
            }
            MaybeHeal(0.3f);
        }
    }

    // Reacts to wind-ups: dodges the moment it notices one (reaction time), then counterattacks.
    // style "react": dodge on noticing. "anticipate": the timing of each attack is learned, so the dodge is
    // placed just before the strike (with timing noise) and aims toward/sideways, Fire style.
    public sealed class DodgerBot : Bot
    {
        readonly bool anticipate;
        readonly float lead;
        public DodgerBot(bool anticipate)
        {
            this.anticipate = anticipate;
            Name = anticipate ? "anticipate" : "react";
            lead = 0.06f;
        }

        public override void Attach(Session s, int seed)
        {
            base.Attach(s, seed);
            if (anticipate) H.TimingSd = 0.05f;
        }

        protected override void Think(ref Pad pad)
        {
            SimEnemy t = Target;
            if (t == null) return;
            foreach (Threat th in Threats)
            {
                if (th.Handled || RealNow < th.SeenRealTime) continue;
                if (!InReachOf(th, 1.2f)) continue;
                th.Handled = true;
                if (th.IsRanged)
                {
                    // Bolts: dodge sideways just before they arrive (they fly straight at where you were).
                    for (int h = 0; h < th.HitCount; h++)
                    {
                        double arrive = th.StrikeTime(h) + Dist(th.Enemy) / Math.Max(1f, th.Attack.Move.Projectile.Speed);
                        if (h == 0) PlanDodge(anticipate ? arrive - 0.1 + H.Jitter() : GameNow, SideStep(th.Enemy));
                    }
                    continue;
                }
                if (anticipate)
                {
                    PlanDodge(th.StrikeTime(0) - lead + H.Jitter(), H.Chance(0.6f) ? DirTo(th.Enemy) : SideStep(th.Enemy));
                    if (th.HitCount > 1) PlanDodge(th.StrikeTime(1) - lead + H.Jitter(), SideStep(th.Enemy));
                }
                else
                {
                    PlanDodge(GameNow, SideStep(th.Enemy, H.Chance(0.3f) ? 1f : 0.2f));
                }
            }
            bool danger = ThreatSoon(0.45, 1.0f);
            MoveToward(t, t.Tuning.Archetype == EnemyArchetype.Ranged ? 2.0f : 2.0f, danger ? 0.5f : 0.2f);
            if (!danger && !Busy && Dist(t) < 2.9f && M.Stamina > 12f) AttackString(TargetOpen(t) ? 3 : 2);
            if (t.Tuning.Archetype == EnemyArchetype.Ranged && Dist(t) > 7f && Dist(t) < 16f && M.Stamina > 40f && !danger && H.Chance(0.01f)) Tap(Button.Skill);
            MaybeHeal(0.35f);
        }
    }

    // Holds guard when something is coming and tries to deflect by re-pressing just before the strike.
    public sealed class GuardBot : Bot
    {
        public GuardBot() { Name = "guard"; }

        public override void Attach(Session s, int seed)
        {
            base.Attach(s, seed);
            H.TimingSd = 0.05f;
        }

        protected override void Think(ref Pad pad)
        {
            SimEnemy t = Target;
            if (t == null) return;
            foreach (Threat th in Threats)
            {
                if (RealNow < th.SeenRealTime || !InReachOf(th, 1.5f)) continue;
                double last = th.StrikeTime(th.HitCount - 1);
                if (!th.IsRanged) HoldGuard(Math.Max(0.1, last - GameNow + 0.25));
                else HoldGuard(Math.Max(0.1, last - GameNow + Dist(th.Enemy) / 32.0 + 0.2));
                if (th.Handled) continue;
                th.Handled = true;
                if (!th.IsRanged)
                    for (int h = 0; h < th.HitCount; h++) PlanGuardTap(th.StrikeTime(h) - 0.06 + H.Jitter());
            }
            bool danger = ThreatSoon(0.5, 1.2f);
            MoveToward(t, 2.1f, 0.2f);
            if (!danger && !Busy && Dist(t) < 2.9f && M.Stamina > 20f) AttackString(TargetOpen(t) ? 3 : 1);
            MaybeHeal(0.35f);
        }
    }

    // Northern Shaolin pressure: stays close, keeps the chain going, sprint-kicks in from range, dodges into
    // attacks (anticipated), fa jin on staggered targets, never backs off.
    public sealed class AggressiveBot : Bot
    {
        public AggressiveBot() { Name = "aggressive"; }
        bool charging;

        public override void Attach(Session s, int seed)
        {
            base.Attach(s, seed);
            H.TimingSd = 0.06f;
        }

        protected override void Think(ref Pad pad)
        {
            SimEnemy t = Target;
            if (t == null) return;
            foreach (Threat th in Threats)
            {
                if (th.Handled || RealNow < th.SeenRealTime || !InReachOf(th, 1.0f)) continue;
                th.Handled = true;
                if (th.IsRanged) { PlanDodge(th.StrikeTime(0) + Dist(th.Enemy) / 32.0 - 0.1 + H.Jitter(), SideStep(th.Enemy)); continue; }
                PlanDodge(th.StrikeTime(0) - 0.06 + H.Jitter(), H.Chance(0.7f) ? DirTo(th.Enemy) : SideStep(th.Enemy));
                if (th.HitCount > 1) PlanDodge(th.StrikeTime(1) - 0.06 + H.Jitter(), SideStep(th.Enemy));
            }
            float d = Dist(t);
            bool danger = ThreatSoon(0.4, 0.8f);
            if (charging)
            {
                pad.Heavy = M.State == PlayerState.Charging ? M.ChargeTime < 0.8f + faJinError : M.State != PlayerState.Attacking;
                if (M.State == PlayerState.Attacking || (M.State != PlayerState.Charging && !pad.Heavy)) charging = false;
                StickWorld = DirTo(t) * 0.2f;
                return;
            }
            if (d > 6f && M.Stamina > 20f)
            {
                StickWorld = DirTo(t);
                pad.Dodge = true;      // sprint (in Fluid the press also dashes)
                if (M.SprintTime > 0.35f && d < 8f) Tap(Button.Light);
                return;
            }
            MoveToward(t, 1.5f, 0f);
            if (t.Brain.State == EnemyState.Staggered && t.Brain.StaggerRemaining > 0.9f && d < 3.0f && M.Stamina > 25f && !danger && !Busy)
            {
                charging = true;
                faJinError = H.Jitter();
                pad.Heavy = true;
                return;
            }
            if (!danger && !Busy && d < 2.9f && M.Stamina > 5f) AttackString(3);
            MaybeHeal(0.25f);
        }

        float faJinError;
    }

    // Waits for openings and punishes with a timed fa jin (hold 0.8 s, human timing noise).
    public sealed class FaJinBot : Bot
    {
        public FaJinBot() { Name = "fajin"; }
        bool charging;
        float error;

        public override void Attach(Session s, int seed)
        {
            base.Attach(s, seed);
            H.TimingSd = 0.05f;
        }

        protected override void Think(ref Pad pad)
        {
            SimEnemy t = Target;
            if (t == null) return;
            foreach (Threat th in Threats)
            {
                if (th.Handled || RealNow < th.SeenRealTime || !InReachOf(th, 1.2f)) continue;
                th.Handled = true;
                if (th.IsRanged) { PlanDodge(th.StrikeTime(0) + Dist(th.Enemy) / 32.0 - 0.1 + H.Jitter(), SideStep(th.Enemy)); continue; }
                PlanDodge(th.StrikeTime(0) - 0.06 + H.Jitter(), SideStep(th.Enemy));
                if (th.HitCount > 1) PlanDodge(th.StrikeTime(1) - 0.06 + H.Jitter(), SideStep(th.Enemy));
            }
            bool danger = ThreatSoon(1.0, 0.8f);
            if (charging)
            {
                bool release = M.State == PlayerState.Charging && M.ChargeTime >= 0.8f + error;
                pad.Heavy = !release && (M.State == PlayerState.Charging || M.State == PlayerState.Locomotion);
                if (danger && M.State == PlayerState.Charging) pad.Heavy = false;
                if (M.State == PlayerState.Attacking) charging = false;
                StickWorld = DirTo(t) * 0.2f;
                return;
            }
            MoveToward(t, 2.2f, 0.3f);
            if (!danger && !Busy && Dist(t) < 3.2f && M.Stamina > 25f && (TargetOpen(t) || t.Brain.State == EnemyState.Circle))
            {
                charging = true;
                error = H.Jitter();
                pad.Heavy = true;
            }
            MaybeHeal(0.35f);
        }
    }

    // Does nothing: how quickly do enemies kill a player who never defends?
    public sealed class IdleBot : Bot
    {
        public IdleBot() { Name = "idle"; UseLockOn = false; }
        protected override void Think(ref Pad pad) { }
    }

    // Frame-perfect defence only (no attacks): it plans a dodge 5 frames before every strike that would reach it
    // and 7 frames before every bolt touches its capsule (on-release presses are started early enough), so the hit
    // lands inside either preset's i-frames; it dodges sideways and away from the group and keeps ~3.5 m from the
    // nearest enemy. Hits it still takes are unavoidable by dodging alone (guarding bolts or positioning may help).
    public sealed class OracleBot : Bot
    {
        public OracleBot() { Name = "oracle"; UseLockOn = false; }
        const double Lead = 5.0 / 60.0;       // Punishing i-frames start 3 frames into the dash
        const double BoltLead = 7.0 / 60.0;   // bolts: a little earlier (arrival estimates carry ~1 frame of error)
        double plannedFor = -1;

        protected override void Think(ref Pad pad)
        {
            double soonest = double.MaxValue;
            SimEnemy by = null;
            Vector3 boltDir = new Vector3(1f, 0f, 0f);
            foreach (Threat th in Threats)
            {
                if (!th.Enemy.IsAlive || th.Enemy.Brain.State != EnemyState.Attacking) continue;
                MoveData m = th.Attack.Move;
                if (th.IsRanged)
                {
                    // Bolts: launch time is known from the telegraph; arrival = launch + flight to the capsule.
                    float speed = Math.Max(1f, m.Projectile.Speed);
                    for (int h = 0; h < th.HitCount; h++)
                    {
                        // The bolt leaves OriginForward in front of the archer and already moves on its launch frame.
                        double t = th.StrikeTime(h) - 1.0 / 60.0
                                   + Math.Max(0f, Vector3.Distance(th.Enemy.Feet, P.Feet) - m.OriginForward - P.Radius - m.Projectile.Radius) / speed
                                   - (BoltLead - Lead);
                        if (t > GameNow - 1e-4 && t < soonest) { soonest = t; by = null; boltDir = Directions.SafeNormalize(Directions.Flatten(P.Feet - th.Enemy.Feet), boltDir); }
                    }
                    continue;
                }
                if (Dist(th.Enemy) > m.Range + 0.4f + m.LungeDistance + 1.0f) continue;
                for (int h = 0; h < th.HitCount; h++)
                {
                    double t = th.StrikeTime(h);
                    if (t > GameNow - 1e-4 && t < soonest) { soonest = t; by = th.Enemy; }
                }
            }
            foreach (var pr in W.Projectiles.Flying)
            {
                if (pr.IsFire) continue;
                float speed = pr.Velocity.Length();
                if (speed < 1f) continue;
                Vector3 v = pr.Velocity / speed;
                Vector3 toMe = P.AimPoint - pr.Position;
                float along = Vector3.Dot(toMe, v);
                if (along < 0f) continue;
                float miss = (toMe - v * along).Length();
                if (miss > P.Radius + pr.Radius + 0.35f) continue;
                double t = GameNow + Math.Max(0f, along - P.Radius - pr.Radius) / speed - (BoltLead - Lead);
                if (t < soonest) { soonest = t; by = null; boltDir = v; }
            }
            if (soonest < double.MaxValue && soonest - GameNow < 0.3 && Math.Abs(soonest - plannedFor) > 1e-3)
            {
                Vector3 away = Vector3.Zero;
                foreach (SimEnemy e in LivingEnemies) away -= DirTo(e) / Math.Max(0.5f, Dist(e));
                Vector3 dir = by != null ? SideStep(by, 0.3f) : Directions.RightFromYaw(Directions.YawOf(boltDir)) * (H.Chance(0.5f) ? 1f : -1f);
                if (away.LengthSquared() > 1e-3f) dir = Directions.SafeNormalize(dir + Vector3.Normalize(away) * 0.5f, dir);
                PlanDodge(soonest - Lead, dir);
                plannedFor = soonest;
            }
            SimEnemy n = Nearest();
            if (n != null) MoveToward(n, 3.5f, 0.6f);
        }
    }

    public static class Bots
    {
        public static Bot Create(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "masher": return new MasherBot();
                case "react": return new DodgerBot(false);
                case "anticipate": return new DodgerBot(true);
                case "guard": return new GuardBot();
                case "aggressive": return new AggressiveBot();
                case "fajin": return new FaJinBot();
                case "oracle": return new OracleBot();
                case "idle": return new IdleBot();
                default: throw new ArgumentException("unknown bot " + name);
            }
        }

        public static readonly string[] Players = { "masher", "react", "anticipate", "guard", "aggressive", "fajin" };
    }
}
