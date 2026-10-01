using System.Collections.Generic;

namespace VaatusRevenge.Core
{
    // Earth (Hung Gar). Everything comes from the rooted horse stance (sei ping ma): feet wide and parallel, thighs low,
    // back straight, both fists chambered at the waist ("earth:idle"). Strikes are short, heavy and driven by the legs and
    // waist without the feet leaving the floor; the hands are fists, tiger claws and the butterfly's paired palms. Earth's
    // air string is pure martial strikes (dust only, no rock: canon), and its plunge lands with both fists in the ground.
    // Clips follow the same rules as Fire's: anticipation in Startup, full extension snapped in on Active 0, then back to
    // the horse stance in Recovery.
    public static partial class PoseLibraryDefaults
    {
        public const string EarthStyle = "earth";

        // Hung Gar's horse stance with both fists chambered at the waist, palms up, eyes on the foe.
        public static PoseSpec EarthStance()
        {
            var s = new PoseSpec();
            s.Hips(0f, -0.24f, 0f).Pelvis(0f, 12f).Torso(2f, 6f).Head(0f, 0f);
            s.Set(PoseChannel.LookFront, 1f).Set(PoseChannel.ArmFollow, 0.5f);
            ChamberFists(s);
            s.Foot(L, 0.32f, Ground, 0.06f, 15f, 0f, 10f).Foot(R, 0.32f, Ground, -0.06f, 20f, 0f, 10f);
            return s;
        }

        // Both fists drawn back to the waist, palms up: Hung Gar's chamber.
        static void ChamberFists(PoseSpec s)
        {
            s.Arm(L, 24f, -80f, 0.62f, -14f, 90f).Arm(R, 24f, -80f, 0.62f, -14f, 90f);
        }

        static void AddEarth(List<PoseClip> clips)
        {
            PoseSpec earth = EarthStance();
            AddEarthStates(clips, earth);

            // 1. Horse Stance Punch: the lead fist drives straight out from the chamber, the waist turning behind it, the rear fist
            //    staying chambered; the feet never move.
            clips.Add(Strike(AnimationKeys.HorsePunch, earth)
                .K(KeyPhase.Startup, 0.45f, PoseEase.InOut, s =>
                {
                    s.Pelvis(0f, 22f).Torso(2f, 16f).Hips(0f, -0.26f, -0.02f);
                    s.Arm(L, 22f, -78f, 0.56f, -14f, 90f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Pelvis(0f, 4f).Torso(4f, 20f).Hips(0f, -0.25f, 0.06f);
                    s.Arm(L, -6f, 0f, 1f, 0f, 0f).Arm(R, 24f, -80f, 0.62f, -14f, 90f);
                    s.Foot(L, 0.3f, Ground, 0.14f, 10f, 0f, 10f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(4f, 22f))
                .K(KeyPhase.Recovery, 0.35f, PoseEase.Out, s => s.Arm(L, -6f, -10f, 0.6f, 0f, 40f).Torso(3f, 12f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, earth)
                .Build());

            // 2. Tiger Claw Rake: the rear hand rises by the ear in a claw, then rakes forward and down across the foe as
            //    the waist turns the other way.
            clips.Add(Strike(AnimationKeys.TigerClaw, earth)
                .K(KeyPhase.Startup, 0.5f, PoseEase.InOut, s =>
                {
                    s.Pelvis(0f, 30f).Torso(-2f, 24f);
                    s.Arm(R, 30f, 40f, 0.45f, 30f, 0f, 60f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Pelvis(0f, -8f).Torso(8f, -24f).Hips(0f, -0.26f, 0.08f);
                    s.Arm(R, -10f, 10f, 1f, 0f, 0f, 60f).Arm(L, -30f, 20f, 0.45f, 10f, 0f, 50f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Arm(R, -24f, -22f, 1f, 0f, 0f, 60f).Torso(12f, -28f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(R, -20f, -40f, 0.7f, 0f, 30f, 30f).Torso(8f, -16f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, earth)
                .Build());

            // 3. Rooted Stomp: the rear knee lifts high, then the heel stamps out low and forward into the foe's shin, and a
            //    crack runs out along the ground. Rooted from the moment the knee is up.
            clips.Add(Strike(AnimationKeys.StompLine, earth)
                .K(KeyPhase.Startup, 0.55f, PoseEase.InOut, s =>
                {
                    s.Hips(0f, -0.14f, 0f).Pelvis(0f, 6f).Torso(-2f, 4f);
                    s.Kick(R, 4f, -5f, 0.45f, 0f, -10f);
                    s.Foot(L, 0.18f, Ground, 0.08f, 10f, 0f, 10f, 1f, 1f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.18f, 0.02f).Torso(6f, 2f);
                    s.Kick(R, 2f, -52f, 1f, 0f, -25f);
                    s.Arm(L, -6f, 6f, 0.62f, 5f, 0f, 50f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Kick(R, 2f, -56f, 1f, 0f, -25f))
                .K(KeyPhase.Recovery, 0.35f, PoseEase.Out, s =>
                {
                    s.Hips(0f, -0.22f, 0.04f);
                    s.Foot(R, 0.22f, Ground, 0.3f, 15f, 0f, 10f);   // the stamp lands and plants
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, earth)
                .Build());

            // 4. Butterfly Palms: both hands chamber at the right hip, then the paired palms (one high, one low, wrists
            //    touching like a butterfly's wings) drive out together. Rooted.
            clips.Add(Strike(AnimationKeys.ButterflyPalms, earth)
                .K(KeyPhase.Startup, 0.55f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Pelvis(0f, 34f).Torso(0f, 28f).Hips(0f, -0.27f, -0.03f);
                    s.Arm(L, -40f, -40f, 0.45f, -10f, 0f, 70f).Arm(R, 10f, -55f, 0.42f, -10f, 0f, 70f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Pelvis(0f, 0f).Torso(8f, -4f).Hips(0f, -0.25f, 0.1f);
                    s.Arm(L, -8f, 10f, 1f, 0f, -80f, 80f).Arm(R, -8f, -10f, 1f, 0f, -80f, 80f);
                    s.Foot(L, 0.3f, Ground, 0.16f, 10f, 0f, 10f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(9f, -4f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(L, -10f, 6f, 0.75f, 0f, -40f, 40f).Arm(R, -10f, -8f, 0.75f, 0f, -40f, 40f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, earth)
                .Build());

            // 5. Mountain Quake: both fists rise overhead as the body rises, then the stance drops to its lowest and both
            //    fists hammer straight down into the earth: the ground shakes all round.
            clips.Add(Strike(AnimationKeys.QuakeSlam, earth)
                .K(KeyPhase.Startup, 0.6f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0.6f);
                    s.Hips(0f, -0.08f, 0f).Pelvis(0f, 4f).Torso(-10f, 0f).Head(-6f, 0f);
                    s.Arm(L, 10f, 80f, 0.62f, 20f, 0f).Arm(R, 10f, 80f, 0.62f, 20f, 0f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.38f, 0.02f).Pelvis(12f, 0f).Torso(34f, 0f).Head(-20f, 0f);
                    s.Arm(L, 8f, -66f, 1f, 0f, 0f).Arm(R, 8f, -66f, 1f, 0f, 0f);
                    s.Foot(L, 0.36f, Ground, 0.08f, 15f, 0f, 15f).Foot(R, 0.36f, Ground, -0.08f, 20f, 0f, 15f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(36f, 0f))
                .K(KeyPhase.Recovery, 0.35f, PoseEase.Out, s => s.Hips(0f, -0.34f, 0.02f).Torso(28f, 0f).Arm(L, 10f, -60f, 0.85f, 0f, 0f).Arm(R, 10f, -60f, 0.85f, 0f, 0f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, earth)
                .Build());

            // Raise the Boulder (pause 1): sink with both palms pressing down at the floor, then heave them up and out as the
            //    legs drive up, lifting the rock (and the foe) out of the ground.
            clips.Add(Strike(AnimationKeys.BoulderRaise, earth)
                .K(KeyPhase.Startup, 0.55f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.34f, 0f).Pelvis(6f, 6f).Torso(20f, 0f);
                    s.Arm(L, 10f, -70f, 0.75f, 0f, 0f, -60f).Arm(R, 10f, -70f, 0.75f, 0f, 0f, -60f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.16f, 0.02f).Pelvis(0f, 4f).Torso(-6f, 0f).Head(-8f, 0f);
                    s.Arm(L, 12f, 44f, 1f, 0f, -90f, 70f).Arm(R, 12f, 44f, 1f, 0f, -90f, 70f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Arm(L, 12f, 50f, 1f, 0f, -90f, 70f).Arm(R, 12f, 50f, 1f, 0f, -90f, 70f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(L, 0f, 30f, 0.6f, 10f, -90f, 60f).Arm(R, 0f, 30f, 0.6f, 10f, -90f, 60f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, earth)
                .Build());

            // Boulder Hurl (pause 2): the held boulder goes back over the right shoulder, then both palms drive it out; it
            //    flies on the first frame of full extension.
            clips.Add(Strike(AnimationKeys.BoulderHurl, earth)
                .K(KeyPhase.Startup, 0.55f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.24f, -0.06f).Pelvis(0f, 40f).Torso(-6f, 36f);
                    s.Arm(L, -30f, 40f, 0.5f, 20f, -90f, 60f).Arm(R, 30f, 45f, 0.45f, 20f, -90f, 60f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.24f, 0.1f).Pelvis(0f, 0f).Torso(10f, -6f);
                    s.Arm(L, -10f, 12f, 1f, 0f, -80f, 80f).Arm(R, -10f, 12f, 1f, 0f, -80f, 80f);
                    s.Foot(L, 0.3f, Ground, 0.18f, 10f, 0f, 10f);
                })
                .K(KeyPhase.Recovery, 0.12f, PoseEase.Linear, s => s.Torso(11f, -6f))   // zero active frames: hold the push a moment
                .K(KeyPhase.Recovery, 0.45f, PoseEase.Out, s => s.Arm(L, -10f, 6f, 0.8f, 0f, -60f, 60f).Arm(R, -10f, 6f, 0.8f, 0f, -60f, 60f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, earth)
                .Build());

            // Pivot Elbow (dodge strike): pivot on the lead foot with the rear elbow leading round, then the forearm snaps
            //    out into a back-fist across the foe.
            clips.Add(Strike(AnimationKeys.PivotElbow, earth)
                .K(KeyPhase.Startup, 0.55f, PoseEase.In, s =>
                {
                    s.Set(PoseChannel.RootYaw, -28f).Set(PoseChannel.ArmFollow, 0.4f);
                    s.Hips(0f, -0.23f, 0f).Torso(4f, -6f);
                    s.Arm(R, -70f, 12f, 0.35f, 60f, 0f).Arm(L, -30f, 20f, 0.45f, 10f, 0f, 50f);
                    s.Foot(L, 0.26f, Ground, 0.1f, 10f, 0f, 10f, 1f, 1f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.RootYaw, 0f).Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.24f, 0.06f).Torso(6f, 10f);
                    s.Arm(R, -12f, 6f, 1f, 0f, -90f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Arm(R, 30f, 6f, 1f, 0f, -90f).Torso(6f, 24f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(R, 30f, -20f, 0.6f, 0f, 0f).Torso(4f, 14f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, earth)
                .Build());

            AddEarthAir(clips, earth);
            AddEarthAbilities(clips, earth);
        }

        // Earth's stance, charge, block and parry (style overrides while Earth is active).
        static void AddEarthStates(List<PoseClip> clips, PoseSpec earth)
        {
            // Idle: rooted breathing. The hips settle a little lower on each out-breath, the fists stay chambered; nothing
            // else moves. The arms hardly swing when walking.
            clips.Add(new ClipBuilder(EarthStyle + ":" + AnimationKeys.Idle, ClipMode.Loop, earth) { LoopPeriod = 3.8f, ArmSwing = 0.25f }
                .K(KeyPhase.Cycle, 0.5f, PoseEase.InOut, s =>
                {
                    s.Hips(0f, -0.26f, 0f).Torso(4f, 6f).Head(-1f, 0f);
                    s.Shrug(L, 2f).Shrug(R, 2f);
                })
                .Build());

            // Charge (Mountain Fa Jin): sink into the deepest horse, the striking fist chambered tight, the lead palm open
            // in front, the whole body coiling down into the ground.
            clips.Add(new ClipBuilder(EarthStyle + ":" + AnimationKeys.Charge, ClipMode.Hold, earth) { FadeIn = 0.06f }
                .K(KeyPhase.Seconds, 0.25f, PoseEase.InOut, EarthCoil)
                .K(KeyPhase.Seconds, 1.2f, PoseEase.InOut, s => s.Hips(0f, -0.36f, -0.02f).Torso(-2f, 22f))
                .Build());

            // Block: the iron bridge. Both forearms rise in front of the face like a wall, elbows down, fists clenched.
            clips.Add(new ClipBuilder(EarthStyle + ":" + AnimationKeys.Block, ClipMode.Hold, earth) { FadeIn = 0.06f, UpperBodyOnly = true }
                .K(KeyPhase.Seconds, 0.08f, PoseEase.Snap, s =>
                {
                    s.Torso(10f, 4f).Head(10f, 0f).Set(PoseChannel.ArmFollow, 0.7f);
                    s.Arm(L, -20f, 46f, 0.46f, 20f, 90f).Arm(R, -24f, 42f, 0.44f, 20f, 90f);
                })
                .Build());

            // Parry (the deflect window as LB goes down): the lead forearm snaps out and up to bat the strike aside.
            clips.Add(new ClipBuilder(EarthStyle + ":" + AnimationKeys.Parry, ClipMode.Hold, earth) { FadeIn = 0.05f, UpperBodyOnly = true }
                .K(KeyPhase.Seconds, 0.05f, PoseEase.Snap, s =>
                {
                    s.Torso(8f, 14f).Set(PoseChannel.ArmFollow, 0.6f);
                    s.Arm(L, 10f, 40f, 0.62f, 30f, 90f).Arm(R, -24f, 36f, 0.42f, 20f, 90f);
                })
                .K(KeyPhase.Seconds, 0.2f, PoseEase.InOut, s => s.Arm(L, -14f, 44f, 0.5f, 20f, 90f).Torso(10f, 6f))
                .Build());
        }

        static void EarthCoil(PoseSpec s)
        {
            s.Set(PoseChannel.ArmFollow, 0.5f);
            s.Hips(0f, -0.33f, -0.02f).Pelvis(0f, 22f).Torso(-2f, 18f);
            s.Arm(R, 18f, -72f, 0.4f, -10f, 90f).Arm(L, -12f, 8f, 0.6f, 10f, 0f, 60f);
        }

        // The launcher and the air string (pure Hung Gar strikes: dust only).
        static void AddEarthAir(List<PoseClip> clips, PoseSpec earth)
        {
            PoseSpec air = AirGuard();

            // Rising Pillar (launcher): drop deep, then a rooted uppercut drives straight up as the legs extend and the
            //    column of rock throws the foe.
            clips.Add(Strike(AnimationKeys.PillarUppercut, earth)
                .K(KeyPhase.Startup, 0.65f, PoseEase.In, s =>
                {
                    s.Hips(0f, -0.36f, 0f).Pelvis(6f, 20f).Torso(16f, 16f);
                    s.Arm(R, 20f, -70f, 0.42f, -10f, 90f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f).Set(PoseChannel.LookFront, 0.6f);
                    s.Hips(0f, -0.04f, 0.02f).Pelvis(0f, 0f).Torso(-8f, -12f).Head(-12f, 0f);
                    s.Arm(R, -6f, 70f, 1f, 0f, 90f).Arm(L, 24f, -80f, 0.62f, -14f, 90f);
                    s.Foot(L, 0.26f, Ground, 0.06f, 15f).Foot(R, 0.26f, Ground + 0.03f, -0.06f, 20f, 25f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Arm(R, -6f, 78f, 1f, 0f, 90f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s =>
                {
                    s.Kick(R, 6f, -30f, 0.52f, 0f, 20f).Kick(L, 6f, -62f, 0.58f, 0f, 25f).Set(PoseChannel.LookFront, 1f);
                    s.Arm(R, -10f, 20f, 0.45f, 10f, 90f).Arm(L, -10f, 10f, 0.5f, 10f, 90f).Set(PoseChannel.ArmFollow, 0.5f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, air)
                .Build());

            // Hammer Fist (air 1): the fist rises over the shoulder and chops down onto the foe.
            clips.Add(Strike(AnimationKeys.AirHammer, air)
                .K(KeyPhase.Startup, 0.5f, PoseEase.InOut, s => s.Torso(-8f, 10f).Arm(R, 15f, 75f, 0.5f, 20f, 0f))
                .K(KeyPhase.Startup, 0.8f, PoseEase.In, s => s.Torso(0f, 4f).Arm(R, 6f, 40f, 0.66f, 15f, 40f))
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f).Torso(14f, -6f);
                    s.Arm(R, -6f, -18f, 1f, 0f, 90f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Arm(R, -6f, -24f, 1f, 0f, 90f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, air)
                .Build());

            // Tiger Tail Kick (air 2): turn the back on the foe and thrust the right heel straight back into it, looking over
            //    the shoulder; turn back round.
            clips.Add(Strike(AnimationKeys.AirBackKick, air)
                .K(KeyPhase.Startup, 0.55f, PoseEase.In, s =>
                {
                    s.Set(PoseChannel.RootYaw, 130f).Set(PoseChannel.LookFront, 0.7f).Torso(14f, 0f);
                    s.Kick(R, 170f, -40f, 0.45f, 0f, -10f).Kick(L, 6f, -70f, 0.6f, 0f, 30f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.RootYaw, 180f).Torso(34f, 0f).Pelvis(12f, 0f);
                    s.Kick(R, 180f, 2f, 1f, 0f, -30f);
                    s.Arm(L, 30f, -10f, 0.8f, 10f).Arm(R, 20f, -20f, 0.8f, 10f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Kick(R, 180f, 4f, 1f, 0f, -30f))
                .K(KeyPhase.Recovery, 0.5f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.RootYaw, 270f).Torso(10f, 0f).Pelvis(0f, 10f).Kick(R, 120f, -50f, 0.55f, 0f, 10f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, s => { s.CopyFrom(air); s.Set(PoseChannel.RootYaw, 360f); })
                .Build());

            // Meteor Drop (air 3): both fists locked together overhead, then the whole body folds and hammers the foe down.
            clips.Add(Strike(AnimationKeys.AirMeteor, air)
                .K(KeyPhase.Startup, 0.55f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0.6f).Torso(-14f, 0f).Head(-8f, 0f);
                    s.Arm(L, 10f, 85f, 0.6f, 20f, 0f).Arm(R, 10f, 85f, 0.6f, 20f, 0f);
                    s.Kick(L, 5f, -40f, 0.55f, 0f, 20f).Kick(R, 5f, -55f, 0.6f, 0f, 25f);
                })
                .K(KeyPhase.Startup, 0.8f, PoseEase.In, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0.2f).Torso(8f, 0f).Head(-14f, 0f);
                    s.Arm(L, 8f, 40f, 0.72f, 15f, 45f).Arm(R, 8f, 40f, 0.72f, 15f, 45f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f).Torso(36f, 0f).Pelvis(14f, 0f).Head(-24f, 0f);
                    s.Arm(L, 4f, -48f, 1f, 0f, 90f).Arm(R, 4f, -48f, 1f, 0f, 90f);
                    s.Kick(L, 5f, -80f, 0.7f, 0f, 30f).Kick(R, 5f, -95f, 0.8f, 0f, 35f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(38f, 0f))
                .K(KeyPhase.Recovery, 0.5f, PoseEase.Out, s => s.Torso(14f, 0f).Pelvis(0f, 10f).Arm(L, 10f, -20f, 0.6f, 10f, 0f).Arm(R, 10f, -20f, 0.6f, 10f, 0f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, air)
                .Build());

            // Earthquake Drop (plunge): crouched with both fists raised through the hang, drop like a stone, then land in the
            //    deepest horse with both fists punching into the ground; rise back into the stance.
            clips.Add(new ClipBuilder(AnimationKeys.QuakeDrop, ClipMode.Action, air) { FadeIn = 0.06f }
                .K(KeyPhase.Startup, 1f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0.5f).Torso(-10f, 0f);
                    s.Arm(L, 15f, 75f, 0.7f, 20f, 0f).Arm(R, 15f, 75f, 0.7f, 20f, 0f);
                    s.Kick(L, 20f, -70f, 0.6f, 20f, 30f).Kick(R, 20f, -75f, 0.6f, 20f, 30f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(-6f, 0f))
                .K(KeyPhase.Recovery, 0.22f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.42f, 0.02f).Pelvis(14f, 0f).Torso(34f, 0f).Head(-20f, 0f);
                    s.Arm(L, 8f, -68f, 1f, 0f, 0f).Arm(R, 8f, -68f, 1f, 0f, 0f);
                    s.Foot(L, 0.38f, Ground, 0.08f, 15f, 0f, 15f).Foot(R, 0.38f, Ground, -0.08f, 20f, 0f, 15f);
                })
                .K(KeyPhase.Recovery, 0.5f, PoseEase.Out, s => s.Hips(0f, -0.38f, 0.02f).Torso(28f, 0f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, earth)
                .Build());
        }

        // The charged heavy, the abilities, the ranged skill, the zip strike and the sprint attack.
        static void AddEarthAbilities(List<PoseClip> clips, PoseSpec earth)
        {
            // Mountain Fa Jin (heavy, released from the charge): out of the deepest horse, the hips snap round and the
            //    rooted rear fist drives straight out; the rear foot pivots, the shock runs along the ground.
            PoseSpec coil = earth.Clone();
            EarthCoil(coil);
            clips.Add(new ClipBuilder(AnimationKeys.RootFaJin, ClipMode.Action, coil)
                .K(KeyPhase.Startup, 0.7f, PoseEase.In, s => s.Hips(0f, -0.36f, -0.03f).Pelvis(0f, 28f).Torso(-3f, 24f))
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.3f, 0.1f).Pelvis(0f, -14f).Torso(8f, -24f);
                    s.Arm(R, -7f, 0f, 1f, 0f, 0f).Arm(L, 24f, -80f, 0.62f, -14f, 90f);
                    s.Foot(L, 0.3f, Ground, 0.22f, 10f, 0f, 10f).Foot(R, 0.32f, Ground, -0.08f, 20f, 15f, 5f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(9f, -26f))
                .K(KeyPhase.Recovery, 0.3f, PoseEase.Out, s => s.Arm(R, -10f, -5f, 0.8f, 0f, 30f).Hips(0f, -0.28f, 0.06f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, earth)
                .Build());

            // Stone Spike Line (LB + Y): stamp the lead foot and drive the fist down and forward along the ground: the spikes
            //    run out from the knuckles.
            clips.Add(Strike(AnimationKeys.SpikeLine, earth)
                .K(KeyPhase.Startup, 0.6f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.18f, -0.04f).Pelvis(0f, 26f).Torso(-4f, 20f);
                    s.Arm(R, 20f, 30f, 0.45f, 20f, 0f).Foot(L, 0.3f, 0.16f, 0.1f, 15f, 15f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.32f, 0.08f).Pelvis(4f, -6f).Torso(24f, -16f);
                    s.Arm(R, -6f, -34f, 1f, 0f, 0f).Arm(L, 24f, -80f, 0.62f, -14f, 90f);
                    s.Foot(L, 0.32f, Ground, 0.14f, 15f, 0f, 10f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(25f, -16f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(R, 0f, -40f, 0.75f, 0f, 30f).Torso(16f, -10f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, earth)
                .Build());

            // Stone Tent (LB + B): crouch with the palms pressing at the floor, then heave both arms up and out overhead as
            //    the tent of rock bursts up round you.
            clips.Add(Strike(AnimationKeys.StoneTent, earth)
                .K(KeyPhase.Startup, 0.6f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.38f, 0f).Pelvis(8f, 4f).Torso(22f, 0f);
                    s.Arm(L, 25f, -70f, 0.8f, 0f, 0f, -60f).Arm(R, 25f, -70f, 0.8f, 0f, 0f, -60f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.2f, 0f).Pelvis(0f, 4f).Torso(-6f, 0f).Head(-10f, 0f);
                    s.Arm(L, 40f, 66f, 1f, 0f, -90f, 70f).Arm(R, 40f, 66f, 1f, 0f, -90f, 70f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Arm(L, 42f, 70f, 1f, 0f, -90f, 70f).Arm(R, 42f, 70f, 1f, 0f, -90f, 70f))
                .K(KeyPhase.Recovery, 0.45f, PoseEase.Out, s => s.Arm(L, 30f, 20f, 0.7f, 10f, 0f, 40f).Arm(R, 30f, 20f, 0.7f, 10f, 0f, 40f).Head(0f, 0f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, earth)
                .Build());

            // Boulder Toss (RB): scoop a boulder up out of the ground and push it away with both palms; it flies on the first
            //    frame of full extension.
            clips.Add(Strike(AnimationKeys.BoulderToss, earth)
                .K(KeyPhase.Startup, 0.4f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.36f, -0.02f).Pelvis(8f, 10f).Torso(22f, 6f);
                    s.Arm(L, 20f, -65f, 0.8f, 0f, 90f, 40f).Arm(R, 20f, -65f, 0.8f, 0f, 90f, 40f);
                })
                .K(KeyPhase.Startup, 0.8f, PoseEase.InOut, s =>
                {
                    s.Hips(0f, -0.24f, -0.04f).Pelvis(0f, 20f).Torso(-2f, 16f);
                    s.Arm(L, -30f, 20f, 0.45f, 10f, -90f, 60f).Arm(R, -30f, 20f, 0.45f, 10f, -90f, 60f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.24f, 0.1f).Pelvis(0f, 0f).Torso(10f, -4f);
                    s.Arm(L, -10f, 10f, 1f, 0f, -80f, 80f).Arm(R, -10f, 10f, 1f, 0f, -80f, 80f);
                    s.Foot(L, 0.3f, Ground, 0.18f, 10f, 0f, 10f);
                })
                .K(KeyPhase.Recovery, 0.12f, PoseEase.Linear, s => s.Torso(11f, -4f))
                .K(KeyPhase.Recovery, 0.45f, PoseEase.Out, s => s.Arm(L, -10f, 6f, 0.8f, 0f, -60f, 60f).Arm(R, -10f, 6f, 0.8f, 0f, -60f, 60f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, earth)
                .Build());

            // Earth Surf Charge (zip): ride a wave of earth across the ground, crouched side-on with the arms out, then
            //    square up and land a rooted punch.
            var earthSurf = Strike(AnimationKeys.EarthSurf, earth);
            earthSurf.Glides = true;
            clips.Add(earthSurf
                .K(KeyPhase.Startup, 0.3f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0.3f);
                    s.Hips(0f, -0.3f, 0f).Pelvis(0f, 75f).Torso(12f, -45f);
                    s.Foot(L, 0.26f, Ground + 0.04f, 0.2f, 65f, 0f, 10f).Foot(R, 0.26f, Ground + 0.04f, -0.24f, 75f, 0f, 10f);
                    s.Arm(L, 55f, 0f, 0.85f, 10f, 0f).Arm(R, 65f, -10f, 0.85f, 10f, 0f);
                })
                .K(KeyPhase.Startup, 0.85f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f).Pelvis(0f, 30f).Torso(6f, 20f);
                    s.Arm(R, 18f, -68f, 0.42f, -10f, 90f).Arm(L, -12f, 10f, 0.6f, 10f, 0f, 60f);
                    s.Foot(L, 0.28f, Ground + 0.02f, 0.14f, 30f, 0f, 10f).Foot(R, 0.3f, Ground + 0.02f, -0.12f, 40f, 0f, 10f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.26f, 0.1f).Pelvis(0f, -10f).Torso(8f, -20f);
                    s.Arm(R, -7f, 0f, 1f, 0f, 0f).Arm(L, 24f, -80f, 0.62f, -14f, 90f);
                    s.Foot(L, 0.3f, Ground, 0.16f, 15f, 0f, 10f).Foot(R, 0.32f, Ground, -0.08f, 20f, 15f, 5f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(9f, -22f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(R, -10f, -5f, 0.75f, 0f, 30f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, earth)
                .Build());

            // Avalanche Shoulder (sprint attack): head down, the lead shoulder lowered and the lead palm driving out ahead of
            //    it, rooted through the impact.
            clips.Add(Strike(AnimationKeys.ShoulderCharge, earth)
                .K(KeyPhase.Startup, 0.5f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.16f, 0.04f).Pelvis(0f, 40f).Torso(22f, 30f).Head(-12f, 0f);
                    s.Arm(L, -40f, -20f, 0.45f, 10f, 0f, 50f).Arm(R, 10f, -50f, 0.5f, 0f, 90f);
                    s.Foot(L, 0.16f, 0.12f, 0.24f, 10f, 20f).Foot(R, 0.18f, Ground, -0.3f, 40f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.26f, 0.14f).Pelvis(0f, 50f).Torso(24f, 36f);
                    s.Arm(L, -6f, -12f, 1f, 0f, -80f, 80f);
                    s.Foot(L, 0.24f, Ground, 0.44f, 10f, 0f, 10f).Foot(R, 0.24f, Ground, -0.28f, 45f, 10f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(25f, 36f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Hips(0f, -0.24f, 0.08f).Torso(14f, 24f).Arm(L, -10f, -10f, 0.7f, 0f, -40f, 40f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, earth)
                .Build());
        }
    }
}
