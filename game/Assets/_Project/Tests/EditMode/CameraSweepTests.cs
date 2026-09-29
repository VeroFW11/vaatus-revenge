using System;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Report 02, round 3: a swing (a lock-on turn, or turning the stick) used to sweep a pillar face straight into the
    // camera, which then jumped 2 m in one frame. The swing sweep looks where the camera is heading and starts the
    // glide in early. The rig's probes are faked with plain numbers, as in CameraCollisionTests.
    public class CameraSweepTests
    {
        const float Frame = 1f / 60f;
        const float Open = float.PositiveInfinity;

        static OrbitCameraModel NewCamera(CameraTuning tuning)
        {
            var camera = new OrbitCameraModel(tuning);
            camera.Snap(Vector3.Zero, 0f, 12f);
            return camera;
        }

        static void Turn(OrbitCameraModel camera, float stickX, Vector3 feet)
        {
            camera.UpdatePivot(feet, Frame);
            camera.UpdateOrientation(new OrbitCameraInput { Look = new Vector2(stickX, 0f) }, Frame);
            camera.UpdateShoulder(Open, Open, Frame);
        }

        [Test]
        public void TheSweepLooksAheadAlongTheTurnAndIsCapped()
        {
            var tuning = new CameraTuning();
            var camera = NewCamera(tuning);
            Turn(camera, 0f, Vector3.Zero);
            Assert.AreEqual(0f, camera.SweepYawDelta, 1e-4f, "holding still");
            Assert.IsFalse(camera.IsSweeping);
            Assert.AreEqual(0f, Vector3.Distance(camera.SweepBack(1f), -camera.Forward), 1e-4f, "no turn: straight back");

            for (int i = 0; i < 10; i++) Turn(camera, 0.3f, Vector3.Zero);   // a gentle turn
            float gentle = camera.SweepYawDelta;
            Assert.Greater(gentle, 1f);
            Assert.IsTrue(camera.IsSweeping);
            float angle = Directions.YawOf(-camera.SweepBack(1f)) - camera.Yaw;
            Assert.AreEqual(gentle, Angles.Wrap180(angle), 0.01f, "the last sample sits at the end of the predicted turn");

            for (int i = 0; i < 10; i++) Turn(camera, 1f, Vector3.Zero);     // full stick: 200°/s x 0.3 s = 60°
            Assert.LessOrEqual(Math.Abs(camera.SweepYawDelta), tuning.CollisionSweepMaxAngle + 1e-3f, "capped");
            Assert.Greater(camera.SweepYawDelta, gentle);
            for (int i = 0; i < 10; i++) Turn(camera, -1f, Vector3.Zero);
            Assert.Less(camera.SweepYawDelta, 0f, "turning the other way looks the other way");

            tuning.CollisionSweepTime = 0f;
            Assert.AreEqual(0f, camera.SweepYawDelta, "switched off");
        }

        [Test]
        public void MovingThePlayerAlsoSweepsAhead()
        {
            var camera = NewCamera(new CameraTuning());
            Vector3 feet = Vector3.Zero;
            for (int i = 0; i < 30; i++)
            {
                feet += new Vector3(6f * Frame, 0f, 0f);   // sprinting sideways
                Turn(camera, 0f, feet);
            }
            Assert.IsTrue(camera.IsSweeping);
            Assert.Greater(camera.SweepOrigin(1f).X - camera.ShoulderPoint.X, 0.5f, "the probe starts where the shoulder is heading");
            Assert.AreEqual(0f, Vector3.Distance(camera.SweepOrigin(0f), camera.ShoulderPoint), 1e-4f);
        }

        // A pillar sits 40° round from where the camera starts. Once the camera's yaw reaches it, the pillar's face is
        // 0.8 m behind the shoulder point and touches the camera. Turning at full stick sweeps the camera into it.
        static float LargestPullIn(bool sweep, out float end)
        {
            var tuning = new CameraTuning();
            if (!sweep) tuning.CollisionSweepTime = 0f;
            var camera = NewCamera(tuning);
            const float pillarYaw = 40f, face = 0.8f;
            float largest = 0f;
            for (int frame = 0; frame < 60; frame++)
            {
                Turn(camera, 1f, Vector3.Zero);
                float before = camera.Distance;
                bool blocked = camera.Yaw >= pillarYaw;
                float probe = blocked ? face : Open;
                float cameraFree = blocked ? (before > face ? 0f : before) : before;
                float swept = Open;
                if (camera.IsSweeping)
                {
                    for (int i = 1; i <= tuning.CollisionSweepSamples; i++)
                    {
                        float share = i / (float)tuning.CollisionSweepSamples;
                        if (camera.Yaw + camera.SweepYawDelta * share >= pillarYaw) swept = Math.Min(swept, face);
                    }
                }
                camera.UpdateDistance(probe, cameraFree, Open, swept, Frame);
                if (blocked) Assert.LessOrEqual(camera.Distance, face + 1e-4f, "never inside the pillar (frame " + frame + ")");
                largest = Math.Max(largest, before - camera.Distance);
            }
            end = camera.Distance;
            return largest;
        }

        [Test]
        public void ASwingIntoAPillarGlidesInInsteadOfJumping()
        {
            float without = LargestPullIn(false, out float endWithout);
            float with = LargestPullIn(true, out float endWith);
            Assert.Greater(without, 2f, "the old pop: the whole way in on one frame");
            Assert.Less(with, 1f, "with the sweep the camera is already most of the way in when the face arrives");
            Assert.AreEqual(endWithout, endWith, 1e-3f, "both end in front of the pillar");
        }

        [Test]
        public void TheSweepAloneNeverBringsTheCameraCloserThanItsFloor()
        {
            var tuning = new CameraTuning();
            var camera = NewCamera(tuning);
            for (int frame = 0; frame < 120; frame++)
            {
                Turn(camera, 0f, Vector3.Zero);
                camera.UpdateDistance(Open, camera.Distance, Open, 0.1f, Frame);   // a predicted wall right at the shoulder
            }
            Assert.AreEqual(tuning.CollisionSweepMinDistance, camera.Distance, 1e-3f, "only real contact goes closer");

            for (int frame = 0; frame < 120; frame++)
            {
                Turn(camera, 0f, Vector3.Zero);
                camera.UpdateDistance(Open, camera.Distance, Open, Open, Frame);
            }
            Assert.AreEqual(camera.DesiredDistance, camera.Distance, 1e-3f, "and it eases back out once the swing has passed");
        }
    }
}
