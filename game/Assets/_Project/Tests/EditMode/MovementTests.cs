using System;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Pins locomotion: camera-relative movement, speeds, acceleration, turning, jumping, coyote time,
    // lock-on strafing, dodge distances and lunges that stop short of the target.
    public class MovementTests
    {
        static readonly Vector2 Up = new Vector2(0f, 1f);

        static float HorizontalSpeed(Vector3 v)
        {
            return new Vector2(v.X, v.Z).Length();
        }

        [Test]
        public void StickMovesRelativeToTheCamera()
        {
            var d = new PlayerDriver();
            d.World.CameraYaw = 90f;                              // camera looking along +X
            d.Run(30, Pad.None, Up);
            Assert.That(d.Last.Velocity.X, Is.EqualTo(d.Model.Tuning.RunSpeed).Within(0.01f));
            Assert.That(d.Last.Velocity.Z, Is.EqualTo(0f).Within(0.01f));
            Assert.That(d.Model.FacingYaw, Is.EqualTo(90f).Within(0.5f), "turns to face where it runs");
        }

        [Test]
        public void SmallTiltWalksFullTiltRuns()
        {
            var walk = new PlayerDriver();
            walk.Run(30, Pad.None, new Vector2(0f, 0.3f));
            Assert.That(HorizontalSpeed(walk.Last.Velocity), Is.EqualTo(walk.Model.Tuning.WalkSpeed).Within(0.01f));
            var run = new PlayerDriver();
            run.Run(30, Pad.None, Up);
            Assert.That(HorizontalSpeed(run.Last.Velocity), Is.EqualTo(run.Model.Tuning.RunSpeed).Within(0.01f));
            var idle = new PlayerDriver();
            idle.Run(30, Pad.None, new Vector2(0.05f, 0.05f));
            Assert.AreEqual(0f, HorizontalSpeed(idle.Last.Velocity), 1e-4f, "inside the deadzone");
        }

        [Test]
        public void AccelerationAndDecelerationFollowTheTuning()
        {
            var d = new PlayerDriver();
            PlayerTuning t = d.Model.Tuning;
            d.Run(6, Pad.None, Up);                               // 0.1 s at 30 m/s^2
            Assert.That(HorizontalSpeed(d.Last.Velocity), Is.EqualTo(t.Acceleration * 0.1f).Within(0.05f));
            d.Run(30, Pad.None, Up);
            d.Run(3);                                             // 0.05 s at 40 m/s^2
            Assert.That(HorizontalSpeed(d.Last.Velocity), Is.EqualTo(t.RunSpeed - t.Deceleration * 0.05f).Within(0.05f));
            d.Run(10);
            Assert.AreEqual(0f, HorizontalSpeed(d.Last.Velocity), 1e-4f);
        }

        [Test]
        public void TurningIsLimitedByTheTurnRate()
        {
            var d = new PlayerDriver();
            d.Step(Pad.None, new Vector2(0f, -1f));              // ask for a 180 degree turn
            Assert.That(Math.Abs(d.Model.FacingYaw), Is.EqualTo(d.Model.Tuning.TurnRate * d.Dt).Within(0.01f));
        }

        [Test]
        public void JumpReachesItsHeightAndLands()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Jump);
            Assert.AreEqual(1, d.Count(PlayerEventType.Jumped));
            float peak = 0f;
            d.RunUntil(x =>
            {
                peak = Math.Max(peak, x.World.Position.Y);
                return x.Model.State == PlayerState.Locomotion;
            }, 120);
            Assert.That(peak, Is.EqualTo(d.Model.Tuning.JumpHeight).Within(0.1f));
            Assert.AreEqual(1, d.Count(PlayerEventType.Landed));
        }

        [Test]
        public void CoyoteTimeForgivesALateJumpOffALedge()
        {
            var d = new PlayerDriver();
            d.Run(5, Pad.None, Up);
            d.World.Position = new Vector3(0f, 3f, d.World.Position.Z);   // the floor vanished: walked off a ledge
            d.World.Grounded = false;
            d.Run(3, Pad.None, Up);                               // 0.05 s: still inside coyote time
            Assert.AreEqual(PlayerState.Locomotion, d.Model.State);
            d.Step(Pad.Jump, Up);
            Assert.AreEqual(PlayerState.Airborne, d.Model.State);
            Assert.Greater(d.Last.Velocity.Y, 0f, "jumped");

            var late = new PlayerDriver();
            late.World.Position = new Vector3(0f, 3f, 0f);
            late.World.Grounded = false;
            late.Run(late.FramesToReach(late.Model.Tuning.CoyoteTime) + 2);
            Assert.AreEqual(PlayerState.Airborne, late.Model.State, "falling once coyote time is over");
            late.Step(Pad.Jump);
            Assert.Less(late.Last.Velocity.Y, 0f, "too late: no mid-air jump");
            Assert.AreEqual(0, late.Count(PlayerEventType.Jumped));
        }

        [Test]
        public void AirControlOnlySteersALittle()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Jump);
            d.Run(6, Pad.None, new Vector2(1f, 0f));
            float air = d.Last.Velocity.X;
            var g = new PlayerDriver();
            g.Run(7, Pad.None, new Vector2(1f, 0f));
            Assert.Less(air, g.Last.Velocity.X * 0.5f);
            Assert.Greater(air, 0f);
        }

        [Test]
        public void LockedOnTheCharacterStrafesAndKeepsFacingTheTarget()
        {
            var d = new PlayerDriver();
            d.LockOn(new Vector3(0f, 0f, 6f));
            d.Run(40, Pad.None, new Vector2(1f, 0f));
            Assert.That(HorizontalSpeed(d.Last.Velocity), Is.EqualTo(d.Model.Tuning.LockOnStrafeSpeed).Within(0.05f));
            float expectedYaw = Directions.YawOf(d.World.LockTargetPosition - d.World.Position);
            Assert.That(Angles.Delta(d.Model.FacingYaw, expectedYaw), Is.EqualTo(0f).Within(1f));
        }

        [Test]
        public void SprintingWhileLockedOnFacesTheWayYouRun()
        {
            var d = new PlayerDriver();
            d.LockOn(new Vector3(0f, 0f, 6f));
            d.Run(40, Pad.Dodge, new Vector2(1f, 0f));
            Assert.AreEqual(PlayerState.Sprinting, d.Model.State);
            Assert.That(d.Model.FacingYaw, Is.EqualTo(90f).Within(1f));
        }

        [Test]
        public void GuardAndHealSlowYouDown()
        {
            var guard = new PlayerDriver();
            guard.Run(40, Pad.Guard, Up);
            Assert.That(HorizontalSpeed(guard.Last.Velocity),
                Is.EqualTo(guard.Model.Tuning.RunSpeed * guard.Model.MoveSet.Guard.MoveSpeedMultiplier).Within(0.05f));

            var heal = new PlayerDriver();
            heal.Step(Pad.Heal, Up);
            heal.Run(20, Pad.None, Up);
            Assert.AreEqual(PlayerState.Healing, heal.Model.State);
            Assert.That(HorizontalSpeed(heal.Last.Velocity),
                Is.EqualTo(heal.Model.Tuning.RunSpeed * heal.Model.Tuning.HealMoveMultiplier).Within(0.05f));
        }

        [Test]
        public void DodgeCoversItsDistanceAndBackstepGoesBackwards()
        {
            foreach (float fps in new[] { 30f, 60f, 144f })
            {
                var d = new PlayerDriver(null, null, fps);
                d.Step(Pad.Dodge, Up);
                d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 200);
                d.Run(10);
                Assert.That(d.World.Position.Z, Is.EqualTo(d.Model.MoveSet.Dodge.Distance).Within(0.02f), fps + " fps");
                Assert.That(d.World.Position.X, Is.EqualTo(0f).Within(1e-3f));
            }
            var back = new PlayerDriver();
            back.Step(Pad.Dodge);
            Assert.IsTrue(back.All(PlayerEventType.DodgeStarted)[0].IsBackstep);
            back.RunUntil(x => x.Model.State == PlayerState.Locomotion, 60);
            Assert.That(back.World.Position.Z, Is.EqualTo(-back.Model.MoveSet.Dodge.BackstepDistance).Within(0.02f));
            Assert.AreEqual(0f, back.Model.FacingYaw, 1e-3f, "a backstep keeps facing forward");
        }

        [Test]
        public void LockedOnDodgeStrafesWithoutTurningAway()
        {
            var d = new PlayerDriver();
            d.LockOn(new Vector3(0f, 0f, 6f));
            d.Step(Pad.Dodge, new Vector2(1f, 0f));
            d.Run(10);
            Assert.That(d.Model.FacingYaw, Is.EqualTo(Directions.YawOf(d.World.LockTargetPosition - d.World.Position)).Within(2f));
            Assert.Greater(d.World.Position.X, 1f);
        }

        [Test]
        public void LungesStopShortOfTheTarget()
        {
            var d = new PlayerDriver();
            Vector3 target = new Vector3(0f, 0f, 3f);
            d.LockOn(target, 0.4f);
            d.RunUntil(x => x.Model.State == PlayerState.Sprinting, 40, Pad.Dodge, Up);
            d.Run(d.FramesToReach(0.35f), Pad.Dodge, Up);
            d.World.Position = new Vector3(0f, 0f, 0f);           // re-place 3 m away for a clean measurement
            d.Step(Pad.Dodge | Pad.Light, Up);
            Assert.AreEqual(PlayerAttackKind.Sprint, d.Model.CurrentAttackKind);
            d.RunUntil(x => x.Model.State != PlayerState.Attacking, 90);
            float gap = Directions.Flatten(target - d.World.Position).Length();
            float stopAt = d.World.SelfRadius + d.World.LockTargetRadius + d.Model.Tuning.LungeStopGap;
            Assert.GreaterOrEqual(gap, stopAt - 0.01f, "never runs into (or through) the target");
            Assert.Less(gap, stopAt + 0.3f, "but does close the distance");
        }

        [Test]
        public void AttacksTrackTheLockTargetDuringStartupThenCommit()
        {
            var moves = ElementMoveSet.CreateFireFluid();
            moves.Heavy.LungeDistance = 0f;                      // keep the geometry fixed for the measurement
            var d = new PlayerDriver(null, moves);
            d.LockOn(new Vector3(3f, 0f, 0f));                   // 90 degrees to the right
            d.Step(Pad.Heavy);
            d.Step();                                             // quick heavy: 0.28 s startup, 720 deg/s tracking
            d.RunUntil(x => x.Model.IsAttackActive, 40);
            Assert.That(d.Model.FacingYaw, Is.EqualTo(90f).Within(0.5f));
            d.LockOn(new Vector3(-3f, 0f, 0f));                  // target jumps behind us mid-swing
            d.Run(3);
            Assert.That(d.Model.FacingYaw, Is.EqualTo(90f).Within(1e-3f), "committed: no more turning");
        }
    }
}
