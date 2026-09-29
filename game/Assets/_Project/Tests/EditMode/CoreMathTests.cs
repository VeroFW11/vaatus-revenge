using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Pins the core maths to Unity's conventions. If one of these fails, cameras and movement will
    // disagree with what you see in the editor (mirrored turning, cameras orbiting the wrong way).
    public class CoreMathTests
    {
        const float Tolerance = 1e-4f;

        static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(Tolerance), "x");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Tolerance), "y");
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(Tolerance), "z");
        }

        [Test]
        public void YawZeroFacesForwardAndNinetyFacesRight()
        {
            AssertVector(new Vector3(0, 0, 1), Directions.FromYaw(0));
            AssertVector(new Vector3(1, 0, 0), Directions.FromYaw(90));
            AssertVector(new Vector3(1, 0, 0), Directions.RightFromYaw(0));
            AssertVector(new Vector3(0, 0, -1), Directions.RightFromYaw(90));
        }

        [Test]
        public void PositivePitchLooksDown()
        {
            AssertVector(new Vector3(0, -1, 0), Directions.FromYawPitch(0, 90));
            Assert.Less(Directions.FromYawPitch(0, 30).Y, 0f);
            Assert.That(Directions.PitchOf(Directions.FromYawPitch(45, 30)), Is.EqualTo(30f).Within(1e-3f));
        }

        [Test]
        public void YawOfRoundTripsAndHandlesStraightUp()
        {
            Assert.That(Directions.YawOf(Directions.FromYaw(-135)), Is.EqualTo(-135f).Within(1e-3f));
            Assert.AreEqual(42f, Directions.YawOf(new Vector3(0, 1, 0), 42f));
        }

        [Test]
        public void CameraRelativeMovementFollowsCameraYaw()
        {
            AssertVector(new Vector3(1, 0, 0), Directions.CameraRelative(new Vector2(0, 1), 90));
            AssertVector(new Vector3(0, 0, -1), Directions.CameraRelative(new Vector2(1, 0), 90));
            Assert.That(Directions.CameraRelative(new Vector2(1, 1), 0).Length(), Is.EqualTo(1f).Within(Tolerance));
            Assert.That(Directions.CameraRelative(new Vector2(0, 0.5f), 10).Length(), Is.EqualTo(0.5f).Within(Tolerance));
        }

        [Test]
        public void AnglesTakeTheShortWayRound()
        {
            Assert.That(Angles.Delta(179, -179), Is.EqualTo(2f).Within(Tolerance));
            Assert.That(Angles.Delta(-179, 179), Is.EqualTo(-2f).Within(Tolerance));
            Assert.That(Angles.MoveTowards(170, -170, 5), Is.EqualTo(175f).Within(Tolerance));
            Assert.That(Angles.Wrap180(540), Is.EqualTo(180f).Within(Tolerance));
        }

        [Test]
        public void SmoothDampReachesTargetWithoutOvershoot()
        {
            float value = 0f, velocity = 0f;
            for (int i = 0; i < 120; i++)
            {
                value = Smooth.Damp(value, 10f, ref velocity, 0.2f, 1f / 60f);
                Assert.LessOrEqual(value, 10f + Tolerance);
            }
            Assert.That(value, Is.EqualTo(10f).Within(0.01f));
        }

        [Test]
        public void DampAngleCrossesTheWrapTheShortWay()
        {
            float yaw = 170f, velocity = 0f;
            for (int i = 0; i < 120; i++) yaw = Smooth.DampAngle(yaw, -170f, ref velocity, 0.1f, 1f / 60f);
            Assert.That(Angles.Delta(yaw, -170f), Is.EqualTo(0f).Within(0.05f));
        }

        [Test]
        public void ArcHitsTargetsInFrontOnly()
        {
            var origin = new Vector3(0, 1, 0);
            var forward = new Vector3(0, 0, 1);
            Assert.IsTrue(HitGeometry.InArc(origin, forward, 2f, 90f, 1f, new Vector3(0, 0, 2.2f), 1.8f, 0.4f));
            Assert.IsFalse(HitGeometry.InArc(origin, forward, 2f, 90f, 1f, new Vector3(0, 0, -1.5f), 1.8f, 0.4f));
            Assert.IsFalse(HitGeometry.InArc(origin, forward, 2f, 90f, 1f, new Vector3(0, 0, 3f), 1.8f, 0.4f));
            Assert.IsTrue(HitGeometry.InArc(origin, forward, 2f, 360f, 1f, new Vector3(0, 0, -1.5f), 1.8f, 0.4f));
            // A target on a ledge 4 m up is out of reach vertically.
            Assert.IsFalse(HitGeometry.InArc(origin, forward, 2f, 90f, 1f, new Vector3(0, 4, 1.5f), 1.8f, 0.4f));
        }

        [Test]
        public void FastProjectileDoesNotTunnelThroughTarget()
        {
            // 60 m in one step, straight through a 0.4 m radius fighter standing at z = 30.
            float t = HitGeometry.SweepSphereVsCapsule(new Vector3(0, 1, 0), new Vector3(0, 1, 60), 0.1f,
                new Vector3(0, 0, 30), 1.8f, 0.4f);
            Assert.GreaterOrEqual(t, 0f);
            Assert.That(t * 60f, Is.EqualTo(29.5f).Within(0.3f));
            Assert.AreEqual(-1f, HitGeometry.SweepSphereVsCapsule(new Vector3(3, 1, 0), new Vector3(3, 1, 60), 0.1f,
                new Vector3(0, 0, 30), 1.8f, 0.4f));
        }
    }
}
