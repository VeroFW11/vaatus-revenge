using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Pins the Spider-Man 2 style controls: parry-only defence, the zip strike and the free-flow gap-closing lunge.
    public class FreeFlowControlTests
    {
        // ---------------------------------------------------------------- parry only

        [Test]
        public void FireIsParryOnlyByDefault()
        {
            Assert.AreEqual(DefenseStyle.ParryOnly, ElementMoveSet.CreateFireFluid().Guard.Style);
            Assert.AreEqual(DefenseStyle.ParryOnly, ElementMoveSet.CreateFirePunishing().Guard.Style);
        }

        [Test]
        public void AParryStanceDropsByItselfEvenWhileTheButtonIsHeld()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Guard);
            Assert.AreEqual(PlayerState.Guarding, d.Model.State);
            Assert.IsTrue(d.Model.IsDeflectWindowOpen);
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 30, Pad.Guard);
            float window = d.Model.MoveSet.Guard.DeflectWindow;
            Assert.LessOrEqual(d.Frame * d.Dt, window + 2f * d.Dt, "the stance lasts the deflect window, not as long as you hold");
            d.Run(20, Pad.Guard);
            Assert.AreEqual(PlayerState.Locomotion, d.Model.State, "holding the button never raises a block");
        }

        [Test]
        public void AParryCatchesAHitFromAnyDirection()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Guard);
            HitResult behind = d.Model.ReceiveHit(PlayerDriver.EnemyHit(20f, 10f, -d.Model.Forward), d.Model.Forward);
            Assert.AreEqual(HitOutcome.Parried, behind.Outcome);
            Assert.AreEqual(d.Model.MaxHealth, d.Model.Health);
        }

        [Test]
        public void AMistimedParryOrAnUnparryableHitLandsCleanly()
        {
            var late = new PlayerDriver();
            late.Step(Pad.Guard);
            late.Run(20, Pad.Guard);                            // window long gone, button still held
            HitResult afterWindow = late.Model.ReceiveHit(PlayerDriver.EnemyHit(20f, 0f, late.Model.Forward), late.Model.Forward);
            Assert.AreEqual(HitOutcome.Hit, afterWindow.Outcome, "no block to fall back on");

            var d = new PlayerDriver();
            d.Step(Pad.Guard);
            HitResult unparryable = d.Model.ReceiveHit(PlayerDriver.EnemyHit(20f, 0f, d.Model.Forward, false), d.Model.Forward);
            Assert.AreEqual(HitOutcome.Hit, unparryable.Outcome, "an unparryable hit must be dodged");
            Assert.AreEqual(d.Model.MaxHealth - 20f, d.Model.Health, 1e-3f);
        }

        [Test]
        public void ABlockingElementStillHoldsItsGuard()
        {
            var d = PlayerDriver.Blocking();
            d.Run(30, Pad.Guard);
            Assert.AreEqual(PlayerState.Guarding, d.Model.State);
            HitResult blocked = d.Model.ReceiveHit(PlayerDriver.EnemyHit(20f, 0f, d.Model.Forward), d.Model.Forward);
            Assert.AreEqual(HitOutcome.Blocked, blocked.Outcome);
        }

        // ---------------------------------------------------------------- zip strike

        static void SetZipTarget(PlayerDriver d, Vector3 feet, float radius = 0.4f)
        {
            d.World.HasZipTarget = true;
            d.World.ZipTargetPosition = feet;
            d.World.ZipTargetAimPoint = feet + new Vector3(0f, 1.2f, 0f);
            d.World.ZipTargetRadius = radius;
        }

        [Test]
        public void ZipStrikeWithoutATargetDoesNothingAndCostsNothing()
        {
            var d = new PlayerDriver();
            float stamina = d.Model.Stamina;
            d.Tap(Pad.Zip);
            d.Run(10);
            Assert.AreEqual(0, d.Count(PlayerEventType.AttackStarted));
            Assert.AreEqual(stamina, d.Model.Stamina);
            Assert.AreEqual(PlayerCommand.None, d.Model.BufferedCommand, "the press is dropped, not saved for later");
        }

        [Test]
        public void ZipStrikeDashesAcrossTheGapAndStopsShortOfTheTarget()
        {
            var d = new PlayerDriver();
            var target = new Vector3(3f, 0f, 9f);             // off to the side: the strike turns to face it
            SetZipTarget(d, target);
            d.Tap(Pad.Zip);
            Assert.AreEqual(PlayerState.Attacking, d.Model.State);
            Assert.AreEqual(PlayerAttackKind.ZipStrike, d.Model.CurrentAttackKind);
            MoveData zip = d.Model.MoveSet.ZipStrike;
            d.RunUntil(x => x.Model.IsAttackActive, 60);

            d.RunUntil(x => !x.Model.IsAttackActive, 60);
            float gap = Directions.Flatten(target - d.World.Position).Length();
            float stopAt = d.World.SelfRadius + 0.4f + d.Model.Tuning.LungeStopGap;
            Assert.AreEqual(stopAt, gap, 0.05f, "arrives right in front of the target, never through it");
            Assert.Less(gap - d.World.SelfRadius - 0.4f, zip.Range, "the kick reaches from where it stops");
            float yawToTarget = Directions.YawOf(Directions.Flatten(target - d.World.Position), 0f);
            Assert.AreEqual(0f, Angles.Delta(d.Model.FacingYaw, yawToTarget), 5f, "faces the target");
        }

        [Test]
        public void ADodgePressIsNotReplacedByAZipPress()
        {
            var d = new PlayerDriver();
            SetZipTarget(d, new Vector3(0f, 0f, 8f));
            d.Step(Pad.Light);
            d.Run(2);
            d.Step(Pad.Dodge);
            d.Step(Pad.Zip);
            Assert.AreEqual(PlayerCommand.Dodge, d.Model.BufferedCommand);
        }

        // ---------------------------------------------------------------- free-flow lunge

        static void SetSoftTarget(PlayerDriver d, Vector3 feet, float radius = 0.4f)
        {
            d.World.HasSoftTarget = true;
            d.World.SoftTargetPosition = feet;
            d.World.SoftTargetAimPoint = feet + new Vector3(0f, 1.2f, 0f);
            d.World.SoftTargetRadius = radius;
        }

        [Test]
        public void ALightAttackClosesTheGapToAFarSoftTarget()
        {
            var d = new PlayerDriver();
            var target = new Vector3(0f, 0f, 5.5f);
            SetSoftTarget(d, target);
            d.Step(Pad.Light);
            d.RunUntil(x => x.Model.State != PlayerState.Attacking || x.Model.Phase == AttackPhase.Recovery, 60);
            float gap = target.Z - d.World.Position.Z;
            float stopAt = d.World.SelfRadius + 0.4f + d.Model.Tuning.LungeStopGap;
            Assert.AreEqual(stopAt, gap, 0.05f, "lunged all the way in");
            Assert.Less(gap - d.World.SelfRadius - 0.4f, d.Model.MoveSet.LightChain[0].Range);
        }

        [Test]
        public void TheGapCloserIsCappedAndOffWithoutATarget()
        {
            var alone = new PlayerDriver();
            alone.Step(Pad.Light);
            alone.RunUntil(x => x.Model.State != PlayerState.Attacking, 90);
            MoveData jab = alone.Model.MoveSet.LightChain[0];
            Assert.AreEqual(jab.LungeDistance, alone.World.Position.Z, 0.02f, "no target: the jab's own small step");

            var far = new PlayerDriver();
            SetSoftTarget(far, new Vector3(0f, 0f, 30f));
            far.Step(Pad.Light);
            far.RunUntil(x => x.Model.State != PlayerState.Attacking, 90);
            Assert.AreEqual(jab.LungeDistance + far.Model.Tuning.GapCloseDistance, far.World.Position.Z, 0.02f, "capped");

            var off = new PlayerDriver();
            off.Model.Tuning.GapCloseDistance = 0f;
            SetSoftTarget(off, new Vector3(0f, 0f, 6f));
            off.Step(Pad.Light);
            off.RunUntil(x => x.Model.State != PlayerState.Attacking, 90);
            Assert.AreEqual(jab.LungeDistance, off.World.Position.Z, 0.02f, "GapCloseDistance 0 turns it off");
        }
    }
}
