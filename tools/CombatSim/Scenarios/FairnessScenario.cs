using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    public static class FairnessScenario
    {
        public static void Run(Options o)
        {
            Out.Heading("Fairness and readability");
            TelegraphTable(o);
            ReactionOdds(o);
            Unavoidable(o);
            TokenAudit(o);
            PlatformCrossbowman(o);
        }

        static IEnumerable<(string enemy, EnemyAttackData attack)> AllAttacks()
        {
            foreach (var t in new[] { EnemyTuning.CreateDaoSoldier(), EnemyTuning.CreateCrossbowman(), EnemyTuning.CreateSparringDummy() })
                foreach (var a in t.Attacks) yield return (t.DisplayName, a);
        }

        static void TelegraphTable(Options o)
        {
            Out.Sub("Every enemy attack: telegraph length vs a human reaction");
            Out.Line("Budget = startup − reaction − 2 frames (a Flame Step's i-frames start 2 frames after the press). Negative = a pure reaction can't make it; "
                     + "the player has to learn the timing (normal for souls-likes, but worth knowing for the 'readable' pillar). "
                     + "A deflect by pure reaction must land in the last 8 frames (0.133 s) before the strike.");
            var t = new Table("Enemy", "Attack", "Glow", "Startup", "Hits", "Budget @0.25 s reaction", "Budget @0.35 s", "Deflectable by reaction?");
            foreach (var (enemy, a) in AllAttacks())
            {
                MoveData m = a.Move;
                double b25 = m.Startup - 0.25 - 2 / 60.0, b35 = m.Startup - 0.35 - 2 / 60.0;
                bool deflectReact = m.Startup - 0.25 >= 0 && m.Startup - 0.25 <= 0.133 + 1e-3;
                string hits = a.HitCount > 1 ? a.HitCount + " × " + Out.N(a.HitInterval, 2) + " s apart" : "1";
                t.Row(enemy, m.DisplayName, a.Telegraph, Out.N(m.Startup, 2) + " s", hits, Out.N(b25, 2) + " s", Out.N(b35, 2) + " s",
                    m.Startup < 0.25 + 0.133 ? (m.Startup >= 0.25 ? "only a fast reaction" : "no") : "no: too early (must anticipate)");
            }
            t.Print();
        }

        static void ReactionOdds(Options o)
        {
            Out.Sub("Reacting to the wind-up (no learned timing): chance the Dao Soldier's attack misses, " + Math.Max(60, o.Seeds * 3) + " trials each");
            Out.Line("The player stands where the soldier starts the attack, sees the telegraph and presses after a reaction time drawn from N(mean, 0.04 s). "
                     + "'Side' = sidestep, 'back' = dash away. Deflect = tap guard on reaction.");
            int trials = Math.Max(60, o.Seeds * 3);
            EnemyTuning st = EnemyTuning.CreateDaoSoldier();
            var t = new Table("Attack", "React 0.25: side", "back", "deflect", "React 0.35: side", "back", "deflect", "Perfect dodges (side, 0.25)");
            for (int ai = 0; ai < st.Attacks.Length; ai++)
            {
                var cells = new List<object> { st.Attacks[ai].Move.DisplayName };
                int perfect = 0;
                foreach (float mean in new[] { 0.25f, 0.35f })
                {
                    foreach (string mode in new[] { "side", "back", "deflect" })
                    {
                        int avoided = 0;
                        for (int k = 0; k < trials; k++)
                        {
                            var r = ReactTrial(o, ai, mean, mode, 1000 + k * 7 + ai);
                            if (r.avoided) avoided++;
                            if (r.perfect && mean == 0.25f && mode == "side") perfect++;
                        }
                        cells.Add(Out.Pct(avoided / (double)trials));
                    }
                }
                cells.Add(Out.Pct(perfect / (double)trials));
                t.Row(cells.ToArray());
            }
            t.Print();
        }

        static (bool avoided, bool perfect) ReactTrial(Options o, int attackIndex, float reactionMean, string mode, int seed)
        {
            var s = new Session(Preset.Fluid, o.Fps, SimLevel.Empty(), camera: false);
            var human = new Human(seed);
            EnemyTuning t = EnemyTuning.CreateDaoSoldier();
            t.Attacks = new[] { t.Attacks[attackIndex] };
            t.Attacks[0].Cooldown = 0f;
            t.AttackIntervalMin = t.AttackIntervalMax = 0.2f;
            float startDistance = t.Attacks[0].MaxRange + 0.4f - 0.05f;
            SimEnemy e = s.World.AddEnemy(t, Vector3.Zero, 0f, seed);
            s.Player.Controller.Position = new Vector3(0f, 0f, startDistance);
            double pressAt = -1;
            bool done = false;
            s.World.EnemyEvent += (en, ev) =>
            {
                if (ev.Type == EnemyEventType.TelegraphStarted && pressAt < 0) pressAt = s.World.RealTime + Math.Max(0.1f, human.Normal(reactionMean, 0.04f));
                if (ev.Type == EnemyEventType.AttackEnded) done = true;
            };
            bool pressed = false;
            for (int f = 0; f < 400 && !done; f++)
            {
                var pad = new Pad();
                if (pressAt >= 0 && !pressed && s.World.RealTime >= pressAt)
                {
                    pressed = true;
                    Vector3 toEnemy = Directions.SafeNormalize(Directions.Flatten(e.Feet - s.Player.Feet), new Vector3(0f, 0f, -1f));
                    if (mode == "deflect") pad.Guard = true;
                    else
                    {
                        pad.Dodge = true;
                        Vector3 d = mode == "back" ? -toEnemy : Directions.RightFromYaw(Directions.YawOf(toEnemy));
                        pad.Move = Session.StickFor(d, 0f);
                    }
                }
                s.Step(pad);
            }
            Metrics m = s.World.Metrics;
            // A deflect trial only counts when the hit was actually deflected; a dodge trial when nothing landed.
            bool avoided = m.HitsTaken == 0 && (mode != "deflect" || m.Deflects > 0);
            return (avoided, m.PerfectDodges > 0);   // the event (a WouldHaveLanded award has no hit outcome)
        }

        static void Unavoidable(Options o)
        {
            Out.Sub("Unavoidable damage: a frame-perfect dodging bot (no attacks) for 60 s, " + o.Seeds + " seeds per group");
            Out.Line("The oracle knows every strike and bolt and dodges 5 frames before a strike (7 before a bolt arrives), so the hit lands inside "
                     + "either preset's i-frames; it dashes sideways and away from the group and never attacks. Any hit it still takes is damage a person "
                     + "couldn't dodge either, at least not by dodging alone (guarding bolts and positioning are other tools). 'platform' = the sandbox's "
                     + "second crossbowman, on the raised block.");
            var t = new Table("Preset", "Group", "Hits taken / min", "Runs with ≥1 hit", "Deaths", "Hits by source", "Max attackers at once");
            foreach (Preset p in o.Presets)
            {
                foreach (string group in new[] { "soldier", "soldier,soldier", "crossbow", "soldier,soldier,crossbow", "soldier,soldier,crossbow,platform" })
                {
                    int hits = 0, runsHit = 0, deaths = 0, maxAttackers = 0;
                    var by = new Dictionary<string, int>();
                    for (int seed = o.Seed; seed < o.Seed + o.Seeds; seed++)
                    {
                        var r = DuelsScenario.Play(o, p, "oracle", group, seed, 60.0, null, stopWhenEnemiesDead: false);
                        hits += r.Metrics.HitsTaken;
                        if (r.Metrics.HitsTaken > 0) runsHit++;
                        deaths += r.Metrics.PlayerDeaths;
                        maxAttackers = Math.Max(maxAttackers, r.MaxSimultaneousAttackers);
                        foreach (var kv in r.Metrics.HitsTakenBy) { by.TryGetValue(kv.Key, out int n); by[kv.Key] = n + kv.Value; }
                    }
                    string src = string.Join(", ", by.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value));
                    t.Row(p, group.Replace(",", " + "), Out.N(hits / (double)o.Seeds, 2), runsHit + "/" + o.Seeds, deaths, src == "" ? "-" : src, maxAttackers);
                }
            }
            t.Print();
        }

        static void TokenAudit(Options o)
        {
            Out.Sub("Attack tokens: released on every way out of an attack?");
            var t = new Table("Case", "Tokens held right after", "One frame later", "OK?");
            foreach (string c in new[] { "killed during wind-up", "staggered (deflect) during strike", "reset (T key) during wind-up", "player dies during wind-up",
                                         "player runs out of aggro range while it approaches", "holding a pending attack, then staggered" })
            {
                var (after, later) = TokenCase(o, c);
                t.Row(c, after, later, later == 0 ? "yes" : "NO");
            }
            t.Print();
        }

        static (int after, int later) TokenCase(Options o, string c)
        {
            var s = new Session(Preset.Fluid, o.Fps, SimLevel.Empty(), camera: false);
            EnemyTuning st = EnemyTuning.CreateDaoSoldier();
            st.AttackIntervalMin = st.AttackIntervalMax = 0.3f;
            SimEnemy e = s.World.AddEnemy(st, Vector3.Zero, 0f, 3);
            s.Player.Controller.Position = new Vector3(0f, 0f, c.StartsWith("player runs") || c.StartsWith("holding") ? 6f : 2.2f);
            int after = -1, later = -1;
            for (int f = 0; f < 600; f++)
            {
                bool trigger = false;
                if (c.StartsWith("holding")) trigger = e.Brain.HoldsToken && e.Brain.State != EnemyState.Attacking;
                else if (c.StartsWith("player runs")) trigger = e.Brain.HoldsToken && e.Brain.State == EnemyState.Approach;
                else trigger = e.Brain.IsTelegraphing || (c.Contains("strike") && e.Brain.IsAttackActive);
                if (c.Contains("strike") && !e.Brain.IsAttackActive) trigger = false;
                if (trigger)
                {
                    switch (c)
                    {
                        case "killed during wind-up":
                            e.ReceiveHit(new DamageInfo { Damage = 1000f, SourceTeam = Team.Player, SourceId = s.Player.Id, AttackId = CombatIds.Next() });
                            break;
                        case "staggered (deflect) during strike":
                            e.Brain.OnParried();
                            break;
                        case "reset (T key) during wind-up":
                            e.ResetEnemy();
                            break;
                        case "player dies during wind-up":
                            s.Player.ReceiveHit(new DamageInfo { Damage = 1000f, SourceTeam = Team.Enemy, SourceId = e.Id, AttackId = CombatIds.Next() });
                            break;
                        case "player runs out of aggro range while it approaches":
                            s.Player.Controller.Position = new Vector3(0f, 0f, 60f);
                            break;
                        case "holding a pending attack, then staggered":
                            e.ReceiveHit(new DamageInfo { Damage = 1f, PoiseDamage = 1000f, SourceTeam = Team.Player, SourceId = s.Player.Id, AttackId = CombatIds.Next() });
                            break;
                    }
                    after = s.World.Tokens.Count;
                    s.Step(new Pad());
                    later = s.World.Tokens.Count;
                    return (after, later);
                }
                s.Step(new Pad());
            }
            return (after, later);
        }

        static void PlatformCrossbowman(Options o)
        {
            Out.Sub("The platform Crossbowman (sandbox spawn Crossbow_Platform, 2.5 m up on a 6 × 6 m block)");
            Out.Line("Where is it after 20 s? The player stands still at different spots (sandbox arena geometry).");
            Out.Line("60cb8ee: LeashRadius 3 m around its first position, plus EnemyFighter's ledge check (mirrored here). 'Ledge stops' = frames the "
                     + "ledge check had to zero its step, i.e. the leash alone would have let it reach the edge.");
            var t = new Table("Player at", "Distance at start", "Crossbowman after 20 s", "Still on the platform?", "Shots fired", "Max drift from spawn", "Ledge stops");
            foreach (var spot in new[] { (new Vector3(0f, 0f, -18f), "player spawn (south)"), (new Vector3(0f, 0f, 0f), "duel ring centre"),
                                         (new Vector3(-13f, 0f, 3f), "5 m south of the platform"), (new Vector3(-2f, 0f, 12f), "11 m east of it"),
                                         (new Vector3(-13f, 0f, 6.5f), "right below its south edge"), (new Vector3(-13f, 2.5f, 13.5f), "up on the platform with it") })
            {
                var s = new Session(Preset.Fluid, o.Fps, SimLevel.SandboxArena(), camera: false, playerAt: spot.Item1);
                SimEnemy cb = s.World.AddEnemy(EnemyTuning.CreateCrossbowman(), new Vector3(-13f, 2.5f, 12f), 135f, 11);
                float startD = Vector3.Distance(cb.Feet, s.Player.Feet);
                int shots = 0;
                s.World.EnemyEvent += (en, ev) => { if (ev.Type == EnemyEventType.ProjectileLaunched) shots++; };
                float drift = 0f;
                bool everOff = false;
                for (int f = 0; f < (int)(20 * o.Fps); f++)
                {
                    s.Step(new Pad { Guard = true });
                    drift = Math.Max(drift, Directions.Flatten(cb.Feet - new Vector3(-13f, 2.5f, 12f)).Length());
                    if (cb.Feet.Y < 2.4f) everOff = true;
                }
                Vector3 p = cb.Feet;
                bool on = p.Y > 2.4f && !everOff;
                t.Row(spot.Item2, Out.N(startD, 1) + " m", "(" + Out.N(p.X, 1) + ", " + Out.N(p.Y, 1) + ", " + Out.N(p.Z, 1) + ")", on ? "yes" : "NO (walked off)", shots,
                    Out.N(drift, 2) + " m", cb.LedgeStops);
            }
            t.Print();
        }
    }
}
