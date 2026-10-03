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
    // blends never pop, and bad frame times never produce a broken pose. Since Build 05 these run over all four
    // elements' move sets (Fire, Water, Earth, Air), including the pause chains and dodge strikes.
    public class AnimationTests
    {
        const float Dt = 1f / 60f;

        static IEnumerable<ElementMoveSet> AllSets()
        {
            yield return ElementMoveSet.CreateFireFluid();
            yield return ElementMoveSet.CreateWaterFluid();
            yield return ElementMoveSet.CreateEarthFluid();
            yield return ElementMoveSet.CreateAirFluid();
        }

        static List<string> AllKeys()
        {
            return typeof(AnimationKeys).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()).ToList();
        }

        static IEnumerable<MoveData> PlayerMoves(ElementMoveSet m)
        {
            foreach (MoveData x in m.LightChain) yield return x;
            foreach (MoveData x in m.PauseChain) yield return x;
            yield return m.DodgeStrike;
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
            foreach (ElementMoveSet set in AllSets())
            {
                foreach (MoveData m in PlayerMoves(set)) moves[m.AnimationKey] = m;
            }
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
            foreach (ElementMoveSet set in AllSets())
            foreach (MoveData move in PlayerMoves(set))
            {
                HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
                var animator = new FighterAnimator(PoseLibrary.Default, skeleton, set.AnimationStyle);
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

        [TestCase(ElementId.Fire)]
        [TestCase(ElementId.Water)]
        [TestCase(ElementId.Earth)]
        [TestCase(ElementId.Air)]
        public void ChainBlendsNeverPop(ElementId element)
        {
            ElementMoveSet set = ElementLoadout.CreateFluid().Get(element);
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
            var animator = new FighterAnimator(PoseLibrary.Default, skeleton, set.AnimationStyle);
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
            // The pause branch (two hits, the pause, the pause chain played out) and a dodge strike out of a slip-in.
            foreach (MoveData move in new[] { set.LightChain[0], set.LightChain[1], set.PauseChain[0], set.PauseChain[1] })
            {
                serial++;
                float end = move == set.LightChain[1] ? move.TotalDuration : move.ChainCancelAt;
                for (float t = 0f; t < end; t += Dt) Step(ActionInput(move.AnimationKey, t, move, serial), move.DisplayName);
            }
            int slip = ++serial;
            for (int i = 0; i < 10; i++) Step(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = AnimationKeys.DodgeSlip, ActionTime = i * Dt, ActionSerial = slip }, "slip");
            for (float t = 0f; t < set.DodgeStrike.TotalDuration; t += Dt) Step(ActionInput(set.DodgeStrike.AnimationKey, t, set.DodgeStrike, ++serial), "dodge strike");
            for (int i = 0; i < 30; i++) Step(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "" }, "back to idle");
            // Hurt interrupting a strike, then a dodge, then a run.
            Step(ActionInput(set.LightChain[1].AnimationKey, 0.1f, set.LightChain[1], ++serial), "second hit");
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

        // V-05: blending out of a tumble must take the short way round: from -217 degrees (feet over the head) to
        // lying on the back at -90 it goes through -180, never up through 0 (standing upright) in mid-air.
        [Test]
        public void TurnsBlendTheShortWayRound()
        {
            PoseSpec flipped = PoseLibraryDefaults.Neutral();
            flipped[PoseChannel.PelvisPitch] = -217f;
            PoseSpec lying = PoseLibraryDefaults.Neutral();
            lying[PoseChannel.PelvisPitch] = -90f;
            var library = new PoseLibrary
            {
                Clips = new[]
                {
                    new PoseClip { Key = "tumble", Mode = ClipMode.Hold, Keys = new[] { new PoseKeyframe { Phase = KeyPhase.Seconds, Pose = flipped } } },
                    new PoseClip { Key = "lying", Mode = ClipMode.Hold, FadeIn = 0.2f, Keys = new[] { new PoseKeyframe { Phase = KeyPhase.Seconds, Pose = lying } } },
                }
            };
            var animator = new FighterAnimator(library, HumanoidSkeleton.Create());
            for (int i = 0; i < 20; i++) animator.Update(new FighterAnimInput { DeltaTime = Dt, ActionKey = "tumble", ActionTime = i * Dt, ActionSerial = 1 });
            for (int i = 0; i < 30; i++)
            {
                animator.Update(new FighterAnimInput { DeltaTime = Dt, ActionKey = "lying", ActionTime = i * Dt, ActionSerial = 2 });
                float pitch = animator.CurrentSpec[PoseChannel.PelvisPitch];
                float wrapped = ((pitch % 360f) + 540f) % 360f - 180f;
                Assert.That(Math.Abs(wrapped), Is.GreaterThanOrEqualTo(89.9f), "passed upright at frame " + i + " (" + pitch + ")");
            }
            Assert.That(animator.CurrentSpec[PoseChannel.PelvisPitch], Is.EqualTo(-90f).Within(0.5f));
        }

        // V-07: with a target, the striking limb points at it on the first active frame, even one well off to the side.
        [Test]
        public void StrikesAimAtTheirTarget()
        {
            ElementMoveSet set = ElementMoveSet.CreateFireFluid();
            foreach (MoveData move in new[] { set.LightChain[0], set.LightChain[3], set.Launcher, set.AirChain[1] })
            {
                HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
                var animator = new FighterAnimator(PoseLibrary.Default, skeleton);
                var fk = new ForwardKinematics(skeleton);
                var target = new Vector3(0.5f, 1.2f, 1.7f);   // chest height, off to the right
                for (int frame = 0; ; frame++)
                {
                    FighterAnimInput input = ActionInput(move.AnimationKey, frame * Dt, move);
                    input.HasTarget = true;
                    input.TargetLocal = target;
                    input.StrikeLimb = move.Limb;
                    animator.Update(input);
                    if (frame * Dt >= move.ActiveStart - 1e-4f) break;   // the first active frame
                }
                fk.Compute(animator.Pose, Vector3.Zero, 0f);
                bool foot = move.Limb == Limb.RightFoot || move.Limb == Limb.LeftFoot;
                Vector3 root = foot ? fk[BodyJoint.RightUpperLeg] : move.Limb == Limb.LeftFist ? fk[BodyJoint.LeftUpperArm] : fk[BodyJoint.RightUpperArm];
                Vector3 tip = foot ? fk[BodyJoint.RightToes] : move.Limb == Limb.LeftFist ? fk[BodyJoint.LeftHand] : fk[BodyJoint.RightHand];
                Vector3 limb = Vector3.Normalize(tip - root), to = Vector3.Normalize(target - root);
                float angle = MathF.Acos(Math.Clamp(Vector3.Dot(limb, to), -1f, 1f)) * AnimMath.Rad2Deg;
                Assert.That(angle, Is.LessThan(12f), move.DisplayName + " points " + angle + " degrees off its target");
            }
        }

        // V-12: a planted foot stays where it stands while the body lunges over it (straight, or turning toward a
        // target as it goes); it only moves by stepping (lifted).
        [TestCase(0f)]
        [TestCase(12f)]
        public void PlantedFeetDontSkateDuringALunge(float turnPerFrame)
        {
            MoveData jab = ElementMoveSet.CreateFireFluid().LightChain[0];
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
            var animator = new FighterAnimator(PoseLibrary.Default, skeleton);
            var fk = new ForwardKinematics(skeleton);
            for (int i = 0; i < 30; i++) animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "" });
            Vector3 root = Vector3.Zero;
            float yaw = 0f;
            var last = new Vector3[2];
            bool first = true;
            float worst = 0f;
            for (float t = 0f; t < jab.TotalDuration; t += Dt)
            {
                float speed = t < jab.ActiveEnd ? 4f : 0f;   // a 0.7 m lunge
                float turn = t < jab.ActiveEnd ? turnPerFrame : 0f;
                yaw += turn;
                FighterAnimInput input = ActionInput(AnimationKeys.Jab, t, jab);
                input.LocalVelocity = new Vector3(0f, 0f, speed);
                input.YawDelta = turn;
                animator.Update(input);
                root += AnimMath.Rotate(AnimMath.Yaw(yaw), new Vector3(0f, 0f, speed * Dt));
                fk.Compute(animator.Pose, root, yaw);
                for (int s = 0; s < 2; s++)
                {
                    Vector3 toes = fk[s == 0 ? BodyJoint.LeftToes : BodyJoint.RightToes];
                    Vector3 ankle = fk[s == 0 ? BodyJoint.LeftFoot : BodyJoint.RightFoot];
                    bool planted = toes.Y < 0.035f && ankle.Y < 0.11f;
                    if (!first && planted && last[s].Y < 0.035f)
                        worst = Math.Max(worst, Vector2.Distance(new Vector2(toes.X, toes.Z), new Vector2(last[s].X, last[s].Z)));
                    last[s] = toes;
                }
                first = false;
            }
            Assert.That(worst, Is.LessThan(0.02f), "a planted foot slid " + worst + " m in one frame");
        }

        // Build 05 verify J-07: a chained dodge (still Dodging) restarts its clip: the feed's ActionTime starts at the new
        // dodge's own clock and the serial changes, for every element.
        [Test]
        public void ChainedDodgeRestartsItsClip()
        {
            foreach (ElementId element in new[] { ElementId.Fire, ElementId.Water, ElementId.Earth, ElementId.Air })
            {
                PlayerDriver d = PlayerDriver.Elements();
                if (element != ElementId.Fire)
                {
                    d.Select = element;
                    d.Step();
                }
                d.Target(new Vector3(0f, 0f, 3f));
                var feed = new PlayerAnimationFeed();
                void Frame(Pad pad)
                {
                    int from = d.Log.Count;
                    d.Step(pad, new Vector2(1f, 0f));
                    for (int i = from; i < d.Log.Count; i++) feed.OnEvent(d.Log[i]);
                }
                Frame(Pad.Dodge);
                FighterAnimInput first = feed.Build(d.Model, Dt);
                DodgeProfile dodge = d.Model.MoveSet.Dodge;
                int guard = 0;
                while (d.Model.DodgeTime < dodge.NextDodgeAt && guard++ < 60)
                {
                    Frame(Pad.None);
                    feed.Build(d.Model, Dt);
                }
                Frame(Pad.Dodge);
                Assert.AreEqual(2, d.Count(PlayerEventType.DodgeStarted), element + ": chained");
                Assert.AreEqual(PlayerState.Dodging, d.Model.State);
                FighterAnimInput second = feed.Build(d.Model, Dt);
                Assert.AreNotEqual(first.ActionSerial, second.ActionSerial, element + ": a new clip");
                Assert.LessOrEqual(second.ActionTime, Dt + 1e-4f, element + ": from its start, not " + second.ActionTime + " s in");
            }
        }

        // Build 05 verify J-09 and J-08: after a fast dash hands back to standing still, the legs stop within ~0.1 s
        // (no running in place while a dash's speed decays) and the leap's lift settles over a few frames instead of
        // dropping the hips to the floor in one.
        [Test]
        public void DashHandsBackWithoutRunningInPlaceOrDropping()
        {
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
            var animator = new FighterAnimator(PoseLibrary.Default, skeleton);
            var fk = new ForwardKinematics(skeleton);
            for (int i = 0; i < 20; i++) animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "" });
            float hips = 0f;
            for (int i = 0; i < 16; i++)
            {
                animator.Update(new FighterAnimInput
                {
                    DeltaTime = Dt, Grounded = true, ActionKey = AnimationKeys.Dodge, ActionTime = i * Dt, ActionDuration = 0.28f,
                    ActionSerial = 7, LocalVelocity = new Vector3(0f, 0f, -12f)
                });
                fk.Compute(animator.Pose, Vector3.Zero, 0f);
                hips = fk[BodyJoint.Hips].Y;
            }
            int idleAfter = -1;
            float worstDrop = 0f;
            for (int i = 0; i < 30; i++)
            {
                animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "" });
                fk.Compute(animator.Pose, Vector3.Zero, 0f);
                worstDrop = Math.Max(worstDrop, hips - fk[BodyJoint.Hips].Y);
                hips = fk[BodyJoint.Hips].Y;
                if (idleAfter < 0 && animator.LocomotionCue.Key == AnimationKeys.Idle) idleAfter = i;
            }
            Assert.That(idleAfter, Is.InRange(0, 6), "the legs reach idle within 0.1 s of standing still");
            Assert.That(worstDrop, Is.LessThan(0.05f), "the hips settle, they don't drop " + worstDrop + " m in one frame");
        }

        // Build 05 verify round 4 (J4-02): the dodge strike's dash back in (up to ArriveLungeMaxSpeed, 20 m/s) runs on
        // strides, so the legs move under the body instead of one crouched pose sliding along with both feet up; and once the
        // dash has slowed, still in the strike, the feet land within a few frames instead of hovering.
        [Test]
        public void DodgeStrikeDashRunsOnStridesAndLands([Values(ElementId.Fire, ElementId.Water, ElementId.Earth)] ElementId element)
        {
            ElementMoveSet set = ElementLoadout.CreateFluid().Get(element);
            MoveData move = set.DodgeStrike;
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
            var animator = new FighterAnimator(PoseLibrary.Default, skeleton, set.AnimationStyle);
            var fk = new ForwardKinematics(skeleton);
            for (int i = 0; i < 20; i++) animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "" });
            Vector3 left0 = Vector3.Zero, right0 = Vector3.Zero;
            float leftMoved = 0f, rightMoved = 0f, lowest = float.MaxValue;
            const int DashFrames = 12;
            for (int i = 0; i < DashFrames; i++)
            {
                FighterAnimInput input = ActionInput(move.AnimationKey, i * Dt * 0.4f, move, 9);   // a stretched startup
                input.LocalVelocity = new Vector3(0f, 0f, 19f);
                input.DashIn = true;
                animator.Update(input);
                fk.Compute(animator.Pose, Vector3.Zero, 0f);
                Vector3 hips = fk[BodyJoint.Hips];
                Vector3 left = fk[BodyJoint.LeftFoot] - hips, right = fk[BodyJoint.RightFoot] - hips;
                if (i == 3) { left0 = left; right0 = right; }
                if (i > 3)
                {
                    leftMoved = Math.Max(leftMoved, Vector3.Distance(left, left0));
                    rightMoved = Math.Max(rightMoved, Vector3.Distance(right, right0));
                    lowest = Math.Min(lowest, Math.Min(fk[BodyJoint.LeftFoot].Y, fk[BodyJoint.RightFoot].Y));
                }
            }
            Assert.Greater(Math.Max(leftMoved, rightMoved), 0.2f, element + ": the legs stride under the body during the dash");
            // (Fire's is a leaping spin kick, Northern Shaolin: the kicking leg is up and the body leaps, so no stride.)
            if (element != ElementId.Fire) Assert.Less(lowest, 0.12f, element + ": a foot comes down to the floor during the dash");
            int landedAfter = -1;
            for (int i = 0; i < 10; i++)
            {
                FighterAnimInput input = ActionInput(move.AnimationKey, move.ActiveStart + i * Dt, move, 9);   // struck, stopped
                input.DashIn = true;
                animator.Update(input);
                fk.Compute(animator.Pose, Vector3.Zero, 0f);
                float low = Math.Min(fk[BodyJoint.LeftFoot].Y, fk[BodyJoint.RightFoot].Y);
                if (landedAfter < 0 && low < 0.11f) landedAfter = i;
            }
            Assert.That(landedAfter, Is.InRange(0, 4), element + ": a foot plants within a few frames of the dash stopping");
        }

        // Verify round 2 (R2-S02): the legs stop at once when the fighter stops (J-09), but the arms ease from the run's
        // swing back to guard instead of snapping there in two or three frames.
        [Test]
        public void ArmsEaseBackToGuardWhenARunStops()
        {
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
            var animator = new FighterAnimator(PoseLibrary.Default, skeleton);
            var fk = new ForwardKinematics(skeleton);
            for (int i = 0; i < 20; i++) animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "" });
            Vector3 left = Vector3.Zero, right = Vector3.Zero;
            for (int i = 0; i < 61; i++)
            {
                animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "", LocalVelocity = new Vector3(0f, 0f, 5.5f) });
                fk.Compute(animator.Pose, Vector3.Zero, 0f);
                left = fk[BodyJoint.LeftHand];
                right = fk[BodyJoint.RightHand];
            }
            float worst = 0f;
            for (int i = 0; i < 20; i++)
            {
                animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "" });
                fk.Compute(animator.Pose, Vector3.Zero, 0f);
                worst = Math.Max(worst, Math.Max(Vector3.Distance(left, fk[BodyJoint.LeftHand]), Vector3.Distance(right, fk[BodyJoint.RightHand])));
                left = fk[BodyJoint.LeftHand];
                right = fk[BodyJoint.RightHand];
            }
            // Before the fix a hand jumped 0.32 m in one frame; easing at SpeedSmoothing (10/s) moves it at most ~0.08 m.
            Assert.That(worst, Is.LessThan(0.12f), "worst hand jump per frame on the stop: " + worst + " m");
        }

        // W-03: touching down from a jump plants the feet; they don't float back up for a few frames.
        [Test]
        public void LandingKeepsAFootOnTheFloor()
        {
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
            var animator = new FighterAnimator(PoseLibrary.Default, skeleton);
            var fk = new ForwardKinematics(skeleton);
            for (int i = 0; i < 20; i++) animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "" });
            for (int i = 0; i < 40; i++)
                animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = false, ActionKey = "", LocalVelocity = new Vector3(0f, 5f - i * 0.4f, 0f) });
            for (int i = 0; i < 30; i++)
            {
                animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "" });
                fk.Compute(animator.Pose, Vector3.Zero, 0f);
                float lowest = Math.Min(fk[BodyJoint.LeftToes].Y, fk[BodyJoint.RightToes].Y);
                Assert.That(lowest, Is.LessThan(0.035f), "both feet off the floor " + i + " frames after landing");
            }
        }

        // J5-04 / J6-01: an air finisher still in its air pose as the body lands (FinisherGravityScale gets it down early)
        // ends up standing on the floor, never with its feet hanging 0.3 m up, and never by pulling a foot down more than
        // 0.15 m (root-relative) in one frame. With the floor known on the way down (the game casts a ray) the legs reach
        // for it before the touchdown and are down on the first grounded frame; without it they come down over a few frames.
        [Test]
        public void AirFinisherLandsWithItsFeetOnTheFloor([Values(true, false)] bool floorKnown)
        {
            ElementMoveSet earth = ElementMoveSet.CreateEarthFluid();
            MoveData meteor = earth.AirChain[earth.AirChain.Length - 1];
            Assert.AreEqual(AnimationKeys.AirMeteor, meteor.AnimationKey);
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
            var animator = new FighterAnimator(PoseLibrary.Default, skeleton, earth.AnimationStyle);
            var fk = new ForwardKinematics(skeleton);
            for (int i = 0; i < 20; i++) animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "" });
            for (int i = 0; i < 30; i++)
                animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = false, ActionKey = "", LocalVelocity = new Vector3(0f, 4f - i * 0.4f, 0f) });
            float t = 0f, height = 1.6f, worstDrop = 0f;
            float[] last = { float.NaN, float.NaN };
            void Track()
            {
                fk.Compute(animator.Pose, Vector3.Zero, 0f);
                float[] now = { fk[BodyJoint.LeftFoot].Y, fk[BodyJoint.RightFoot].Y };
                for (int f = 0; f < 2; f++)
                {
                    if (!float.IsNaN(last[f])) worstDrop = Math.Max(worstDrop, last[f] - now[f]);
                    last[f] = now[f];
                }
            }
            for (int i = 0; i < 8; i++, t += Dt)
            {
                FighterAnimInput air = ActionInput(AnimationKeys.AirMeteor, t, meteor);
                air.Grounded = false;
                air.LocalVelocity = new Vector3(0f, -12f, 0f);
                height = Math.Max(0f, height - 12f * Dt);
                air.HasFloorBelow = floorKnown;
                air.FloorBelow = height;
                animator.Update(air);
                if (i >= 4) Track();
            }
            int downBy = floorKnown ? 0 : (int)Math.Ceiling(PoseLibrary.Default.Settings.LandingFitRise / Dt) + 1;
            for (int i = 0; i < 20; i++, t += Dt)
            {
                animator.Update(ActionInput(AnimationKeys.AirMeteor, t, meteor));
                Track();
                float lowest = Math.Min(Math.Min(fk[BodyJoint.LeftToes].Y, fk[BodyJoint.RightToes].Y), Math.Min(fk[BodyJoint.LeftFoot].Y, fk[BodyJoint.RightFoot].Y));
                if (i >= downBy) Assert.That(lowest, Is.LessThan(0.08f), "both feet off the floor " + i + " frames after landing: " + lowest + " m");
            }
            Assert.That(worstDrop, Is.LessThanOrEqualTo(0.15f), "a foot dropped " + worstDrop + " m in one frame around the touchdown");
        }

        // J6-01: a plunge in every element, through the real combat model and PlayerAnimationFeed, with the floor known (as
        // PlayerController's ray gives it): from the frame before the touchdown to 12 frames after, no foot moves down more
        // than 0.15 m relative to the root in one frame (the axe kick's raised leg used to drop 1.22 m in one), the feet are
        // on the floor by the touchdown, and the body under the impact is crouched in the landing (hips below the stance's).
        [Test]
        public void PlungeTouchdownNeverSnapsAFoot([Values(ElementId.Fire, ElementId.Water, ElementId.Earth, ElementId.Air)] ElementId element)
        {
            PlayerDriver d = PlayerDriver.Elements();
            Assert.IsTrue(d.Model.SetElementAtRest(element));
            ElementMoveSet set = d.Model.MoveSet;
            var feed = new PlayerAnimationFeed();
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
            var animator = new FighterAnimator(PoseLibrary.Default, skeleton, set.AnimationStyle);
            var fk = new ForwardKinematics(skeleton);
            float[] last = { float.NaN, float.NaN };
            float worstDrop = 0f, standingHips = float.NaN, impactHips = float.NaN, impactLowest = float.NaN;
            int window = -1, touchdown = -1;
            bool wasGrounded = true;
            void Frame(Pad pad)
            {
                d.Step(pad);
                for (int i = 0; i < d.Last.Events.Count; i++) feed.OnEvent(d.Last.Events[i]);
                animator.Update(feed.Build(d.Model, d.Dt, false, d.World.Position, Vector3.Zero, d.World.Position.Y));
                fk.Compute(animator.Pose, Vector3.Zero, 0f);
                float[] now = { fk[BodyJoint.LeftFoot].Y, fk[BodyJoint.RightFoot].Y };
                bool grounded = d.Model.IsGrounded;
                if (grounded && !wasGrounded && d.Frame > 5)
                {
                    window = 13;
                    touchdown = d.Frame;
                    impactHips = fk[BodyJoint.Hips].Y;
                    impactLowest = Math.Min(Math.Min(fk[BodyJoint.LeftToes].Y, fk[BodyJoint.RightToes].Y), Math.Min(now[0], now[1]) - 0.06f);
                }
                if (!grounded && d.World.Position.Y < 0.4f && window < 0) window = 16;   // the frames just before it too
                if (window > 0)
                {
                    for (int f = 0; f < 2; f++) if (!float.IsNaN(last[f])) worstDrop = Math.Max(worstDrop, last[f] - now[f]);
                    window--;
                }
                last[0] = now[0];
                last[1] = now[1];
                wasGrounded = grounded;
            }
            for (int i = 0; i < 30; i++) Frame(Pad.None);
            fk.Compute(animator.Pose, Vector3.Zero, 0f);
            standingHips = fk[BodyJoint.Hips].Y;
            Frame(Pad.Jump);
            Frame(Pad.None);
            int guard = 0, waitFrames = (int)Math.Ceiling((set.Plunge.MinAirTime + 0.1f) / d.Dt);
            while (d.Model.State != PlayerState.Plunging && guard++ < 60)
                Frame(guard > waitFrames && guard % 2 == 0 ? Pad.Heavy : Pad.None);
            Assert.AreEqual(PlayerState.Plunging, d.Model.State, element + ": plunged");
            for (int i = 0; i < 60; i++) Frame(Pad.None);
            Assert.GreaterOrEqual(touchdown, 0, element + ": landed");
            Assert.AreEqual(1, d.Count(PlayerEventType.PlungeImpact), element + ": the plunge hit the floor");
            Assert.That(worstDrop, Is.LessThanOrEqualTo(0.15f), element + ": a foot dropped " + worstDrop + " m in one frame at the touchdown");
            Assert.That(impactLowest, Is.LessThan(0.08f), element + ": feet on the floor at the impact");
            Assert.That(impactHips, Is.LessThan(standingHips - 0.05f), element + ": crouched into the landing at the impact (hips "
                + impactHips + " vs standing " + standingHips + ")");
        }

        // J7-02: every dodge start (evade-out, side-slips, backstep, slip-in), from standing and from a run, in every element:
        // a foot near the floor (ankle under 0.2 m this frame or the last) never moves more than 0.3 m over the ground in one
        // frame, in the world. The feet used to stay planted for the dash's first moving frame and then jump 0.4-0.7 m.
        [Test]
        public void DodgeStartsNeverJumpAFootAlongTheFloor([Values(ElementId.Fire, ElementId.Water, ElementId.Earth, ElementId.Air)] ElementId element)
        {
            var sticks = new[] { new Vector2(0f, -1f), new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), Vector2.Zero };
            float worst = 0f;
            string worstAt = "";
            foreach (bool running in new[] { false, true })
            {
                foreach (Vector2 stick in sticks)
                {
                    PlayerDriver d = PlayerDriver.Elements();
                    Assert.IsTrue(d.Model.SetElementAtRest(element));
                    d.Target(new Vector3(0f, 0f, 3.5f));
                    var feed = new PlayerAnimationFeed();
                    HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
                    var animator = new FighterAnimator(PoseLibrary.Default, skeleton, d.Model.MoveSet.AnimationStyle);
                    var fk = new ForwardKinematics(skeleton);
                    var last = new Vector3[2];
                    bool haveLast = false;
                    void Frame(Pad pad, Vector2 move)
                    {
                        d.Step(pad, move);
                        for (int i = 0; i < d.Last.Events.Count; i++) feed.OnEvent(d.Last.Events[i]);
                        animator.Update(feed.Build(d.Model, d.Dt, false, d.World.Position, Vector3.Zero, d.World.Position.Y));
                        fk.Compute(animator.Pose, d.World.Position, d.Model.FacingYaw);
                        Vector3[] now = { fk[BodyJoint.LeftFoot], fk[BodyJoint.RightFoot] };
                        if (haveLast && d.Model.IsGrounded)
                        {
                            for (int f = 0; f < 2; f++)
                            {
                                if (Math.Min(last[f].Y, now[f].Y) - d.World.Position.Y >= 0.2f) continue;
                                float step = new Vector2(now[f].X - last[f].X, now[f].Z - last[f].Z).Length();
                                if (step > worst)
                                {
                                    worst = step;
                                    worstAt = (running ? "running, " : "standing, ") + "stick " + stick + ", frame " + d.Frame + ", " + d.Model.State;
                                }
                            }
                        }
                        last[0] = now[0];
                        last[1] = now[1];
                        haveLast = true;
                    }
                    for (int i = 0; i < 20; i++) Frame(Pad.None, running ? new Vector2(1f, 0f) : Vector2.Zero);
                    Frame(Pad.Dodge, stick);
                    for (int i = 0; i < 30; i++) Frame(Pad.None, Vector2.Zero);
                    Assert.GreaterOrEqual(d.Count(PlayerEventType.DodgeStarted), 1, element + ": dodged");
                }
            }
            Assert.That(worst, Is.LessThanOrEqualTo(0.3f), element + ": a foot near the floor moved " + worst + " m in one frame (" + worstAt + ")");
        }

        // S7-06: running into a wall (the character controller doesn't move), the legs follow the real velocity, not the
        // model's intended one, so they stop striding; in a dodge the model's velocity still drives the leap.
        [Test]
        public void FreeLegsFollowTheRealVelocityActionsTheModels()
        {
            PlayerDriver d = PlayerDriver.Elements();
            var feed = new PlayerAnimationFeed();
            d.Run(30, Pad.None, new Vector2(0f, 1f));
            Assert.AreEqual(PlayerState.Locomotion, d.Model.State);
            Assert.Greater(Directions.Flatten(d.Model.Velocity).Length(), 3f, "the model wants to run");
            FighterAnimInput walled = feed.Build(d.Model, d.Dt, false, d.World.Position, Vector3.Zero, -1f, true, Vector3.Zero);
            Assert.Less(new Vector2(walled.LocalVelocity.X, walled.LocalVelocity.Z).Length(), 1e-4f, "against a wall: the legs stop");
            FighterAnimInput free = feed.Build(d.Model, d.Dt, false, d.World.Position, Vector3.Zero, -1f, true, d.Model.Velocity);
            Assert.Greater(free.LocalVelocity.Z, 3f, "moving freely: the real velocity is the model's");
            FighterAnimInput unknown = feed.Build(d.Model, d.Dt, false, d.World.Position, Vector3.Zero);
            Assert.Greater(unknown.LocalVelocity.Z, 3f, "no real velocity given (CombatSim): the model's");
            d.Step(Pad.Dodge, new Vector2(0f, 1f));
            d.Step(Pad.None);
            Assert.AreEqual(PlayerState.Dodging, d.Model.State);
            FighterAnimInput dodge = feed.Build(d.Model, d.Dt, false, d.World.Position, Vector3.Zero, -1f, true, Vector3.Zero);
            Assert.Greater(new Vector2(dodge.LocalVelocity.X, dodge.LocalVelocity.Z).Length(), 3f, "a dodge keeps the model's velocity for its leap");
        }

        // J6-02: back out (an evade-out), stop, then walk and run straight back in: the gait faces the way it goes from its
        // first step. The old normalised lerp from the backward heading swept through sideways: a 'strafe' crab with the
        // feet 0.93 m apart while moving straight forward. Also a start to the side while the body turns to face it (15
        // degrees a frame) never lags into a sideways step: the heading is smoothed in the world, not the body's frame.
        [Test]
        public void WalkingBackInAfterBackingOutNeverCrabs([Values("", "water", "earth", "air")] string style)
        {
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
            var animator = new FighterAnimator(PoseLibrary.Default, skeleton, style);
            var fk = new ForwardKinematics(skeleton);
            FighterAnimInput Move(Vector3 v, float yawDelta = 0f) =>
                new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "", LocalVelocity = v, YawDelta = yawDelta };
            for (int i = 0; i < 20; i++) animator.Update(Move(Vector3.Zero));
            // An evade-out goes back and a little to the side (170 degrees off the facing, as the dodge carries it).
            for (int i = 0; i < 20; i++) animator.Update(Move(new Vector3(0.7f, 0f, -3.94f)));
            for (int i = 0; i < 15; i++) animator.Update(Move(Vector3.Zero));
            float widest = 0f;
            for (int i = 0; i < 40; i++)
            {
                animator.Update(Move(new Vector3(0f, 0f, Math.Min(4.8f, 0.6f * (i + 1)))));
                fk.Compute(animator.Pose, Vector3.Zero, 0f);
                Assert.AreNotEqual(AnimationKeys.Strafe, animator.LocomotionCue.Key, style + ": strafe key moving straight, frame " + i);
                if (animator.LocomotionCue.Key == AnimationKeys.Run) widest = Math.Max(widest, Math.Abs(fk[BodyJoint.LeftFoot].X - fk[BodyJoint.RightFoot].X));
            }
            Assert.That(widest, Is.LessThanOrEqualTo(0.6f), style + ": feet apart across the body while running straight");

            // Start sideways from a stand while the body turns 15 degrees a frame to face the way it goes.
            for (int i = 0; i < 30; i++) animator.Update(Move(Vector3.Zero));
            for (int i = 0; i < 12; i++)
            {
                float local = Math.Max(0f, 90f - 15f * i);       // the travel, seen from the turning body
                float speed = Math.Min(3f, 0.5f * (i + 1));
                var v = new Vector3((float)Math.Sin(local * Math.PI / 180.0), 0f, (float)Math.Cos(local * Math.PI / 180.0)) * speed;
                animator.Update(Move(v, i > 0 && local > 0f ? 15f : 0f));
                if (local <= 30f) Assert.AreNotEqual(AnimationKeys.Strafe, animator.LocomotionCue.Key, style + ": strafe key once facing the way it goes, frame " + i);
            }
        }

        // MV-03: Earth's horse stance sits 0.2 m under its walk; stopping (or starting) eases the pelvis there instead of
        // dropping it 10 cm in one frame.
        [Test]
        public void EarthStanceHipsEaseOnStopAndStart()
        {
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
            var animator = new FighterAnimator(PoseLibrary.Default, skeleton, ElementMoveSet.CreateEarthFluid().AnimationStyle);
            var fk = new ForwardKinematics(skeleton);
            float last = float.NaN, worst = 0f;
            for (int i = 0; i < 150; i++)
            {
                bool walking = i >= 30 && i < 90;
                animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "", LocalVelocity = walking ? new Vector3(0f, 0f, 1.8f) : Vector3.Zero });
                fk.Compute(animator.Pose, Vector3.Zero, 0f);
                float hips = fk[BodyJoint.Hips].Y;
                if (!float.IsNaN(last)) worst = Math.Max(worst, Math.Abs(hips - last));
                last = hips;
            }
            Assert.That(worst, Is.LessThan(0.05f), "the hips moved " + worst + " m in one frame");
        }

        [Test]
        public void LeapingClipsAreMarked()
        {
            foreach (string key in new[] { AnimationKeys.ZipKick, AnimationKeys.SprintKick, AnimationKeys.WindLeap, AnimationKeys.WindRunnerKick })
                Assert.IsTrue(PoseLibrary.Default.Get(key).Leaps, key);
            Assert.IsFalse(PoseLibrary.Default.Get(AnimationKeys.AirMeteor).Leaps);
            foreach (string key in new[] { AnimationKeys.AxeKick, AnimationKeys.SnakeDrop, AnimationKeys.QuakeDrop, AnimationKeys.AirLanding })
                Assert.IsTrue(PoseLibrary.Default.Get(key).LandsItself, key + " lands its own legs (J6-01)");
        }

        // The combat rules snap the facing round at an attack's start (a jab at a foe behind you): the body whips round
        // over a few frames on its planted feet instead of flipping 180 degrees in one.
        [Test]
        public void AnInstantAboutTurnIsShownAsAQuickTurn()
        {
            MoveData jab = ElementMoveSet.CreateFireFluid().LightChain[0];
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
            var animator = new FighterAnimator(PoseLibrary.Default, skeleton);
            var fk = new ForwardKinematics(skeleton);
            for (int i = 0; i < 30; i++) animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "" });
            fk.Compute(animator.Pose, Vector3.Zero, 0f);
            var last = (Vector3[])fk.Positions.Clone();
            float worst = 0f;
            for (float t = 0f; t < jab.ActiveStart + 0.001f; t += Dt)
            {
                FighterAnimInput input = ActionInput(AnimationKeys.Jab, t, jab);
                if (t == 0f) input.YawDelta = 180f;
                animator.Update(input);
                fk.Compute(animator.Pose, Vector3.Zero, 180f);
                for (int j = 0; j < BodyJoints.Count; j++) worst = Math.Max(worst, Vector3.Distance(last[j], fk[(BodyJoint)j]));
                last = (Vector3[])fk.Positions.Clone();
            }
            Assert.That(worst, Is.LessThan(0.6f), "a joint jumped " + worst + " m in one frame");
            // By the first active frame the body faces the new way (the jab's own twist aside).
            Vector3 fist = fk[BodyJoint.RightHand] - fk[BodyJoint.Hips];
            Assert.That(fist.Z, Is.LessThan(0f), "the jab still points the old way");
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
