using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // The pad's shoulder chords (PadChordReader), frame by frame (Build 05 verify round 2, R2-01; modifier first since
    // round 5): hold RB, then a face button picks an element and fires nothing else (RB and the face on the same frame
    // too); a face pressed before RB does its own job on its own frame; a lone RB tap fires the skill.
    public class PadChordReaderTests
    {
        const float Dt = 1f / 60f;

        // Holds a set of raw buttons per frame and feeds the reader the edges, like PlayerInputReader does.
        sealed class Rig
        {
            public readonly PadChordReader Reader = new PadChordReader();
            public readonly ElementButtonLayout Layout = new ElementButtonLayout();
            bool n, e, s, w, rb, lb;
            public ButtonState North, East, South, West;
            public PadChordReader.Result Last;
            public int Picks, Skills, DodgePresses, LightPresses;
            public ElementId LastPick;
            public int Frame = -1;
            public int FirstDodgeFrame = -1, FirstLightFrame = -1, FirstJumpFrame = -1, FirstZipFrame = -1;

            public void Step(bool north = false, bool east = false, bool south = false, bool west = false, bool rbNow = false, bool lbNow = false)
            {
                North = ButtonState.From(north, n);
                East = ButtonState.From(east, e);
                South = ButtonState.From(south, s);
                West = ButtonState.From(west, w);
                ButtonState rbState = ButtonState.From(rbNow, rb);
                ButtonState lbState = ButtonState.From(lbNow, lb);
                Frame++;
                n = north; e = east; s = south; w = west; rb = rbNow; lb = lbNow;
                Last = Reader.Read(ref North, ref East, ref South, ref West, rbState, lbState, Dt, Layout);
                if (Last.ElementSelect != ElementId.None)
                {
                    Picks++;
                    LastPick = Last.ElementSelect;
                }
                if (Last.Skill.Pressed) Skills++;
                if (East.Pressed) DodgePresses++;
                if (West.Pressed) LightPresses++;
                if (East.Pressed && FirstDodgeFrame < 0) FirstDodgeFrame = Frame;
                if (West.Pressed && FirstLightFrame < 0) FirstLightFrame = Frame;
                if (South.Pressed && FirstJumpFrame < 0) FirstJumpFrame = Frame;
                if (North.Pressed && FirstZipFrame < 0) FirstZipFrame = Frame;
            }
        }

        [Test]
        public void RbAndBSameFrameReleasedAtPointOneSecondPicksOnceAndFiresNoSkill()
        {
            var rig = new Rig();
            for (int i = 0; i < 6; i++) rig.Step(east: true, rbNow: true);   // both down on frame 0, held 0.1 s
            rig.Step();                                                        // both let go
            rig.Step();
            Assert.AreEqual(1, rig.Picks, "one element pick");
            Assert.AreEqual(ElementId.Fire, rig.LastPick, "B is Fire");
            Assert.AreEqual(0, rig.Skills, "the RB release must not fire the ranged skill");
            Assert.AreEqual(0, rig.DodgePresses, "B never reads as a dodge");
        }

        // Modifier first (lead decision, round 5): RB held, then the face, k frames later: only the pick.
        [Test]
        public void RbThenFacePicksOnTheFacesFrame([Values(0, 1, 2, 3)] int face, [Values(1, 3, 6)] int rbFirst)
        {
            var rig = new Rig();
            for (int i = 0; i < rbFirst; i++) rig.Step(rbNow: true);
            for (int i = 0; i < 5; i++)
                rig.Step(north: face == 0, east: face == 1, south: face == 2, west: face == 3, rbNow: true);
            rig.Step();
            rig.Step();
            Assert.AreEqual(1, rig.Picks, "one element pick");
            Assert.AreEqual(rig.Layout.PadSlot(face), rig.LastPick);
            Assert.AreEqual(-1, rig.FirstDodgeFrame, "no dodge");
            Assert.AreEqual(-1, rig.FirstLightFrame, "no attack");
            Assert.AreEqual(-1, rig.FirstJumpFrame, "no jump");
            Assert.AreEqual(-1, rig.FirstZipFrame, "no zip");
            Assert.AreEqual(0, rig.Skills, "no skill on the RB release");
        }

        // J5-01: every face press reaches the rules on the frame it went down (no hold-back on the pad).
        [Test]
        public void FacePressIsReportedOnItsOwnFrame([Values(0, 1, 2, 3)] int face)
        {
            var rig = new Rig();
            rig.Step();
            rig.Step(north: face == 0, east: face == 1, south: face == 2, west: face == 3);
            int reported = face == 0 ? rig.FirstZipFrame : face == 1 ? rig.FirstDodgeFrame : face == 2 ? rig.FirstJumpFrame : rig.FirstLightFrame;
            Assert.AreEqual(1, reported, "reported on the frame it was pressed");
        }

        // Thumb first is not a chord any more: B before RB is a dodge on B's own frame, and RB then picks nothing.
        [Test]
        public void FaceBeforeRbDoesItsOwnJobAndPicksNothing([Values(0, 1, 2, 3)] int face, [Values(1, 2, 4)] int early)
        {
            var rig = new Rig();
            for (int i = 0; i < early; i++) rig.Step(north: face == 0, east: face == 1, south: face == 2, west: face == 3);
            for (int i = 0; i < 4; i++) rig.Step(north: face == 0, east: face == 1, south: face == 2, west: face == 3, rbNow: true);
            rig.Step();
            rig.Step();
            int reported = face == 0 ? rig.FirstZipFrame : face == 1 ? rig.FirstDodgeFrame : face == 2 ? rig.FirstJumpFrame : rig.FirstLightFrame;
            Assert.AreEqual(0, reported, "its own action, at once");
            Assert.AreEqual(0, rig.Picks, "no pick: the chord is RB first");
            Assert.AreEqual(0, rig.Skills, "and RB's release is no stray skill (ChordSkillGuard)");
        }

        // J5-02: presses come out in the order they were made. B then X: the dodge first, then the X (a dodge strike).
        [Test]
        public void PressesKeepTheirOrder([Values(1, 2, 3, 4)] int apart)
        {
            var bThenX = new Rig();
            bThenX.Step(east: true);
            for (int i = 1; i < apart; i++) bThenX.Step(east: true);
            for (int i = 0; i < 3; i++) bThenX.Step(east: true, west: true);
            Assert.AreEqual(0, bThenX.FirstDodgeFrame);
            Assert.AreEqual(apart, bThenX.FirstLightFrame);

            var aThenX = new Rig();
            aThenX.Step(south: true);
            for (int i = 1; i < apart; i++) aThenX.Step(south: true);
            for (int i = 0; i < 3; i++) aThenX.Step(south: true, west: true);
            Assert.AreEqual(0, aThenX.FirstJumpFrame);
            Assert.AreEqual(apart, aThenX.FirstLightFrame);
        }

        // J5-02: B then LB is a dodge, then a guard: never latched as LB + B (Flame Wheel).
        [Test]
        public void BThenLbIsADodgeNotAnAbility([Values(1, 2, 3, 4)] int apart)
        {
            var rig = new Rig();
            int abilities = 0;
            rig.Step(east: true);
            for (int i = 1; i < apart; i++) rig.Step(east: true);
            for (int i = 0; i < 6; i++)
            {
                rig.Step(east: true, lbNow: true);
                if (rig.Last.AbilityEast.Pressed) abilities++;
            }
            Assert.AreEqual(0, rig.FirstDodgeFrame, "the dodge, at once");
            Assert.AreEqual(0, abilities, "no Flame Wheel");
        }

        [Test]
        public void FaceJustTooEarlyForAPickStillFiresNoSkill()
        {
            var rig = new Rig();
            for (int i = 0; i < 6; i++) rig.Step(east: true);                // B 6 frames before RB: its own dodge
            Assert.AreEqual(1, rig.DodgePresses);
            rig.Step(east: true, rbNow: true);
            rig.Step(east: true, rbNow: true);
            rig.Step();
            Assert.AreEqual(0, rig.Picks, "too slow to pick");
            Assert.AreEqual(0, rig.Skills, "but RB's release is no stray skill (BH-03)");
        }

        [Test]
        public void LbChordIsTheAbilityAtOnce()
        {
            var rig = new Rig();
            rig.Step(lbNow: true);
            rig.Step(lbNow: true, north: true);
            Assert.IsTrue(rig.Last.AbilityNorth.Pressed, "LB + Y is the ability at once");
        }

        [Test]
        public void FaceHeldLongBeforeRbStaysItsOwnActionAndTheRbTapFiresTheSkill()
        {
            var rig = new Rig();
            for (int i = 0; i < 10; i++) rig.Step(east: true);               // holding B (sprint) for 0.17 s
            rig.Step(east: true, rbNow: true);
            rig.Step(east: true, rbNow: true);
            rig.Step(east: true);
            Assert.AreEqual(0, rig.Picks, "outside the grace: no pick");
            Assert.AreEqual(1, rig.Skills, "a deliberate RB tap while sprinting is the skill");
            Assert.IsTrue(rig.East.Held, "the sprint goes on");
        }

        [Test]
        public void LoneRbTapFiresSkillOnRelease()
        {
            var rig = new Rig();
            rig.Step(rbNow: true);
            rig.Step(rbNow: true);
            Assert.AreEqual(0, rig.Skills, "not on the press");
            rig.Step();
            Assert.AreEqual(1, rig.Skills);
        }

        // J7-01: RB down frames 0-4, LB pressed on frame 2 and held, RB let go on frame 5: RB was a modifier (the player
        // went for a parry instead of the switch), so its release fires no skill.
        [Test]
        public void RbHeldThenLbTapThenReleaseFiresNoSkill()
        {
            var rig = new Rig();
            rig.Step(rbNow: true);
            rig.Step(rbNow: true);
            rig.Step(rbNow: true, lbNow: true);
            rig.Step(rbNow: true, lbNow: true);
            rig.Step(rbNow: true, lbNow: true);
            rig.Step(lbNow: true);
            rig.Step(lbNow: true);
            rig.Step();
            Assert.AreEqual(0, rig.Skills, "RB was the modifier, not a tap");
            // A later lone RB tap is still the skill.
            rig.Step(rbNow: true);
            rig.Step(rbNow: true);
            rig.Step();
            Assert.AreEqual(1, rig.Skills);
        }

        [Test]
        public void LongRbHoldFiresNothing()
        {
            var rig = new Rig();
            for (int i = 0; i < 40; i++) rig.Step(rbNow: true);
            rig.Step();
            Assert.AreEqual(0, rig.Skills);
        }

        [Test]
        public void RbHeldThenFacePicksAndSwallowsUntilReleased()
        {
            var rig = new Rig();
            rig.Step(rbNow: true);
            rig.Step(rbNow: true, north: true);
            Assert.AreEqual(ElementId.Air, rig.LastPick, "Y is Air");
            Assert.IsFalse(rig.North.Pressed);
            rig.Step(north: true);                                             // RB let go, Y still held: still swallowed
            Assert.IsFalse(rig.North.Held);
            Assert.AreEqual(0, rig.Skills);
            rig.Step();
            rig.Step(north: true);                                             // a fresh Y press is a zip again, at once
            Assert.IsTrue(rig.North.Pressed, "reported as a zip on its own frame");
        }

        [Test]
        public void LbChordIsAnAbilityAndNotAnElementPick()
        {
            var rig = new Rig();
            rig.Step(lbNow: true);
            rig.Step(lbNow: true, west: true);
            Assert.IsTrue(rig.Last.Heavy.Pressed, "LB + X = Heavy");
            Assert.IsFalse(rig.West.Pressed);
            rig.Step(lbNow: true, west: true, rbNow: true);                    // RB arriving during a held ability
            Assert.AreEqual(0, rig.Picks, "a face taken by an LB chord is not upgraded");
        }

        [Test]
        public void SkillCannotReplaceAQueuedSwitchStrike()
        {
            var buffer = new InputBuffer();
            buffer.Push(PlayerCommand.SwitchStrike, 0.0);
            buffer.Lock();
            buffer.Push(PlayerCommand.Skill, 0.05);
            Assert.AreEqual(PlayerCommand.SwitchStrike, buffer.Command);
            buffer.Push(PlayerCommand.ZipStrike, 0.06);
            Assert.AreEqual(PlayerCommand.SwitchStrike, buffer.Command);
            buffer.Push(PlayerCommand.Dodge, 0.07);
            Assert.AreEqual(PlayerCommand.Dodge, buffer.Command, "a dodge still gets you out");
        }
    }
}
