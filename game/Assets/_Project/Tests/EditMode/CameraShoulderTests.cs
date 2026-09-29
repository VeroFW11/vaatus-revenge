using System;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // The over-the-shoulder camera (like Marvel's Spider-Man 2): shoulder offset and swapping, walls on the
    // shoulder side, lock-on framing with the offset, and the combat pull-back.
    public class CameraShoulderTests
    {
        const float Frame = 1f / 60f;
        const float Open = float.PositiveInfinity; // "no wall" for a probe

        static OrbitCameraModel NewCamera(CameraTuning tuning = null, float yaw = 0f)
        {
            var camera = new OrbitCameraModel(tuning ?? new CameraTuning());
            camera.Snap(Vector3.Zero, yaw, 12f);
            return camera;
        }

        // One whole frame in the rig's order, with the given free space around the camera.
        static void Step(OrbitCameraModel camera, OrbitCameraInput input, float freeUp = Open, float freeRight = Open,
            float freeLeft = Open, float freeBack = Open, Vector3 feet = default(Vector3))
        {
            camera.UpdatePivot(feet, Frame);
            camera.UpdateOrientation(input, Frame);
            camera.UpdateLift(freeUp, Frame);
            camera.UpdateShoulder(freeRight, freeLeft, Frame);
            camera.UpdateDistance(freeBack, Frame);
            AssertFinite(camera);
        }

        static void Run(OrbitCameraModel camera, OrbitCameraInput input, float seconds, float freeRight = Open, float freeLeft = Open)
        {
            int frames = (int)Math.Round(seconds / Frame);
            for (int i = 0; i < frames; i++) Step(camera, input, Open, freeRight, freeLeft);
        }

        static OrbitCameraInput Idle()
        {
            return new OrbitCameraInput();
        }

        static OrbitCameraInput Foe(float distance)
        {
            return new OrbitCameraInput { HasFoe = true, NearestFoeDistance = distance };
        }

        static OrbitCameraInput LockOn(Vector3 point)
        {
            return new OrbitCameraInput { HasLockTarget = true, LockTargetPoint = point };
        }

        static void AssertFinite(OrbitCameraModel camera)
        {
            Vector3 position = camera.CameraPosition;
            Vector3 shoulder = camera.ShoulderPoint;
            Assert.IsTrue(IsFinite(camera.Yaw) && IsFinite(camera.Pitch), "angles");
            Assert.IsTrue(IsFinite(camera.ShoulderOffset) && IsFinite(camera.Lift) && IsFinite(camera.Distance), "offsets");
            Assert.IsTrue(IsFinite(position.X) && IsFinite(position.Y) && IsFinite(position.Z), "camera position " + position);
            Assert.IsTrue(IsFinite(shoulder.X) && IsFinite(shoulder.Y) && IsFinite(shoulder.Z), "shoulder point " + shoulder);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static void AssertVector(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(tolerance), "x");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(tolerance), "y");
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(tolerance), "z");
        }

        // Horizontal angle of a point as seen by the camera: negative = left of the screen centre.
        static float ScreenAngle(OrbitCameraModel camera, Vector3 point)
        {
            return Angles.Delta(camera.Yaw, Directions.YawOf(point - camera.CameraPosition, camera.Yaw));
        }

        [Test]
        public void CentredTuningMatchesTheOldFramingExactly()
        {
            CameraTuning centred = CameraTuning.CreateCentred();
            Assert.AreEqual(4.0f, centred.FreeDistance);
            Assert.AreEqual(4.6f, centred.LockedDistance);
            Assert.AreEqual(1.55f, centred.PivotHeight);

            var camera = NewCamera(centred, 30f);
            var random = new Random(7);
            for (int i = 0; i < 360; i++)
            {
                // A busy sequence: looking around, locking on, foes, swap presses and walls everywhere.
                var input = new OrbitCameraInput
                {
                    Look = new Vector2((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() - 0.5f),
                    HasLockTarget = i > 120 && i < 240,
                    LockTargetPoint = new Vector3(6f, 1.3f, 9f),
                    SwapShoulder = i % 50 == 0,
                    HasFoe = true,
                    NearestFoeDistance = 3f,
                };
                Step(camera, input, 0.05f, 0.1f, 0.3f, 2f + (float)random.NextDouble() * 3f, new Vector3(i * 0.01f, 0f, 0f));

                // The shoulder point IS the pivot and the camera sits straight behind it: the old formula, bit for bit.
                Assert.AreEqual(camera.Pivot, camera.ShoulderPoint);
                Assert.AreEqual(camera.Pivot - camera.Forward * camera.Distance, camera.CameraPosition);
                Assert.AreEqual(0f, camera.ShoulderOffset);
                Assert.AreEqual(0f, camera.Lift);
                Assert.AreEqual(0, camera.ShoulderSide);
                Assert.IsFalse(camera.IsShoulderAutoSwapped);
            }
            Assert.That(camera.DesiredDistance, Is.EqualTo(4f).Within(1e-3f), "no combat pull-back in the centred preset");
        }

        [Test]
        public void TheDefaultIsOverTheRightShoulderWithTheFighterLeftOfCentre()
        {
            var tuning = new CameraTuning();
            Assert.Greater(tuning.ShoulderOffset, 0f);
            Assert.Less(tuning.FreeDistance, CameraTuning.CreateCentred().FreeDistance, "closer than the centred camera");

            var camera = NewCamera(tuning);
            Assert.AreEqual(1, camera.ShoulderSide);
            Assert.That(camera.ShoulderOffset, Is.EqualTo(tuning.ShoulderOffset).Within(1e-6f));
            // Yaw 0 looks along +Z, so the camera's right is +X.
            AssertVector(new Vector3(tuning.ShoulderOffset, tuning.PivotHeight, 0f), camera.ShoulderPoint);
            AssertVector(camera.ShoulderPoint - camera.Forward * tuning.FreeDistance, camera.CameraPosition);
            Assert.Less(ScreenAngle(camera, camera.Pivot), -1f, "the fighter sits left of the screen centre");

            // Facing +X (yaw 90), the camera's right is -Z.
            var turned = NewCamera(tuning, 90f);
            AssertVector(new Vector3(0f, tuning.PivotHeight, -tuning.ShoulderOffset), turned.ShoulderPoint);
        }

        [Test]
        public void ANegativeOffsetLooksOverTheLeftShoulder()
        {
            var tuning = new CameraTuning { ShoulderOffset = -0.55f, LockedShoulderOffset = -0.35f };
            var camera = NewCamera(tuning);
            Assert.AreEqual(-1, camera.ShoulderSide);
            AssertVector(new Vector3(-0.55f, tuning.PivotHeight, 0f), camera.ShoulderPoint);
            Assert.Greater(ScreenAngle(camera, camera.Pivot), 1f, "the fighter sits right of the screen centre");
        }

        static OrbitCameraInput HoldingSwap()
        {
            return new OrbitCameraInput { SwapShoulderHeld = true };
        }

        [Test]
        public void TheSwapButtonMustBeHeldBriefly()
        {
            var tuning = new CameraTuning();
            var camera = NewCamera(tuning);
            int almost = (int)(tuning.ShoulderSwapHoldTime / Frame) - 2;
            for (int i = 0; i < almost; i++) Step(camera, HoldingSwap());
            Assert.AreEqual(1, camera.ShoulderSide, "not held long enough yet");
            for (int i = 0; i < 4; i++) Step(camera, HoldingSwap());
            Assert.AreEqual(-1, camera.ShoulderSide, "held long enough: swapped");

            Run(camera, HoldingSwap(), 2f);
            Assert.AreEqual(-1, camera.ShoulderSide, "one swap per hold, however long it's held");

            Step(camera, Idle());
            Run(camera, HoldingSwap(), tuning.ShoulderSwapHoldTime + 0.1f);
            Assert.AreEqual(1, camera.ShoulderSide, "let go and hold again to swap back");
        }

        [Test]
        public void QuickClicksNeverSwap()
        {
            // The accidental L3 clicks from pushing the stick hard while sprinting: 0.1 s presses.
            var camera = NewCamera();
            for (int click = 0; click < 20; click++)
            {
                for (int i = 0; i < 6; i++) Step(camera, HoldingSwap());
                for (int i = 0; i < 3; i++) Step(camera, Idle());
            }
            Assert.AreEqual(1, camera.ShoulderSide);
            Assert.That(camera.ShoulderOffset, Is.EqualTo(0.55f).Within(1e-4f));
        }

        [Test]
        public void AZeroHoldTimeSwapsOnPressOncePerPress()
        {
            var camera = NewCamera(new CameraTuning { ShoulderSwapHoldTime = 0f });
            Step(camera, HoldingSwap());
            Assert.AreEqual(-1, camera.ShoulderSide);
            Run(camera, HoldingSwap(), 1f);
            Assert.AreEqual(-1, camera.ShoulderSide, "still one swap per press");
            Step(camera, Idle());
            Step(camera, HoldingSwap());
            Assert.AreEqual(1, camera.ShoulderSide);
        }

        [Test]
        public void TheSwapHoldWaitsWhileTimeIsStopped()
        {
            var camera = NewCamera();
            for (int i = 0; i < 100; i++) camera.UpdateOrientation(HoldingSwap(), 0f);
            Assert.AreEqual(1, camera.ShoulderSide);
        }

        [Test]
        public void AnInstantSwapRequestSlidesSmoothlyToTheOtherShoulder()
        {
            var camera = NewCamera();
            Step(camera, new OrbitCameraInput { SwapShoulder = true });
            Assert.AreEqual(-1, camera.ShoulderSide);

            float previous = camera.ShoulderOffset;
            for (int i = 0; i < 90; i++)
            {
                Step(camera, Idle());
                Assert.LessOrEqual(camera.ShoulderOffset, previous + 1e-6f, "only ever moves towards the new side");
                Assert.Less(previous - camera.ShoulderOffset, 0.1f, "a slide, not a jump");
                previous = camera.ShoulderOffset;
            }
            Assert.That(camera.ShoulderOffset, Is.EqualTo(-0.55f).Within(0.01f));

            Step(camera, new OrbitCameraInput { SwapShoulder = true });
            Run(camera, Idle(), 1.5f);
            Assert.AreEqual(1, camera.ShoulderSide);
            Assert.That(camera.ShoulderOffset, Is.EqualTo(0.55f).Within(0.01f));
        }

        [Test]
        public void ARespawnKeepsTheChosenShoulder()
        {
            var camera = NewCamera();
            camera.SwapShoulder();
            camera.Snap(new Vector3(10f, 0f, 10f), 45f, 12f);
            Assert.AreEqual(-1, camera.ShoulderSide);
            Assert.That(camera.ShoulderOffset, Is.EqualTo(-0.55f).Within(1e-6f), "cuts straight to it, no slide");
        }

        [Test]
        public void CallersThatNeverProbeTheSidesStillGetTheOffset()
        {
            // The headless harness only calls UpdatePivot, UpdateOrientation and UpdateDistance.
            var camera = NewCamera();
            for (int i = 0; i < 30; i++)
            {
                camera.UpdatePivot(Vector3.Zero, Frame);
                camera.UpdateOrientation(Idle(), Frame);
                camera.UpdateDistance(Open, Frame);
            }
            Assert.That(camera.ShoulderOffset, Is.EqualTo(0.55f).Within(1e-5f));
            AssertVector(camera.Pivot + camera.Right * 0.55f - camera.Forward * 3.2f, camera.CameraPosition);
        }

        [Test]
        public void AShoulderWallShrinksTheOffsetInstantlyAndItEasesBack()
        {
            var camera = NewCamera(new CameraTuning { AutoSwapShoulderWhenBlocked = false });
            Step(camera, Idle(), Open, 0.2f, Open);
            Assert.That(camera.ShoulderOffset, Is.EqualTo(0.2f).Within(1e-5f), "pulled in on the same frame");

            Step(camera, Idle());
            Assert.Greater(camera.ShoulderOffset, 0.2f, "starts easing back out");
            Assert.Less(camera.ShoulderOffset, 0.35f, "but doesn't pop");
            Run(camera, Idle(), 0.5f);
            Assert.That(camera.ShoulderOffset, Is.EqualTo(0.55f).Within(0.01f));
        }

        [Test]
        public void ABlockedShoulderSwapsSidesAndSwapsBackOnceClear()
        {
            var tuning = new CameraTuning();
            var camera = NewCamera(tuning);
            // Hugging a wall on the right.
            Step(camera, Idle(), Open, 0.05f, Open);
            Assert.LessOrEqual(camera.ShoulderOffset, 0.05f + 1e-6f, "never pushed into the wall");
            Assert.IsTrue(camera.IsShoulderAutoSwapped);
            Assert.AreEqual(-1, camera.ShoulderSide);

            for (int i = 0; i < 90; i++)
            {
                Step(camera, Idle(), Open, 0.05f, Open);
                if (camera.ShoulderOffset > 0f) Assert.LessOrEqual(camera.ShoulderOffset, 0.05f + 1e-6f);
            }
            Assert.That(camera.ShoulderOffset, Is.EqualTo(-0.55f).Within(0.01f), "moved over to the open side");

            // The wall ends: stay put for ShoulderSwapBackDelay (no flip-flopping), then go back.
            Run(camera, Idle(), tuning.ShoulderSwapBackDelay - 0.1f);
            Assert.AreEqual(-1, camera.ShoulderSide, "still waiting to be sure the right side stays clear");
            Run(camera, Idle(), 0.1f + 1.5f);
            Assert.IsFalse(camera.IsShoulderAutoSwapped);
            Assert.AreEqual(1, camera.ShoulderSide);
            Assert.That(camera.ShoulderOffset, Is.EqualTo(0.55f).Within(0.01f));
        }

        [Test]
        public void AutoSwapCanBeSwitchedOff()
        {
            var camera = NewCamera(new CameraTuning { AutoSwapShoulderWhenBlocked = false });
            Run(camera, Idle(), 1f, 0.05f, Open);
            Assert.AreEqual(1, camera.ShoulderSide);
            Assert.That(camera.ShoulderOffset, Is.EqualTo(0.05f).Within(1e-5f));
        }

        [Test]
        public void ANarrowCorridorShrinksTheOffsetWithoutFlipping()
        {
            // Walls close on both sides: swapping wouldn't help, so the camera just tucks in.
            var camera = NewCamera();
            Run(camera, Idle(), 2f, 0.2f, 0.2f);
            Assert.IsFalse(camera.IsShoulderAutoSwapped);
            Assert.AreEqual(1, camera.ShoulderSide);
            Assert.That(camera.ShoulderOffset, Is.EqualTo(0.2f).Within(1e-5f));
        }

        [Test]
        public void WallsNeverPutTheCameraInsideOrBehindGeometry()
        {
            // Random walls all around, changing every frame, with shoulder swaps, lock-ons and fights mixed in.
            // Whatever happens, every part of the camera stays inside the free space the probes reported.
            var camera = NewCamera();
            var random = new Random(12345);
            for (int i = 0; i < 3000; i++)
            {
                float freeUp = random.NextDouble() < 0.3 ? Open : (float)random.NextDouble() * 0.3f;
                float freeRight = random.NextDouble() < 0.3 ? Open : (float)random.NextDouble() * 0.7f;
                float freeLeft = random.NextDouble() < 0.3 ? Open : (float)random.NextDouble() * 0.7f;
                float freeBack = random.NextDouble() < 0.3 ? Open : (float)random.NextDouble() * 5f;
                var input = new OrbitCameraInput
                {
                    Look = new Vector2((float)random.NextDouble() * 2f - 1f, 0f),
                    SwapShoulder = random.NextDouble() < 0.02,
                    HasLockTarget = (i / 400) % 2 == 1,
                    LockTargetPoint = new Vector3(3f, 1.3f, 8f),
                    HasFoe = (i / 250) % 2 == 0,
                    NearestFoeDistance = 4f,
                };
                Step(camera, input, freeUp, freeRight, freeLeft, freeBack);

                Assert.LessOrEqual(camera.Lift, freeUp + 1e-5f, "frame " + i + ": through the ceiling");
                if (camera.ShoulderOffset > 0f) Assert.LessOrEqual(camera.ShoulderOffset, freeRight + 1e-5f, "frame " + i + ": into the right wall");
                if (camera.ShoulderOffset < 0f) Assert.LessOrEqual(-camera.ShoulderOffset, freeLeft + 1e-5f, "frame " + i + ": into the left wall");
                Assert.LessOrEqual(camera.Distance, freeBack + 1e-5f, "frame " + i + ": through the wall behind");
            }
        }

        [Test]
        public void LockOnAimsFromTheShoulderSoTheTargetSitsOnTheCentreLine()
        {
            var camera = NewCamera();
            var target = new Vector3(4f, 1.3f, 12f);
            Run(camera, LockOn(target), 2f);

            Assert.That(camera.ShoulderOffset, Is.EqualTo(0.35f).Within(0.01f), "the smaller locked-on offset");
            Assert.That(camera.Distance, Is.EqualTo(4.0f).Within(0.02f), "the wider locked-on distance");
            Assert.That(ScreenAngle(camera, target), Is.EqualTo(0f).Within(0.3f), "target on the centre line");
            Assert.Less(ScreenAngle(camera, camera.Pivot), -1f, "fighter still off to the left");
            float aboveCentre = camera.Pitch - Directions.PitchOf(target - camera.CameraPosition);
            Assert.Less(Math.Abs(aboveCentre), 30f, "and inside the frame vertically");
        }

        [Test]
        public void LockOnAtMeleeRangeCapsTheShoulderCorrection()
        {
            // 0.9 m away the full correction would be asin(0.35 / 0.9) = 23 degrees; it's capped at 15.
            var camera = NewCamera();
            var target = new Vector3(0f, 1.3f, 0.9f);
            Run(camera, LockOn(target), 2f);
            float correction = Angles.Delta(camera.Yaw, 0f);
            Assert.That(correction, Is.EqualTo(15f).Within(0.5f));
            Assert.Less(Math.Abs(ScreenAngle(camera, target)), 10f, "the target stays near the centre");
        }

        [Test]
        public void LockOnWithTheOffsetNeverProducesNaN()
        {
            var pivot = new Vector3(0f, 1.6f, 0f);
            var holdYaw = new[]
            {
                pivot,                                  // exactly on the pivot
                pivot + new Vector3(0f, 10f, 0f),       // straight above
                pivot - new Vector3(0f, 10f, 0f),       // straight below
                pivot + new Vector3(1e-4f, 0f, 1e-4f),  // a hair off the pivot
            };
            foreach (Vector3 target in holdYaw)
            {
                var camera = NewCamera(null, 37f);
                Run(camera, LockOn(target), 1f, 0.1f, Open);
                Assert.That(camera.Yaw, Is.EqualTo(37f).Within(1e-4f), "no direction to turn to: hold the yaw (" + target + ")");
            }

            var onShoulder = NewCamera();
            Run(onShoulder, LockOn(onShoulder.ShoulderPoint), 1f);
            var onCamera = NewCamera();
            Run(onCamera, LockOn(onCamera.CameraPosition), 1f);
            var far = NewCamera();
            Run(far, LockOn(new Vector3(1e6f, 0f, 1e6f)), 1f);
        }

        [Test]
        public void CombatPullbackEasesOutAndBackIn()
        {
            var tuning = new CameraTuning();
            var camera = NewCamera(tuning);
            float wide = tuning.FreeDistance + tuning.CombatPullback;

            Step(camera, Foe(5f));
            Run(camera, Foe(5f), 0.1f);
            Assert.Greater(camera.DesiredDistance, tuning.FreeDistance, "starts widening");
            Assert.Less(camera.DesiredDistance, tuning.FreeDistance + 0.3f, "gently, not in one jump");

            float previous = camera.DesiredDistance;
            for (int i = 0; i < 120; i++)
            {
                Step(camera, Foe(5f));
                Assert.GreaterOrEqual(camera.DesiredDistance, previous - 1e-5f);
                previous = camera.DesiredDistance;
            }
            Assert.That(camera.DesiredDistance, Is.EqualTo(wide).Within(0.02f));
            Assert.That(camera.Lift, Is.EqualTo(tuning.CombatPullbackHeight).Within(0.01f), "and a touch higher");

            // The fight moves away: stay wide for the release delay, then ease back in.
            Run(camera, Idle(), tuning.CombatFramingReleaseDelay - 0.1f);
            Assert.Greater(camera.DesiredDistance, wide - 0.05f, "doesn't pump in the moment a foe steps out");
            previous = camera.DesiredDistance;
            for (int i = 0; i < 180; i++)
            {
                Step(camera, Idle());
                Assert.LessOrEqual(camera.DesiredDistance, previous + 1e-5f);
                previous = camera.DesiredDistance;
            }
            Assert.That(camera.DesiredDistance, Is.EqualTo(tuning.FreeDistance).Within(0.02f));
            Assert.That(camera.Lift, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void CombatPullbackNeedsANearbyFoeAndNoLockOn()
        {
            var camera = NewCamera();
            Run(camera, Foe(12f), 2f);
            Assert.That(camera.DesiredDistance, Is.EqualTo(3.2f).Within(1e-3f), "a foe outside the radius doesn't count");

            var locked = new OrbitCameraInput { HasFoe = true, NearestFoeDistance = 3f, HasLockTarget = true, LockTargetPoint = new Vector3(0f, 1.3f, 5f) };
            Run(camera, locked, 2f);
            Assert.That(camera.CombatFraming, Is.EqualTo(0f).Within(0.01f), "locked on: the locked framing takes over");
            Assert.That(camera.DesiredDistance, Is.EqualTo(4.0f).Within(0.01f));

            var off = NewCamera(new CameraTuning { CombatPullback = 0f, CombatPullbackHeight = 0f });
            Run(off, Foe(3f), 2f);
            Assert.That(off.DesiredDistance, Is.EqualTo(3.2f).Within(1e-4f), "0 turns it off");
            Assert.AreEqual(0f, off.Lift);
        }

        [Test]
        public void TheCombatLiftStopsUnderALowCeiling()
        {
            var camera = NewCamera();
            for (int i = 0; i < 120; i++)
            {
                Step(camera, Foe(3f), 0.05f);
                Assert.LessOrEqual(camera.Lift, 0.05f + 1e-6f);
            }
            Assert.That(camera.Lift, Is.EqualTo(0.05f).Within(1e-4f));
            Step(camera, Foe(3f));
            Assert.Less(camera.Lift, 0.1f, "the room comes back gently");
            Run(camera, Foe(3f), 1f);
            Assert.That(camera.Lift, Is.EqualTo(0.2f).Within(0.01f));
        }

        [Test]
        public void BadProbeValuesAreSafe()
        {
            var camera = NewCamera();
            camera.UpdateLift(float.NaN, Frame);
            camera.UpdateShoulder(float.NaN, float.NaN, float.NaN);
            AssertFinite(camera);
            Assert.That(camera.ShoulderOffset, Is.EqualTo(0.55f).Within(1e-5f), "unknown counts as open");

            camera.UpdateShoulder(-5f, -5f, Frame);
            camera.UpdateLift(-1f, Frame);
            AssertFinite(camera);
            Assert.AreEqual(0f, camera.ShoulderOffset, "negative room means touching: no offset at all");
            Assert.AreEqual(0f, camera.Lift);

            var input = new OrbitCameraInput { HasFoe = true, NearestFoeDistance = float.NaN, Look = new Vector2(float.NaN, 1f) };
            Step(camera, input, float.NegativeInfinity, float.PositiveInfinity, float.NaN, float.NaN);
        }

        [Test]
        public void ChangingTheOffsetWhilePlayingEasesOver()
        {
            var tuning = new CameraTuning();
            var camera = NewCamera(tuning);
            tuning.ShoulderOffset = 0f; // Jeremy flips to centred in the Inspector mid-fight
            Step(camera, Idle());
            Assert.Greater(camera.ShoulderOffset, 0.4f, "no jump");
            Run(camera, Idle(), 1.5f);
            Assert.That(camera.ShoulderOffset, Is.EqualTo(0f).Within(1e-3f));
            Assert.AreEqual(0, camera.ShoulderSide);
        }
    }
}
