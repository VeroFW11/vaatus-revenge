using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // The pause branch (Build 05, spec 2.4.3): X X, wait, X. After the 2nd hit with no press, a press after its combo
    // window (until PauseGrace past its end) starts the pause chain; mashing never does, and a dodge in between turns the
    // press into a plain continue.
    public class CombatPauseBranchTests
    {
        // X, X on the beat; then waits (no press) until 'afterWindowEnd' seconds past hit 2's combo window.
        static PlayerDriver XXThenWait(float afterWindowEnd)
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.OnBeatString(1);
            PlayerEvent second = d.LastStarted;
            double start = d.Model.Clock;
            double until = start + (second.Move.ComboWindowEnd + afterWindowEnd) / second.PlaybackRate;
            d.RunUntil(x => x.Model.Clock >= until, 120);
            return d;
        }

        [Test]
        public void XXPauseXEntersPauseChain()
        {
            PlayerDriver d = XXThenWait(0.03f);
            Assert.AreEqual(2, d.Started, "nothing pressed while waiting");
            d.Step(Pad.Light);
            Assert.AreEqual(BeatGrade.Pause, d.LastOf(PlayerEventType.BeatJudged).Grade);
            d.RunUntilStarted(3);
            PlayerEvent sweep = d.LastStarted;
            Assert.AreEqual(ComboBranch.Pause, sweep.Branch);
            Assert.AreEqual(0, sweep.ChainIndex);
            Assert.AreSame(d.Model.MoveSet.PauseChain[0], sweep.Move);
            Assert.AreEqual(BeatGrade.Pause, sweep.Grade);
            Assert.AreEqual(1f, sweep.PlaybackRate, 1e-6f);

            d.PressOnBeat();                                     // presses inside the pause chain are judged normally
            d.RunUntilStarted(4);
            PlayerEvent rising = d.LastStarted;
            Assert.AreEqual(ComboBranch.Pause, rising.Branch);
            Assert.AreEqual(1, rising.ChainIndex);
            Assert.IsTrue(rising.IsFinisher);
            Assert.AreEqual(BeatGrade.OnBeat, rising.Grade);
            Assert.AreEqual(1, d.Count(PlayerEventType.PerfectString), "Pause doesn't break a perfect string");

            d.PressOnBeat();                                     // after the pause chain: back to the first hit
            d.RunUntilStarted(5);
            Assert.AreEqual(ComboBranch.Main, d.LastStarted.Branch);
            Assert.AreEqual(0, d.LastStarted.ChainIndex);
        }

        // Build 05 verify S-07 and J-02: in every element the presses that take the pause branch form one unbroken band (no
        // single frame in it that plays main hit 3), and RhythmView.PauseReady (the HUD's "now" cue) lights exactly over it.
        [Test]
        public void PauseBandHasNoHolesAndTheCueMatchesIt()
        {
            foreach (ElementId element in new[] { ElementId.Fire, ElementId.Water, ElementId.Earth, ElementId.Air })
            {
                var pause = new System.Collections.Generic.List<int>();
                var ready = new System.Collections.Generic.List<int>();
                for (int wait = 0; wait < 90; wait++)
                {
                    PlayerDriver d = PlayerDriver.Elements();
                    if (element != ElementId.Fire)
                    {
                        d.Select = element;
                        d.Step();
                    }
                    d.OnBeatString(1);
                    d.Run(wait);
                    if (d.Model.Rhythm.PauseReady) ready.Add(wait);
                    if (d.Model.State != PlayerState.Locomotion && d.Model.State != PlayerState.Attacking) break;
                    d.Step(Pad.Light);
                    d.RunUntil(x => x.Started >= 3 || x.Model.State == PlayerState.Locomotion && x.Frame > 400, 60);
                    if (d.Started >= 3 && d.LastStarted.Branch == ComboBranch.Pause) pause.Add(wait);
                }
                Assert.IsNotEmpty(pause, element + ": a pause band");
                Assert.AreEqual(pause.Count, pause[pause.Count - 1] - pause[0] + 1, element + ": the band has a hole: " + string.Join(",", pause));
                Assert.IsNotEmpty(ready, element + ": the cue lights");
                Assert.LessOrEqual(System.Math.Abs(ready[0] - pause[0]), 1, element + ": the cue lights as the band opens");
                Assert.LessOrEqual(System.Math.Abs(ready[ready.Count - 1] - pause[pause.Count - 1]), 1, element + ": and goes out as it closes");
                Assert.AreEqual(ready.Count, ready[ready.Count - 1] - ready[0] + 1, element + ": the cue stays lit through the band");
            }
        }

        [Test]
        public void PauseWorksAfterTheMoveHasEnded()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.OnBeatString(1);
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 90);
            d.Step(Pad.Light);                                   // inside PauseGrace after the end of hit 2
            Assert.AreEqual(ComboBranch.Pause, d.LastStarted.Branch);
        }

        [Test]
        public void PauseOnlyAfterPauseAfterIndex()
        {
            foreach (int followUps in new[] { 0, 2, 3 })
            {
                PlayerDriver d = PlayerDriver.Elements();
                d.OnBeatString(followUps);
                PlayerEvent last = d.LastStarted;
                double until = d.Model.Clock + (last.Move.ComboWindowEnd + 0.03f) / last.PlaybackRate;
                d.RunUntil(x => x.Model.Clock >= until, 120);
                d.Step(Pad.Light);
                d.RunUntilStarted(followUps + 2, 90);
                Assert.AreEqual(ComboBranch.Main, d.LastStarted.Branch, "no pause after hit " + (followUps + 1));
                Assert.AreEqual(0, d.Count(PlayerEventType.BeatJudged) - followUps, "no Pause judgement");
            }
        }

        [Test]
        public void NoPauseAfterFinisher()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.OnBeatString(4);
            Assert.IsTrue(d.LastStarted.IsFinisher);
            PlayerEvent last = d.LastStarted;
            double until = d.Model.Clock + (last.Move.ComboWindowEnd + 0.01f) / last.PlaybackRate;
            d.RunUntil(x => x.Model.Clock >= until, 120);
            d.Step(Pad.Light);
            d.RunUntilStarted(6, 90);
            Assert.AreEqual(ComboBranch.Main, d.LastStarted.Branch);
            Assert.AreEqual(0, d.LastStarted.ChainIndex);
        }

        [Test]
        public void PressAfterPauseBandRestarts()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.OnBeatString(1);
            PlayerEvent second = d.LastStarted;
            double until = d.Model.Clock + (second.Move.TotalDuration + d.Model.Tuning.Rhythm.PauseGrace + 0.03f) / second.PlaybackRate;
            d.RunUntil(x => x.Model.Clock >= until, 120);
            d.Step(Pad.Light);
            Assert.AreEqual(ComboBranch.Main, d.LastStarted.Branch);
            Assert.AreEqual(0, d.LastStarted.ChainIndex, "too long: a fresh string");
        }

        [Test]
        public void DodgeBetweenTurnsPauseIntoContinue()
        {
            PlayerDriver d = XXThenWait(0.03f);
            d.Step(Pad.Dodge, new Vector2(-1f, 0f));
            Assert.AreEqual(PlayerState.Dodging, d.Model.State);
            d.Step(Pad.Light, new Vector2(-1f, 0f));
            d.RunUntilStarted(3, 60);
            Assert.AreEqual(ComboBranch.Main, d.LastStarted.Branch, "a dodge in between: a continue, never a pause");
            Assert.AreEqual(2, d.LastStarted.ChainIndex);
        }

        [Test]
        public void MashedPressesNeverTriggerPause()
        {
            foreach (int every in new[] { 3, 4, 5, 6, 8, 10 })
            {
                PlayerTuning t = PlayerTuning.CreateFluid();
                t.StaminaRegen = 1000f;
                PlayerDriver d = PlayerDriver.Elements(t);
                for (int f = 0; f < 600; f++) d.Step(f % every == 0 ? Pad.Light : Pad.None);
                foreach (PlayerEvent e in d.All(PlayerEventType.AttackStarted))
                    Assert.AreNotEqual(ComboBranch.Pause, e.Branch, "mashing every " + every + " frames");
                Assert.Greater(d.Started, 10);
            }
        }
    }
}
