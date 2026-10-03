using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    public sealed class DuelResult
    {
        public bool Won, Died;
        public double Seconds;
        public Metrics Metrics;
        public int MaxSimultaneousAttackers;
        public Invariants Invariants;
        public Bot Bot;
    }

    // Bots fight the sandbox's enemies in the arena (duel ring layout): 1 soldier, 2 soldiers, a crossbowman,
    // or the mixed group, many seeds, both presets.
    public static class DuelsScenario
    {
        public static DuelResult Play(Options o, Preset preset, string botName, string enemies, int seed, double seconds, Recorder rec,
            bool stopWhenEnemiesDead = true, bool invariants = true, Action<Session> setup = null)
        {
            var s = new Session(preset, o.Fps, SimLevel.SandboxArena(), camera: true, playerAt: new Vector3(0f, 0f, -5f), playerYaw: 0f);
            s.World.Recorder = rec;
            var inv = invariants ? new Invariants() : null;
            s.World.Invariants = inv;
            string[] list = enemies.Split(',', StringSplitOptions.RemoveEmptyEntries);
            int soldiers = 0;
            foreach (string raw in list)
            {
                string e = raw.Trim().ToLowerInvariant();
                if (e.StartsWith("sold"))
                {
                    float x = soldiers == 0 ? -2.5f : 2.5f;
                    if (list.Count(l => l.Trim().StartsWith("sold")) == 1) x = 0f;
                    s.World.AddEnemy(EnemyTuning.CreateDaoSoldier(), new Vector3(x, 0f, 2f), 180f, seed * 31 + soldiers * 7 + 1);
                    soldiers++;
                }
                else if (e.StartsWith("cross")) s.World.AddEnemy(EnemyTuning.CreateCrossbowman(), new Vector3(8f, 0f, 9f), -140f, seed * 31 + 17);
                // The sandbox's second crossbowman (spawn Crossbow_Platform): on top of the 2.5 m block, facing the ring.
                else if (e.StartsWith("plat")) s.World.AddEnemy(EnemyTuning.CreateCrossbowman(), new Vector3(-13f, 2.5f, 12f), 128f, seed * 31 + 23);
                else if (e.StartsWith("dummy")) s.World.AddEnemy(EnemyTuning.CreateSparringDummy(), new Vector3(0f, 0f, -1f), 180f, seed, true);
                else throw new ArgumentException("unknown enemy '" + raw + "' (use soldier, crossbow, platform, dummy)");
            }
            s.World.LockOn.SnapBehindPlayer();
            setup?.Invoke(s);
            Bot bot = Bots.Create(botName);
            bot.Attach(s, seed * 977 + 13);
            int maxAtt = 0;
            int frames = (int)(seconds * o.Fps);
            var result = new DuelResult { Bot = bot, Invariants = inv };
            for (int f = 0; f < frames; f++)
            {
                s.Step(bot.NextPad());
                int attacking = s.World.Enemies.Count(e => e.Brain.State == EnemyState.Attacking);
                if (attacking > maxAtt) maxAtt = attacking;
                if (!s.Model.IsAlive)
                {
                    // The killing hit lands during the enemies' update, after the player's, so the model queues its
                    // Damaged and Died events for its next Tick (Unity ticks a dead player every frame and gets them
                    // then). Step one idle frame so they reach the recorder and metrics; the time stays the death time.
                    result.Died = true;
                    double deathTime = s.World.GameTime;
                    s.Step(default(Pad));
                    result.Seconds = deathTime;
                    result.Metrics = s.World.Metrics;
                    result.MaxSimultaneousAttackers = maxAtt;
                    return result;
                }
                if (stopWhenEnemiesDead && s.World.AllEnemiesDead) { result.Won = true; break; }
            }
            result.Seconds = s.World.GameTime;
            result.Metrics = s.World.Metrics;
            result.MaxSimultaneousAttackers = maxAtt;
            return result;
        }

        public static void Run(Options o)
        {
            Out.Heading("Duels: bots vs the sandbox enemies (" + o.Seeds + " seeds each, 120 s limit)");
            Out.Line("Bots: masher (mashes light, never defends), react (dodges on seeing a wind-up, ~0.25 s reaction), anticipate (knows each attack's timing, "
                     + "dodges ~0.06 s before the strike ± 0.05 s, dashes into attacks), guard (holds guard, re-presses to deflect ± 0.05 s), aggressive "
                     + "(stays close, chains, sprint-kicks in, fa jin on staggers), fajin (waits for openings, fa jin with ± 0.05 s timing), "
                     + "rhythm (Build 05: on-beat strings ± 0.03 s, defends like anticipate), switcher (on-beat strings that mix all four "
                     + "elements), sense (on-beat strings, dodges on the danger sense's white cue). All use lock-on and move relative to the real camera.");
            // The last group is the whole sandbox ring: both soldiers and both crossbowmen (one on the platform).
            string[] groups = { "soldier", "soldier,soldier", "crossbow", "soldier,soldier,crossbow", "soldier,soldier,crossbow,platform" };
            if (!string.IsNullOrEmpty(o.Groups)) groups = o.Groups.Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (Preset p in o.Presets)
            {
                Out.Sub(p + " preset");
                var t = new Table("Bot", "Enemies", "Win", "Time to win (median)", "Damage taken (avg)", "Deaths", "Stamina empty (s/min)",
                    "Perfect dodges / dodges", "Deflects", "Momentum avg", "Heals");
                string[] bots = string.IsNullOrEmpty(o.Bots) ? Bots.Players : o.Bots.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (string bot in bots)
                {
                    foreach (string g in groups)
                    {
                        var results = new List<DuelResult>();
                        for (int seed = o.Seed; seed < o.Seed + o.Seeds; seed++) results.Add(Play(o, p, bot, g, seed, 120.0, null));
                        var wins = results.Where(r => r.Won).ToList();
                        double winTime = wins.Count > 0 ? Stats.Percentile(wins.Select(r => r.Seconds), 0.5) : double.NaN;
                        double dmg = results.Average(r => r.Metrics.DamageTaken);
                        double emptyPerMin = results.Average(r => r.Metrics.StaminaEmptySeconds / Math.Max(1e-3, r.Seconds) * 60);
                        int perfect = results.Sum(r => r.Metrics.PerfectDodges), dodges = results.Sum(r => r.Metrics.Dodges);
                        t.Row(bot, g.Replace(",", "+"), Out.Pct(wins.Count / (double)results.Count), double.IsNaN(winTime) ? "-" : Out.N(winTime, 1) + " s",
                            Out.N(dmg, 0), results.Count(r => r.Died), Out.N(emptyPerMin, 1), perfect + " / " + dodges,
                            results.Sum(r => r.Metrics.Deflects), Out.N(results.Average(r => r.Metrics.MomentumAverage), 0),
                            results.Sum(r => r.Metrics.Heals));
                        var viol = results.Where(r => r.Invariants != null).SelectMany(r => r.Invariants.ViolationCounts).GroupBy(kv => kv.Key).ToList();
                        foreach (var v in viol) Invariant.Report(p + " " + bot + " vs " + g, v.Key, v.Sum(kv => kv.Value));
                    }
                }
                t.Print();
            }
            PadParity(o);
            Invariant.Flush();
        }

        // The Xbox path (J5-01): the defending bots again with every press through the game's PadChordReader (element picks
        // played as RB and the face together). Since round 5 a face press reaches the rules on its own frame, so the pad
        // must fight as well as the keyboard: perfect-dodge rate within 3 points, damage within 10 %.
        static void PadParity(Options o)
        {
            int seeds = Math.Min(o.Seeds, 12);
            Out.Sub("Xbox pad vs keyboard (" + seeds + " seeds; target: pad perfect-dodge rate >= keyboard - 3 points, damage taken <= keyboard x 1.1 + 5)");
            var t = new Table("Preset", "Bot", "Enemies", "Win keys / pad", "Perfect dodges keys / pad", "Damage keys / pad", "Picks on the pad", "Target");
            foreach (Preset p in o.Presets)
            {
                foreach (string bot in new[] { "react", "anticipate", "rhythm", "sense", "switcher" })
                {
                    foreach (string g in new[] { "soldier", "soldier,soldier" })
                    {
                        var keys = new List<DuelResult>();
                        var pad = new List<DuelResult>();
                        int picks = 0;
                        for (int seed = o.Seed; seed < o.Seed + seeds; seed++)
                        {
                            keys.Add(Play(o, p, bot, g, seed, 120.0, null, invariants: false));
                            int padSeed = seed * 389 + 5;
                            PadInput input = null;
                            pad.Add(Play(o, p, bot, g, seed, 120.0, null, invariants: false, setup: s =>
                            {
                                s.Input.UseRealPad(padSeed);
                                // A practised chord: RB already down as the face lands on the bot's beat. (The bots decide on
                                // the beat itself and can't pre-hold RB, so a spread here would only make their picks late;
                                // the switch scenario stress-tests a 0-80 ms RB-first spread.)
                                s.Input.MaxChordSkew = 0f;
                                input = s.Input;
                            }));
                            picks += input != null ? input.ChordsPlayed : 0;
                        }
                        double keyRate = Rate(keys), padRate = Rate(pad);
                        double keyDmg = keys.Average(r => r.Metrics.DamageTaken), padDmg = pad.Average(r => r.Metrics.DamageTaken);
                        bool ok = padRate >= keyRate - 0.03 && padDmg <= keyDmg * 1.1 + 5.0;
                        t.Row(p, bot, g.Replace(",", "+"), Out.Pct(keys.Count(r => r.Won) / (double)keys.Count) + " / " + Out.Pct(pad.Count(r => r.Won) / (double)pad.Count),
                            Out.Pct(keyRate) + " / " + Out.Pct(padRate), Out.N(keyDmg, 0) + " / " + Out.N(padDmg, 0), picks, Out.Target(ok));
                    }
                }
            }
            t.Print();
        }

        static double Rate(List<DuelResult> results)
        {
            return results.Sum(r => r.Metrics.PerfectDodges) / Math.Max(1.0, results.Sum(r => r.Metrics.Dodges));
        }

        public static void RunOne(Options o)
        {
            Preset p = o.Presets[0];
            Recorder rec = null;
            if (!string.IsNullOrEmpty(o.Record))
            {
                rec = new Recorder("duel " + o.Bot + " vs " + o.Enemies);
                rec.Meta("preset", p.ToString());
                rec.Meta("seed", o.Seed.ToString());
                rec.Meta("bot", o.Bot);
                rec.Meta("enemies", o.Enemies);
                rec.Meta("level", "sandbox arena (ArenaBuilder mirror); player spawned at (0,0,-5) facing +Z");
            }
            DuelResult r = Play(o, p, o.Bot, o.Enemies, o.Seed, o.Seconds, rec);
            Out.Heading("Duel: " + o.Bot + " vs " + o.Enemies + " (" + p + ", seed " + o.Seed + ")");
            Metrics m = r.Metrics;
            var t = new Table("Result", "Value");
            t.Row("Outcome", r.Won ? "won" : r.Died ? "died" : "time up");
            t.Row("Time", Out.N(r.Seconds, 1) + " s");
            t.Row("Damage taken / dealt", Out.N(m.DamageTaken, 0) + " / " + Out.N(m.DamageDealt, 0));
            t.Row("Hits taken / landed", m.HitsTaken + " / " + m.HitsLanded);
            t.Row("Dodges (perfect)", m.Dodges + " (" + m.PerfectDodges + ")");
            t.Row("Deflects / blocks / guard breaks", m.Deflects + " / " + m.Blocks + " / " + m.GuardBreaks);
            t.Row("Enemy staggers", m.EnemyStaggers);
            t.Row("Momentum avg / max", Out.N(m.MomentumAverage, 0) + " / " + Out.N(m.MomentumMax, 0));
            t.Row("Stamina empty", Out.N(m.StaminaEmptySeconds, 1) + " s");
            t.Row("Fa jins / counters / plunges / sprint attacks", m.FaJins + " / " + m.Counters + " / " + m.Plunges + " / " + m.SprintAttacks);
            t.Row("Enemy attacks", string.Join(", ", m.EnemyAttackCounts.Select(kv => kv.Key + " " + kv.Value)));
            t.Row("Hits taken by", string.Join(", ", m.HitsTakenBy.Select(kv => kv.Key + " " + kv.Value)));
            t.Row("Invariant violations", r.Invariants == null || r.Invariants.ViolationCounts.Count == 0 ? "none" : string.Join(", ", r.Invariants.ViolationCounts.Select(kv => kv.Key + " " + kv.Value)));
            t.Print();
            if (rec != null)
            {
                rec.Meta("outcome", r.Won ? "won" : r.Died ? "died" : "time up");
                rec.Save(o.Record, 1f / o.Fps);
                Out.Line("Replay written to " + o.Record);
            }
        }
    }

    // Collects invariant violations seen in duels, printed once at the end.
    public static class Invariant
    {
        static readonly List<string> lines = new List<string>();

        public static void Report(string where, string rule, int count)
        {
            lines.Add("- " + where + ": `" + rule + "` × " + count);
        }

        public static void Flush()
        {
            Out.Sub("Invariant violations during the duels");
            if (lines.Count == 0) Out.Line("None.");
            foreach (string l in lines.Take(40)) Out.Line(l);
            lines.Clear();
        }
    }
}
