using System;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // The combat tutorial's rules (Build 05, spec 5 D and 6.4). Two kinds of test:
    //   * synthetic: hand-made events per goal, checking it passes, doesn't pass on a near miss, forgets a combo that
    //     ended, and that skipping moves on without passing;
    //   * integration: every step (1-11, with 8b and 8c) completed by a real PlayerCombatModel on the default Fluid
    //     tuning, driven by scripted input frames, so a change to the combat rules that breaks a lesson fails here.
    public class TutorialTrackerTests
    {
        const float Frame = 1f / 60f;

        // ---------------------------------------------------------------- synthetic helpers

        static TutorialTracker At(string id)
        {
            var t = new TutorialTracker(TutorialScript.CreateDefault());
            t.Start();
            while (t.IsRunning && t.Step.Id != id) t.Skip();
            Assert.IsTrue(t.IsRunning, "the default script has a step " + id);
            return t;
        }

        static void Tick(TutorialTracker t, float seconds = Frame, TutorialSnapshot snapshot = default)
        {
            t.Tick(seconds, seconds, snapshot);
        }

        static int nextInstance = 1000;

        static PlayerEvent Hit(int count, ComboBranch branch = ComboBranch.Main, bool finisher = false,
            ElementId element = ElementId.Fire, PlayerAttackKind kind = PlayerAttackKind.Light, int chainIndex = 0, bool inAir = false)
        {
            int id = ++nextInstance;
            return new PlayerEvent
            {
                Type = PlayerEventType.ComboHit, Count = count, Branch = branch, IsFinisher = finisher, Element = element,
                AttackKind = kind, ChainIndex = chainIndex, InAir = inAir, AttackId = id, MoveInstanceId = id
            };
        }

        static PlayerEvent Of(PlayerEventType type)
        {
            return new PlayerEvent { Type = type };
        }

        static PlayerEvent Beat(BeatGrade grade)
        {
            return new PlayerEvent { Type = PlayerEventType.BeatJudged, Grade = grade };
        }

        static PlayerEvent StringStart()
        {
            return new PlayerEvent { Type = PlayerEventType.AttackStarted, AttackKind = PlayerAttackKind.Light, Branch = ComboBranch.Main };
        }

        static PlayerEvent Dodge(DodgeKind kind = DodgeKind.EvadeOut)
        {
            return new PlayerEvent { Type = PlayerEventType.DodgeStarted, DodgeKind = kind };
        }

        static PlayerEvent Switched(ElementId element, bool strike = true)
        {
            return new PlayerEvent { Type = PlayerEventType.ElementSwitched, Element = element, IsSwitchStrike = strike };
        }

        static PlayerEvent Mix(int level)
        {
            return new PlayerEvent { Type = PlayerEventType.MixChanged, Count = level };
        }

        static PlayerEvent Ended(int count)
        {
            return new PlayerEvent { Type = PlayerEventType.ComboEnded, Count = count };
        }

        static void Feed(TutorialTracker t, params PlayerEvent[] events)
        {
            foreach (PlayerEvent e in events) t.OnEvent(e);
        }

        static void AssertPassed(TutorialTracker t, string id)
        {
            Assert.AreEqual(id, t.Step.Id, "still showing the step during its flash");
            Assert.IsTrue(t.InSuccessPause, "step " + id + " passed");
            Assert.AreEqual(t.Required, t.Progress);
        }

        // ---------------------------------------------------------------- the script

        [Test]
        public void DefaultScriptHasElevenStepsWithThreeSwitchParts()
        {
            TutorialScript script = TutorialScript.CreateDefault();
            string[] ids = { "1", "2", "3", "4", "5", "6", "7", "8", "8b", "8c", "9", "10", "11" };
            Assert.AreEqual(ids.Length, script.StepCount);
            for (int i = 0; i < ids.Length; i++) Assert.AreEqual(ids[i], script.Steps[i].Id);
            Assert.AreEqual("11", script.DisplayTotal);
            Assert.AreEqual(TutorialScript.CurrentDataVersion, script.DataVersion);
            Assert.AreEqual(0, new TutorialScript().DataVersion, "an asset saved before the version existed reads 0 (stale)");
        }

        [Test]
        public void SwitchStepsUseTheColourMatchedLayout()
        {
            // Spec 8.1: hold RB + B (red) Fire, X (blue) Water, A (green) Earth, Y (yellow) Air; keys 1-4 Fire, Water, Earth, Air.
            var layout = new ElementButtonLayout();
            TutorialScript script = TutorialScript.CreateDefault();
            string[] faces = { "{Y}", "{B}", "{A}", "{X}" };
            foreach (TutorialStepData step in script.Steps)
            {
                if (step.Goal != TutorialGoal.SwitchStrikeTo) continue;
                string chord = "{RB}+" + faces[layout.PadSlotOf(step.Element)];
                StringAssert.Contains(chord, step.Prompt, step.Id + " teaches the element's own button");
                string key = ((int)step.Element).ToString();
                Assert.AreEqual(step.Element, layout.KeySlot((int)step.Element - 1));
                StringAssert.Contains(key, step.KeyboardPrompt, step.Id + " names the number key on the keyboard");
            }
        }

        [Test]
        public void PlayerFacingTextHasNoEmDashes()
        {
            TutorialScript script = TutorialScript.CreateDefault();
            foreach (TutorialStepData step in script.Steps)
            {
                foreach (string text in new[] { step.Title, step.Prompt, step.KeyboardPrompt, step.Hint, step.KeyboardHint,
                                                step.AlreadyInElementHint, step.KeyboardAlreadyInElementHint })
                {
                    Assert.IsFalse((text ?? "").Contains("\u2014"), step.Id + ": " + text);
                }
            }
        }

        // ---------------------------------------------------------------- synthetic: per goal

        [Test]
        public void TimedStepPassesAfterItsDurationThenAdvancesAfterThePause()
        {
            TutorialTracker t = At("1");
            Tick(t, 3.9f);
            Assert.IsFalse(t.InSuccessPause);
            Assert.AreEqual(0.975f, t.AttemptProgress01, 1e-3f);
            Tick(t, 0.2f);
            AssertPassed(t, "1");
            Assert.AreEqual(1, t.PassSerial);
            Tick(t, t.Script.SuccessPause + 0.01f);
            Assert.AreEqual("2", t.Step.Id);
            Assert.AreEqual(0, t.Progress);
        }

        [Test]
        public void OnBeatFinisherNeedsEnoughOnBeatPressesInTheString()
        {
            TutorialTracker t = At("2");
            Feed(t, StringStart(), Hit(1), Beat(BeatGrade.OnBeat), Hit(2), Beat(BeatGrade.OnBeat), Hit(3), Beat(BeatGrade.Late),
                 Hit(4), Beat(BeatGrade.OnBeat), Beat(BeatGrade.Mashed), Hit(5, ComboBranch.Main, true));
            Assert.IsFalse(t.InSuccessPause, "fail: the mash took one back, only 2 on the beat");
            Feed(t, Beat(BeatGrade.OnBeat), StringStart());
            Assert.AreEqual(0f, t.AttemptProgress01, "a new string starts the count again");
            Feed(t, Hit(6), Beat(BeatGrade.OnBeat), Hit(7), Beat(BeatGrade.OnBeat), Hit(8), Beat(BeatGrade.OnBeat), Hit(9),
                 Hit(10, ComboBranch.Pause, true));
            Assert.IsFalse(t.InSuccessPause, "a pause finisher isn't the main finisher");
            Feed(t, Hit(11, ComboBranch.Main, true));
            AssertPassed(t, "2");
        }

        [Test]
        public void OnBeatCountForgottenWhenTheComboEnds()
        {
            TutorialTracker t = At("2");
            Feed(t, StringStart(), Beat(BeatGrade.OnBeat), Beat(BeatGrade.OnBeat), Beat(BeatGrade.OnBeat), Ended(3),
                 Hit(1, ComboBranch.Main, true));
            Assert.IsFalse(t.InSuccessPause, "combo reset");
        }

        [Test]
        public void PauseFinisherCountsEachMoveOnce()
        {
            TutorialTracker t = At("3");
            PlayerEvent finisher = Hit(4, ComboBranch.Pause, true);
            Feed(t, Hit(3, ComboBranch.Pause), finisher, finisher);
            Assert.AreEqual(1, t.Progress, "a multi-hit finisher's sub-hits count once");
            Feed(t, Hit(5, ComboBranch.Main, true));
            Assert.AreEqual(1, t.Progress, "fail: the main finisher");
            Feed(t, Hit(8, ComboBranch.Pause, true));
            AssertPassed(t, "3");
        }

        [Test]
        public void DodgeKeepsComboNeedsAComboAndTheSameCombo()
        {
            TutorialTracker t = At("4");
            Feed(t, Hit(1), Dodge(), Hit(2));
            Assert.AreEqual(0, t.Progress, "fail: a one-hit combo isn't a combo to keep");
            Feed(t, Dodge(), Ended(2), Hit(1));
            Assert.AreEqual(0, t.Progress, "combo reset: the dodge's combo ended");
            Feed(t, Hit(2), Dodge(), Hit(3));
            Assert.AreEqual(1, t.Progress);
            Feed(t, Dodge(), Hit(4));
            AssertPassed(t, "4");
        }

        [Test]
        public void SlipInMustStartFromRangeAndHitInTime()
        {
            TutorialTracker t = At("5");
            TutorialStepData step = t.Step;
            var far = new TutorialSnapshot { TargetDistance = step.MinStartDistance + 0.5f };
            var near = new TutorialSnapshot { TargetDistance = step.MinStartDistance - 0.5f };
            Tick(t, Frame, near);
            Feed(t, Dodge(DodgeKind.SlipIn), Hit(1));
            Assert.AreEqual(0, t.Progress, "fail: started too close");
            Tick(t, Frame, far);
            Feed(t, Dodge(DodgeKind.EvadeOut), Hit(2));
            Assert.AreEqual(0, t.Progress, "fail: not a slip-in");
            Tick(t, Frame, far);
            Feed(t, Dodge(DodgeKind.SlipIn));
            Tick(t, step.FollowUpWindow + 0.05f, near);
            Feed(t, Hit(3));
            Assert.AreEqual(0, t.Progress, "fail: the hit came too late");
            Tick(t, Frame, far);
            Feed(t, Dodge(DodgeKind.SlipIn));
            Tick(t, step.FollowUpWindow - 0.05f, near);
            Feed(t, Hit(4));
            Assert.AreEqual(1, t.Progress);
        }

        [Test]
        public void DodgeStrikeStepCountsDodgeStrikes()
        {
            TutorialTracker t = At("6");
            Feed(t, Hit(1), Hit(2, ComboBranch.DodgeStrike));
            Assert.AreEqual(1, t.Progress);
            Feed(t, Hit(3, ComboBranch.Main, true));
            Assert.AreEqual(1, t.Progress, "fail: an ordinary hit");
            Feed(t, Hit(4, ComboBranch.DodgeStrike));
            AssertPassed(t, "6");
        }

        [Test]
        public void LaunchAndJuggleNeedsTheLastAirHitInTheSameCombo()
        {
            TutorialTracker t = At("7");
            var snapshot = new TutorialSnapshot { Loadout = ElementLoadout.CreateFluid() };
            Tick(t, Frame, snapshot);
            int last = snapshot.Loadout.Fire.AirChain.Length - 1;
            Feed(t, Hit(1, ComboBranch.Air, false, ElementId.Fire, PlayerAttackKind.Air, last, true));
            Assert.AreEqual(0, t.Progress, "fail: no launcher first");
            Feed(t, Hit(1, ComboBranch.Launcher, false, ElementId.Fire, PlayerAttackKind.Launcher), Ended(1),
                 Hit(1, ComboBranch.Air, false, ElementId.Fire, PlayerAttackKind.Air, last, true));
            Assert.AreEqual(0, t.Progress, "combo reset between launcher and juggle");
            Feed(t, Hit(1, ComboBranch.Launcher, false, ElementId.Fire, PlayerAttackKind.Launcher),
                 Hit(2, ComboBranch.Air, false, ElementId.Fire, PlayerAttackKind.Air, last - 1, true),
                 Hit(3, ComboBranch.Air, false, ElementId.Fire, PlayerAttackKind.Air, last, false));
            Assert.AreEqual(0, t.Progress, "fail: the target had landed");
            Feed(t, Hit(4, ComboBranch.Air, false, ElementId.Fire, PlayerAttackKind.Air, last, true));
            AssertPassed(t, "7");
        }

        [Test]
        public void SwitchStepNeedsASwitchStrikeIntoItsElementThenAHitInIt()
        {
            TutorialTracker t = At("8");
            Feed(t, Switched(ElementId.Water, false), Hit(1, ComboBranch.Main, false, ElementId.Water));
            Assert.AreEqual(0, t.Progress, "fail: a plain switch (not mid-string)");
            Feed(t, Switched(ElementId.Earth), Hit(2, ComboBranch.Main, false, ElementId.Earth));
            Assert.AreEqual(0, t.Progress, "fail: the wrong element");
            Feed(t, Switched(ElementId.Water), Ended(2), Hit(1, ComboBranch.Main, false, ElementId.Water));
            Assert.AreEqual(0, t.Progress, "combo reset");
            Feed(t, Switched(ElementId.Water), Hit(2, ComboBranch.Main, false, ElementId.Water));
            AssertPassed(t, "8");
        }

        [Test]
        public void AlreadyInTheTargetElementShowsTheOtherHint()
        {
            TutorialTracker t = At("8b");
            Tick(t, Frame, new TutorialSnapshot { ActiveElement = ElementId.Earth });
            Assert.IsTrue(t.AlreadyInTargetElement);
            Tick(t, Frame, new TutorialSnapshot { ActiveElement = ElementId.Water });
            Assert.IsFalse(t.AlreadyInTargetElement);
        }

        [Test]
        public void MixFinisherNeedsTheLevel()
        {
            TutorialTracker t = At("9");
            Feed(t, new PlayerEvent { Type = PlayerEventType.MixFinisher, Count = 1 });
            Assert.AreEqual(0, t.Progress, "fail: one element");
            Feed(t, new PlayerEvent { Type = PlayerEventType.MixFinisher, Count = 2 });
            AssertPassed(t, "9");
        }

        [Test]
        public void DangerResponseMustFollowTheNowCue()
        {
            TutorialTracker t = At("10");
            float window = t.Step.FollowUpWindow;
            Feed(t, Of(PlayerEventType.PerfectDodge));
            Assert.AreEqual(0, t.Progress, "fail: no cue");
            Feed(t, Of(PlayerEventType.DangerNow));
            Tick(t, window + 0.05f);
            Feed(t, Of(PlayerEventType.Deflected));
            Assert.AreEqual(0, t.Progress, "fail: too long after the cue");
            Feed(t, Of(PlayerEventType.DangerNow));
            Tick(t, window - 0.05f);
            Feed(t, Of(PlayerEventType.Deflected));
            Assert.AreEqual(1, t.Progress);
            Feed(t, Of(PlayerEventType.PerfectDodge));
            Assert.AreEqual(1, t.Progress, "one response per cue");
            Feed(t, Of(PlayerEventType.DangerNow), Of(PlayerEventType.PerfectDodge));
            AssertPassed(t, "10");
        }

        [Test]
        public void GraduationNeedsHitsAndMixTogether()
        {
            TutorialTracker t = At("11");
            TutorialStepData step = t.Step;
            Feed(t, Mix(1), Mix(2));
            for (int i = 1; i <= step.MinComboCount; i++) Feed(t, Hit(i));
            Assert.AreEqual(0, t.Progress, "fail: only two elements");
            Assert.Less(t.AttemptProgress01, 1f);
            Feed(t, Ended(step.MinComboCount), Mix(1), Mix(2), Mix(3), Hit(1));
            Assert.AreEqual(0, t.Progress, "combo reset: the count starts again");
            for (int i = 2; i <= step.MinComboCount; i++) Feed(t, Hit(i));
            AssertPassed(t, "11");
            Tick(t, t.Script.SuccessPause + 0.01f);
            Assert.IsFalse(t.IsRunning);
            Assert.IsTrue(t.IsFinished, "the last step passed: the tutorial is done");
        }

        // ---------------------------------------------------------------- synthetic: skip, quit, the flash

        [Test]
        public void SkipMovesOnWithoutPassing()
        {
            TutorialTracker t = At("2");
            t.Skip();
            Assert.AreEqual("3", t.Step.Id);
            Assert.AreEqual(0, t.PassSerial, "skipping is not passing");
            Assert.IsFalse(t.InSuccessPause);
        }

        [Test]
        public void SkipDuringTheFlashJustEndsItEarly()
        {
            TutorialTracker t = At("9");
            Feed(t, new PlayerEvent { Type = PlayerEventType.MixFinisher, Count = 2 });
            Assert.IsTrue(t.InSuccessPause);
            t.Skip();
            Assert.AreEqual("10", t.Step.Id);
            Assert.AreEqual(1, t.PassSerial);
        }

        [Test]
        public void SkippingTheLastStepFinishes()
        {
            TutorialTracker t = At("11");
            t.Skip();
            Assert.IsFalse(t.IsRunning);
            Assert.IsTrue(t.IsFinished);
            Assert.IsNull(t.Step);
        }

        [Test]
        public void StopForgetsEverythingAndStartBeginsAgain()
        {
            TutorialTracker t = At("5");
            t.Stop();
            Assert.IsFalse(t.IsRunning);
            Assert.IsFalse(t.IsFinished);
            Feed(t, Hit(1, ComboBranch.DodgeStrike));
            Tick(t, 10f);
            Assert.AreEqual(0, t.PassSerial, "nothing is tracked after quitting");
            t.Start();
            Assert.AreEqual("1", t.Step.Id);
        }

        [Test]
        public void EventsDuringTheFlashDontCountTowardTheNextStep()
        {
            TutorialTracker t = At("6");
            Feed(t, Hit(1, ComboBranch.DodgeStrike), Hit(2, ComboBranch.DodgeStrike));
            Assert.IsTrue(t.InSuccessPause);
            Feed(t, Hit(3, ComboBranch.Launcher, false, ElementId.Fire, PlayerAttackKind.Launcher));
            Tick(t, t.Script.SuccessPause + 0.01f, new TutorialSnapshot { Loadout = ElementLoadout.CreateFluid() });
            Assert.AreEqual("7", t.Step.Id);
            Feed(t, Hit(4, ComboBranch.Air, false, ElementId.Fire, PlayerAttackKind.Air, 2, true));
            Assert.AreEqual(0, t.Progress, "the launcher landed during step 6's flash, so step 7 hasn't seen one");
        }

        // ---------------------------------------------------------------- integration: a real model, scripted input

        // A PlayerDriver on the default Fluid loadout with a sparring partner, feeding every event to a tracker and ticking
        // it once per frame, with every strike landing (the partner can't die). 'airborneTarget': hits land on a target
        // in the air (the juggle).
        sealed class Rig
        {
            static readonly Vector2 Toward = new Vector2(0f, 1f);

            public readonly PlayerDriver D = PlayerDriver.Elements();
            public readonly TutorialTracker T = new TutorialTracker(TutorialScript.CreateDefault());
            public Vector3 Partner;
            public bool AirborneTarget;
            int fed;

            public Rig(string stepId, float partnerDistance = 2.2f)
            {
                PlacePartner(partnerDistance);
                T.Start();
                while (T.IsRunning && T.Step.Id != stepId) T.Skip();
                Assert.IsTrue(T.IsRunning);
                Frame();
            }

            public string StepId => T.Step != null ? T.Step.Id : "";

            public void PlacePartner(float distance)
            {
                Partner = D.World.Position + new Vector3(0f, 0f, distance);
                Partner.Y = 0f;
                D.Target(Partner);
            }

            float Distance
            {
                get
                {
                    Vector3 gap = Partner - D.World.Position;
                    gap.Y = 0f;
                    return gap.Length();
                }
            }

            public void Frame(Pad pad = Pad.None, Vector2 move = default)
            {
                D.Step(pad, move);
                D.LandStrikes(AirborneTarget);
                for (; fed < D.Log.Count; fed++) T.OnEvent(D.Log[fed]);
                T.Tick(D.Dt, D.Dt, TutorialSnapshot.From(D.Model, Distance));
            }

            public void Run(float seconds, Pad pad = Pad.None, Vector2 move = default)
            {
                int frames = (int)Math.Ceiling(seconds / D.Dt);
                for (int i = 0; i < frames; i++) Frame(pad, move);
            }

            public void RunUntil(Func<Rig, bool> done, int maxFrames, Pad pad = Pad.None)
            {
                for (int i = 0; i < maxFrames; i++)
                {
                    Frame(pad);
                    if (done(this)) return;
                }
                Assert.Fail("condition not reached in " + maxFrames + " frames (state " + D.Model.State + ", step " + StepId + ")");
            }

            public void RunUntilStarted(int n)
            {
                if (D.Started < n) RunUntil(r => r.D.Started >= n, 120);
            }

            // Waits for the running string move's beat, then presses (or picks an element with RB) on it.
            public void PressOnBeat(Pad pad = Pad.Light, ElementId select = ElementId.None)
            {
                for (int i = 0; i < 120 && D.Model.Rhythm.Active && D.Model.Rhythm.TimeToBeat > D.Dt * 0.5f; i++) Frame();
                D.Select = select;
                Frame(select == ElementId.None ? pad : Pad.None);
            }

            // Presses X, then 'followUps' more on the beat; switchAt: before that follow-up (1-based), RB + the element instead.
            public void OnBeatString(int followUps, int switchAt = 0, ElementId switchTo = ElementId.None)
            {
                int n = D.Started + 1;
                Frame(Pad.Light);
                for (int i = 1; i <= followUps; i++)
                {
                    RunUntilStarted(n);
                    if (i == switchAt) PressOnBeat(Pad.None, switchTo);
                    else PressOnBeat();
                    n++;
                }
                RunUntilStarted(n);
            }

            // Waits (combo still alive) until the stamina bar can pay for another string.
            public void Breathe(float share)
            {
                for (int i = 0; i < 600; i++)
                {
                    if (D.Model.State == PlayerState.Locomotion && D.Model.Stamina >= D.Model.MaxStamina * share) return;
                    Frame();
                }
            }

            public void SettleToLocomotion()
            {
                RunUntil(r => r.D.Model.State == PlayerState.Locomotion && r.D.Model.IsGrounded, 240);
            }

            public void Dodge(Vector2 stick)
            {
                Frame(Pad.Dodge, stick);
            }

            public void DodgeToward()
            {
                Dodge(Toward);
            }
        }

        static void AssertStepPassed(Rig r, string id)
        {
            Assert.IsTrue(r.T.PassSerial > 0, "step " + id + " passed");
            r.Run(r.T.Script.SuccessPause + 0.05f);
            Assert.AreNotEqual(id, r.StepId, "and moved on after its flash");
        }

        [Test]
        public void Step1MoveAndLookCompletes()
        {
            var r = new Rig("1");
            r.Run(r.T.Step.Duration + 0.1f, Pad.None, new Vector2(0.6f, 0.8f));
            AssertStepPassed(r, "1");
        }

        [Test]
        public void Step2OnTheBeatCompletes()
        {
            var r = new Rig("2");
            int chain = r.D.Model.MoveSet.LightChain.Length;
            r.OnBeatString(chain - 1);
            Assert.IsTrue(r.D.LastStarted.IsFinisher);
            r.RunUntil(x => x.T.PassSerial > 0, 90);
            AssertStepPassed(r, "2");
        }

        [Test]
        public void Step3PauseFinisherCompletes()
        {
            var r = new Rig("3");
            for (int attempt = 0; attempt < 2; attempt++)
            {
                int pauseAfter = r.D.Model.MoveSet.Rhythm.PauseAfterIndex;
                r.OnBeatString(pauseAfter);                     // X X (the pause is allowed after this hit)
                r.SettleToLocomotion();                          // ... wait ...
                int n = r.D.Started + 1;
                r.Frame(Pad.Light);                              // X: the pause chain
                r.RunUntilStarted(n);
                Assert.AreEqual(ComboBranch.Pause, r.D.LastStarted.Branch);
                while (!r.D.LastStarted.IsFinisher)
                {
                    n++;
                    r.PressOnBeat();
                    r.RunUntilStarted(n);
                }
                r.SettleToLocomotion();
                r.Breathe(0.6f);
                r.Run(0.6f);                                    // let the string forget itself
            }
            AssertStepPassed(r, "3");
        }

        // Build 05 verify J-01: doing exactly what step 3's prompt says passes it in every element: X X on the beat, wait
        // until the circle glows blue (RhythmView.PauseReady), then X X. The prompt must name every press it needs.
        [TestCase(ElementId.Fire)]
        [TestCase(ElementId.Water)]
        [TestCase(ElementId.Earth)]
        [TestCase(ElementId.Air)]
        public void Step3PassesDoingExactlyWhatThePromptSays(ElementId element)
        {
            var r = new Rig("3");
            StringAssert.Contains("{X} {X} … wait … {X} {X}", r.T.Step.Prompt, "the prompt teaches X X (wait) X X");
            if (element != ElementId.Fire)
            {
                r.D.Select = element;
                r.Frame();
                r.Run(0.5f);
            }
            for (int attempt = 0; attempt < 2; attempt++)
            {
                r.OnBeatString(1);                               // X X
                r.RunUntil(x => x.D.Model.Rhythm.PauseReady, 120);   // ... wait for the blue circle ...
                int n = r.D.Started + 1;
                r.Frame(Pad.Light);                              // X
                r.RunUntilStarted(n);
                Assert.AreEqual(ComboBranch.Pause, r.D.LastStarted.Branch, element + ": the pause chain");
                r.PressOnBeat();                                 // X
                r.RunUntilStarted(n + 1);
                Assert.IsTrue(r.D.LastStarted.IsFinisher, element + ": X X (wait) X X ends on the pause finisher");
                r.SettleToLocomotion();
                r.Breathe(0.6f);
                r.Run(0.6f);
            }
            AssertStepPassed(r, "3");
        }

        [Test]
        public void Step4DodgeKeepsTheComboCompletes()
        {
            var r = new Rig("4");
            r.OnBeatString(1);                                   // X X: a two-hit combo
            for (int attempt = 0; attempt < 2; attempt++)
            {
                r.RunUntil(x => x.D.Model.Phase == AttackPhase.Recovery, 90);
                int count = r.D.Model.ComboCount;
                Assert.GreaterOrEqual(count, 2);
                r.Dodge(new Vector2(1f, 0f));                    // a side-step, as if a strike were coming
                int n = r.D.Started + 1;
                r.Frame(Pad.Light);                              // keep pressing X
                r.RunUntilStarted(n);
                r.RunUntil(x => x.D.Model.ComboCount > count, 60);
            }
            r.RunUntil(x => x.T.PassSerial > 0, 30);
            AssertStepPassed(r, "4");
        }

        [Test]
        public void Step5SlipInCompletes()
        {
            var r = new Rig("5");
            float start = r.T.Step.MinStartDistance + 0.5f;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                r.D.World.Position = Vector3.Zero;              // back off to range
                r.PlacePartner(start);
                r.Frame();
                r.DodgeToward();
                Assert.AreEqual(DodgeKind.SlipIn, r.D.LastOf(PlayerEventType.DodgeStarted).DodgeKind);
                r.Frame(Pad.Light);                              // then X
                r.RunUntil(x => x.T.Progress > attempt || x.T.PassSerial > 0, 60);
                r.SettleToLocomotion();
                r.Run(0.5f);
            }
            AssertStepPassed(r, "5");
        }

        [Test]
        public void Step6DodgeStrikeCompletes()
        {
            var r = new Rig("6");
            for (int attempt = 0; attempt < 2; attempt++)
            {
                r.OnBeatString(1);
                r.RunUntil(x => x.D.Model.Phase == AttackPhase.Recovery, 90);
                r.Dodge(new Vector2(0f, -1f));                   // evade out ...
                int n = r.D.Started + 1;
                r.Frame(Pad.Light, new Vector2(0f, -1f));        // ... X late in it: the strike dashes back in
                r.RunUntilStarted(n);
                Assert.AreEqual(ComboBranch.DodgeStrike, r.D.LastStarted.Branch);
                r.SettleToLocomotion();
                r.Breathe(0.6f);
            }
            AssertStepPassed(r, "6");
        }

        [Test]
        public void Step7LaunchAndJuggleCompletes()
        {
            var r = new Rig("7");
            r.RunUntil(x => x.D.Model.CurrentAttackKind == PlayerAttackKind.Launcher, 90, Pad.Light);   // hold X
            r.RunUntil(x => !x.D.Model.IsGrounded && x.D.Model.State != PlayerState.Attacking, 120);    // up you go
            r.AirborneTarget = true;                             // the partner is in the air with you
            int airHits = r.D.Model.MoveSet.AirChain.Length;
            r.OnBeatString(airHits - 1);                         // X X X in the air
            r.RunUntil(x => x.T.PassSerial > 0, 90);
            AssertStepPassed(r, "7");
        }

        static void SwitchStep(string id, ElementId element)
        {
            var r = new Rig(id);
            Assert.AreNotEqual(element, r.D.Model.ActiveElement);
            r.OnBeatString(2, 2, element);                       // X X, RB + the element's button on the beat
            Assert.AreEqual(element, r.D.Model.ActiveElement);
            Assert.IsTrue(r.D.LastOf(PlayerEventType.ElementSwitched).IsSwitchStrike);
            r.RunUntil(x => x.T.PassSerial > 0, 90);
            AssertStepPassed(r, id);
        }

        [Test]
        public void Step8SwitchToWaterCompletes()
        {
            SwitchStep("8", ElementId.Water);
        }

        [Test]
        public void Step8bSwitchToEarthCompletes()
        {
            SwitchStep("8b", ElementId.Earth);
        }

        [Test]
        public void Step8cSwitchToAirCompletes()
        {
            SwitchStep("8c", ElementId.Air);
        }

        [Test]
        public void Step9MixFinisherCompletes()
        {
            var r = new Rig("9");
            r.OnBeatString(2, 2, ElementId.Water);               // X X, RB + X (Water) ...
            int n = r.D.Started + 1;
            while (!r.D.LastStarted.IsFinisher)                  // ... and on to Water's finisher
            {
                r.PressOnBeat();
                r.RunUntilStarted(n++);
            }
            r.RunUntil(x => x.T.PassSerial > 0, 90);
            Assert.GreaterOrEqual(r.D.LastOf(PlayerEventType.MixFinisher).Count, 2);
            AssertStepPassed(r, "9");
        }

        [Test]
        public void Step10DangerSenseCompletes()
        {
            var r = new Rig("10");
            DangerSenseSettings sense = r.D.Model.Tuning.DangerSense;
            const float Reaction = 0.22f;                       // a person pressing on the white flash
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool parry = attempt == 0;                       // gold (parryable): LB; then red (must dodge): B
                IncomingStrike strike = DangerSenseTests.Strike(r.D, 7, 1.0f, attempt, parry);
                r.D.Model.NotifyIncomingStrike(strike);
                int nows = r.D.Count(PlayerEventType.DangerNow);
                r.RunUntil(x => x.D.Count(PlayerEventType.DangerNow) > nows, 90);
                Assert.AreEqual(strike.ImpactClock - sense.NowLead, r.D.Model.Clock, r.D.Dt * 1.5f);
                r.Run(Reaction);
                if (parry) r.Frame(Pad.Guard);
                else r.Dodge(new Vector2(1f, 0f));
                r.RunUntil(x => x.D.Model.Clock >= strike.ImpactClock - 1e-6, 30);
                r.D.Model.ReceiveHit(PlayerDriver.EnemyHit(10f, 5f, r.D.Model.Forward, parry), r.D.Model.Forward);
                r.Frame();                                       // the reward event arrives with the next frame
                Assert.AreEqual(1, r.D.Count(parry ? PlayerEventType.Deflected : PlayerEventType.PerfectDodge),
                    parry ? "deflected" : "perfect dodge");
                Assert.AreEqual(attempt + 1, r.T.Progress, "counted: it came on the white flash");
                r.SettleToLocomotion();
                r.Run(0.5f);
            }
            AssertStepPassed(r, "10");
        }

        // ---------------------------------------------------------------- docs/Prototype/How-To-Play.md recipes

        // Presses X, then each follow-up on the beat: None = X, an element = RB + its button. Ends with the last move started.
        static void Recipe(Rig r, params ElementId[] followUps)
        {
            int n = r.D.Started + 1;
            r.Frame(Pad.Light);
            foreach (ElementId element in followUps)
            {
                r.RunUntilStarted(n);
                r.PressOnBeat(Pad.Light, element);
                n++;
            }
            r.RunUntilStarted(n);
        }

        [Test]
        public void HowToPlayThreeElementLaunchRecipeLaunches()
        {
            // "X, RB + X (Water), X, RB + A (Earth), X": Fire, Water and Earth land before Earth's finisher.
            var r = new Rig("11");
            Recipe(r, ElementId.Water, ElementId.None, ElementId.Earth, ElementId.None);
            Assert.IsTrue(r.D.LastStarted.IsFinisher);
            Assert.AreEqual(ElementId.Earth, r.D.LastStarted.Element);
            Assert.AreEqual(3, r.D.Model.MixLevel, "a switch between every other hit is never refused by the cooldown");
            Assert.AreEqual(r.D.Model.Tuning.Mix.FinisherLaunchSpeed, r.D.Model.BuildCurrentDamage().LaunchSpeed, 1e-4f);
        }

        [Test]
        public void HowToPlayFullMixRecipeBreaksGuard()
        {
            // The three-element launch, then a second string with Air in it: "X, RB + Y (Air), X X X".
            var r = new Rig("11");
            Recipe(r, ElementId.Water, ElementId.None, ElementId.Earth, ElementId.None);
            r.SettleToLocomotion();
            r.Breathe(0.5f);
            Recipe(r, ElementId.Air, ElementId.None, ElementId.None, ElementId.None);
            Assert.IsTrue(r.D.LastStarted.IsFinisher);
            Assert.AreEqual(4, r.D.Model.MixLevel, "MIX lasts the whole combo, across strings");
            Assert.AreEqual(float.MaxValue, r.D.Model.BuildCurrentDamage().PoiseDamage, "MIX 4: the finisher breaks guard");
        }

        [Test]
        public void Step11GraduationCompletes()
        {
            var r = new Rig("11");
            TutorialStepData step = r.T.Step;
            ElementId[] order = { ElementId.Water, ElementId.Earth, ElementId.Air, ElementId.Fire };
            int strings = 0;
            while (r.T.PassSerial == 0 && strings < 12)
            {
                // One string per element, switching in on its second follow-up, resting between strings so the stamina
                // bar can pay for the next (well inside the combo timeout).
                r.OnBeatString(3, 2, order[strings % order.Length]);
                r.SettleToLocomotion();
                Assert.Greater(r.D.Model.ComboCount, 0, "the combo lives between strings");
                r.Breathe(0.55f);
                strings++;
            }
            Assert.GreaterOrEqual(r.T.ComboCount, step.MinComboCount);
            Assert.GreaterOrEqual(r.T.MixLevel, step.MinMixLevel);
            AssertStepPassed(r, "11");
            Assert.IsTrue(r.T.IsFinished, "graduated: the tutorial is over");
        }
    }
}
