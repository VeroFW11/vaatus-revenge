using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Pins the charged heavy (fa jin), plunge, skill, Momentum, presets, live tuning, determinism and NaN safety.
    public class CombatResourceTests
    {
        static readonly Vector2 Forward = new Vector2(0f, 1f);

        static PlayerEvent ReleaseHeavyAfter(float seconds, out PlayerDriver driver)
        {
            // Pressed on frame 0 (charge time 0); released on frame K, when the charge time is K frames.
            var d = new PlayerDriver();
            int releaseFrame = d.FramesToReach(seconds);
            d.Step(Pad.Heavy);
            d.Run(releaseFrame - 1, Pad.Heavy);
            d.Run(40);
            driver = d;
            Assert.AreEqual(1, d.Count(PlayerEventType.AttackStarted), "released at " + seconds);
            return d.All(PlayerEventType.AttackStarted)[0];
        }

        [Test]
        public void FaJinOnlyWhenReleasedInsideTheSweetSpot()
        {
            ChargeSettings c = new ChargeSettings();
            Assert.AreEqual(ChargeTier.Quick, ReleaseHeavyAfter(c.QuickReleaseTime * 0.5f, out _).ChargeTier);
            Assert.AreEqual(ChargeTier.Partial, ReleaseHeavyAfter((c.QuickReleaseTime + c.SweetSpotStart) * 0.5f, out _).ChargeTier);
            Assert.AreEqual(ChargeTier.Partial, ReleaseHeavyAfter(c.SweetSpotStart - 0.03f, out _).ChargeTier);
            Assert.AreEqual(ChargeTier.FaJin, ReleaseHeavyAfter(c.SweetSpotStart + 0.01f, out _).ChargeTier);
            Assert.AreEqual(ChargeTier.FaJin, ReleaseHeavyAfter(c.SweetSpotEnd - 0.01f, out _).ChargeTier);
            Assert.AreEqual(ChargeTier.Charged, ReleaseHeavyAfter(c.SweetSpotEnd + 0.03f, out _).ChargeTier);
        }

        [Test]
        public void HoldingToMaxChargeAutoReleasesAsCharged()
        {
            var d = new PlayerDriver();
            ChargeSettings c = d.Model.MoveSet.Charge;
            d.Step(Pad.Heavy);
            d.Run(d.FramesToReach(c.MaxChargeTime) + 30, Pad.Heavy);
            var starts = d.All(PlayerEventType.AttackStarted);
            Assert.AreEqual(1, starts.Count);
            Assert.AreEqual(ChargeTier.Charged, starts[0].ChargeTier);
            Assert.AreEqual(d.FramesToReach(c.MaxChargeTime), d.FirstFrame(PlayerEventType.AttackStarted));
        }

        [Test]
        public void SweetSpotFlashEventFiresOnceWhenItOpens()
        {
            var d = new PlayerDriver();
            ChargeSettings c = d.Model.MoveSet.Charge;
            d.Step(Pad.Heavy);
            d.Run(d.FramesToReach(c.SweetSpotStart) - 1, Pad.Heavy);
            Assert.AreEqual(0, d.Count(PlayerEventType.ChargeSweetSpot));
            Assert.IsFalse(d.Model.InSweetSpot);
            d.Step(Pad.Heavy);
            Assert.AreEqual(1, d.Count(PlayerEventType.ChargeSweetSpot));
            Assert.IsTrue(d.Model.InSweetSpot);
            Assert.That(d.Model.ChargeLevel, Is.EqualTo(c.SweetSpotStart / c.MaxChargeTime).Within(0.02f));
            d.Run(10, Pad.Heavy);
            Assert.AreEqual(1, d.Count(PlayerEventType.ChargeSweetSpot));
        }

        [Test]
        public void FaJinAndChargedMultipliersReachTheDamage()
        {
            var d = new PlayerDriver();
            MoveData heavy = d.Model.MoveSet.Heavy;
            ChargeSettings c = d.Model.MoveSet.Charge;
            DamageInfo quick = d.Model.BuildDamage(heavy, 5, ChargeTier.Quick);
            DamageInfo faJin = d.Model.BuildDamage(heavy, 6, ChargeTier.FaJin);
            DamageInfo charged = d.Model.BuildDamage(heavy, 7, ChargeTier.Charged);
            Assert.AreEqual(heavy.Damage, quick.Damage, 1e-4f);
            Assert.AreEqual(heavy.Damage * c.FaJinDamageMultiplier, faJin.Damage, 1e-4f);
            Assert.AreEqual(heavy.PoiseDamage * c.FaJinPoiseMultiplier, faJin.PoiseDamage, 1e-4f);
            Assert.AreEqual(heavy.Hitstop + c.FaJinHitstopBonus, faJin.Hitstop, 1e-4f);
            Assert.AreEqual(heavy.Damage * c.ChargedDamageMultiplier, charged.Damage, 1e-4f);
            Assert.AreEqual(Team.Player, faJin.SourceTeam);
            Assert.AreEqual(6, faJin.AttackId);
            Assert.AreEqual(1, faJin.SourceId);
        }

        [Test]
        public void FaJinLandingGivesItsOwnMomentum()
        {
            PlayerEvent start = ReleaseHeavyAfter(0.8f, out PlayerDriver d);
            Assert.AreEqual(ChargeTier.FaJin, start.ChargeTier);
            d.Model.OnAttackLanded(new HitResult { Outcome = HitOutcome.Hit }, start.AttackId);
            Assert.AreEqual(d.Model.MoveSet.Charge.FaJinMomentumGain, d.Model.Momentum, 1e-4f);
        }

        [Test]
        public void PlungeDropsLandsWithARingAndRecovers()
        {
            var d = new PlayerDriver();
            ElementMoveSet set = d.Model.MoveSet;
            d.Step(Pad.Jump);
            Assert.AreEqual(PlayerState.Airborne, d.Model.State);
            d.RunUntil(x => x.Last.Velocity.Y <= 0f, 60);        // apex
            float stamina = d.Model.Stamina;
            d.Step(Pad.Light);
            Assert.AreEqual(PlayerState.Plunging, d.Model.State);
            Assert.That(d.Model.Stamina, Is.EqualTo(stamina - set.PlungeAttack.StaminaCost).Within(0.5f));
            Assert.AreEqual(0f, d.Last.Velocity.Y, 1e-4f, "hangs first");
            d.Run(d.FramesToReach(set.Plunge.HangTime) + 1);
            Assert.AreEqual(-set.Plunge.FallSpeed, d.Last.Velocity.Y, 1e-4f, "then drops fast");
            d.RunUntil(x => x.Count(PlayerEventType.PlungeImpact) > 0, 120);

            PlayerEvent impact = d.All(PlayerEventType.PlungeImpact)[0];
            Assert.AreEqual(set.Plunge.RingRadius, impact.Radius);
            Assert.AreEqual(0f, impact.Origin.Y, 1e-3f);
            Assert.AreSame(set.PlungeAttack, impact.Move);
            Assert.AreNotEqual(0, impact.AttackId);
            Assert.AreEqual(AttackPhase.Recovery, d.Model.Phase);
            DamageInfo damage = d.Model.BuildDamage(impact);
            Assert.AreEqual(HitKind.Plunge, damage.Kind);

            d.Run(d.FramesToReach(set.PlungeAttack.Recovery) + 1);
            Assert.AreEqual(PlayerState.Locomotion, d.Model.State);
        }

        [Test]
        public void FireBlastLaunchesAtTheEndOfStartupTowardTheTarget()
        {
            var d = new PlayerDriver();
            d.LockOn(new Vector3(0f, 3f, 10f));                 // up on a platform
            d.Step(Pad.Skill);
            d.Run(40);
            var launches = d.All(PlayerEventType.ProjectileLaunched);
            Assert.AreEqual(1, launches.Count);
            PlayerEvent shot = launches[0];
            Assert.AreEqual(d.FramesToReach(d.Model.MoveSet.Skill.Startup), d.FirstFrame(PlayerEventType.ProjectileLaunched));
            Assert.IsTrue(shot.Move.LaunchesProjectile);
            Assert.AreEqual(1f, shot.Direction.Length(), 1e-4f);
            Assert.Greater(shot.Direction.Y, 0f, "aims up at the raised target");
            Assert.Greater(shot.Direction.Z, 0.8f);
            Assert.AreEqual(0, d.Count(PlayerEventType.AttackActiveStart), "projectiles don't open a melee window");
        }

        [Test]
        public void MomentumBuildsOnCleanHitsOnlyOncePerAttackAndCaps()
        {
            var d = new PlayerDriver();
            MomentumSettings m = d.Model.MoveSet.Momentum;
            d.Step(Pad.Light);
            int jabId = d.All(PlayerEventType.AttackStarted)[0].AttackId;
            d.Model.OnAttackLanded(new HitResult { Outcome = HitOutcome.Blocked }, jabId);
            Assert.AreEqual(0f, d.Model.Momentum, "blocked hits build nothing");
            d.Model.OnAttackLanded(new HitResult { Outcome = HitOutcome.Hit }, jabId);
            d.Model.OnAttackLanded(new HitResult { Outcome = HitOutcome.Hit }, jabId);
            Assert.AreEqual(8f, d.Model.Momentum, 1e-4f, "+8 once per attack");

            PlayerTuning plenty = PlayerTuning.CreateFluid();
            plenty.MaxStamina = 10000f;                          // this test is about Momentum, not about running out of stamina
            var capped = new PlayerDriver(plenty);
            for (int i = 0; i < 30; i++)
            {
                capped.Step(Pad.Light);
                capped.Model.OnAttackLanded(new HitResult { Outcome = HitOutcome.Hit });
                capped.Run(35);
            }
            Assert.AreEqual(m.Max, capped.Model.Momentum, 1e-3f);
            Assert.AreEqual(m.MaxDamageMultiplier, capped.Model.MomentumMultiplier, 1e-4f);
        }

        [Test]
        public void MomentumWaitsThenDecays()
        {
            var d = new PlayerDriver();
            MomentumSettings m = d.Model.MoveSet.Momentum;
            d.Step(Pad.Light);
            d.Model.OnAttackLanded(new HitResult { Outcome = HitOutcome.Hit });
            float start = d.Model.Momentum;
            d.Run(d.FramesToReach(m.DecayDelay) - 2);
            Assert.AreEqual(start, d.Model.Momentum, 1e-4f, "no drain during the delay");
            d.Run(d.FramesToReach(0.2f) + 2);
            Assert.That(d.Model.Momentum, Is.EqualTo(start - m.DecayRate * 0.2f).Within(0.5f));
            d.Run(120);
            Assert.AreEqual(0f, d.Model.Momentum);
            Assert.AreEqual(1f, d.Model.MomentumMultiplier, 1e-5f);
        }

        [Test]
        public void BackingAwayFromTheLockTargetDrainsMomentumAtOnce()
        {
            var d = new PlayerDriver();
            MomentumSettings m = d.Model.MoveSet.Momentum;
            d.LockOn(new Vector3(0f, 0f, 5f));
            d.Step(Pad.Guard);
            Assert.AreEqual(HitOutcome.Parried, d.HitFromFront(10f).Outcome);   // +25 Momentum
            d.Run(15);
            float before = d.Model.Momentum;
            Assert.AreEqual(d.Model.MoveSet.Guard.DeflectMomentumGain, before, 1e-4f);
            d.Run(30, Pad.None, new Vector2(0f, -1f));          // strafe straight back for 0.5 s
            float drained = before - d.Model.Momentum;
            Assert.That(drained, Is.GreaterThan(m.BackOffDrainRate * 0.4f), "well within the 1.6 s decay delay");

            var sideways = new PlayerDriver();
            sideways.LockOn(new Vector3(0f, 0f, 5f));
            sideways.Step(Pad.Light);
            sideways.Model.OnAttackLanded(new HitResult { Outcome = HitOutcome.Hit });
            float s0 = sideways.Model.Momentum;
            sideways.Run(30, Pad.None, new Vector2(1f, 0f));    // circling the target is not backing off
            Assert.AreEqual(s0, sideways.Model.Momentum, 1e-4f);
        }

        [Test]
        public void PunishingPresetDiffersFromFluid()
        {
            PlayerTuning fluid = PlayerTuning.CreateFluid(), punishing = PlayerTuning.CreatePunishing();
            ElementMoveSet fluidSet = ElementMoveSet.CreateFireFluid(), punishingSet = ElementMoveSet.CreateFirePunishing();
            Assert.AreEqual(DodgeTrigger.OnPress, fluid.DodgeTrigger);
            Assert.AreEqual(DodgeTrigger.OnRelease, punishing.DodgeTrigger);
            Assert.Greater(punishingSet.Dodge.StaminaCost, fluidSet.Dodge.StaminaCost);
            Assert.Less(punishing.StaminaRegen, fluid.StaminaRegen);
            Assert.Greater(punishing.StaminaRegenDelay, fluid.StaminaRegenDelay);
            Assert.Greater(punishing.SprintStaminaDrain, 0f);
            Assert.AreEqual(0f, fluid.SprintStaminaDrain);
            Assert.IsFalse(punishingSet.Dodge.PerfectDodgeEnabled);
            Assert.GreaterOrEqual(punishingSet.Dodge.AttackCancelAt + 1e-4f, punishingSet.Dodge.TotalDuration);
            Assert.AreEqual(13f, punishingSet.LightChain[0].StaminaCost);
            Assert.AreEqual(28f, punishingSet.Heavy.StaminaCost);
            Assert.AreEqual("Punishing", punishing.PresetName);

            // Behaviour: the same last-moment dodge is perfect in Fluid, only an evade in Punishing.
            var p = PlayerDriver.Punishing();
            p.Step(Pad.Dodge, Forward);
            p.Step(Pad.None, Forward);
            p.Run(3, Pad.None, Forward);
            Assert.IsTrue(p.Model.IsInvulnerable);
            Assert.AreEqual(HitOutcome.Evaded, p.HitFromFront(10f).Outcome);
        }

        [Test]
        public void PunishingSprintDrainsStamina()
        {
            var d = PlayerDriver.Punishing();
            d.RunUntil(x => x.Model.State == PlayerState.Sprinting, 30, Pad.Dodge, Forward);
            float before = d.Model.Stamina;
            d.Run(60, Pad.Dodge, Forward);
            Assert.That(before - d.Model.Stamina, Is.EqualTo(d.Model.Tuning.SprintStaminaDrain).Within(0.2f));
        }

        [Test]
        public void ApplyTuningSwapsPresetsLiveAndClosesAnOpenWindow()
        {
            var d = new PlayerDriver();
            d.Model.ReceiveHit(PlayerDriver.EnemyHit(50f, 0f, d.Model.Forward), d.Model.Forward);
            d.Step(Pad.Light);
            d.RunUntil(x => x.Model.IsAttackActive, 20);
            var tuning = PlayerTuning.CreatePunishing();
            tuning.MaxHealth = 200f;
            d.Model.ApplyTuning(tuning, ElementMoveSet.CreateFirePunishing());
            Assert.AreEqual(PlayerState.Locomotion, d.Model.State);
            Assert.AreEqual(100f, d.Model.Health, 1e-3f, "keeps its share of max health");
            d.Step();
            Assert.AreEqual(d.Count(PlayerEventType.AttackActiveStart), d.Count(PlayerEventType.AttackActiveEnd));
            Assert.AreEqual("Punishing", d.Model.Tuning.PresetName);
            Assert.AreEqual(DodgeTrigger.OnRelease, d.Model.Tuning.DodgeTrigger);
        }

        [Test]
        public void InspectorEditsApplyLiveAndTheModelNeverWritesTuning()
        {
            var tuning = PlayerTuning.CreateFluid();
            var moves = ElementMoveSet.CreateFireFluid();
            var d = new PlayerDriver(tuning, moves);
            d.Run(30, Pad.None, Forward);
            Assert.AreEqual(tuning.RunSpeed, d.Last.Velocity.Z, 0.05f);
            tuning.RunSpeed = 6f;                                // "dragging a slider" mid-play
            d.Run(30, Pad.None, Forward);
            Assert.AreEqual(6f, d.Last.Velocity.Z, 0.05f);

            // Play a busy minute, then check nothing in the tuning changed.
            string before = Snapshot(tuning, moves);
            var bot = new DeterministicRandom(7);
            for (int i = 0; i < 3600; i++)
            {
                Pad pad = (Pad)(bot.NextUInt() & 127u);
                d.Step(pad, new Vector2(bot.Range(-1f, 1f), bot.Range(-1f, 1f)));
                if (i % 50 == 0) d.Model.ReceiveHit(PlayerDriver.EnemyHit(8f, 12f, d.Model.Forward), d.Model.Forward);
                if (!d.Model.IsAlive) d.Model.Respawn();
            }
            Assert.AreEqual(before, Snapshot(tuning, moves));
        }

        static string Snapshot(PlayerTuning t, ElementMoveSet m)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var f in typeof(PlayerTuning).GetFields()) sb.Append(f.Name).Append('=').Append(f.GetValue(t)).Append(';');
            MoveData[] moves = { m.LightChain[0], m.LightChain[1], m.LightChain[2], m.Heavy, m.SprintAttack, m.PlungeAttack, m.Skill };
            foreach (MoveData move in moves)
                foreach (var f in typeof(MoveData).GetFields()) sb.Append(f.Name).Append('=').Append(f.GetValue(move)).Append(';');
            foreach (object settings in new object[] { m.Dodge, m.Guard, m.Charge, m.Plunge, m.Momentum })
                foreach (var f in settings.GetType().GetFields()) sb.Append(f.Name).Append('=').Append(f.GetValue(settings)).Append(';');
            return sb.ToString();
        }

        [Test]
        public void SameInputsGiveTheSameResult()
        {
            var a = new PlayerDriver();
            var b = new PlayerDriver();
            a.LockOn(new Vector3(2f, 0f, 6f));
            b.LockOn(new Vector3(2f, 0f, 6f));
            var botA = new DeterministicRandom(12345);
            var botB = new DeterministicRandom(12345);
            for (int i = 0; i < 2000; i++)
            {
                Pad padA = (Pad)(botA.NextUInt() & 127u), padB = (Pad)(botB.NextUInt() & 127u);
                Vector2 moveA = new Vector2(botA.Range(-1f, 1f), botA.Range(-1f, 1f));
                Vector2 moveB = new Vector2(botB.Range(-1f, 1f), botB.Range(-1f, 1f));
                a.Step(padA, moveA);
                b.Step(padB, moveB);
                if (i % 37 == 0)
                {
                    a.Model.ReceiveHit(PlayerDriver.EnemyHit(6f, 10f, a.Model.Forward), a.Model.Forward);
                    b.Model.ReceiveHit(PlayerDriver.EnemyHit(6f, 10f, b.Model.Forward), b.Model.Forward);
                }
                Assert.AreEqual(a.Last.Velocity, b.Last.Velocity, "frame " + i);
                Assert.AreEqual(a.Last.FacingYaw, b.Last.FacingYaw);
                Assert.AreEqual(a.Model.State, b.Model.State);
                Assert.AreEqual(a.Last.Events.Count, b.Last.Events.Count);
            }
        }

        [Test]
        public void TargetStandingExactlyOnThePlayerNeverProducesNaN()
        {
            foreach (bool soft in new[] { false, true })
            {
                var d = new PlayerDriver();
                if (soft)
                {
                    d.World.HasSoftTarget = true;
                    d.World.SoftTargetPosition = d.World.Position;
                }
                else
                {
                    d.LockOn(d.World.Position, 0.4f);
                    d.World.LockTargetAimPoint = Vector3.Zero;
                }
                Pad[] script = { Pad.Light, Pad.None, Pad.Heavy, Pad.None, Pad.Skill, Pad.Dodge, Pad.None, Pad.Guard, Pad.Jump, Pad.Light };
                for (int i = 0; i < 600; i++)
                {
                    d.World.LockTargetPosition = d.World.Position;
                    d.World.SoftTargetPosition = d.World.Position;
                    d.Step(script[(i / 7) % script.Length], i % 90 < 45 ? Forward : Vector2.Zero);
                }
                foreach (PlayerEvent e in d.Log)
                {
                    PlayerDriver.AssertFinite(e.Origin, e.Type + " origin");
                    PlayerDriver.AssertFinite(e.Direction, e.Type + " direction");
                }
                Assert.Greater(d.Count(PlayerEventType.AttackStarted), 2);
            }
        }

        [Test]
        public void SoftLockPicksOnlyNearbyEnemiesInFrontOfTheAim()
        {
            var tuning = PlayerTuning.CreateFluid();
            Vector3 self = Vector3.Zero;
            Assert.IsTrue(SoftLockSelector.TryScore(self, 0f, new Vector3(0f, 0f, 3f), tuning, out float ahead));
            Assert.IsTrue(SoftLockSelector.TryScore(self, 0f, new Vector3(1.8f, 0f, 2.4f), tuning, out float angled));
            Assert.Less(ahead, angled, "same distance: the one straight ahead wins");
            Assert.IsTrue(SoftLockSelector.TryScore(self, 0f, new Vector3(1.2f, 0f, 1.6f), tuning, out float nearer));
            Assert.Less(nearer, ahead, "otherwise nearest first");
            Assert.IsFalse(SoftLockSelector.TryScore(self, 0f, new Vector3(0f, 0f, 5f), tuning, out _), "too far");
            Assert.IsFalse(SoftLockSelector.TryScore(self, 0f, new Vector3(3f, 0f, 0.5f), tuning, out _), "too far off the aim");
            Assert.IsTrue(SoftLockSelector.TryScore(self, 0f, self, tuning, out _), "on top of us counts (no NaN)");

            var d = new PlayerDriver();
            d.World.HasSoftTarget = true;
            d.World.SoftTargetPosition = new Vector3(2f, 0f, 2f);
            d.Step(Pad.Light);
            d.Run(10);
            Assert.AreEqual(45f, d.Model.FacingYaw, 1f, "the attack turned toward the soft target");
        }
    }
}
