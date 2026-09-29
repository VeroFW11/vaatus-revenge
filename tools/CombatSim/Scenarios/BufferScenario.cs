using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // Input buffering: when does a press made while busy actually fire, and can a stale press fire late?
    public static class BufferScenario
    {
        public static void Run(Options o)
        {
            Out.Heading("Input buffer");
            foreach (Preset p in o.Presets)
            {
                Out.Sub(p + ": a second Light press at frame k after a Jab pressed at frame 0");
                var t = new Table("Second press at", "Next move", "Starts at frame", "Delay after press");
                foreach (int k in new[] { 2, 5, 8, 9, 12, 15, 20, 24, 25, 28, 31, 32, 34 })
                {
                    Trace tr = Trace.Run(p, f => new Pad { Light = f == 0 || f == k }, 90, null, o.Fps);
                    var starts = tr.Events.Where(e => e.e.Type == PlayerEventType.AttackStarted).ToList();
                    if (starts.Count < 2) { t.Row(k + " f (" + Out.N(k / o.Fps, 3) + " s)", "none (press dropped)", "-", "-"); continue; }
                    var second = starts[1];
                    t.Row(k + " f (" + Out.N(k / o.Fps, 3) + " s)", second.e.Move.DisplayName, second.frame, (second.frame - k) + " f (" + Out.N((second.frame - k) / o.Fps, 3) + " s)");
                }
                t.Print();
            }

            Out.Sub("Two presses during one Jab (Fluid): which one wins?");
            var w = new Table("Presses", "Result");
            w.Row("Light f0, Dodge f5, Light f8", Describe(Preset.Fluid, f => new Pad { Light = f == 0 || f == 8, Dodge = f == 5 }));
            w.Row("Light f0, Light f5, Dodge f8", Describe(Preset.Fluid, f => new Pad { Light = f == 0 || f == 5, Dodge = f == 8 }));
            w.Row("Light f0, Dodge f10, Heavy f12 (released f13)", Describe(Preset.Fluid, f => new Pad { Light = f == 0, Dodge = f == 10, Heavy = f == 12 }));
            w.Row("Light f0, Guard f6 (tap), Light f7", Describe(Preset.Fluid, f => new Pad { Light = f == 0 || f == 7, Guard = f == 6 }));
            w.Row("Heal f0, Dodge f30, Dodge f50", Describe(Preset.Fluid, f => new Pad { Heal = f == 0, Dodge = f == 30 || f == 50 }));
            w.Row("Heal f0, Light f40 (0.33 s before the heal ends)", Describe(Preset.Fluid, f => new Pad { Heal = f == 0, Light = f == 40 }));
            w.Row("Heal f0, Light f48 (0.22 s before the heal ends)", Describe(Preset.Fluid, f => new Pad { Heal = f == 0, Light = f == 48 }));
            w.Row("Heavy tap f0, Light f30, Light f55", Describe(Preset.Fluid, f => new Pad { Heavy = f == 0, Light = f == 30 || f == 55 }));
            w.Print();

            Out.Sub("Heavy pressed during a Jab and held for 0.8 s from the press (the fa jin timing a player learns)");
            var h = new Table("Preset", "Heavy pressed at", "Charge started at", "Held from press", "Charge time at release", "Tier");
            foreach (Preset p in o.Presets)
            {
                foreach (int k in new[] { 0, 4, 10 })
                {
                    int holdFrames = (int)Math.Round(0.8f * o.Fps);
                    bool jab = k > 0;
                    float lastCharge = 0f;   // the model's own charge clock (60cb8ee: real time, plus credit for the held press)
                    Trace tr = Trace.Run(p, f => new Pad { Light = jab && f == 0, Heavy = f >= k && f < k + holdFrames }, 150, null, o.Fps,
                        (s, f) => { if (s.Model.State == PlayerState.Charging) lastCharge = s.Model.ChargeTime; });
                    int chargeStart = tr.First(PlayerEventType.ChargeStarted);
                    var heavy = tr.Events.FirstOrDefault(e => e.e.Type == PlayerEventType.AttackStarted && e.e.AttackKind == PlayerAttackKind.Heavy);
                    h.Row(p, jab ? "frame " + k + " of a Jab" : "from idle", chargeStart, Out.N(0.8, 2) + " s",
                        Out.N(lastCharge, 3) + " s", heavy.e.ChargeTier);
                }
            }
            h.Print();

            Out.Sub("Oldest buffered press executed during 200k frames of random input (fuzz, Fluid)");
            Out.Line("(see the fuzz scenario: max buffered age is reported there)");
        }

        static string Describe(Preset p, Func<int, Pad> script)
        {
            Trace t = Trace.Run(p, script, 120, null, 60f);
            var parts = new List<string>();
            foreach (var (frame, e) in t.Events)
            {
                switch (e.Type)
                {
                    case PlayerEventType.AttackStarted: parts.Add(e.Move.DisplayName + (e.ChargeTier != ChargeTier.None ? " (" + e.ChargeTier + ")" : "") + " @" + frame); break;
                    case PlayerEventType.DodgeStarted: parts.Add("Dodge @" + frame); break;
                    case PlayerEventType.GuardStarted: parts.Add("Guard @" + frame); break;
                    case PlayerEventType.HealStarted: parts.Add("Heal @" + frame); break;
                    case PlayerEventType.HealApplied: parts.Add("(healed @" + frame + ")"); break;
                    case PlayerEventType.ChargeStarted: parts.Add("charge @" + frame); break;
                }
            }
            return string.Join(", ", parts);
        }
    }
}
