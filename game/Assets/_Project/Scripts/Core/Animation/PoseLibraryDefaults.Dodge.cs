using System.Collections.Generic;

namespace VaatusRevenge.Core
{
    // The Spider-Man 2 dodges (Build 05): every dodge keeps facing the fight, so each kind is its own clip authored
    // facing forward (the combat rules keep the body turned to the focus target), plus the short flourish of a plain
    // element switch. Shared by every element; the element's stance comes back in through the recovery blend.
    //   dodge_slip     stick toward the foe: duck and weave in under its guard, hands up, ready to hit
    //   dodge_side_l/r stick sideways (and the automatic side-step): a low shuffle-hop to that side, head slipping off
    //                  the line, eyes on the foe
    //   dodge_evade    stick away: a long hop back out of reach, chin tucked, still squared up
    public static partial class PoseLibraryDefaults
    {
        static void AddDodges(List<PoseClip> clips)
        {
            PoseSpec guard = Guard();

            // Slip in: a duck under the strike and a step in, chest low over the lead knee, both hands tight by the face.
            clips.Add(new ClipBuilder(AnimationKeys.DodgeSlip, ClipMode.Action, guard) { DefaultDuration = 0.26f, StartupShare = 0.25f, ActiveShare = 0.45f, FadeIn = 0.05f }
                .K(KeyPhase.Startup, 1f, PoseEase.Snap, s =>
                {
                    s.Hips(-0.04f, -0.26f, 0.08f).Pelvis(6f, 18f).Torso(28f, 10f, -8f).Head(-18f, 0f, 6f);
                    s.Arm(L, -30f, 38f, 0.42f, 15f, 40f).Arm(R, -42f, 32f, 0.34f, 15f, 40f).Set(PoseChannel.ArmFollow, 0.7f);
                    // driving off the rear foot, the lead foot skimming forward
                    s.Foot(L, 0.13f, Ground + 0.04f, 0.42f, 5f, -8f).Foot(R, 0.15f, 0.13f, -0.36f, 30f, 35f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s =>
                {
                    s.Hips(-0.02f, -0.22f, 0.06f).Torso(22f, 8f, -5f).Head(-12f, 0f, 4f);
                    s.Foot(L, 0.13f, Ground, 0.4f, 5f).Foot(R, 0.15f, 0.1f, -0.3f, 30f, 25f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            clips.Add(SideSlip(AnimationKeys.DodgeSideLeft, guard, -1f));
            clips.Add(SideSlip(AnimationKeys.DodgeSideRight, guard, 1f));

            // Evade out: a long hop straight back, the torso leaning back then folding forward over the landing, hands up.
            clips.Add(new ClipBuilder(AnimationKeys.DodgeEvade, ClipMode.Action, guard) { DefaultDuration = 0.26f, StartupShare = 0.25f, ActiveShare = 0.45f, FadeIn = 0.05f }
                .K(KeyPhase.Startup, 1f, PoseEase.Snap, s =>
                {
                    s.Hips(0f, -0.06f, -0.1f).Pelvis(-4f, 20f).Torso(-12f, 6f).Head(10f, 0f);
                    s.Arm(L, -18f, 26f, 0.5f, 10f, 30f).Arm(R, -42f, 32f, 0.36f, 10f, 30f).Set(PoseChannel.ArmFollow, 0.6f);
                    // both feet off the floor: the lead knee comes up, the rear foot reaches back for the landing
                    s.Foot(L, 0.12f, 0.2f, 0.16f, 5f, 25f).Foot(R, 0.15f, 0.12f, -0.44f, 30f, 30f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.In, s =>
                {
                    s.Hips(0f, -0.2f, -0.02f).Torso(10f, 6f).Head(4f, 0f);
                    s.Foot(L, 0.13f, Ground, 0.28f, 5f).Foot(R, 0.15f, Ground, -0.32f, 35f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());

            // Element switch (a plain switch while standing free): the palms meet in front of the chest, open out as the
            // new element answers, and settle. Upper body only: the legs keep walking or standing in the new stance.
            clips.Add(new ClipBuilder(AnimationKeys.ElementSwitch, ClipMode.Action, guard) { DefaultDuration = 0.4f, StartupShare = 0.35f, ActiveShare = 0.3f, FadeIn = 0.06f, UpperBodyOnly = true }
                .K(KeyPhase.Startup, 1f, PoseEase.InOut, s =>
                {
                    s.Torso(2f, 0f).Head(4f, 0f).Set(PoseChannel.ArmFollow, 0.8f);
                    s.Arm(L, -38f, 8f, 0.42f, 20f, 80f, 10f).Arm(R, -38f, 8f, 0.42f, 20f, 80f, 10f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Out, s =>
                {
                    s.Torso(-4f, 0f).Head(-4f, 0f).Shrug(L, 4f).Shrug(R, 4f);
                    s.Arm(L, 40f, 18f, 0.85f, 15f, 0f, 40f).Arm(R, 40f, 18f, 0.85f, 15f, 0f, 40f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build());
        }

        // A side-slip: the body drops and slides to one side (side -1 = left, +1 = right), the head slipping off the
        // centre line and the torso leaning away from where the strike was, the far foot pushing off and catching up.
        static PoseClip SideSlip(string key, PoseSpec guard, float side)
        {
            bool left = side < 0f;
            BodySide lead = left ? L : R;       // the foot that reaches out first
            BodySide trail = left ? R : L;
            return new ClipBuilder(key, ClipMode.Action, guard) { DefaultDuration = 0.26f, StartupShare = 0.22f, ActiveShare = 0.5f, FadeIn = 0.05f }
                .K(KeyPhase.Startup, 1f, PoseEase.Snap, s =>
                {
                    s.Hips(0.1f * side, -0.22f, 0f).Pelvis(0f, 22f, -6f * side).Torso(8f, 6f, -16f * side).Head(-4f, -6f * side, -10f * side);
                    s.Arm(L, -20f, 30f, 0.5f, 15f, 30f).Arm(R, -40f, 30f, 0.38f, 15f, 30f).Set(PoseChannel.ArmFollow, 0.6f);
                    s.Foot(lead, 0.34f, Ground + 0.03f, left ? 0.24f : -0.14f, 20f, -6f);
                    s.Foot(trail, 0.06f, 0.12f, left ? -0.2f : 0.18f, 25f, 30f);
                })
                .K(KeyPhase.Active, 1f, PoseEase.Linear, s =>
                {
                    s.Hips(0.04f * side, -0.18f, 0f).Torso(8f, 6f, -8f * side).Head(-2f, -3f * side, -5f * side);
                    s.Foot(lead, 0.26f, Ground, left ? 0.26f : -0.2f, 15f);
                    s.Foot(trail, 0.1f, 0.1f, left ? -0.22f : 0.22f, 25f, 20f);
                })
                .K(KeyPhase.Recovery, 1f, PoseEase.InOut, guard)
                .Build();
        }
    }
}
