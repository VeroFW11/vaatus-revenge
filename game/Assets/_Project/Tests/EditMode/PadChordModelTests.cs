using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // The Xbox chord end to end (Build 05 verify round 4, J4-01; modifier first since round 5): raw pad buttons ->
    // PadChordReader -> PlayerInputFrame -> PlayerCombatModel, wired exactly like PlayerInputReader. Hold RB, then a face
    // button 0-5 frames (60 fps) later must only switch the element (a switch strike when a string is live): no jump,
    // dodge, zip, skill or extra attack on top, from neutral, from string memory, mid-move and after a dodge, on both
    // presets. Every other press reaches the rules on its own frame, in the order it was made (J5-01, J5-02).
    public class PadChordModelTests
    {
        static readonly int[] FramesRbFirst = { 0, 1, 2, 3, 5 };
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
                    ElementSelectOffAttack = chord.ElementSelectOffAttack,
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

            // The chord, modifier first: RB goes down 'rbFirst' frames before the face (0 = the same frame), both held,
            // then both let go.
            public void Chord(int face, int rbFirst)
            {
                for (int i = 0; i < rbFirst; i++) Step(-1, true);
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
                foreach (int rbFirst in FramesRbFirst)
                {
                    var p = new PadProbe(punishing);
                    ElementId target = p.Layout.PadSlot(face);
                    int from = Prepare(p, context, target);
                    // Is there a string for the chord to continue (a switch strike) or not (a plain switch)?
                    bool live = context == Context.MidMove || p.D.Model.StringNextIndex >= 0;
                    p.Chord(face, rbFirst);
                    p.Run(90);
                    string label = (punishing ? "Punishing " : "Fluid ") + context + " face " + face + " RB " + rbFirst + "f first: " + Describe(p.D, from);

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
                    // From neutral a chord is a plain switch (no attack); with a string live it is the switch strike (one attack).
                    int expectedAttacks = live ? 1 : 0;
                    if (jumps != 0 || dodges != 0 || zips != 0 || skills != 0) failures.Add(label + "  [stray action]");
                    else if (attacks != expectedAttacks) failures.Add(label + "  [" + attacks + " attacks, expected " + expectedAttacks + "]");
                    else if (p.D.Model.ActiveElement != target) failures.Add(label + "  [ended in " + p.D.Model.ActiveElement + "]");
                    else if (live && switchStrikes != 1) failures.Add(label + "  [no switch strike]");
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

        // BH-02: Water in hand, RB held for the next X (X is Water's button): mid-string that X is the next hit, one attack.
        [Test]
        public void SameElementRbThenXIsOneAttack([Values(0, 1, 3, 5)] int rbFirst, [Values(false, true)] bool memory)
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
            p.Chord(PadChordReader.FaceWest, rbFirst);
            p.Run(90);
            Assert.AreEqual(memory ? 1 : 0, p.D.Started - before, Describe(p.D, from));
            Assert.AreEqual(ElementId.Water, p.D.Model.ActiveElement);
        }

        // J6-S02: RB still held mid-string and the face of the element you're already in, when that face isn't X: B in
        // Fire, A in Earth, Y in Air. It's no attack (only X carries the string on), just the wheel's same-element shake.
        [Test]
        public void SameElementNonAttackFaceMidStringIsNoAttack([Values(ElementId.Fire, ElementId.Earth, ElementId.Air)] ElementId element,
            [Values(false, true)] bool punishing)
        {
            var p = new PadProbe(punishing);
            p.D.Select = element;
            p.D.Step();
            p.Run(30);
            Assert.AreEqual(element, p.D.Model.ActiveElement);
            int face = -1;
            for (int f = 0; f < 4; f++) if (p.Layout.PadSlot(f) == element) face = f;
            Assert.AreNotEqual(PadChordReader.FaceWest, face);
            p.Tap(PadChordReader.FaceWest, 2);
            p.RunUntilStarted(1);
            int from = p.D.Log.Count;
            // On the beat of hit 1, RB then the element's own face.
            for (int i = 0; i < 120 && p.D.Model.Rhythm.Active && p.D.Model.Rhythm.TimeToBeat > p.D.Dt * 0.5f; i++) p.Step();
            p.Chord(face, 1);
            p.Run(90);
            Assert.AreEqual(1, p.D.Started, "no attack from RB + " + face + ": " + Describe(p.D, from));
            int denied = 0;
            for (int i = from; i < p.D.Log.Count; i++)
                if (p.D.Log[i].Type == PlayerEventType.ElementSwitchDenied && p.D.Log[i].DenyReason == SwitchDeniedReason.SameElement) denied++;
            Assert.AreEqual(1, denied, "the wheel shakes once");
            Assert.AreEqual(0, p.D.Count(PlayerEventType.DodgeStarted) + p.D.Count(PlayerEventType.Jumped), "the face was the chord's, nothing else");
        }

        // J5-01: no added latency on the pad. Each face press starts its action on the frame it was pressed, exactly like
        // the keyboard, so a dodge on the white danger cue starts (and its i-frames open) when the thumb lands.
        [Test]
        public void FaceActionsStartOnThePressFrame([Values(false, true)] bool punishing)
        {
            var jump = new PadProbe(punishing);
            jump.Run(5);
            int from = jump.D.Frame + 1;
            jump.Tap(PadChordReader.FaceSouth, 10);
            jump.Run(10);
            Assert.AreEqual(from, jump.D.FirstFrame(PlayerEventType.Jumped, from), "jumps on the press frame");

            if (punishing) return;   // Punishing dodges on the release (a hold sprints)
            var dodge = new PadProbe(false);
            dodge.Run(5);
            from = dodge.D.Frame + 1;
            dodge.Tap(PadChordReader.FaceEast, 3);
            dodge.Run(10);
            Assert.AreEqual(from, dodge.D.FirstFrame(PlayerEventType.DodgeStarted, from), "dodges on the press frame");
        }

        // J5-02: a brisk B (stick toward) then X is a dodge then the dodge strike, A then X a jump then the air attack, and
        // B then LB a dodge, never LB + B (Flame Wheel), at every gap a human makes.
        [Test]
        public void BriskSequencesComeOutInOrder([Values(1, 2, 3, 4)] int apart)
        {
            var toward = new Vector2(0f, 1f);

            var dodgeStrike = new PadProbe(false);
            dodgeStrike.Run(5);
            int from = dodgeStrike.D.Log.Count;
            for (int i = 0; i < apart; i++) dodgeStrike.Step(PadChordReader.FaceEast, false, toward);
            for (int i = 0; i < 3; i++) dodgeStrike.Step(PadChordReader.FaceEast, false, toward, PadChordReader.FaceWest);
            dodgeStrike.Run(40, -1, toward);
            string log = Describe(dodgeStrike.D, from);
            Assert.AreEqual(1, dodgeStrike.D.Count(PlayerEventType.DodgeStarted), "B then X: one dodge. " + log);
            Assert.AreEqual(PlayerAttackKind.DodgeStrike, FirstAttack(dodgeStrike.D, from), "B then X: the dodge strike. " + log);

            var air = new PadProbe(false);
            air.Run(5);
            from = air.D.Log.Count;
            for (int i = 0; i < apart; i++) air.Step(PadChordReader.FaceSouth);
            for (int i = 0; i < 3; i++) air.Step(PadChordReader.FaceSouth, false, default, PadChordReader.FaceWest);
            air.Run(20);
            log = Describe(air.D, from);
            Assert.AreEqual(1, air.D.Count(PlayerEventType.Jumped), "A then X: a jump. " + log);
            Assert.AreEqual(PlayerAttackKind.Air, FirstAttack(air.D, from), "A then X: the air attack. " + log);

            // B then LB: through the raw reader (the probe never holds LB), the dodge goes out on B's frame and LB + B
            // never latches as the ability.
            var reader = new PadChordReader();
            var layout = new ElementButtonLayout();
            bool bWas = false, lbWas = false;
            int dodgeFrame = -1, abilities = 0;
            for (int f = 0; f < apart + 6; f++)
            {
                bool lbNow = f >= apart;
                ButtonState n = default(ButtonState), so = default(ButtonState), w = default(ButtonState);
                ButtonState e = ButtonState.From(true, bWas);
                ButtonState lb = ButtonState.From(lbNow, lbWas);
                bWas = true;
                lbWas = lbNow;
                PadChordReader.Result r = reader.Read(ref n, ref e, ref so, ref w, default(ButtonState), lb, 1f / 60f, layout);
                if (e.Pressed && dodgeFrame < 0) dodgeFrame = f;
                if (r.AbilityEast.Pressed) abilities++;
            }
            Assert.AreEqual(0, dodgeFrame, "B then LB: the dodge on B's frame");
            Assert.AreEqual(0, abilities, "B then LB: no Flame Wheel");
        }

        // J5-03: hold RB, then X mid-string, pressed Late (past even the switch strike's wider window: 0.16 s Fluid, 0.11 s
        // Punishing, and before the pause band opens): still the switch strike in Water at the next slot, graded Late (no
        // on-beat bonus), never a plain switch.
        [Test]
        public void LateRbThenXMidStringIsTheSwitchStrike([Values(false, true)] bool punishing, [Values(11, 12, 13)] int lateFrames,
                                                          [Values(1, 3, 5)] int rbFirst)
        {
            var p = new PadProbe(punishing);
            int started = p.D.Started;
            p.Tap(PadChordReader.FaceWest, 2);
            p.RunUntilStarted(started + 1);
            PressOnBeat(p);
            p.RunUntilStarted(started + 2);
            // Wait for hit 2's beat, then lateFrames more, holding RB for the last rbFirst of them.
            for (int i = 0; i < 120 && p.D.Model.Rhythm.Active && p.D.Model.Rhythm.TimeToBeat > p.D.Dt * 0.5f; i++) p.Step();
            int from = p.D.Log.Count;
            for (int i = 0; i < lateFrames; i++) p.Step(-1, i >= lateFrames - rbFirst);
            for (int i = 0; i < 3; i++) p.Step(PadChordReader.FaceWest, true);
            p.Step();
            p.RunUntilStarted(started + 3);
            p.Run(30);
            string log = Describe(p.D, from);
            PlayerEvent third = default(PlayerEvent);
            int seen = 0;
            for (int i = 0; i < p.D.Log.Count; i++)
            {
                if (p.D.Log[i].Type != PlayerEventType.AttackStarted) continue;
                if (++seen == started + 3) { third = p.D.Log[i]; break; }
            }
            Assert.IsTrue(third.IsSwitchStrike, "a switch strike: " + log);
            Assert.AreEqual(ElementId.Water, third.Element, log);
            Assert.AreEqual(2, third.ChainIndex, "hit 3 of the string: " + log);
            Assert.AreEqual(ComboBranch.Main, third.Branch, "the main string, not the pause chain: " + log);
            Assert.AreEqual(BeatGrade.Late, third.Grade, "graded Late (no on-beat bonus): " + log);
        }

        // A switch strike queued mid-string, then a dodge pressed before it ran: the dodge wins the buffer, and the pick still
        // switches (a plain switch), never lost without a trace.
        [Test]
        public void DodgeAfterAQueuedSwitchStrikeStillSwitches([Values(false, true)] bool punishing)
        {
            PlayerDriver d = punishing ? PlayerDriver.Elements(PlayerTuning.CreatePunishing(), ElementLoadout.CreatePunishing()) : PlayerDriver.Elements();
            d.Target(new Vector3(0f, 0f, 1.6f));
            d.Step(Pad.Light);
            d.RunUntilStarted(1);
            d.Select = ElementId.Water;
            d.Step();
            Assert.AreEqual(PlayerCommand.SwitchStrike, d.Model.BufferedCommand, "queued as the switch strike");
            d.Tap(Pad.Dodge, new Vector2(0f, -1f));
            d.Run(40);
            // (Punishing's dodge comes on the release and its buffer is short: it may not get out of the jab, but it still
            // took the buffer's place.)
            if (!punishing) Assert.GreaterOrEqual(d.Count(PlayerEventType.DodgeStarted), 1, "the dodge went out");
            Assert.AreEqual(ElementId.Water, d.Model.ActiveElement, "the pick still switched");
        }

        static PlayerAttackKind FirstAttack(PlayerDriver d, int from)
        {
            for (int i = from; i < d.Log.Count; i++)
            {
                if (d.Log[i].Type == PlayerEventType.AttackStarted) return d.Log[i].AttackKind;
            }
            return PlayerAttackKind.None;
        }

        // Punishing's dodge is on release (a hold past TapHoldThreshold sprints).
        [Test]
        public void PunishingHoldIsTimedFromThePress()
        {
            var p = new PadProbe(true);
            var forward = new Vector2(0f, 1f);
            p.Run(5, -1, forward);
            float threshold = PlayerTuning.CreatePunishing().TapHoldThreshold;
            int holdFrames = (int)System.Math.Ceiling(threshold * 60f) + 1;   // just past the threshold
            p.Run(holdFrames, PadChordReader.FaceEast, forward);
            p.Step(-1, false, forward);
            p.Run(20, -1, forward);
            Assert.AreEqual(0, p.D.Count(PlayerEventType.DodgeStarted), "held past the threshold: a sprint, not a dodge");
        }
    }
}
