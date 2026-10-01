using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // The four elements' bodies (Build 05 spec 3.6, 4.5, acceptance 6.2): each element's stance stands on the floor with
    // the knees over the toes, every new move has a real clip that is fully extended on its first active frame (each
    // sub-hit of a flurry strikes too), no limb ever stretches past its bones, switching style in the middle of a blend
    // never pops, and the player feed picks the element's style, the dodge clip by kind and the switch flourish only when
    // the player is free. No placeholder clip is left.
    public class ElementPoseTests
    {
        const float Dt = 1f / 60f;
        static readonly string[] Styles = { "", PoseLibraryDefaults.WaterStyle, PoseLibraryDefaults.EarthStyle, PoseLibraryDefaults.AirStyle };

        static IEnumerable<ElementMoveSet> AllSets() => new[]
        {
            ElementMoveSet.CreateFireFluid(), ElementMoveSet.CreateWaterFluid(), ElementMoveSet.CreateEarthFluid(), ElementMoveSet.CreateAirFluid()
        };

        static IEnumerable<MoveData> Moves(ElementMoveSet m)
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

        static FighterAnimInput Idle(string style = null)
        {
            return new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "", Style = style };
        }

        static FighterAnimInput Strike(MoveData move, float t, int serial)
        {
            return new FighterAnimInput
            {
                DeltaTime = Dt, Grounded = true, ActionKey = move.AnimationKey, ActionTime = t, ActionSerial = serial, HasFrameData = true,
                Timing = ClipTiming.FromMove(move)
            };
        }

        static float ArmExtension(ForwardKinematics fk, HumanoidSkeleton sk, BodySide side) =>
            Vector3.Distance(fk[BodyJoints.UpperArm(side)], fk[BodyJoints.Hand(side)]) / sk.ArmLength;

        [Test]
        public void EveryKeyHasItsOwnClip()
        {
            Assert.AreEqual(0, PoseLibraryDefaults.PlaceholderCount, "no animation key borrows another clip any more");
            var keys = typeof(AnimationKeys).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()).ToList();
            Assert.AreEqual(111, keys.Count, "the spec's 111 keys");
            foreach (string key in keys)
                Assert.IsTrue(PoseLibrary.Default.Has(key) || FighterAnimator.IsGaitKey(key), "no clip for '" + key + "'");
        }

        [Test]
        public void EachElementHasItsOwnStanceChargeAndGuard()
        {
            foreach (string style in Styles.Skip(1))
            {
                Assert.IsTrue(PoseLibrary.Default.Has(style + ":" + AnimationKeys.Idle), style + " idle");
                Assert.IsTrue(PoseLibrary.Default.Has(style + ":" + AnimationKeys.Charge), style + " charge");
                Assert.IsTrue(PoseLibrary.Default.Has(style + ":" + AnimationKeys.Parry), style + " parry");
            }
            Assert.IsTrue(PoseLibrary.Default.Has(PoseLibraryDefaults.EarthStyle + ":" + AnimationKeys.Block), "Earth blocks with its iron bridge");
        }

        // Each stance stands: both feet on the floor, knees bending forward over the toes, and it reads differently from
        // the others (the stances aren't the same pose under four names).
        [Test]
        public void EachStanceStandsOnTheFloorAndIsItsOwn()
        {
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
            var stances = new List<Vector3[]>();
            foreach (string style in Styles)
            {
                var animator = new FighterAnimator(PoseLibrary.Default, skeleton, style);
                var fk = new ForwardKinematics(skeleton);
                for (int i = 0; i < 90; i++) animator.Update(Idle());
                fk.Compute(animator.Pose, Vector3.Zero, 0f);
                foreach (BodySide side in new[] { BodySide.Left, BodySide.Right })
                {
                    Vector3 hip = fk[BodyJoints.UpperLeg(side)], knee = fk[BodyJoints.LowerLeg(side)], ankle = fk[BodyJoints.Foot(side)];
                    Vector3 bend = knee - (hip + ankle) * 0.5f;
                    Vector3 toes = Vector3.Normalize(Directions.Flatten(fk[BodyJoints.Toes(side)] - ankle));
                    Assert.That(Vector3.Dot(bend, toes), Is.GreaterThan(0f), style + " " + side + " knee bends backwards");
                    Assert.That(ankle.Y, Is.EqualTo(skeleton.AnkleHeight).Within(0.02f), style + " " + side + " foot is off the floor");
                }
                stances.Add((Vector3[])fk.Positions.Clone());
            }
            for (int a = 0; a < stances.Count; a++)
            for (int b = a + 1; b < stances.Count; b++)
            {
                float difference = 0f;
                for (int j = 0; j < BodyJoints.Count; j++) difference = Math.Max(difference, Vector3.Distance(stances[a][j], stances[b][j]));
                Assert.That(difference, Is.GreaterThan(0.12f), "the '" + Styles[a] + "' and '" + Styles[b] + "' stances look alike");
            }
        }

        // Earth's stance is the wide horse, Water's stands tallest with the weight back, Air's lead palm is up by the eyes.
        [Test]
        public void StancesShowTheirMartialArt()
        {
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
            Vector3[] Stance(string style)
            {
                var animator = new FighterAnimator(PoseLibrary.Default, skeleton, style);
                var fk = new ForwardKinematics(skeleton);
                for (int i = 0; i < 90; i++) animator.Update(Idle());
                fk.Compute(animator.Pose, Vector3.Zero, 0f);
                return (Vector3[])fk.Positions.Clone();
            }
            Vector3[] fire = Stance(""), water = Stance(PoseLibraryDefaults.WaterStyle), earth = Stance(PoseLibraryDefaults.EarthStyle),
                air = Stance(PoseLibraryDefaults.AirStyle);
            float Width(Vector3[] p) => Math.Abs(p[(int)BodyJoint.LeftFoot].X - p[(int)BodyJoint.RightFoot].X);
            Assert.Greater(Width(earth), 0.55f, "Earth: feet wide apart in the horse stance");
            Assert.Greater(Width(earth), Width(fire) + 0.2f);
            Assert.Greater(water[(int)BodyJoint.Hips].Y, fire[(int)BodyJoint.Hips].Y, "Water stands taller than Fire's low bow stance");
            Assert.Less(water[(int)BodyJoint.Hips].Z, 0f, "Water's weight sits back");
            Assert.Less(earth[(int)BodyJoint.Hips].Y, fire[(int)BodyJoint.Hips].Y, "Earth sits lowest");
            Assert.Greater(air[(int)BodyJoint.LeftHand].Y, air[(int)BodyJoint.Head].Y - 0.12f, "Air's lead palm is up at eye height");
            Assert.Less(earth[(int)BodyJoint.LeftHand].Y, earth[(int)BodyJoint.Chest].Y, "Earth's fists are chambered at the waist, below the chest");
            Assert.Less(earth[(int)BodyJoint.RightHand].Y, earth[(int)BodyJoint.Chest].Y);
        }

        // Every move of every element: full extension snapped in on the first active frame, as fully extended as it ever
        // gets (in the element's own style). A flurry (HitCount > 1) strikes again on each later sub-hit: one hand or the
        // other is straight at each sub-hit's moment.
        [Test]
        public void EveryMoveStrikesOnItsActiveFramesInItsOwnStyle()
        {
            foreach (ElementMoveSet set in AllSets())
            foreach (MoveData move in Moves(set))
            {
                HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
                var animator = new FighterAnimator(PoseLibrary.Default, skeleton, set.AnimationStyle);
                var fk = new ForwardKinematics(skeleton);
                var samples = new List<(float t, float ext, float bestArm)>();
                float end = move.ActiveEnd + 2 * Dt;
                for (int frame = 0; frame * Dt <= end; frame++)
                {
                    float t = frame * Dt;
                    animator.Update(Strike(move, t, 1));
                    fk.Compute(animator.Pose, Vector3.Zero, 0f);
                    float ext = Extension(fk, skeleton, move.Limb);
                    samples.Add((t, ext, Math.Max(ArmExtension(fk, skeleton, BodySide.Left), ArmExtension(fk, skeleton, BodySide.Right))));
                }
                float max = samples.Max(x => x.ext);
                (float t, float ext, float bestArm) atActive = samples.First(x => x.t >= move.ActiveStart - 1e-4f);
                string what = set.DisplayName + " " + move.DisplayName;
                Assert.That(max, Is.GreaterThan(0.97f), what + ": the striking limb never straightens");
                Assert.That(atActive.ext, Is.GreaterThanOrEqualTo(max - 0.002f), what + ": not fully extended on the first active frame");
                if (move.HitCount > 1 && move.Limb != Limb.LeftFoot && move.Limb != Limb.RightFoot)
                {
                    for (int k = 1; k < move.HitCount; k++)
                    {
                        float hit = move.ActiveStart + k * move.HitInterval;
                        // the frame nearest the sub-hit's moment
                        (float t, float ext, float bestArm) near = samples.OrderBy(x => Math.Abs(x.t - hit)).First();
                        Assert.That(near.bestArm, Is.GreaterThan(0.93f), what + ": no palm out at sub-hit " + (k + 1));
                    }
                }
            }
        }

        static float Extension(ForwardKinematics fk, HumanoidSkeleton sk, Limb limb)
        {
            float Leg(BodySide s) => Vector3.Distance(fk[BodyJoints.UpperLeg(s)], fk[BodyJoints.Foot(s)]) / sk.LegLength;
            switch (limb)
            {
                case Limb.LeftFist: return ArmExtension(fk, sk, BodySide.Left);
                case Limb.RightFoot: return Leg(BodySide.Right);
                case Limb.LeftFoot: return Leg(BodySide.Left);
                case Limb.BothFists: return Math.Min(ArmExtension(fk, sk, BodySide.Left), ArmExtension(fk, sk, BodySide.Right));
                default: return ArmExtension(fk, sk, BodySide.Right);
            }
        }

        // Every style clip and every new clip, solved through its whole timing at three body sizes: no bone ever changes
        // length and no limb reaches past its full length (the skeleton tolerance).
        [Test]
        public void NoLimbStretchesInAnyElementClip()
        {
            var timing = new ClipTiming { Startup = 0.2f, Active = 0.12f, Recovery = 0.3f, HitCount = 3, HitInterval = 0.08f };
            foreach (float scale in new[] { 1f, 1.04f, 0.9f })
            {
                HumanoidSkeleton skeleton = HumanoidSkeleton.Create(null, scale);
                var solver = new PoseSolver(skeleton);
                var fk = new ForwardKinematics(skeleton);
                var spec = new PoseSpec();
                var pose = new BodyPose();
                foreach (PoseClip clip in PoseLibrary.Default.Clips)
                {
                    for (float t = 0f; t < 0.9f; t += 0.03f)
                    {
                        clip.Evaluate(t, in timing, spec);
                        solver.Solve(spec, pose);
                        fk.Compute(pose, Vector3.Zero, 0f);
                        for (int j = 1; j < BodyJoints.Count; j++)
                        {
                            float length = Vector3.Distance(fk.Positions[j], fk.Positions[BodyJoints.Parent(j)]);
                            Assert.That(length, Is.EqualTo(skeleton.RestOffset(j).Length()).Within(1e-3f), clip.Key + " " + BodyJoints.Name((BodyJoint)j));
                        }
                        foreach (BodySide side in new[] { BodySide.Left, BodySide.Right })
                        {
                            Assert.That(ArmExtension(fk, skeleton, side), Is.LessThanOrEqualTo(1f + 1e-3f), clip.Key + " " + side + " arm overstretched");
                            float leg = Vector3.Distance(fk[BodyJoints.UpperLeg(side)], fk[BodyJoints.Foot(side)]) / skeleton.LegLength;
                            Assert.That(leg, Is.LessThanOrEqualTo(1f + 1e-3f), clip.Key + " " + side + " leg overstretched");
                        }
                    }
                }
            }
        }

        // Switching element in the middle of anything (a blend into a strike, walking, an upper-body parry, standing) blends
        // into the new stance: no joint ever jumps (the same limits as AnimationTests.ChainBlendsNeverPop).
        [Test]
        public void AStyleSwitchMidBlendNeverPops()
        {
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

            ElementLoadout loadout = ElementLoadout.CreateFluid();
            for (int i = 0; i < 20; i++) Step(Idle(), "fire idle");
            int serial = 0;
            // Mid-blend into a Fire jab, switch to Water: the strike plays on, the stance underneath changes.
            MoveData jab = loadout.Fire.LightChain[0];
            serial++;
            for (float t = 0f; t < jab.TotalDuration; t += Dt)
            {
                FighterAnimInput input = Strike(jab, t, serial);
                if (t > 0.03f) input.Style = PoseLibraryDefaults.WaterStyle;
                Step(input, "jab, switching to water");
            }
            for (int i = 0; i < 4; i++) Step(Idle(PoseLibraryDefaults.WaterStyle), "water idle");
            // Mid-way through the blend back to the stance: switch again, twice in quick succession.
            Step(Idle(PoseLibraryDefaults.EarthStyle), "to earth mid-blend");
            for (int i = 0; i < 3; i++) Step(Idle(PoseLibraryDefaults.EarthStyle), "earth");
            for (int i = 0; i < 30; i++) Step(Idle(PoseLibraryDefaults.AirStyle), "to air mid-blend");
            // Walking, then a switch.
            for (int i = 0; i < 20; i++) Step(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "", LocalVelocity = new Vector3(0f, 0f, 2f) }, "air walk");
            for (int i = 0; i < 20; i++)
                Step(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "", LocalVelocity = new Vector3(0f, 0f, 2f), Style = "" }, "walk, to fire");
            // An upper-body action (the parry) with a switch under it, and the switch flourish itself.
            serial++;
            for (int i = 0; i < 12; i++)
                Step(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = AnimationKeys.Parry, ActionTime = i * Dt, ActionSerial = serial,
                    Style = i < 5 ? "" : PoseLibraryDefaults.EarthStyle }, "parry, to earth");
            serial++;
            for (int i = 0; i < 24; i++)
                Step(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = AnimationKeys.ElementSwitch, ActionTime = i * Dt, ActionSerial = serial,
                    ActionDuration = 0.4f, Style = PoseLibraryDefaults.WaterStyle }, "flourish, to water");
            for (int i = 0; i < 30; i++) Step(Idle(PoseLibraryDefaults.WaterStyle), "water idle");
            Assert.AreEqual(PoseLibraryDefaults.WaterStyle, animator.Style);

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

        // Without a style on the input (enemies) the animator keeps the one it was built with.
        [Test]
        public void ANullStyleKeepsTheAnimatorsOwn()
        {
            var animator = new FighterAnimator(PoseLibrary.Default, HumanoidSkeleton.Create(), "sword");
            animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "" });
            Assert.AreEqual("sword", animator.Style);
            Assert.AreSame(PoseLibrary.Default.Get("sword:" + AnimationKeys.Idle), animator.Clip(AnimationKeys.Idle));
            animator.SetStyle(PoseLibraryDefaults.EarthStyle);
            Assert.AreSame(PoseLibrary.Default.Get(PoseLibraryDefaults.EarthStyle + ":" + AnimationKeys.Idle), animator.Clip(AnimationKeys.Idle));
            Assert.AreSame(PoseLibrary.Default.Get(AnimationKeys.Jab), animator.Clip(AnimationKeys.Jab), "unstyled keys fall back to the base clip");
        }

        // ---------------------------------------------------------------- the player feed

        static FighterAnimInput Feed(PlayerDriver d, PlayerAnimationFeed feed)
        {
            foreach (PlayerEvent e in d.Last.Events) feed.OnEvent(e);
            return feed.Build(d.Model, d.Dt);
        }

        [Test]
        public void TheFeedWearsTheActiveElementsStyle()
        {
            PlayerDriver d = PlayerDriver.Elements();
            var feed = new PlayerAnimationFeed();
            d.Step();
            Assert.AreEqual("", Feed(d, feed).Style, "Fire is the base style");
            foreach (ElementId element in new[] { ElementId.Water, ElementId.Earth, ElementId.Air, ElementId.Fire })
            {
                d.Select = element;
                d.Step();
                Assert.AreEqual(d.Model.Loadout.Get(element).AnimationStyle, Feed(d, feed).Style, element + "'s style");
                d.Run(25);
                feed.Build(d.Model, d.Dt);
            }
        }

        [Test]
        public void DodgesPickTheirClipByKind()
        {
            Assert.AreEqual(AnimationKeys.DodgeSlip, PlayerAnimationFeed.DodgeKeyFor(DodgeKind.SlipIn, Vector3.UnitZ, false, false));
            Assert.AreEqual(AnimationKeys.DodgeSideLeft, PlayerAnimationFeed.DodgeKeyFor(DodgeKind.SideSlip, -Vector3.UnitX, false, false));
            Assert.AreEqual(AnimationKeys.DodgeSideRight, PlayerAnimationFeed.DodgeKeyFor(DodgeKind.SideSlip, Vector3.UnitX, false, false));
            Assert.AreEqual(AnimationKeys.DodgeSideLeft, PlayerAnimationFeed.DodgeKeyFor(DodgeKind.AutoEvade, new Vector3(-0.7f, 0f, 0.7f), false, false));
            Assert.AreEqual(AnimationKeys.DodgeSideRight, PlayerAnimationFeed.DodgeKeyFor(DodgeKind.AutoEvade, new Vector3(0.7f, 0f, 0.7f), false, false));
            Assert.AreEqual(AnimationKeys.DodgeEvade, PlayerAnimationFeed.DodgeKeyFor(DodgeKind.EvadeOut, -Vector3.UnitZ, false, false));
            Assert.AreEqual(AnimationKeys.Backstep, PlayerAnimationFeed.DodgeKeyFor(DodgeKind.Backstep, -Vector3.UnitZ, false, true));
            Assert.AreEqual(AnimationKeys.Dodge, PlayerAnimationFeed.DodgeKeyFor(DodgeKind.Traverse, Vector3.UnitZ, false, false));
            Assert.AreEqual(AnimationKeys.AirDash, PlayerAnimationFeed.DodgeKeyFor(DodgeKind.AirDash, Vector3.UnitZ, true, false));

            // And the feed shows it: a slip-in toward a target in front.
            PlayerDriver d = PlayerDriver.Elements();
            var feed = new PlayerAnimationFeed();
            d.Target(new Vector3(0f, 0f, 3f));
            d.Run(5);
            d.Step(Pad.Dodge, new System.Numerics.Vector2(0f, 1f));
            FighterAnimInput input = Feed(d, feed);
            Assert.AreEqual(PlayerState.Dodging, d.Model.State);
            Assert.AreEqual(AnimationKeys.DodgeSlip, input.ActionKey);
        }

        [Test]
        public void ThePlainSwitchFlourishesOnlyWhenFree()
        {
            // Standing free: a plain switch shows the flourish.
            PlayerDriver d = PlayerDriver.Elements();
            var feed = new PlayerAnimationFeed();
            d.Run(5);
            d.Select = ElementId.Earth;
            d.Step();
            Assert.AreEqual(AnimationKeys.ElementSwitch, Feed(d, feed).ActionKey, "standing free");

            // Mid-string: a switch strike is the next hit, never a flourish (nor is a switch while the string's memory lives).
            PlayerDriver s = PlayerDriver.Elements();
            var sFeed = new PlayerAnimationFeed();
            s.Run(5);
            s.OnBeatString(1);
            bool switched = false;
            for (int i = 0; i < 90; i++)
            {
                if (!switched && s.Model.Rhythm.Active && s.Model.Rhythm.TimeToBeat <= s.Dt * 0.5f)
                {
                    s.Select = ElementId.Water;   // RB + X on the beat of the second hit
                    switched = true;
                }
                s.Step();
                Assert.AreNotEqual(AnimationKeys.ElementSwitch, Feed(s, sFeed).ActionKey, "mid-combo, frame " + i);
            }
            Assert.AreEqual(ElementId.Water, s.Model.ActiveElement, "the switch strike switched");
            Assert.IsTrue(s.LastOf(PlayerEventType.ElementSwitched).IsSwitchStrike);
            Assert.AreEqual(PoseLibraryDefaults.WaterStyle, sFeed.Build(s.Model, s.Dt).Style, "and the body wears Water's style");
        }
    }
}
