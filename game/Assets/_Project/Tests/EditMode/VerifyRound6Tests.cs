using System.Collections.Generic;
using NUnit.Framework;
using VaatusRevenge.Core;
using Vector3 = System.Numerics.Vector3;

namespace VaatusRevenge.Tests
{
    // Build 05 verify round 6: the pure-C# halves of the fixes (J6-03 launcher hold, J6-04 air switch after the air
    // finisher, J6-05 on-beat feedback only once the grade is final). The animation half (J6-01, J6-02) is in
    // AnimationTests.
    public class VerifyRound6Tests
    {
        static PlayerDriver Make(bool punishing, ElementId element)
        {
            PlayerDriver d = punishing
                ? PlayerDriver.Elements(PlayerTuning.CreatePunishing(), ElementLoadout.CreatePunishing())
                : PlayerDriver.Elements();
            Assert.IsTrue(d.Model.SetElementAtRest(element), element + " learned");
            d.Target(new Vector3(0f, 0f, 1.6f));
            return d;
        }

        static string Name(bool punishing, ElementId element) => (punishing ? "Punishing " : "Fluid ") + element;

        // Steps until the running string move's beat (the frame that crosses it), without pressing.
        static void WaitForBeat(PlayerDriver d)
        {
            for (int i = 0; i < 120 && d.Model.Rhythm.Active && d.Model.Rhythm.TimeToBeat > d.Dt * 0.5f; i++) d.RunLanding(1);
        }

        // ---------------------------------------------------------------- J6-03

        // Holding the finisher's X (0.25 s and more) used to replace the finisher with the launcher 3 frames in, losing its
        // perfect-string bonus. The hold only works on hits before the finisher: the finisher plays and lands, and the
        // perfect string is raised, in every element on both presets.
        [Test]
        public void HoldingTheFinisherPressStillLandsTheFinisher([Values(false, true)] bool punishing, [Values(15, 24, 40)] int holdFrames)
        {
            for (ElementId element = ElementId.Fire; element <= ElementId.Air; element++)
            {
                PlayerDriver d = Make(punishing, element);
                string context = Name(punishing, element) + " hold " + holdFrames + "f";
                MoveData[] chain = d.Model.MoveSet.LightChain;
                MoveData finisher = chain[chain.Length - 1];
                d.Step(Pad.Light);
                d.Step();
                for (int i = 1; i < chain.Length - 1; i++)
                {
                    d.RunUntilStarted(i);                  // hit i is running: press on its beat for hit i + 1
                    WaitForBeat(d);
                    d.Step(Pad.Light);
                    d.Step();
                    d.LandStrikes();
                }
                d.RunUntilStarted(chain.Length - 1);       // the hit before the finisher
                WaitForBeat(d);
                int from = d.Log.Count;
                for (int i = 0; i < holdFrames; i++)
                {
                    d.Step(Pad.Light);
                    d.LandStrikes();
                }
                d.RunLanding(60);

                int finishers = 0, launchers = 0, perfect = 0;
                bool finisherStruck = false;
                for (int i = from; i < d.Log.Count; i++)
                {
                    PlayerEvent e = d.Log[i];
                    if (e.Type == PlayerEventType.AttackStarted && e.AttackKind == PlayerAttackKind.Light && e.IsFinisher) finishers++;
                    if (e.Type == PlayerEventType.AttackStarted && e.AttackKind == PlayerAttackKind.Launcher) launchers++;
                    if (e.Type == PlayerEventType.PerfectString) perfect++;
                    if (e.Type == PlayerEventType.AttackActiveStart && e.Move == finisher) finisherStruck = true;
                }
                Assert.AreEqual(1, finishers, context + ": the finisher started");
                Assert.AreEqual(0, launchers, context + ": the held finisher press never becomes the launcher");
                Assert.IsTrue(finisherStruck, context + ": the finisher reached its active frames");
                Assert.AreEqual(1, perfect, context + ": the on-beat string still earns its perfect-string finisher");
            }
        }

        // A hit-3 press made on the beat (buffered during hit 2) and kept down: hit 3 starts, and the launcher replaces it
        // only once the press has been held LauncherHoldTime from when hit 3 began, never earlier.
        [Test]
        public void ABufferedPressHeldGivesThatHitThenTheLauncher([Values(false, true)] bool punishing)
        {
            for (ElementId element = ElementId.Fire; element <= ElementId.Air; element++)
            {
                PlayerDriver d = Make(punishing, element);
                string context = Name(punishing, element);
                float hold = d.Model.MoveSet.Aerial.LauncherHoldTime;
                d.Step(Pad.Light);
                d.Step();
                d.RunUntilStarted(1);
                WaitForBeat(d);
                d.Step(Pad.Light);
                d.Step();
                d.RunUntilStarted(2);
                WaitForBeat(d);
                // The hit-3 press, on the beat of hit 2, held ~0.5 s.
                int launcherFrame = -1, hit3Frame = -1;
                for (int i = 0; i < 30; i++)
                {
                    d.Step(Pad.Light);
                    d.LandStrikes();
                    PlayerEvent last = d.LastStarted;
                    if (hit3Frame < 0 && last.AttackKind == PlayerAttackKind.Light && last.ChainIndex == 2) hit3Frame = d.Frame;
                    if (launcherFrame < 0 && last.AttackKind == PlayerAttackKind.Launcher) launcherFrame = d.Frame;
                }
                Assert.GreaterOrEqual(hit3Frame, 0, context + ": hit 3 started");
                Assert.GreaterOrEqual(launcherFrame, 0, context + ": then the launcher");
                Assert.GreaterOrEqual((launcherFrame - hit3Frame) * d.Dt, hold - d.Dt - 1e-4f,
                    context + ": the hold is timed from hit 3's start, not from the buffered press");
            }
        }

        // ---------------------------------------------------------------- J6-04

        // Hold RB + a face while still airborne after the air string's finisher: the air string is spent, so the pick is a
        // plain switch at once (in the air), on both presets, and nothing turns it into a ground hit on landing.
        [Test]
        public void SwitchAfterTheAirFinisherIsAPlainSwitchInTheAir([Values(false, true)] bool punishing)
        {
            var failures = new List<string>();
            for (ElementId element = ElementId.Fire; element <= ElementId.Air; element++)
            {
                ElementId target = element == ElementId.Water ? ElementId.Fire : ElementId.Water;
                for (int wait = 0; wait <= 12; wait += 2)
                {
                    PlayerDriver d = Make(punishing, element);
                    string context = Name(punishing, element) + " -> " + target + " " + wait + "f after the air finisher";
                    d.Tap(Pad.Jump);
                    d.Run(4);
                    MoveData[] air = d.Model.MoveSet.AirChain;
                    d.Step(Pad.Light);
                    d.Step();
                    for (int i = 1; i < air.Length; i++)
                    {
                        d.RunUntilStarted(i);
                        WaitForBeat(d);
                        d.Step(Pad.Light);
                        d.Step();
                    }
                    d.RunUntilStarted(air.Length);
                    Assert.IsTrue(d.LastStarted.IsFinisher || d.LastStarted.ChainIndex == air.Length - 1, context + ": the air finisher runs");
                    d.Run(wait);
                    if (d.World.Grounded) continue;            // landed already: not this case
                    int from = d.Log.Count;
                    int started = d.Started;
                    d.Select = target;
                    d.Step();
                    bool airborneAtSwitch = false;
                    int switches = 0, strikeSwitches = 0;
                    for (int f = 0; f < 90; f++)
                    {
                        for (int i = from; i < d.Log.Count; i++)
                        {
                            PlayerEvent e = d.Log[i];
                            if (e.Type != PlayerEventType.ElementSwitched) continue;
                            switches++;
                            if (e.IsSwitchStrike) strikeSwitches++;
                            if (e.InAir) airborneAtSwitch = true;
                        }
                        from = d.Log.Count;
                        d.Step();
                    }
                    if (switches != 1) failures.Add(context + ": " + switches + " switches");
                    if (strikeSwitches != 0) failures.Add(context + ": a switch strike after a spent air string");
                    if (!airborneAtSwitch) failures.Add(context + ": switched on the ground, not at the press");
                    if (d.Started != started) failures.Add(context + ": an attack started (" + d.LastStarted.AttackKind + ")");
                    if (d.Model.ActiveElement != target) failures.Add(context + ": element " + d.Model.ActiveElement);
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // ---------------------------------------------------------------- J6-05

        // A masher's on-beat presses are mostly downgraded to Mashed by the next press. The chime, rumble tick and HUD
        // burst play on BeatConfirmed, which only comes when a move graded OnBeat starts, so none of them is ever for a
        // press that ends up a mash; and the HUD's confirmed streak is 0 whenever a mash has just broken it.
        [Test]
        public void MasherNeverGetsAChimeForAPressThatBecomesAMash([Values(false, true)] bool punishing, [Values(6, 8, 10)] int every)
        {
            PlayerDriver d = Make(punishing, ElementId.Fire);
            int onBeatAtPress = 0, mashed = 0, confirmed = 0, onBeatStarts = 0;
            for (int f = 0; f < 600; f++)
            {
                int from = d.Log.Count;
                d.Step(f % every == 0 ? Pad.Light : Pad.None);
                d.LandStrikes();
                bool mashedNow = false;
                for (int i = from; i < d.Log.Count; i++)
                {
                    PlayerEvent e = d.Log[i];
                    if (e.Type == PlayerEventType.BeatJudged && e.Grade == BeatGrade.OnBeat) onBeatAtPress++;
                    if (e.Type == PlayerEventType.BeatJudged && e.Grade == BeatGrade.Mashed)
                    {
                        mashed++;
                        mashedNow = true;
                    }
                    if (e.Type == PlayerEventType.AttackStarted && e.Grade == BeatGrade.OnBeat) onBeatStarts++;
                    if (e.Type == PlayerEventType.BeatConfirmed)
                    {
                        confirmed++;
                        Assert.AreEqual(BeatGrade.OnBeat, e.Grade);
                        // The move it confirms started this frame with an OnBeat grade.
                        PlayerEvent started = d.LastStarted;
                        Assert.AreEqual(e.MoveInstanceId, started.MoveInstanceId, "BeatConfirmed belongs to the move just started");
                        Assert.AreEqual(BeatGrade.OnBeat, started.Grade, "only an on-beat move is confirmed");
                    }
                }
                if (mashedNow) Assert.AreEqual(0, d.Model.Rhythm.ConfirmedStreak, "a mash leaves no ON BEAT pip lit");
            }
            Assert.Greater(mashed, 0, "the masher's on-beat presses do get downgraded (the case this guards)");
            Assert.AreEqual(onBeatStarts, confirmed, "one chime per move started on the beat");
            Assert.LessOrEqual(confirmed, onBeatAtPress - mashed, "chimes never include a press that became a mash");
        }

        // The rhythm player still hears every on-beat press: four on-beat follow-ups = four confirmations, and the HUD's
        // confirmed streak reaches 4 as the finisher starts.
        [Test]
        public void OnBeatStringConfirmsEveryFollowUp([Values(false, true)] bool punishing)
        {
            for (ElementId element = ElementId.Fire; element <= ElementId.Air; element++)
            {
                PlayerDriver d = Make(punishing, element);
                d.OnBeatString(4);
                Assert.AreEqual(4, d.Count(PlayerEventType.BeatConfirmed), Name(punishing, element));
                Assert.AreEqual(4, d.Model.Rhythm.ConfirmedStreak, Name(punishing, element));
            }
        }
    }
}
