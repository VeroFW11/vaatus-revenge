using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Generates walk / run / sprint poses from ground speed and a cycle phase (0..1, one full cycle = two steps).
    //
    // The trick that keeps feet from skating: while a foot is on the ground it moves backwards (in the
    // fighter's own frame) at exactly the fighter's speed, so in the world it stays still. Stride length is
    // speed / cadence, so a faster fighter takes longer AND quicker steps, like a real runner. Everything
    // else (pelvis bounce, arm swing against the legs, forward lean) is layered on in step with the feet.
    public static class GaitGenerator
    {
        const float TwoPi = MathF.PI * 2f;

        // phase: cycle position (left foot touches down at 0, right at 0.5). direction: unit travel direction in
        // the fighter's frame (x right, z forward). speed: m/s. stance: the standing pose to build on (its upper
        // body is used at low speed). result may not be stance.
        public static void Evaluate(float phase, float speed, Vector2 direction, GaitSettings g, PoseSpec stance, PoseSpec result, float armSwing = 1f)
        {
            result.CopyFrom(stance);
            speed = Math.Max(0f, speed);
            float cadence = g.Cadence(speed);
            float stride = speed / cadence;
            float run = g.RunBlend(speed);
            float stanceShare = AnimMath.Lerp(g.WalkStance, g.RunStance, run);
            float lift = AnimMath.Lerp(g.WalkLift, g.RunLift, run) * AnimMath.Clamp01(speed / Math.Max(0.1f, g.WalkSpeed));
            float half = stanceShare * stride * 0.5f;
            Vector2 dir = direction.LengthSquared() > 1e-4f ? Vector2.Normalize(direction) : new Vector2(0f, 1f);
            float forwardness = dir.Y;
            float sideways = Math.Abs(dir.X);

            for (int s = 0; s < 2; s++)
            {
                BodySide side = s == 0 ? BodySide.Left : BodySide.Right;
                float sign = (float)side;
                float p = Wrap01(phase + (s == 0 ? 0f : 0.5f));
                FootCycle(p, stanceShare, half, lift, run, out float along, out float height, out float pitch);
                float width = g.FootWidth + sideways * g.StrafeWidth;
                float x = sign * width + dir.X * along;
                float z = dir.Y * along;
                result.Foot(side, x * sign, 0.08f + height, z, 6f * (1f - run), pitch, 0f, 1f, 0f);
            }

            // Pelvis: lower when running, bouncing twice per cycle (lowest as each foot takes the weight).
            float bob = AnimMath.Lerp(g.WalkBob, g.RunBob, run);
            float drop = AnimMath.Lerp(g.WalkHipsDrop, g.RunHipsDrop, run);
            float bounce = 0.5f + 0.5f * MathF.Cos(TwoPi * 2f * (phase - stanceShare * 0.5f));
            result[PoseChannel.HipsX] = 0f;
            result[PoseChannel.HipsZ] = 0f;
            result[PoseChannel.HipsY] = -drop - bob * bounce;

            // The pelvis swings with the forward leg; the chest counter-rotates (the natural twist of walking).
            float swing = MathF.Cos(TwoPi * phase);    // +1 = left foot forward
            float pelvisYaw = g.PelvisSwing * swing * forwardness;
            result.Pelvis(0f, pelvisYaw, 0f);
            float lean = Math.Min(g.MaxLean, g.LeanPerSpeed * speed);
            result.Torso(lean * forwardness, -pelvisYaw * g.TorsoCounter, -lean * 0.4f * dir.X);
            result[PoseChannel.HeadPitch] = -lean * 0.5f * forwardness;
            result[PoseChannel.HeadYaw] = 0f;
            result[PoseChannel.LookFront] = 1f;
            result[PoseChannel.ArmFollow] = 0.6f;
            result[PoseChannel.LegFrame] = 0f;
            result[PoseChannel.RootYaw] = 0f;

            // Arms swing against the legs: the right arm comes forward with the left foot. Walking keeps a loose
            // guard; running pumps bent arms.
            float amp = AnimMath.Lerp(g.WalkArmSwing, g.RunArmSwing, run);
            float reach = AnimMath.Lerp(g.WalkArmReach, g.RunArmReach, run);
            float basePitch = AnimMath.Lerp(-80f, -70f, run);
            float armBlend = AnimMath.Clamp01(speed / Math.Max(0.1f, g.WalkSpeed)) * AnimMath.Clamp01(armSwing);
            for (int s = 0; s < 2; s++)
            {
                BodySide side = s == 0 ? BodySide.Left : BodySide.Right;
                float armPhase = side == BodySide.Right ? swing : -swing;
                float pitch = basePitch + amp * armPhase * Math.Max(0.3f, forwardness);
                float yaw = AnimMath.Lerp(4f, -12f, run);
                float elbow = AnimMath.Lerp(5f, 12f, run);
                PoseChannel yawC = PoseSpec.Arm(side, 0), pitchC = PoseSpec.Arm(side, 1), reachC = PoseSpec.Arm(side, 2);
                result[yawC] = AnimMath.Lerp(stance[yawC], yaw, armBlend);
                result[pitchC] = AnimMath.Lerp(stance[pitchC], pitch, armBlend);
                result[reachC] = AnimMath.Lerp(stance[reachC], reach, armBlend);
                result[PoseSpec.Arm(side, 3)] = AnimMath.Lerp(stance[PoseSpec.Arm(side, 3)], elbow, armBlend);
                result[PoseSpec.Arm(side, 4)] = AnimMath.Lerp(stance[PoseSpec.Arm(side, 4)], 0f, armBlend);
                result[PoseSpec.Arm(side, 5)] = AnimMath.Lerp(stance[PoseSpec.Arm(side, 5)], 0f, armBlend);
            }
        }

        // One foot through the cycle: on the ground (stance) it slides back under the body at ground speed,
        // then it lifts, swings forward and reaches out for the next step.
        static void FootCycle(float p, float stanceShare, float half, float lift, float run, out float along, out float height, out float pitch)
        {
            if (p < stanceShare)
            {
                float u = p / Math.Max(1e-3f, stanceShare);
                along = half - 2f * half * u;
                height = 0f;
                // Heel strikes toes-up, rolls flat, and peels off the ground toes-down at push-off. When the foot tips,
                // the ankle rises so the ball of the foot (or the heel) stays on the floor instead of sinking into it.
                pitch = u < 0.2f ? AnimMath.Lerp(-12f, 0f, u / 0.2f) : (u > 0.7f ? AnimMath.Lerp(0f, 25f + 20f * run, (u - 0.7f) / 0.3f) : 0f);
                height = FootTipRise(pitch);
                return;
            }
            float q = (p - stanceShare) / Math.Max(1e-3f, 1f - stanceShare);
            along = -half + 2f * half * AnimMath.SmoothStep(q);
            // The swing peaks early (the heel kicks up behind when running) and reaches forward to land.
            // The lift eases in and out (sin^1.5), so the foot skims the floor at the start and end of the swing
            // rather than hovering, and peaks early (the heel kicks up behind when running).
            float arc = MathF.Pow(Math.Max(0f, MathF.Sin(MathF.PI * MathF.Pow(q, 0.75f))), 1.5f);
            float pitchNow = AnimMath.Lerp(45f * run + 20f, -12f, AnimMath.SmoothStep(q));
            height = Math.Max(lift * arc, FootTipRise(pitchNow));
            along -= run * half * 0.35f * MathF.Sin(MathF.PI * q) * (1f - q);
            pitch = pitchNow;
        }

        // How far the ankle must rise for a foot tipped by pitch degrees to keep its lowest point on the floor
        // (toes down: the ball of the foot, 13 cm ahead and 6 cm below the ankle; toes up: the heel).
        static float FootTipRise(float pitch)
        {
            float a = Math.Abs(pitch) * AnimMath.Deg2Rad;
            float reach = pitch > 0f ? 0.13f : 0.07f;
            float drop = reach * MathF.Sin(a) + 0.06f * MathF.Cos(a);
            return Math.Max(0f, drop - 0.06f);
        }

        static float Wrap01(float x)
        {
            x %= 1f;
            return x < 0f ? x + 1f : x;
        }
    }
}
