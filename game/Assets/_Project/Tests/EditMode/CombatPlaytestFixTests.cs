using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Player-side fixes from Playtest Report 01 (docs/Prototype/Playtest-Report-01.md). Each test names the
    // report item it pins.
    public class CombatPlaytestFixTests
    {
        static readonly Vector2 Forward = new Vector2(0f, 1f);

        // ---------------------------------------------------------------- ABIL-01: jump-plunge spam

        [Test]
        public void PlungeRightAfterTakeOffDoesNothingAndIsNotSavedForLater()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Jump);
            d.Step(Pad.Light);                                   // one frame after take-off: far below MinAirTime
            Assert.AreEqual(PlayerState.Airborne, d.Model.State);
            Assert.AreEqual(PlayerCommand.None, d.Model.BufferedCommand, "dropped, not buffered");
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 120);
            Assert.AreEqual(0, d.Count(PlayerEventType.AttackStarted), "the early press never turns into a plunge");
        }

        [Test]
        public void PlungeWorksOnceTheMinimumAirTimeHasPassed()
        {
            float minAir = new PlungeSettings().MinAirTime;
            var early = new PlayerDriver();
            early.Step(Pad.Jump);
            early.Run(early.FramesToReach(minAir) - 2);
            early.Step(Pad.Light);                               // just too soon
            Assert.AreEqual(PlayerState.Airborne, early.Model.State);

            var onTime = new PlayerDriver();
            onTime.Step(Pad.Jump);
            onTime.Run(onTime.FramesToReach(minAir) - 1);
            onTime.Step(Pad.Light);
            Assert.AreEqual(PlayerState.Plunging, onTime.Model.State);
        }

        [Test]
        public void PlungeIsACommitmentInBothPresets()
        {
            foreach (ElementMoveSet set in new[] { ElementMoveSet.CreateFireFluid(), ElementMoveSet.CreateFirePunishing() })
            {
                Assert.AreEqual(0.55f, set.PlungeAttack.Recovery, 1e-5f);
                Assert.AreEqual(20f, set.PlungeAttack.StaminaCost, 1e-5f);
                Assert.AreEqual(0.25f, set.Plunge.MinAirTime, 1e-5f);
                Assert.GreaterOrEqual(set.PlungeAttack.ChainCancelAt, set.PlungeAttack.Recovery - 1e-5f, "no attacking out of the landing early");
            }
            ElementMoveSet punishing = ElementMoveSet.CreateFirePunishing();
            Assert.AreEqual(punishing.PlungeAttack.Recovery, punishing.PlungeAttack.DodgeCancelAt, 1e-5f, "Punishing: no dodging out of it either");
        }

        // ---------------------------------------------------------------- ABIL-03: charge clock on real time

        static ChargeTier HoldHeavy(float realSeconds, float timeScale, bool passRealTime)
        {
            var d = new PlayerDriver();
            int frames = d.FramesToReach(realSeconds);
            d.World.RealDeltaTime = passRealTime ? d.Dt : 0f;
            d.Step(Pad.Heavy, default, d.Dt * timeScale);
            for (int i = 1; i < frames; i++) d.Step(Pad.Heavy, default, d.Dt * timeScale);
            d.Step(Pad.None, default, d.Dt * timeScale);
            return d.All(PlayerEventType.AttackStarted)[0].ChargeTier;
        }

        [Test]
        public void PractisedFaJinHoldStillWorksInSlowMotion()
        {
            // 0.8 s of real holding is a fa jin whether or not the game is slowed (after a perfect dodge, 0.35x).
            Assert.AreEqual(ChargeTier.FaJin, HoldHeavy(0.8f, 1f, true));
            Assert.AreEqual(ChargeTier.FaJin, HoldHeavy(0.8f, 0.35f, true));
            // Without a real delta time (the headless harness), game time is used instead.
            Assert.AreEqual(ChargeTier.Partial, HoldHeavy(0.8f, 0.35f, false));
        }

        [Test]
        public void ChargeNeverAdvancesWhileThePauseFreezesTheGame()
        {
            var d = new PlayerDriver();
            d.World.RealDeltaTime = d.Dt;
            d.Step(Pad.Heavy);
            d.Run(10, Pad.Heavy);
            float charge = d.Model.ChargeTime;
            for (int i = 0; i < 60; i++) d.Step(Pad.Heavy, default, 0f);   // paused: game dt 0, real time still ticking
            Assert.AreEqual(charge, d.Model.ChargeTime);
            Assert.AreEqual(PlayerState.Charging, d.Model.State);
        }

        // ---------------------------------------------------------------- ABIL-04: heavy held during another move

        [Test]
        public void BufferedHeavyCountsTheTimeAlreadyHeld()
        {
            var d = new PlayerDriver();
            MoveData jab = d.Model.MoveSet.LightChain[0];
            d.World.RealDeltaTime = d.Dt;
            d.Step(Pad.Light);
            d.Run(3);
            d.Step(Pad.Heavy);                                   // frame 4 of the jab, and kept held
            int cancel = d.FramesToReach(jab.ChainCancelAt);
            d.Run(cancel - d.Frame, Pad.Heavy);
            Assert.AreEqual(PlayerState.Charging, d.Model.State);
            Assert.AreEqual((cancel - 4) * d.Dt, d.Model.ChargeTime, 1e-3f, "the time held during the jab counts");

            int releaseFrame = 4 + d.FramesToReach(0.8f);        // 0.8 s after the press, as practised from idle
            d.Run(releaseFrame - d.Frame - 1, Pad.Heavy);
            d.Step(Pad.None);
            var starts = d.All(PlayerEventType.AttackStarted);
            Assert.AreEqual(ChargeTier.FaJin, starts[starts.Count - 1].ChargeTier);
        }

        [Test]
        public void HeldTimeCreditStopsJustBeforeTheReadyCue()
        {
            // With the default 0.25 s buffer a buffered heavy can't have been held as long as the cue time (0.53 s);
            // a long buffer lets the press wait out a whole quick heavy, which is when the cap matters.
            var t = PlayerTuning.CreateFluid();
            t.InputBufferWindow = 1f;
            var d = new PlayerDriver(t);
            ChargeSettings c = d.Model.MoveSet.Charge;
            d.World.RealDeltaTime = d.Dt;
            d.Tap(Pad.Heavy);                                    // a quick heavy: 0.95 s before anything else can start
            d.Step(Pad.Heavy);                                   // press again straight away and hold through it all
            d.RunUntil(x => x.Model.State == PlayerState.Charging, 120, Pad.Heavy);
            float cue = c.SweetSpotStart - c.ReadyCueLead;
            Assert.Less(d.Model.ChargeTime, cue, "capped before the cue");
            Assert.Greater(d.Model.ChargeTime, cue - 0.01f);
            Assert.AreEqual(0, d.Count(PlayerEventType.ChargeReadyCue));
            d.Step(Pad.Heavy);
            Assert.AreEqual(1, d.Count(PlayerEventType.ChargeReadyCue), "the cue still shows, one frame later");
        }

        // ---------------------------------------------------------------- HUD-01: sweet spot and ready cue

        [Test]
        public void ReadyCueComesOnceBeforeAWiderSweetSpot()
        {
            var d = new PlayerDriver();
            ChargeSettings c = d.Model.MoveSet.Charge;
            Assert.AreEqual(0.65f, c.SweetSpotStart, 1e-5f);
            Assert.AreEqual(0.95f, c.SweetSpotEnd, 1e-5f);
            Assert.AreEqual(0.65f, ElementMoveSet.CreateFirePunishing().Charge.SweetSpotStart, 1e-5f);
            Assert.AreEqual(0.95f, ElementMoveSet.CreateFirePunishing().Charge.SweetSpotEnd, 1e-5f);

            d.Step(Pad.Heavy);
            d.Run(d.FramesToReach(c.MaxChargeTime) - 2, Pad.Heavy);
            Assert.AreEqual(1, d.Count(PlayerEventType.ChargeReadyCue));
            Assert.AreEqual(1, d.Count(PlayerEventType.ChargeSweetSpot));
            Assert.AreEqual(d.FramesToReach(c.SweetSpotStart - c.ReadyCueLead), d.FirstFrame(PlayerEventType.ChargeReadyCue));
            Assert.AreEqual(d.FramesToReach(c.SweetSpotStart), d.FirstFrame(PlayerEventType.ChargeSweetSpot));
            Assert.AreSame(d.Model.MoveSet.Heavy, d.All(PlayerEventType.ChargeReadyCue)[0].Move);
        }

        // ---------------------------------------------------------------- CTRL-02: queued presses expire

        static int CrossesAfterAQueuedPressWaitsForStamina(float queuedMaxAge)
        {
            var t = PlayerTuning.CreateFluid();
            t.MaxStamina = 9f;                                   // the jab empties the bar
            t.StaminaRegenDelay = 0.42f;                         // regen resumes 0.42 s into the jab, after the cap
            t.EmptyStaminaRegenDelay = 0.42f;
            t.QueuedPressMaxAge = queuedMaxAge;
            var d = new PlayerDriver(t);
            d.Step(Pad.Light);
            d.Run(2);
            d.Step(Pad.Light);                                   // 0.05 s: buffered, then queued when the window opens
            d.Run(60);
            int crosses = 0;
            foreach (PlayerEvent e in d.All(PlayerEventType.AttackStarted)) if (e.Move.DisplayName == "Flame Cross") crosses++;
            return crosses;
        }

        [Test]
        public void QueuedChainPressExpiresInsteadOfFiringLate()
        {
            Assert.AreEqual(0, CrossesAfterAQueuedPressWaitsForStamina(0.35f), "0.38 s old by the time stamina is back: dropped");
            Assert.AreEqual(1, CrossesAfterAQueuedPressWaitsForStamina(10f), "the bug this pins: without the cap it fires late");
        }

        // ---------------------------------------------------------------- CTRL-03: sprint attack grace

        static PlayerDriver SprintThenRelease(bool keepStick)
        {
            var d = new PlayerDriver();
            d.RunUntil(x => x.Model.State == PlayerState.Sprinting, 60, Pad.Dodge, Forward);
            d.Run(d.FramesToReach(d.Model.MoveSet.SprintAttackMinSprintTime), Pad.Dodge, Forward);
            d.Step(Pad.None, keepStick ? Forward : Vector2.Zero);   // let go of sprint
            Assert.AreEqual(PlayerState.Locomotion, d.Model.State);
            return d;
        }

        [Test]
        public void LightJustAfterLettingGoOfSprintIsStillASprintAttack()
        {
            PlayerDriver d = SprintThenRelease(true);
            d.Run(4, Pad.None, Forward);
            d.Step(Pad.Light, Forward);
            Assert.AreEqual("Flying Fire Kick", d.Model.CurrentMove.DisplayName);
        }

        [Test]
        public void SprintAttackGraceEndsWithTimeOrWhenYouSlowDown()
        {
            PlayerDriver late = SprintThenRelease(true);
            late.Run(late.FramesToReach(late.Model.MoveSet.SprintAttackGrace) + 1, Pad.None, Forward);
            late.Step(Pad.Light, Forward);
            Assert.AreEqual("Flame Jab", late.Model.CurrentMove.DisplayName, "grace over");

            PlayerDriver stopped = SprintThenRelease(false);
            stopped.Run(4);                                      // stick released too: already below running speed
            stopped.Step(Pad.Light);
            Assert.AreEqual("Flame Jab", stopped.Model.CurrentMove.DisplayName, "slowed down");
        }

        // ---------------------------------------------------------------- tuning: stamina

        [Test]
        public void EmptyingStaminaPausesRegenLonger()
        {
            var t = PlayerTuning.CreateFluid();
            Assert.AreEqual(0.6f, t.StaminaRegenDelay, 1e-5f);
            Assert.AreEqual(1.5f, t.EmptyStaminaRegenDelay, 1e-5f);
            // Round 2 (report 02, NEW-02): Punishing's pauses came back down so dodging players aren't empty half the fight.
            PlayerTuning punishing = PlayerTuning.CreatePunishing();
            Assert.AreEqual(0.65f, punishing.StaminaRegenDelay, 1e-5f);
            Assert.AreEqual(1.1f, punishing.EmptyStaminaRegenDelay, 1e-5f);
            Assert.AreEqual(38f, punishing.StaminaRegen, 1e-5f);
            Assert.Greater(punishing.EmptyStaminaRegenDelay, punishing.StaminaRegenDelay, "running dry still costs extra");

            t.MaxStamina = 6f;                                   // one dodge empties it
            var empty = new PlayerDriver(t);
            empty.Tap(Pad.Dodge, Forward);
            Assert.AreEqual(0f, empty.Model.Stamina);
            empty.Run(empty.FramesToReach(t.EmptyStaminaRegenDelay) - 3);
            Assert.AreEqual(0f, empty.Model.Stamina, "still waiting after the normal delay");
            empty.Run(6);
            Assert.Greater(empty.Model.Stamina, 0f);

            var partial = new PlayerDriver();
            partial.Tap(Pad.Dodge, Forward);
            float left = partial.Model.Stamina;
            partial.Run(partial.FramesToReach(0.6f) + 2);
            Assert.Greater(partial.Model.Stamina, left, "not empty: the normal delay applies");
        }

        [Test]
        public void TuningFromThePlaytestReportIsSeeded()
        {
            ElementMoveSet fluid = ElementMoveSet.CreateFireFluid(), punishing = ElementMoveSet.CreateFirePunishing();
            foreach (ElementMoveSet set in new[] { fluid, punishing })
            {
                Assert.AreEqual(22f, set.Skill.StaminaCost, 1e-5f);
                Assert.AreEqual(13f, set.Skill.Damage, 1e-5f);
                Assert.AreEqual(1.2f, set.Charge.ChargedDamageMultiplier, 1e-5f);
                Assert.AreEqual(0.2f, set.SprintAttackMinSprintTime, 1e-5f);
                Assert.AreEqual(0.15f, set.SprintAttackGrace, 1e-5f);
                Assert.AreEqual(10f, set.LightChain[2].MomentumGain, 1e-5f);
                Assert.AreEqual(6f, set.Momentum.BackOffRadius, 1e-5f);
            }
            Assert.AreEqual(0.35f, PlayerTuning.CreateFluid().QueuedPressMaxAge, 1e-5f);
            Assert.AreEqual(0.65f, PlayerTuning.CreatePunishing().StaminaRegenDelay, 1e-5f);   // round 2 (report 02, NEW-02)
        }

        // ---------------------------------------------------------------- Momentum back-off without lock-on

        static float MomentumLostBackingAway(float enemyDistance)
        {
            var d = new PlayerDriver();
            d.World.HasNearestEnemy = true;
            d.World.NearestEnemyPosition = new Vector3(0f, 0f, enemyDistance);
            d.Step(Pad.Guard);
            Assert.AreEqual(HitOutcome.Parried, d.HitFromFront(10f).Outcome);   // +25 Momentum
            d.Run(12);
            float before = d.Model.Momentum;
            for (int i = 0; i < 30; i++)
            {
                d.World.NearestEnemyPosition = d.World.Position + new Vector3(0f, 0f, enemyDistance);
                d.Step(Pad.None, new Vector2(0f, -1f));          // walk straight away from it
            }
            return before - d.Model.Momentum;
        }

        [Test]
        public void BackingAwayFromANearbyEnemyDrainsMomentumWithoutLockOn()
        {
            MomentumSettings m = new MomentumSettings();
            Assert.Greater(MomentumLostBackingAway(3f), m.BackOffDrainRate * 0.35f, "enemy 3 m away: backing off drains");
            Assert.AreEqual(0f, MomentumLostBackingAway(m.BackOffRadius + 2f), 1e-4f, "beyond BackOffRadius: no drain yet");
        }

        // ---------------------------------------------------------------- CTRL-01: buffered dodge/guard vs later attacks
        // (default pending David's answer; PlayerTuning.DefensivePressesWin flips it)

        static PlayerDriver PressSequence(bool defensiveWins, params (int frame, Pad pad)[] presses)
        {
            var t = PlayerTuning.CreateFluid();
            t.DefensivePressesWin = defensiveWins;
            var d = new PlayerDriver(t);
            for (int frame = 0; frame < 60; frame++)
            {
                Pad pad = Pad.None;
                foreach (var press in presses) if (press.frame == frame) pad = press.pad;
                d.Step(pad, Forward);
            }
            return d;
        }

        [Test]
        public void BufferedDodgeSurvivesALaterAttackPress()
        {
            // The report's nervous double-tap: light, dodge, light again during the jab.
            PlayerDriver d = PressSequence(true, (0, Pad.Light), (5, Pad.Dodge), (8, Pad.Light));
            Assert.AreEqual(1, d.Count(PlayerEventType.DodgeStarted), "the escape still happens");
            Assert.AreEqual(1, d.Count(PlayerEventType.AttackStarted), "and the extra light press doesn't");
            Assert.AreEqual(d.FramesToReach(d.Model.MoveSet.LightChain[0].DodgeCancelAt), d.FirstFrame(PlayerEventType.DodgeStarted));

            PlayerDriver heavy = PressSequence(true, (0, Pad.Light), (10, Pad.Dodge), (12, Pad.Heavy));
            Assert.AreEqual(1, heavy.Count(PlayerEventType.DodgeStarted));
            Assert.AreEqual(0, heavy.Count(PlayerEventType.ChargeStarted));

            PlayerDriver lastWins = PressSequence(false, (0, Pad.Light), (5, Pad.Dodge), (8, Pad.Light));
            Assert.AreEqual(0, lastWins.Count(PlayerEventType.DodgeStarted), "flipped: the last press wins (Elden Ring)");
            Assert.AreEqual(2, lastWins.Count(PlayerEventType.AttackStarted));
        }

        [Test]
        public void AttacksStillReplaceAttacksAndDodgeStillReplacesAttacks()
        {
            PlayerDriver d = PressSequence(true, (0, Pad.Light), (4, Pad.Heavy), (6, Pad.Light));
            Assert.AreEqual(0, d.Count(PlayerEventType.ChargeStarted), "the later light replaced the buffered heavy");
            Assert.AreEqual("Flame Cross", d.All(PlayerEventType.AttackStarted)[1].Move.DisplayName);

            PlayerDriver dodge = PressSequence(true, (0, Pad.Light), (5, Pad.Light), (8, Pad.Dodge));
            Assert.AreEqual(1, dodge.Count(PlayerEventType.DodgeStarted));
            Assert.AreEqual(1, dodge.Count(PlayerEventType.AttackStarted));
        }

        [Test]
        public void GuardPressedDuringAnAttackIsBufferedAndRaisedAtTheCancelPoint()
        {
            var d = PlayerDriver.Blocking();   // held guard: a blocking element
            MoveData jab = d.Model.MoveSet.LightChain[0];
            d.Step(Pad.Light);
            d.Run(3);
            d.Step(Pad.Guard);                                   // too early to guard: buffered
            d.Step(Pad.Guard | Pad.Light);                       // a later attack press doesn't replace it
            Assert.AreEqual(PlayerCommand.Guard, d.Model.BufferedCommand);
            d.RunUntil(x => x.Model.State == PlayerState.Guarding, 30, Pad.Guard);
            Assert.AreEqual(d.FramesToReach(jab.DodgeCancelAt), d.FirstFrame(PlayerEventType.GuardStarted));
            Assert.AreEqual(1, d.Count(PlayerEventType.AttackStarted));
            Assert.IsFalse(d.Model.IsDeflectWindowOpen, "the deflect window is timed from the real press, long gone");
        }

        // ---------------------------------------------------------------- ABIL-02: perfect dodge "would have landed"
        // (default pending David's answer; DodgeProfile.PerfectRule flips it)

        static MoveData QuickSlash()
        {
            return EnemyTuning.CreateDaoSoldier().Attacks[0].Move;
        }

        // A soldier 1.6 m in front swings at where the player stood; the player dodges on 'stick' a few frames before.
        static PlayerDriver DodgeAStrike(Vector2 stick, int framesBeforeStrike, PerfectDodgeRule rule, out bool awarded)
        {
            var moves = ElementMoveSet.CreateFireFluid();
            moves.Dodge.PerfectRule = rule;
            var d = new PlayerDriver(null, moves);
            Vector3 soldier = new Vector3(0f, 0f, 1.6f);
            d.LockOn(soldier);
            d.Step(Pad.Dodge, stick);
            d.Run(framesBeforeStrike - 1, Pad.None, stick);
            MoveData slash = QuickSlash();
            Vector3 origin = soldier + new Vector3(0f, slash.OriginHeight, 0f) + new Vector3(0f, 0f, -1f) * slash.OriginForward;
            awarded = d.Model.NotifyEnemyStrike(origin, new Vector3(0f, 0f, -1f), slash, soldier);
            d.Step(Pad.None, stick);
            return d;
        }

        [Test]
        public void DodgingAwayAtTheLastMomentCountsAsPerfect()
        {
            PlayerDriver d = DodgeAStrike(new Vector2(0f, -1f), 4, PerfectDodgeRule.WouldHaveLanded, out bool awarded);
            Assert.IsTrue(awarded, "the swing would have landed where you stood");
            Assert.Greater(Directions.Flatten(d.World.Position - new Vector3(0f, 0f, 1.6f)).Length(), 2.9f, "and it can't reach you now");
            Assert.AreEqual(1, d.Count(PlayerEventType.PerfectDodge));
            // (Momentum itself is already draining: dashing away from the lock target is backing off.)
            Assert.AreEqual(d.Model.MoveSet.Dodge.PerfectMomentumGain, d.All(PlayerEventType.PerfectDodge)[0].Amount, 1e-3f,
                "no bonus for dodging away");
            Assert.IsTrue(d.Model.IsCounterWindowOpen);
        }

        [Test]
        public void DashingThroughTheAttackEarnsTheBonus()
        {
            PlayerDriver d = DodgeAStrike(new Vector2(0f, 1f), 4, PerfectDodgeRule.WouldHaveLanded, out bool awarded);
            Assert.IsTrue(awarded);
            DodgeProfile p = d.Model.MoveSet.Dodge;
            Assert.AreEqual(p.PerfectMomentumGain + p.PerfectTowardBonus, d.Model.Momentum, 1e-3f);
            Assert.AreEqual(p.PerfectMomentumGain + p.PerfectTowardBonus, d.All(PlayerEventType.PerfectDodge)[0].Amount, 1e-3f);
        }

        [Test]
        public void TooEarlyOrMissingAnywayOrTheOldRuleIsNotPerfect()
        {
            DodgeAStrike(new Vector2(0f, -1f), 12, PerfectDodgeRule.WouldHaveLanded, out bool early);
            Assert.IsFalse(early, "dodged too early (outside the perfect window)");
            DodgeAStrike(new Vector2(0f, -1f), 1, PerfectDodgeRule.WouldHaveLanded, out bool beforeIFrames);
            Assert.IsFalse(beforeIFrames, "the i-frames weren't up yet");
            DodgeAStrike(new Vector2(0f, -1f), 4, PerfectDodgeRule.SwingMustReachYou, out bool oldRule);
            Assert.IsFalse(oldRule, "flipped to the old rule: a swing that no longer reaches you doesn't count");

            // A strike aimed well away from where you started isn't a perfect dodge either.
            var d = new PlayerDriver();
            d.Step(Pad.Dodge, new Vector2(1f, 0f));
            d.Run(3, Pad.None, new Vector2(1f, 0f));
            MoveData slash = QuickSlash();
            Assert.IsFalse(d.Model.NotifyEnemyStrike(new Vector3(8f, 1.1f, 8f), new Vector3(1f, 0f, 0f), slash, new Vector3(8f, 0f, 8f)));
        }

        [Test]
        public void OnlyOnePerfectDodgePerDodge()
        {
            DodgeAStrike(new Vector2(0f, -1f), 4, PerfectDodgeRule.WouldHaveLanded, out bool first);
            var d = new PlayerDriver();
            Vector3 soldier = new Vector3(0f, 0f, 1.6f);
            d.Step(Pad.Dodge, new Vector2(0f, -1f));
            d.Run(3, Pad.None, new Vector2(0f, -1f));
            MoveData slash = QuickSlash();
            Vector3 origin = soldier + new Vector3(0f, slash.OriginHeight, -slash.OriginForward);
            Assert.IsTrue(first);
            Assert.IsTrue(d.Model.NotifyEnemyStrike(origin, new Vector3(0f, 0f, -1f), slash, soldier));
            Assert.IsFalse(d.Model.NotifyEnemyStrike(origin, new Vector3(0f, 0f, -1f), slash, soldier), "second strike, same dodge");
            Assert.AreEqual(HitOutcome.Evaded, d.HitFromFront(10f).Outcome, "a real hit after it is a plain evade");
        }

        // ---------------------------------------------------------------- soft lock height

        [Test]
        public void SoftLockIgnoresEnemiesFarAboveOrBelow()
        {
            var t = PlayerTuning.CreateFluid();
            Assert.IsTrue(SoftLockSelector.TryScore(Vector3.Zero, 0f, new Vector3(0f, 1.2f, 2f), t, out _));
            Assert.IsFalse(SoftLockSelector.TryScore(Vector3.Zero, 0f, new Vector3(0f, 2.5f, 2f), t, out _), "up on a ledge");
            Assert.IsFalse(SoftLockSelector.TryScore(Vector3.Zero, 0f, new Vector3(0f, -2.5f, 2f), t, out _), "down below");
            Assert.IsTrue(SoftLockSelector.TryScore(Vector3.Zero, 0f, new Vector3(0f, 2.5f, 2f), 4f, 60f, out _), "old overload: no height rule");
        }
    }
}
