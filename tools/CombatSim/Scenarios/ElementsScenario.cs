using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // Build 05 element data check (spec 3 and 6.2), for whoever fills in an element's move set:
    //   string dps   - the light string's damage over the time to the end of its finisher at 1.0x (each hit chained at its
    //                  cancel point): from the data, and measured by playing the string into the passive partner with the
    //                  rhythm switched off (target 22-32; Fire is the reference, 28.6)
    //   poise        - the "a single-element string plus a jab never staggers a fresh soldier" budget (MaxPoise 52): the light
    //                  string, the pause path, and the light string with the dodge strike in each slot, each + hit 1
    //   beat data    - every chain / pause / air / dodge-strike move against the spec 2.4.3 rules
    //   moves        - each element's string moves as in the spec 3 tables, for a spot check
    public static class ElementsScenario
    {
        static readonly ElementId[] Elements = { ElementId.Fire, ElementId.Water, ElementId.Earth, ElementId.Air };

        public static void Run(Options o)
        {
            Out.Heading("Elements: string dps, poise budgets, beat data (both presets, all four elements)");
            StringDps(o);
            Poise(o);
            BeatData(o);
            Moves(o);
        }

        // ---------------------------------------------------------------- string dps

        static (float damage, float seconds) FromData(ElementMoveSet set)
        {
            MoveData[] chain = set.LightChain;
            float damage = chain.Sum(m => m.Damage * Math.Max(1, m.HitCount));   // per sub-hit
            float seconds = 0f;
            for (int i = 0; i < chain.Length - 1; i++) seconds += chain[i].ChainCancelAt;
            seconds += chain[chain.Length - 1].TotalDuration;
            return (damage, seconds);
        }

        // The string played by the model: rhythm off (rate 1.0, no beat bonus), each press made at the move's combo window
        // start, so it chains at the cancel point. Time from the first move's start to the finisher's end.
        static (float damage, double seconds) Measured(Options o, Preset p, ElementId element)
        {
            var s = new Session(p, o.Fps, SimLevel.Empty(), camera: true, playerAt: new Vector3(0f, 0f, -1.5f));
            s.Model.Tuning.Rhythm.Enabled = false;
            EnemyTuning t = EnemyTuning.CreateTutorialPartner();
            t.BreakOut.Enabled = false;
            t.WalkSpeed = t.ChaseSpeed = t.StrafeSpeed = t.RetreatSpeed = 0f;
            SimEnemy partner = s.World.AddEnemy(t, new Vector3(0f, 0f, 1f), 180f, 3);
            partner.Brain.Passive = true;
            s.World.LockOn.SnapBehindPlayer();
            if (element != ElementId.Fire) s.Step(new Pad { Element = element });
            s.Idle(2);
            float damage = 0f;
            double start = -1, end = -1, pressAt = 0;
            int moves = s.Model.MoveSet.LightChain.Length;
            s.World.PlayerEvent += e =>
            {
                if (e.Type == PlayerEventType.ComboHit && e.Move != null) damage += e.Move.Damage;
                if (e.Type == PlayerEventType.AttackStarted && e.AttackKind == PlayerAttackKind.Light)
                {
                    if (start < 0) start = s.World.GameTime;
                    if (!e.IsFinisher) pressAt = s.World.GameTime + e.Move.ComboWindowStart;
                }
                if (e.Type == PlayerEventType.AttackEnded && e.AttackKind == PlayerAttackKind.Light && e.IsFinisher && end < 0) end = s.World.GameTime;
            };
            int frames = (int)(5f * o.Fps);
            for (int f = 0; f < frames && end < 0; f++)
            {
                bool press = pressAt >= 0 && s.World.GameTime + 1.0 / o.Fps >= pressAt;
                if (press) pressAt = -1;
                s.Step(new Pad { Light = press });
            }
            return (damage, end > start ? end - start : double.NaN);
        }

        static void StringDps(Options o)
        {
            Out.Sub("Light string dps at 1.0x (target: 22-32; Fire reference 28.6)");
            var t = new Table("Preset", "Element", "Damage", "To the finisher's end", "dps (data)", "dps (played)", "In range");
            foreach (Preset p in o.Presets)
            {
                Session.MakePreset(p, out PlayerTuning _, out ElementLoadout loadout);
                foreach (ElementId el in Elements)
                {
                    (float damage, float seconds) = FromData(loadout.Get(el));
                    (float played, double playedSeconds) = Measured(o, p, el);
                    double dps = damage / seconds, playedDps = played / playedSeconds;
                    t.Row(p, el, Out.N(damage, 0), Out.N(seconds, 2) + " s", Out.N(dps, 1), Out.N(playedDps, 1),
                        dps >= 22 && dps <= 32 ? "yes" : "**no**");
                }
            }
            t.Print();
            Out.Line("\"Played\" counts each hit's base damage (no Momentum, MIX or beat bonus) from the first move's start to the finisher's end, as the "
                     + "model ran it; it should match the data column, a little lower because each hit chains on the first frame at or "
                     + "after its cancel point.");
        }

        // ---------------------------------------------------------------- poise

        static void Poise(Options o)
        {
            float soldier = EnemyTuning.CreateDaoSoldier().MaxPoise;
            Out.Sub("Poise budgets: each + hit 1 must stay below the Dao Soldier's " + Out.N(soldier, 0));
            var t = new Table("Preset", "Element", "Light string + hit 1", "Pause path + hit 1", "Dodge strike substituted (worst slot) + hit 1", "Pass");
            foreach (Preset p in o.Presets)
            {
                Session.MakePreset(p, out PlayerTuning _, out ElementLoadout loadout);
                foreach (ElementId el in Elements)
                {
                    ElementMoveSet set = loadout.Get(el);
                    float hit1 = Poise(set.LightChain[0]);
                    float main = set.LightChain.Sum(Poise) + hit1;
                    float pause = hit1;
                    for (int i = 0; i <= set.Rhythm.PauseAfterIndex && i < set.LightChain.Length; i++) pause += Poise(set.LightChain[i]);
                    pause += (set.PauseChain ?? new MoveData[0]).Sum(Poise);
                    float worst = 0f;
                    for (int slot = 0; slot < set.LightChain.Length - 1; slot++)
                        worst = Math.Max(worst, main - Poise(set.LightChain[slot]) + Poise(set.DodgeStrike));
                    bool pass = main < soldier && pause < soldier && worst < soldier;
                    t.Row(p, el, Out.N(main, 0), Out.N(pause, 0), Out.N(worst, 0), pass ? "yes" : "**no**");
                }
            }
            t.Print();
        }

        // Poise a string move deals: its PoiseDamage, every sub-hit of a multi-hit move.
        static float Poise(MoveData m) => m == null ? 0f : m.PoiseDamage * Math.Max(1, m.HitCount);

        // ---------------------------------------------------------------- beat data

        static void BeatData(Options o)
        {
            Out.Sub("Beat data validation (spec 2.4.3; target: no failures)");
            var t = new Table("Preset", "Element", "Moves checked", "Failures");
            var failures = new List<string>();
            foreach (Preset p in o.Presets)
            {
                Session.MakePreset(p, out PlayerTuning tuning, out ElementLoadout loadout);
                RhythmTuning r = tuning.Rhythm;
                foreach (ElementId el in Elements)
                {
                    ElementMoveSet set = loadout.Get(el);
                    ElementRhythm er = set.Rhythm;
                    float rOn = er.OnBeatPlaybackRate > 0f ? er.OnBeatPlaybackRate : r.OnBeatPlaybackRate;
                    float early = r.BeatEarly + er.BeatEarlyDelta;
                    float late = r.BeatLate + er.BeatLateDelta + tuning.ElementSwitch.SwitchStrikeBeatLateBonus;
                    var moves = new List<(MoveData move, bool chains)>();
                    foreach (MoveData m in set.LightChain) moves.Add((m, true));
                    foreach (MoveData m in set.PauseChain ?? new MoveData[0]) moves.Add((m, true));
                    for (int i = 0; i < set.AirChain.Length; i++) moves.Add((set.AirChain[i], i < set.AirChain.Length - 1));
                    if (set.DodgeStrike != null) moves.Add((set.DodgeStrike, true));
                    int failed = 0;
                    foreach ((MoveData m, bool chains) in moves)
                    {
                        void Check(bool ok, string rule)
                        {
                            if (ok) return;
                            failed++;
                            failures.Add(p + " " + el + " " + m.DisplayName + ": " + rule);
                        }
                        Check(m.ActiveStart / rOn - early >= 0f, "ActiveStart / rOn - BeatEarly >= 0");
                        if (chains) Check(m.ActiveStart + late <= m.ComboWindowEnd + 1e-4f, "ActiveStart + late <= ComboWindowEnd");
                        Check(m.ComboWindowEnd < m.TotalDuration + r.PauseGrace, "ComboWindowEnd < TotalDuration + PauseGrace");
                        Check(m.ChainCancelAt >= m.ActiveEnd - 1e-4f, "ChainCancelAt >= ActiveEnd");
                        Check(m.ComboWindowStart <= m.ChainCancelAt + 1e-4f, "ComboWindowStart <= ChainCancelAt");
                        Check(m.Active >= (Math.Max(1, m.HitCount) - 1) * m.HitInterval - 1e-4f, "Active >= (HitCount - 1) x HitInterval");
                    }
                    t.Row(p, el, moves.Count, failed == 0 ? "0" : "**" + failed + "**");
                }
            }
            t.Print();
            foreach (string f in failures.Take(40)) Out.Line("- " + f);
        }

        // ---------------------------------------------------------------- the move tables

        static void Moves(Options o)
        {
            Out.Sub("String moves (Fluid) for a spot check against spec 3");
            Session.MakePreset(Preset.Fluid, out PlayerTuning _, out ElementLoadout loadout);
            var t = new Table("Element", "Slot", "Name", "S/A/R", "CW", "Ch/Dg", "Dmg", "Po", "Hits", "St");
            foreach (ElementId el in Elements)
            {
                ElementMoveSet set = loadout.Get(el);
                void Row(string slot, MoveData m)
                {
                    if (m == null) return;
                    t.Row(el, slot, m.DisplayName, Out.N(m.Startup, 2) + "/" + Out.N(m.Active, 2) + "/" + Out.N(m.Recovery, 2),
                        Out.N(m.ComboWindowStart, 2) + "-" + Out.N(m.ComboWindowEnd, 2), Out.N(m.ChainCancelAt, 2) + "/" + Out.N(m.DodgeCancelAt, 2),
                        Out.N(m.Damage, 0), Out.N(m.PoiseDamage, 0), m.HitCount > 1 ? m.HitCount + " x " + Out.N(m.HitInterval, 2) : "1",
                        Out.N(m.StaminaCost, 0));
                }
                for (int i = 0; i < set.LightChain.Length; i++) Row("LightChain[" + i + "]", set.LightChain[i]);
                for (int i = 0; i < (set.PauseChain?.Length ?? 0); i++) Row("PauseChain[" + i + "]", set.PauseChain[i]);
                Row("DodgeStrike", set.DodgeStrike);
            }
            t.Print();
        }
    }
}
