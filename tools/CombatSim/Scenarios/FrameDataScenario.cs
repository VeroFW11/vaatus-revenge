using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // Frame data measured by playing the model (not copied from tuning). Frame 0 = the frame the button goes
    // down (or is released, for the charged heavy). "Active" = first frame the hitbox checks. "Free" = first
    // frame another action could start without a cancel. Cancel columns: the first frame a dodge / attack
    // actually starts when the button is mashed from the frame after the move began.
    public static class FrameDataScenario
    {
        public static void Run(Options o)
        {
            Out.Heading("Frame data (measured at " + o.Fps + " fps)");
            Out.Line("Frame 0 is the frame the button goes down (released, for the heavy). 1 frame = " + Out.N(1000.0 / o.Fps, 1) + " ms. "
                     + "Cancel columns mash the button from the frame after the move starts.");
            foreach (Preset p in o.Presets)
            {
                Out.Sub(p + " preset");
                var t = new Table("Move", "Input → first active", "Active", "Input → free", "Dodge cancel from", "Attack cancel from", "Stamina");
                for (int k = 0; k < 3; k++) t.Row(MeasureChain(p, k, o.Fps));
                foreach (float hold in new[] { 0f, 0.4f, 0.8f, 1.3f }) t.Row(MeasureHeavy(p, hold, o.Fps));
                t.Row(MeasureSprintAttack(p, o.Fps));
                t.Row(MeasurePlunge(p, o.Fps));
                t.Row(MeasureSkill(p, o.Fps));
                t.Print();

                var d = new Table("Defensive move", "Input → start", "I-frames (from start)", "Duration", "Next dodge from", "Attack from", "Distance", "Stamina");
                d.Row(MeasureDodge(p, true, o.Fps));
                d.Row(MeasureDodge(p, false, o.Fps));
                d.Row(MeasureChainedDodges(p, o.Fps));
                d.Print();

                var m = new Table("Other", "Measured");
                foreach (var row in MeasureMisc(p, o.Fps)) m.Row(row.Item1, row.Item2);
                m.Print();
            }
        }

        static float Flat(Vector3 v)
        {
            return new Vector2(v.X, v.Z).Length();
        }

        static string F(int frames, float fps)
        {
            if (frames < 0) return "never";
            return frames + " f (" + Out.N(frames / fps, 3) + " s)";
        }

        // k = 0 jab, 1 cross, 2 kick. Light is re-pressed until chain move k starts, then mashes are probed.
        static object[] MeasureChain(Preset p, int k, float fps)
        {
            int startK = -1;
            Session s = null;
            Func<int, Pad> baseScript = f =>
            {
                var pad = new Pad();
                if (s != null && startK < 0)
                {
                    if (s.Model.State == PlayerState.Attacking && s.Model.ChainIndex == k && s.Model.ActionTime <= 1e-6f) startK = f - 1;
                    if (f == 0 || (startK < 0 && f % 2 == 0)) pad.Light = true;
                }
                return pad;
            };
            // Pass 1: find the frame move k starts and its own timings.
            Trace t = Trace.Run(p, baseScript, 120, ss => s = ss, fps);
            var starts = t.Events.Where(e => e.e.Type == PlayerEventType.AttackStarted).Select(e => e.frame).ToList();
            if (starts.Count <= k) return new object[] { "chain " + k, "-", "-", "-", "-", "-", "-" };
            int start = starts[k];
            MoveData move = t.Events.First(e => e.e.Type == PlayerEventType.AttackStarted && e.frame == start).e.Move;
            int active = t.First(PlayerEventType.AttackActiveStart, start);
            int activeEnd = t.First(PlayerEventType.AttackActiveEnd, start);
            int ended = t.First(PlayerEventType.AttackEnded, start + 1);
            int inputFrame = k == 0 ? 0 : start;   // for chain moves the press was buffered: count from the move start
            // Pass 2: same, but mash dodge once move k has started.
            int dodgeAt = ProbeAfter(p, fps, k, Button.Dodge, PlayerEventType.DodgeStarted);
            int attackAt = ProbeAfter(p, fps, k, Button.Light, PlayerEventType.AttackStarted);
            string label = move.DisplayName + (k == 0 ? "" : " (chained)");
            return new object[]
            {
                label, F(active - inputFrame, fps), F(activeEnd - active, fps), F(ended - inputFrame, fps),
                dodgeAt >= 0 ? F(dodgeAt - start, fps) : "never", attackAt >= 0 ? F(attackAt - start, fps) : "never", move.StaminaCost
            };
        }

        static int ProbeAfter(Preset p, float fps, int k, Button b, PlayerEventType type)
        {
            int startK = -1;
            Session s = null;
            int result = -1;
            Func<int, Pad> script = f =>
            {
                var pad = new Pad();
                if (s == null) return pad;
                if (startK < 0 && s.Model.State == PlayerState.Attacking && s.Model.ChainIndex == k) startK = s.World.Frame;
                if (startK < 0)
                {
                    if (f == 0 || f % 2 == 0) pad.Light = true;
                }
                else if ((f - startK) % 2 == 1)
                {
                    Trace.Set(ref pad, b, true);
                }
                return pad;
            };
            Trace t = Trace.Run(p, script, 140, ss => s = ss, fps);
            var starts = t.Events.Where(e => e.e.Type == PlayerEventType.AttackStarted).Select(e => e.frame).ToList();
            if (starts.Count <= k) return -1;
            int moveStart = starts[k];
            for (int i = 0; i < t.Events.Count; i++)
            {
                var (frame, e) = t.Events[i];
                if (frame <= moveStart || e.Type != type) continue;
                result = frame;
                break;
            }
            return result;
        }

        static object[] MeasureHeavy(Preset p, float hold, float fps)
        {
            int holdFrames = Math.Max(1, (int)Math.Round(hold * fps));
            Func<int, Pad> script = f => new Pad { Heavy = f < holdFrames };
            Trace t = Trace.Run(p, script, 180, null, fps);
            int started = t.First(PlayerEventType.AttackStarted);
            PlayerEvent ev = t.FirstEvent(PlayerEventType.AttackStarted);
            int active = t.First(PlayerEventType.AttackActiveStart);
            int activeEnd = t.First(PlayerEventType.AttackActiveEnd);
            int ended = t.First(PlayerEventType.AttackEnded, started + 1);
            int release = hold <= 0f ? 0 : Math.Min(holdFrames, started);
            int dodge = ProbeHeavy(p, fps, holdFrames, Button.Dodge, PlayerEventType.DodgeStarted);
            int attack = ProbeHeavy(p, fps, holdFrames, Button.Light, PlayerEventType.AttackStarted);
            string name = "Fa Jin Palm, " + (hold <= 0f ? "tap" : "hold " + Out.N(hold, 1) + " s") + " → " + ev.ChargeTier;
            return new object[]
            {
                name, F(active - release, fps) + " after release", F(activeEnd - active, fps), F(ended - release, fps) + " after release",
                dodge >= 0 ? F(dodge - started, fps) + " after release" : "never", attack >= 0 ? F(attack - started, fps) + " after release" : "never",
                ev.Move != null ? ev.Move.StaminaCost : 0f
            };
        }

        static int ProbeHeavy(Preset p, float fps, int holdFrames, Button b, PlayerEventType type)
        {
            int released = -1;
            Session s = null;
            Func<int, Pad> script = f =>
            {
                var pad = new Pad { Heavy = f < holdFrames };
                if (s != null && released < 0 && s.Model.State == PlayerState.Attacking) released = s.World.Frame;
                if (released >= 0 && (f - released) % 2 == 1) Trace.Set(ref pad, b, true);
                return pad;
            };
            Trace t = Trace.Run(p, script, 200, ss => s = ss, fps);
            int start = t.First(PlayerEventType.AttackStarted);
            for (int i = 0; i < t.Events.Count; i++)
            {
                var (frame, e) = t.Events[i];
                if (frame > start && e.Type == type) return frame;
            }
            return -1;
        }

        static object[] MeasureSprintAttack(Preset p, float fps)
        {
            // Hold dodge (sprint) with the stick forward for 50 frames, then press light.
            int pressAt = 50;
            Func<int, Pad> script = f => new Pad { Dodge = f < pressAt + 2, Move = new Vector2(0f, 1f), Light = f == pressAt };
            Trace t = Trace.Run(p, script, 150, null, fps);
            int start = t.First(PlayerEventType.AttackStarted);
            PlayerEvent ev = t.FirstEvent(PlayerEventType.AttackStarted);
            int active = t.First(PlayerEventType.AttackActiveStart);
            int activeEnd = t.First(PlayerEventType.AttackActiveEnd);
            int ended = t.First(PlayerEventType.AttackEnded, start + 1);
            float travel = start >= 0 && ended > start ? Vector3.Distance(t.States[start].Position, t.States[ended].Position) : 0f;
            string name = (ev.Move != null ? ev.Move.DisplayName : "?") + " (light after " + pressAt + " f of held Dodge)";
            return new object[] { name, F(active - pressAt, fps), F(activeEnd - active, fps), F(ended - pressAt, fps) + ", travels " + Out.N(travel, 2) + " m", "see Punishing note", "-", ev.Move?.StaminaCost };
        }

        static object[] MeasurePlunge(Preset p, float fps)
        {
            int jump = 0, light = 16;   // Plunge.MinAirTime 0.25 s = 15 f (60cb8ee); earlier presses are dropped
            Func<int, Pad> script = f => new Pad { Jump = f == jump, Light = f == light };
            Trace t = Trace.Run(p, script, 120, null, fps);
            int start = t.First(PlayerEventType.AttackStarted);
            int impact = t.First(PlayerEventType.PlungeImpact);
            int ended = t.First(PlayerEventType.AttackEnded, start + 1);
            PlayerEvent ev = t.FirstEvent(PlayerEventType.AttackStarted);
            return new object[] { (ev.Move != null ? ev.Move.DisplayName : "?") + " (jump, light 16 f later)", F(impact - light, fps) + " (landing ring)", "1 f (one-shot ring)", F(ended - light, fps), "-", "-", ev.Move?.StaminaCost };
        }

        static object[] MeasureSkill(Preset p, float fps)
        {
            Func<int, Pad> script = f => new Pad { Skill = f == 0 };
            Trace t = Trace.Run(p, script, 90, null, fps);
            int launch = t.First(PlayerEventType.ProjectileLaunched);
            int ended = t.First(PlayerEventType.AttackEnded);
            int dodge = -1;
            {
                Func<int, Pad> s2 = f => new Pad { Skill = f == 0, Dodge = f >= 1 && f % 2 == 1 };
                Trace t2 = Trace.Run(p, s2, 90, null, fps);
                dodge = t2.First(PlayerEventType.DodgeStarted);
            }
            PlayerEvent ev = t.FirstEvent(PlayerEventType.AttackStarted);
            return new object[] { (ev.Move != null ? ev.Move.DisplayName : "?") + " (projectile)", F(launch, fps) + " (launch)", "flies " + ev.Move?.Projectile.Speed + " m/s", F(ended, fps), F(dodge, fps), "-", ev.Move?.StaminaCost };
        }

        static object[] MeasureDodge(Preset p, bool withStick, float fps)
        {
            Vector2 stick = withStick ? new Vector2(1f, 0f) : Vector2.Zero;
            // A 3-frame tap: fires on press (Fluid) or on release (Punishing).
            Func<int, Pad> script = f => new Pad { Dodge = f < 3, Move = f < 30 ? stick : Vector2.Zero };
            Trace t = Trace.Run(p, script, 90, null, fps);
            int start = t.First(PlayerEventType.DodgeStarted);
            int ended = t.First(PlayerEventType.DodgeEnded);
            var span = t.Span(st => st.Invulnerable, start);
            float dist = start >= 0 && ended > 0 ? Vector3.Distance(t.States[Math.Max(0, start - 1)].Position, t.States[ended].Position) : 0f;
            int next = ProbeDodgeFollow(p, fps, stick, Button.Dodge, PlayerEventType.DodgeStarted);
            int attack = ProbeDodgeFollow(p, fps, stick, Button.Light, PlayerEventType.AttackStarted);
            string iframes = span.start >= 0 ? "frames " + (span.start - start) + "–" + (span.end - start) + " (" + (span.end - span.start + 1) + " f)" : "none";
            DodgeProfile d = t.Session.Model.MoveSet.Dodge;
            return new object[] { withStick ? d.DisplayName + " (stick)" : "Backstep (no stick)", F(start, fps) + (start > 0 ? " (on release)" : ""), iframes, F(ended - start, fps), F(next, fps), F(attack, fps), Out.N(dist, 2) + " m", d.StaminaCost };
        }

        static int ProbeDodgeFollow(Preset p, float fps, Vector2 stick, Button b, PlayerEventType type)
        {
            int dodgeStart = -1;
            Session s = null;
            Func<int, Pad> script = f =>
            {
                var pad = new Pad { Dodge = f < 3, Move = stick };
                if (s != null && dodgeStart < 0 && s.Model.State == PlayerState.Dodging) dodgeStart = s.World.Frame;
                if (dodgeStart >= 0 && f > 3 && (f - dodgeStart) % 2 == 1) Trace.Set(ref pad, b, true);
                if (b == Button.Dodge && dodgeStart >= 0 && f > 3) pad.Dodge = (f - dodgeStart) % 2 == 1;
                return pad;
            };
            Trace t = Trace.Run(p, script, 120, ss => s = ss, fps);
            int first = t.First(PlayerEventType.DodgeStarted);
            for (int i = 0; i < t.Events.Count; i++)
            {
                var (frame, e) = t.Events[i];
                if (frame > first && e.Type == type) return frame - first;
            }
            return -1;
        }

        // Dodge spam: how much of the time is the player invulnerable, and how long is the longest gap?
        static object[] MeasureChainedDodges(Preset p, float fps)
        {
            Func<int, Pad> script = f => new Pad { Dodge = f % 4 < 2, Move = new Vector2(1f, 0f) };
            Trace t = Trace.Run(p, script, 600, null, fps);
            int inv = t.States.Count(st => st.Invulnerable);
            int dodges = t.Count(PlayerEventType.DodgeStarted);
            int longestVuln = 0, run = 0, maxInv = 0, runInv = 0;
            for (int i = 60; i < t.States.Count; i++)
            {
                if (t.States[i].Invulnerable) { runInv++; maxInv = Math.Max(maxInv, runInv); run = 0; }
                else { run++; longestVuln = Math.Max(longestVuln, run); runInv = 0; }
            }
            float staminaEnd = t.States[t.States.Count - 1].Stamina;
            return new object[] { "Dodge mashed for 10 s", dodges + " dodges", Out.Pct(inv / (double)t.States.Count) + " of frames invulnerable, longest " + maxInv + " f", "-", "longest vulnerable gap " + longestVuln + " f", "-", "-", "stamina at end " + Out.N(staminaEnd, 0) };
        }

        static List<(string, string)> MeasureMisc(Preset p, float fps)
        {
            var rows = new List<(string, string)>();
            // Heal: press R once.
            {
                Trace t = Trace.Run(p, f => new Pad { Heal = f == 0 }, 120, null, fps);
                int st = t.First(PlayerEventType.HealStarted), ap = t.First(PlayerEventType.HealApplied);
                int free = t.FirstState(x => x.State == PlayerState.Locomotion, st + 1);
                rows.Add(("Heal (Spirit Water)", "starts " + F(st, fps) + ", health arrives " + F(ap, fps) + ", free " + F(free, fps)));
            }
            // Jump: air time on flat ground.
            {
                Trace t = Trace.Run(p, f => new Pad { Jump = f == 0 }, 120, null, fps);
                int j = t.First(PlayerEventType.Jumped), l = t.First(PlayerEventType.Landed, j + 1);
                float apex = t.States.Max(x => x.Position.Y);
                rows.Add(("Jump", "leaves ground " + F(j, fps) + ", lands " + F(l, fps) + ", apex " + Out.N(apex, 2) + " m"));
            }
            // Guard: deflect window after a tap.
            {
                Trace t = Trace.Run(p, f => new Pad { Guard = f == 0 }, 60, null, fps);
                var span = t.Span(x => x.DeflectOpen);
                var guard = t.Span(x => x.Guarding);
                rows.Add(("Guard tap (1 f)", "deflect window frames " + span.start + "–" + span.end + " (" + (span.end - span.start + 1) + " f); guard stays up " + (guard.end - guard.start + 1) + " f"));
            }
            // Walk/run/sprint speeds.
            {
                Trace t = Trace.Run(p, f => new Pad { Move = new Vector2(0f, 1f) }, 120, null, fps);
                float run = Flat(t.States[119].Velocity);
                int reach = t.FirstState(x => Flat(x.Velocity) >= run * 0.99f);
                Trace t2 = Trace.Run(p, f => new Pad { Move = new Vector2(0f, 1f), Dodge = true }, 180, null, fps);
                float sprint = Flat(t2.States[179].Velocity);
                int sprintStart = t2.First(PlayerEventType.SprintStarted);
                float staminaSprint = t2.States[179].Stamina;
                rows.Add(("Run / sprint speed", Out.N(run, 2) + " m/s (reached in " + reach + " f) / " + Out.N(sprint, 2) + " m/s (sprint starts " + F(sprintStart, fps) + " after pressing, stamina after 3 s: " + Out.N(staminaSprint, 0) + ")"));
            }
            return rows;
        }
    }
}
