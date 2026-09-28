using System;
using System.Collections.Generic;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // Runs a scripted input sequence on a fresh session and keeps every player event and per-frame state.
    public sealed class Trace
    {
        public struct FrameState
        {
            public PlayerState State;
            public bool Invulnerable;
            public bool Guarding;
            public bool DeflectOpen;
            public float Stamina;
            public float Momentum;
            public Vector3 Position;
            public Vector3 Velocity;
            public float FacingYaw;
            public float ActionTime;
            public float TimeScale;
            public PlayerCommand Buffered;
        }

        public readonly List<(int frame, PlayerEvent e)> Events = new List<(int, PlayerEvent)>();
        public readonly List<FrameState> States = new List<FrameState>();
        public Session Session;

        public static Trace Run(Preset preset, Func<int, Pad> script, int frames, Action<Session> setup = null, float fps = 60f,
            Action<Session, int> each = null, SimLevel level = null)
        {
            var t = new Trace();
            var s = new Session(preset, fps, level, camera: false);
            t.Session = s;
            setup?.Invoke(s);
            s.World.PlayerEvent += e => t.Events.Add((s.World.Frame, e));
            for (int f = 0; f < frames; f++)
            {
                s.Step(script(f));
                PlayerCombatModel m = s.Model;
                t.States.Add(new FrameState
                {
                    State = m.State, Invulnerable = m.IsInvulnerable, Guarding = m.IsGuarding, DeflectOpen = m.IsDeflectWindowOpen,
                    Stamina = m.Stamina, Momentum = m.Momentum, Position = s.Player.Feet, Velocity = m.Velocity, FacingYaw = m.FacingYaw,
                    ActionTime = m.ActionTime, TimeScale = s.World.Time.TimeScale, Buffered = m.BufferedCommand
                });
                each?.Invoke(s, f);
            }
            return t;
        }

        public int First(PlayerEventType type, int from = 0, Func<PlayerEvent, bool> pred = null)
        {
            for (int i = 0; i < Events.Count; i++)
            {
                if (Events[i].e.Type != type || Events[i].frame < from) continue;
                if (pred != null && !pred(Events[i].e)) continue;
                return Events[i].frame;
            }
            return -1;
        }

        public PlayerEvent FirstEvent(PlayerEventType type, int from = 0)
        {
            for (int i = 0; i < Events.Count; i++)
                if (Events[i].e.Type == type && Events[i].frame >= from) return Events[i].e;
            return default;
        }

        public int Count(PlayerEventType type, int from = 0, int to = int.MaxValue)
        {
            int n = 0;
            for (int i = 0; i < Events.Count; i++)
                if (Events[i].e.Type == type && Events[i].frame >= from && Events[i].frame <= to) n++;
            return n;
        }

        public int FirstState(Func<FrameState, bool> pred, int from = 0)
        {
            for (int i = from; i < States.Count; i++) if (pred(States[i])) return i;
            return -1;
        }

        // Frames [a, b] where the predicate holds continuously starting at the first match at or after 'from'.
        public (int start, int end) Span(Func<FrameState, bool> pred, int from = 0)
        {
            int s = FirstState(pred, from);
            if (s < 0) return (-1, -1);
            int e = s;
            while (e + 1 < States.Count && pred(States[e + 1])) e++;
            return (s, e);
        }

        // Presses a button every other frame from 'from' on (a masher), holding nothing else.
        public static Func<int, Pad> Mash(Func<int, Pad> baseScript, Button b, int from)
        {
            return f =>
            {
                Pad p = baseScript(f);
                if (f >= from && (f - from) % 2 == 0) Set(ref p, b, true);
                return p;
            };
        }

        public static void Set(ref Pad p, Button b, bool on)
        {
            switch (b)
            {
                case Button.Light: p.Light = on; break;
                case Button.Heavy: p.Heavy = on; break;
                case Button.Dodge: p.Dodge = on; break;
                case Button.Jump: p.Jump = on; break;
                case Button.Guard: p.Guard = on; break;
                case Button.Skill: p.Skill = on; break;
                case Button.Heal: p.Heal = on; break;
                case Button.LockOn: p.LockOn = on; break;
            }
        }
    }
}
