using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Pins frame data, input buffering, stamina gating and the tap/hold dodge button.
    public class CombatTimingTests
    {
        static readonly Vector2 Forward = new Vector2(0f, 1f);

        [Test]
        public void JabStartupActiveAndRecoveryLandOnTheRightFramesAt60Fps()
        {
            var d = new PlayerDriver();
            MoveData jab = d.Model.MoveSet.LightChain[0];
            d.Step(Pad.Light);
            Assert.AreEqual(0, d.FirstFrame(PlayerEventType.AttackStarted));
            Assert.AreSame(jab, d.Model.CurrentMove);
            Assert.AreEqual(AttackPhase.Startup, d.Model.Phase);

            d.Run(60);
            int activeStart = d.FirstFrame(PlayerEventType.AttackActiveStart);
            int activeEnd = d.FirstFrame(PlayerEventType.AttackActiveEnd);
            int ended = d.FirstFrame(PlayerEventType.AttackEnded);
            Assert.AreEqual(d.FramesToReach(jab.Startup), activeStart, "0.12 s startup = active on frame 8");
            Assert.AreEqual(d.FramesToReach(jab.ActiveEnd), activeEnd, "0.10 s active = closes on frame 14");
            Assert.AreEqual(d.FramesToReach(jab.TotalDuration), ended);
            Assert.AreEqual(8, activeStart);
            Assert.AreEqual(14, activeEnd);
            Assert.AreEqual(PlayerState.Locomotion, d.Model.State);
            Assert.AreEqual(1, d.Count(PlayerEventType.AttackActiveStart));
            Assert.AreEqual(1, d.Count(PlayerEventType.AttackActiveEnd));
        }

        [Test]
        public void AttackIsActiveExactlyDuringItsActiveFrames()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Light);
            for (int frame = 1; frame < 20; frame++)
            {
                d.Step();
                bool expected = frame >= 8 && frame < 14;
                Assert.AreEqual(expected, d.Model.IsAttackActive, "frame " + frame);
                if (expected) Assert.AreNotEqual(0, d.Model.ActiveAttackId);
            }
        }

        [Test]
        public void EarlyLightPressIsBufferedAndChainsAtTheCancelPoint()
        {
            var d = new PlayerDriver();
            MoveData jab = d.Model.MoveSet.LightChain[0];
            d.Step(Pad.Light);
            d.Run(4);
            d.Step(Pad.Light);                                   // frame 5: 0.083 s, before the combo window opens
            Assert.AreEqual(PlayerCommand.Light, d.Model.BufferedCommand);
            d.Run(30);

            var starts = d.All(PlayerEventType.AttackStarted);
            Assert.AreEqual(2, starts.Count);
            Assert.AreEqual("Flame Cross", starts[1].Move.DisplayName);
            int crossFrame = d.FirstFrame(PlayerEventType.AttackStarted, 1);
            Assert.AreEqual(d.FramesToReach(jab.ChainCancelAt), crossFrame, "fires at the cancel point, not before");
            Assert.Less(d.FirstFrame(PlayerEventType.AttackActiveEnd), crossFrame, "the jab's active frames finish first");
        }

        [Test]
        public void BufferedPressExpiresWhenTheActionEndsTooLate()
        {
            // Quick heavy: 0.95 s total, cancel point at the very end. A light pressed 0.5 s in is older than
            // the 0.25 s buffer when the heavy ends, so it's dropped; one pressed 0.75 s in comes out on time.
            var d = new PlayerDriver();
            d.Tap(Pad.Heavy);
            Assert.AreEqual(PlayerState.Attacking, d.Model.State);
            d.Run(28);
            d.Step(Pad.Light);                                   // frame 30
            d.Run(60);
            Assert.AreEqual(1, d.Count(PlayerEventType.AttackStarted), "stale press expired");

            var d2 = new PlayerDriver();
            MoveData heavy = d2.Model.MoveSet.Heavy;
            d2.Tap(Pad.Heavy);                                   // charge on frame 0, released (quick heavy) on frame 1
            int heavyStart = d2.FirstFrame(PlayerEventType.AttackStarted);
            Assert.AreEqual(1, heavyStart);
            Assert.AreEqual(ChargeTier.Quick, d2.All(PlayerEventType.AttackStarted)[0].ChargeTier);
            d2.Run(43);
            d2.Step(Pad.Light);                                  // frame 45
            d2.Run(30);
            Assert.AreEqual(2, d2.Count(PlayerEventType.AttackStarted));
            Assert.AreEqual(heavyStart + d2.FramesToReach(heavy.TotalDuration), d2.FirstFrame(PlayerEventType.AttackStarted, heavyStart + 1));
        }

        [Test]
        public void PunishingDodgeCannotBeCutShortAndItsBufferExpires()
        {
            // Punishing: attacks only after the dodge fully ends (0.48 s); the buffer is 0.2 s, so a light
            // pressed right at the start of the dodge is gone by then.
            var d = PlayerDriver.Punishing();
            d.Step(Pad.Dodge, Forward);
            d.Step(Pad.None, Forward);                           // released quickly = tap = dodge (OnRelease)
            Assert.AreEqual(PlayerState.Dodging, d.Model.State);
            d.Step(Pad.Light, Forward);
            d.Run(60, Pad.None, Forward);
            Assert.AreEqual(0, d.Count(PlayerEventType.AttackStarted));
        }

        [Test]
        public void FluidDodgeCancelsIntoABufferedAttack()
        {
            var d = new PlayerDriver();
            DodgeProfile dodge = d.Model.MoveSet.Dodge;
            d.Step(Pad.Dodge, Forward);
            d.Step(Pad.Light, Forward);
            d.Run(30, Pad.None, Forward);
            int dodgeFrame = d.FirstFrame(PlayerEventType.DodgeStarted);
            int attackFrame = d.FirstFrame(PlayerEventType.AttackStarted);
            Assert.AreEqual(0, dodgeFrame);
            Assert.AreEqual(d.FramesToReach(dodge.AttackCancelAt), attackFrame);
            Assert.Less(d.FirstFrame(PlayerEventType.DodgeEnded), attackFrame + 1);
        }

        [Test]
        public void ActionsStartWhileAnyStaminaIsLeftAndStaminaNeverGoesNegative()
        {
            var tuning = PlayerTuning.CreateFluid();
            tuning.MaxStamina = 10f;
            tuning.StaminaRegen = 0f;
            var d = new PlayerDriver(tuning);
            d.Tap(Pad.Dodge, Forward);                           // 6 -> 4 left
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 60, Pad.None, Forward);
            Assert.That(d.Model.Stamina, Is.EqualTo(4f).Within(1e-4f));

            d.Tap(Pad.Dodge, Forward);                           // 4 > 0, so it's allowed, and clamps at 0
            Assert.AreEqual(2, d.Count(PlayerEventType.DodgeStarted));
            Assert.AreEqual(0f, d.Model.Stamina);
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 60, Pad.None, Forward);

            d.Tap(Pad.Dodge, Forward);                           // empty: nothing happens
            d.Tap(Pad.Light, Forward);
            d.Run(30);
            Assert.AreEqual(2, d.Count(PlayerEventType.DodgeStarted));
            Assert.AreEqual(0, d.Count(PlayerEventType.AttackStarted));
            Assert.AreEqual(0f, d.Model.Stamina);
        }

        [Test]
        public void StaminaRegenWaitsForItsDelay()
        {
            var d = new PlayerDriver();
            PlayerTuning t = d.Model.Tuning;
            d.Tap(Pad.Dodge, Forward);
            float afterSpend = d.Model.Stamina;
            int delayFrames = d.FramesToReach(t.StaminaRegenDelay) - 2;
            d.Run(delayFrames);
            Assert.AreEqual(afterSpend, d.Model.Stamina, 1e-4f, "no regen during the delay");
            d.Run(30);
            Assert.Greater(d.Model.Stamina, afterSpend);
        }

        [Test]
        public void OnPressModeDodgesImmediatelyAndHoldingAfterwardsSprints()
        {
            var d = new PlayerDriver();
            Assert.AreEqual(DodgeTrigger.OnPress, d.Model.Tuning.DodgeTrigger);
            d.Step(Pad.Dodge, Forward);
            Assert.AreEqual(PlayerState.Dodging, d.Model.State, "dodge on the press frame");
            d.RunUntil(x => x.Model.State == PlayerState.Sprinting, 40, Pad.Dodge, Forward);
            Assert.AreEqual(1, d.Count(PlayerEventType.DodgeStarted));
            Assert.Greater(d.FirstFrame(PlayerEventType.SprintStarted), d.FirstFrame(PlayerEventType.DodgeEnded) - 1);
            d.Run(20, Pad.Dodge, Forward);
            Assert.That(d.Model.Velocity.Z, Is.EqualTo(d.Model.Tuning.SprintSpeed).Within(0.05f));
            d.Step(Pad.None, Forward);
            Assert.AreEqual(PlayerState.Locomotion, d.Model.State, "letting go stops the sprint");
            Assert.AreEqual(1, d.Count(PlayerEventType.DodgeStarted), "releasing a hold never dodges");
        }

        [Test]
        public void OnReleaseModeTapDodgesOnReleaseAndHoldSprintsWithoutDodging()
        {
            var d = PlayerDriver.Punishing();
            Assert.AreEqual(DodgeTrigger.OnRelease, d.Model.Tuning.DodgeTrigger);
            d.Run(4, Pad.Dodge, Forward);
            Assert.AreEqual(0, d.Count(PlayerEventType.DodgeStarted), "no dodge while still held");
            d.Step(Pad.None, Forward);
            Assert.AreEqual(4, d.FirstFrame(PlayerEventType.DodgeStarted), "dodge on the release frame");

            var h = PlayerDriver.Punishing();
            int threshold = h.FramesToReach(h.Model.Tuning.TapHoldThreshold);
            h.Run(threshold + 20, Pad.Dodge, Forward);
            Assert.AreEqual(threshold, h.FirstFrame(PlayerEventType.SprintStarted), "sprint once the hold passes the threshold");
            h.Step(Pad.None, Forward);
            h.Run(10, Pad.None, Forward);
            Assert.AreEqual(0, h.Count(PlayerEventType.DodgeStarted));
        }

        [Test]
        public void SubFrameTapStillDodgesInBothModes()
        {
            foreach (PlayerDriver d in new[] { new PlayerDriver(), PlayerDriver.Punishing() })
            {
                PlayerInputFrame input = d.MakeInput(Pad.None, Forward);
                input.Dodge = new ButtonState { Pressed = true, Released = true, Held = false };
                d.Model.Tick(d.Dt, input, d.World);
                Assert.AreEqual(PlayerState.Dodging, d.Model.State, d.Model.Tuning.PresetName);
            }
        }

        [Test]
        public void LightChainWalksThroughTheMovesAndLoops()
        {
            var d = new PlayerDriver();
            for (int press = 0; press < 5; press++)
            {
                d.Step(Pad.Light);
                d.Run(18);                                       // next press lands inside each combo window
            }
            d.Run(60);
            var starts = d.All(PlayerEventType.AttackStarted);
            Assert.AreEqual(5, starts.Count);
            string[] expected = { "Flame Jab", "Flame Cross", "Dragon Tail Kick", "Flame Jab", "Flame Cross" };
            for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], starts[i].Move.DisplayName, "press " + i);
        }

        [Test]
        public void LightPressAfterTheComboWindowRestartsTheChain()
        {
            var d = new PlayerDriver();
            MoveData jab = d.Model.MoveSet.LightChain[0];
            d.Step(Pad.Light);
            int late = d.FramesToReach(jab.ComboWindowEnd) + 1;
            d.Run(late - 1);
            d.Step(Pad.Light);
            d.Run(40);
            var starts = d.All(PlayerEventType.AttackStarted);
            Assert.AreEqual(2, starts.Count);
            Assert.AreEqual("Flame Jab", starts[1].Move.DisplayName);
            Assert.AreEqual(late, d.FirstFrame(PlayerEventType.AttackStarted, 1), "past the cancel point it starts at once");
        }

        [Test]
        public void ChainSurvivesARecoveryTunedShorterThanTheComboWindow()
        {
            var moves = ElementMoveSet.CreateFireFluid();
            moves.LightChain[0].Recovery = 0f;                   // jab now ends at 0.22 s, window runs to 0.40 s
            var d = new PlayerDriver(null, moves);
            d.Step(Pad.Light);
            d.Run(16);                                           // the jab has ended
            Assert.AreEqual(PlayerState.Locomotion, d.Model.State);
            d.Step(Pad.Light);
            d.Run(5);
            Assert.AreEqual("Flame Cross", d.All(PlayerEventType.AttackStarted)[1].Move.DisplayName);
        }

        [Test]
        public void SprintAttackNeedsTheMinimumSprintTime()
        {
            var d = new PlayerDriver();
            d.RunUntil(x => x.Model.State == PlayerState.Sprinting, 40, Pad.Dodge, Forward);
            d.Step(Pad.Dodge | Pad.Light, Forward);              // sprinting for a single frame
            Assert.AreEqual("Flame Jab", d.Model.CurrentMove.DisplayName);

            var s = new PlayerDriver();
            s.RunUntil(x => x.Model.State == PlayerState.Sprinting, 40, Pad.Dodge, Forward);
            s.Run(s.FramesToReach(s.Model.MoveSet.SprintAttackMinSprintTime), Pad.Dodge, Forward);
            s.Step(Pad.Dodge | Pad.Light, Forward);
            Assert.AreEqual("Flying Fire Kick", s.Model.CurrentMove.DisplayName);
            Assert.AreEqual(PlayerAttackKind.Sprint, s.Model.CurrentAttackKind);
        }

        [Test]
        public void FrozenFramesAdvanceNothingButRememberPresses()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Light);
            d.Run(3);
            float actionTime = d.Model.ActionTime;
            double clock = d.Model.Clock;
            float stamina = d.Model.Stamina;
            // Hitstop / pause: dt = 0. Mash light during the freeze.
            d.Step(Pad.None, default, 0f);
            d.Step(Pad.Light, default, 0f);
            d.Step(Pad.None, default, 0f);
            d.Step(Pad.None, default, -1e-3f);
            Assert.AreEqual(actionTime, d.Model.ActionTime);
            Assert.AreEqual(clock, d.Model.Clock);
            Assert.AreEqual(stamina, d.Model.Stamina);
            Assert.AreEqual(0, d.Last.Events.Count);
            Assert.AreEqual(PlayerCommand.Light, d.Model.BufferedCommand, "the press made during the freeze is kept");
            d.Run(30);
            Assert.AreEqual(2, d.Count(PlayerEventType.AttackStarted));
        }

        [Test]
        public void HugeFrameStillReportsBothEndsOfTheActiveWindowInOrder()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Light);
            d.Step(Pad.None, default, 0.5f);                     // one 0.5 s hitch skips the whole active window
            Assert.AreEqual(1, d.Count(PlayerEventType.AttackActiveStart));
            Assert.AreEqual(1, d.Count(PlayerEventType.AttackActiveEnd));
            int start = d.Log.FindIndex(e => e.Type == PlayerEventType.AttackActiveStart);
            int end = d.Log.FindIndex(e => e.Type == PlayerEventType.AttackActiveEnd);
            Assert.Less(start, end);
        }
    }
}
