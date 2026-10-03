using System;
using System.Collections.Generic;

namespace VaatusRevenge.Core
{
    // The built-in martial-arts animations, written as keyframes of PoseSpec dials (see PoseSpec for what each
    // dial means). Fire is Northern Shaolin: long, fully extended strikes driven by the hips from a bow stance
    // (gong bu: front knee bent, back leg straight and planted), high snapping kicks, spins, and fast footwork.
    //
    // How an attack is built (the frame data decides the timing, see KeyPhase):
    //   Startup: anticipation. The body coils the opposite way and the striking limb chambers (a fist pulled
    //            back, a knee raised), so the strike has somewhere to come from and the opponent can read it.
    //   Active 0: FULL EXTENSION exactly on the first frame the move can hit (Reach 1), eased in with Snap.
    //   Active 1: held (or sweeping through the arc) until the active frames end.
    //   Recovery: follow-through, then back to guard. The slow part of the recovery is the punish window.
    //
    // The fighter faces +Z; lengths are metres for a 1.8 m fighter; angles are degrees. Fire's stance leads with
    // the left side (left foot and fist forward), so the lead hand jabs and the rear hand crosses.
    public static partial class PoseLibraryDefaults
    {
        const BodySide L = BodySide.Left;
        const BodySide R = BodySide.Right;
        const float Ground = 0.08f;      // ankle height when the foot is flat on the floor

        public static PoseClip[] CreateClips()
        {
            var clips = new List<PoseClip>(160);
            AddStates(clips);
            AddPlayerMoves(clips);
            AddFireBranches(clips);
            AddDodges(clips);              // PoseLibraryDefaults.Dodge.cs
            AddWater(clips);               // PoseLibraryDefaults.Water.cs
            AddEarth(clips);               // PoseLibraryDefaults.Earth.cs
            AddAir(clips);                 // PoseLibraryDefaults.Air.cs
            AddEnemyMoves(clips);
            AddPlaceholders(clips);
            return clips.ToArray();
        }

        // Standing up straight, arms relaxed: used only when a clip is missing.
        public static PoseSpec Neutral()
        {
            var s = new PoseSpec();
            s.Hips(0f, -0.02f, 0f).Set(PoseChannel.LookFront, 1f).Set(PoseChannel.ArmFollow, 1f);
            s.Arm(L, 8f, -80f, 0.95f).Arm(R, 8f, -80f, 0.95f);
            s.Foot(L, 0.11f, Ground, 0f, 8f).Foot(R, 0.11f, Ground, 0f, 8f);
            return s;
        }

        // ------------------------------------------------------------------ stances

        // The Fire fighting stance: side-on, weight low and even, lead (left) hand up and forward on the centre
        // line, rear (right) hand guarding the chin.
        public static PoseSpec Guard()
        {
            var s = new PoseSpec();
            s.Hips(0f, -0.10f, 0f).Pelvis(0f, 25f).Torso(4f, 6f).Head(0f, 0f);
            s.Set(PoseChannel.LookFront, 1f).Set(PoseChannel.ArmFollow, 0.5f);
            s.Arm(L, -8f, 10f, 0.62f, 5f, 20f).Arm(R, -40f, 28f, 0.38f, 10f, 30f);
            s.Foot(L, 0.12f, Ground, 0.30f, 5f).Foot(R, 0.14f, Ground, -0.26f, 35f);
            return s;
        }

        // Guard in the air: knees drawn up, hands up.
        static PoseSpec AirGuard()
        {
            PoseSpec s = Guard();
            s.Hips(0f, 0f, 0f).Pelvis(0f, 15f).Torso(6f, 5f);
            s.Kick(L, 6f, -55f, 0.62f, 0f, 20f).Kick(R, 8f, -80f, 0.6f, 0f, 30f);
            return s;
        }

        // A dao soldier's guard: the same stance, sword held forward at the waist, blade angled up at the opponent.
        static PoseSpec SwordGuard()
        {
            PoseSpec s = Guard();
            s.Pelvis(0f, 20f).Torso(3f, 5f);
            s.Arm(R, -6f, -18f, 0.72f, 5f, 0f).Set(PoseChannel.Blade, 38f);
            s.Arm(L, -18f, 18f, 0.5f, 10f, 20f, 30f);
            return s;
        }

        // A crossbowman at ease: the weapon held across the body in both hands, pointing at the ground ahead.
        static PoseSpec CrossbowGuard()
        {
            PoseSpec s = Guard();
            // at ease: the crossbow hangs low at the right hip, pointing at the floor ahead
            s.Hips(0f, -0.05f, 0f).Pelvis(0f, 12f).Torso(2f, 3f);
            s.Arm(R, 12f, -68f, 0.82f, 10f, 0f).Arm(L, -25f, -45f, 0.7f, 10f, 0f);
            s.Set(PoseChannel.PropAim, 0f).Set(PoseChannel.Blade, 25f);
            return s;
        }

        // A sparring dummy: a stiff straw figure on its post, one arm holding the practice stick forward.
        static PoseSpec DummyGuard()
        {
            var s = new PoseSpec();
            s.Hips(0f, -0.01f, 0f).Set(PoseChannel.LookFront, 1f).Set(PoseChannel.ArmFollow, 1f);
            s.Arm(L, 35f, -45f, 0.95f).Arm(R, 10f, -20f, 0.9f).Set(PoseChannel.Blade, 60f);
            s.Foot(L, 0.1f, Ground, 0f, 0f).Foot(R, 0.1f, Ground, 0f, 0f);
            return s;
        }

        // Horse stance (ma bu): feet wide and parallel, thighs low. The root of fa jin power and the fire blast.
        static PoseSpec Horse(PoseSpec from)
        {
            PoseSpec s = from.Clone();
            s.Hips(0f, -0.25f, 0f).Pelvis(0f, 0f).Torso(-3f, 0f);
            s.Foot(L, 0.32f, Ground, 0.08f, 18f, 0f, 10f).Foot(R, 0.32f, Ground, -0.08f, 22f, 0f, 10f);
            return s;
        }

        // ------------------------------------------------------------------ locomotion and states

        static void AddStates(List<PoseClip> clips)
        {
            PoseSpec guard = Guard();

            // Idle: the guard breathing (chest rises, shoulders lift, hips settle) with a slow weight shift.
            clips.Add(new ClipBuilder(AnimationKeys.Idle, ClipMode.Loop, guard) { LoopPeriod = 3.2f }
                .K(KeyPhase.Cycle, 0.5f, PoseEase.InOut, s =>
                {
                    s.Hips(0.018f, -0.115f, -0.01f).Torso(6f, 8f).Head(-1.5f, 0f);
                    s.Shrug(L, 3f).Shrug(R, 3f);
                    s[PoseChannel.LArmReach] = 0.6f;
                    s[PoseChannel.RArmPitch] = 30f;
                })
                .Build());

            clips.Add(new ClipBuilder("sword:" + AnimationKeys.Idle, ClipMode.Loop, SwordGuard()) { LoopPeriod = 3.4f, ArmSwing = 0.25f }
                .K(KeyPhase.Cycle, 0.5f, PoseEase.InOut, s => s.Hips(0.015f, -0.115f, -0.01f).Torso(5f, 7f).Shrug(L, 2f).Shrug(R, 2f))
                .Build());
            clips.Add(new ClipBuilder("crossbow:" + AnimationKeys.Idle, ClipMode.Loop, CrossbowGuard()) { LoopPeriod = 3.6f, ArmSwing = 0.15f }
                .K(KeyPhase.Cycle, 0.5f, PoseEase.InOut, s => s.Hips(0.012f, -0.07f, 0f).Torso(3f, 6f).Shrug(L, 2f).Shrug(R, 2f))
                .Build());
            clips.Add(new ClipBuilder("dummy:" + AnimationKeys.Idle, ClipMode.Loop, DummyGuard()) { LoopPeriod = 4f, ArmSwing = 0f }
                .K(KeyPhase.Cycle, 0.5f, PoseEase.InOut, s => s.Torso(1f, 2f, 1f))
                .Build());
            // A dummy on its post rocks back and wobbles when hit; its feet never leave the post.
            clips.Add(new ClipBuilder("dummy:" + AnimationKeys.Hurt, ClipMode.Action, DummyGuard()) { DefaultDuration = 0.4f, StartupShare = 0.15f, ActiveShare = 0.35f, FadeIn = 0.06f }
                .K(KeyPhase.Startup, 1f, PoseEase.Snap, s => s.Torso(-12f, 8f, 4f).Head(-10f, 5f))
                .K(KeyPhase.Active, 1f, PoseEase.InOut, s => s.Torso(5f, -3f, -2f).Head(4f, -2f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, DummyGuard())
                .Build());
            clips.Add(new ClipBuilder("dummy:" + AnimationKeys.Stagger, ClipMode.Action, DummyGuard()) { DefaultDuration = 1f, StartupShare = 0.12f, ActiveShare = 0.66f, FadeIn = 0.06f }
                .K(KeyPhase.Startup, 1f, PoseEase.Snap, s => s.Torso(-20f, 10f, 6f).Head(-18f, 0f))
                .K(KeyPhase.Active, 0.35f, PoseEase.InOut, s => s.Torso(12f, -6f, -8f).Head(10f, 0f, -6f))
                .K(KeyPhase.Active, 0.7f, PoseEase.InOut, s => s.Torso(-6f, 4f, 5f).Head(-4f, 0f, 4f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, DummyGuard())
                .Build());

            // Jump: legs drive the take-off, then knees tuck up with the hands high (held while rising).
            PoseSpec push = guard.Clone();
            push.Hips(0f, 0f, 0f).Pelvis(0f, 10f).Torso(-2f, 5f);
            push.Kick(L, 5f, -92f, 0.97f, 0f, 45f).Kick(R, 5f, -95f, 0.97f, 0f, 45f);
            push.Arm(L, 15f, 45f, 0.8f, 10f).Arm(R, 5f, 40f, 0.75f, 10f);
            clips.Add(new ClipBuilder(AnimationKeys.Jump, ClipMode.Hold, push) { FadeIn = 0.06f }
                .K(KeyPhase.Seconds, 0.2f, PoseEase.Out, s =>
                {
                    s.Kick(L, 5f, -30f, 0.55f, 0f, 25f).Kick(R, 5f, -60f, 0.6f, 0f, 30f);
                    s.Arm(L, 20f, 15f, 0.68f, 15f, 20f).Arm(R, -20f, 25f, 0.45f, 15f, 30f).Torso(10f, 5f);
                })
                .Build());

            // Fall: legs reach for the ground, arms out for balance, a slow scramble.
            PoseSpec fall = guard.Clone();
            fall.Hips(0f, 0f, 0f).Pelvis(0f, 10f).Torso(2f, 5f);
            fall.Kick(L, 6f, -78f, 0.82f, 0f, 20f).Kick(R, 6f, -96f, 0.9f, 0f, 25f);
            fall.Arm(L, 45f, 18f, 0.8f, 20f).Arm(R, 40f, 10f, 0.8f, 20f);
            clips.Add(new ClipBuilder(AnimationKeys.Fall, ClipMode.Loop, fall) { LoopPeriod = 1.1f }
                .K(KeyPhase.Cycle, 0.5f, PoseEase.InOut, s =>
                {
                    s.Kick(L, 6f, -94f, 0.9f, 0f, 25f).Kick(R, 6f, -80f, 0.84f, 0f, 20f);
                    s[PoseChannel.LArmPitch] = 26f;
                    s[PoseChannel.RArmPitch] = 16f;
                })
                .Build());

            // Land: knees and hips absorb the impact, hands drop for balance, then back up to guard.
            PoseSpec land = guard.Clone();
            land.Hips(0f, -0.3f, 0f).Pelvis(0f, 15f).Torso(18f, 4f);
            land.Foot(L, 0.16f, Ground, 0.2f, 10f).Foot(R, 0.17f, Ground, -0.18f, 25f);
            land.Arm(L, 35f, -20f, 0.85f, 15f).Arm(R, 30f, -25f, 0.85f, 15f);
            clips.Add(new ClipBuilder(AnimationKeys.Land, ClipMode.Action, land) { DefaultDuration = 0.22f, StartupShare = 0.25f, ActiveShare = 0.25f, FadeIn = 0.06f }
                .K(KeyPhase.Active, 1f, PoseEase.Out, s => s.Hips(0f, -0.22f, 0f).Torso(12f, 5f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // Flame Step (dodge): a low dash, chest over the lead knee, arms trailing like a sprinter's start.
            clips.Add(new ClipBuilder(AnimationKeys.Dodge, ClipMode.Action, guard) { DefaultDuration = 0.34f, StartupShare = 0.18f, ActiveShare = 0.5f, FadeIn = 0.06f }
                .K(KeyPhase.Startup, 1f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.24f, 0.06f).Pelvis(0f, 5f).Torso(30f, 0f).Head(-20f, 0f);
                    // fire-propelled: both feet skim just off the floor for the dash (no skating on a planted foot); the
                    // leap lift raises them further at dash speed, so the clip keeps them low (round 7: a 4 m dash from a
                    // run hovered both feet over 0.2 m for 5 frames)
                    s.Foot(L, 0.13f, Ground + 0.01f, 0.38f, 5f, -10f).Foot(R, 0.15f, 0.1f, -0.4f, 20f, 35f);
                    s.Arm(L, 25f, -45f, 0.95f, 10f).Arm(R, 20f, -50f, 0.95f, 10f).Set(PoseChannel.ArmFollow, 0.2f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s =>
                {
                    s.Torso(22f, 0f).Hips(0f, -0.2f, 0.04f);
                    s.Foot(R, 0.15f, 0.12f, -0.35f, 20f, 40f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // Air dash: body stretched out flat behind a jet of flame from the feet, arms swept back. It stretches out over
            // the startup (eased, not snapped: MV-06) and folds back over a longer recovery, so no limb jumps half a metre
            // in a frame on the way in or out.
            PoseSpec air = AirGuard();
            clips.Add(new ClipBuilder(AnimationKeys.AirDash, ClipMode.Action, air) { DefaultDuration = 0.3f, StartupShare = 0.38f, ActiveShare = 0.17f, FadeIn = 0.06f }
                .K(KeyPhase.Startup, 1f, PoseEase.InOut, s =>
                {
                    s.Hips(0f, 0f, 0f).Pelvis(28f, 5f).Torso(40f, 0f).Head(-45f, 0f);
                    s.Kick(L, 4f, -140f, 0.95f, 0f, 55f).Kick(R, 6f, -130f, 0.92f, 0f, 55f);
                    s.Arm(L, 20f, -65f, 1f, 5f).Arm(R, 18f, -60f, 1f, 5f).Set(PoseChannel.ArmFollow, 0.8f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(36f, 0f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, air)
                .Build());

            // Backstep: a quick hop back, chin tucked, guard tight.
            clips.Add(new ClipBuilder(AnimationKeys.Backstep, ClipMode.Action, guard) { DefaultDuration = 0.36f, StartupShare = 0.3f, ActiveShare = 0.35f, FadeIn = 0.06f }
                .K(KeyPhase.Startup, 1f, PoseEase.Out, s =>
                {
                    s.Hips(0f, -0.04f, -0.06f).Torso(-8f, 6f).Head(8f, 0f);
                    s.Foot(L, 0.12f, 0.16f, 0.22f, 5f, 20f).Foot(R, 0.14f, 0.12f, -0.34f, 30f, 25f);
                    s.Arm(L, -15f, 18f, 0.5f, 10f, 20f).Arm(R, -40f, 30f, 0.36f, 10f, 30f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.In, s =>
                {
                    s.Hips(0f, -0.18f, 0f).Torso(8f, 6f);
                    s.Foot(L, 0.13f, Ground, 0.3f, 5f).Foot(R, 0.15f, Ground, -0.3f, 35f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // Parry (Fire has no block): the forearms snap across in front of the face, then stay ready.
            PoseSpec parry = guard.Clone();
            parry.Torso(7f, 4f).Head(6f, 0f).Set(PoseChannel.ArmFollow, 0.6f);
            parry.Arm(L, -48f, 36f, 0.48f, 55f, 60f).Arm(R, -45f, 30f, 0.45f, 55f, 60f);
            clips.Add(new ClipBuilder(AnimationKeys.Parry, ClipMode.Hold, guard) { FadeIn = 0.06f, UpperBodyOnly = true }
                .K(KeyPhase.Seconds, 0.06f, PoseEase.Snap, parry)
                .K(KeyPhase.Seconds, 0.2f, PoseEase.InOut, s =>
                {
                    s[PoseChannel.LArmPitch] = 30f;
                    s[PoseChannel.RArmPitch] = 25f;
                })
                .Build());

            // Parry success: the crossed arms sweep outward, throwing the attacker's weapon wide, and step in.
            clips.Add(new ClipBuilder(AnimationKeys.ParrySuccess, ClipMode.Action, parry) { DefaultDuration = 0.42f, StartupShare = 0.2f, ActiveShare = 0.3f, FadeIn = 0.06f }
                .K(KeyPhase.Startup, 1f, PoseEase.Snap, s =>
                {
                    s.Torso(-4f, 0f).Pelvis(0f, 12f).Hips(0f, -0.14f, 0.08f);
                    s.Arm(L, 58f, 25f, 0.88f, 20f, 0f, 60f).Arm(R, 55f, 22f, 0.86f, 20f, 0f, 60f).Set(PoseChannel.ArmFollow, 0.3f);
                    s.Foot(L, 0.12f, Ground, 0.42f, 5f).Foot(R, 0.14f, Ground, -0.26f, 35f, 0f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(-2f, 3f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // Block (elements that block): both forearms up like a wall, chin down behind them.
            clips.Add(new ClipBuilder(AnimationKeys.Block, ClipMode.Hold, guard) { FadeIn = 0.06f, UpperBodyOnly = true }
                .K(KeyPhase.Seconds, 0.08f, PoseEase.Snap, s =>
                {
                    s.Torso(12f, 2f).Head(12f, 0f).Set(PoseChannel.ArmFollow, 0.7f);
                    s.Arm(L, -22f, 48f, 0.45f, 30f, 80f).Arm(R, -25f, 44f, 0.43f, 30f, 80f);
                })
                .Build());

            // Hurt: a flinch. The body snaps back from the blow (the animator adds a spring push in the hit's real
            // direction on top), the hands come up, the rear foot catches the weight.
            clips.Add(new ClipBuilder(AnimationKeys.Hurt, ClipMode.Action, guard) { DefaultDuration = 0.4f, StartupShare = 0.15f, ActiveShare = 0.3f, FadeIn = 0.06f }
                .K(KeyPhase.Startup, 1f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.14f, -0.08f).Torso(-12f, 12f, 4f).Head(-18f, 10f);
                    s.Arm(L, 30f, 25f, 0.7f, 30f, 30f).Arm(R, 35f, 8f, 0.72f, 30f, 30f);
                    s.Foot(R, 0.16f, Ground, -0.4f, 35f);
                    s.Set(PoseChannel.LookFront, 0.5f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Out, s => s.Torso(-5f, 8f, 2f).Head(-6f, 4f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // Stagger: poise broken. Knocked back, arms flung, then a dazed, sagging sway before the guard returns.
            clips.Add(new ClipBuilder(AnimationKeys.Stagger, ClipMode.Action, guard) { DefaultDuration = 1f, StartupShare = 0.12f, ActiveShare = 0.66f, FadeIn = 0.06f }
                .K(KeyPhase.Startup, 1f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.1f, -0.12f).Torso(-22f, 10f).Head(-25f, 0f).Set(PoseChannel.LookFront, 0f);
                    s.Arm(L, 60f, 30f, 0.9f, 30f).Arm(R, 50f, 15f, 0.86f, 30f);
                    s.Foot(R, 0.16f, Ground, -0.46f, 35f).Foot(L, 0.12f, Ground, 0.26f, 5f, 20f);
                })
                .K(KeyPhase.Active, 0.35f, PoseEase.InOut, s =>
                {
                    s.Hips(-0.03f, -0.2f, -0.05f).Torso(20f, 6f, 8f).Head(28f, 0f, 10f);
                    s.Arm(L, 12f, -80f, 0.95f, 5f).Arm(R, 10f, -78f, 0.95f, 5f).Set(PoseChannel.ArmFollow, 0.9f);
                    s.Foot(L, 0.14f, Ground, 0.2f, 0f, 0f, -8f).Foot(R, 0.16f, Ground, -0.3f, 30f, 0f, 12f);
                })
                .K(KeyPhase.Active, 0.7f, PoseEase.InOut, s => s.Hips(0.04f, -0.18f, -0.05f).Torso(16f, 3f, -7f).Head(22f, -8f, -9f))
                .K(KeyPhase.Active, 1f, PoseEase.InOut, s => s.Hips(0f, -0.16f, -0.03f).Torso(14f, 5f, 3f).Head(15f, 0f, 3f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // Launched: thrown into the air, flung onto the back with the limbs flailing, rocking as it rises and falls,
            // and already lying flat by the time it comes down (so the landing is a bounce, never a flip). As the
            // body tips flat the pelvis drops toward the capsule's base, so it lands on the floor, not above it.
            PoseSpec tumble = guard.Clone();
            tumble.Set(PoseChannel.LegFrame, 1f).Set(PoseChannel.ArmFollow, 1f).Set(PoseChannel.LookFront, 0f);
            tumble.Hips(0f, 0f, 0f).Pelvis(-20f, 0f).Torso(-25f, 0f).Head(-25f, 0f);
            tumble.Arm(L, 30f, 55f, 0.9f, 20f).Arm(R, 35f, 45f, 0.9f, 20f);
            tumble.Kick(L, 8f, -115f, 0.9f, 0f, 40f).Kick(R, 8f, -90f, 0.85f, 0f, 40f);
            clips.Add(new ClipBuilder(AnimationKeys.Launched, ClipMode.Hold, tumble) { FadeIn = 0.08f }
                .K(KeyPhase.Seconds, 0.3f, PoseEase.Out, s =>
                {
                    s.Hips(0f, -0.3f, 0f).Pelvis(-95f, 10f).Torso(-15f, 5f).Head(-15f, 10f);
                    s.Kick(L, 20f, -60f, 0.7f, 0f, 30f).Kick(R, 10f, -100f, 0.92f, 0f, 45f);
                    s.Arm(L, 80f, 10f, 0.9f, 20f).Arm(R, 60f, -30f, 0.9f, 20f);
                })
                .K(KeyPhase.Seconds, 0.6f, PoseEase.InOut, s =>
                {
                    s.Hips(0f, -0.55f, 0f).Pelvis(-128f, -5f).Torso(-8f, -5f).Head(10f, -10f);
                    s.Kick(L, 5f, -95f, 0.9f, 0f, 40f).Kick(R, 25f, -55f, 0.7f, 0f, 30f);
                    s.Arm(L, 50f, -30f, 0.9f, 20f).Arm(R, 85f, 20f, 0.9f, 20f);
                })
                .K(KeyPhase.Seconds, 1.0f, PoseEase.InOut, s =>
                {
                    s.Hips(0f, -0.62f, 0f).Pelvis(-108f, 5f).Torso(-4f, 0f).Head(15f, 5f);
                    s.Kick(L, 15f, -70f, 0.8f, 0f, 30f).Kick(R, 8f, -100f, 0.9f, 0f, 35f);
                    s.Arm(L, 75f, 25f, 0.9f, 20f).Arm(R, 55f, -20f, 0.9f, 20f);
                })
                .K(KeyPhase.Seconds, 1.4f, PoseEase.InOut, s =>
                {
                    s.Pelvis(-122f, -4f).Head(8f, -5f);
                    s.Kick(L, 8f, -100f, 0.9f, 0f, 35f).Kick(R, 20f, -65f, 0.75f, 0f, 30f);
                    s.Arm(L, 55f, -25f, 0.9f, 20f).Arm(R, 80f, 25f, 0.9f, 20f);
                })
                .K(KeyPhase.Seconds, 1.9f, PoseEase.InOut, s => s.Pelvis(-106f, 0f).Head(12f, 0f))
                .Build());

            // Knockdown: flat on the back after a juggle, a bounce off the floor, then lying still.
            PoseSpec lying = guard.Clone();
            lying.Set(PoseChannel.LegFrame, 1f).Set(PoseChannel.ArmFollow, 1f).Set(PoseChannel.LookFront, 0f);
            lying.Hips(0f, -0.84f, 0f).Pelvis(-90f, 0f).Torso(-4f, 0f).Head(8f, 0f);
            lying.Arm(L, 70f, -15f, 0.92f, 10f).Arm(R, 62f, -25f, 0.9f, 10f);
            lying.Kick(L, 8f, -72f, 0.84f, 10f, 30f).Kick(R, 5f, -93f, 0.95f, 5f, 35f);
            PoseSpec bounce = lying.Clone();
            bounce.Hips(0f, -0.74f, 0f).Pelvis(-78f, 0f).Head(20f, 0f);
            bounce.Kick(L, 8f, -60f, 0.8f, 10f, 30f).Kick(R, 5f, -75f, 0.9f, 5f, 35f);
            clips.Add(new ClipBuilder(AnimationKeys.Knockdown, ClipMode.Hold, lying) { FadeIn = 0.1f }
                .K(KeyPhase.Seconds, 0.12f, PoseEase.Out, bounce)
                .K(KeyPhase.Seconds, 0.32f, PoseEase.In, lying)
                .K(KeyPhase.Seconds, 0.6f, PoseEase.InOut, s => s.Head(4f, 12f))
                .Build());

            // Get up: a kip-up. Knees roll back over the chest, then the legs snap forward and the body springs
            // up into a crouch, back to guard.
            clips.Add(new ClipBuilder(AnimationKeys.GetUp, ClipMode.Action, lying) { DefaultDuration = 0.65f, StartupShare = 0.35f, ActiveShare = 0.35f, FadeIn = 0.08f }
                .K(KeyPhase.Startup, 1f, PoseEase.InOut, s =>
                {
                    s.Hips(0f, -0.56f, -0.05f).Pelvis(-118f, 0f).Torso(8f, 0f).Head(30f, 0f);   // rocked back onto the shoulders
                    s.Kick(L, 8f, -8f, 0.45f, 10f, 30f).Kick(R, 6f, -12f, 0.45f, 10f, 30f);
                    s.Arm(L, 40f, 30f, 0.5f, 40f, 0f, 70f).Arm(R, 40f, 30f, 0.5f, 40f, 0f, 70f);   // palms planted by the shoulders
                })
                .K(KeyPhase.Active, 1f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.LegFrame, 0f).Set(PoseChannel.ArmFollow, 0.5f).Set(PoseChannel.LookFront, 1f);
                    s.Hips(0f, -0.36f, 0f).Pelvis(-10f, 15f).Torso(28f, 0f).Head(0f, 0f);
                    s.Foot(L, 0.16f, Ground, 0.12f, 10f).Foot(R, 0.16f, Ground, -0.14f, 25f);
                    s.Arm(L, 20f, 5f, 0.75f, 20f).Arm(R, 10f, 0f, 0.7f, 20f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // Death: the blow arches the body back, the knees buckle, and the fighter falls forward onto the
            // ground and stays there.
            clips.Add(new ClipBuilder(AnimationKeys.Death, ClipMode.Hold, guard) { FadeIn = 0.06f }
                .K(KeyPhase.Seconds, 0.12f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.1f, -0.05f).Torso(-18f, 5f).Head(-25f, 0f).Set(PoseChannel.LookFront, 0f);
                    s.Arm(L, 40f, 12f, 0.9f, 20f).Arm(R, 45f, 5f, 0.9f, 20f);
                })
                .K(KeyPhase.Seconds, 0.5f, PoseEase.In, s =>
                {
                    s.Hips(0f, -0.46f, 0.05f).Pelvis(10f, 10f).Torso(28f, 0f, 6f).Head(30f, 0f, 8f);
                    s.Foot(L, 0.15f, Ground, 0.12f, 10f).Foot(R, 0.16f, Ground, -0.12f, 20f, 30f);
                    s.Arm(L, 12f, -82f, 0.95f, 5f).Arm(R, 15f, -80f, 0.95f, 5f).Set(PoseChannel.ArmFollow, 1f);
                })
                .K(KeyPhase.Seconds, 0.7f, PoseEase.InOut, s =>
                {
                    // toppling forward off the knees: the legs straighten out behind as the body goes down
                    s.Set(PoseChannel.LegFrame, 1f);
                    s.Hips(0f, -0.62f, 0.15f).Pelvis(45f, 8f).Torso(12f, 0f, 3f).Head(10f, 30f, 0f);
                    s.Kick(L, 6f, -75f, 0.96f, 10f, 90f).Kick(R, 12f, -72f, 0.94f, 20f, 90f);
                })
                .K(KeyPhase.Seconds, 0.92f, PoseEase.In, s =>
                {
                    s.Set(PoseChannel.LegFrame, 1f);
                    s.Hips(0f, -0.85f, 0.3f).Pelvis(86f, 8f).Torso(4f, 0f, 0f).Head(-5f, 65f, 0f);
                    s.Kick(L, 6f, -100f, 1f, 10f, 95f).Kick(R, 16f, -96f, 0.99f, 20f, 90f);   // legs straight out behind, insteps on the floor
                    s.Arm(L, 85f, -30f, 0.9f, 20f, 0f).Arm(R, 85f, 50f, 0.85f, 20f, 0f);   // flung out to the sides, along the floor
                })
                .K(KeyPhase.Seconds, 1.1f, PoseEase.Out, s => s.Hips(0f, -0.87f, 0.3f).Pelvis(89f, 8f))
                .Build());

            // Spirit water: the rear hand lifts the gourd to the mouth and the head tips back; the legs are free.
            clips.Add(new ClipBuilder(AnimationKeys.Heal, ClipMode.Action, guard) { DefaultDuration = 1.2f, StartupShare = 0.22f, ActiveShare = 0.58f, UpperBodyOnly = true }
                .K(KeyPhase.Startup, 1f, PoseEase.InOut, s =>
                {
                    s.Torso(-6f, 5f).Head(-22f, 0f).Set(PoseChannel.ArmFollow, 0.8f);
                    s.Arm(R, -38f, 38f, 0.4f, 55f, 60f).Arm(L, 18f, -75f, 0.9f, 10f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Head(-30f, 0f).Torso(-9f, 5f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // Charge (fa jin): sink into horse stance, the striking palm chambered at the hip, the lead hand open
            // in front, breathing down into the belly. The animator adds a tremble that grows with the charge.
            PoseSpec chamber = Horse(guard);
            chamber.Pelvis(0f, 20f).Torso(-2f, 14f);
            chamber.Arm(R, 18f, -72f, 0.42f, -10f, 90f).Arm(L, -12f, 8f, 0.6f, 10f, 0f, 60f).Set(PoseChannel.ArmFollow, 0.5f);
            clips.Add(new ClipBuilder(AnimationKeys.Charge, ClipMode.Hold, guard) { FadeIn = 0.06f }
                .K(KeyPhase.Seconds, 0.22f, PoseEase.InOut, chamber)
                .K(KeyPhase.Seconds, 0.9f, PoseEase.InOut, s => s.Hips(0f, -0.28f, 0f).Torso(-3f, 16f))
                .Build());
        }

        // ------------------------------------------------------------------ Fire (Northern Shaolin)

        static void AddPlayerMoves(List<PoseClip> clips)
        {
            PoseSpec guard = Guard();

            // 1. Flame Jab: the lead fist shoots straight down the centre line as the lead foot steps in (bow
            //    stance); the hips turn the left shoulder into it, the rear foot stays planted.
            clips.Add(Strike(AnimationKeys.Jab, guard)
                .K(KeyPhase.Startup, 0.4f, PoseEase.InOut, s =>
                {
                    s.Pelvis(0f, 30f).Torso(4f, 10f).Hips(0f, -0.12f, -0.02f);
                    s.Arm(L, -8f, 8f, 0.5f, 5f, 20f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Pelvis(0f, 22f).Torso(8f, 32f).Hips(0f, -0.15f, 0.08f);
                    s.Arm(L, -6f, 0f, 1f, 0f, -70f).Arm(R, -45f, 28f, 0.36f, 10f, 30f);
                    s.Foot(L, 0.12f, Ground, 0.46f, 5f).Foot(R, 0.14f, Ground, -0.26f, 35f, 0f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(8f, 34f))
                .K(KeyPhase.Recovery, 0.35f, PoseEase.Out, s => s.Arm(L, -8f, 8f, 0.55f, 5f, 0f).Torso(6f, 26f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // 2. Flame Cross: the rear fist drives through as the hips whip round and the back heel pivots up; the
            //    lead fist snaps back to the chin.
            clips.Add(Strike(AnimationKeys.Cross, guard)
                .K(KeyPhase.Startup, 0.4f, PoseEase.InOut, s =>
                {
                    s.Pelvis(0f, 32f).Torso(4f, 12f).Hips(0f, -0.12f, -0.02f);
                    s.Arm(R, -30f, 20f, 0.34f, 10f, 30f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Pelvis(0f, -12f).Torso(8f, -32f).Hips(0f, -0.15f, 0.1f);
                    s.Arm(R, -9f, 0f, 1f, 0f, -70f).Arm(L, -45f, 28f, 0.38f, 10f, 30f);
                    s.Foot(L, 0.12f, Ground, 0.42f, 5f).Foot(R, 0.14f, Ground, -0.26f, 12f, 30f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(8f, -35f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(R, -25f, 12f, 0.5f, 5f, 0f).Torso(6f, -20f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // 3. Rising Snap Kick (tan tui): the rear knee chambers high, then the foot snaps out at the midsection,
            //    striking with the ball of the foot; the body leans back to balance and the arms counter.
            clips.Add(Strike(AnimationKeys.SnapKick, guard)
                .K(KeyPhase.Startup, 0.45f, PoseEase.InOut, s =>
                {
                    s.Pelvis(0f, 5f).Torso(-6f, 6f).Hips(0f, -0.07f, 0.02f);
                    s.Kick(R, 0f, -25f, 0.5f, 0f, -10f);
                    s.Foot(L, 0.08f, Ground, 0.24f, 10f, 0f, 0f, 1f, 1f);
                    s.Arm(L, -5f, 10f, 0.58f, 5f, 20f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Pelvis(-6f, 0f).Torso(-12f, 4f).Hips(0f, -0.04f, 0.06f);
                    s.Kick(R, -3f, 8f, 1f, 0f, -25f);
                    s.Arm(L, -10f, 5f, 0.72f, 5f, 20f).Arm(R, 28f, -45f, 0.82f, 10f, 0f).Set(PoseChannel.ArmFollow, 0.2f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Kick(R, -3f, 10f, 1f, 0f, -25f))
                .K(KeyPhase.Recovery, 0.35f, PoseEase.Out, s => s.Kick(R, 0f, -30f, 0.5f, 0f, -5f).Torso(-4f, 6f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // 4. Dragon Tail Kick: a full spin. The body turns its back to the opponent, the right leg extends and
            //    the heel sweeps through the whole arc as the spin carries on, landing back in guard facing front.
            //    RootYaw goes 0 -> 360 across the move.
            clips.Add(Strike(AnimationKeys.SpinKick, guard)
                .K(KeyPhase.Startup, 0.4f, PoseEase.In, s =>
                {
                    s.Set(PoseChannel.RootYaw, 55f).Set(PoseChannel.LookFront, 0.25f);
                    s.Pelvis(0f, 5f).Torso(8f, 0f).Hips(0f, -0.1f, 0f);
                    s.Foot(L, 0.03f, Ground, 0.06f, 0f, 20f);
                    s.Kick(R, 115f, -40f, 0.55f, 0f, 20f);
                    s.Arm(L, 45f, 5f, 0.7f, 10f).Arm(R, 20f, 20f, 0.5f, 10f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Linear, s =>
                {
                    s.Set(PoseChannel.RootYaw, 150f).Set(PoseChannel.ArmFollow, 0.5f);
                    s.Pelvis(8f, 0f).Torso(24f, 0f, 10f).Hips(0f, -0.08f, 0f);
                    s.Kick(R, 150f, 14f, 1f, 0f, 35f);
                    s.Arm(L, 70f, -5f, 0.85f, 10f).Arm(R, 40f, -15f, 0.8f, 10f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Set(PoseChannel.RootYaw, 270f).Kick(R, 150f, 12f, 1f, 0f, 35f))
                .K(KeyPhase.Recovery, 0.3f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.RootYaw, 325f).Set(PoseChannel.LookFront, 0.8f);
                    s.Kick(R, 110f, -35f, 0.55f, 0f, 20f).Torso(6f, 0f, 3f);
                    s.Arm(L, 10f, 10f, 0.6f, 10f).Arm(R, -20f, 20f, 0.45f, 10f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, s => { s.CopyFrom(guard); s.Set(PoseChannel.RootYaw, 360f); })
                .Build());

            // 5. Phoenix Palm: sink into horse stance with both palms drawn to the chest, then drive forward into a
            //    bow stance and push both palms out together, heels of the palms first: a cone of fire.
            PoseSpec horse = Horse(guard);
            clips.Add(Strike(AnimationKeys.PhoenixPalm, guard)
                .K(KeyPhase.Startup, 0.6f, PoseEase.InOut, s =>
                {
                    s.CopyFrom(horse);
                    s.Hips(0f, -0.25f, -0.05f).Torso(-5f, 0f).Set(PoseChannel.ArmFollow, 0f);
                    s.Arm(L, -35f, 5f, 0.3f, -10f, 0f, 30f).Arm(R, -35f, 5f, 0.3f, -10f, 0f, 30f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.2f, 0.12f).Pelvis(0f, 8f).Torso(12f, -4f);
                    s.Foot(L, 0.16f, Ground, 0.46f, 8f, 0f, 10f).Foot(R, 0.22f, Ground, -0.3f, 30f, 10f, 0f, 1f, 1f);
                    s.Arm(L, -10f, 0f, 1f, 0f, -80f, 80f).Arm(R, -10f, 0f, 1f, 0f, -80f, 80f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(13f, -4f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s =>
                {
                    s.Hips(0f, -0.17f, 0.08f);
                    s.Arm(L, -12f, 5f, 0.72f, 0f, -40f, 40f).Arm(R, -12f, 5f, 0.72f, 0f, -40f, 40f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // Rising Dragon Kick (launcher): load low, then the rear leg swings straight up past the face as the
            //    body springs into the air after the target; arms swing up and back.
            PoseSpec air = AirGuard();
            clips.Add(Strike(AnimationKeys.Launcher, guard)
                .K(KeyPhase.Startup, 0.6f, PoseEase.In, s =>
                {
                    s.Hips(0f, -0.26f, 0f).Pelvis(0f, 8f).Torso(14f, 4f);
                    s.Arm(L, 25f, -55f, 0.8f, 10f).Arm(R, 20f, -60f, 0.8f, 10f).Set(PoseChannel.ArmFollow, 0.2f);
                })
                .K(KeyPhase.Startup, 0.8f, PoseEase.InOut, s =>
                {
                    // the rear knee drives up first: the kick unfolds from there
                    s.Hips(0f, -0.14f, 0f).Torso(4f, 2f);
                    s.Kick(R, 0f, 5f, 0.5f, 0f, -10f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, 0.04f, 0f).Pelvis(-8f, 0f).Torso(-18f, 0f).Set(PoseChannel.LookFront, 0.5f);
                    s.Kick(R, 0f, 62f, 1f, 0f, -20f).Kick(L, 4f, -95f, 0.98f, 0f, 45f);
                    s.Arm(L, 10f, 60f, 0.9f, 10f).Arm(R, 25f, -50f, 0.9f, 10f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Kick(R, 0f, 76f, 1f, 0f, -15f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s =>
                {
                    s.Kick(R, 6f, -30f, 0.52f, 0f, 20f).Kick(L, 6f, -62f, 0.58f, 0f, 25f);
                    s.Torso(6f, 4f).Set(PoseChannel.LookFront, 1f);
                    s.Arm(L, -8f, 12f, 0.6f, 5f, 20f).Arm(R, -40f, 28f, 0.38f, 10f, 30f).Set(PoseChannel.ArmFollow, 0.5f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, air)
                .Build());

            // Sky Jab: a lead-hand punch in the air, knees tucked, the rear leg kicking back to balance the turn.
            clips.Add(Strike(AnimationKeys.AirJab, air)
                .K(KeyPhase.Startup, 0.5f, PoseEase.InOut, s => s.Torso(6f, 12f).Arm(L, -8f, 5f, 0.48f, 5f, 20f))
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f).Torso(8f, 32f).Pelvis(0f, 20f);
                    s.Arm(L, -6f, -6f, 1f, 0f, -70f).Arm(R, -45f, 28f, 0.36f, 10f, 30f);
                    s.Kick(R, 10f, -125f, 0.85f, 0f, 45f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(8f, 34f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, air)
                .Build());

            // Crescent Flame Kick: the straight right leg sweeps up from the outside and across the front in an arc.
            clips.Add(Strike(AnimationKeys.AirCrescent, air)
                .K(KeyPhase.Startup, 0.5f, PoseEase.InOut, s =>
                {
                    s.Kick(R, 75f, -5f, 0.6f, 20f, 10f).Torso(4f, 10f, 5f);
                    s.Arm(L, 70f, 10f, 0.8f, 10f).Arm(R, 20f, 20f, 0.6f, 10f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Kick(R, 65f, 35f, 1f, 30f, 30f).Torso(-4f, -8f, 12f).Set(PoseChannel.ArmFollow, 0.5f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Kick(R, -55f, 28f, 1f, 30f, 30f).Torso(-4f, -22f, 10f))
                .K(KeyPhase.Recovery, 0.5f, PoseEase.Out, s => s.Kick(R, -30f, -30f, 0.6f, 0f, 20f).Torso(4f, -10f, 3f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, air)
                .Build());

            // Tornado Slam Kick: a spinning roundhouse in the air (the body turns a full circle to the left), the
            //    shin chopping down through the target to drive it into the floor.
            clips.Add(Strike(AnimationKeys.AirTornado, air)
                .K(KeyPhase.Startup, 0.45f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.RootYaw, 25f).Torso(4f, 15f);
                    s.Kick(R, 70f, -20f, 0.5f, 20f, 30f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.RootYaw, -30f).Set(PoseChannel.LookFront, 0.3f).Set(PoseChannel.ArmFollow, 0.6f);
                    s.Torso(-5f, 0f, 20f).Kick(R, 90f, -8f, 1f, 30f, 45f).Kick(L, 6f, -70f, 0.55f, 0f, 25f);
                    s.Arm(L, 75f, 10f, 0.85f, 10f).Arm(R, 30f, 20f, 0.55f, 10f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Set(PoseChannel.RootYaw, -150f).Kick(R, 90f, -38f, 1f, 30f, 45f))
                .K(KeyPhase.Recovery, 0.5f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.RootYaw, -300f).Set(PoseChannel.LookFront, 0.8f);
                    s.Kick(R, 35f, -60f, 0.6f, 0f, 30f).Torso(6f, 5f, 4f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, s => { s.CopyFrom(air); s.Set(PoseChannel.RootYaw, -360f); })
                .Build());

            // Falling Axe Kick (plunge): the right leg swings straight up overhead while hanging in the air, stays
            //    up through the fall, then chops down as you land, bowing into a low stance.
            clips.Add(new ClipBuilder(AnimationKeys.AxeKick, ClipMode.Action, air) { FadeIn = 0.06f, LandsItself = true }
                .K(KeyPhase.Startup, 1f, PoseEase.Out, s =>
                {
                    s.Kick(R, 12f, 80f, 1f, 0f, 10f).Kick(L, 5f, -85f, 0.8f, 0f, 35f);
                    s.Torso(-10f, 5f).Arm(L, 50f, 40f, 0.8f, 10f).Arm(R, 40f, 30f, 0.8f, 10f).Set(PoseChannel.ArmFollow, 0.5f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(-5f, 5f))
                .K(KeyPhase.Recovery, 0.25f, PoseEase.InOut, s =>
                {
                    // the chop lands over about five frames, not one
                    s.Hips(0f, -0.3f, 0.05f).Pelvis(0f, 5f).Torso(26f, 0f);
                    s.Kick(R, 5f, -36f, 0.95f, 0f, -15f).Foot(L, 0.13f, Ground, -0.22f, 25f);   // heel chops to the floor, not through it
                    s.Arm(L, 10f, -30f, 0.9f, 10f).Arm(R, 5f, -28f, 0.9f, 10f).Set(PoseChannel.ArmFollow, 0.3f);
                })
                // the heel settles flat where it landed, then steps back to the guard (lifted, not dragged along the floor)
                .K(KeyPhase.Recovery, 0.45f, PoseEase.Out, s => s.Hips(0f, -0.28f, 0.03f).Torso(20f, 0f).Foot(R, 0.1f, Ground, 0.6f, 0f))
                .K(KeyPhase.Recovery, 0.65f, PoseEase.InOut, s =>
                {
                    for (int c = 0; c < PoseSpec.LegChannels; c++) s[PoseSpec.Leg(R, c)] = guard[PoseSpec.Leg(R, c)];
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // Fa Jin Palm (heavy, released from the charge): from horse stance, sink and coil, then an explosive
            //    short palm strike with a stamp of the lead foot, the whole body snapping into it at once.
            PoseSpec chamber = Horse(guard);
            chamber.Pelvis(0f, 20f).Torso(-2f, 14f);
            chamber.Arm(R, 18f, -72f, 0.42f, -10f, 90f).Arm(L, -12f, 8f, 0.6f, 10f, 0f, 60f).Set(PoseChannel.ArmFollow, 0.5f);
            clips.Add(new ClipBuilder(AnimationKeys.FaJinPalm, ClipMode.Action, chamber)
                .K(KeyPhase.Startup, 0.7f, PoseEase.In, s => s.Hips(0f, -0.3f, -0.03f).Pelvis(0f, 26f).Torso(-3f, 20f))
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.27f, 0.12f).Pelvis(0f, -12f).Torso(8f, -24f);
                    s.Arm(R, -7f, 0f, 1f, 0f, -80f, 85f).Arm(L, 12f, -62f, 0.36f, -10f, 90f);
                    s.Foot(L, 0.2f, Ground, 0.4f, 10f, 0f, 10f).Foot(R, 0.3f, Ground, -0.12f, 20f, 15f, 5f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(9f, -26f))
                .K(KeyPhase.Recovery, 0.3f, PoseEase.Out, s => s.Arm(R, -10f, 3f, 0.8f, 0f, -50f, 60f).Hips(0f, -0.24f, 0.08f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // Fire Blast (ranged): drop into horse stance with both fists at the hips, then thrust both fists
            //    straight out together: the fireball leaves on the first frame of full extension.
            clips.Add(Strike(AnimationKeys.FireBlast, guard)
                .K(KeyPhase.Startup, 0.55f, PoseEase.InOut, s =>
                {
                    s.CopyFrom(horse);
                    s.Hips(0f, -0.21f, -0.03f).Set(PoseChannel.ArmFollow, 0f);
                    s.Arm(L, 12f, -70f, 0.38f, -15f, 0f).Arm(R, 12f, -70f, 0.38f, -15f, 0f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.23f, 0.06f).Torso(8f, 0f);
                    s.Arm(L, -8f, 3f, 1f, 0f, -70f).Arm(R, -8f, 3f, 1f, 0f, -70f);
                    s.Foot(R, 0.32f, Ground, -0.08f, 22f, 0f, 10f, 1f, 1f);
                })
                .K(KeyPhase.Recovery, 0.12f, PoseEase.Linear, s => s.Torso(9f, 0f))   // zero active frames: hold the extension a moment
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(L, -8f, 3f, 0.86f, 0f, -70f).Arm(R, -8f, 3f, 0.86f, 0f, -70f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // Fire Whip (mid range): wind the right arm back and round, then sweep it across the body at full
            //    length through the whole arc, the weight rolling from the back foot to the front, and follow through.
            clips.Add(Strike(AnimationKeys.FireWhip, guard)
                .K(KeyPhase.Startup, 0.7f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0.05f, -0.14f, -0.08f).Pelvis(0f, 32f).Torso(0f, 45f);
                    s.Arm(R, 110f, 25f, 0.8f, 20f, 0f).Arm(L, -20f, 10f, 0.7f, 5f, 20f, 40f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.14f, 0f).Pelvis(0f, 10f).Torso(4f, 15f);
                    s.Arm(R, 55f, 12f, 1f, 10f, 0f, 20f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s =>
                {
                    s.Hips(-0.05f, -0.16f, 0.1f).Pelvis(0f, -15f).Torso(8f, -30f);
                    s.Arm(R, -55f, 5f, 1f, 10f, 0f, 20f).Arm(L, 30f, -20f, 0.7f, 10f);
                    s.Foot(R, 0.14f, Ground, -0.26f, 20f, 25f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Recovery, 0.35f, PoseEase.Out, s => s.Arm(R, -95f, -15f, 0.75f, 10f).Torso(8f, -42f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // Flame Wheel (close, all round): drop onto the bent left leg with the right leg stretched along the
            //    floor, one hand on the ground, and spin a full circle to the left: a low sweeping kick ringed in fire.
            clips.Add(Strike(AnimationKeys.FlameWheel, guard)
                .K(KeyPhase.Startup, 1f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.RootYaw, 20f).Set(PoseChannel.LookFront, 0.4f).Set(PoseChannel.ArmFollow, 0.3f);
                    s.Hips(0f, -0.52f, 0.02f).Pelvis(15f, 0f).Torso(22f, 0f, 14f);
                    s.Foot(L, 0.04f, Ground, 0.12f, 20f, 0f, 25f);
                    s.Kick(R, 90f, -23f, 1f, 30f, 8f);   // shin skimming just above the floor
                    s.Arm(L, 25f, -70f, 1f, 10f).Arm(R, 70f, 0f, 0.8f, 10f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Linear, s => s.Set(PoseChannel.RootYaw, 0f))
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Set(PoseChannel.RootYaw, -360f))
                .K(KeyPhase.Recovery, 0.45f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.RootYaw, -385f).Set(PoseChannel.LookFront, 0.9f);
                    s.Hips(0f, -0.3f, 0f).Torso(10f, 0f, 4f).Pelvis(4f, 10f);
                    s.Kick(R, 60f, -55f, 0.72f, 20f, 20f);
                    s.Arm(L, 10f, 0f, 0.6f, 10f).Arm(R, 20f, 10f, 0.6f, 10f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, s => { s.CopyFrom(guard); s.Set(PoseChannel.RootYaw, -360f); })
                .Build());

            // Flame Step Strike (zip strike): fire jets carry you forward leaning into the flight with knees tucked,
            //    the body turns side-on and the right leg fires out in a flying side kick.
            clips.Add(Strike(AnimationKeys.ZipKick, guard, leaps: true)
                .K(KeyPhase.Startup, 0.3f, PoseEase.Out, s =>
                {
                    s.Hips(0f, 0f, 0f).Pelvis(10f, 10f).Torso(32f, 0f).Head(-25f, 0f).Set(PoseChannel.ArmFollow, 0.3f);
                    s.Kick(L, 5f, -60f, 0.55f, 0f, 30f).Kick(R, 10f, -78f, 0.6f, 0f, 35f);
                    s.Arm(L, 30f, -40f, 0.9f, 10f).Arm(R, 28f, -45f, 0.9f, 10f);
                })
                .K(KeyPhase.Startup, 0.85f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.RootYaw, -75f).Set(PoseChannel.ArmFollow, 0f);
                    s.Pelvis(0f, 0f).Torso(10f, 0f, 25f).Head(0f, 0f);
                    s.Kick(R, 80f, 0f, 0.45f, 0f, -10f).Set(PoseChannel.RFootYaw, 80f);
                    s.Kick(L, 20f, -80f, 0.5f, 0f, 30f);
                    s.Arm(L, -20f, 20f, 0.45f, 20f).Arm(R, 70f, 0f, 0.6f, 20f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.RootYaw, -80f).Torso(10f, 0f, 35f);
                    s.Kick(R, 82f, 5f, 1f, 0f, -30f);
                    s.Arm(R, 75f, -10f, 0.9f, 10f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Kick(R, 82f, 3f, 1f, 0f, -30f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.RootYaw, -30f).Torso(12f, 0f, 8f).Hips(0f, -0.22f, 0f);
                    s.Foot(L, 0.14f, Ground, -0.12f, 10f).Kick(R, 60f, -40f, 0.55f, 0f, 10f);
                    s.Arm(L, -10f, 10f, 0.6f, 5f).Arm(R, 20f, 0f, 0.6f, 10f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // Flying Fire Kick (sprint attack): drive the lead knee up to take off, then the right leg fires out in a
            //    flying front kick at head height while the body leans back and the arms counter.
            clips.Add(Strike(AnimationKeys.SprintKick, guard, leaps: true)
                .K(KeyPhase.Startup, 0.5f, PoseEase.Out, s =>
                {
                    s.Hips(0f, 0.06f, 0f).Pelvis(0f, 0f).Torso(6f, 0f).Set(PoseChannel.ArmFollow, 0.2f);
                    s.Kick(L, 0f, -12f, 0.45f, 0f, 20f).Kick(R, 5f, -100f, 0.95f, 0f, 40f);
                    s.Arm(R, 0f, 50f, 0.8f, 10f).Arm(L, 15f, -40f, 0.85f, 10f);
                })
                .K(KeyPhase.Startup, 0.8f, PoseEase.InOut, s =>
                {
                    // in the air: the kicking knee comes up to chamber as the take-off knee drops
                    s.Kick(R, 0f, -5f, 0.48f, 0f, -10f).Kick(L, 6f, -60f, 0.6f, 0f, 30f).Torso(-6f, 0f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, 0.1f, 0f).Torso(-16f, -4f);
                    s.Kick(R, -3f, 14f, 1f, 0f, -25f).Kick(L, 6f, -45f, 0.5f, 0f, 30f);
                    s.Arm(L, -8f, 10f, 0.7f, 5f, 20f).Arm(R, 40f, -30f, 0.9f, 10f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Kick(R, -3f, 12f, 1f, 0f, -25f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s =>
                {
                    s.Hips(0f, -0.22f, 0f).Torso(15f, 0f);
                    s.Foot(R, 0.12f, Ground, 0.34f, 10f).Foot(L, 0.13f, Ground, -0.25f, 30f);
                    s.Arm(L, 10f, -10f, 0.7f, 10f).Arm(R, 10f, -5f, 0.7f, 10f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());
        }

        // Fire's pause branch (X X, wait, X X) and its dodge strike (Build 05).
        static void AddFireBranches(List<PoseClip> clips)
        {
            PoseSpec guard = Guard();

            // Sweeping Flame Kick: drop low onto the bent left leg and spin, the straight right leg sweeping the floor
            //    through the arc at ankle height, then carry the spin round and rise back into guard. RootYaw 0 -> -360.
            clips.Add(Strike(AnimationKeys.SweepKick, guard)
                .K(KeyPhase.Startup, 0.6f, PoseEase.InOut, s =>
                {
                    s.Set(PoseChannel.RootYaw, 30f).Set(PoseChannel.LookFront, 0.5f).Set(PoseChannel.ArmFollow, 0.3f);
                    s.Hips(0f, -0.4f, 0.02f).Pelvis(10f, 5f).Torso(18f, 0f, 8f);
                    s.Foot(L, 0.06f, Ground, 0.14f, 20f, 0f, 20f);
                    s.Kick(R, 70f, -40f, 0.6f, 20f, 10f);
                    s.Arm(L, 30f, -60f, 0.9f, 10f).Arm(R, 50f, 10f, 0.7f, 10f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.RootYaw, 0f).Hips(0f, -0.48f, 0.02f).Torso(22f, 0f, 12f);
                    s.Kick(R, 90f, -25f, 1f, 30f, 8f);   // the shin skims the floor
                    s.Arm(L, 25f, -70f, 1f, 10f).Arm(R, 70f, 0f, 0.8f, 10f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Set(PoseChannel.RootYaw, -160f))
                .K(KeyPhase.Recovery, 0.45f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.RootYaw, -300f).Set(PoseChannel.LookFront, 0.9f);
                    s.Hips(0f, -0.28f, 0f).Torso(10f, 0f, 4f).Pelvis(4f, 10f);
                    s.Kick(R, 60f, -55f, 0.7f, 20f, 20f);
                    s.Arm(L, 10f, 0f, 0.6f, 10f).Arm(R, 20f, 10f, 0.6f, 10f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, s => { s.CopyFrom(guard); s.Set(PoseChannel.RootYaw, -360f); })
                .Build());

            // Rising Phoenix Kick: out of the sweep, the right leg swings straight up through the foe's chin as the body
            //    hops and leans back, both arms flung up with it: the kick that throws the foe into the air.
            clips.Add(Strike(AnimationKeys.RisingPhoenixKick, guard)
                .K(KeyPhase.Startup, 0.35f, PoseEase.InOut, s =>
                {
                    s.Hips(0f, -0.24f, 0f).Pelvis(4f, 10f).Torso(16f, 4f);
                    s.Arm(L, 30f, -50f, 0.85f, 10f).Arm(R, 25f, -55f, 0.85f, 10f).Set(PoseChannel.ArmFollow, 0.2f);
                    s.Kick(R, 6f, -96f, 0.88f, 0f, 30f);   // the rear foot pushing off
                })
                .K(KeyPhase.Startup, 0.65f, PoseEase.Linear, s =>
                {
                    s.Hips(0f, -0.14f, 0f).Pelvis(0f, 6f).Torso(6f, 3f);
                    s.Kick(R, 2f, -42f, 0.72f, 0f, 0f);
                    s.Arm(L, 30f, -10f, 0.88f, 10f).Arm(R, 30f, -18f, 0.88f, 10f);
                })
                .K(KeyPhase.Startup, 0.85f, PoseEase.Linear, s =>
                {
                    s.Hips(0f, -0.04f, 0f).Pelvis(-5f, 3f).Torso(-8f, 2f);
                    s.Kick(R, -2f, 32f, 0.92f, 0f, -14f);
                    s.Arm(L, 30f, 30f, 0.9f, 10f).Arm(R, 30f, 20f, 0.9f, 10f);
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, 0.03f, 0f).Pelvis(-10f, 0f).Torso(-16f, 0f).Set(PoseChannel.LookFront, 0.5f);
                    s.Kick(R, -4f, 58f, 1f, 0f, -20f).Foot(L, 0.1f, 0.12f, 0.02f, 5f, 35f);
                    s.Arm(L, 30f, 66f, 0.95f, 10f).Arm(R, 30f, 58f, 0.95f, 10f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Kick(R, -4f, 70f, 1f, 0f, -15f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s =>
                {
                    s.Hips(0f, -0.2f, 0.04f).Pelvis(0f, 15f).Torso(10f, 4f).Set(PoseChannel.LookFront, 1f);
                    s.Foot(R, 0.14f, Ground, 0.3f, 10f).Foot(L, 0.12f, Ground, -0.2f, 30f);
                    s.Arm(L, -8f, 12f, 0.6f, 5f, 20f).Arm(R, -40f, 28f, 0.4f, 10f, 30f).Set(PoseChannel.ArmFollow, 0.5f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // Turning Heel Counter (dodge strike): spin to the right until the back faces the foe, then the right heel
            //    thrusts straight back into it, the head turned to watch over the shoulder; finish the turn into guard.
            clips.Add(Strike(AnimationKeys.SpinBackKick, guard)
                .K(KeyPhase.Startup, 0.4f, PoseEase.In, s =>
                {
                    s.Set(PoseChannel.RootYaw, 70f).Set(PoseChannel.LookFront, 0.7f).Set(PoseChannel.ArmFollow, 0.6f);
                    s.Hips(0f, -0.14f, 0f).Pelvis(0f, 8f).Torso(10f, 0f);
                    s.Foot(L, 0.06f, Ground, 0.04f, 0f, 0f, 0f, 1f, 1f);
                    s.Kick(R, 120f, -60f, 0.6f, 0f, 10f);
                    s.Arm(L, -30f, 30f, 0.45f, 15f, 30f).Arm(R, -30f, 25f, 0.45f, 15f, 30f);
                })
                .K(KeyPhase.Startup, 0.82f, PoseEase.Linear, s =>
                {
                    s.Set(PoseChannel.RootYaw, 160f).Torso(24f, 0f).Pelvis(6f, 0f);
                    s.Kick(R, 174f, -8f, 0.78f, 0f, -20f);   // the knee chambered, heel aimed back at the foe
                })
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.RootYaw, 180f).Torso(32f, 0f).Pelvis(10f, 0f);
                    s.Kick(R, 180f, 6f, 1f, 0f, -30f);
                    s.Arm(L, 30f, -20f, 0.8f, 10f).Arm(R, 20f, -30f, 0.8f, 10f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Kick(R, 180f, 8f, 1f, 0f, -30f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.RootYaw, 260f).Torso(10f, 0f).Pelvis(0f, 10f);
                    s.Kick(R, 120f, -50f, 0.55f, 0f, 10f);
                    s.Arm(L, -10f, 15f, 0.6f, 10f, 20f).Arm(R, -30f, 25f, 0.45f, 10f, 30f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, s => { s.CopyFrom(guard); s.Set(PoseChannel.RootYaw, 360f); })
                .Build());
        }

        // ------------------------------------------------------------------ enemies

        static void AddEnemyMoves(List<PoseClip> clips)
        {
            PoseSpec sword = SwordGuard();

            // Quick Slash: the dao cocks over the right shoulder (held for the telegraph), then a flat cut sweeps
            //    across the arc from right to left with a lunging step.
            PoseSpec cocked = sword.Clone();
            cocked.Hips(0f, -0.12f, -0.05f).Pelvis(0f, 30f).Torso(-2f, 30f);
            cocked.Arm(R, 70f, 55f, 0.55f, 40f, 0f).Set(PoseChannel.Blade, 75f).Set(PoseChannel.ArmFollow, 0.3f);
            cocked.Arm(L, -12f, 15f, 0.62f, 10f, 20f, 40f);
            clips.Add(Strike(AnimationKeys.SwordSlash, sword)
                .K(KeyPhase.Startup, 0.45f, PoseEase.Out, cocked)
                .K(KeyPhase.Startup, 0.8f, PoseEase.InOut, s => s.Torso(-3f, 35f))
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s => SlashFrom(s, 50f))
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => SlashTo(s, -50f))
                .K(KeyPhase.Recovery, 0.3f, PoseEase.Out, s => s.Arm(R, -75f, -30f, 0.82f, 10f, 0f).Set(PoseChannel.Blade, 25f).Torso(4f, -42f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, sword)
                .Build());

            // Heavy Overhead: the sword goes up and back behind the head as the body rears, a long held threat,
            //    then chops straight down with a deep lunge and stays low (the punish window).
            clips.Add(Strike(AnimationKeys.SwordOverhead, sword)
                .K(KeyPhase.Startup, 0.35f, PoseEase.Out, s =>
                {
                    s.Hips(0f, -0.06f, -0.06f).Pelvis(-4f, 15f).Torso(-12f, 8f).Set(PoseChannel.ArmFollow, 0.4f);
                    s.Arm(R, 12f, 100f, 0.72f, 30f, 0f).Arm(L, -15f, 95f, 0.66f, 30f, 0f).Set(PoseChannel.Blade, 65f);
                })
                .K(KeyPhase.Startup, 0.85f, PoseEase.InOut, s => s.Torso(-15f, 8f).Arm(R, 12f, 108f, 0.72f, 30f, 0f))
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.17f, 0.16f).Pelvis(4f, 10f).Torso(18f, 4f);
                    s.Arm(R, -3f, 22f, 1f, 5f, 0f).Arm(L, -12f, 18f, 0.88f, 10f, 0f).Set(PoseChannel.Blade, 8f);
                    s.Foot(L, 0.13f, Ground, 0.56f, 5f).Foot(R, 0.14f, Ground, -0.26f, 35f, 0f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Arm(R, -3f, -12f, 1f, 5f, 0f).Arm(L, -12f, -15f, 0.9f, 10f, 0f).Torso(26f, 4f))
                .K(KeyPhase.Recovery, 0.5f, PoseEase.Out, s => s.Arm(R, -3f, -35f, 0.9f, 5f, 0f).Torso(24f, 4f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, sword)
                .Build());

            // Double Slash: the quick slash, then the blade cocks back on the left and cuts back the other way.
            clips.Add(Strike(AnimationKeys.SwordDoubleSlash, sword)
                .K(KeyPhase.Startup, 0.45f, PoseEase.Out, cocked)
                .K(KeyPhase.Startup, 0.8f, PoseEase.InOut, s => s.Torso(-3f, 35f))
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s => SlashFrom(s, 50f))
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => SlashTo(s, -50f))
                .K(KeyPhase.Interval, 0.65f, PoseEase.Out, s =>
                {
                    s.Arm(R, -70f, 30f, 0.72f, 40f, 0f).Set(PoseChannel.Blade, 70f).Torso(-2f, -38f);
                })
                .K(KeyPhase.Interval, 1f, PoseEase.Snap, s => s.Arm(R, -50f, 5f, 1f, 5f, 0f).Set(PoseChannel.Blade, 8f).Torso(4f, -12f))
                .K(KeyPhase.Recovery, 0f, PoseEase.Linear, s => s.Arm(R, 50f, 0f, 1f, 5f, 0f).Torso(5f, 22f))
                .K(KeyPhase.Recovery, 0.3f, PoseEase.Out, s => s.Arm(R, 75f, -25f, 0.82f, 10f, 0f).Set(PoseChannel.Blade, 25f).Torso(4f, 30f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, sword)
                .Build());

            // Delayed Thrust: the sword draws back level at the hip with the lead hand pointing at you, a dead-still
            //    pause that baits a panic dodge, then a long lunging thrust straight down the line.
            clips.Add(Strike(AnimationKeys.SwordThrust, sword)
                .K(KeyPhase.Startup, 0.3f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0f);
                    s.Hips(0f, -0.14f, -0.12f).Pelvis(0f, 32f).Torso(-4f, 38f);
                    s.Arm(R, 25f, -8f, 0.32f, -25f, 0f).Set(PoseChannel.Blade, 0f).Set(PoseChannel.PropAim, 1f);   // blade level, aimed at you
                    s.Arm(L, -8f, 6f, 0.85f, 5f, 0f, 55f);
                })
                .K(KeyPhase.Startup, 0.85f, PoseEase.InOut, s => s.Torso(-4f, 40f))
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    // the longest reach of the four: a deep fencing-style lunge puts the tip 2.6 m out
                    s.Hips(0f, -0.26f, 0.45f).Pelvis(6f, -5f).Torso(20f, -28f);
                    s.Arm(R, -3f, -6f, 1f, 0f, 0f).Arm(L, 40f, -22f, 0.82f, 10f);
                    s.Foot(L, 0.13f, Ground, 0.92f, 5f).Foot(R, 0.14f, Ground, -0.12f, 30f, 25f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(21f, -29f).Set(PoseChannel.PropAim, 0.6f))
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, s => s.Arm(R, 0f, -5f, 0.7f, 0f, 0f).Hips(0f, -0.2f, 0.3f).Torso(12f, -20f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, sword)
                .Build());

            // Break-Out Shove: hunched low with the blade swept down behind (its own silhouette), then a
            //    shoulder-and-palm shove with the free hand while the sword sweeps low across.
            clips.Add(Strike(AnimationKeys.Shove, sword)
                .K(KeyPhase.Startup, 0.5f, PoseEase.Out, s =>
                {
                    s.Set(PoseChannel.ArmFollow, 0.2f);
                    s.Hips(0f, -0.22f, -0.05f).Pelvis(4f, 30f).Torso(24f, 34f);
                    s.Arm(L, -30f, 0f, 0.36f, -10f, 0f).Arm(R, 60f, -52f, 0.9f, 10f, 0f).Set(PoseChannel.Blade, 0f);
                })
                .K(KeyPhase.Startup, 0.9f, PoseEase.InOut, s => s.Torso(26f, 36f))
                .K(KeyPhase.Active, 0f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.18f, 0.2f).Pelvis(0f, 0f).Torso(12f, -10f);
                    s.Arm(L, -5f, 5f, 1f, 0f, -80f, 80f).Arm(R, 20f, -30f, 1f, 5f, 0f);
                    s.Foot(L, 0.13f, Ground, 0.56f, 5f).Foot(R, 0.14f, Ground, -0.26f, 30f, 10f, 0f, 1f, 1f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Arm(R, -40f, -28f, 1f, 5f, 0f).Torso(12f, -20f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, sword)
                .Build());

            // Aimed Shot: the crossbow comes up to the shoulder, cheek on the stock, held steady through the
            //    telegraph, kicks with the shot and settles.
            PoseSpec crossbow = CrossbowGuard();
            PoseSpec aimed = crossbow.Clone();
            // raised and aimed: side-on, stock to the shoulder, elbow up, cheek down on it, the other hand under the bow
            aimed.Pelvis(0f, 38f).Torso(2f, 30f).Head(10f, -14f, -8f).Set(PoseChannel.ArmFollow, 0f).Set(PoseChannel.PropAim, 1f);
            aimed.Arm(R, -22f, 12f, 0.46f, 70f, 0f).Arm(L, -28f, 4f, 0.82f, 5f, 0f).Shrug(R, 8f);
            aimed.Hips(0f, -0.1f, 0f);
            PoseSpec recoil = aimed.Clone();
            recoil.Torso(-6f, 24f).Head(-4f, -6f);
            recoil.Arm(R, -22f, 20f, 0.42f, 70f, 0f).Arm(L, -28f, 12f, 0.76f, 5f, 0f);
            clips.Add(new ClipBuilder(AnimationKeys.CrossbowShot, ClipMode.Action, crossbow) { Aims = true }
                .K(KeyPhase.Startup, 0.3f, PoseEase.InOut, aimed)
                .K(KeyPhase.Startup, 1f, PoseEase.Linear, aimed)
                .K(KeyPhase.Recovery, 0.06f, PoseEase.Snap, recoil)
                .K(KeyPhase.Recovery, 0.35f, PoseEase.Out, aimed)
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, crossbow)
                .Build());

            // Repeater Burst: the same raise, three kicks spread over the burst.
            clips.Add(new ClipBuilder(AnimationKeys.CrossbowBurst, ClipMode.Action, crossbow) { Aims = true }
                .K(KeyPhase.Startup, 0.3f, PoseEase.InOut, aimed)
                .K(KeyPhase.Startup, 1f, PoseEase.Linear, aimed)
                .K(KeyPhase.Interval, 0.1f, PoseEase.Snap, recoil)
                .K(KeyPhase.Interval, 0.42f, PoseEase.Out, aimed)
                .K(KeyPhase.Interval, 0.58f, PoseEase.Snap, recoil)
                .K(KeyPhase.Interval, 0.92f, PoseEase.Out, aimed)
                .K(KeyPhase.Recovery, 0.08f, PoseEase.Snap, recoil)
                .K(KeyPhase.Recovery, 0.4f, PoseEase.Out, aimed)
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, crossbow)
                .Build());

            // Practice Swing (sparring dummy): a stiff wooden sweep of the stick across the front.
            PoseSpec dummy = DummyGuard();
            clips.Add(Strike(AnimationKeys.PracticeSwing, dummy)
                .K(KeyPhase.Startup, 0.5f, PoseEase.InOut, s => s.Torso(0f, 25f).Arm(R, 70f, 20f, 0.8f, 20f, 0f))
                .K(KeyPhase.Startup, 0.8f, PoseEase.Linear, s => s.Torso(0f, 28f))
                .K(KeyPhase.Active, 0f, PoseEase.Out, s => s.Torso(0f, 10f).Arm(R, 55f, 5f, 1f, 5f, 0f).Set(PoseChannel.Blade, 10f))
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s => s.Torso(0f, -18f).Arm(R, -55f, 0f, 1f, 5f, 0f))
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, dummy)
                .Build());
        }

        // The sword arm fully extended entering the arc on the right, a lunging step behind it.
        static void SlashFrom(PoseSpec s, float yaw)
        {
            s.Set(PoseChannel.ArmFollow, 0f);
            s.Hips(0f, -0.15f, 0.12f).Pelvis(0f, 12f).Torso(5f, 8f);
            s.Arm(R, yaw, 6f, 1f, 5f, 0f).Set(PoseChannel.Blade, 8f);
            s.Foot(L, 0.13f, Ground, 0.52f, 5f).Foot(R, 0.14f, Ground, -0.26f, 35f, 0f, 0f, 1f, 1f);
        }

        static void SlashTo(PoseSpec s, float yaw)
        {
            s.Hips(-0.04f, -0.16f, 0.14f).Pelvis(0f, -10f).Torso(6f, -32f);
            s.Arm(R, yaw, 2f, 1f, 5f, 0f).Set(PoseChannel.Blade, 5f);
        }

        static ClipBuilder Strike(string key, PoseSpec start, bool leaps = false)
        {
            return new ClipBuilder(key, ClipMode.Action, start) { FadeIn = 0.08f, Leaps = leaps };
        }

        // Builds a clip one key at a time; each key starts as a copy of the one before, so a key only states
        // what changes (like an animator moving a few controls between keyframes).
        sealed class ClipBuilder
        {
            readonly string key;
            readonly ClipMode mode;
            readonly List<PoseKeyframe> keys = new List<PoseKeyframe>();
            PoseSpec last;

            public float LoopPeriod = 1f;
            public float DefaultDuration = 0.5f;
            public float StartupShare = 0.3f;
            public float ActiveShare = 0.3f;
            public float FadeIn = 0.1f;
            public bool UpperBodyOnly;
            public bool Aims;
            public bool Glides;
            public bool Leaps;
            public bool LandsItself;
            public float ArmSwing = 1f;

            public ClipBuilder(string key, ClipMode mode, PoseSpec start)
            {
                this.key = key;
                this.mode = mode;
                KeyPhase first = mode == ClipMode.Action ? KeyPhase.Startup : mode == ClipMode.Loop ? KeyPhase.Cycle : KeyPhase.Seconds;
                last = start.Clone();
                keys.Add(new PoseKeyframe { Phase = first, At = 0f, Ease = PoseEase.Linear, Pose = last });
            }

            public ClipBuilder K(KeyPhase phase, float at, PoseEase ease, Action<PoseSpec> edit)
            {
                PoseSpec pose = last.Clone();
                edit?.Invoke(pose);
                return Add(phase, at, ease, pose);
            }

            public ClipBuilder K(KeyPhase phase, float at, PoseEase ease, PoseSpec pose)
            {
                return Add(phase, at, ease, pose.Clone());
            }

            // The sub-hits of a multi-hit strike (MoveData.HitCount > 1), on Interval keys. The first snaps to full extension
            // on the move's first active frame (Interval 0 = Active 0) and holds there for HoldShare of the interval, so the
            // hit shows on the frame it lands at any frame rate. The rest flow: the body moves at an even speed through the
            // in-between pose (between(pose, k)) and on into each following strike (strike(pose, k)) as it lands. Flurries
            // are a few frames per hit, so a stop-and-snap per hit would read as popping rather than speed.
            public ClipBuilder Hits(int hits, Action<PoseSpec, int> strike, Action<PoseSpec, int> between)
            {
                int n = Math.Max(1, hits);
                float step = n > 1 ? 1f / (n - 1) : 0f;
                for (int k = 0; k < n; k++)
                {
                    int hit = k;
                    float at = hit * step;
                    if (hit > 0 && between != null) K(KeyPhase.Interval, at - step * BetweenShare, PoseEase.Linear, s => between(s, hit));
                    K(KeyPhase.Interval, at, hit == 0 ? PoseEase.Snap : PoseEase.Linear, s => strike(s, hit));
                    if (hit == 0 && n > 1) K(KeyPhase.Interval, at + step * HoldShare, PoseEase.Linear, (Action<PoseSpec>)null);
                }
                return this;
            }

            const float HoldShare = 0.2f;        // of the interval: the first strike holds full extension this long...
            const float BetweenShare = 0.45f;    // ...and each in-between pose comes this long before the next sub-hit

            ClipBuilder Add(KeyPhase phase, float at, PoseEase ease, PoseSpec pose)
            {
                keys.Add(new PoseKeyframe { Phase = phase, At = at, Ease = ease, Pose = pose });
                last = pose;
                return this;
            }

            public PoseClip Build()
            {
                return new PoseClip
                {
                    Key = key, Mode = mode, LoopPeriod = LoopPeriod, DefaultDuration = DefaultDuration, StartupShare = StartupShare,
                    ActiveShare = ActiveShare, FadeIn = FadeIn, UpperBodyOnly = UpperBodyOnly, Aims = Aims, Glides = Glides, Leaps = Leaps, LandsItself = LandsItself, ArmSwing = ArmSwing,
                    Keys = keys.ToArray()
                };
            }
        }
    }
}
