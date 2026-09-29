using System;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Pins dodging, guarding, deflecting, healing and what happens to incoming hits.
    public class CombatDefenseTests
    {
        static readonly Vector2 Forward = new Vector2(0f, 1f);

        [Test]
        public void DodgeIFramesMatchTheProfile()
        {
            var d = new PlayerDriver();
            DodgeProfile dodge = d.Model.MoveSet.Dodge;
            d.Step(Pad.Dodge, Forward);
            Assert.IsFalse(d.Model.IsInvulnerable, "first frame of the dash is still vulnerable (i-frames start at 0.02)");
            int firstInvulnerable = -1, lastInvulnerable = -1;
            for (int frame = 1; frame < 30; frame++)
            {
                d.Step(Pad.None, Forward);
                if (!d.Model.IsInvulnerable) continue;
                if (firstInvulnerable < 0) firstInvulnerable = frame;
                lastInvulnerable = frame;
            }
            Assert.AreEqual(2, firstInvulnerable);
            Assert.AreEqual(14, lastInvulnerable, "0.24 s = vulnerable again from frame 15");
            Assert.That(firstInvulnerable * d.Dt, Is.GreaterThanOrEqualTo(dodge.IFrameStart - 1e-4f));
            Assert.That(lastInvulnerable * d.Dt, Is.LessThan(dodge.IFrameEnd));
        }

        [Test]
        public void HitInTheFirstMomentsIsAPerfectDodgeOnlyOnce()
        {
            var d = new PlayerDriver();
            d.LockOn(new Vector3(0f, 0f, 3f));
            d.Step(Pad.Dodge, new Vector2(1f, 0f));
            d.Run(3, Pad.None, new Vector2(1f, 0f));             // 0.05 s into the dodge
            HitResult first = d.HitFromFront(20f);
            HitResult second = d.HitFromFront(20f);
            Assert.AreEqual(HitOutcome.PerfectEvade, first.Outcome);
            Assert.AreEqual(HitOutcome.Evaded, second.Outcome, "the reward is once per dodge");
            Assert.AreEqual(d.Model.MaxHealth, d.Model.Health);
            Assert.That(d.Model.Momentum, Is.EqualTo(d.Model.MoveSet.Dodge.PerfectMomentumGain).Within(1e-4f));
            Assert.IsTrue(d.Model.IsCounterWindowOpen);

            d.Step(Pad.None, new Vector2(1f, 0f));
            var perfect = d.All(PlayerEventType.PerfectDodge);
            Assert.AreEqual(1, perfect.Count, "delivered with the next Tick");
            Assert.AreEqual(d.Model.MoveSet.Dodge.PerfectSlowMoScale, perfect[0].TimeScale);
            Assert.AreEqual(d.Model.MoveSet.Dodge.PerfectSlowMoDuration, perfect[0].Duration);
        }

        [Test]
        public void HitAfterThePerfectWindowIsAPlainEvade()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Dodge, Forward);
            d.Run(d.FramesToReach(d.Model.MoveSet.Dodge.PerfectWindow) + 1, Pad.None, Forward);
            Assert.IsTrue(d.Model.IsInvulnerable);
            Assert.AreEqual(HitOutcome.Evaded, d.HitFromFront(20f).Outcome);
            Assert.AreEqual(0f, d.Model.Momentum);
        }

        [Test]
        public void CounterWindowBoostsTheFirstAttackStartedInsideIt()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Dodge, Forward);
            d.Run(3, Pad.None, Forward);
            Assert.AreEqual(HitOutcome.PerfectEvade, d.HitFromFront(20f).Outcome);
            d.Step(Pad.Light, Forward);
            d.RunUntil(x => x.Model.State == PlayerState.Attacking, 30);
            Assert.IsTrue(d.Model.IsCounterAttack);
            MoveData jab = d.Model.CurrentMove;
            DamageInfo counter = d.Model.BuildCurrentDamage();
            float expected = jab.Damage * d.Model.MomentumMultiplier * d.Model.MoveSet.Dodge.CounterDamageMultiplier;
            Assert.That(counter.Damage, Is.EqualTo(expected).Within(1e-3f));
            Assert.IsFalse(d.Model.IsCounterWindowOpen, "used up by that attack");
        }

        // The key anti-exploit rule: however you time dodge presses, there's always a vulnerable gap of at
        // least ChainIFrameGap between one dodge's i-frames and the next. Swept over press rhythms and phases,
        // both presets, several frame rates, and a deliberately broken tuning (next dodge long before the
        // i-frames end) to prove the rule is enforced by code, not just by good numbers.
        [Test]
        public void ChainedDodgesNeverGiveContinuousInvulnerability()
        {
            var broken = ElementMoveSet.CreateFireFluid();
            broken.Dodge.NextDodgeAt = 0.05f;
            broken.Dodge.IFrameStart = 0f;
            broken.Dodge.IFrameEnd = 0.3f;
            broken.Dodge.StaminaCost = 0f;

            foreach (float fps in new[] { 30f, 60f, 144f })
            {
                foreach (int preset in new[] { 0, 1, 2 })
                {
                    for (int interval = 1; interval <= 40; interval += 1)
                    {
                        for (int phase = 0; phase < Math.Min(interval, 4); phase++)
                        {
                            PlayerDriver d = preset == 0 ? new PlayerDriver(null, null, fps)
                                : preset == 1 ? PlayerDriver.Punishing(fps) : new PlayerDriver(null, broken, fps);
                            d.Model.Tuning.StaminaRegen = 1000f;       // never run out: spam as much as possible
                            DodgeProfile profile = d.Model.MoveSet.Dodge;
                            CheckDodgeSpam(d, profile, interval, phase, fps);
                        }
                    }
                }
            }
        }

        static void CheckDodgeSpam(PlayerDriver d, DodgeProfile profile, int interval, int phase, float fps)
        {
            int frames = (int)(3f * fps);
            int run = 0, longestRun = 0, gap = 0, runsSeen = 0;
            float maxRun = profile.IFrameEnd - profile.IFrameStart + d.Dt + 1e-4f;
            string context = d.Model.Tuning.PresetName + " " + fps + "fps every " + interval + " frames, phase " + phase;
            for (int f = 0; f < frames; f++)
            {
                // Pressed on the chosen rhythm; the release (for OnRelease) comes one frame later.
                bool press = f % interval == phase;
                d.Step(press ? Pad.Dodge : Pad.None, Forward);
                if (d.Model.IsInvulnerable)
                {
                    if (run == 0 && runsSeen > 0)
                    {
                        float gapTime = gap * d.Dt;
                        Assert.GreaterOrEqual(gapTime, profile.ChainIFrameGap - d.Dt - 1e-4f, context);
                    }
                    run++;
                    gap = 0;
                    longestRun = Math.Max(longestRun, run);
                }
                else
                {
                    if (run > 0) runsSeen++;
                    run = 0;
                    gap++;
                }
            }
            Assert.LessOrEqual(longestRun * d.Dt, maxRun, context + ": one run is at most one dodge's i-frames");
        }

        [Test]
        public void GuardBlocksFromTheFrontForStaminaButNotFromBehind()
        {
            var d = new PlayerDriver();
            d.Run(12, Pad.Guard);                                // held past the deflect window
            Assert.AreEqual(PlayerState.Guarding, d.Model.State);
            float stamina = d.Model.Stamina;
            HitResult front = d.Model.ReceiveHit(PlayerDriver.EnemyHit(20f, 10f, d.Model.Forward, true, 15f), d.Model.Forward);
            Assert.AreEqual(HitOutcome.Blocked, front.Outcome);
            Assert.AreEqual(d.Model.MaxHealth, d.Model.Health);
            Assert.That(d.Model.Stamina, Is.EqualTo(stamina - 15f).Within(1e-4f));

            // 90 degrees off is still inside a 160 degree arc? No: the arc is +-80, so a side hit gets through.
            HitResult side = d.Model.ReceiveHit(PlayerDriver.EnemyHit(20f, 0f, new Vector3(1f, 0f, 0f)), d.Model.Forward);
            Assert.AreEqual(HitOutcome.Hit, side.Outcome);
            HitResult back = d.Model.ReceiveHit(PlayerDriver.EnemyHit(10f, 0f, -d.Model.Forward), d.Model.Forward);
            Assert.AreEqual(HitOutcome.Hit, back.Outcome);
            Assert.That(d.Model.Health, Is.EqualTo(d.Model.MaxHealth - 30f).Within(1e-4f));

            HitResult unblockable = d.Model.ReceiveHit(PlayerDriver.EnemyHit(5f, 0f, d.Model.Forward, true, 15f, true), d.Model.Forward);
            Assert.AreEqual(HitOutcome.Hit, unblockable.Outcome);
        }

        [Test]
        public void BlockingWithTooLittleStaminaBreaksTheGuard()
        {
            var d = new PlayerDriver();
            d.Run(12, Pad.Guard);
            HitResult result = d.Model.ReceiveHit(PlayerDriver.EnemyHit(30f, 0f, d.Model.Forward, true, 150f), d.Model.Forward);
            Assert.AreEqual(HitOutcome.GuardBroken, result.Outcome);
            Assert.IsTrue(result.PoiseBroken);
            Assert.AreEqual(PlayerState.Staggered, d.Model.State);
            Assert.AreEqual(0f, d.Model.Stamina);
            Assert.AreEqual(d.Model.MaxHealth, d.Model.Health, "a guard break stuns but the hit is still blocked");
            int stagger = d.FramesToReach(d.Model.MoveSet.Guard.GuardBreakStagger);
            d.Run(stagger - 1, Pad.Guard);
            Assert.AreEqual(PlayerState.Staggered, d.Model.State);
            d.Run(2, Pad.Guard);
            Assert.AreNotEqual(PlayerState.Staggered, d.Model.State);
        }

        [Test]
        public void GuardPressJustBeforeAParryableHitDeflectsIt()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Guard);
            d.Run(5, Pad.Guard);                                 // 0.083 s later: inside the 0.15 s window
            float stamina = d.Model.Stamina;
            Assert.IsTrue(d.Model.IsDeflectWindowOpen);
            HitResult result = d.HitFromFront(20f, 10f, true, 15f);
            Assert.AreEqual(HitOutcome.Parried, result.Outcome, "the attacker is told it was parried");
            Assert.AreEqual(stamina, d.Model.Stamina, "deflects cost nothing");
            Assert.AreEqual(d.Model.MaxHealth, d.Model.Health);
            Assert.That(d.Model.Momentum, Is.EqualTo(d.Model.MoveSet.Guard.DeflectMomentumGain).Within(1e-4f));
            d.Step(Pad.Guard);
            Assert.AreEqual(1, d.Count(PlayerEventType.Deflected));
        }

        [Test]
        public void NonParryableHitsAndLatePressesAreOnlyBlocked()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Guard);
            d.Run(3, Pad.Guard);
            Assert.AreEqual(HitOutcome.Blocked, d.HitFromFront(20f, 10f, false, 15f).Outcome);

            var late = new PlayerDriver();
            late.Step(Pad.Guard);
            late.Run(late.FramesToReach(late.Model.MoveSet.Guard.DeflectWindow) + 1, Pad.Guard);
            Assert.AreEqual(HitOutcome.Blocked, late.HitFromFront(20f, 10f, true, 15f).Outcome);
        }

        [Test]
        public void TappedGuardStillDeflectsInsideItsWindow()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Guard);
            d.Run(4);                                            // already let go
            Assert.AreEqual(PlayerState.Guarding, d.Model.State, "a tap keeps the guard up for the deflect window");
            Assert.AreEqual(HitOutcome.Parried, d.HitFromFront(20f).Outcome);
            d.Run(20);
            Assert.AreEqual(PlayerState.Locomotion, d.Model.State);
        }

        [Test]
        public void WhiffedDeflectLocksDeflectingOutBriefly()
        {
            var d = new PlayerDriver();
            GuardSettings guard = d.Model.MoveSet.Guard;
            d.Step(Pad.Guard);
            d.Run(d.FramesToReach(guard.DeflectWindow) + 2);    // window closes having caught nothing
            Assert.Greater(d.Model.DeflectLockoutRemaining, 0f);
            Assert.AreEqual(1, d.Count(PlayerEventType.DeflectWhiffed));

            d.Step(Pad.Guard);                                   // mash: pressed again during the lockout
            d.Step(Pad.Guard);
            Assert.AreEqual(PlayerState.Guarding, d.Model.State, "still guards");
            Assert.IsFalse(d.Model.IsDeflectWindowOpen);
            Assert.AreEqual(HitOutcome.Blocked, d.HitFromFront(10f, 0f, true, 5f).Outcome);

            d.Step(Pad.None);
            d.RunUntil(x => x.Model.DeflectLockoutRemaining <= 0f, 60);
            d.Step(Pad.Guard);
            Assert.AreEqual(HitOutcome.Parried, d.HitFromFront(10f).Outcome, "works again after the lockout");
        }

        [Test]
        public void GuardPressCutsAttackRecoveryShortFromTheDodgeCancelPoint()
        {
            var d = new PlayerDriver();
            MoveData jab = d.Model.MoveSet.LightChain[0];
            d.Step(Pad.Light);
            d.Run(d.FramesToReach(jab.DodgeCancelAt));
            d.Step(Pad.Guard);
            Assert.AreEqual(PlayerState.Guarding, d.Model.State);
            Assert.AreEqual(HitOutcome.Parried, d.HitFromFront(10f).Outcome);
        }

        [Test]
        public void GuardPressedAfterABufferedAttackWins()
        {
            // Light, a queued follow-up light, then guard pressed at the defensive cancel point: the guard is the
            // latest intention, so the queued attack must not fire and knock the guard down.
            var d = new PlayerDriver();
            MoveData jab = d.Model.MoveSet.LightChain[0];
            d.Step(Pad.Light);
            d.Run(3);
            d.Step(Pad.Light);
            int cancelFrame = d.FramesToReach(jab.DodgeCancelAt);
            d.Run(cancelFrame - d.Frame - 1);
            Assert.IsTrue(d.Model.BufferedCommandQueued, "the follow-up was queued in the combo window");
            d.Step(Pad.Guard);
            Assert.AreEqual(PlayerState.Guarding, d.Model.State);
            d.Run(30, Pad.Guard);
            Assert.AreEqual(1, d.Count(PlayerEventType.AttackStarted));
            Assert.AreEqual(PlayerState.Guarding, d.Model.State);
        }

        [Test]
        public void HealIsCommittedAndTheChargeIsUsedOnlyAtTheApplyTime()
        {
            var d = new PlayerDriver();
            PlayerTuning t = d.Model.Tuning;
            Assert.AreEqual(HitOutcome.Hit, d.Model.ReceiveHit(PlayerDriver.EnemyHit(60f, 0f, d.Model.Forward), d.Model.Forward).Outcome);
            d.Step(Pad.Heal);
            Assert.AreEqual(PlayerState.Healing, d.Model.State);
            int applyFrame = d.FramesToReach(t.HealApplyTime);
            d.Run(applyFrame - 1);
            Assert.AreEqual(3, d.Model.HealCharges, "not used yet");
            Assert.That(d.Model.Health, Is.EqualTo(40f).Within(1e-4f));
            d.Step();
            Assert.AreEqual(2, d.Model.HealCharges);
            Assert.That(d.Model.Health, Is.EqualTo(40f + t.HealAmount).Within(1e-4f));
            Assert.AreEqual(applyFrame, d.FirstFrame(PlayerEventType.HealApplied));

            d.Tap(Pad.Dodge, Forward);                           // drinking can't be cancelled
            Assert.AreEqual(PlayerState.Healing, d.Model.State);
            d.RunUntil(x => x.Model.State != PlayerState.Healing, 60, Pad.None, Forward);
        }

        [Test]
        public void StaggerCancelsTheHealAndKeepsTheCharge()
        {
            var d = new PlayerDriver();
            d.Model.ReceiveHit(PlayerDriver.EnemyHit(50f, 0f, d.Model.Forward), d.Model.Forward);
            d.Step(Pad.Heal);
            d.Run(15);
            HitResult hit = d.Model.ReceiveHit(PlayerDriver.EnemyHit(5f, 100f, d.Model.Forward), d.Model.Forward);
            Assert.IsTrue(hit.PoiseBroken);
            d.Run(60);
            Assert.AreEqual(3, d.Model.HealCharges);
            Assert.That(d.Model.Health, Is.EqualTo(45f).Within(1e-4f));
            Assert.AreEqual(1, d.Count(PlayerEventType.HealInterrupted));
            Assert.AreEqual(0, d.Count(PlayerEventType.HealApplied));
        }

        [Test]
        public void HealWithNoChargesDoesNothing()
        {
            var d = new PlayerDriver();
            for (int i = 0; i < 3; i++)
            {
                d.Tap(Pad.Heal);
                d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 90);
            }
            Assert.AreEqual(0, d.Model.HealCharges);
            d.Tap(Pad.Heal);
            Assert.AreEqual(PlayerState.Locomotion, d.Model.State);
            Assert.AreEqual(1, d.Count(PlayerEventType.HealFailed));
        }

        [Test]
        public void PoiseAbsorbsSmallHitsAndBreaksOnBigOnes()
        {
            var d = new PlayerDriver();
            HitResult small = d.Model.ReceiveHit(PlayerDriver.EnemyHit(5f, 12f, d.Model.Forward), d.Model.Forward);
            Assert.IsFalse(small.PoiseBroken);
            Assert.AreEqual(PlayerState.Locomotion, d.Model.State);
            HitResult second = d.Model.ReceiveHit(PlayerDriver.EnemyHit(5f, 20f, d.Model.Forward), d.Model.Forward);
            Assert.IsTrue(second.PoiseBroken, "12 + 20 > 30 poise");
            Assert.AreEqual(PlayerState.Staggered, d.Model.State);
        }

        [Test]
        public void HeavyHyperArmorShrugsOffStaggerDuringItsActiveFrames()
        {
            var d = new PlayerDriver();
            d.Tap(Pad.Heavy);
            d.RunUntil(x => x.Model.IsAttackActive, 40);
            HitResult hit = d.Model.ReceiveHit(PlayerDriver.EnemyHit(10f, 100f, d.Model.Forward), d.Model.Forward);
            Assert.AreEqual(HitOutcome.Hit, hit.Outcome);
            Assert.IsFalse(hit.PoiseBroken);
            Assert.AreEqual(PlayerState.Attacking, d.Model.State);
        }

        [Test]
        public void DeathIgnoresFurtherHitsAndRespawnRestoresEverything()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Light);
            d.Run(9);
            Assert.IsTrue(d.Model.IsAttackActive);
            HitResult kill = d.Model.ReceiveHit(PlayerDriver.EnemyHit(500f, 0f, d.Model.Forward), d.Model.Forward);
            Assert.IsTrue(kill.Killed);
            Assert.AreEqual(PlayerState.Dead, d.Model.State);
            Assert.IsFalse(d.Model.IsAttackActive);
            Assert.AreEqual(HitOutcome.Ignored, d.HitFromFront(10f).Outcome);
            d.Step(Pad.Light);
            Assert.AreEqual(1, d.Count(PlayerEventType.Died));
            Assert.AreEqual(d.Count(PlayerEventType.AttackActiveStart), d.Count(PlayerEventType.AttackActiveEnd), "window closed on death");
            d.Run(30, Pad.Light);
            Assert.AreEqual(PlayerState.Dead, d.Model.State);

            d.Model.Respawn(90f);
            d.Step();
            Assert.AreEqual(PlayerState.Locomotion, d.Model.State);
            Assert.AreEqual(d.Model.MaxHealth, d.Model.Health);
            Assert.AreEqual(d.Model.MaxStamina, d.Model.Stamina);
            Assert.AreEqual(3, d.Model.HealCharges);
            Assert.AreEqual(0f, d.Model.Momentum);
            Assert.AreEqual(90f, d.Model.FacingYaw, 1e-4f);
            Assert.AreEqual(1, d.Count(PlayerEventType.Respawned));
        }

        [Test]
        public void SameAttackCannotHitTwice()
        {
            var d = new PlayerDriver();
            DamageInfo hit = PlayerDriver.EnemyHit(10f, 0f, d.Model.Forward);
            Assert.AreEqual(HitOutcome.Hit, d.Model.ReceiveHit(hit, d.Model.Forward).Outcome);
            Assert.AreEqual(HitOutcome.Ignored, d.Model.ReceiveHit(hit, d.Model.Forward).Outcome);
            DamageInfo friendly = PlayerDriver.EnemyHit(10f, 0f, d.Model.Forward);
            friendly.SourceTeam = Team.Player;
            Assert.AreEqual(HitOutcome.Ignored, d.Model.ReceiveHit(friendly, d.Model.Forward).Outcome);
        }

        [Test]
        public void StaggerMidSwingClosesTheActiveWindowAndParryStaggersUs()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Light);
            d.RunUntil(x => x.Model.IsAttackActive, 20);
            d.Model.OnParried();
            Assert.AreEqual(PlayerState.Staggered, d.Model.State);
            d.Step();
            Assert.AreEqual(1, d.Count(PlayerEventType.Parried));
            Assert.AreEqual(1, d.Count(PlayerEventType.AttackActiveEnd));
            Assert.AreEqual(1, d.Count(PlayerEventType.AttackEnded));
            d.Run(d.FramesToReach(d.Model.Tuning.ParriedStaggerDuration) + 1);
            Assert.AreEqual(PlayerState.Locomotion, d.Model.State);
            Assert.AreEqual(1, d.Count(PlayerEventType.StaggerEnded));
        }
    }
}
