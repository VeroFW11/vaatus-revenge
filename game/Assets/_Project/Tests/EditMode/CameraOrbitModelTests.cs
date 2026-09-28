using System;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // The orbit camera's rules: look input, pitch limits, lock-on framing, pivot follow and wall collision.
    // These are the things a player feels every second, so they're pinned down here.
    public class CameraOrbitModelTests
    {
        const float Frame = 1f / 60f;
        const float PivotHeight = 1.55f;

        static OrbitCameraModel NewCamera(CameraTuning tuning = null, float yaw = 0f, float pitch = 12f)
        {
            var camera = new OrbitCameraModel(tuning ?? new CameraTuning());
            camera.Snap(Vector3.Zero, yaw, pitch);
            return camera;
        }

        static OrbitCameraInput Stick(float x, float y)
        {
            return new OrbitCameraInput { Look = new Vector2(x, y) };
        }

        static OrbitCameraInput Mouse(float x, float y)
        {
            return new OrbitCameraInput { Look = new Vector2(x, y), LookIsMouse = true };
        }

        static OrbitCameraInput LockOn(Vector3 point)
        {
            return new OrbitCameraInput { HasLockTarget = true, LockTargetPoint = point };
        }

        // Whole frames in the rig's order, with nothing in the way of the camera.
        static void Run(OrbitCameraModel camera, OrbitCameraInput input, float seconds, Vector3 feet = default(Vector3))
        {
            int frames = (int)Math.Round(seconds / Frame);
            for (int i = 0; i < frames; i++)
            {
                camera.UpdatePivot(feet, Frame);
                camera.UpdateOrientation(input, Frame);
                camera.UpdateDistance(float.PositiveInfinity, Frame);
                AssertFinite(camera);
            }
        }

        static void AssertFinite(OrbitCameraModel camera)
        {
            Assert.IsTrue(IsFinite(camera.Yaw), "yaw is " + camera.Yaw);
            Assert.IsTrue(IsFinite(camera.Pitch), "pitch is " + camera.Pitch);
            Assert.IsTrue(IsFinite(camera.Distance), "distance is " + camera.Distance);
            Assert.IsTrue(IsFinite(camera.DesiredDistance), "desired distance is " + camera.DesiredDistance);
            Vector3 position = camera.CameraPosition;
            Assert.IsTrue(IsFinite(position.X) && IsFinite(position.Y) && IsFinite(position.Z), "camera position is " + position);
            Vector3 pivot = camera.Pivot;
            Assert.IsTrue(IsFinite(pivot.X) && IsFinite(pivot.Y) && IsFinite(pivot.Z), "pivot is " + pivot);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        [Test]
        public void YawWrapsAtPlusMinus180WithoutJumping()
        {
            var camera = NewCamera(yaw: 170f);
            float previous = camera.Yaw;
            for (int i = 0; i < 120; i++)
            {
                camera.UpdateOrientation(Stick(1f, 0f), Frame);
                Assert.That(camera.Yaw, Is.GreaterThan(-180f).And.LessThanOrEqualTo(180f));
                Assert.That(Angles.Delta(previous, camera.Yaw), Is.EqualTo(200f * Frame).Within(1e-3f), "every frame is one small step");
                previous = camera.Yaw;
            }
            // 2 s at 200 deg/s from 170 = 570 degrees, which is -150.
            Assert.That(camera.Yaw, Is.EqualTo(-150f).Within(0.01f));

            var mouseCamera = NewCamera(yaw: 179f);
            mouseCamera.UpdateOrientation(Mouse(20f, 0f), Frame);
            Assert.That(mouseCamera.Yaw, Is.EqualTo(-178.6f).Within(1e-3f));
        }

        [Test]
        public void PitchIsClampedBothWays()
        {
            var camera = NewCamera();
            Run(camera, Stick(0f, 1f), 3f);
            Assert.That(camera.Pitch, Is.EqualTo(-40f).Within(1e-4f), "looking up stops at MinPitch");
            Run(camera, Stick(0f, -1f), 3f);
            Assert.That(camera.Pitch, Is.EqualTo(65f).Within(1e-4f), "looking down stops at MaxPitch");
            camera.UpdateOrientation(Mouse(0f, 100000f), Frame);
            Assert.That(camera.Pitch, Is.EqualTo(-40f).Within(1e-4f), "a huge mouse flick is clamped too");
        }

        [Test]
        public void MisorderedPitchLimitsStillClamp()
        {
            var camera = NewCamera(new CameraTuning { MinPitch = 50f, MaxPitch = -10f });
            Run(camera, Stick(0f, -1f), 3f);
            Assert.That(camera.Pitch, Is.EqualTo(50f).Within(1e-4f));
        }

        [Test]
        public void PushingUpLooksUpUnlessInverted()
        {
            var camera = NewCamera();
            camera.UpdateOrientation(Stick(0f, 1f), Frame);
            Assert.Less(camera.Pitch, 12f, "stick up looks up (smaller pitch)");

            var mouseCamera = NewCamera();
            mouseCamera.UpdateOrientation(Mouse(0f, 10f), Frame);
            Assert.That(mouseCamera.Pitch, Is.EqualTo(12f - 1.2f).Within(1e-4f), "mouse up looks up");

            var inverted = NewCamera(new CameraTuning { InvertY = true });
            inverted.UpdateOrientation(Stick(0f, 1f), Frame);
            Assert.Greater(inverted.Pitch, 12f);
        }

        [Test]
        public void MouseLookIsNotScaledByFrameTime()
        {
            foreach (float dt in new[] { 0f, 1f / 30f, 1f / 144f, 0.5f })
            {
                var camera = NewCamera();
                camera.UpdateOrientation(Mouse(100f, 0f), dt);
                // 100 pixels x 0.12 degrees per pixel, at any frame rate.
                Assert.That(camera.Yaw, Is.EqualTo(12f).Within(1e-4f), "dt " + dt);
            }
        }

        [Test]
        public void StickLookIsASpeedWithAResponseCurve()
        {
            // Full tilt turns 200 deg/s whatever the frame rate: half a second is 100 degrees.
            var at30 = NewCamera();
            for (int i = 0; i < 15; i++) at30.UpdateOrientation(Stick(1f, 0f), 1f / 30f);
            var at144 = NewCamera();
            for (int i = 0; i < 72; i++) at144.UpdateOrientation(Stick(1f, 0f), 1f / 144f);
            Assert.That(at30.Yaw, Is.EqualTo(100f).Within(0.01f));
            Assert.That(at144.Yaw, Is.EqualTo(100f).Within(0.01f));

            // Half tilt is slower than half speed (0.5^1.6 = 0.33), for fine aiming.
            var half = NewCamera();
            half.UpdateOrientation(Stick(0.5f, 0f), 0.1f);
            Assert.That(half.Yaw, Is.EqualTo((float)Math.Pow(0.5, 1.6) * 200f * 0.1f).Within(1e-3f));

            // A two-second hitch doesn't swing a held stick 400 degrees in one frame.
            var hitch = NewCamera();
            hitch.UpdateOrientation(Stick(1f, 0f), 2f);
            Assert.That(hitch.Yaw, Is.EqualTo(20f).Within(1e-3f));
        }

        [Test]
        public void ResponseCurveKeepsDirectionAndFullTilt()
        {
            Vector2 full = OrbitCameraModel.ApplyResponseCurve(new Vector2(1f, 0f), 1.6f);
            Assert.That(full.X, Is.EqualTo(1f).Within(1e-5f));
            Vector2 diagonal = OrbitCameraModel.ApplyResponseCurve(new Vector2(0.70710678f, 0.70710678f), 1.6f);
            Assert.That(diagonal.Length(), Is.EqualTo(1f).Within(1e-4f), "diagonals aren't slowed down");
            Assert.That(diagonal.X, Is.EqualTo(diagonal.Y).Within(1e-5f));
            Vector2 over = OrbitCameraModel.ApplyResponseCurve(new Vector2(1.3f, 0f), 1.6f);
            Assert.That(over.Length(), Is.EqualTo(1f).Within(1e-5f));
            Assert.AreEqual(Vector2.Zero, OrbitCameraModel.ApplyResponseCurve(Vector2.Zero, 1.6f));
            Assert.AreEqual(Vector2.Zero, OrbitCameraModel.ApplyResponseCurve(new Vector2(float.NaN, 0f), 1.6f));
            Vector2 linear = OrbitCameraModel.ApplyResponseCurve(new Vector2(0.5f, 0f), 0f);
            Assert.That(linear.X, Is.EqualTo(0.5f).Within(1e-5f), "a zero exponent falls back to linear");
        }

        [Test]
        public void LockOnTurnsTheShortWayToATargetBehind()
        {
            foreach (float targetYaw in new[] { 160f, -160f, 180f })
            {
                var camera = NewCamera(yaw: 0f);
                Vector3 target = new Vector3(0f, PivotHeight, 0f) + Directions.FromYaw(targetYaw) * 10f;
                float direction = Angles.Delta(0f, targetYaw) >= 0f ? 1f : -1f;
                float previous = camera.Yaw;
                for (int i = 0; i < 90; i++)
                {
                    camera.UpdatePivot(Vector3.Zero, Frame);
                    camera.UpdateOrientation(LockOn(target), Frame);
                    AssertFinite(camera);
                    // Always turning the same (short) way, never reversing or overshooting.
                    Assert.GreaterOrEqual(Angles.Delta(previous, camera.Yaw) * direction, -1e-3f, "target yaw " + targetYaw);
                    previous = camera.Yaw;
                }
                Assert.That(Angles.Delta(camera.Yaw, targetYaw), Is.EqualTo(0f).Within(0.5f), "target yaw " + targetYaw);
            }
        }

        [Test]
        public void LockOnAcrossTheWrapTakesTheShortWay()
        {
            var camera = NewCamera(yaw: 170f);
            Vector3 target = new Vector3(0f, PivotHeight, 0f) + Directions.FromYaw(-170f) * 10f;
            for (int i = 0; i < 60; i++)
            {
                camera.UpdatePivot(Vector3.Zero, Frame);
                camera.UpdateOrientation(LockOn(target), Frame);
                // 170 -> 180 -> -170 is 20 degrees; going the long way would pass through 0.
                Assert.GreaterOrEqual(Math.Abs(camera.Yaw), 169.9f);
            }
            Assert.That(Angles.Delta(camera.Yaw, -170f), Is.EqualTo(0f).Within(0.1f));
        }

        [Test]
        public void LockOnRestsAtTheLockPitchForATargetAtChestHeight()
        {
            var camera = NewCamera(yaw: 30f);
            Run(camera, LockOn(new Vector3(0f, 1.3f, 8f)), 2f);
            Assert.That(camera.Yaw, Is.EqualTo(0f).Within(0.05f));
            Assert.That(camera.Pitch, Is.EqualTo(18f).Within(0.05f));
            Assert.IsTrue(camera.IsLockedOn);
        }

        [Test]
        public void LockOnTiltsUpToKeepAnElevatedTargetInFrame()
        {
            // A crossbowman on a platform 3 m up and 6 m away: aim point 4.3 m high.
            var camera = NewCamera();
            var target = new Vector3(0f, 4.3f, 6f);
            Run(camera, LockOn(target), 2f);
            float pitchToTarget = Directions.PitchOf(target - camera.Pivot);
            Assert.Less(camera.Pitch, 18f, "tilted up from the resting lock-on pitch");
            Assert.LessOrEqual(camera.Pitch - pitchToTarget, 20f + 0.05f, "within LockOnFramingAbove of the centre");
            // And from where the camera really is, the target is well inside the 60 degree field of view.
            float aboveCentre = camera.Pitch - Directions.PitchOf(target - camera.CameraPosition);
            Assert.Less(aboveCentre, 25f);
        }

        [Test]
        public void LockOnTiltsDownForATargetBelowALedge()
        {
            // Player on a 3 m platform, enemy on the ground 2 m out from the edge.
            var feet = new Vector3(0f, 3f, 0f);
            var camera = new OrbitCameraModel(new CameraTuning());
            camera.Snap(feet, 0f, 12f);
            var target = new Vector3(0f, 1.3f, 2f);
            Run(camera, LockOn(target), 2f, feet);
            float pitchToTarget = Directions.PitchOf(target - camera.Pivot);
            Assert.Greater(camera.Pitch, 18f, "tilted down");
            Assert.LessOrEqual(pitchToTarget - camera.Pitch, 25f + 0.05f, "within LockOnFramingBelow of the centre");
        }

        [Test]
        public void DegenerateLockTargetsNeverProduceNaN()
        {
            var pivot = new Vector3(0f, PivotHeight, 0f);
            var holdYaw = new[]
            {
                pivot,                                   // exactly on the pivot
                pivot + new Vector3(0f, 10f, 0f),        // straight above
                pivot - new Vector3(0f, 10f, 0f),        // straight below
                Vector3.Zero,                            // at the player's feet
                pivot + new Vector3(1e-4f, 1e-4f, 0f),   // a hair off the pivot
            };
            foreach (Vector3 target in holdYaw)
            {
                var camera = NewCamera(yaw: 37f);
                Run(camera, LockOn(target), 1f);
                Assert.That(camera.Yaw, Is.EqualTo(37f).Within(1e-4f), "no direction to turn to: hold the yaw (" + target + ")");
                Assert.That(camera.Pitch, Is.InRange(-40f, 65f));
            }

            var straightAbove = NewCamera();
            Run(straightAbove, LockOn(pivot + new Vector3(0f, 10f, 0f)), 2f);
            Assert.That(straightAbove.Pitch, Is.EqualTo(-40f).Within(0.01f), "looks up as far as allowed");

            foreach (Vector3 target in new[] { new Vector3(1e6f, 0f, 1e6f), new Vector3(0f, PivotHeight, -4f) })
            {
                var camera = NewCamera(yaw: 37f);
                Run(camera, LockOn(target), 1f);
            }

            // Standing exactly on the camera's own position.
            var onCamera = NewCamera();
            Run(onCamera, LockOn(onCamera.CameraPosition), 1f);

            // A NaN target is treated as no target at all.
            var nanTarget = NewCamera(yaw: 37f);
            Run(nanTarget, LockOn(new Vector3(float.NaN, 0f, 0f)), 0.5f);
            Assert.IsFalse(nanTarget.IsLockedOn);
            Assert.That(nanTarget.Yaw, Is.EqualTo(37f).Within(1e-4f));
        }

        [Test]
        public void BadTimeStepsAreSafe()
        {
            var camera = NewCamera();
            foreach (float dt in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                camera.UpdatePivot(new Vector3(1f, 0f, 1f), dt);
                camera.UpdateOrientation(Stick(1f, 1f), dt);
                camera.UpdateOrientation(LockOn(new Vector3(3f, 1f, 3f)), dt);
                camera.UpdateOrientation(Stick(0f, 0f), dt);
                camera.UpdateDistance(2f, dt);
                AssertFinite(camera);
            }
            var still = NewCamera(yaw: 10f);
            still.UpdateOrientation(Stick(1f, 0f), 0f);
            Assert.That(still.Yaw, Is.EqualTo(10f).Within(1e-5f), "no time passed, no stick turn");
        }

        [Test]
        public void WallsPullTheCameraInInstantlyAndItEasesBackOut()
        {
            var camera = NewCamera();
            Assert.That(camera.Distance, Is.EqualTo(4f).Within(1e-4f));

            camera.UpdateDistance(1.5f, Frame);
            Assert.That(camera.Distance, Is.EqualTo(1.5f).Within(1e-4f), "pulled in on the same frame");

            camera.UpdateDistance(4f, Frame);
            Assert.Greater(camera.Distance, 1.5f, "starts easing out");
            Assert.Less(camera.Distance, 1.7f, "but doesn't pop back");

            float elapsed = Frame;
            float previous = camera.Distance;
            while (elapsed < 0.35f - 1e-4f)
            {
                camera.UpdateDistance(4f, Frame);
                Assert.GreaterOrEqual(camera.Distance, previous);
                previous = camera.Distance;
                elapsed += Frame;
            }
            Assert.Greater(camera.Distance, 3.9f, "back out after about CollisionEaseOutTime");
            Assert.LessOrEqual(camera.Distance, 4f);

            camera.UpdateDistance(0.4f, Frame);
            Assert.That(camera.Distance, Is.EqualTo(0.4f).Within(1e-4f), "a wall mid-ease pulls in instantly too");
            camera.UpdateDistance(0.1f, Frame);
            Assert.That(camera.Distance, Is.EqualTo(0.1f).Within(1e-4f), "walls closer than MinDistance still win");
            camera.UpdateDistance(-3f, Frame);
            Assert.That(camera.Distance, Is.EqualTo(0f).Within(1e-4f), "bad probe results clamp at the pivot");
        }

        [Test]
        public void LockingOnMovesTheCameraFurtherBack()
        {
            var camera = NewCamera();
            Run(camera, LockOn(new Vector3(0f, 1.3f, 8f)), 2f);
            Assert.That(camera.Distance, Is.EqualTo(4.6f).Within(0.01f));
            Run(camera, Stick(0f, 0f), 2f);
            Assert.That(camera.Distance, Is.EqualTo(4.0f).Within(0.01f));
        }

        [Test]
        public void PivotFollowsTightlySidewaysAndLooselyUpAndDown()
        {
            var camera = NewCamera();
            // The player steps 1 m sideways and 1 m up (onto a ledge) in one frame.
            var feet = new Vector3(1f, 1f, 0f);
            for (int i = 0; i < 6; i++) camera.UpdatePivot(feet, Frame);
            float sidewaysLag = Math.Abs(camera.Pivot.X - feet.X);
            float verticalLag = Math.Abs(camera.Pivot.Y - (feet.Y + PivotHeight));
            Assert.Less(sidewaysLag, verticalLag);

            for (int i = 0; i < 120; i++) camera.UpdatePivot(feet, Frame);
            Assert.That(camera.Pivot.X, Is.EqualTo(1f).Within(0.01f));
            Assert.That(camera.Pivot.Y, Is.EqualTo(1f + PivotHeight).Within(0.01f));
        }

        [Test]
        public void AJumpStaysInsideTheLagCapSoItNeverJolts()
        {
            // 1.25 m jump (gravity 28) while running: the pivot's own smoothing handles it, the cap is never hit.
            var tuning = new CameraTuning();
            var camera = NewCamera(tuning);
            var feet = Vector3.Zero;
            float upSpeed = (float)Math.Sqrt(2.0 * 28.0 * 1.25);
            float maxLag = 0f;
            for (int i = 0; i < 90; i++)
            {
                upSpeed -= 28f * Frame;
                feet.Y = Math.Max(0f, feet.Y + upSpeed * Frame);
                feet.Z += 4.8f * Frame;
                camera.UpdatePivot(feet, Frame);
                maxLag = Math.Max(maxLag, Math.Abs(camera.Pivot.Y - (feet.Y + PivotHeight)));
            }
            Assert.Greater(maxLag, 0.3f, "the camera doesn't bob along with the jump");
            Assert.Less(maxLag, tuning.PivotMaxVerticalLag - 0.2f);
        }

        [Test]
        public void ALongFallNeverDropsThePlayerOffScreen()
        {
            var tuning = new CameraTuning();
            var camera = NewCamera(tuning);
            var feet = Vector3.Zero;
            for (int i = 0; i < 60; i++)
            {
                feet.Y -= 20f * Frame; // falling at 20 m/s
                camera.UpdatePivot(feet, Frame);
                float lag = Math.Abs(camera.Pivot.Y - (feet.Y + PivotHeight));
                Assert.LessOrEqual(lag, tuning.PivotMaxVerticalLag + 1e-4f);
                // Still inside the 60 degree field of view (30 degrees below the centre) seen from 4 m back.
                Assert.Less(Math.Atan2(lag, tuning.FreeDistance) * Directions.Rad2Deg, 30.0);
            }
        }

        [Test]
        public void PivotSnapsOnTeleportAndFreezesWhenGameTimeStops()
        {
            var camera = NewCamera();
            camera.UpdatePivot(new Vector3(50f, 0f, 0f), Frame);
            Assert.That(camera.Pivot.X, Is.EqualTo(50f).Within(1e-4f), "a respawn cuts instead of gliding across the arena");
            Assert.That(camera.Pivot.Y, Is.EqualTo(PivotHeight).Within(1e-4f));

            Vector3 before = camera.Pivot;
            camera.UpdatePivot(new Vector3(51f, 0f, 0f), 0f);
            Assert.AreEqual(before, camera.Pivot, "during hitstop the pivot waits with the player");
        }

        [Test]
        public void RecenterSwingsBehindThePlayerTheShortWay()
        {
            var camera = NewCamera(yaw: -120f, pitch: 40f);
            camera.BeginRecenter(90f);
            float previous = camera.Yaw;
            for (int i = 0; i < 60; i++)
            {
                camera.UpdateOrientation(Stick(0f, 0f), Frame);
                // -120 to 90 is 150 degrees anticlockwise (210 the other way).
                Assert.LessOrEqual(Angles.Delta(previous, camera.Yaw), 1e-3f);
                previous = camera.Yaw;
            }
            Assert.IsFalse(camera.IsRecentering);
            Assert.That(camera.Yaw, Is.EqualTo(90f).Within(1e-3f));
            Assert.That(camera.Pitch, Is.EqualTo(12f).Within(1e-3f));
        }

        [Test]
        public void LookInputCancelsARecentre()
        {
            var camera = NewCamera();
            camera.BeginRecenter(90f);
            camera.UpdateOrientation(Stick(0f, 0f), Frame);
            Assert.IsTrue(camera.IsRecentering);
            camera.UpdateOrientation(Stick(-0.5f, 0f), Frame);
            Assert.IsFalse(camera.IsRecentering, "the player took over");
        }

        [Test]
        public void MissingTuningFallsBackToDefaults()
        {
            var camera = new OrbitCameraModel(null);
            Assert.IsNotNull(camera.Tuning);
            camera.Tuning = null;
            Assert.IsNotNull(camera.Tuning);
            Run(camera, Stick(1f, 0f), 0.1f);
        }
    }
}
