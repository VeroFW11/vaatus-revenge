using System;
using System.Collections.Generic;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // Which buttons are physically down on one frame, plus the sticks. PadInput turns a sequence of these
    // into PlayerInputFrames with Pressed/Released edges, exactly like PlayerInputReader.ReadButton does.
    public struct Pad
    {
        public bool Light, Heavy, Dodge, Jump, Guard, Skill, Heal, LockOn, SwapShoulder, ZipStrike, AbilityNorth, AbilityEast;
        public Vector2 Move;
        public Vector2 Look;
        public bool LookIsMouse;
        public int SwitchTarget;
        public ElementId Element;
    }

    public sealed class PadInput
    {
        Pad last;

        // Real-pad mode (Build 05 verify round 4, BH-04): Light / ZipStrike / Dodge / Jump are the raw X / Y / B / A face
        // buttons and Guard is LB; an element pick (Pad.Element) is played as a human chord: its face button and RB go
        // down up to MaxChordSkew apart in either order, held ChordHold, and all of it goes through the game's
        // PadChordReader exactly like PlayerInputReader (so the hold-back, the grace and the swallowed faces are tested).
        // Off (the default): the keyboard path, presses reach the rules as they are and Element is the number keys.
        public bool RealPad;
        public float MaxChordSkew = 0.08f;
        public float ChordHold = 0.10f;
        public readonly PadChordReader Chords = new PadChordReader();
        readonly ElementButtonLayout layout = new ElementButtonLayout();
        DeterministicRandom rng = new DeterministicRandom(1);
        double clock;
        int chordFace = -1;
        double chordFaceDown, chordRbDown, chordEnd;
        bool rawN, rawE, rawS, rawW, rawRb, rawLb;

        // Real time of the bot's own last press of each face (for "stray action" checks): X, Y, B, A.
        public double LastOwnLight = double.NegativeInfinity, LastOwnZip = double.NegativeInfinity;
        public double LastOwnDodge = double.NegativeInfinity, LastOwnJump = double.NegativeInfinity, LastOwnSkill = double.NegativeInfinity;
        public int ChordsPlayed, ChordsFaceFirst;
        public double Clock => clock;

        public void UseRealPad(int seed)
        {
            RealPad = true;
            rng = new DeterministicRandom(seed);
        }

        public PlayerInputFrame Build(in Pad now)
        {
            return Build(now, 1f / 60f);
        }

        public PlayerInputFrame Build(in Pad now, float realDt)
        {
            if (RealPad) return BuildRealPad(now, realDt);
            var f = new PlayerInputFrame
            {
                Move = ClampMagnitude(now.Move),
                Look = now.Look,
                LookIsMouse = now.LookIsMouse,
                Light = ButtonState.From(now.Light, last.Light),
                Heavy = ButtonState.From(now.Heavy, last.Heavy),
                Dodge = ButtonState.From(now.Dodge, last.Dodge),
                Jump = ButtonState.From(now.Jump, last.Jump),
                Guard = ButtonState.From(now.Guard, last.Guard),
                Skill = ButtonState.From(now.Skill, last.Skill),
                Heal = ButtonState.From(now.Heal, last.Heal),
                LockOn = ButtonState.From(now.LockOn, last.LockOn),
                SwapShoulder = ButtonState.From(now.SwapShoulder, last.SwapShoulder),
                ZipStrike = ButtonState.From(now.ZipStrike, last.ZipStrike),
                AbilityNorth = ButtonState.From(now.AbilityNorth, last.AbilityNorth),
                AbilityEast = ButtonState.From(now.AbilityEast, last.AbilityEast),
                SwitchTargetDelta = now.SwitchTarget,
                ElementSelect = now.Element
            };
            last = now;
            return f;
        }

        PlayerInputFrame BuildRealPad(in Pad now, float realDt)
        {
            float dt = realDt > 0f ? realDt : 0f;
            clock += dt;
            if (now.Light && !last.Light) LastOwnLight = clock;
            if (now.ZipStrike && !last.ZipStrike) LastOwnZip = clock;
            if (now.Dodge && !last.Dodge) LastOwnDodge = clock;
            if (now.Jump && !last.Jump) LastOwnJump = clock;
            if (now.Skill && !last.Skill) LastOwnSkill = clock;

            // A new element pick becomes a chord in flight (one at a time: a pick during a chord is a change of mind
            // that waits for the next chord).
            if (now.Element != ElementId.None && chordFace < 0)
            {
                int face = layout.PadSlotOf(now.Element);
                if (face >= 0)
                {
                    float skew = rng.Range(-MaxChordSkew, MaxChordSkew);   // > 0: the face first, RB 'skew' later
                    // The chord owns its face button from now on. If the bot is still holding that face from a press of its
                    // own, a human's thumb comes up first: the chord starts a frame later.
                    double start = RawFace(face) ? clock + Math.Max(dt, 1e-3) : clock;
                    chordFace = face;
                    chordFaceDown = skew >= 0f ? start : start - skew;
                    chordRbDown = skew >= 0f ? start + skew : start;
                    chordEnd = Math.Max(chordFaceDown, chordRbDown) + ChordHold;
                    ChordsPlayed++;
                    if (skew > 0f) ChordsFaceFirst++;
                }
            }
            bool chordFaceHeld = false, chordRbHeld = false;
            if (chordFace >= 0)
            {
                if (clock >= chordEnd - 1e-6) chordFace = -1;
                else
                {
                    chordFaceHeld = clock >= chordFaceDown - 1e-6;
                    chordRbHeld = clock >= chordRbDown - 1e-6;
                }
            }
            int cf = chordFace;
            bool n = cf == PadChordReader.FaceNorth ? chordFaceHeld : now.ZipStrike;
            bool e = cf == PadChordReader.FaceEast ? chordFaceHeld : now.Dodge;
            bool so = cf == PadChordReader.FaceSouth ? chordFaceHeld : now.Jump;
            bool w = cf == PadChordReader.FaceWest ? chordFaceHeld : now.Light;
            bool rb = chordRbHeld;
            bool lb = now.Guard;
            ButtonState north = ButtonState.From(n, rawN);
            ButtonState east = ButtonState.From(e, rawE);
            ButtonState south = ButtonState.From(so, rawS);
            ButtonState west = ButtonState.From(w, rawW);
            ButtonState rbState = ButtonState.From(rb, rawRb);
            ButtonState lbState = ButtonState.From(lb, rawLb);
            rawN = n; rawE = e; rawS = so; rawW = w; rawRb = rb; rawLb = lb;
            PadChordReader.Result chord = Chords.Read(ref north, ref east, ref south, ref west, rbState, lbState, dt, layout);

            ButtonState heavy = Or(chord.Heavy, ButtonState.From(now.Heavy, last.Heavy));
            ButtonState abilityNorth = Or(chord.AbilityNorth, ButtonState.From(now.AbilityNorth, last.AbilityNorth));
            ButtonState abilityEast = Or(chord.AbilityEast, ButtonState.From(now.AbilityEast, last.AbilityEast));
            ButtonState skill = ButtonState.From(now.Skill, last.Skill);   // the mouse's right button (RB's tap is chord.Skill)
            if (chord.Skill.Pressed) skill.Pressed = true;
            var f = new PlayerInputFrame
            {
                Move = ClampMagnitude(now.Move),
                Look = now.Look,
                LookIsMouse = now.LookIsMouse,
                Light = west,
                Heavy = heavy,
                Dodge = east,
                Jump = south,
                Guard = lbState,
                Skill = skill,
                Heal = ButtonState.From(now.Heal, last.Heal),
                LockOn = ButtonState.From(now.LockOn, last.LockOn),
                SwapShoulder = ButtonState.From(now.SwapShoulder, last.SwapShoulder),
                ZipStrike = north,
                AbilityNorth = abilityNorth,
                AbilityEast = abilityEast,
                SwitchTargetDelta = now.SwitchTarget,
                ElementSelect = chord.ElementSelect,
                RetractPress = chord.ElementSelect != ElementId.None ? chord.RetractPress : PlayerCommand.None,
            };
            last = now;
            return f;
        }

        bool RawFace(int face)
        {
            return face == PadChordReader.FaceNorth ? rawN : face == PadChordReader.FaceEast ? rawE : face == PadChordReader.FaceSouth ? rawS : rawW;
        }

        static ButtonState Or(ButtonState a, ButtonState b)
        {
            return new ButtonState { Held = a.Held || b.Held, Pressed = a.Pressed || b.Pressed, Released = (a.Released || b.Released) && !(a.Held || b.Held) };
        }

        static Vector2 ClampMagnitude(Vector2 v)
        {
            float l = v.Length();
            return l > 1f ? v / l : v;
        }
    }

    public enum Button { Light, Heavy, Dodge, Jump, Guard, Skill, Heal, LockOn }

    // Scheduled button holds: "press Dodge at real time t for 0.08 s". Several holds of the same button merge.
    public sealed class ButtonScheduler
    {
        struct Hold
        {
            public Button Button;
            public double Start, End;
        }

        readonly List<Hold> holds = new List<Hold>();

        public void Press(Button b, double start, double duration)
        {
            holds.Add(new Hold { Button = b, Start = start, End = start + Math.Max(1e-4, duration) });
        }

        public bool IsScheduled(Button b, double from)
        {
            for (int i = 0; i < holds.Count; i++) if (holds[i].Button == b && holds[i].End > from) return true;
            return false;
        }

        public void Cancel(Button b)
        {
            holds.RemoveAll(h => h.Button == b);
        }

        public void Apply(ref Pad pad, double now)
        {
            for (int i = holds.Count - 1; i >= 0; i--)
            {
                Hold h = holds[i];
                if (now >= h.End) { holds.RemoveAt(i); continue; }
                if (now < h.Start) continue;
                switch (h.Button)
                {
                    case Button.Light: pad.Light = true; break;
                    case Button.Heavy: pad.Heavy = true; break;
                    case Button.Dodge: pad.Dodge = true; break;
                    case Button.Jump: pad.Jump = true; break;
                    case Button.Guard: pad.Guard = true; break;
                    case Button.Skill: pad.Skill = true; break;
                    case Button.Heal: pad.Heal = true; break;
                    case Button.LockOn: pad.LockOn = true; break;
                }
            }
        }
    }

    // Human timing: reaction times and timing errors drawn from normal distributions (seeded).
    public sealed class Human
    {
        readonly DeterministicRandom rng;
        public float ReactionMean = 0.25f;     // seconds from seeing something to pressing (typical 0.2-0.3)
        public float ReactionSd = 0.04f;
        public float ReactionMin = 0.15f;
        public float TimingSd = 0.05f;         // error when timing a learned rhythm (anticipation)
        public float PressMin = 0.06f, PressMax = 0.12f;

        public Human(int seed)
        {
            rng = new DeterministicRandom(seed);
        }

        public DeterministicRandom Rng => rng;

        public float Normal(float mean, float sd)
        {
            float u1 = Math.Max(1e-7f, rng.NextFloat());
            float u2 = rng.NextFloat();
            float z = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
            return mean + sd * z;
        }

        public float Reaction()
        {
            return Math.Clamp(Normal(ReactionMean, ReactionSd), ReactionMin, 0.6f);
        }

        public float Jitter()
        {
            return Normal(0f, TimingSd);
        }

        public float PressLength()
        {
            return rng.Range(PressMin, PressMax);
        }

        public bool Chance(float p)
        {
            return rng.Chance(p);
        }

        public float Range(float a, float b)
        {
            return rng.Range(a, b);
        }
    }

    // A fixed script: holds and stick keyframes by frame number (a tiny DSL for the frame-data scenarios).
    //   var s = new InputScript().Hold(Button.Light, 0, 3).Stick(0, new Vector2(0, 1)).Hold(Button.Dodge, 20, 2);
    public sealed class InputScript
    {
        readonly List<(Button b, int start, int frames)> holds = new List<(Button, int, int)>();
        readonly SortedDictionary<int, Vector2> sticks = new SortedDictionary<int, Vector2>();
        readonly SortedDictionary<int, Vector2> looks = new SortedDictionary<int, Vector2>();

        public InputScript Hold(Button b, int startFrame, int frames)
        {
            holds.Add((b, startFrame, frames));
            return this;
        }

        public InputScript Stick(int fromFrame, Vector2 value)
        {
            sticks[fromFrame] = value;
            return this;
        }

        public InputScript Look(int fromFrame, Vector2 value)
        {
            looks[fromFrame] = value;
            return this;
        }

        public Pad At(int frame)
        {
            var pad = new Pad();
            foreach (var h in holds)
            {
                if (frame < h.start || frame >= h.start + h.frames) continue;
                switch (h.b)
                {
                    case Button.Light: pad.Light = true; break;
                    case Button.Heavy: pad.Heavy = true; break;
                    case Button.Dodge: pad.Dodge = true; break;
                    case Button.Jump: pad.Jump = true; break;
                    case Button.Guard: pad.Guard = true; break;
                    case Button.Skill: pad.Skill = true; break;
                    case Button.Heal: pad.Heal = true; break;
                    case Button.LockOn: pad.LockOn = true; break;
                }
            }
            foreach (var kv in sticks) if (kv.Key <= frame) pad.Move = kv.Value;
            foreach (var kv in looks) if (kv.Key <= frame) pad.Look = kv.Value;
            return pad;
        }
    }
}
