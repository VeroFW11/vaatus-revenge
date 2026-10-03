using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // The hit counter (Build 05, spec 2.5): +1 per clean hit per AttackId; ends on a hit taken, a guard break, a timeout,
    // a stagger, death, respawn or a preset change; refreshed (not added to) by a perfect dodge or a deflect.
    public class ComboCounterTests
    {
        static readonly HitResult Clean = new HitResult { Outcome = HitOutcome.Hit };

        static PlayerDriver Jabbed(int hits)
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.OnBeatString(hits - 1);
            d.RunUntil(x => x.Model.Phase == AttackPhase.Active, 60);
            d.LandStrikes();
            d.Step();
            return d;
        }

        [Test]
        public void CountsCleanHitsOncePerAttackId()
        {
            PlayerDriver d = Jabbed(3);
            Assert.AreEqual(3, d.Model.ComboCount);
            PlayerEvent hit = d.LastOf(PlayerEventType.ComboHit);
            Assert.AreEqual(3, hit.Count);
            Assert.AreEqual(2, hit.ChainIndex);
            Assert.AreEqual(ElementId.Fire, hit.Element);
            Assert.AreEqual(BeatGrade.OnBeat, hit.Grade);
            Assert.AreEqual(d.Model.Tuning.Combo.ComboTimeout, hit.Amount, 0.02f);
            d.Model.OnAttackLanded(Clean, hit.AttackId);         // the same attack again
            d.Model.OnAttackLanded(new HitResult { Outcome = HitOutcome.Blocked }, d.Model.CurrentAttackId);
            d.Step();
            Assert.AreEqual(3, d.Model.ComboCount);
        }

        [Test]
        public void MultiTargetHitCountsOnce()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Step(Pad.Light);
            d.RunUntil(x => x.Model.IsAttackActive, 60);
            int id = d.Model.ActiveAttackId;
            d.Model.OnAttackLanded(Clean, id);
            d.Model.OnAttackLanded(Clean, id);                   // a second enemy in the same swing
            d.Step();
            Assert.AreEqual(1, d.Model.ComboCount);
            Assert.AreEqual(1, d.Count(PlayerEventType.ComboHit));
        }

        [Test]
        public void MultiHitMoveCountsEachSubHit()
        {
            ElementLoadout loadout = ElementLoadout.CreateFluid();
            MoveData palm = loadout.Fire.LightChain[0];
            palm.HitCount = 3;
            palm.HitInterval = 0.03f;
            palm.Active = 0.10f;
            PlayerDriver d = PlayerDriver.Elements(null, loadout);
            d.Step(Pad.Light);
            d.RunLanding(30);
            Assert.AreEqual(3, d.Model.ComboCount, "every sub-hit counts");
        }

        [Test]
        public void ProjectileCountsWhenItLands()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Step(Pad.Skill);
            d.RunUntil(x => x.Count(PlayerEventType.ProjectileLaunched) > 0, 60);
            PlayerEvent blast = d.LastOf(PlayerEventType.ProjectileLaunched);
            d.Run(20);
            Assert.AreEqual(0, d.Model.ComboCount, "not while it flies");
            d.Model.OnAttackLanded(Clean, blast.AttackId, true);
            d.Step();
            Assert.AreEqual(1, d.Model.ComboCount);
            Assert.IsTrue(d.LastOf(PlayerEventType.ComboHit).InAir);
            Assert.AreEqual(PlayerAttackKind.Skill, d.LastOf(PlayerEventType.ComboHit).AttackKind);
        }

        [Test]
        public void ResetsOnDamagedWithReason()
        {
            PlayerDriver d = Jabbed(2);
            d.HitFromFront(5f);
            d.Step();
            PlayerEvent ended = d.LastOf(PlayerEventType.ComboEnded);
            Assert.AreEqual(ComboEndReason.TookHit, ended.EndReason);
            Assert.AreEqual(2, ended.Count);
            Assert.AreEqual(0, d.Model.ComboCount);
        }

        [Test]
        public void ResetsOnGuardBroken()
        {
            ElementMoveSet set = ElementMoveSet.CreateFireFluid();
            set.Guard.Style = DefenseStyle.BlockAndParry;
            PlayerTuning t = PlayerTuning.CreateFluid();
            t.MaxStamina = 10f;
            PlayerDriver d = new PlayerDriver(t, set);
            d.Step(Pad.Light);
            d.RunUntil(x => x.Model.IsAttackActive, 60);
            d.LandStrikes();
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 90);
            d.Run(15, Pad.Guard);
            Assert.AreEqual(1, d.Model.ComboCount);
            d.Model.ReceiveHit(PlayerDriver.EnemyHit(5f, 0f, d.Model.Forward, true, 50f), d.Model.Forward);
            d.Step();
            Assert.AreEqual(1, d.Count(PlayerEventType.GuardBroken));
            Assert.AreEqual(ComboEndReason.TookHit, d.LastOf(PlayerEventType.ComboEnded).EndReason);
        }

        [Test]
        public void NotOnEvadeDeflectBlock()
        {
            PlayerDriver d = Jabbed(1);
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 90);
            d.Step(Pad.Dodge, new Vector2(-1f, 0f));
            d.Step();
            d.Step();
            Assert.AreNotEqual(HitOutcome.Hit, d.HitFromFront(5f).Outcome);
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 60);
            d.Step(Pad.Guard);
            Assert.AreEqual(HitOutcome.Parried, d.HitFromFront(5f).Outcome);
            d.Run(30);
            d.Model.OnAttackLanded(new HitResult { Outcome = HitOutcome.Blocked }, 12345);
            d.Step();
            Assert.AreEqual(1, d.Model.ComboCount);
            Assert.AreEqual(0, d.Count(PlayerEventType.ComboEnded));
        }

        [Test]
        public void TimesOut()
        {
            PlayerDriver d = Jabbed(1);
            float timeout = d.Model.Tuning.Combo.ComboTimeout;
            d.Run(d.FramesToReach(timeout) - 3);
            Assert.AreEqual(1, d.Model.ComboCount);
            Assert.That(d.Model.ComboTimeRemaining, Is.InRange(1e-4f, d.Dt * 4f), "about to run out");
            d.Run(4);
            Assert.AreEqual(0, d.Model.ComboCount);
            Assert.AreEqual(ComboEndReason.Timeout, d.LastOf(PlayerEventType.ComboEnded).EndReason);
        }

        [Test]
        public void PerfectDodgeAndDeflectRefreshWithoutIncrement()
        {
            PlayerDriver d = Jabbed(1);
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 90);
            d.Run(60);
            float left = d.Model.ComboTimeRemaining;
            d.Step(Pad.Dodge, new Vector2(-1f, 0f));
            d.Step();
            Assert.AreEqual(HitOutcome.PerfectEvade, d.HitFromFront(5f).Outcome);
            d.Step();
            Assert.AreEqual(1, d.Model.ComboCount);
            Assert.Greater(d.Model.ComboTimeRemaining, left + 0.5f, "a perfect dodge refreshes it");
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 60);
            d.Run(60);
            left = d.Model.ComboTimeRemaining;
            d.Step(Pad.Guard);
            Assert.AreEqual(HitOutcome.Parried, d.HitFromFront(5f).Outcome);
            d.Step();
            Assert.AreEqual(1, d.Model.ComboCount);
            Assert.Greater(d.Model.ComboTimeRemaining, left + 0.5f, "so does a deflect");
        }

        [Test]
        public void RespawnAndPresetSwapEndCombo()
        {
            PlayerDriver d = Jabbed(2);
            d.Model.Respawn();
            d.Step();
            Assert.AreEqual(ComboEndReason.Respawned, d.LastOf(PlayerEventType.ComboEnded).EndReason);
            Assert.AreEqual(0, d.Model.ComboCount);

            PlayerDriver p = Jabbed(2);
            p.Model.ApplyTuning(PlayerTuning.CreatePunishing(), ElementLoadout.CreatePunishing());
            p.Step();
            Assert.AreEqual(ComboEndReason.PresetChanged, p.LastOf(PlayerEventType.ComboEnded).EndReason);
            Assert.AreEqual(0, p.Model.ComboCount);
        }
    }
}
