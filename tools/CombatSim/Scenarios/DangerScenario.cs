using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // Build 05 danger sense (spec 2.7), in real fights against the sandbox's enemies:
    //   timing      - each strike's warning came WarningLead (x the attack's DangerLeadScale) before it landed, or at its
    //                 wind-up when that was later; landed = the melee hit's first active frame, or the moment a bolt touched
    //                 the player (passed closest, if it missed)
    //   resolution  - every warning ends in DangerCleared (the strike landed, missed or was called off)
    //   sense       - the 'sense' bot (dodges on the white cue) against 'react' (dodges on seeing the wind-up)
    //   thrust      - how often 'sense' dodges far too early for the Dao Soldier's delayed thrust (the cue follows the strike,
    //                 not the wind-up, so it shouldn't)
    public static class DangerScenario
    {
        static readonly string[] Groups = { "soldier", "soldier,soldier", "crossbow", "soldier,soldier,crossbow,platform" };

        sealed class Timing
        {
            public readonly List<double> MeleeErrors = new List<double>();   // frames
            public readonly List<double> BoltErrors = new List<double>();
            public int Strikes, Hidden, Unwarned, Warnings, Cleared, Unresolved;
        }

        // A strike that went off: who, which hit, when it was wound up and when it landed (game seconds).
        struct Impact
        {
            public int Attacker, Hit;
            public double Telegraph, At;
            public float LeadScale;
            public bool Ranged, Hidden;
        }

        sealed class Cue
        {
            public int Attacker, Hit;
            public double At;
            public bool Cleared;
        }

        sealed class BoltTrack
        {
            public SimProjectiles.Projectile Bolt;
            public int Attacker, Hit;
            public double Telegraph;
            public float LeadScale;
            public double ClosestAt;
            public float Closest = float.MaxValue;
            public bool Ended;
        }

        public static void Run(Options o)
        {
            Out.Heading("Danger sense: cue timing, resolution, sense vs react (" + o.Seeds + " seeds, duels in the sandbox arena)");
            TimingAndResolution(o);
            SenseVsReact(o, false);
            SenseVsReact(o, true);
        }

        // Hooks the measuring onto a duel's session (called before the bot attaches).
        static void Measure(Session s, Timing t, List<Action> finish)
        {
            var telegraphs = new Dictionary<int, (double at, EnemyAttackData attack)>();
            var impacts = new List<Impact>();
            var warnings = new List<Cue>();
            var bolts = new List<BoltTrack>();
            var known = new HashSet<SimProjectiles.Projectile>();
            var launched = new List<(int attacker, int hit, double telegraph, float leadScale)>();
            s.World.EnemyEvent += (enemy, e) =>
            {
                double now = s.World.GameTime;
                if (e.Type == EnemyEventType.TelegraphStarted) telegraphs[enemy.Id] = (now, e.Attack);
                if (!telegraphs.TryGetValue(enemy.Id, out var tel) || tel.attack == null) return;
                if (e.Type == EnemyEventType.AttackActiveStart && e.Move != null && !e.Move.LaunchesProjectile)
                {
                    impacts.Add(new Impact
                    {
                        Attacker = enemy.Id, Hit = e.HitIndex, Telegraph = tel.at, At = now, LeadScale = tel.attack.DangerLeadScale,
                        Hidden = tel.attack.HideDangerSense
                    });
                }
                // The bolt itself flies once the enemy has handled the event: picked up after the frame.
                if (e.Type == EnemyEventType.ProjectileLaunched) launched.Add((enemy.Id, e.HitIndex, tel.at, tel.attack.DangerLeadScale));
            };
            s.World.PlayerEvent += e =>
            {
                if (e.Type == PlayerEventType.DangerWarning) warnings.Add(new Cue { Attacker = e.AttackerId, Hit = e.Count, At = s.World.GameTime });
                if (e.Type != PlayerEventType.DangerCleared) return;
                for (int i = warnings.Count - 1; i >= 0; i--)
                {
                    Cue c = warnings[i];
                    if (c.Cleared || c.Attacker != e.AttackerId || c.Hit != e.Count) continue;
                    c.Cleared = true;
                    break;
                }
            };
            s.World.Stepped += () =>
            {
                foreach ((int attacker, int hit, double telegraph, float leadScale) in launched)
                {
                    foreach (SimProjectiles.Projectile b in s.World.Projectiles.Flying)
                    {
                        if (b.Damage.SourceId != attacker || !known.Add(b)) continue;
                        bolts.Add(new BoltTrack { Bolt = b, Attacker = attacker, Hit = hit, Telegraph = telegraph, LeadScale = leadScale });
                    }
                }
                launched.Clear();
                foreach (BoltTrack b in bolts)
                {
                    if (b.Ended) continue;
                    if (!b.Bolt.Flying)
                    {
                        // It stopped this frame: on the player's body, that was the landing.
                        b.Ended = true;
                        if (b.Bolt.EndReason == "hit " + s.Player.Name || b.Bolt.EndReason == "deflected") b.ClosestAt = s.World.GameTime;
                        continue;
                    }
                    float d = Directions.Flatten(b.Bolt.Position - s.Player.Feet).Length();
                    if (d >= b.Closest) continue;
                    b.Closest = d;
                    b.ClosestAt = s.World.GameTime;
                }
            };
            finish.Add(() =>
            {
                DangerSenseSettings rules = s.Model.Tuning.DangerSense;
                double end = s.World.GameTime;
                foreach (BoltTrack b in bolts)
                {
                    if (b.Closest < float.MaxValue && b.ClosestAt < end - 0.2)
                        impacts.Add(new Impact { Attacker = b.Attacker, Hit = b.Hit, Telegraph = b.Telegraph, At = b.ClosestAt, LeadScale = b.LeadScale, Ranged = true });
                }
                foreach (Impact i in impacts)
                {
                    t.Strikes++;
                    if (i.Hidden)
                    {
                        t.Hidden++;
                        continue;
                    }
                    double lead = rules.WarningLead * (i.LeadScale > 0f ? i.LeadScale : 1f);
                    double expected = Math.Max(i.Telegraph, i.At - lead);
                    Cue cue = warnings.LastOrDefault(c => c.Attacker == i.Attacker && c.Hit == i.Hit && c.At >= i.Telegraph - 1e-6 && c.At <= i.At + 1e-6);
                    if (cue == null)
                    {
                        t.Unwarned++;
                        continue;
                    }
                    double error = Math.Abs(cue.At - expected) / s.Dt;
                    (i.Ranged ? t.BoltErrors : t.MeleeErrors).Add(error);
                }
                foreach (Cue c in warnings)
                {
                    if (c.At > end - 1.5) continue;   // still live when the fight stopped
                    t.Warnings++;
                    if (c.Cleared) t.Cleared++;
                    else t.Unresolved++;
                }
            });
        }

        static void TimingAndResolution(Options o)
        {
            Out.Sub("Cue timing and resolution (target: |warning - max(wind-up, impact - lead)| <= 1 frame; 100 % of warnings resolved)");
            var table = new Table("Preset", "Bot", "Enemies", "Strikes", "Melee error P95 / max", "Bolt error P95 / max", "Hidden by design", "Unwarned",
                "Warnings resolved");
            // A player standing still (the bolt's landing is well defined), then two fighting bots against every group.
            var runs = new List<(string bot, string group)> { ("idle", "crossbow"), ("idle", "crossbow,platform") };
            foreach (string bot in new[] { "react", "sense" }) foreach (string g in Groups) runs.Add((bot, g));
            foreach (Preset p in o.Presets)
            {
                foreach ((string bot, string g) in runs)
                {
                    {
                        var t = new Timing();
                        for (int seed = o.Seed; seed < o.Seed + o.Seeds; seed++)
                        {
                            var finish = new List<Action>();
                            DuelsScenario.Play(o, p, bot, g, seed, 60, null, stopWhenEnemiesDead: false, invariants: false, setup: s => Measure(s, t, finish));
                            foreach (Action a in finish) a();
                        }
                        string Errors(List<double> e) => e.Count == 0 ? "-" : Out.N(Stats.Percentile(e, 0.95), 2) + " / " + Out.N(e.Max(), 2) + " f";
                        table.Row(p, bot, g.Replace(",", "+"), t.Strikes, Errors(t.MeleeErrors), Errors(t.BoltErrors), t.Hidden, t.Unwarned,
                            t.Warnings > 0 ? Out.Pct(t.Cleared / (double)t.Warnings) + " of " + t.Warnings : "-");
                    }
                }
            }
            table.Print();
            Out.Line("Errors are in frames (" + Out.N(o.Fps, 0) + " fps). A melee strike lands on its first active frame; a bolt when it touches the player "
                     + "(or, dodged, when it passes closest). A melee warning can't come before the wind-up that announces it: when the wind-up is shorter than the "
                     + "lead, it shows on the next frame (the 1 frame above). A bolt's landing is predicted from where the player stood when it "
                     + "flew: exact for a player standing still ('idle'), early or late by however much a moving player changed course after the "
                     + "launch (the fighting bots dodge and close in all the time). Fights run 60 s (enemies respawn); warnings in the last 1.5 s are left out.");
        }

        // pad: the same fights on the Xbox path (every press through the game's PadChordReader, element picks played as
        // hold RB then the face button), so a dodge on the white cue is measured exactly as a pad player presses it (J5-01).
        static void SenseVsReact(Options o, bool pad)
        {
            Out.Sub("Sense vs react" + (pad ? ", Xbox pad (real PadChordReader path)" : ", keyboard path")
                    + " (target, Fluid: sense perfect-dodge rate >= react + 20 points, damage taken <= react's; panic dodges on the delayed thrust <= 10 %)");
            var table = new Table("Preset", "Enemies", "Bot", "Win", "Perfect dodges / dodges", "Rate", "Perfect dodges / enemy strike", "Damage taken (avg)",
                "Delayed-thrust panic dodges", "Target");
            foreach (Preset p in o.Presets)
            {
                foreach (string g in new[] { "soldier", "soldier,soldier", "soldier,soldier,crossbow,platform" })
                {
                    var rates = new Dictionary<string, double>();
                    var damage = new Dictionary<string, double>();
                    foreach (string bot in new[] { "react", "sense" })
                    {
                        var results = new List<DuelResult>();
                        for (int seed = o.Seed; seed < o.Seed + o.Seeds; seed++)
                        {
                            int padSeed = seed * 389 + 5;
                            results.Add(DuelsScenario.Play(o, p, bot, g, seed, 120.0, null, invariants: false,
                                setup: pad ? s => s.Input.UseRealPad(padSeed) : (Action<Session>)null));
                        }
                        int perfect = results.Sum(r => r.Metrics.PerfectDodges), dodges = results.Sum(r => r.Metrics.Dodges);
                        int strikes = results.Sum(r => r.Metrics.Telegraphs);
                        double rate = perfect / Math.Max(1.0, dodges);
                        rates[bot] = rate;
                        damage[bot] = results.Average(r => r.Metrics.DamageTaken);
                        string panic = "-";
                        if (bot == "sense")
                        {
                            int thrusts = results.Sum(r => ((SenseBot)r.Bot).ThrustDodges), early = results.Sum(r => ((SenseBot)r.Bot).ThrustPanicDodges);
                            panic = thrusts > 0 ? Out.Pct(early / (double)thrusts) + " of " + thrusts : "-";
                        }
                        table.Row(p, g.Replace(",", "+"), bot, Out.Pct(results.Count(r => r.Won) / (double)results.Count), perfect + " / " + dodges, Out.Pct(rate),
                            Out.Pct(perfect / Math.Max(1.0, strikes)), Out.N(damage[bot], 0), panic, "");
                    }
                    double gain = (rates["sense"] - rates["react"]) * 100;
                    // Graded on Fluid (Punishing has no white cue, so the sense bot can't beat react by much there).
                    string target = p == Preset.Fluid ? Out.Target(gain >= 20.0 && damage["sense"] <= damage["react"] + 1e-6) : "-";
                    table.Row("", "", "sense - react", "", "", Out.N(gain, 0) + " points", "",
                        Out.N(damage["sense"] - damage["react"], 0), "", target);
                }
            }
            table.Print();
            Out.Line("'react' dodges a reaction time after it sees the wind-up (it knows each attack's startup); 'sense' dodges a reaction time "
                     + "after the mark turns white (Punishing has no white cue: it waits out the gap it has learned after the mark) and plays "
                     + "on-beat strings.");
        }
    }
}
