using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // The procedural martial-arts animator: every animation key has a real pose, strikes reach full extension on
    // the move's first active frame (so what you see lines up with when the hit can land), limbs never stretch,
    // blends never pop, and bad frame times never produce a broken pose.
    public class AnimationTests
    {
        const float Dt = 1f / 60f;

        static List<string> AllKeys()
        {
            return typeof(AnimationKeys).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()).ToList();
        }

        static IEnumerable<MoveData> PlayerMoves(ElementMoveSet m)
        {
            foreach (MoveData x in m.LightChain) yield return x;
            foreach (MoveData x in m.AirChain) yield return x;
            yield return m.Launcher;
            yield return m.AbilityNorth;
            yield return m.AbilityEast;
            yield return m.Heavy;
            yield return m.SprintAttack;
            yield return m.Skill;
            yield return m.ZipStrike;
        }

        static Dictionary<string, MoveData> MovesByKey()
        {
            var moves = new Dictionary<string, MoveData>();
            foreach (MoveData m in PlayerMoves(ElementMoveSet.CreateFireFluid())) moves[m.AnimationKey] = m;
            foreach (EnemyTuning t in new[] { EnemyTuning.CreateDaoSoldier(), EnemyTuning.CreateCrossbowman(), EnemyTuning.CreateSparringDummy() })
            {
                foreach (EnemyAttackData a in t.Attacks) if (a.Move != null && !string.IsNullOrEmpty(a.Move.AnimationKey)) moves[a.Move.AnimationKey] = a.Move;
                if (t.BreakOut != null && t.BreakOut.Attack != null) moves[t.BreakOut.Attack.Move.AnimationKey] = t.BreakOut.Attack.Move;
            }
            return moves;
        }

        static FighterAnimInput ActionInput(string key, float time, MoveData move, int serial = 1)
        {
            var input = new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = key, ActionTime = time, ActionSerial = serial };
            if (move != null)
            {
                input.HasFrameData = true;
                input.Timing = ClipTiming.FromMove(move);
            }
            return input;
        }

        // How straight a limb is: 1 = fully extended.
        static float Extension(ForwardKinematics fk, HumanoidSkeleton sk, Limb limb)
        {
            float Arm(BodySide s) => Vector3.Distance(fk[BodyJoints.UpperArm(s)], fk[BodyJoints.Hand(s)]) / sk.ArmLength;
            float Leg(BodySide s) => Vector3.Distance(fk[BodyJoints.UpperLeg(s)], fk[BodyJoints.Foot(s)]) / sk.LegLength;
            switch (limb)
            {
                case Limb.LeftFist: return Arm(BodySide.Left);
                case Limb.RightFoot: return Leg(BodySide.Right);
                case Limb.LeftFoot: return Leg(BodySide.Left);
                case Limb.BothFists: return Math.Min(Arm(BodySide.Left), Arm(BodySide.Right));
                default: return Arm(BodySide.Right);
            }
        }

        [Test]
        public void EveryAnimationKeyHasARealPose()
        {
            Dictionary<string, MoveData> moves = MovesByKey();
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
            var rest = new BodyPose();
            foreach (string key in AllKeys())
            {
                var animator = new FighterAnimator(PoseLibrary.Default, skeleton);
                Assert.That(animator.CanPlay(key), Is.True, "no pose for animation key '" + key + "'");
                moves.TryGetValue(key, out MoveData move);
                float length = move != null ? move.TotalDuration : 1f;
                float maxDifference = 0f;
                for (float t = 0f; t <= length; t += Dt)
                {
                    animator.Update(ActionInput(key, t, move));
                    Assert.That(animator.Pose.IsFinite(), Is.True, key + " at " + t);
                    float difference = 0f;
                    for (int j = 1; j < BodyJoints.Count; j++) difference += 1f - Math.Abs(Quaternion.Dot(animator.Pose.Local[j], rest.Local[j]));
                    maxDifference = Math.Max(maxDifference, difference);
                }
                Assert.That(animator.ActionCue.Key, Is.EqualTo(key), "the action cue names the key being shown");
                Assert.That(maxDifference, Is.GreaterThan(0.05f), key + " never leaves the T-pose");
            }
        }

        [Test]
        public void StrikesReachFullExtensionOnTheirFirstActiveFrame()
        {
            ElementMoveSet set = ElementMoveSet.CreateFireFluid();
            foreach (MoveData move in PlayerMoves(set))
            {
                HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
                var animator = new FighterAnimator(PoseLibrary.Default, skeleton);
                var fk = new ForwardKinematics(skeleton);
                var samples = new List<(float t, float ext)>();
                for (int frame = 0; frame * Dt <= move.ActiveEnd + 2 * Dt; frame++)
                {
                    float t = frame * Dt;
                    animator.Update(ActionInput(move.AnimationKey, t, move));
                    fk.Compute(animator.Pose, Vector3.Zero, 0f);
                    samples.Add((t, Extension(fk, skeleton, move.Limb)));
                }
                float max = samples.Max(x => x.ext);
                float firstFull = samples.First(x => x.ext >= max - 0.002f).t;
                (float t, float ext) atActive = samples.First(x => x.t >= move.ActiveStart - 1e-4f);
                Assert.That(max, Is.GreaterThan(0.97f), move.DisplayName + ": the striking limb never straightens");
                Assert.That(atActive.ext, Is.GreaterThanOrEqualTo(max - 0.002f), move.DisplayName + ": not fully extended on the first active frame");
                Assert.That(Math.Abs(firstFull - move.ActiveStart), Is.LessThanOrEqualTo(Dt + 1e-4f),
                    move.DisplayName + ": full extension at " + firstFull + " s, the strike goes active at " + move.ActiveStart + " s");
            }
        }

        [Test]
        public void EveryClipGivesFinitePosesAtAnyTime()
        {
            var solver = new PoseSolver(HumanoidSkeleton.Create());
            var spec = new PoseSpec();
            var pose = new BodyPose();
            var timing = new ClipTiming { Startup = 0.3f, Active = 0.15f, Recovery = 0.4f, HitCount = 3, HitInterval = 0.3f };
            foreach (PoseClip clip in PoseLibrary.Default.Clips)
            {
                foreach (float t in new[] { -1f, 0f, 0.01f, 0.1f, 0.3f, 0.45f, 0.7f, 1f, 2f, 50f, float.NaN })
                {
                    clip.Evaluate(t, in timing, spec);
                    Assert.That(spec.IsFinite(), Is.True, clip.Key + " dials at " + t);
                    solver.Solve(spec, pose);
                    Assert.That(pose.IsFinite(), Is.True, clip.Key + " pose at " + t);
                }
            }
        }

        [Test]
        public void ForwardKinematicsKeepsEveryBoneItsLength()
        {
            foreach (float scale in new[] { 1f, 1.04f, 0.9f })
            {
                HumanoidSkeleton skeleton = HumanoidSkeleton.Create(null, scale);
                var solver = new PoseSolver(skeleton);
                var fk = new ForwardKinematics(skeleton);
                var spec = new PoseSpec();
                var pose = new BodyPose();
                var timing = new ClipTiming { Startup = 0.2f, Active = 0.1f, Recovery = 0.3f, HitCount = 1 };
                foreach (PoseClip clip in PoseLibrary.Default.Clips)
                {
                    for (float t = 0f; t < 0.7f; t += 0.07f)
                    {
                        clip.Evaluate(t, in timing, spec);
                        solver.Solve(spec, pose);
                        fk.Compute(pose, new Vector3(3f, 0f, -2f), 37f);
                        for (int j = 1; j < BodyJoints.Count; j++)
                        {
                            float length = Vector3.Distance(fk.Positions[j], fk.Positions[BodyJoints.Parent(j)]);
                            Assert.That(length, Is.EqualTo(skeleton.RestOffset(j).Length()).Within(1e-3f),
                                clip.Key + " " + BodyJoints.Name((BodyJoint)j) + " at " + t);
                        }
                    }
                }
            }
        }

        [Test]
        public void KneesBendForwardAndFeetStayOnTheGroundInStance()
        {
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
            var animator = new FighterAnimator(PoseLibrary.Default, skeleton);
            var fk = new ForwardKinematics(skeleton);
            for (int i = 0; i < 90; i++) animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "" });
            fk.Compute(animator.Pose, Vector3.Zero, 0f);
            foreach (BodySide side in new[] { BodySide.Left, BodySide.Right })
            {
                Vector3 hip = fk[BodyJoints.UpperLeg(side)], knee = fk[BodyJoints.LowerLeg(side)], ankle = fk[BodyJoints.Foot(side)];
                Vector3 bend = knee - (hip + ankle) * 0.5f;
                Vector3 toes = Vector3.Normalize(Directions.Flatten(fk[BodyJoints.Toes(side)] - ankle));
                Assert.That(Vector3.Dot(bend, toes), Is.GreaterThan(0f), side + " knee bends backwards");
                Assert.That(ankle.Y, Is.EqualTo(skeleton.AnkleHeight).Within(0.02f), side + " foot is not on the ground");
                Vector3 shoulder = fk[BodyJoints.UpperArm(side)], elbow = fk[BodyJoints.LowerArm(side)], hand = fk[BodyJoints.Hand(side)];
                Assert.That(elbow.Y, Is.LessThan((shoulder.Y + hand.Y) * 0.5f + 0.02f), side + " elbow points up in guard");
            }
        }

        [Test]
        public void ChainBlendsNeverPop()
        {
            ElementMoveSet set = ElementMoveSet.CreateFireFluid();
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
            var animator = new FighterAnimator(PoseLibrary.Default, skeleton);
            var fk = new ForwardKinematics(skeleton);
            var frames = new List<Vector3[]>();
            var labels = new List<string>();

            void Step(FighterAnimInput input, string label)
            {
                animator.Update(input);
                fk.Compute(animator.Pose, Vector3.Zero, 0f);
                frames.Add((Vector3[])fk.Positions.Clone());
                labels.Add(label);
            }

            for (int i = 0; i < 20; i++) Step(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "" }, "idle");
            int serial = 0;
            // The five-hit chain, each move cut into by the next at its chain-cancel point, then back to idle.
            foreach (MoveData move in set.LightChain)
            {
                serial++;
                for (float t = 0f; t < move.ChainCancelAt; t += Dt) Step(ActionInput(move.AnimationKey, t, move, serial), move.DisplayName);
            }
            for (int i = 0; i < 30; i++) Step(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "" }, "back to idle");
            // Hurt interrupting a strike, then a dodge, then a run.
            Step(ActionInput(AnimationKeys.Cross, 0.1f, set.LightChain[1], ++serial), "cross");
            for (int i = 0; i < 12; i++) Step(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = AnimationKeys.Hurt, ActionTime = i * Dt, ActionSerial = serial + 1 }, "hurt");
            for (int i = 0; i < 12; i++) Step(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = AnimationKeys.Dodge, ActionTime = i * Dt, ActionSerial = serial + 2 }, "dodge");
            for (int i = 0; i < 40; i++) Step(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "", LocalVelocity = new Vector3(0f, 0f, 4.8f) }, "run");

            // A pop is a frame where a joint jumps much further than on the frames either side of it (fast, smooth
            // motion like a snapping kick speeds up and slows down over several frames). Anything over 0.8 m in one
            // frame is a pop whatever its neighbours do.
            for (int i = 1; i < frames.Count - 1; i++)
            {
                for (int j = 0; j < BodyJoints.Count; j++)
                {
                    float before = Vector3.Distance(frames[i - 1][j], frames[i][j]);
                    float after = Vector3.Distance(frames[i][j], frames[i + 1][j]);
                    float previous = i >= 2 ? Vector3.Distance(frames[i - 2][j], frames[i - 1][j]) : 0f;
                    string where = labels[i] + " frame " + i + " " + BodyJoints.Name((BodyJoint)j);
                    Assert.That(before, Is.LessThan(0.8f), "huge jump at " + where);
                    Assert.That(before, Is.LessThanOrEqualTo(Math.Max(0.12f, 2.5f * Math.Max(previous, after))),
                        "pop at " + where + ": " + before + " m after " + previous + " m, followed by " + after + " m");
                }
            }
        }

        [Test]
        public void BadFrameTimesNeverBreakThePose()
        {
            var animator = new FighterAnimator(PoseLibrary.Default, HumanoidSkeleton.Create());
            MoveData jab = ElementMoveSet.CreateFireFluid().LightChain[0];
            foreach (float dt in new[] { 0f, 1000f, float.NaN, -1f, float.PositiveInfinity, 1e-7f, Dt })
            {
                var input = ActionInput(AnimationKeys.Jab, 0.05f, jab);
                input.DeltaTime = dt;
                input.LocalVelocity = new Vector3(0f, -3f, 5f);
                input.YawDelta = 90f;
                input.HitReaction = true;
                input.HitDirectionLocal = new Vector3(0f, 0f, -1f);
                input.HitStrength = 1f;
                animator.Update(input);
                Assert.That(animator.Pose.IsFinite(), Is.True, "dt " + dt);
                animator.Update(new FighterAnimInput { DeltaTime = dt, Grounded = dt > 1f, ActionKey = "", LocalVelocity = new Vector3(float.NaN, 0f, 0f) });
                Assert.That(animator.Pose.IsFinite(), Is.True, "locomotion, dt " + dt);
            }
        }

        [Test]
        public void HitstopFreezesThePose()
        {
            var animator = new FighterAnimator(PoseLibrary.Default, HumanoidSkeleton.Create());
            MoveData cross = ElementMoveSet.CreateFireFluid().LightChain[1];
            for (int i = 0; i < 8; i++) animator.Update(ActionInput(AnimationKeys.Cross, i * Dt, cross));
            var before = new BodyPose();
            before.CopyFrom(animator.Pose);
            var frozen = ActionInput(AnimationKeys.Cross, 7 * Dt, cross);
            frozen.DeltaTime = 0f;
            animator.Update(frozen);
            for (int j = 0; j < BodyJoints.Count; j++)
                Assert.That(Math.Abs(Quaternion.Dot(before.Local[j], animator.Pose.Local[j])), Is.GreaterThan(0.99999f), BodyJoints.Name((BodyJoint)j));
        }

        [Test]
        public void EulerAndLookRotationFollowUnityConventions()
        {
            Vector3 forward = AnimMath.Rotate(AnimMath.Euler(0f, 90f, 0f), Vector3.UnitZ);
            Assert.That(forward.X, Is.EqualTo(1f).Within(1e-4f), "yaw +90 faces +X");
            Vector3 down = AnimMath.Rotate(AnimMath.Euler(90f, 0f, 0f), Vector3.UnitZ);
            Assert.That(down.Y, Is.EqualTo(-1f).Within(1e-4f), "pitch +90 looks down");
            Quaternion look = AnimMath.LookRotation(new Vector3(1f, 0f, 1f), Vector3.UnitY);
            Vector3 z = AnimMath.Rotate(look, Vector3.UnitZ);
            Assert.That(z.X, Is.EqualTo(z.Z).Within(1e-4f));
            Assert.That(AnimMath.Rotate(look, Vector3.UnitY).Y, Is.EqualTo(1f).Within(1e-4f));
        }
    }
}
