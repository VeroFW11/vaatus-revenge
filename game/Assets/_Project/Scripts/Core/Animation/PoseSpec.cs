using System;

namespace VaatusRevenge.Core
{
    // The "dials" a pose is described with. Instead of storing 22 joint rotations per keyframe (unreadable and
    // impossible to tweak by hand), a key says what a martial artist would: how low the hips sink, how far the
    // torso twists, where each fist points and how far the arm is extended, where each foot is planted or which
    // way a kick travels. PoseSolver turns these dials into joint rotations with inverse kinematics.
    //
    // Conventions (the fighter faces +Z; lengths in metres for a 1.8 m fighter, angles in degrees):
    //   Yaw + = turn right, Pitch + = tip forward (torso) or aim up (limbs), Roll + = lean left.
    //   Limb dials are written "outward positive" so the left and right sides mirror: ArmYaw + = away from the
    //   body's midline, FootX + = out to that side, KneeOut + = knee pointing outward, and so on.
    //   Arm aim: yaw/pitch of the shoulder-to-wrist line; Reach 1 = arm fully straight, 0.4 = fist near the chest.
    //   Leg: FootX/Y/Z is where the ankle is planted (Y = height above the ground; 0.08 = standing on it), unless
    //   LegReach mode is 1, when KickYaw/KickPitch/KickReach aim the leg from the hip like an arm (kicks).
    public enum PoseChannel
    {
        // Whole body
        HipsX, HipsY, HipsZ,            // pelvis shift from its rest spot (HipsY - = sink into a stance)
        RootYaw,                        // turns the whole body (spins)
        PelvisPitch, PelvisYaw, PelvisRoll,
        TorsoPitch, TorsoYaw, TorsoRoll, // spread over the three spine joints
        HeadPitch, HeadYaw, HeadRoll,
        LookFront,                      // 0..1: the head turns back toward the fighter's facing (eyes on the opponent)
        ArmFollow,                      // 0..1: arm aim follows the chest (1) or the body's facing (0)
        LegFrame,                       // 0..1: kick aim follows the pelvis (1, tumbling) or the body's facing (0)
        Blade,                          // prop angle: 0 = along the forearm, 90 = straight up out of the fist
        PropAim,                        // 0..1: prop points along the body's facing (a crossbow being aimed)

        // Left arm
        LArmYaw, LArmPitch, LArmReach, LElbowOut, LHandRoll, LWrist, LShrug,
        // Right arm
        RArmYaw, RArmPitch, RArmReach, RElbowOut, RHandRoll, RWrist, RShrug,

        // Left leg
        LFootX, LFootY, LFootZ, LKneeOut, LFootYaw, LFootPitch, LFootFlat, LLegReach, LKickYaw, LKickPitch, LKickReach, LAnchor,
        // Right leg
        RFootX, RFootY, RFootZ, RKneeOut, RFootYaw, RFootPitch, RFootFlat, RLegReach, RKickYaw, RKickPitch, RKickReach, RAnchor,
    }

    // A full set of dials. Keyframes, blends and the animator's working buffers are all PoseSpecs; blending two
    // poses is blending their dials (then solving once), which keeps limbs the right length and joints bending
    // the right way through any blend.
    [Serializable]
    public class PoseSpec
    {
        public const int ChannelCount = (int)PoseChannel.RAnchor + 1;
        public const int ArmChannels = 7;
        public const int LegChannels = 12;

        public float[] Values = new float[ChannelCount];

        public float this[PoseChannel channel]
        {
            get => Values[(int)channel];
            set => Values[(int)channel] = value;
        }

        public PoseSpec Clone()
        {
            var copy = new PoseSpec();
            copy.CopyFrom(this);
            return copy;
        }

        public void CopyFrom(PoseSpec other)
        {
            if (other == null || other == this) return;
            EnsureSize();
            other.EnsureSize();
            Array.Copy(other.Values, Values, ChannelCount);
        }

        // Unity may hand back an array of the wrong length after the channel list grows (old saved data).
        public void EnsureSize()
        {
            if (Values != null && Values.Length == ChannelCount) return;
            var fresh = new float[ChannelCount];
            if (Values != null) Array.Copy(Values, fresh, Math.Min(Values.Length, ChannelCount));
            Values = fresh;
        }

        // result = a + (b - a) * t for every dial. result may be a or b.
        public static void Lerp(PoseSpec a, PoseSpec b, float t, PoseSpec result)
        {
            float[] av = a.Values, bv = b.Values, rv = result.Values;
            for (int i = 0; i < ChannelCount; i++) rv[i] = av[i] + (bv[i] - av[i]) * t;
        }

        // Lower body (hips height/position and both legs) from 'lower', everything else from 'upper'.
        // Used for upper-body actions (guard, drinking) that let the legs keep walking.
        public static void Merge(PoseSpec upper, PoseSpec lower, PoseSpec result)
        {
            for (int i = 0; i < ChannelCount; i++)
            {
                result.Values[i] = IsLowerBody((PoseChannel)i) ? lower.Values[i] : upper.Values[i];
            }
        }

        public static bool IsLowerBody(PoseChannel c)
        {
            if (c >= PoseChannel.LFootX) return true;
            return c == PoseChannel.HipsX || c == PoseChannel.HipsY || c == PoseChannel.HipsZ
                   || c == PoseChannel.PelvisPitch || c == PoseChannel.PelvisRoll || c == PoseChannel.LegFrame;
        }

        // Channel of one side's arm or leg dial: e.g. Arm(Right, 2) = RArmReach.
        public static PoseChannel Arm(BodySide side, int offset)
        {
            return (PoseChannel)((int)(side == BodySide.Left ? PoseChannel.LArmYaw : PoseChannel.RArmYaw) + offset);
        }

        public static PoseChannel Leg(BodySide side, int offset)
        {
            return (PoseChannel)((int)(side == BodySide.Left ? PoseChannel.LFootX : PoseChannel.RFootX) + offset);
        }

        public bool IsFinite()
        {
            for (int i = 0; i < ChannelCount; i++)
            {
                if (!AnimMath.IsFinite(Values[i])) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- authoring helpers (readable keyframes)

        public PoseSpec Hips(float x, float y, float z)
        {
            this[PoseChannel.HipsX] = x;
            this[PoseChannel.HipsY] = y;
            this[PoseChannel.HipsZ] = z;
            return this;
        }

        public PoseSpec Pelvis(float pitch, float yaw, float roll = 0f)
        {
            this[PoseChannel.PelvisPitch] = pitch;
            this[PoseChannel.PelvisYaw] = yaw;
            this[PoseChannel.PelvisRoll] = roll;
            return this;
        }

        public PoseSpec Torso(float pitch, float yaw, float roll = 0f)
        {
            this[PoseChannel.TorsoPitch] = pitch;
            this[PoseChannel.TorsoYaw] = yaw;
            this[PoseChannel.TorsoRoll] = roll;
            return this;
        }

        public PoseSpec Head(float pitch, float yaw, float roll = 0f)
        {
            this[PoseChannel.HeadPitch] = pitch;
            this[PoseChannel.HeadYaw] = yaw;
            this[PoseChannel.HeadRoll] = roll;
            return this;
        }

        public PoseSpec Set(PoseChannel channel, float value)
        {
            this[channel] = value;
            return this;
        }

        // Aim an arm: yaw (+ = outward), pitch (+ = up), reach (1 = straight), elbow swivel (+ = elbow out/up),
        // hand roll (+ = thumb turns up/out), wrist (+ = bends back, 80-90 for a palm strike).
        public PoseSpec Arm(BodySide side, float yaw, float pitch, float reach, float elbowOut = 0f, float handRoll = 0f, float wrist = 0f)
        {
            this[Arm(side, 0)] = yaw;
            this[Arm(side, 1)] = pitch;
            this[Arm(side, 2)] = reach;
            this[Arm(side, 3)] = elbowOut;
            this[Arm(side, 4)] = handRoll;
            this[Arm(side, 5)] = wrist;
            return this;
        }

        public PoseSpec Shrug(BodySide side, float degrees)
        {
            this[Arm(side, 6)] = degrees;
            return this;
        }

        // Plant a foot: ankle position (x outward, y height, z forward), toe-out yaw, pitch (+ = toes down, heel up).
        // flat 1 = the sole stays level with the ground; anchor 1 = it stays put on the ground while the body lunges.
        public PoseSpec Foot(BodySide side, float x, float y, float z, float yaw = 0f, float pitch = 0f, float kneeOut = 0f,
            float flat = 1f, float anchor = 0f)
        {
            this[Leg(side, 0)] = x;
            this[Leg(side, 1)] = y;
            this[Leg(side, 2)] = z;
            this[Leg(side, 3)] = kneeOut;
            this[Leg(side, 4)] = yaw;
            this[Leg(side, 5)] = pitch;
            this[Leg(side, 6)] = flat;
            this[Leg(side, 7)] = 0f;
            this[Leg(side, 11)] = anchor;
            return this;
        }

        // Aim a leg from the hip (a kick): yaw (+ = outward), pitch (0 = straight ahead at hip height, -90 = down),
        // reach (1 = straight), foot pitch (+ = toes pointed, - = toes pulled back to strike with the ball of the foot).
        public PoseSpec Kick(BodySide side, float yaw, float pitch, float reach, float kneeOut = 0f, float footPitch = 0f)
        {
            this[Leg(side, 3)] = kneeOut;
            this[Leg(side, 5)] = footPitch;
            this[Leg(side, 6)] = 0f;
            this[Leg(side, 7)] = 1f;
            this[Leg(side, 8)] = yaw;
            this[Leg(side, 9)] = pitch;
            this[Leg(side, 10)] = reach;
            this[Leg(side, 11)] = 0f;
            return this;
        }

        // Swap every left and right dial (a left-lead pose becomes right-lead). Whole-body turns flip sign.
        public PoseSpec Mirrored()
        {
            var m = Clone();
            for (int i = 0; i < ArmChannels; i++)
            {
                m[Arm(BodySide.Left, i)] = this[Arm(BodySide.Right, i)];
                m[Arm(BodySide.Right, i)] = this[Arm(BodySide.Left, i)];
            }
            for (int i = 0; i < LegChannels; i++)
            {
                m[Leg(BodySide.Left, i)] = this[Leg(BodySide.Right, i)];
                m[Leg(BodySide.Right, i)] = this[Leg(BodySide.Left, i)];
            }
            m[PoseChannel.HipsX] = -this[PoseChannel.HipsX];
            m[PoseChannel.RootYaw] = -this[PoseChannel.RootYaw];
            m[PoseChannel.PelvisYaw] = -this[PoseChannel.PelvisYaw];
            m[PoseChannel.PelvisRoll] = -this[PoseChannel.PelvisRoll];
            m[PoseChannel.TorsoYaw] = -this[PoseChannel.TorsoYaw];
            m[PoseChannel.TorsoRoll] = -this[PoseChannel.TorsoRoll];
            m[PoseChannel.HeadYaw] = -this[PoseChannel.HeadYaw];
            m[PoseChannel.HeadRoll] = -this[PoseChannel.HeadRoll];
            return m;
        }
    }
}
