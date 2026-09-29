using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Pins the fixes for the independent verifier's findings (docs/Prototype/Playtest-Report-03.md).
    public class VerificationFixTests
    {
        static readonly Vector2 Forward = new Vector2(0f, 1f);

        // ---------------------------------------------------------------- V-01: LB ability chords

        [Test]
        public void ChordPressedOnTheSameFrameAsTheGuardIsTheAbilityNotAParry()
        {
            var fajin = new PlayerDriver();
            fajin.Step(Pad.Guard | Pad.Heavy);
            Assert.AreEqual(PlayerState.Charging, fajin.Model.State, "LB + X together: the fa jin charge");

            var whip = new PlayerDriver();
            whip.Step(Pad.Guard | Pad.AbilityNorth);
            Assert.AreSame(whip.Model.MoveSet.AbilityNorth, whip.Model.CurrentMove, "LB + Y together: the whip");

            var wheel = new PlayerDriver();
            wheel.Step(Pad.Guard | Pad.AbilityEast);
            Assert.AreSame(wheel.Model.MoveSet.AbilityEast, wheel.Model.CurrentMove, "LB + B together: the wheel");
        }

        [Test]
        public void ChordFinishedDuringAJabReplacesTheBufferedParry()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Light);
            d.Run(2);
            d.Step(Pad.Guard);                                   // LB goes down mid-jab: buffered as a parry...
            d.Run(2, Pad.Guard);
            d.Step(Pad.Guard | Pad.Heavy);                       // ...and X completes the chord
            Assert.AreEqual(PlayerCommand.Heavy, d.Model.BufferedCommand);
            d.RunUntil(x => x.Model.State == PlayerState.Charging, 60, Pad.Guard | Pad.Heavy);
            Assert.AreEqual(0, d.Count(PlayerEventType.GuardStarted), "no parry stance");
        }

        [Test]
        public void AChordAfterAParryPressDoesNotLockOutTheNextParry()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Guard);                                   // parry stance opens...
            d.Step(Pad.Guard | Pad.AbilityNorth);                // ...and the whip chord takes over
            Assert.AreSame(d.Model.MoveSet.AbilityNorth, d.Model.CurrentMove);
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 120);
            Assert.AreEqual(0, d.Count(PlayerEventType.DeflectWhiffed), "the chord's own parry never counts as a whiff");
            d.Step(Pad.Guard);
            Assert.IsTrue(d.Model.IsDeflectWindowOpen, "a real parry right after works");
        }

        // ---------------------------------------------------------------- V-02: aerial heavy, grounded-only moves

        [Test]
        public void HeavyAfterAnAirStrikeIsThePlungeNotAMidAirCharge()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Jump);
            d.Run(6);
            d.Step(Pad.Light);                                   // air jab
            Assert.AreEqual(PlayerAttackKind.Air, d.Model.CurrentAttackKind);
            d.RunUntil(x => x.Model.State == PlayerState.Plunging || x.Model.State == PlayerState.Charging, 60, Pad.Heavy);
            Assert.AreEqual(PlayerState.Plunging, d.Model.State);
        }

        [Test]
        public void AbilitiesAndTheSkillWaitForTheGround()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Jump);
            d.Run(6);
            d.Step(Pad.AbilityNorth);
            Assert.AreEqual(PlayerState.Airborne, d.Model.State, "no fire whip in mid-air");
            var skill = new PlayerDriver();
            skill.Step(Pad.Jump);
            skill.Run(6);
            skill.Step(Pad.Skill);
            Assert.AreEqual(PlayerState.Airborne, skill.Model.State, "no fire blast in mid-air");
        }

        // ---------------------------------------------------------------- V-04: the zip strike arrives on its active frame

        [Test]
        public void ZipStrikeArrivesAsTheKickGoesActive()
        {
            var d = new PlayerDriver();
            var target = new Vector3(0f, 0f, 10f);
            d.World.HasZipTarget = true;
            d.World.ZipTargetPosition = target;
            d.World.ZipTargetRadius = 0.4f;
            d.Step(Pad.Zip);
            d.RunUntil(x => x.Model.IsAttackActive, 60);
            float gap = Directions.Flatten(target - d.World.Position).Length() - d.World.SelfRadius - 0.4f;
            Assert.AreEqual(d.Model.Tuning.LungeStopGap, gap, 0.1f, "already arrived when the hitbox opens");
        }

        // ---------------------------------------------------------------- V-08: a homing lunge keeps its target

        [Test]
        public void AStretchedLungeKeepsGoingForItsTargetWhenTheSoftLockDropsIt()
        {
            var d = new PlayerDriver();
            var target = new Vector3(0f, 0f, 5.5f);
            d.World.HasSoftTarget = true;
            d.World.SoftTargetPosition = target;
            d.World.SoftTargetRadius = 0.4f;
            d.Step(Pad.Light, Forward);
            d.World.HasSoftTarget = false;                       // the stick is let go and the soft lock loses it
            d.RunUntil(x => x.Model.State != PlayerState.Attacking || x.Model.Phase == AttackPhase.Recovery, 60);
            float gap = target.Z - d.World.Position.Z - d.World.SelfRadius - 0.4f;
            Assert.AreEqual(d.Model.Tuning.LungeStopGap, gap, 0.1f, "still closed the gap");
        }

        // ---------------------------------------------------------------- V-10: no floating over a standing foe

        [Test]
        public void AirStrikesDontLiftYouOverAStandingFoesHead()
        {
            var d = new PlayerDriver();
            d.World.HasSoftTarget = true;
            d.World.SoftTargetPosition = new Vector3(0f, 0f, 1.6f);
            d.World.SoftTargetRadius = 0.4f;
            d.Step(Pad.Jump);
            d.Run(6);
            float peak = 0f;
            for (int i = 0; i < 90; i++)
            {
                if (d.Model.State == PlayerState.Airborne || (d.Model.CurrentMove != null
                    && d.Model.ActionTime >= d.Model.CurrentMove.ComboWindowStart)) d.Step(Pad.Light);
                else d.Step();
                peak = System.Math.Max(peak, d.World.Position.Y);
                if (d.Model.IsGrounded && i > 10) break;
            }
            Assert.Less(peak, 1.8f, "feet stay below a standing soldier's head");
        }
    }
}
