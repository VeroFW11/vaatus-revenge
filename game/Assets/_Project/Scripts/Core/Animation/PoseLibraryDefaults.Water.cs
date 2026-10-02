using System.Collections.Generic;

namespace VaatusRevenge.Core
{
    // Water (Tai Chi, Yang style). Everything is round and continuous: the body sinks back onto the rear leg to receive,
    // then the waist turns and the weight rolls forward into the strike; the arms stay softly curved between strikes and
    // only straighten at the moment of contact. The stance is upright with the weight back, the lead hand rounded in front
    // of the chest and the rear palm low on the centre line ("water:idle"). Lead side left, like every element.
    // Clips follow the same rules as Fire's (see the top of PoseLibraryDefaults.cs): anticipation in Startup, full
    // extension snapped in on Active 0, follow-through and back to the stance in Recovery.
    public static partial class PoseLibraryDefaults
    {
        public const string WaterStyle = "water";

        // Tai Chi ready stance: upright, weight on the rear leg, the lead foot light, the arms rounded as if holding a ball.
        public static PoseSpec WaterStance()
        {
            var s = new PoseSpec();
            s.Hips(0f, -0.07f, -0.07f).Pelvis(0f, 16f).Torso(-3f, 4f).Head(0f, 0f);
            s.Set(PoseChannel.LookFront, 1f).Set(PoseChannel.ArmFollow, 0.5f);
            // elbows sunk, wrists soft: the lead forearm rounded across the chest, the rear palm low over the belly
            s.Arm(L, -16f, -2f, 0.64f, 8f, 70f, 20f).Arm(R, -26f, -34f, 0.6f, 8f, 80f, 30f);
            s.Foot(L, 0.1f, Ground, 0.34f, 0f).Foot(R, 0.15f, Ground, -0.2f, 45f);
            return s;
        }

        static void AddWater(List<PoseClip> clips)
        {
            PoseSpec water = WaterStance();
            AddWaterStates(clips, water);

            // 1. Ward Off: sink back, then the lead arm rounds out and forward as the weight rolls onto the front leg; at
            //    contact the palm straightens into the foe.
            clips.Add(Strike(AnimationKeys.PalmWard, water)
                .K(KeyPhase.Startup, 0.45f, PoseEase.InOut, s =>
                {
                    s.Hips(0f, -0.12f, -0.08f).Pelvis(0f, 30f).Torso(-2f, 14f);
                    s.Arm(L, -22f, 2f, 0.5f, 15f, 60f, 10f).Arm(R, -30f, -20f, 0.5f, 10f, 60f, 20f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.14f, 0.1f).Pelvis(0f, 18f).Torso(6f, 24f);
                    s.Arm(L, -6f, 6f, 1f, 0f, 70f, 60f).Arm(R, -35f, -10f, 0.5f, 10f, 60f, 30f);
                    s.Foot(L, 0.11f, Ground, 0.44f, 0f).Foot(R, 0.15f, Ground, -0.22f, 45f, 0f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(6f, 22f).Arm(L, -4f, 8f, 1f, 0f, 70f, 60f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(L, -8f, 8f, 0.75f, 12f, 60f, 20f).Hips(0f, -0.11f, 0.04f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, water)
                .Build());

            // 2. Roll Back: the rear palm reaches out to catch the foe's arm, then the waist turns and the weight sinks back,
            //    drawing it in (the pull) past your right side.
            clips.Add(Strike(AnimationKeys.PalmRollback, water)
                .K(KeyPhase.Startup, 0.5f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0.2f);
                    s.Pelvis(0f, 26f).Torso(0f, 18f).Hips(0f, -0.11f, -0.02f);
                    s.Arm(R, 4f, 10f, 0.6f, 10f, 60f, 30f).Arm(L, -12f, 12f, 0.66f, 10f, 60f, 20f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.12f, 0.06f).Pelvis(0f, 8f).Torso(4f, -8f);
                    s.Arm(R, -12f, 8f, 1f, 0f, 60f, 40f).Arm(L, -15f, 12f, 0.76f, 10f, 60f, 30f);
                    s.Foot(L, 0.11f, Ground, 0.4f, 0f);
                })
                .K(KeyPhase.Active, 0.3f, PoseEase.Linear, s => s.Torso(4f, -10f).Arm(R, -14f, 8f, 1f, 0f, 60f, 40f))
                .K(KeyPhase.Active, 1f, PoseEase.InOut, s =>
                {
                    s.Hips(0f, -0.13f, -0.04f).Pelvis(0f, 20f).Torso(0f, 20f);
                    s.Arm(R, 20f, 2f, 0.82f, 10f, 60f, 30f).Arm(L, 0f, 10f, 0.72f, 10f, 60f, 30f);
                })
                .K(KeyPhase.Recovery, 0.45f, PoseEase.Out, s =>
                {
                    s.Hips(0f, -0.11f, -0.08f).Pelvis(0f, 32f).Torso(-2f, 34f);
                    s.Arm(R, 40f, -12f, 0.62f, 10f, 60f, 20f).Arm(L, 10f, 0f, 0.65f, 10f, 60f, 20f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, water)
                .Build());

            // 3. Press: the lead forearm comes across the chest with the rear palm laid behind its wrist, then both drive
            //    out together as the weight rolls forward.
            clips.Add(Strike(AnimationKeys.ForearmPress, water)
                .K(KeyPhase.Startup, 0.5f, PoseEase.InOut, s =>
                {
                    s.Hips(0f, -0.12f, -0.07f).Torso(-2f, 10f);
                    s.Arm(L, -40f, 10f, 0.45f, 10f, 0f, 0f).Arm(R, -50f, 6f, 0.4f, 10f, 60f, 60f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.14f, 0.12f).Pelvis(0f, 15f).Torso(8f, 12f);
                    s.Arm(L, -10f, 8f, 1f, 0f, 90f, 0f).Arm(R, -30f, 6f, 0.78f, 10f, 60f, 70f);
                    s.Foot(L, 0.11f, Ground, 0.46f, 0f).Foot(R, 0.15f, Ground, -0.22f, 45f, 0f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(8f, 14f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(L, -12f, 8f, 0.7f, 10f, 60f, 20f).Arm(R, -30f, 0f, 0.6f, 10f, 60f, 30f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, water)
                .Build());

            // 4. Push: sink back with both palms drawn in to the chest, then push them out together, heels of the palms
            //    first, the whole body behind them: a surge of water.
            clips.Add(Strike(AnimationKeys.TwoPalmPush, water)
                .K(KeyPhase.Startup, 0.55f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.15f, -0.08f).Torso(-4f, 4f);
                    s.Arm(L, -30f, 5f, 0.35f, -10f, 0f, 60f).Arm(R, -30f, 5f, 0.35f, -10f, 0f, 60f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.15f, 0.14f).Pelvis(0f, 8f).Torso(10f, 0f);
                    s.Arm(L, -10f, 4f, 1f, 0f, -80f, 80f).Arm(R, -10f, 4f, 1f, 0f, -80f, 80f);
                    s.Foot(L, 0.13f, Ground, 0.46f, 5f).Foot(R, 0.16f, Ground, -0.26f, 40f, 0f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(11f, 0f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s =>
                {
                    s.Hips(0f, -0.13f, 0.08f);
                    s.Arm(L, -12f, 5f, 0.75f, 0f, -40f, 40f).Arm(R, -12f, 5f, 0.75f, 0f, -40f, 40f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, water)
                .Build());

            // 5. Single Whip: both hands circle to the right, the rear hand forms the hook and stretches back, then the lead
            //    palm sweeps out across the whole front at full length as the body opens: a long lash of water.
            clips.Add(Strike(AnimationKeys.SingleWhip, water)
                .K(KeyPhase.Startup, 0.6f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0.03f, -0.12f, -0.05f).Pelvis(0f, 45f).Torso(0f, 40f);
                    s.Arm(R, 70f, 10f, 0.85f, 10f, 0f, -60f).Arm(L, -50f, 0f, 0.5f, 15f, 60f, 20f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.16f, 0.06f).Pelvis(0f, 25f).Torso(4f, 20f);
                    s.Arm(L, -40f, 8f, 1f, 0f, 60f, 60f).Arm(R, 110f, 10f, 1f, 0f, 0f, -70f);
                    s.Foot(L, 0.14f, Ground, 0.44f, 10f).Foot(R, 0.16f, Ground, -0.24f, 45f, 0f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Arm(L, 30f, 8f, 1f, 0f, 60f, 60f).Torso(4f, -5f).Pelvis(0f, 10f))
                .K(KeyPhase.Recovery, 0.35f, PoseEase.Out, s => s.Arm(L, 20f, 5f, 0.85f, 10f, 60f, 30f).Arm(R, 90f, 0f, 0.85f, 10f, 0f, -40f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, water)
                .Build());

            // Cloud Hands (pause 1): the waist turns side to side and the palms take turns circling out past the face,
            //    right, left, right, left, one soft strike on each turn, drawing the foe in all the while.
            clips.Add(Strike(AnimationKeys.CloudHands, water)
                .K(KeyPhase.Startup, 0.6f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0.6f);
                    s.Hips(-0.04f, -0.14f, 0f).Torso(0f, -18f);
                    s.Arm(R, -30f, -10f, 0.55f, 10f, 60f, 20f).Arm(L, -30f, 15f, 0.55f, 10f, 60f, 20f);
                })
                .K(KeyPhase.Startup, 0.85f, PoseEase.In, s =>
                {
                    s.Hips(0.02f, -0.14f, 0f).Pelvis(0f, 26f).Torso(1f, 10f);
                    s.Arm(R, 12f, 0f, 0.8f, 10f, 60f, 30f).Arm(L, -22f, -8f, 0.66f, 10f, 60f, 20f);
                })
                .Hits(4, (s, k) => CloudHand(s, k % 2 == 0 ? R : L), (s, k) => CloudHandsBetween(s))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(L, 0f, 0f, 0.75f, 10f, 60f, 20f).Torso(2f, -10f).Hips(0f, -0.12f, 0f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, water)
                .Build());

            // Part the Wild Horse's Mane (pause 2): hold the ball on the right, step through, and the lead arm sweeps up and
            //    out along the diagonal at full length while the rear hand presses down by the hip.
            clips.Add(Strike(AnimationKeys.SplitMane, water)
                .K(KeyPhase.Startup, 0.55f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.14f, -0.06f).Pelvis(0f, 35f).Torso(0f, 30f);
                    s.Arm(R, -10f, 12f, 0.5f, 10f, 90f, 10f).Arm(L, -30f, -25f, 0.5f, 10f, -90f, 10f);
                    s.Foot(L, 0.1f, 0.1f, 0.2f, 0f, 20f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.16f, 0.12f).Pelvis(0f, 12f).Torso(6f, 10f);
                    s.Arm(L, 15f, 22f, 1f, 0f, 30f, 0f).Arm(R, 20f, -70f, 0.9f, 10f, 0f, 70f);
                    s.Foot(L, 0.14f, Ground, 0.48f, 5f).Foot(R, 0.16f, Ground, -0.24f, 45f, 0f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Arm(L, 25f, 26f, 1f, 0f, 30f, 0f).Torso(6f, 4f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(L, 15f, 15f, 0.8f, 10f, 40f, 10f).Hips(0f, -0.13f, 0.06f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, water)
                .Build());

            // Return the Tide (dodge strike): still turned away from the dodge, the body swings back in and the rear palm
            //    sends the wave home, the lead palm guarding at the elbow.
            clips.Add(Strike(AnimationKeys.ReturnTide, water)
                .K(KeyPhase.Startup, 0.5f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.16f, -0.04f).Pelvis(0f, 22f).Torso(0f, 12f);
                    s.Arm(R, 24f, 5f, 0.52f, 10f, 60f, 40f).Arm(L, -34f, 10f, 0.55f, 10f, 60f, 30f);
                })
                .K(KeyPhase.Startup, 0.8f, PoseEase.In, s => s.Pelvis(0f, 16f).Torso(4f, 4f).Hips(0f, -0.15f, 0.02f).Arm(R, 6f, 5f, 0.7f, 5f, 0f, 60f))
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.15f, 0.08f).Pelvis(0f, 10f).Torso(8f, -4f);
                    s.Arm(R, -10f, 6f, 1f, 0f, -70f, 80f).Arm(L, -40f, 10f, 0.55f, 10f, 60f, 40f);
                    s.Foot(L, 0.12f, Ground, 0.44f, 5f).Foot(R, 0.15f, Ground, -0.24f, 20f, 20f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(8f, -6f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(R, -12f, 6f, 0.75f, 0f, -40f, 40f).Hips(0f, -0.13f, 0.06f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, water)
                .Build());

            AddWaterAir(clips, water);
            AddWaterAbilities(clips, water);
        }

        // One turn of cloud hands: the waist turns toward 'side', whose palm circles out past the face at full length; the
        // other hand sweeps low across the belly.
        static void CloudHand(PoseSpec s, BodySide side)
        {
            float turn = side == R ? 18f : -18f;
            BodySide other = side == R ? L : R;
            s.Hips(side == R ? 0.05f : -0.05f, -0.14f, 0f).Pelvis(0f, 22f + turn * 0.5f).Torso(2f, turn);
            s.Arm(side, 35f, 6f, 1f, 0f, 70f, 40f).Arm(other, -16f, -10f, 0.7f, 10f, 60f, 20f);
        }

        // Between two turns: both hands pass in front of the body, the waist square.
        static void CloudHandsBetween(PoseSpec s)
        {
            s.Hips(0f, -0.15f, 0f).Pelvis(0f, 22f).Torso(2f, 0f);
            s.Arm(L, 8f, 2f, 0.84f, 5f, 60f, 30f).Arm(R, 8f, -4f, 0.84f, 5f, 60f, 30f);
        }

        // Water's stance, charge and guard (style overrides: "water:idle" replaces "idle" while Water is active).
        static void AddWaterStates(List<PoseClip> clips, PoseSpec water)
        {
            // Idle: slow, deep breathing; the arms float up a little on the in-breath and sink on the out-breath, the
            // weight easing back and forth between the legs.
            clips.Add(new ClipBuilder(WaterStyle + ":" + AnimationKeys.Idle, ClipMode.Loop, water) { LoopPeriod = 4.2f, ArmSwing = 0.6f }
                .K(KeyPhase.Cycle, 0.5f, PoseEase.InOut, s =>
                {
                    s.Hips(0.012f, -0.1f, -0.07f).Torso(-2f, 10f).Head(-1f, 0f);
                    s.Shrug(L, 2f).Shrug(R, 2f);
                    s.Arm(L, -8f, 14f, 0.76f, 10f, 60f, 15f).Arm(R, -30f, -10f, 0.6f, 10f, 60f, 25f);
                })
                .Build());

            // Charge (Ocean Palm): sink back and gather, holding a ball of water at the belly (rear palm on top, lead palm
            // beneath), breathing down.
            clips.Add(new ClipBuilder(WaterStyle + ":" + AnimationKeys.Charge, ClipMode.Hold, water) { FadeIn = 0.06f }
                .K(KeyPhase.Seconds, 0.25f, PoseEase.InOut, WaterGather)
                .K(KeyPhase.Seconds, 1.1f, PoseEase.InOut, s => s.Hips(0f, -0.2f, -0.12f).Torso(-4f, 24f))
                .Build());

            // Parry: both palms circle out in front, meeting the strike softly and widely (the widest parry).
            PoseSpec parry = water.Clone();
            parry.Torso(2f, 6f).Set(PoseChannel.ArmFollow, 0.6f);
            parry.Arm(L, -10f, 26f, 0.72f, 15f, 70f, 30f).Arm(R, -36f, 14f, 0.56f, 15f, 70f, 30f);
            clips.Add(new ClipBuilder(WaterStyle + ":" + AnimationKeys.Parry, ClipMode.Hold, water) { FadeIn = 0.06f, UpperBodyOnly = true }
                .K(KeyPhase.Seconds, 0.06f, PoseEase.Snap, parry)
                .K(KeyPhase.Seconds, 0.22f, PoseEase.InOut, s => s.Arm(L, -6f, 20f, 0.7f, 15f, 70f, 25f))
                .Build());
        }

        static void WaterGather(PoseSpec s)
        {
            s.Set(PoseChannel.ArmFollow, 0.5f);
            s.Hips(0f, -0.17f, -0.1f).Pelvis(0f, 30f).Torso(-3f, 20f);
            s.Arm(R, -34f, 2f, 0.42f, 10f, 90f, 10f).Arm(L, -30f, -28f, 0.45f, 10f, -90f, 10f);
        }

        // The air string and the launcher.
        static void AddWaterAir(List<PoseClip> clips, PoseSpec water)
        {
            PoseSpec air = AirGuard();

            // White Crane Spreads Wings (launcher): sink onto the rear leg, then rise as the right palm sweeps up above the
            //    head and the left presses down by the hip, the right knee lifting: the crane opening its wings.
            clips.Add(Strike(AnimationKeys.CraneRise, water)
                .K(KeyPhase.Startup, 0.6f, PoseEase.In, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0.3f);
                    s.Hips(0f, -0.24f, -0.06f).Torso(10f, 10f);
                    s.Arm(R, -20f, -40f, 0.6f, 10f, 60f, 20f).Arm(L, -20f, -30f, 0.6f, 10f, 60f, 20f);
                    s.Kick(R, 10f, -100f, 0.8f, 0f, 30f);   // weight sinking onto the lead leg, the rear foot light
                })
                .K(KeyPhase.Startup, 0.85f, PoseEase.In, s =>
                {
                    s.Hips(0f, -0.08f, 0f).Torso(-2f, 6f).Kick(R, 6f, -44f, 0.62f, 0f, 20f);
                    s.Arm(R, -4f, 30f, 0.72f, 10f, 30f, 40f).Arm(L, 10f, -45f, 0.75f, 10f, 30f, 50f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, 0.04f, 0f).Torso(-10f, 0f).Set(PoseChannel.LookFront, 0.6f);
                    s.Arm(R, 10f, 78f, 1f, 0f, 0f, 60f).Arm(L, 30f, -60f, 0.95f, 10f, 0f, 70f);
                    s.Kick(L, 4f, -95f, 0.98f, 0f, 45f).Kick(R, 8f, -50f, 0.6f, 0f, 30f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Arm(R, 10f, 84f, 1f, 0f, 0f, 60f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s =>
                {
                    s.Kick(R, 6f, -30f, 0.52f, 0f, 20f).Kick(L, 6f, -62f, 0.58f, 0f, 25f);
                    s.Torso(6f, 4f).Set(PoseChannel.LookFront, 1f);
                    s.Arm(L, -8f, 12f, 0.6f, 5f, 20f).Arm(R, -40f, 28f, 0.4f, 10f, 30f).Set(PoseChannel.ArmFollow, 0.5f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, air)
                .Build());

            // Brush Knee Palm (air 1): the lead hand brushes low across the knees, the rear palm pushes out past the ear.
            clips.Add(Strike(AnimationKeys.AirBrushPalm, air)
                .K(KeyPhase.Startup, 0.5f, PoseEase.InOut, s =>
                {
                    s.Torso(4f, 20f).Arm(L, -30f, -40f, 0.8f, 10f, 0f, 20f).Arm(R, 20f, 30f, 0.45f, 20f, 0f, 40f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f).Torso(8f, -20f).Pelvis(0f, 0f);
                    s.Arm(R, -8f, -4f, 1f, 0f, -70f, 80f).Arm(L, 30f, -45f, 0.9f, 10f, 0f, 30f);
                    s.Kick(R, 10f, -110f, 0.8f, 0f, 40f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(8f, -22f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, air)
                .Build());

            // Fair Lady Works the Shuttles (air 2): turning in the air, the rear forearm rolls up to cover the head and the
            //    lead palm drives out under it.
            clips.Add(Strike(AnimationKeys.AirShuttle, air)
                .K(KeyPhase.Startup, 0.5f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.RootYaw, -15f).Torso(4f, -20f);
                    s.Arm(L, 10f, -10f, 0.45f, 20f, 0f, 60f).Arm(R, -28f, 44f, 0.56f, 25f, 60f, 0f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.RootYaw, 15f).Set(PoseChannel.ArmFollow, 0f).Torso(6f, 25f);
                    s.Arm(R, -25f, 56f, 0.6f, 25f, 60f, 0f).Arm(L, -8f, 4f, 1f, 0f, -70f, 80f);
                    s.Kick(L, 6f, -70f, 0.55f, 0f, 25f).Kick(R, 8f, -100f, 0.85f, 0f, 30f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Set(PoseChannel.RootYaw, 25f).Torso(6f, 30f))
                .K(KeyPhase.Recovery, 0.5f, PoseEase.Out, s => s.Set(PoseChannel.RootYaw, 10f).Torso(6f, 10f).Arm(L, -10f, 5f, 0.7f, 5f, 0f, 30f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, air)
                .Build());

            // Needle at Sea Bottom (air 3): the rear hand rises by the ear, then the body folds forward and the fingers stab
            //    straight down through the foe, driving it to the floor.
            clips.Add(Strike(AnimationKeys.AirNeedle, air)
                .K(KeyPhase.Startup, 0.5f, PoseEase.InOut, s =>
                {
                    s.Torso(-6f, 10f).Arm(R, 15f, 60f, 0.5f, 20f, 90f, 0f).Arm(L, -20f, 10f, 0.6f, 10f, 20f);
                    s.Kick(L, 5f, -50f, 0.55f, 0f, 20f).Kick(R, 6f, -70f, 0.6f, 0f, 30f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f).Torso(30f, -5f).Pelvis(10f, 0f);
                    s.Arm(R, -6f, -58f, 1f, 0f, 90f, 0f).Arm(L, 30f, 20f, 0.7f, 10f);
                    s.Kick(L, 5f, -60f, 0.6f, 0f, 30f).Kick(R, 6f, -95f, 0.8f, 0f, 35f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(32f, -5f).Arm(R, -6f, -62f, 1f, 0f, 90f, 0f))
                .K(KeyPhase.Recovery, 0.5f, PoseEase.Out, s => s.Torso(10f, 0f).Pelvis(0f, 10f).Arm(R, -20f, -20f, 0.6f, 10f, 30f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, air)
                .Build());

            // Snake Creeps Down (plunge): open into the single whip while hanging, ride the fall, then land in the snake's
            //    low posture (lead leg stretched along the floor, the hand reaching along it) and rise back to the stance.
            clips.Add(new ClipBuilder(AnimationKeys.SnakeDrop, ClipMode.Action, air) { FadeIn = 0.06f, LandsItself = true }
                .K(KeyPhase.Startup, 1f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f).Torso(-6f, 20f);
                    s.Arm(R, 100f, 30f, 1f, 0f, 0f, -70f).Arm(L, -30f, 30f, 0.6f, 10f, 60f, 20f);
                    s.Kick(L, 5f, -60f, 0.6f, 0f, 30f).Kick(R, 8f, -85f, 0.8f, 0f, 35f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(-4f, 20f))
                .K(KeyPhase.Recovery, 0.25f, PoseEase.InOut, s =>
                {
                    s.Hips(-0.06f, -0.5f, -0.06f).Pelvis(10f, 40f).Torso(28f, 25f, 8f);
                    s.Foot(R, 0.18f, Ground, -0.16f, 50f, 0f, 25f).Foot(L, 0.22f, Ground, 0.66f, 0f, -10f);
                    s.Arm(L, -10f, -32f, 1f, 0f, 60f, 20f).Arm(R, 100f, 20f, 1f, 0f, 0f, -70f);
                })
                .K(KeyPhase.Recovery, 0.5f, PoseEase.Out, s => s.Hips(-0.05f, -0.46f, -0.04f).Torso(24f, 25f, 6f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, water)
                .Build());
        }

        // The charged heavy, the abilities, the ranged skill, the zip strike and the sprint attack.
        static void AddWaterAbilities(List<PoseClip> clips, PoseSpec water)
        {
            // Ocean Palm (heavy, released from the charge): out of the gathered ball, sink back once more, then roll the
            //    whole weight forward into one long palm.
            PoseSpec gather = water.Clone();
            WaterGather(gather);
            clips.Add(new ClipBuilder(AnimationKeys.TidePalm, ClipMode.Action, gather)
                .K(KeyPhase.Startup, 0.7f, PoseEase.In, s => s.Hips(0f, -0.2f, -0.12f).Pelvis(0f, 34f).Torso(-4f, 24f))
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.18f, 0.14f).Pelvis(0f, -5f).Torso(10f, -18f);
                    s.Arm(R, -8f, 2f, 1f, 0f, -80f, 85f).Arm(L, 20f, -55f, 0.6f, 10f, 90f, 20f);
                    s.Foot(L, 0.16f, Ground, 0.5f, 8f).Foot(R, 0.2f, Ground, -0.24f, 40f, 10f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(11f, -20f))
                .K(KeyPhase.Recovery, 0.3f, PoseEase.Out, s => s.Arm(R, -10f, 3f, 0.8f, 0f, -50f, 60f).Hips(0f, -0.16f, 0.1f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, water)
                .Build());

            // Water Whip (LB + Y): the rear arm winds up high and back, then lashes forward and down at full length.
            clips.Add(Strike(AnimationKeys.WaterLash, water)
                .K(KeyPhase.Startup, 0.7f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0.04f, -0.12f, -0.06f).Pelvis(0f, 30f).Torso(-6f, 35f);
                    s.Arm(R, 80f, 55f, 0.85f, 20f, 0f, 0f).Arm(L, -20f, 10f, 0.65f, 10f, 60f, 20f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.14f, 0.06f).Pelvis(0f, 10f).Torso(6f, 5f);
                    s.Arm(R, 10f, 10f, 1f, 0f, 0f, 20f);
                    s.Foot(R, 0.15f, Ground, -0.22f, 45f, 0f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Arm(R, -10f, -15f, 1f, 0f, 0f, 20f).Torso(10f, -10f))
                .K(KeyPhase.Recovery, 0.35f, PoseEase.Out, s => s.Arm(R, -40f, -40f, 0.8f, 10f, 0f, 10f).Torso(10f, -20f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, water)
                .Build());

            // Tide Ring (LB + B): crouch and wind, then spin a full turn with both arms spread wide, the ring of water
            //    sweeping round twice. RootYaw 0 -> -360.
            clips.Add(Strike(AnimationKeys.TideRing, water)
                .K(KeyPhase.Startup, 1f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.RootYaw, 30f).Set(PoseChannel.ArmFollow, 0.3f).Set(PoseChannel.LookFront, 0.4f);
                    s.Hips(0f, -0.22f, 0f).Torso(10f, 0f);
                    s.Arm(L, 30f, -20f, 0.55f, 10f, 0f, 30f).Arm(R, 30f, -20f, 0.55f, 10f, 0f, 30f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.RootYaw, 0f).Hips(0f, -0.2f, 0f).Torso(4f, 0f);
                    s.Arm(L, 80f, 5f, 1f, 0f, 0f, 40f).Arm(R, 80f, 5f, 1f, 0f, 0f, 40f);
                })
                .K(KeyPhase.Interval, 1f, PoseEase.Linear, s => s.Set(PoseChannel.RootYaw, -180f))
                .K(KeyPhase.Recovery, 0f, PoseEase.Linear, s => s.Set(PoseChannel.RootYaw, -330f))
                .K(KeyPhase.Recovery, 0.5f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.RootYaw, -360f).Set(PoseChannel.LookFront, 0.9f);
                    s.Arm(L, 40f, -10f, 0.75f, 10f, 0f, 20f).Arm(R, 40f, -10f, 0.75f, 10f, 0f, 20f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, s => { s.CopyFrom(water); s.Set(PoseChannel.RootYaw, -360f); })
                .Build());

            // Ice Dart (RB): the rear hand cocks by the ear, then the fingers flick forward and the dart flies on the first
            //    frame of full extension.
            clips.Add(Strike(AnimationKeys.DartFlick, water)
                .K(KeyPhase.Startup, 0.55f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Pelvis(0f, 35f).Torso(-2f, 35f);
                    s.Arm(R, 40f, 45f, 0.45f, 30f, 90f, -20f).Arm(L, -10f, 10f, 0.8f, 5f, 60f, 20f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.12f, 0.08f).Pelvis(0f, 0f).Torso(6f, -15f);
                    s.Arm(R, -6f, 4f, 1f, 0f, -90f, -30f).Arm(L, 20f, -30f, 0.6f, 10f, 60f, 20f);
                })
                .K(KeyPhase.Recovery, 0.12f, PoseEase.Linear, s => s.Torso(7f, -16f))   // zero active frames: hold the flick a moment
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(R, -8f, 2f, 0.8f, 0f, -60f, 0f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, water)
                .Build());

            // Wave Ride Strike (zip): ride across on a surge of water, side-on like a board rider, arms out for balance, then
            //    turn square and arrive with both palms.
            var waveRide = Strike(AnimationKeys.WaveRide, water);
            waveRide.Glides = true;
            clips.Add(waveRide
                .K(KeyPhase.Startup, 0.3f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0.3f);
                    s.Hips(0f, -0.2f, 0f).Pelvis(0f, 70f).Torso(8f, -40f).Head(0f, 0f);
                    s.Foot(L, 0.18f, Ground + 0.05f, 0.18f, 60f).Foot(R, 0.18f, Ground + 0.05f, -0.22f, 70f);
                    s.Arm(L, 60f, 10f, 0.9f, 10f).Arm(R, 70f, 0f, 0.9f, 10f);
                })
                .K(KeyPhase.Startup, 0.85f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f).Pelvis(0f, 30f).Torso(4f, -10f);
                    s.Arm(L, -30f, 5f, 0.35f, -10f, 0f, 60f).Arm(R, -30f, 5f, 0.35f, -10f, 0f, 60f);
                    s.Foot(L, 0.14f, Ground + 0.02f, 0.36f, 25f).Foot(R, 0.17f, Ground + 0.02f, -0.26f, 50f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.16f, 0.12f).Pelvis(0f, 5f).Torso(10f, 0f);
                    s.Arm(L, -10f, 4f, 1f, 0f, -80f, 80f).Arm(R, -10f, 4f, 1f, 0f, -80f, 80f);
                    s.Foot(L, 0.13f, Ground, 0.46f, 5f).Foot(R, 0.16f, Ground, -0.26f, 40f, 0f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(11f, 0f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(L, -12f, 5f, 0.75f, 0f, -40f, 40f).Arm(R, -12f, 5f, 0.75f, 0f, -40f, 40f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, water)
                .Build());

            // Tidal Rush Palm (sprint attack): running in low, the rear palm drawn back at the hip, then one long driving palm.
            clips.Add(Strike(AnimationKeys.RushPalm, water)
                .K(KeyPhase.Startup, 0.5f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.1f, 0.04f).Pelvis(0f, 10f).Torso(18f, 10f);
                    s.Arm(R, 20f, 0f, 0.45f, 10f, 0f, 60f).Arm(L, -20f, 15f, 0.7f, 10f, 60f, 20f);
                    s.Foot(L, 0.12f, 0.14f, 0.25f, 0f, 20f).Foot(R, 0.14f, Ground, -0.3f, 30f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.16f, 0.16f).Pelvis(0f, -5f).Torso(12f, -20f);
                    s.Arm(R, -8f, 4f, 1f, 0f, -80f, 85f).Arm(L, 25f, -40f, 0.7f, 10f, 60f, 20f);
                    s.Foot(L, 0.13f, Ground, 0.5f, 5f).Foot(R, 0.16f, Ground, -0.26f, 30f, 15f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(13f, -22f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Hips(0f, -0.14f, 0.1f).Arm(R, -10f, 4f, 0.8f, 0f, -50f, 50f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, water)
                .Build());
        }
    }
}
