using System;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // The camera near pillars and wall ends (Playtest Report 02, NEW-03): it must never sit inside level geometry,
    // never end up inside the player's head, and never jump most of the way to the player in one frame just because
    // a pillar crossed the view. The probes the Unity rig would do are faked here with plain numbers.
    public class CameraCollisionTests
    {
        const float Frame = 1f / 60f;
        const float Open = float.PositiveInfinity;

        static OrbitCameraModel NewCamera(CameraTuning tuning = null)
        {
            var camera = new OrbitCameraModel(tuning ?? new CameraTuning());
            camera.Snap(Vector3.Zero, 0f, 12f);
            return camera;
        }

        // Steps 1 and 2 of a frame (pivot and orientation): nothing to the sides or above.
        static void Orient(OrbitCameraModel camera)
        {
            camera.UpdatePivot(Vector3.Zero, Frame);
            camera.UpdateOrientation(new OrbitCameraInput(), Frame);
            camera.UpdateShoulder(Open, Open, Frame);
        }

        // Total downward look of the camera, from its Forward vector.
        static float LookDownDegrees(OrbitCameraModel camera)
        {
            return Directions.PitchOf(camera.Forward);
        }

        [Test]
        public void APillarThatOnlyBlocksTheViewWaitsThenGlidesAndOnlySkipsItsOwnThickness()
        {
            CameraTuning tuning = new CameraTuning();
            var camera = NewCamera(tuning);
            float start = camera.Distance;
            Assert.Greater(start, 3f);
            // A pillar between the camera and the player: its near face 1.0 m behind the shoulder point, its far face 1.8 m.
            const float nearFace = 1.0f, farFace = 1.8f;
            int held = 0;
            float largestStep = 0f;
            for (int frame = 0; frame < 90; frame++)
            {
                Orient(camera);
                float before = camera.Distance;
                // The camera itself touches the pillar only once it's inside its far face.
                float cameraFree = before > farFace ? before - farFace : 0f;
                camera.UpdateDistance(nearFace, cameraFree, Open, Frame);
                if (camera.Distance == start) held++;
                largestStep = Math.Max(largestStep, before - camera.Distance);
                Assert.IsFalse(camera.Distance > nearFace + 1e-4f && camera.Distance < farFace - 1e-4f,
                    "never inside the pillar (frame " + frame + ", " + camera.Distance + " m)");
            }
            Assert.GreaterOrEqual(held, (int)Math.Floor(tuning.OcclusionGraceTime / Frame), "waited out the grace time first");
            Assert.AreEqual(nearFace, camera.Distance, 1e-4f, "ends up in front of the pillar");
            Assert.LessOrEqual(largestStep, farFace - nearFace + 0.25f, "only the pillar's thickness is skipped in one frame, not the "
                                                                          + (start - nearFace) + " m to the player");
        }

        [Test]
        public void AWallTouchingTheCameraStillMovesItInOnTheSameFrame()
        {
            var camera = NewCamera();
            Orient(camera);
            camera.UpdateDistance(1.2f, 0f, Open, Frame);
            Assert.AreEqual(1.2f, camera.Distance, 1e-4f, "never inside geometry, not even for a frame");
        }

        [Test]
        public void AnOccluderThatClearsDuringTheGraceTimeNeverMovesTheCamera()
        {
            CameraTuning tuning = new CameraTuning();
            var camera = NewCamera(tuning);
            float start = camera.Distance;
            int frames = Math.Max(1, (int)Math.Floor(tuning.OcclusionGraceTime / Frame) - 1);
            for (int frame = 0; frame < frames; frame++)
            {
                Orient(camera);
                camera.UpdateDistance(1.0f, camera.Distance - 2f, Open, Frame);   // a pillar edge sweeping past the view
            }
            Orient(camera);
            camera.UpdateDistance(Open, Open, Open, Frame);
            Assert.AreEqual(start, camera.Distance, 1e-4f);
            Assert.IsFalse(camera.IsWaitingOutOcclusion);
        }

        [Test]
        public void WithAWallCloseBehindTheCameraRisesOverTheHeadInsteadOfIntoIt()
        {
            CameraTuning tuning = new CameraTuning();
            var camera = NewCamera(tuning);
            const float room = 0.3f;                              // a pillar 0.3 m behind the shoulder point (plus the camera's radius)
            float closestToPivot = float.MaxValue;
            for (int frame = 0; frame < 60; frame++)
            {
                Orient(camera);
                // A vertical wall: the room along the view grows as the camera looks further down.
                float cos = (float)Math.Cos(LookDownDegrees(camera) * Directions.Deg2Rad);
                float along = room / Math.Max(cos, 1e-3f);
                camera.UpdateDistance(along, 0f, Open, Frame);
                if (frame >= 30) closestToPivot = Math.Min(closestToPivot, Vector3.Distance(camera.CameraPosition, camera.Pivot));
                float behind = -Vector3.Dot(camera.CameraPosition - camera.ShoulderPoint, Directions.FromYaw(camera.Yaw));
                Assert.LessOrEqual(behind, room + 1e-3f, "never inside the wall");
            }
            Assert.Greater(camera.CollisionRise, 30f, "tilted down to rise over the player");
            Assert.GreaterOrEqual(camera.Distance, tuning.MinCollisionDistance - 0.05f, "kept its distance");
            Assert.Greater(camera.CameraPosition.Y - camera.Pivot.Y, 0.6f, "above the head, not in it");
            Assert.Greater(closestToPivot, 0.9f);
            Assert.AreEqual(12f, camera.Pitch, 1e-3f, "the player's own pitch is untouched: the rise is added on top");

            for (int frame = 0; frame < 90; frame++)
            {
                Orient(camera);
                camera.UpdateDistance(Open, Open, Open, Frame);
            }
            Assert.Less(camera.CollisionRise, 0.5f, "settles back in the open");
        }

        [Test]
        public void TheRiseMovesAFarCameraGently()
        {
            CameraTuning tuning = new CameraTuning();
            var camera = NewCamera(tuning);
            Vector3 previous = camera.CameraPosition;
            for (int frame = 0; frame < 20; frame++)
            {
                Orient(camera);
                // The view is blocked 0.2 m behind the player, but the camera, beyond the pillar, is still far out.
                camera.UpdateDistance(0.2f, camera.Distance - 0.8f, Open, Frame);
                float moved = Vector3.Distance(previous, camera.CameraPosition);
                previous = camera.CameraPosition;
                if (camera.Distance > 3f) Assert.LessOrEqual(moved, tuning.CollisionRiseMaxSpeed * Frame * 1.05f, "frame " + frame);
            }
        }

        [Test]
        public void TheShoulderOffsetAlsoHasToFitAtTheCameraEnd()
        {
            var camera = NewCamera();
            for (int frame = 0; frame < 30; frame++)
            {
                camera.UpdatePivot(Vector3.Zero, Frame);
                camera.UpdateOrientation(new OrbitCameraInput(), Frame);
                // Open beside the player; a wall 0.1 m to the right of where the camera trails behind.
                camera.UpdateShoulder(Open, Open, 0.1f, Open, Frame);
                camera.UpdateDistance(Open, Open, Open, Frame);
            }
            Assert.LessOrEqual(camera.ShoulderOffset, 0.1f + 1e-4f, "the camera end stays clear of the wall");
            Assert.IsFalse(camera.IsShoulderAutoSwapped, "no swap: the player's side is clear");

            for (int frame = 0; frame < 60; frame++)
            {
                camera.UpdatePivot(Vector3.Zero, Frame);
                camera.UpdateOrientation(new OrbitCameraInput(), Frame);
                camera.UpdateShoulder(Open, Open, Open, Open, Frame);
                camera.UpdateDistance(Open, Open, Open, Frame);
            }
            Assert.AreEqual(0.55f, camera.ShoulderOffset, 0.01f, "eases back out once the camera has passed the wall");
        }

        [Test]
        public void TheLookAheadGlidesInEarlyButNeverCloserThanTheMinimum()
        {
            CameraTuning tuning = new CameraTuning();
            var camera = NewCamera(tuning);
            float start = camera.Distance;
            float largestStep = 0f;
            for (int frame = 0; frame < 60; frame++)
            {
                Orient(camera);
                float before = camera.Distance;
                camera.UpdateDistance(Open, Open, 2.0f, Frame);   // nothing touching yet; the fat probe sees a pillar at 2 m
                largestStep = Math.Max(largestStep, before - camera.Distance);
            }
            Assert.AreEqual(2.0f, camera.Distance, 0.01f);
            Assert.Less(largestStep, (start - 2f) * 0.5f, "a glide, not a jump");

            for (int frame = 0; frame < 60; frame++)
            {
                Orient(camera);
                camera.UpdateDistance(Open, Open, 0.1f, Frame);   // grazing everything in a tight spot
            }
            Assert.AreEqual(tuning.MinCollisionDistance, camera.Distance, 0.01f, "only real contact brings it closer");
        }

        [Test]
        public void WhileWaitingItNeverSitsFurtherOutThanItWants()
        {
            CameraTuning tuning = new CameraTuning();
            var camera = NewCamera(tuning);
            tuning.FreeDistance = 2f;                             // the wanted distance shrinks while a pillar blocks the view
            for (int frame = 0; frame < 60; frame++)
            {
                Orient(camera);
                camera.UpdateDistance(0.8f, Math.Max(0f, camera.Distance - 1.4f), Open, Frame);
                Assert.LessOrEqual(camera.Distance, camera.DesiredDistance + 1e-4f, "frame " + frame);
                Assert.IsFalse(camera.Distance > 0.8f + 1e-4f && camera.Distance < 1.4f - 1e-4f, "never inside the pillar");
            }
        }

        [Test]
        public void TheOldTwoArgumentUpdateStillPullsInAtOnce()
        {
            var camera = NewCamera();
            Orient(camera);
            camera.UpdateDistance(1.5f, Frame);
            Assert.AreEqual(1.5f, camera.Distance, 1e-4f);
        }

        [Test]
        public void BadProbeValuesStayFinite()
        {
            var camera = NewCamera();
            float[] values = { float.NaN, float.NegativeInfinity, -2f, 0f, Open };
            foreach (float a in values)
                foreach (float b in values)
                {
                    Orient(camera);
                    camera.UpdateShoulder(a, b, b, a, Frame);
                    camera.UpdateDistance(a, b, a, Frame);
                    Vector3 p = camera.CameraPosition;
                    Assert.IsFalse(float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z) || float.IsInfinity(p.Y), a + " / " + b);
                    Assert.IsFalse(float.IsNaN(camera.CollisionRise));
                }
        }
    }
}
