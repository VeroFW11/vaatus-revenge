using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Build 05 verify round 3: the pure-C# halves of the fixes (the Unity halves are in the HUD, PlayerFeedback and the
    // effect runners).
    public class VerifyRound3Tests
    {
        const float MinFollowUpLead = 0.25f;

        // J3-01: the beat ring must be on screen long enough to be anticipated. Pressing on each beat, the ring for every
        // follow-up after the first is visible >= 0.25 s before its beat (it starts closing at the previous press), in all
        // four elements and both presets; the first beat's ring is up from the opening press. The predicted beat matches
        // the real one to within a frame (the move starts on the first frame past the cancel point).
        [Test]
        public void BeatRingLeadsEveryFollowUp()
        {
            foreach (bool punishing in new[] { false, true })
            {
                for (ElementId element = ElementId.Fire; element <= ElementId.Air; element++)
                {
                    PlayerDriver d = punishing
                        ? PlayerDriver.Elements(PlayerTuning.CreatePunishing(), ElementLoadout.CreatePunishing())
                        : PlayerDriver.Elements();
                    string context = (punishing ? "Punishing " : "Fluid ") + element;
                    Assert.IsTrue(d.Model.SetElementAtRest(element), context);
                    List<float> leads = BeatLeads(d, 4, out float maxJump);
                    Assert.AreEqual(4, leads.Count, context + ": four judged beats");
                    float opener = d.Model.Loadout.Get(element).LightChain[0].ActiveStart;
                    Assert.GreaterOrEqual(leads[0], opener - d.Dt - 1e-4f, context + ": the first ring is up from the opening press");
                    for (int i = 1; i < leads.Count; i++)
                        Assert.GreaterOrEqual(leads[i], MinFollowUpLead, context + ": ring lead before beat " + (i + 1));
                    Assert.LessOrEqual(maxJump, d.Dt + 1e-3f, context + ": the predicted beat is the real one (within a frame)");
                    Assert.AreEqual(4, d.Model.Rhythm.Streak, context + ": every press was on the beat");
                }
            }
        }

        // Presses on each judged beat and returns how long a ring had been closing on that beat before it (rings are
        // followed frame to frame by their beat time); maxJump is the largest change in a followed ring's beat time between
        // two frames (a misprediction).
        static List<float> BeatLeads(PlayerDriver d, int presses, out float maxJump)
        {
            var leads = new List<float>();
            maxJump = 0f;
            double now = 0.0;
            var rings = new List<(double beat, double since)>();
            var seen = new List<(double beat, double since)>();
            double tolerance = d.Dt + 1e-3;
            d.Step(Pad.Light);
            now += d.Dt;
            for (int f = 0; f < 600 && leads.Count < presses; f++)
            {
                RhythmView view = d.Model.Rhythm;
                seen.Clear();
                if (view.Cue) seen.Add((now + view.CueTimeToBeat, 0.0));
                if (view.NextCue) seen.Add((now + view.NextCueTimeToBeat, 0.0));
                for (int i = 0; i < seen.Count; i++)
                {
                    double since = now - d.Dt;                        // a new ring appeared with the previous step
                    double best = double.MaxValue;
                    foreach ((double beat, double from) in rings)
                    {
                        double jump = Math.Abs(beat - seen[i].beat);
                        if (jump <= tolerance * 2 && jump < best)
                        {
                            best = jump;
                            since = from;
                        }
                    }
                    if (best < double.MaxValue) maxJump = Math.Max(maxJump, (float)best);
                    seen[i] = (seen[i].beat, since);
                }
                rings.Clear();
                rings.AddRange(seen);
                bool press = view.Active && view.TimeToBeat <= d.Dt * 0.5f;
                if (press)
                {
                    Assert.IsTrue(view.Cue && !view.CueIsNextHit, "the ring on screen is the beat being judged");
                    leads.Add((float)(now + view.TimeToBeat - rings[0].since));
                }
                d.Step(press ? Pad.Light : Pad.None);
                now += d.Dt;
            }
            return leads;
        }

        // J3-01: the tutorial puts you in its start element (Fire), whatever you were in; a switch at rest is free.
        [Test]
        public void TutorialStartsInFire()
        {
            Assert.AreEqual(ElementId.Fire, TutorialScript.CreateDefault().StartElement);
            PlayerDriver d = PlayerDriver.Elements();
            Assert.IsTrue(d.Model.SetElementAtRest(ElementId.Air));
            Assert.AreEqual(ElementId.Air, d.Model.ActiveElement);
            Assert.AreEqual(0f, d.Model.SwitchCooldownRemaining, "no cooldown");
            d.Step();
            Assert.IsFalse(d.LastOf(PlayerEventType.ElementSwitched).IsSwitchStrike);
            d.Step(Pad.Light);
            d.Step();
            Assert.IsFalse(d.Model.SetElementAtRest(ElementId.Fire), "not during an attack");
            Assert.AreEqual(ElementId.Air, d.Model.ActiveElement);
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 200);
            Assert.IsTrue(d.Model.SetElementAtRest(ElementId.Fire));
            Assert.AreEqual(ElementId.Fire, d.Model.ActiveElement);
        }

        const float Dt = 1f / 60f;

        // J3-02: a blend out of a fast spin goes on turning the same way rather than unwinding the short way round; a slow
        // turn still takes the short way.
        [Test]
        public void SpinCarriesOnIntoTheNextClip()
        {
            Assert.Less(RootYawAfterSpin(1000f, out float worstBack), -300f, "a 1000 deg/s spin to -100 carries on round to -360");
            Assert.LessOrEqual(worstBack, 1f, "never turned back against the spin");
            Assert.Greater(RootYawAfterSpin(200f, out _), -1f, "a slow turn to -100 comes back the short way");
        }

        // Plays a clip turning RootYaw 0 -> -100 at 'speed' deg/s, then blends into a clip at 0; returns where the blend
        // ended (unwrapped by following it frame to frame) and how far it ever turned back (positive yaw) on the way.
        static float RootYawAfterSpin(float speed, out float worstBack)
        {
            PoseSpec start = PoseLibraryDefaults.Neutral();
            PoseSpec end = PoseLibraryDefaults.Neutral();
            end[PoseChannel.RootYaw] = -100f;
            PoseSpec rest = PoseLibraryDefaults.Neutral();
            var library = new PoseLibrary
            {
                Clips = new[]
                {
                    new PoseClip { Key = "spin", Mode = ClipMode.Hold, Keys = new[]
                    {
                        new PoseKeyframe { Phase = KeyPhase.Seconds, At = 0f, Pose = start },
                        new PoseKeyframe { Phase = KeyPhase.Seconds, At = 100f / speed, Ease = PoseEase.Linear, Pose = end },
                    } },
                    new PoseClip { Key = "rest", Mode = ClipMode.Hold, FadeIn = 0.15f, Keys = new[] { new PoseKeyframe { Phase = KeyPhase.Seconds, Pose = rest } } },
                }
            };
            var animator = new FighterAnimator(library, HumanoidSkeleton.Create());
            int frames = (int)Math.Ceiling(100f / speed / Dt);
            for (int i = 0; i < frames; i++) animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "spin", ActionTime = (i + 1) * Dt, ActionSerial = 1 });
            float yaw = animator.CurrentSpec[PoseChannel.RootYaw];
            worstBack = 0f;
            float lowest = yaw;
            for (int i = 0; i < 30; i++)
            {
                animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "rest", ActionTime = i * Dt, ActionSerial = 2 });
                float now = animator.CurrentSpec[PoseChannel.RootYaw];
                float step = now - yaw;
                step = ((step % 360f) + 540f) % 360f - 180f;
                yaw += step;
                lowest = Math.Min(lowest, yaw);
                worstBack = Math.Max(worstBack, yaw - lowest);
            }
            return yaw;
        }

        // J3-02: Air's Spiral Kick, chained into Downburst Palm at its cancel point on the beat, keeps turning the way it spins.
        [Test]
        public void SpiralKickChainNeverUnwinds()
        {
            ElementMoveSet air = ElementMoveSet.CreateAirFluid();
            MoveData kick = air.AirChain[1], palm = air.AirChain[2];
            Assert.AreEqual(AnimationKeys.AirSpiralKick, kick.AnimationKey);
            var animator = new FighterAnimator(PoseLibrary.Default, HumanoidSkeleton.Create(), air.AnimationStyle);
            float rate = air.Rhythm.OnBeatPlaybackRate;
            float t = 0f, yaw = 0f, previous = 0f, against = 0f;
            for (int i = 0; t < kick.ChainCancelAt; i++, t += Dt * rate)
            {
                animator.Update(Action(kick, t, 1, false));
                float now = animator.CurrentSpec[PoseChannel.RootYaw];
                if (i > 0) yaw += Wrap(now - previous);
                previous = now;
            }
            float spin = Math.Sign(yaw - 20f);                // from the wind-up (+20) on, it has been turning this way
            Assert.AreEqual(-1f, spin, "the kick spins to the left (negative yaw)");
            float turned = 0f;
            for (int i = 0; i < 12; i++)
            {
                animator.Update(Action(palm, i * Dt * rate, 2, false));
                float now = animator.CurrentSpec[PoseChannel.RootYaw];
                float step = Wrap(now - previous);
                previous = now;
                turned += step;
                against = Math.Max(against, -spin * turned);
                Assert.LessOrEqual(Math.Abs(step), 45f, "frame " + i + " of the blend turns " + step + " degrees");
            }
            Assert.LessOrEqual(against, 30f, "the body turned back against the spin");
        }

        static FighterAnimInput Action(MoveData move, float time, int serial, bool grounded, Vector3 velocity = default)
        {
            return new FighterAnimInput
            {
                DeltaTime = Dt, Grounded = grounded, ActionKey = move.AnimationKey, ActionTime = time, ActionSerial = serial,
                HasFrameData = true, Timing = ClipTiming.FromMove(move), LocalVelocity = velocity
            };
        }

        static float Wrap(float a)
        {
            return ((a % 360f) + 540f) % 360f - 180f;
        }

        // J3-03: a gap-closing opener rushing in at 8 m/s runs there in steps: a foot is planted (holding its spot on the
        // floor) on most frames, and both feet never slide along low together for 3 frames.
        [Test]
        public void GapClosingLungeTakesSteps()
        {
            foreach ((ElementMoveSet set, MoveData move) in new[]
            {
                (ElementMoveSet.CreateEarthFluid(), ElementMoveSet.CreateEarthFluid().LightChain[0]),
                (ElementMoveSet.CreateWaterFluid(), ElementMoveSet.CreateWaterFluid().LightChain[0]),
            })
            {
                var animator = new FighterAnimator(PoseLibrary.Default, HumanoidSkeleton.Create(), set.AnimationStyle);
                for (int i = 0; i < 30; i++) animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "" });
                var speed = new Vector3(0f, 0f, 8f);
                float travel = 0f;
                Vector3[] last = Feet(animator, 0f);
                int glide = 0, worstGlide = 0, planted = 0, frames = 0;
                for (float t = Dt; t < move.Startup; t += Dt, frames++)
                {
                    animator.Update(Action(move, t, 1, true, speed));
                    travel += speed.Z * Dt;
                    Vector3[] feet = Feet(animator, travel);
                    bool lowBoth = feet[0].Y < 0.16f && feet[1].Y < 0.16f;
                    float v0 = Vector3.Distance(feet[0], last[0]) / Dt, v1 = Vector3.Distance(feet[1], last[1]) / Dt;
                    glide = lowBoth && v0 > 2f && v1 > 2f ? glide + 1 : 0;
                    worstGlide = Math.Max(worstGlide, glide);
                    if ((feet[0].Y < 0.12f && v0 < 0.5f) || (feet[1].Y < 0.12f && v1 < 0.5f)) planted++;
                    last = feet;
                }
                Assert.Less(worstGlide, 3, move.DisplayName + ": both feet slid along together");
                Assert.GreaterOrEqual(planted, frames / 3, move.DisplayName + ": a foot holds its spot while the body rushes over it");
            }
        }

        // The feet in the world (the body has travelled 'travel' metres along +Z).
        static Vector3[] Feet(FighterAnimator animator, float travel)
        {
            Vector3 offset = new Vector3(0f, 0f, travel);
            return new[] { animator.Solver.ModelPosition(BodyJoint.LeftFoot) + offset, animator.Solver.ModelPosition(BodyJoint.RightFoot) + offset };
        }

        // J3-03: a surf rides on purpose: its clip says so and keeps its glide.
        [Test]
        public void SurfClipsGlide()
        {
            Assert.IsTrue(PoseLibrary.Default.Get(AnimationKeys.WaveRide).Glides);
            Assert.IsTrue(PoseLibrary.Default.Get(AnimationKeys.EarthSurf).Glides);
            Assert.IsFalse(PoseLibrary.Default.Get(AnimationKeys.HorsePunch).Glides);
        }

        // J3-06 / J3-07: Earth's stone comes from the floor, and an airborne switch strike into Earth accents with dust.
        [Test]
        public void EarthStoneRules()
        {
            Assert.IsTrue(ElementFxRules.MixAccentIsDust(ElementId.Earth, true), "airborne switch into Earth: dust");
            Assert.IsFalse(ElementFxRules.MixAccentIsDust(ElementId.Earth, false));
            foreach (ElementId other in new[] { ElementId.Fire, ElementId.Water, ElementId.Air })
            {
                Assert.IsFalse(ElementFxRules.MixAccentIsDust(other, true), other + " has no rock");
                Assert.IsFalse(ElementFxRules.StoneFromFloor(other));
            }
            Assert.IsTrue(ElementFxRules.StoneFromFloor(ElementId.Earth));
            Assert.IsTrue(ElementFxRules.FloorStoneUnder(ElementId.Earth, 1.1f), "a fist at chest height: rock from the floor under it");
            Assert.IsFalse(ElementFxRules.FloorStoneUnder(ElementId.Earth, 3f), "a juggled foe: dust only");
            Assert.IsFalse(ElementFxRules.FloorStoneUnder(ElementId.Water, 0f));
        }

        // J3-S05: an air-string slam doesn't lift you, and from its strike on you fall at full gravity with the foe.
        [Test]
        public void SlamFinisherDropsYou()
        {
            foreach (bool punishing in new[] { false, true })
            {
                ElementLoadout loadout = punishing ? ElementLoadout.CreatePunishing() : ElementLoadout.CreateFluid();
                for (ElementId element = ElementId.Fire; element <= ElementId.Air; element++)
                {
                    MoveData[] chain = loadout.Get(element).AirChain;
                    Assert.AreEqual(0f, chain[chain.Length - 1].SelfLift, element + " " + chain[chain.Length - 1].DisplayName);
                }
            }
            PlayerDriver d = PlayerDriver.Elements();
            d.Model.SetElementAtRest(ElementId.Air);
            d.Step(Pad.Jump);
            d.Run(6);
            int started = d.Started;
            d.Step(Pad.Light);
            d.RunUntilStarted(++started, 60);
            for (int i = 0; i < 4 && !d.LastStarted.IsFinisher; i++)
            {
                d.PressOnBeat();
                d.RunUntilStarted(++started, 60);
            }
            Assert.IsTrue(d.LastStarted.IsFinisher && d.LastStarted.AttackKind == PlayerAttackKind.Air, "reached the air finisher");
            Assert.IsFalse(d.Model.IsGrounded);
            d.RunUntil(x => x.Model.Phase != AttackPhase.Startup, 60);
            d.Step();
            float before = d.Last.Velocity.Y;
            d.Step();
            Assert.IsFalse(d.Model.IsGrounded, "still in the air as the slam strikes");
            float gravity = d.Model.Tuning.Gravity;
            Assert.AreEqual(-gravity * d.Dt, d.Last.Velocity.Y - before, gravity * d.Dt * 0.15f, "full gravity after the slam strikes");
        }

        // J3-S10: Tiger Claw Rake is a raking swipe (dust and stone from the floor along it), not a straight spike line.
        [Test]
        public void TigerClawRakeSwipes()
        {
            MoveData rake = ElementMoveSet.CreateEarthFluid().LightChain[1];
            Assert.AreEqual("Tiger Claw Rake", rake.DisplayName);
            Assert.AreEqual(EffectKeys.Trail, rake.EffectKey);
        }

        // J3-S03: on Punishing a switch strike pressed early in a mid-string dodge outlives its short buffer: the switch
        // still happens (a plain switch) instead of the press vanishing, and the next X carries the string on in it.
        [Test]
        public void SwitchStrikeThatExpiresStillSwitches()
        {
            PlayerDriver d = PlayerDriver.Elements(PlayerTuning.CreatePunishing(), ElementLoadout.CreatePunishing());
            d.Target(new Vector3(0f, 0f, 1.5f));
            d.Step(Pad.Light);
            d.RunUntil(x => x.Model.State == PlayerState.Attacking && x.Model.Phase == AttackPhase.Recovery, 60);
            d.Step(Pad.Dodge, new Vector2(1f, 0f));
            d.RunUntil(x => x.Model.State == PlayerState.Dodging, 60, Pad.None, new Vector2(1f, 0f));
            d.Select = ElementId.Water;
            d.Step();
            d.RunUntil(x => x.Model.State != PlayerState.Dodging, 120);
            d.Run(2);
            Assert.AreEqual(ElementId.Water, d.Model.ActiveElement, "RB + X in the dodge was not dropped");
        }

        // J3-S01: RB still held mid-string for the next X in the same element carries the string on without a denial
        // (the element wheel doesn't shake on every hit).
        [Test]
        public void SameElementWithRbHeldIsNoDenial()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Step(Pad.Light);
            d.RunUntilStarted(1);
            d.SwitchOnBeat(ElementId.Fire);
            Assert.AreEqual(PlayerCommand.Light, d.Model.BufferedCommand);
            Assert.AreEqual(0, d.Count(PlayerEventType.ElementSwitchDenied));
        }

        // J3-S07: a jump takes the jump pose on its first frame off the ground, not after hovering in the stance.
        [Test]
        public void JumpPoseStartsAtTakeOff()
        {
            var animator = new FighterAnimator(PoseLibrary.Default, HumanoidSkeleton.Create());
            for (int i = 0; i < 20; i++) animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionKey = "" });
            animator.Update(new FighterAnimInput { DeltaTime = Dt, Grounded = false, ActionKey = "", LocalVelocity = new Vector3(0f, 7f, 0f) });
            Assert.AreEqual(AnimationKeys.Jump, animator.LocomotionCue.Key);
        }
    }
}
