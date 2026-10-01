using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // The pad's shoulder chords (PadChordReader), frame by frame (Build 05 verify round 2, R2-01): RB + a face button
    // picks an element and fires nothing else, in whatever order a human's two thumbs land; a lone RB tap fires the skill.
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
            public PlayerCommand LastRetract;

            public void Step(bool north = false, bool east = false, bool south = false, bool west = false, bool rbNow = false, bool lbNow = false)
            {
                North = ButtonState.From(north, n);
                East = ButtonState.From(east, e);
                South = ButtonState.From(south, s);
                West = ButtonState.From(west, w);
                ButtonState rbState = ButtonState.From(rbNow, rb);
                ButtonState lbState = ButtonState.From(lbNow, lb);
                n = north; e = east; s = south; w = west; rb = rbNow; lb = lbNow;
                Last = Reader.Read(ref North, ref East, ref South, ref West, rbState, lbState, Dt, Layout);
                if (Last.ElementSelect != ElementId.None)
                {
                    Picks++;
                    LastPick = Last.ElementSelect;
                    LastRetract = Last.RetractPress;
                }
                if (Last.Skill.Pressed) Skills++;
                if (East.Pressed) DodgePresses++;
                if (West.Pressed) LightPresses++;
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
            Assert.AreEqual(PlayerCommand.None, rig.LastRetract, "nothing to take back: B never acted");
            Assert.AreEqual(0, rig.Skills, "the RB release must not fire the ranged skill");
            Assert.AreEqual(0, rig.DodgePresses, "B never reads as a dodge");
        }

        [Test]
        public void BOneFrameBeforeRbIsUpgradedToThePick()
        {
            var rig = new Rig();
            rig.Step(east: true);                                              // B first: its dodge press goes out
            Assert.AreEqual(1, rig.DodgePresses);
            rig.Step(east: true, rbNow: true);                                 // RB one frame later
            Assert.AreEqual(1, rig.Picks);
            Assert.AreEqual(ElementId.Fire, rig.LastPick);
            Assert.AreEqual(PlayerCommand.Dodge, rig.LastRetract, "the dodge press is named for the model to take back");
            Assert.IsFalse(rig.East.Held, "B is swallowed from RB's frame on");
            for (int i = 0; i < 4; i++) rig.Step(east: true, rbNow: true);
            rig.Step();
            rig.Step();
            Assert.AreEqual(0, rig.Skills, "no skill on the RB release");
            Assert.AreEqual(1, rig.Picks);
        }

        [Test]
        public void XTwoFramesBeforeRbIsUpgradedAndNamesTheLight()
        {
            var rig = new Rig();
            rig.Step(west: true);
            rig.Step(west: true);
            rig.Step(west: true, rbNow: true);
            Assert.AreEqual(ElementId.Water, rig.LastPick, "X is Water");
            Assert.AreEqual(PlayerCommand.Light, rig.LastRetract);
            rig.Step();
            Assert.AreEqual(0, rig.Skills);
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
            rig.Step(north: true);                                             // a fresh Y press is a zip again
            Assert.IsTrue(rig.North.Pressed);
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

        [Test]
        public void ModelUpgradesAQueuedXIntoTheSwitchStrike()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Step(Pad.Light);
            d.PressOnBeat();                                                  // X on the beat: queued as hit 2
            Assert.AreEqual(PlayerCommand.Light, d.Model.BufferedCommand);
            Assert.IsTrue(d.Model.BufferedCommandQueued);
            int judged = d.Count(PlayerEventType.BeatJudged);
            d.Select = ElementId.Water;                                       // RB a frame later: that X was RB + X
            d.Retract = PlayerCommand.Light;
            d.Step();
            Assert.AreEqual(PlayerCommand.SwitchStrike, d.Model.BufferedCommand, "the queued X becomes the switch strike");
            Assert.IsTrue(d.Model.BufferedCommandQueued, "keeps its place");
            Assert.AreEqual(judged, d.Count(PlayerEventType.BeatJudged), "not judged again (no Mashed)");
            d.RunUntilStarted(2);
            Assert.AreEqual(ElementId.Water, d.LastStarted.Element);
            Assert.IsTrue(d.LastStarted.IsSwitchStrike);
            Assert.AreEqual(1, d.LastStarted.ChainIndex);
        }

        [Test]
        public void ModelDropsADodgeUpgradedIntoAPick()
        {
            PlayerDriver d = PlayerDriver.Elements(PlayerTuning.CreatePunishing(), ElementLoadout.CreatePunishing());
            d.Step(Pad.Dodge);                                                // B (Punishing: the dodge waits for the release)
            d.Select = ElementId.Water;
            d.Retract = PlayerCommand.Dodge;
            d.Step();                                                          // RB a frame later; B swallowed
            d.Run(60);
            Assert.AreEqual(0, d.Count(PlayerEventType.DodgeStarted), "the swallowed B never dodges");
            Assert.AreEqual(ElementId.Water, d.Model.ActiveElement, "the pick still switched");
        }
    }
}
