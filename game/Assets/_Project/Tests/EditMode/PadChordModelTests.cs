using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // The Xbox chord end to end (Build 05 verify round 4, J4-01): raw pad buttons -> PadChordReader -> PlayerInputFrame
    // -> PlayerCombatModel, wired exactly like PlayerInputReader. A face button that lands up to 5 frames (60 fps) before
    // RB must only switch the element: no jump, dodge, zip, skill or second attack on top, from neutral, from string
    // memory, mid-move and after a dodge, on both presets.
    public class PadChordModelTests
    {
        static readonly int[] FramesEarly = { 0, 1, 2, 3, 5 };
        static readonly int[] Faces = { PadChordReader.FaceNorth, PadChordReader.FaceEast, PadChordReader.FaceSouth, PadChordReader.FaceWest };

        public enum Context { Neutral, StringMemory, MidMove, AfterDodge }

        // Raw pad -> chord reader -> model, one frame at a time.
        sealed class PadProbe
        {
            public readonly PlayerDriver D;
            public readonly PadChordReader Reader = new PadChordReader();
            public readonly ElementButtonLayout Layout = new ElementButtonLayout();
            readonly bool[] faceWas = new bool[4];
            bool rbWas, lbWas;

            public PadProbe(bool punishing)
            {
                D = punishing ? PlayerDriver.Elements(PlayerTuning.CreatePunishing(), ElementLoadout.CreatePunishing()) : PlayerDriver.Elements();
                D.Target(new Vector3(0f, 0f, 1.6f));
            }

            public void Step(int face = -1, bool rb = false, Vector2 move = default, int face2 = -1)
            {
                var now = new bool[4];
                if (face >= 0) now[face] = true;
                if (face2 >= 0) now[face2] = true;
                ButtonState north = ButtonState.From(now[0], faceWas[0]);
                ButtonState east = ButtonState.From(now[1], faceWas[1]);
                ButtonState south = ButtonState.From(now[2], faceWas[2]);
                ButtonState west = ButtonState.From(now[3], faceWas[3]);
                ButtonState rbState = ButtonState.From(rb, rbWas);
                ButtonState lbState = ButtonState.From(false, lbWas);
                for (int i = 0; i < 4; i++) faceWas[i] = now[i];
                rbWas = rb;
                lbWas = false;
                PadChordReader.Result chord = Reader.Read(ref north, ref east, ref south, ref west, rbState, lbState, D.Dt, Layout);
                var frame = new PlayerInputFrame
                {
                    Move = move,
                    Light = west,
                    ZipStrike = north,
                    Dodge = east,
                    Jump = south,
                    Guard = lbState,
                    Skill = chord.Skill,
                    Heavy = chord.Heavy,
                    AbilityNorth = chord.AbilityNorth,
                    AbilityEast = chord.AbilityEast,
                    ElementSelect = chord.ElementSelect,
                    RetractPress = chord.ElementSelect != ElementId.None ? chord.RetractPress : PlayerCommand.None,
                };
                D.StepFrame(frame);
            }

            public void Run(int frames, int face = -1, Vector2 move = default)
            {
                for (int i = 0; i < frames; i++) Step(face, false, move);
            }

            public void Tap(int face, int frames = 3, Vector2 move = default)
            {
                Run(frames, face, move);
                Step(-1, false, move);
            }

            // Steps until the model has started n attacks in total.
            public void RunUntilStarted(int n, int max = 120)
            {
                for (int i = 0; i < max && D.Started < n; i++) Step();
                Assert.GreaterOrEqual(D.Started, n, "attack " + n + " never started");
            }

            // The chord: the face goes down 'early' frames before RB, both held, then both let go.
            public void Chord(int face, int early)
            {
                for (int i = 0; i < early; i++) Step(face);
                for (int i = 0; i < 6; i++) Step(face, true);
                Step();
            }
        }

        static string Describe(PlayerDriver d, int from)
        {
            var parts = new List<string>();
            for (int i = from; i < d.Log.Count; i++)
            {
                PlayerEvent e = d.Log[i];
                if (e.Type == PlayerEventType.AttackStarted) parts.Add("f" + d.LogFrames[i] + " Attack:" + e.AttackKind + (e.IsSwitchStrike ? "(switch)" : "") + ":" + e.Element);
                else if (e.Type == PlayerEventType.Jumped || e.Type == PlayerEventType.DodgeStarted || e.Type == PlayerEventType.ElementSwitched)
                    parts.Add("f" + d.LogFrames[i] + " " + e.Type + (e.Type == PlayerEventType.ElementSwitched ? "->" + e.Element : ""));
            }
            return string.Join(", ", parts);
        }

        // Builds the context, with the active element different from the face's element, and returns the log index the
        // chord starts at.
        static int Prepare(PadProbe p, Context context, ElementId target)
        {
            ElementId start = target == ElementId.Fire ? ElementId.Water : ElementId.Fire;
            if (p.D.Model.ActiveElement != start)
            {
                p.D.Select = start;
                p.D.Step();
                p.Run(30);
            }
            Assert.AreEqual(start, p.D.Model.ActiveElement, "set-up element");
            int started = p.D.Started;
            switch (context)
            {
                case Context.Neutral:
                    break;
                case Context.StringMemory:
                    p.Tap(PadChordReader.FaceWest, 2);
                    p.RunUntilStarted(started + 1);
                    PressOnBeat(p);
                    p.RunUntilStarted(started + 2);
                    for (int i = 0; i < 200 && p.D.Model.State == PlayerState.Attacking; i++) p.Step();
                    Assert.GreaterOrEqual(p.D.Model.StringNextIndex, 0, "the string is remembered");
                    break;
                case Context.MidMove:
                    p.Tap(PadChordReader.FaceWest, 2);
                    p.RunUntilStarted(started + 1);
                    PressOnBeat(p);
                    p.RunUntilStarted(started + 2);
                    p.Run(2);
                    Assert.AreEqual(PlayerState.Attacking, p.D.Model.State);
                    break;
                case Context.AfterDodge:
                    // X, then dodge out of the hit (stick away), then the chord as the dodge ends.
                    p.Tap(PadChordReader.FaceWest, 2);
                    p.RunUntilStarted(started + 1);
                    int dodges = p.D.Count(PlayerEventType.DodgeStarted);
                    var back = new Vector2(0f, -1f);
                    for (int tries = 0; tries < 20 && p.D.Count(PlayerEventType.DodgeStarted) == dodges; tries++)
                    {
                        p.Tap(PadChordReader.FaceEast, 2, back);
                        p.Run(2, -1, back);
                    }
                    Assert.Greater(p.D.Count(PlayerEventType.DodgeStarted), dodges, "the set-up dodge");
                    for (int i = 0; i < 60 && p.D.Model.State == PlayerState.Dodging; i++) p.Step();
                    break;
            }
            return p.D.Log.Count;
        }

        static void PressOnBeat(PadProbe p)
        {
            for (int i = 0; i < 120 && p.D.Model.Rhythm.Active && p.D.Model.Rhythm.TimeToBeat > p.D.Dt * 0.5f; i++) p.Step();
            p.Tap(PadChordReader.FaceWest, 2);
        }

        static void CheckChord(bool punishing, Context context)
        {
            var failures = new List<string>();
            int notLive = 0;
            foreach (int face in Faces)
            {
                foreach (int early in FramesEarly)
                {
                    var p = new PadProbe(punishing);
                    ElementId target = p.Layout.PadSlot(face);
                    int from = Prepare(p, context, target);
                    // Is there a string for the chord to continue (a switch strike) or not (a plain switch)?
                    bool live = context == Context.MidMove || p.D.Model.StringNextIndex >= 0;
                    p.Chord(face, early);
                    p.Run(90);
                    string label = (punishing ? "Punishing " : "Fluid ") + context + " face " + face + " " + early + "f early: " + Describe(p.D, from);

                    int attacks = 0, switchStrikes = 0, jumps = 0, dodges = 0, zips = 0, skills = 0;
                    for (int i = from; i < p.D.Log.Count; i++)
                    {
                        PlayerEvent e = p.D.Log[i];
                        if (e.Type == PlayerEventType.Jumped) jumps++;
                        if (e.Type == PlayerEventType.DodgeStarted) dodges++;
                        if (e.Type != PlayerEventType.AttackStarted) continue;
                        attacks++;
                        if (e.IsSwitchStrike) switchStrikes++;
                        if (e.AttackKind == PlayerAttackKind.ZipStrike) zips++;
                        if (e.AttackKind == PlayerAttackKind.Skill) skills++;
                    }
                    bool xFace = face == PadChordReader.FaceWest;
                    // From neutral a Y / B / A chord is a plain switch (no attack); X is its one hit, kept, plus the switch.
                    // (X and RB on the same frame is a pure chord: no hit from neutral either.)
                    int expectedAttacks = !live && (!xFace || early == 0) ? 0 : 1;
                    if (jumps != 0 || dodges != 0 || zips != 0 || skills != 0) failures.Add(label + "  [stray action]");
                    else if (attacks != expectedAttacks) failures.Add(label + "  [" + attacks + " attacks, expected " + expectedAttacks + "]");
                    else if (p.D.Model.ActiveElement != target) failures.Add(label + "  [ended in " + p.D.Model.ActiveElement + "]");
                    else if (!xFace && live && switchStrikes != 1) failures.Add(label + "  [no switch strike]");
                    if (context != Context.Neutral && !live) notLive++;
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
            Assert.AreEqual(0, notLive, "every string context kept the string alive for the chord");
        }

        [Test] public void FluidNeutral() { CheckChord(false, Context.Neutral); }
        [Test] public void FluidStringMemory() { CheckChord(false, Context.StringMemory); }
        [Test] public void FluidMidMove() { CheckChord(false, Context.MidMove); }
        [Test] public void FluidAfterDodge() { CheckChord(false, Context.AfterDodge); }
        [Test] public void PunishingNeutral() { CheckChord(true, Context.Neutral); }
        [Test] public void PunishingStringMemory() { CheckChord(true, Context.StringMemory); }
        [Test] public void PunishingMidMove() { CheckChord(true, Context.MidMove); }
        [Test] public void PunishingAfterDodge() { CheckChord(true, Context.AfterDodge); }

        // BH-02: Water in hand, X a few frames before RB (RB + X = Water, the element already in hand): one attack, not two.
        [Test]
        public void SameElementEarlyXIsOneAttack([Values(1, 2, 3, 4, 5)] int early, [Values(false, true)] bool memory)
        {
            var p = new PadProbe(false);
            p.D.Select = ElementId.Water;
            p.D.Step();
            p.Run(30);
            int started = p.D.Started;
            if (memory)
            {
                p.Tap(PadChordReader.FaceWest, 2);
                p.RunUntilStarted(started + 1);
                PressOnBeat(p);
                p.RunUntilStarted(started + 2);
                for (int i = 0; i < 200 && p.D.Model.State == PlayerState.Attacking; i++) p.Step();
            }
            int from = p.D.Log.Count;
            int before = p.D.Started;
            p.Chord(PadChordReader.FaceWest, early);
            p.Run(90);
            Assert.AreEqual(1, p.D.Started - before, Describe(p.D, from));
            Assert.AreEqual(ElementId.Water, p.D.Model.ActiveElement);
        }

        // A held-back press keeps its real time: a jump pressed on its own still jumps, 5 frames after the press.
        [Test]
        public void LoneJumpComesOutAfterTheHoldBack()
        {
            var p = new PadProbe(false);
            p.Run(5);
            int from = p.D.Frame + 1;
            p.Tap(PadChordReader.FaceSouth, 10);
            p.Run(10);
            int jumped = p.D.FirstFrame(PlayerEventType.Jumped, from);
            Assert.AreEqual(from + 5, jumped, "jumps 0.083 s after the press");
        }

        // Punishing's dodge is on release (a hold past TapHoldThreshold sprints): the hold-back must not stretch the
        // tap window, because the hold is timed from the real press.
        [Test]
        public void PunishingHoldIsTimedFromTheRealPress()
        {
            var p = new PadProbe(true);
            var forward = new Vector2(0f, 1f);
            p.Run(5, -1, forward);
            float threshold = PlayerTuning.CreatePunishing().TapHoldThreshold;
            int holdFrames = (int)System.Math.Ceiling(threshold * 60f) + 1;   // just past the threshold, really
            p.Run(holdFrames, PadChordReader.FaceEast, forward);
            p.Step(-1, false, forward);
            p.Run(20, -1, forward);
            Assert.AreEqual(0, p.D.Count(PlayerEventType.DodgeStarted), "held past the threshold: a sprint, not a dodge");
        }
    }
}
