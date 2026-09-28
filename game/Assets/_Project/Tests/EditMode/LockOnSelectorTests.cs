using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Which enemy lock-on picks, which one a switch moves to, and when a lock breaks.
    // Scene used by most tests: player at the origin, camera 4 m behind and above, looking along +Z.
    public class LockOnSelectorTests
    {
        const float Frame = 1f / 60f;
        static readonly Vector3 PlayerEye = new Vector3(0f, 1.55f, 0f);
        static readonly Vector3 CameraPosition = new Vector3(0f, 2.5f, -4f);
        static readonly Vector3 CameraForward = new Vector3(0f, 0f, 1f);

        static LockOnCandidate Enemy(int id, float x, float z, bool alive = true, bool visible = true, float y = 1.3f)
        {
            return new LockOnCandidate { Id = id, Point = new Vector3(x, y, z), IsAlive = alive, HasLineOfSight = visible };
        }

        static LockOnSelector NewSelector()
        {
            return new LockOnSelector(new LockOnTuning());
        }

        [Test]
        public void PicksTheEnemyNearestTheScreenCentre()
        {
            var candidates = new List<LockOnCandidate> { Enemy(1, -4f, 8f), Enemy(2, 0.5f, 12f), Enemy(3, 6f, 5f) };
            Assert.AreEqual(1, NewSelector().PickInitial(candidates, PlayerEye, CameraPosition, CameraForward));
        }

        [Test]
        public void SkipsEnemiesThatAreDeadHiddenOrOutOfRange()
        {
            var candidates = new List<LockOnCandidate>
            {
                Enemy(1, 0f, 10f, alive: false),
                Enemy(2, 0.2f, 10f, visible: false),
                Enemy(3, 0f, 25f),  // beyond the 22 m acquire range
                Enemy(4, -5f, 9f),  // valid, just off-centre
            };
            LockOnSelector selector = NewSelector();
            Assert.AreEqual(3, selector.PickInitial(candidates, PlayerEye, CameraPosition, CameraForward));
            Assert.AreEqual(-1, selector.PickInitial(candidates.GetRange(0, 3), PlayerEye, CameraPosition, CameraForward));
            Assert.AreEqual(-1, selector.PickInitial(new List<LockOnCandidate>(), PlayerEye, CameraPosition, CameraForward));
            Assert.AreEqual(-1, selector.PickInitial(null, PlayerEye, CameraPosition, CameraForward));
        }

        [Test]
        public void IgnoresEnemiesBehindTheCameraOrFarOffScreen()
        {
            var candidates = new List<LockOnCandidate> { Enemy(1, 0f, -8f), Enemy(2, 10f, -4f) };
            Assert.AreEqual(-1, NewSelector().PickInitial(candidates, PlayerEye, CameraPosition, CameraForward));
        }

        [Test]
        public void DistanceBreaksTiesBetweenEnemiesInLine()
        {
            // Camera level with both targets and looking straight through them: same angle, so nearer wins.
            var levelCamera = new Vector3(0f, 1.3f, -4f);
            var candidates = new List<LockOnCandidate> { Enemy(1, 0f, 15f), Enemy(2, 0f, 5f) };
            Assert.AreEqual(1, NewSelector().PickInitial(candidates, PlayerEye, levelCamera, CameraForward));
        }

        [Test]
        public void SwitchingPicksTheNearestEnemyOnThatSide()
        {
            var candidates = new List<LockOnCandidate>
            {
                Enemy(1, 0f, 10f),   // current target, dead ahead
                Enemy(2, -4f, 10f),
                Enemy(3, 2f, 10f),
                Enemy(4, 6f, 10f),
                Enemy(5, -9f, 10f),
            };
            LockOnSelector selector = NewSelector();
            Vector3 current = candidates[0].Point;
            Assert.AreEqual(2, selector.PickNext(candidates, 1, current, +1, PlayerEye, CameraPosition, 0f), "right: the nearer one on the right");
            Assert.AreEqual(1, selector.PickNext(candidates, 1, current, -1, PlayerEye, CameraPosition, 0f), "left: the nearer one on the left");

            Vector3 farRight = candidates[3].Point;
            Assert.AreEqual(-1, selector.PickNext(candidates, 4, farRight, +1, PlayerEye, CameraPosition, 0f), "nothing further right: keep the target");
            Assert.AreEqual(2, selector.PickNext(candidates, 4, farRight, -1, PlayerEye, CameraPosition, 0f));
            Assert.AreEqual(-1, selector.PickNext(candidates, 1, current, 0, PlayerEye, CameraPosition, 0f), "no direction, no switch");
        }

        [Test]
        public void SwitchingFollowsTheCameraNotTheWorld()
        {
            // Camera at the origin facing +X (yaw 90): the right of the screen is towards -Z.
            var levelCamera = new Vector3(0f, 1.3f, 0f);
            var eye = new Vector3(2f, 1.55f, 0f);
            var candidates = new List<LockOnCandidate> { Enemy(1, 10f, 0f), Enemy(2, 10f, -3f), Enemy(3, 10f, 3f) };
            LockOnSelector selector = NewSelector();
            Assert.AreEqual(1, selector.PickNext(candidates, 1, candidates[0].Point, +1, eye, levelCamera, 90f));
            Assert.AreEqual(2, selector.PickNext(candidates, 1, candidates[0].Point, -1, eye, levelCamera, 90f));
        }

        [Test]
        public void SwitchingSkipsDeadHiddenFarAndBehindEnemies()
        {
            var candidates = new List<LockOnCandidate>
            {
                Enemy(1, 0f, 10f),                 // current target
                Enemy(2, 2f, 10f, alive: false),
                Enemy(3, 3f, 10f, visible: false),
                Enemy(4, 4f, 30f),                 // out of range
                Enemy(5, 4f, -8f),                 // behind the camera
                Enemy(6, 8f, 10f),
            };
            Assert.AreEqual(5, NewSelector().PickNext(candidates, 1, candidates[0].Point, +1, PlayerEye, CameraPosition, 0f));
        }

        [Test]
        public void SwitchingStillWorksFromATargetThatJustDied()
        {
            var candidates = new List<LockOnCandidate> { Enemy(1, 0f, 10f, alive: false), Enemy(2, 3f, 10f) };
            Assert.AreEqual(1, NewSelector().PickNext(candidates, 1, candidates[0].Point, +1, PlayerEye, CameraPosition, 0f));
        }

        [Test]
        public void LockBreaksAfterTheLineOfSightGraceTime()
        {
            LockOnSelector selector = NewSelector();
            for (float t = 0f; t < 1.1f; t += Frame)
            {
                Assert.AreEqual(LockOnBreakReason.None, selector.UpdateBreak(true, false, 10f, Frame), "hidden for " + t + " s");
            }

            // Seeing it again resets the clock.
            Assert.AreEqual(LockOnBreakReason.None, selector.UpdateBreak(true, true, 10f, Frame));
            Assert.AreEqual(0f, selector.TimeWithoutSight);

            LockOnBreakReason reason = LockOnBreakReason.None;
            float hidden = 0f;
            while (reason == LockOnBreakReason.None && hidden < 5f)
            {
                reason = selector.UpdateBreak(true, false, 10f, Frame);
                hidden += Frame;
            }
            Assert.AreEqual(LockOnBreakReason.LostSight, reason);
            Assert.That(hidden, Is.EqualTo(1.2f).Within(0.02f));
        }

        [Test]
        public void GracePeriodPausesWhenGameTimeStops()
        {
            LockOnSelector selector = NewSelector();
            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(LockOnBreakReason.None, selector.UpdateBreak(true, false, 10f, 0f));
            }
            Assert.AreEqual(0f, selector.TimeWithoutSight);
        }

        [Test]
        public void LockBreaksWhenTooFarOrDead()
        {
            LockOnSelector selector = NewSelector();
            Assert.AreEqual(LockOnBreakReason.None, selector.UpdateBreak(true, true, 29.9f, Frame));
            Assert.AreEqual(LockOnBreakReason.TooFar, selector.UpdateBreak(true, true, 30.1f, Frame));
            Assert.AreEqual(LockOnBreakReason.TooFar, selector.UpdateBreak(true, true, float.NaN, Frame));
            Assert.AreEqual(LockOnBreakReason.TargetDead, selector.UpdateBreak(false, true, 5f, Frame));
        }

        [Test]
        public void DegenerateGeometryIsSafe()
        {
            var candidates = new List<LockOnCandidate>
            {
                Enemy(1, 0f, -4f, y: 2.5f), // exactly where the camera is
                new LockOnCandidate { Id = 2, Point = new Vector3(float.NaN, 0f, 0f), IsAlive = true, HasLineOfSight = true },
            };
            LockOnSelector selector = NewSelector();
            // A zero camera forward falls back to +Z; the candidate on the camera counts as dead centre.
            Assert.AreEqual(0, selector.PickInitial(candidates, PlayerEye, CameraPosition, Vector3.Zero));
            // Neither has a side of the screen, so there's nothing to switch to.
            Assert.AreEqual(-1, selector.PickNext(candidates, 99, new Vector3(float.NaN, 0f, 0f), +1, PlayerEye, CameraPosition, 0f));
            Assert.AreEqual(-1, selector.PickNext(candidates, 99, Vector3.Zero, +1, PlayerEye, CameraPosition, float.NaN));
        }

        [Test]
        public void MissingTuningFallsBackToDefaults()
        {
            var selector = new LockOnSelector(null);
            Assert.IsNotNull(selector.Tuning);
            Assert.AreEqual(LockOnBreakReason.None, selector.UpdateBreak(true, true, 10f, Frame));
        }
    }
}
