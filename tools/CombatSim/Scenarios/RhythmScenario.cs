using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // Build 05 rhythm combos: how players with different timing fare against the beat. Each bot plays light strings for
    // 60 s into a passive sparring partner (the tutorial's: can't die, never attacks, moves or breaks out), so only the string
    // itself is measured. "On-beat" = the share of judged string moves (each move's first follow-up press, downgraded to
    // Mashed by a second press) graded OnBeat; "pause moves" = pause-branch moves per minute (accidental for every bot but
    // the pauser); string DPS = damage dealt per second.
    public static class RhythmScenario
    {
        public sealed class Result
        {
            public int OnBeat, Early, Late, Mashed, Pause, Graded;
            public double Seconds, Damage;
            public int Moves;
            public double DamagePerSecond => Seconds > 0 ? Damage / Seconds : 0;
        }

        public static readonly string[] BotNames = { "rhythm", "sloppy", "masher", "reactpress", "slowtap", "pauser" };

        public static Result Play(Options o, Preset preset, string botName, int seed, double seconds = 60.0)
        {
            var s = new Session(preset, o.Fps, SimLevel.Empty(), camera: true, playerAt: new Vector3(0f, 0f, -1.5f));
            EnemyTuning partner = EnemyTuning.CreateTutorialPartner();
            partner.BreakOut.Enabled = false;
            partner.WalkSpeed = partner.ChaseSpeed = partner.StrafeSpeed = partner.RetreatSpeed = 0f;   // stands its ground
            SimEnemy target = s.World.AddEnemy(partner, new Vector3(0f, 0f, 1f), 180f, seed * 31 + 3);
            target.Brain.Passive = true;
            s.World.LockOn.SnapBehindPlayer();
            Bot bot = Bots.Create(botName);
            bot.Attach(s, seed * 977 + 41);
            var r = new Result();
            // Each string move's verdict: the grade of its first follow-up press (a later Mashed replaces an OnBeat). A move
            // whose follow-up never ran (the bar was empty, the press expired) still counts: it was a press the beat judged.
            BeatGrade verdict = BeatGrade.None;
            void Tally()
            {
                switch (verdict)
                {
                    case BeatGrade.OnBeat:
                    case BeatGrade.Auto: r.OnBeat++; r.Graded++; break;
                    case BeatGrade.Early: r.Early++; r.Graded++; break;
                    case BeatGrade.Late: r.Late++; r.Graded++; break;
                    case BeatGrade.Mashed: r.Mashed++; r.Graded++; break;
                    case BeatGrade.Pause: r.Graded++; break;
                }
                verdict = BeatGrade.None;
            }
            s.World.PlayerEvent += e =>
            {
                if (e.Type == PlayerEventType.BeatJudged)
                {
                    if (e.Grade == BeatGrade.Mashed || verdict == BeatGrade.None) verdict = e.Grade;
                    return;
                }
                if (e.Type != PlayerEventType.AttackStarted) return;
                bool stringMove = e.AttackKind == PlayerAttackKind.Light || e.AttackKind == PlayerAttackKind.Air
                                  || e.AttackKind == PlayerAttackKind.DodgeStrike;
                if (!stringMove) return;
                Tally();
                r.Moves++;
                if (e.Branch == ComboBranch.Pause) r.Pause++;
                if (e.Grade == BeatGrade.Auto) verdict = BeatGrade.None;
            };
            int frames = (int)(seconds * o.Fps);
            for (int f = 0; f < frames && s.Model.IsAlive; f++) s.Step(bot.NextPad());
            Tally();
            r.Seconds = s.World.GameTime;
            r.Damage = s.World.Metrics.DamageDealt;
            return r;
        }

        public static void Run(Options o)
        {
            Out.Heading("Rhythm: the beat vs mashing (" + o.Seeds + " seeds x 60 s into a passive sparring partner)");
            Out.Line("Bots: rhythm (presses on the beat, ± 0.03 s), sloppy (± 0.08 s), masher (every 0.12-0.2 s), reactpress (presses a "
                     + "reaction time after each hit lands, 15% double taps), slowtap (presses after the beat window, inside the combo "
                     + "window), pauser (X X, wait, X X).");
            foreach (Preset p in o.Presets)
            {
                Out.Sub(p + " preset");
                var t = new Table("Bot", "On-beat", "Early", "Late", "Mashed", "Pause moves / min", "String DPS", "String moves / min");
                var dps = new Dictionary<string, double>();
                foreach (string bot in BotNames)
                {
                    var results = new List<Result>();
                    for (int seed = o.Seed; seed < o.Seed + o.Seeds; seed++) results.Add(Play(o, p, bot, seed));
                    double graded = Math.Max(1, results.Sum(r => r.Graded));
                    double minutes = results.Sum(r => r.Seconds) / 60.0;
                    double d = results.Average(r => r.DamagePerSecond);
                    dps[bot] = d;
                    t.Row(bot, Out.Pct(results.Sum(r => r.OnBeat) / graded), Out.Pct(results.Sum(r => r.Early) / graded),
                        Out.Pct(results.Sum(r => r.Late) / graded), Out.Pct(results.Sum(r => r.Mashed) / graded),
                        Out.N(results.Sum(r => r.Pause) / minutes, 2), Out.N(d, 1), Out.N(results.Sum(r => r.Moves) / minutes, 0));
                }
                t.Print();
                Out.Line("String DPS rhythm / masher: **" + Out.N(dps["rhythm"] / Math.Max(1e-6, dps["masher"]), 2) + "** (target >= "
                         + (p == Preset.Fluid ? "1.30" : "1.25") + ")");
            }
        }
    }
}
