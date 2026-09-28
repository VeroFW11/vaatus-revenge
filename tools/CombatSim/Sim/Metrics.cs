using System;
using System.Collections.Generic;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // Counters for one session: what the player did and what happened to them.
    public sealed class Metrics
    {
        public int Frames;
        public double GameSeconds, RealSeconds;
        public float DamageTaken, DamageDealt;
        public int HitsTaken, HitsLanded, Kills, PlayerDeaths;
        public int Dodges, PerfectDodges, Evades, Deflects, Blocks, GuardBreaks, DeflectWhiffs;
        public int PlayerStaggers, EnemyStaggers, EnemyStaggerImmuneHits;
        public int AttacksStarted, FaJins, Counters, Heals, HealsInterrupted, Plunges, SprintAttacks, Skills;
        public double StaminaEmptySeconds, InvulnerableSeconds, HitstopRealSeconds, SlowMoRealSeconds;
        public double MomentumIntegral;
        public float MomentumMax;
        public double FirstKillTime = -1, LastKillTime = -1;
        public int Telegraphs;
        public readonly Dictionary<string, int> PlayerEventCounts = new Dictionary<string, int>();
        public readonly Dictionary<string, int> EnemyAttackCounts = new Dictionary<string, int>();
        public readonly Dictionary<string, int> HitsTakenBy = new Dictionary<string, int>();
        public readonly List<double> KillTimes = new List<double>();

        public double MomentumAverage => GameSeconds > 0 ? MomentumIntegral / GameSeconds : 0;

        public void BeginFrame(SimWorld w, float gameDt, float realDt)
        {
        }

        public void EndFrame(SimWorld w, float gameDt, float realDt)
        {
            Frames++;
            GameSeconds += gameDt;
            RealSeconds += realDt;
            if (w.Player != null && gameDt > 0f)
            {
                PlayerCombatModel m = w.Player.Model;
                if (m.Stamina <= 0f && m.IsAlive) StaminaEmptySeconds += gameDt;
                if (m.IsInvulnerable) InvulnerableSeconds += gameDt;
                MomentumIntegral += m.Momentum * gameDt;
                if (m.Momentum > MomentumMax) MomentumMax = m.Momentum;
            }
            if (w.Time.IsHitstopActive) HitstopRealSeconds += realDt;
            else if (w.Time.IsSlowMotionActive) SlowMoRealSeconds += realDt;
        }

        public void OnPlayerEvent(SimWorld w, in PlayerEvent e)
        {
            string key = e.Type.ToString();
            PlayerEventCounts.TryGetValue(key, out int n);
            PlayerEventCounts[key] = n + 1;
            switch (e.Type)
            {
                case PlayerEventType.DodgeStarted: Dodges++; break;
                case PlayerEventType.PerfectDodge: PerfectDodges++; break;
                case PlayerEventType.Deflected: Deflects++; break;
                case PlayerEventType.Blocked: Blocks++; break;
                case PlayerEventType.GuardBroken: GuardBreaks++; break;
                case PlayerEventType.DeflectWhiffed: DeflectWhiffs++; break;
                case PlayerEventType.Staggered: PlayerStaggers++; break;
                case PlayerEventType.Died: PlayerDeaths++; break;
                case PlayerEventType.HealApplied: Heals++; break;
                case PlayerEventType.HealInterrupted: HealsInterrupted++; break;
                case PlayerEventType.AttackStarted:
                    AttacksStarted++;
                    if (e.ChargeTier == ChargeTier.FaJin) FaJins++;
                    if (e.IsCounter) Counters++;
                    if (e.AttackKind == PlayerAttackKind.Plunge) Plunges++;
                    if (e.AttackKind == PlayerAttackKind.Sprint) SprintAttacks++;
                    if (e.AttackKind == PlayerAttackKind.Skill) Skills++;
                    break;
            }
        }

        public void OnEnemyEvent(SimWorld w, SimEnemy enemy, in EnemyEvent e)
        {
            switch (e.Type)
            {
                case EnemyEventType.TelegraphStarted:
                    Telegraphs++;
                    string name = e.Move != null ? e.Move.DisplayName : "?";
                    EnemyAttackCounts.TryGetValue(name, out int n);
                    EnemyAttackCounts[name] = n + 1;
                    break;
                case EnemyEventType.Staggered: EnemyStaggers++; break;
                case EnemyEventType.Died:
                    if (enemy.Tuning.Archetype != EnemyArchetype.Dummy)
                    {
                        Kills++;
                        KillTimes.Add(w.GameTime);
                        if (FirstKillTime < 0) FirstKillTime = w.GameTime;
                        LastKillTime = w.GameTime;
                    }
                    break;
            }
        }

        public void OnHitResolved(SimWorld w, SimFighter target, in DamageInfo hit, in HitResult result)
        {
            if (target == w.Player)
            {
                if (result.Outcome == HitOutcome.Hit)
                {
                    HitsTaken++;
                    DamageTaken += result.DamageDealt;
                    string by = hit.Kind + (hit.Kind == HitKind.Projectile ? "" : "");
                    SimFighter src = null;
                    for (int i = 0; i < w.Enemies.Count; i++) if (w.Enemies[i].Id == hit.SourceId) src = w.Enemies[i];
                    string key = (src != null ? src.Name : "?") + " " + hit.Kind;
                    HitsTakenBy.TryGetValue(key, out int n);
                    HitsTakenBy[key] = n + 1;
                }
                else if (result.Outcome == HitOutcome.Evaded || result.Outcome == HitOutcome.PerfectEvade) Evades++;
            }
            else if (hit.SourceTeam == Team.Player && result.Outcome == HitOutcome.Hit)
            {
                HitsLanded++;
                DamageDealt += result.DamageDealt;
                if (target is SimEnemy se && se.Brain.IsStaggerImmune && !result.PoiseBroken) EnemyStaggerImmuneHits++;
            }
        }
    }

    // Per-frame rule checks for the fuzz runs: numbers finite and in range, nothing stuck, events balanced,
    // i-frames never permanent, no enemy strike without a telegraph, tokens consistent.
    public sealed class Invariants
    {
        public readonly List<string> Violations = new List<string>();
        public readonly Dictionary<string, int> ViolationCounts = new Dictionary<string, int>();
        readonly HashSet<int> openPlayerWindows = new HashSet<int>();
        readonly Dictionary<SimEnemy, HashSet<int>> openEnemyWindows = new Dictionary<SimEnemy, HashSet<int>>();
        readonly Dictionary<SimEnemy, bool> telegraphed = new Dictionary<SimEnemy, bool>();
        PlayerState lastState;
        double stateSince;
        double invulnerableSince = -1;
        public double LongestInvulnerable;
        public double LongestState;
        public string LongestStateName = "";
        public int DodgesStarted, DodgesEnded;
        public int PlayerActiveOpened, PlayerActiveClosed, EnemyActiveOpened, EnemyActiveClosed;
        public int MaxTokenHolders;
        public double MaxBufferedAge;
        public string MaxBufferedAgeCommand = "";
        public int Checks;

        void Fail(SimWorld w, string rule, string detail)
        {
            ViolationCounts.TryGetValue(rule, out int n);
            ViolationCounts[rule] = n + 1;
            if (Violations.Count < 60) Violations.Add("frame " + w.Frame + " [" + rule + "] " + detail);
        }

        public void OnPlayerEvent(SimWorld w, in PlayerEvent e)
        {
            switch (e.Type)
            {
                case PlayerEventType.DodgeStarted:
                case PlayerEventType.AttackStarted:
                case PlayerEventType.ChargeStarted:
                case PlayerEventType.Staggered:
                case PlayerEventType.HealStarted:
                    stateSince = w.GameTime;   // a new action restarts the "how long in this state" clock
                    break;
            }
            switch (e.Type)
            {
                case PlayerEventType.AttackActiveStart:
                    PlayerActiveOpened++;
                    if (!openPlayerWindows.Add(e.AttackId)) Fail(w, "player-window-reopened", "attack " + e.AttackId);
                    break;
                case PlayerEventType.AttackActiveEnd:
                    PlayerActiveClosed++;
                    if (!openPlayerWindows.Remove(e.AttackId)) Fail(w, "player-window-close-without-open", "attack " + e.AttackId);
                    break;
                case PlayerEventType.DodgeStarted: DodgesStarted++; break;
                case PlayerEventType.DodgeEnded: DodgesEnded++; break;
                case PlayerEventType.Respawned: openPlayerWindows.Clear(); break;
            }
        }

        public void OnEnemyEvent(SimWorld w, SimEnemy enemy, in EnemyEvent e)
        {
            if (!openEnemyWindows.TryGetValue(enemy, out HashSet<int> open))
            {
                open = new HashSet<int>();
                openEnemyWindows[enemy] = open;
            }
            switch (e.Type)
            {
                case EnemyEventType.TelegraphStarted:
                    telegraphed[enemy] = true;
                    if (e.Move != null && Math.Abs(e.Duration - e.Move.Startup) > 1e-4f) Fail(w, "telegraph-duration", enemy.Name);
                    break;
                case EnemyEventType.AttackActiveStart:
                case EnemyEventType.ProjectileLaunched:
                    if (!telegraphed.TryGetValue(enemy, out bool t) || !t) Fail(w, "strike-without-telegraph", enemy.Name + " " + e.Move?.DisplayName);
                    if (e.Type == EnemyEventType.AttackActiveStart)
                    {
                        EnemyActiveOpened++;
                        if (!open.Add(e.AttackId)) Fail(w, "enemy-window-reopened", enemy.Name);
                    }
                    break;
                case EnemyEventType.AttackActiveEnd:
                    EnemyActiveClosed++;
                    if (!open.Remove(e.AttackId)) Fail(w, "enemy-window-close-without-open", enemy.Name);
                    break;
                case EnemyEventType.AttackEnded:
                    telegraphed[enemy] = false;
                    if (open.Count > 0) Fail(w, "enemy-attack-ended-with-open-window", enemy.Name);
                    break;
                case EnemyEventType.Reset:
                    open.Clear();
                    telegraphed[enemy] = false;
                    break;
            }
        }

        public void Check(SimWorld w)
        {
            Checks++;
            SimPlayer p = w.Player;
            if (p != null)
            {
                PlayerCombatModel m = p.Model;
                if (!Finite(p.Feet)) Fail(w, "player-position-nan", p.Feet.ToString());
                if (!Finite(m.Velocity)) Fail(w, "player-velocity-nan", m.Velocity.ToString());
                if (!Finite(m.FacingYaw)) Fail(w, "player-yaw-nan", "");
                if (m.Health < -1e-3f || m.Health > m.MaxHealth + 1e-3f || !Finite(m.Health)) Fail(w, "player-health-range", m.Health.ToString());
                if (m.Stamina < -1e-3f || m.Stamina > m.MaxStamina + 1e-3f || !Finite(m.Stamina)) Fail(w, "player-stamina-range", m.Stamina.ToString());
                if (m.Poise < -1e-3f || m.Poise > m.Tuning.MaxPoise + 1e-3f) Fail(w, "player-poise-range", m.Poise.ToString());
                if (m.Momentum < -1e-3f || m.Momentum > m.MaxMomentum + 1e-3f) Fail(w, "player-momentum-range", m.Momentum.ToString());
                if (m.HealCharges < 0 || m.HealCharges > m.MaxHealCharges) Fail(w, "player-heal-range", m.HealCharges.ToString());
                if (p.Feet.Y < -5f) Fail(w, "player-fell-out", p.Feet.ToString());
                if (m.State != lastState)
                {
                    lastState = m.State;
                    stateSince = w.GameTime;
                }
                double inState = w.GameTime - stateSince;
                double limit = StateLimit(m);
                if (inState > LongestState && m.State != PlayerState.Locomotion && m.State != PlayerState.Dead
                    && m.State != PlayerState.Guarding && m.State != PlayerState.Sprinting && m.State != PlayerState.Airborne)
                {
                    LongestState = inState;
                    LongestStateName = m.State.ToString();
                }
                if (inState > limit) Fail(w, "player-stuck-" + m.State, inState.ToString("0.00") + " s");
                if (m.IsInvulnerable)
                {
                    if (invulnerableSince < 0) invulnerableSince = w.GameTime;
                    LongestInvulnerable = Math.Max(LongestInvulnerable, w.GameTime - invulnerableSince);
                }
                else invulnerableSince = -1;
                if (m.BufferedCommand != PlayerCommand.None)
                {
                    double age = m.BufferedCommandAge;
                    if (age > MaxBufferedAge)
                    {
                        MaxBufferedAge = age;
                        MaxBufferedAgeCommand = m.BufferedCommand + (m.BufferedCommandQueued ? " (queued)" : "") + " in " + m.State;
                    }
                }
                if (!m.IsAttackActive && openPlayerWindows.Count > 0 && m.State != PlayerState.Attacking)
                {
                    // A window can only be open while attacking; closed ones report AttackActiveEnd in the same Tick.
                    Fail(w, "player-window-left-open", "state " + m.State);
                    openPlayerWindows.Clear();
                }
            }
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                SimEnemy e = w.Enemies[i];
                EnemyBrain b = e.Brain;
                if (!Finite(e.Feet)) Fail(w, "enemy-position-nan", e.Name);
                if (b.Health < -1e-3f || b.Health > b.MaxHealth + 1e-3f) Fail(w, "enemy-health-range", e.Name + " " + b.Health);
                if (b.Poise < -1e-3f || b.Poise > e.Tuning.MaxPoise + 1e-3f) Fail(w, "enemy-poise-range", e.Name);
                if (b.HoldsToken && b.State != EnemyState.Attacking && b.State != EnemyState.Approach && b.State != EnemyState.Retreat
                    && b.State != EnemyState.Circle)
                    Fail(w, "token-held-while-" + b.State, e.Name);
                if (!b.IsAlive && b.HoldsToken) Fail(w, "token-held-by-dead", e.Name);
            }
            MaxTokenHolders = Math.Max(MaxTokenHolders, w.Tokens.Count);
            if (w.Tokens.Count > w.Tokens.MaxTokens) Fail(w, "tokens-over-limit", w.Tokens.Count.ToString());
        }

        static double StateLimit(PlayerCombatModel m)
        {
            switch (m.State)
            {
                case PlayerState.Attacking: return 2.0;   // longest move ~1 s
                case PlayerState.Charging: return m.MoveSet.Charge.MaxChargeTime + 0.1;
                case PlayerState.Plunging: return 3.0;    // hang + MaxFallTime + recovery
                case PlayerState.Dodging: return m.MoveSet.Dodge.TotalDuration + 0.1;
                case PlayerState.Healing: return m.Tuning.HealDuration + 0.1;
                case PlayerState.Staggered: return 1.5;   // longest stagger (guard break 1.0 / parried 1.0)
                default: return double.MaxValue;
            }
        }

        static bool Finite(float f)
        {
            return !float.IsNaN(f) && !float.IsInfinity(f);
        }

        static bool Finite(Vector3 v)
        {
            return Finite(v.X) && Finite(v.Y) && Finite(v.Z);
        }
    }
}
