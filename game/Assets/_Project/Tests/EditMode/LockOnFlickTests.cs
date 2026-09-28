using System;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Right-stick flicks switch lock-on targets: one push is one switch, and the stick has to come back to
    // the centre before the next push counts.
    public class LockOnFlickTests
    {
        const float Frame = 1f / 60f;
        static readonly Vector2 Centre = Vector2.Zero;
        static readonly Vector2 Right = new Vector2(0.95f, 0f);
        static readonly Vector2 Left = new Vector2(-0.95f, 0f);

        // A detector that has seen the stick at rest, ready for a flick.
        static StickFlickDetector ArmedDetector()
        {
            var detector = new StickFlickDetector(new LockOnTuning());
            detector.Update(Centre, Frame);
            return detector;
        }

        static void Hold(StickFlickDetector detector, Vector2 stick, float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame) detector.Update(stick, Frame);
        }

        [Test]
        public void FlicksReportTheirDirection()
        {
            StickFlickDetector detector = ArmedDetector();
            Assert.AreEqual(1, detector.Update(Right, Frame));
            Hold(detector, Centre, 0.5f);
            Assert.AreEqual(-1, detector.Update(Left, Frame));
        }

        [Test]
        public void HoldingTheStickOverSwitchesOnlyOnce()
        {
            StickFlickDetector detector = ArmedDetector();
            int flicks = 0;
            for (int i = 0; i < 120; i++) flicks += Math.Abs(detector.Update(Right, Frame));
            Assert.AreEqual(1, flicks);
        }

        [Test]
        public void TheStickMustReturnToTheCentreBetweenFlicks()
        {
            StickFlickDetector detector = ArmedDetector();
            Assert.AreEqual(1, detector.Update(Right, Frame));
            Hold(detector, new Vector2(0.5f, 0f), 0.5f); // eased off, but not back to the centre
            Assert.AreEqual(0, detector.Update(Right, Frame));
            detector.Update(Centre, Frame);
            Assert.AreEqual(1, detector.Update(Right, Frame));
        }

        [Test]
        public void AQuickSecondFlickInsideTheCooldownIsSwallowed()
        {
            StickFlickDetector detector = ArmedDetector();
            Assert.AreEqual(1, detector.Update(Right, Frame));
            Assert.AreEqual(0, detector.Update(Centre, Frame));
            Assert.AreEqual(0, detector.Update(Right, Frame), "0.03 s later, inside the 0.3 s cooldown");
            int late = 0;
            for (int i = 0; i < 60; i++) late += Math.Abs(detector.Update(Right, Frame));
            Assert.AreEqual(0, late, "and it doesn't fire late while the stick is still held");
        }

        [Test]
        public void UpAndDownPushesNeverSwitch()
        {
            StickFlickDetector detector = ArmedDetector();
            Assert.AreEqual(0, detector.Update(new Vector2(0f, 1f), Frame));
            Assert.AreEqual(0, detector.Update(new Vector2(0.1f, -0.95f), Frame));
            Assert.AreEqual(0, detector.Update(new Vector2(0.76f, 0.8f), Frame), "more up than sideways");
            Assert.AreEqual(0, detector.Update(new Vector2(0.6f, 0f), Frame), "not far enough");
        }

        [Test]
        public void AStickAlreadyTiltedWhenLockingOnDoesNotSwitch()
        {
            var fresh = new StickFlickDetector(new LockOnTuning());
            Assert.AreEqual(0, fresh.Update(Right, Frame), "never seen the centre");

            StickFlickDetector detector = ArmedDetector();
            detector.Reset(); // what LockOnController does when a lock starts
            Assert.AreEqual(0, detector.Update(Right, Frame));
            detector.Update(Centre, Frame);
            Assert.AreEqual(1, detector.Update(Right, Frame));
        }

        [Test]
        public void BadInputIsIgnored()
        {
            StickFlickDetector detector = ArmedDetector();
            Assert.AreEqual(0, detector.Update(new Vector2(float.NaN, 0f), Frame));
            Assert.AreEqual(1, detector.Update(Right, float.NaN));
            Assert.IsNotNull(new StickFlickDetector(null).Tuning);
        }
    }
}
