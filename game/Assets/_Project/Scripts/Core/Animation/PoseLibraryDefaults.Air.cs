using System.Collections.Generic;

namespace VaatusRevenge.Core
{
    // Air (Baguazhang). Bagua walks the circle: the torso stays twisted in toward the centre (the foe), the lead palm held
    // up at eye height, the rear palm guarding under the lead elbow, the knees close together for the gliding mud-step
    // ("air:idle"). Strikes are open palms that change hands constantly: most of Air's moves are two to four quick
    // sub-hits, each one its own snap to full extension (KeyPhase.Interval keys land on each sub-hit), and the body turns
    // and steps round the foe as it strikes (the combat rules move it: MoveData.OrbitDegrees). Clips follow the same rules
    // as Fire's: anticipation in Startup, full extension snapped in on the first active frame, then back to the stance.
    public static partial class PoseLibraryDefaults
    {
        public const string AirStyle = "air";

        // Bagua's circle-walking stance: torso turned in, the lead palm up at eye height, the rear palm under the lead
        // elbow, the lead foot toed in.
        public static PoseSpec AirStance()
        {
            var s = new PoseSpec();
            s.Hips(0f, -0.14f, 0f).Pelvis(0f, 34f).Torso(4f, 22f).Head(0f, 0f);
            s.Set(PoseChannel.LookFront, 1f).Set(PoseChannel.ArmFollow, 0.5f);
            s.Arm(L, -4f, 26f, 0.84f, 10f, -20f, 60f).Arm(R, -36f, 2f, 0.46f, 10f, 40f, 50f);
            s.Foot(L, 0.08f, Ground, 0.27f, -18f, 0f, -6f).Foot(R, 0.12f, Ground, -0.24f, 28f);
            return s;
        }

        static void AddAir(List<PoseClip> clips)
        {
            PoseSpec bagua = AirStance();
            AddAirStates(clips, bagua);

            // 1. Piercing Palm: the lead hand spears out fingers first, then the rear hand spears through under it.
            clips.Add(Strike(AnimationKeys.PiercingPalm, bagua)
                .K(KeyPhase.Startup, 0.5f, PoseEase.InOut, s =>
                {
                    s.Torso(4f, 28f).Arm(L, -10f, 14f, 0.62f, 10f, -20f, 20f).Arm(R, -30f, 4f, 0.56f, 10f, 40f, 50f);
                })
                .Hits(2, (s, k) =>
                {
                    if (k == 0) PalmStrike(s, L, 4f, 4f);
                    else PalmStrike(s, R, -2f, 2f).Arm(L, -14f, 8f, 0.76f, 5f, -40f, 50f);
                }, (s, k) => s.Torso(6f, 14f).Arm(L, -8f, 6f, 0.86f, 5f, -60f, 60f).Arm(R, -8f, 4f, 0.86f, 5f, -60f, 60f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(R, -20f, 4f, 0.7f, 10f, 40f, 40f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, bagua)
                .Build());

            // 2. Turning Palm: the waist turns and the rear palm sweeps across at full length, then the lead palm follows it.
            clips.Add(Strike(AnimationKeys.TurningPalm, bagua)
                .K(KeyPhase.Startup, 0.5f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f).Torso(2f, 34f).Pelvis(0f, 38f);
                    s.Arm(R, 30f, 10f, 0.6f, 10f, 0f, 40f);
                })
                .Hits(2, (s, k) =>
                {
                    if (k == 0)
                    {
                        s.Torso(6f, 16f).Pelvis(0f, 28f).Hips(0f, -0.15f, 0.05f);
                        s.Arm(R, 10f, 6f, 1f, 0f, -80f, 70f).Arm(L, -30f, 20f, 0.55f, 10f, -20f, 40f);
                    }
                    else
                    {
                        s.Torso(6f, -2f).Pelvis(0f, 18f);
                        s.Arm(L, 10f, 8f, 1f, 0f, -80f, 70f).Arm(R, -30f, 2f, 0.62f, 10f, 40f, 40f);
                    }
                }, (s, k) => s.Torso(6f, 8f).Arm(R, -8f, 4f, 0.84f, 5f, -70f, 60f).Arm(L, 0f, 10f, 0.84f, 5f, -60f, 60f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(L, 0f, 14f, 0.8f, 10f, -40f, 50f).Torso(4f, 6f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, bagua)
                .Build());

            // 3. Swimming Body Sweep: the body dips and rolls like a swimmer, the palms sweeping low, middle, low in turn.
            clips.Add(Strike(AnimationKeys.SwimSweep, bagua)
                .K(KeyPhase.Startup, 0.6f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f).Hips(0f, -0.24f, 0f).Torso(14f, 22f, 6f);
                    s.Arm(L, -14f, -12f, 0.66f, 10f, -60f, 30f).Arm(R, -24f, -10f, 0.62f, 10f, 40f, 40f);
                })
                .Hits(3, (s, k) =>
                {
                    if (k == 1)
                    {
                        s.Hips(-0.015f, -0.24f, 0.04f).Torso(14f, 7f, -1f);
                        s.Arm(R, 12f, 0f, 1f, 0f, -80f, 50f).Arm(L, -8f, -8f, 0.78f, 5f, -60f, 30f);
                    }
                    else
                    {
                        s.Hips(0.015f, -0.25f, 0.04f).Torso(16f, 13f, 4f);
                        s.Arm(L, 20f, -12f, 1f, 0f, -80f, 50f).Arm(R, -10f, -4f, 0.76f, 5f, -60f, 40f);
                    }
                }, (s, k) => s.Torso(15f, 10f, 1.5f).Arm(L, 6f, -10f, 0.86f, 5f, -70f, 40f).Arm(R, 8f, -2f, 0.86f, 5f, -70f, 40f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Hips(0f, -0.18f, 0.02f).Torso(8f, 18f, 2f).Arm(L, 0f, 10f, 0.75f, 10f, -40f, 40f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, bagua)
                .Build());

            // 4. Double Palm Change: stepping round the foe, the palms change three times, right, left, right, the waist
            //    whipping from side to side with each change.
            clips.Add(Strike(AnimationKeys.DoublePalmChange, bagua)
                .K(KeyPhase.Startup, 0.55f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f).Torso(4f, 18f).Pelvis(0f, 28f);
                    s.Arm(R, 20f, 0f, 0.55f, 10f, 0f, 40f).Arm(L, -20f, 20f, 0.7f, 10f, -20f, 50f);
                })
                .Hits(3, (s, k) => PalmChange(s, k == 1 ? L : R), (s, k) => PalmChangeBetween(s))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(R, -10f, 8f, 0.7f, 10f, -40f, 40f).Torso(4f, 10f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, bagua)
                .Build());

            // 5. Gale Palm: sink and draw both palms back to the chest, then drive them out together in a deep bow stance:
            //    a gust that blows the foe far away.
            clips.Add(Strike(AnimationKeys.GalePalm, bagua)
                .K(KeyPhase.Startup, 0.6f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.2f, -0.08f).Pelvis(0f, 20f).Torso(-4f, 16f);
                    s.Arm(L, -30f, 8f, 0.35f, -10f, 0f, 70f).Arm(R, -30f, 8f, 0.35f, -10f, 0f, 70f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.2f, 0.16f).Pelvis(0f, 6f).Torso(12f, 0f);
                    s.Arm(L, -10f, 6f, 1f, 0f, -80f, 85f).Arm(R, -10f, 6f, 1f, 0f, -80f, 85f);
                    s.Foot(L, 0.13f, Ground, 0.5f, 5f).Foot(R, 0.16f, Ground, -0.28f, 35f, 10f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(13f, 0f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(L, -12f, 8f, 0.75f, 0f, -40f, 50f).Arm(R, -12f, 8f, 0.75f, 0f, -40f, 50f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, bagua)
                .Build());

            // Circle Walk Flurry (pause 1): walking the circle half way round the foe, a palm on every step: right, left,
            //    right, left, the feet crossing in Bagua's toe-in, toe-out steps.
            clips.Add(Strike(AnimationKeys.CircleWalk, bagua)
                .K(KeyPhase.Startup, 0.6f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f).Torso(4f, 14f);
                    s.Arm(R, 14f, 6f, 0.64f, 10f, 0f, 40f).Arm(L, -10f, 10f, 0.78f, 5f, -40f, 50f);
                })
                .K(KeyPhase.Startup, 0.75f, PoseEase.In, s => s.Hips(0.01f, -0.15f, 0.025f).Pelvis(0f, 22f).Torso(5f, 10f).Arm(R, 6f, 7f, 0.86f, 5f, -60f, 60f))
                .Hits(4, (s, k) => CircleStep(s, k % 2 == 0 ? R : L), (s, k) => CircleStepBetween(s))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(L, 0f, 16f, 0.8f, 10f, -30f, 50f).Torso(4f, 16f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, bagua)
                .Build());

            // Whirlwind (pause 2): crouch and wind, then spin two full turns with both arms spread wide, the vortex lifting
            //    the foe; rise out of the last turn. RootYaw 0 -> -720.
            clips.Add(Strike(AnimationKeys.Whirlwind, bagua)
                .K(KeyPhase.Startup, 1f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.RootYaw, 25f).Set(PoseChannel.ArmFollow, 0.3f).Set(PoseChannel.LookFront, 0.3f);
                    s.Hips(0f, -0.2f, 0f).Torso(10f, 0f).Head(-4f, 0f);
                    s.Arm(L, 40f, -20f, 0.6f, 10f, 0f, 30f).Arm(R, 40f, -20f, 0.6f, 10f, 0f, 30f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.RootYaw, 0f).Hips(0f, -0.14f, 0f).Torso(2f, 0f).Head(-8f, 0f);
                    s.Arm(L, 84f, 12f, 1f, 0f, -90f, 30f).Arm(R, 84f, 12f, 1f, 0f, -90f, 30f);
                })
                .K(KeyPhase.Interval, 0.5f, PoseEase.Linear, s => s.Set(PoseChannel.RootYaw, -200f))
                .K(KeyPhase.Interval, 1f, PoseEase.Linear, s => s.Set(PoseChannel.RootYaw, -400f).Arm(L, 84f, 30f, 1f, 0f, -90f, 30f).Arm(R, 84f, 30f, 1f, 0f, -90f, 30f))
                .K(KeyPhase.Recovery, 0f, PoseEase.Linear, s => s.Set(PoseChannel.RootYaw, -560f))
                .K(KeyPhase.Recovery, 0.5f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.RootYaw, -700f).Set(PoseChannel.LookFront, 0.9f).Head(0f, 0f);
                    s.Arm(L, 30f, 10f, 0.75f, 10f, -40f, 40f).Arm(R, 30f, 10f, 0.75f, 10f, -40f, 40f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, s => { s.CopyFrom(bagua); s.Set(PoseChannel.RootYaw, -720f); })
                .Build());

            // Circle Step Palm (dodge strike): stepping round the foe out of the dodge, three palms in quick succession.
            clips.Add(Strike(AnimationKeys.CircleStepPalm, bagua)
                .K(KeyPhase.Startup, 0.5f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f).Torso(4f, 16f).Hips(0f, -0.17f, 0f);
                    s.Arm(R, 14f, 4f, 0.6f, 10f, 0f, 40f);
                })
                .Hits(3, (s, k) => CircleStep(s, k == 1 ? L : R), (s, k) => CircleStepBetween(s))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(R, -10f, 6f, 0.7f, 10f, -40f, 40f).Torso(4f, 16f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, bagua)
                .Build());

            AddAirAerial(clips, bagua);
            AddAirAbilities(clips, bagua);
        }

        // An open-palm strike at full length with one hand (the other drawn back), the waist turned behind it.
        static PoseSpec PalmStrike(PoseSpec s, BodySide side, float yaw, float pitch)
        {
            s.Set(PoseChannel.ArmFollow, 0f);
            s.Hips(0f, -0.15f, 0.08f).Pelvis(0f, side == L ? 26f : 18f).Torso(6f, side == L ? 20f : 9f);
            s.Arm(side, yaw, pitch, 1f, 0f, -80f, 70f);
            s.Foot(L, 0.09f, Ground, 0.36f, -10f, 0f, -6f);
            return s;
        }

        // One change of the double palm change: the waist whips toward the striking side and that palm strikes at full
        // length; the other hand guards under the elbow.
        static void PalmChange(PoseSpec s, BodySide side)
        {
            bool right = side == R;
            BodySide other = right ? L : R;
            s.Hips(right ? 0.015f : -0.015f, -0.17f, 0.05f).Pelvis(0f, right ? 18f : 28f).Torso(6f, right ? 4f : 16f);
            s.Arm(side, right ? 6f : 2f, 8f, 1f, 0f, -80f, 70f).Arm(other, -12f, 4f, 0.76f, 5f, -40f, 50f);
        }

        static void PalmChangeBetween(PoseSpec s)
        {
            s.Hips(0f, -0.16f, 0.04f).Pelvis(0f, 23f).Torso(6f, 10f);
            s.Arm(L, -2f, 10f, 0.86f, 5f, -60f, 60f).Arm(R, 0f, 6f, 0.86f, 5f, -60f, 60f);
        }

        // A palm on a step round the circle: the waist turns toward the striking hand. The feet keep Bagua's toe-in stance;
        // as the combat rules carry the body round the foe (OrbitDegrees) the animator's foot locks step them along.
        static void CircleStep(PoseSpec s, BodySide side)
        {
            bool right = side == R;
            BodySide other = right ? L : R;
            s.Hips(right ? 0.015f : -0.015f, -0.16f, 0.04f).Pelvis(0f, right ? 22f : 30f).Torso(6f, right ? 8f : 18f);
            s.Arm(side, 4f, 8f, 1f, 0f, -80f, 70f).Arm(other, -12f, 4f, 0.76f, 5f, -40f, 50f);
        }

        // Between two palms: both hands passing the centre line, the waist square.
        static void CircleStepBetween(PoseSpec s)
        {
            s.Hips(0f, -0.16f, 0.04f).Pelvis(0f, 26f).Torso(6f, 13f);
            s.Arm(L, -2f, 10f, 0.86f, 5f, -60f, 60f).Arm(R, 0f, 6f, 0.86f, 5f, -60f, 60f);
        }

        // Air's stance, charge and parry (style overrides while Air is active).
        static void AddAirStates(List<PoseClip> clips, PoseSpec bagua)
        {
            // Idle: never quite still. The weight drifts round in a small circle and the waist keeps turning in and out, the
            // palms floating with it, as if about to walk the circle.
            clips.Add(new ClipBuilder(AirStyle + ":" + AnimationKeys.Idle, ClipMode.Loop, bagua) { LoopPeriod = 2.8f, ArmSwing = 0.8f }
                .K(KeyPhase.Cycle, 0.33f, PoseEase.InOut, s =>
                {
                    s.Hips(0.025f, -0.15f, 0.015f).Torso(5f, 28f).Pelvis(0f, 38f);
                    s.Arm(L, -2f, 30f, 0.86f, 10f, -20f, 60f);
                })
                .K(KeyPhase.Cycle, 0.66f, PoseEase.InOut, s =>
                {
                    s.Hips(-0.02f, -0.13f, -0.015f).Torso(3f, 18f).Pelvis(0f, 30f);
                    s.Arm(L, -6f, 24f, 0.82f, 10f, -20f, 60f).Arm(R, -34f, 6f, 0.48f, 10f, 40f, 50f);
                })
                .Build());

            // Charge (Hurricane Palm): turn the body away and coil, both palms drawn back to the right hip, wound tight.
            clips.Add(new ClipBuilder(AirStyle + ":" + AnimationKeys.Charge, ClipMode.Hold, bagua) { FadeIn = 0.06f }
                .K(KeyPhase.Seconds, 0.18f, PoseEase.InOut, AirCoil)
                .K(KeyPhase.Seconds, 0.7f, PoseEase.InOut, s => s.Torso(-2f, 66f).Hips(0f, -0.24f, -0.06f))
                .Build());

            // Parry: the lead palm sweeps up and out across the face, turning the strike aside.
            PoseSpec parry = bagua.Clone();
            parry.Torso(4f, 14f).Set(PoseChannel.ArmFollow, 0.6f);
            parry.Arm(L, 20f, 40f, 0.8f, 15f, -60f, 60f).Arm(R, -36f, 10f, 0.5f, 10f, 40f, 50f);
            clips.Add(new ClipBuilder(AirStyle + ":" + AnimationKeys.Parry, ClipMode.Hold, bagua) { FadeIn = 0.05f, UpperBodyOnly = true }
                .K(KeyPhase.Seconds, 0.05f, PoseEase.Snap, parry)
                .K(KeyPhase.Seconds, 0.2f, PoseEase.InOut, s => s.Arm(L, 6f, 32f, 0.82f, 12f, -40f, 60f))
                .Build());
        }

        static void AirCoil(PoseSpec s)
        {
            s.Set(PoseChannel.ArmFollow, 0.3f);
            s.Hips(0f, -0.22f, -0.05f).Pelvis(0f, 50f).Torso(-2f, 60f);
            s.Arm(L, -50f, -30f, 0.45f, 10f, 0f, 70f).Arm(R, 20f, -40f, 0.42f, 10f, 0f, 70f);
        }

        // The launcher and the air string.
        static void AddAirAerial(List<PoseClip> clips, PoseSpec bagua)
        {
            PoseSpec air = AirGuard();

            // Updraft Palm (launcher): drop low, then the rear palm drives straight up as the body springs after it, the
            //    gust lifting the foe high.
            clips.Add(Strike(AnimationKeys.UpdraftPalm, bagua)
                .K(KeyPhase.Startup, 0.6f, PoseEase.In, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0.3f);
                    s.Hips(0f, -0.3f, 0f).Torso(14f, 10f);
                    s.Arm(R, 20f, -55f, 0.5f, 10f, 0f, 70f).Arm(L, -10f, 0f, 0.6f, 10f, -20f, 50f);
                    s.Kick(R, 10f, -105f, 0.78f, 0f, 30f).Kick(L, 4f, -80f, 0.76f, 0f, 20f);   // crouched, about to spring
                })
                .K(KeyPhase.Startup, 0.85f, PoseEase.In, s =>
                {
                    s.Hips(0f, -0.12f, 0f).Torso(4f, 6f);
                    s.Arm(R, 2f, 40f, 0.8f, 10f, -60f, 75f);
                    s.Kick(R, 8f, -80f, 0.72f, 0f, 32f).Kick(L, 4f, -92f, 0.88f, 0f, 30f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.LookFront, 0.6f).Hips(0f, 0.04f, 0f).Torso(-10f, 0f).Head(-14f, 0f);
                    s.Arm(R, -4f, 76f, 1f, 0f, -90f, 80f).Arm(L, 30f, -40f, 0.8f, 10f, 0f, 60f);
                    s.Kick(L, 4f, -95f, 0.98f, 0f, 45f).Kick(R, 6f, -70f, 0.7f, 0f, 35f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Arm(R, -4f, 82f, 1f, 0f, -90f, 80f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s =>
                {
                    s.Kick(R, 6f, -30f, 0.52f, 0f, 20f).Kick(L, 6f, -62f, 0.58f, 0f, 25f).Set(PoseChannel.LookFront, 1f).Head(0f, 0f);
                    s.Arm(L, -8f, 12f, 0.6f, 5f, 20f).Arm(R, -40f, 28f, 0.4f, 10f, 30f).Set(PoseChannel.ArmFollow, 0.5f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, air)
                .Build());

            // Air Swipe (air 1): the rear hand swipes across the foe at full length, then the lead hand swipes back.
            clips.Add(Strike(AnimationKeys.AirSwipe, air)
                .K(KeyPhase.Startup, 0.5f, PoseEase.InOut, s => s.Torso(4f, 18f).Arm(R, 40f, 10f, 0.6f, 10f, 0f, 30f))
                .Hits(2, (s, k) =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    if (k == 0) s.Torso(6f, 6f).Arm(R, 20f, 4f, 1f, 0f, -80f, 50f);
                    else s.Torso(6f, 18f).Arm(L, 10f, 6f, 1f, 0f, -80f, 50f).Arm(R, -40f, -10f, 0.7f, 10f, 0f, 30f);
                }, (s, k) => s.Torso(6f, 12f).Arm(R, -2f, 2f, 0.86f, 5f, -60f, 45f).Arm(L, -4f, 8f, 0.84f, 5f, -60f, 45f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, air)
                .Build());

            // Spiral Kick (air 2): the body corkscrews a full turn with the right leg straight out, the shin striking three
            //    times as it comes round. RootYaw 0 -> -360.
            clips.Add(Strike(AnimationKeys.AirSpiralKick, air)
                .K(KeyPhase.Startup, 0.6f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.RootYaw, 30f).Torso(4f, 15f);
                    s.Kick(R, 60f, -20f, 0.5f, 20f, 30f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.RootYaw, -40f).Set(PoseChannel.LookFront, 0.3f).Set(PoseChannel.ArmFollow, 0.6f);
                    s.Torso(-5f, 0f, 18f).Kick(R, 40f, 0f, 1f, 30f, 45f).Kick(L, 6f, -70f, 0.55f, 0f, 25f);
                    s.Arm(L, 75f, 10f, 0.85f, 10f).Arm(R, 30f, 20f, 0.55f, 10f);
                })
                .K(KeyPhase.Interval, 1f, PoseEase.Linear, s => s.Set(PoseChannel.RootYaw, -160f))
                .K(KeyPhase.Recovery, 0f, PoseEase.Linear, s => s.Set(PoseChannel.RootYaw, -220f))
                .K(KeyPhase.Recovery, 0.5f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.RootYaw, -300f).Set(PoseChannel.LookFront, 0.8f);
                    s.Kick(R, 35f, -60f, 0.6f, 0f, 30f).Torso(6f, 5f, 4f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, s => { s.CopyFrom(air); s.Set(PoseChannel.RootYaw, -360f); })
                .Build());

            // Downburst Palm (air 3): both palms rise overhead, then the body folds and pushes them straight down: the
            //    downburst slams the foe into the floor.
            clips.Add(Strike(AnimationKeys.AirDownburst, air)
                .K(KeyPhase.Startup, 0.55f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0.6f).Torso(-12f, 0f);
                    s.Arm(L, 20f, 70f, 0.55f, 20f, 0f, 60f).Arm(R, 20f, 70f, 0.55f, 20f, 0f, 60f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f).Torso(30f, 0f).Pelvis(10f, 0f).Head(-20f, 0f);
                    s.Arm(L, -4f, -44f, 1f, 0f, 90f, 80f).Arm(R, -4f, -44f, 1f, 0f, 90f, 80f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(32f, 0f))
                .K(KeyPhase.Recovery, 0.5f, PoseEase.Out, s => s.Torso(10f, 0f).Pelvis(0f, 10f).Head(0f, 0f).Arm(L, 10f, -10f, 0.6f, 10f, 0f, 30f).Arm(R, 10f, -10f, 0.6f, 10f, 0f, 30f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, air)
                .Build());

            // Air Burst Landing (plunge): float through a long hang with the arms spread like wings, drop, and land softly
            //    on bent knees with both palms pressing out at the floor as the cushion of air bursts round you.
            clips.Add(new ClipBuilder(AnimationKeys.AirLanding, ClipMode.Action, air) { FadeIn = 0.06f }
                .K(KeyPhase.Startup, 1f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0.5f).Torso(-6f, 0f).Head(-6f, 0f);
                    s.Arm(L, 80f, 30f, 0.95f, 10f, -90f, 30f).Arm(R, 80f, 30f, 0.95f, 10f, -90f, 30f);
                    s.Kick(L, 6f, -60f, 0.6f, 0f, 30f).Kick(R, 6f, -75f, 0.7f, 0f, 35f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Arm(L, 70f, 20f, 0.95f, 10f, -90f, 30f).Arm(R, 70f, 20f, 0.95f, 10f, -90f, 30f))
                .K(KeyPhase.Recovery, 0.25f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.4f, 0f).Pelvis(8f, 10f).Torso(26f, 0f).Head(-12f, 0f);
                    s.Arm(L, 40f, -55f, 1f, 0f, 90f, 80f).Arm(R, 40f, -55f, 1f, 0f, 90f, 80f);
                    s.Foot(L, 0.2f, Ground, 0.2f, 10f, 0f, 10f).Foot(R, 0.2f, Ground, -0.2f, 25f, 0f, 10f);
                })
                .K(KeyPhase.Recovery, 0.5f, PoseEase.Out, s => s.Hips(0f, -0.34f, 0f).Torso(20f, 0f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, bagua)
                .Build());
        }

        // The charged heavy, the abilities, the ranged skill, the zip strike and the sprint attack.
        static void AddAirAbilities(List<PoseClip> clips, PoseSpec bagua)
        {
            // Hurricane Palm (heavy, released from the charge): out of the coil, the body unwinds in one turn and both palms
            //    drive out together: a narrow, violent gust.
            PoseSpec coil = bagua.Clone();
            AirCoil(coil);
            clips.Add(new ClipBuilder(AnimationKeys.HurricanePalm, ClipMode.Action, coil)
                .K(KeyPhase.Startup, 0.7f, PoseEase.In, s => s.Pelvis(0f, 56f).Torso(-3f, 68f))
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.2f, 0.16f).Pelvis(0f, 0f).Torso(12f, -6f);
                    s.Arm(L, -10f, 6f, 1f, 0f, -80f, 85f).Arm(R, -10f, 6f, 1f, 0f, -80f, 85f);
                    s.Foot(L, 0.13f, Ground, 0.5f, 5f).Foot(R, 0.16f, Ground, -0.28f, 35f, 10f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(13f, -6f))
                .K(KeyPhase.Recovery, 0.3f, PoseEase.Out, s => s.Arm(L, -12f, 6f, 0.8f, 0f, -40f, 50f).Arm(R, -12f, 6f, 0.8f, 0f, -40f, 50f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, bagua)
                .Build());

            // Air Blade (LB + Y): the rear hand rises high to the side, then chops down and across at full length, sending
            //    a thin blade of air down the line.
            clips.Add(Strike(AnimationKeys.AirBlade, bagua)
                .K(KeyPhase.Startup, 0.65f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Pelvis(0f, 40f).Torso(-6f, 40f);
                    s.Arm(R, 60f, 70f, 0.75f, 20f, 0f, 0f).Arm(L, -10f, 10f, 0.7f, 10f, -20f, 40f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.16f, 0.08f).Pelvis(0f, 6f).Torso(8f, -8f);
                    s.Arm(R, 4f, 10f, 1f, 0f, 0f, 0f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Arm(R, -24f, -16f, 1f, 0f, 0f, 0f).Torso(12f, -18f))
                .K(KeyPhase.Recovery, 0.35f, PoseEase.Out, s => s.Arm(R, -40f, -40f, 0.75f, 10f, 0f, 0f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, bagua)
                .Build());

            // Air Shield (LB + B): the arms sweep out and the body turns a full circle inside the sphere of spinning air,
            //    which bursts out twice. RootYaw 0 -> -360.
            clips.Add(Strike(AnimationKeys.AirShield, bagua)
                .K(KeyPhase.Startup, 1f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0.2f).Hips(0f, -0.2f, 0f).Torso(4f, 6f);
                    s.Arm(L, 52f, 16f, 0.8f, 10f, -80f, 60f).Arm(R, 52f, 16f, 0.8f, 10f, -80f, 60f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f).Torso(0f, 0f);
                    s.Arm(L, 70f, 20f, 1f, 0f, -90f, 70f).Arm(R, 70f, 20f, 1f, 0f, -90f, 70f);
                })
                .K(KeyPhase.Interval, 0.5f, PoseEase.Linear, s => s.Set(PoseChannel.RootYaw, -160f).Arm(L, 72f, 14f, 1f, 0f, -90f, 70f).Arm(R, 72f, 14f, 1f, 0f, -90f, 70f))
                .K(KeyPhase.Interval, 1f, PoseEase.Linear, s => s.Set(PoseChannel.RootYaw, -300f).Arm(L, 75f, 22f, 1f, 0f, -90f, 70f).Arm(R, 75f, 22f, 1f, 0f, -90f, 70f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.RootYaw, -360f);
                    s.Arm(L, 30f, 10f, 0.7f, 10f, -40f, 40f).Arm(R, 30f, 10f, 0.7f, 10f, -40f, 40f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, s => { s.CopyFrom(bagua); s.Set(PoseChannel.RootYaw, -360f); })
                .Build());

            // Air Blast (RB): the rear palm draws back to the hip, then snaps out at full length; the blast flies on the
            //    first frame of full extension.
            clips.Add(Strike(AnimationKeys.AirBlast, bagua)
                .K(KeyPhase.Startup, 0.55f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Pelvis(0f, 40f).Torso(0f, 36f).Hips(0f, -0.18f, -0.04f);
                    s.Arm(R, 20f, -40f, 0.42f, 10f, 0f, 70f).Arm(L, -10f, 16f, 0.75f, 10f, -20f, 50f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.16f, 0.1f).Pelvis(0f, 0f).Torso(8f, -14f);
                    s.Arm(R, -8f, 6f, 1f, 0f, -80f, 85f).Arm(L, 20f, -30f, 0.6f, 10f, 0f, 40f);
                })
                .K(KeyPhase.Recovery, 0.12f, PoseEase.Linear, s => s.Torso(9f, -15f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(R, -10f, 6f, 0.8f, 0f, -50f, 60f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, bagua)
                .Build());

            // Wind Leap Strike (zip): a gust-assisted leap (a jump, not flight): the knees tuck as you sail across, then the
            //    right leg fires out in a flying front kick.
            clips.Add(Strike(AnimationKeys.WindLeap, bagua)
                .K(KeyPhase.Startup, 0.3f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0.3f);
                    s.Hips(0f, 0.02f, 0f).Pelvis(4f, 10f).Torso(14f, 0f).Head(-12f, 0f);
                    s.Kick(L, 5f, -40f, 0.5f, 0f, 30f).Kick(R, 10f, -60f, 0.5f, 0f, 35f);
                    s.Arm(L, 50f, 20f, 0.9f, 10f).Arm(R, 50f, 20f, 0.9f, 10f);
                })
                .K(KeyPhase.Startup, 0.85f, PoseEase.InOut, s =>
                {
                    s.Torso(-4f, 0f).Head(0f, 0f).Kick(R, 0f, -8f, 0.46f, 0f, -10f).Kick(L, 6f, -60f, 0.55f, 0f, 30f);
                    s.Arm(L, -8f, 20f, 0.7f, 10f, -20f, 50f).Arm(R, 30f, -20f, 0.7f, 10f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, 0.06f, 0f).Torso(-14f, -4f);
                    s.Kick(R, -3f, 12f, 1f, 0f, -25f).Kick(L, 6f, -45f, 0.5f, 0f, 30f);
                    s.Arm(L, -8f, 16f, 0.75f, 5f, -20f, 50f).Arm(R, 40f, -30f, 0.9f, 10f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Kick(R, -3f, 10f, 1f, 0f, -25f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s =>
                {
                    s.Hips(0f, -0.2f, 0f).Torso(12f, 10f);
                    s.Foot(R, 0.12f, Ground, 0.3f, 10f).Foot(L, 0.12f, Ground, -0.24f, 30f);
                    s.Arm(L, -4f, 20f, 0.8f, 10f, -20f, 50f).Arm(R, -30f, 4f, 0.5f, 10f, 40f, 50f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, bagua)
                .Build());

            // Wind Runner Kick (sprint attack): out of the run, a light skipping take-off into a high front kick, the arms
            //    swept back like wings.
            clips.Add(Strike(AnimationKeys.WindRunnerKick, bagua)
                .K(KeyPhase.Startup, 0.5f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0.2f);
                    s.Hips(0f, 0.04f, 0f).Pelvis(0f, 0f).Torso(8f, 0f);
                    s.Kick(L, 0f, -14f, 0.45f, 0f, 20f).Kick(R, 5f, -100f, 0.95f, 0f, 40f);
                    s.Arm(L, 40f, -20f, 0.9f, 10f).Arm(R, 40f, -20f, 0.9f, 10f);
                })
                .K(KeyPhase.Startup, 0.8f, PoseEase.InOut, s => s.Kick(R, 0f, -6f, 0.48f, 0f, -10f).Kick(L, 6f, -60f, 0.6f, 0f, 30f).Torso(-6f, 0f))
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, 0.08f, 0f).Torso(-16f, 0f);
                    s.Kick(R, -3f, 20f, 1f, 0f, -25f).Kick(L, 6f, -45f, 0.5f, 0f, 30f);
                    s.Arm(L, 50f, -40f, 0.95f, 10f).Arm(R, 50f, -40f, 0.95f, 10f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Kick(R, -3f, 18f, 1f, 0f, -25f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s =>
                {
                    s.Hips(0f, -0.2f, 0f).Torso(12f, 10f);
                    s.Foot(R, 0.12f, Ground, 0.32f, 10f).Foot(L, 0.13f, Ground, -0.24f, 30f);
                    s.Arm(L, -4f, 20f, 0.8f, 10f, -20f, 50f).Arm(R, -30f, 4f, 0.5f, 10f, 40f, 50f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, bagua)
                .Build());
        }
    }
}
