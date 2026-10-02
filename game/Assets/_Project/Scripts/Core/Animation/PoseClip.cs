using System;

namespace VaatusRevenge.Core
{
    // When a keyframe happens. Attack keys are pinned to the move's own frame data rather than to seconds, so
    // retuning a move's Startup in the tuning asset moves the whole animation with it and the strike still
    // reaches full extension exactly when the hit can land:
    //   Startup  : At x Startup                       (0 = the move starts, 1 = the first active frame)
    //   Active   : Startup + At x Active              (the strike is live; 1 = active ends)
    //   Interval : Startup + At x (HitCount-1) x HitInterval   (multi-hit attacks: 1 = the last strike lands)
    //   Recovery : last active end + At x Recovery    (1 = back to neutral)
    //   Seconds  : At seconds from the start          (held states: guard, charge, knockdown, death)
    //   Cycle    : At x the loop period                (looping clips: idle breathing, tumbling)
    public enum KeyPhase { Startup, Active, Interval, Recovery, Seconds, Cycle }

    // Action: plays once over its timing and holds its last key. Hold: keys in seconds, the last one is held
    // for as long as the state lasts. Loop: repeats every LoopPeriod seconds.
    public enum ClipMode { Action, Hold, Loop }

    [Serializable]
    public class PoseKeyframe
    {
        public KeyPhase Phase = KeyPhase.Startup;
        public float At;
        public PoseEase Ease = PoseEase.InOut;       // how the pose eases in from the previous key
        public PoseSpec Pose = new PoseSpec();
    }

    // The timing an action is played with: a move's frame data, or a state's length.
    public struct ClipTiming
    {
        public float Startup;
        public float Active;
        public float Recovery;
        public int HitCount;
        public float HitInterval;

        public float LastActiveEnd => Startup + Math.Max(0, HitCount - 1) * Math.Max(0f, HitInterval) + Active;
        public float Total => LastActiveEnd + Recovery;

        // A player move's frame data. A multi-hit move (MoveData.HitCount > 1) spreads its sub-hits over its Active time,
        // sub-hit k landing at Startup + k x HitInterval and the last one live until the move's ActiveEnd. As clip timing
        // that is HitCount strikes HitInterval apart with Active = the last sub-hit's live time, so Interval keys land on
        // each sub-hit and LastActiveEnd is the move's own ActiveEnd.
        public static ClipTiming FromMove(MoveData move)
        {
            if (move == null) return default;
            int hits = Math.Max(1, move.HitCount);
            float interval = hits > 1 ? Math.Max(0f, move.HitInterval) : 0f;
            float lastActive = Math.Max(0f, move.Active - (hits - 1) * interval);
            return new ClipTiming { Startup = move.Startup, Active = lastActive, Recovery = move.Recovery, HitCount = hits, HitInterval = interval };
        }

        public static ClipTiming FromShares(float duration, float startupShare, float activeShare)
        {
            duration = Math.Max(0f, duration);
            float startup = duration * AnimMath.Clamp01(startupShare);
            float active = duration * AnimMath.Clamp01(activeShare);
            return new ClipTiming { Startup = startup, Active = active, Recovery = Math.Max(0f, duration - startup - active), HitCount = 1 };
        }
    }

    // One animation: a named list of keyframes (a martial-arts move or a state), how it's timed, and how it
    // blends in. The pose between keys is an eased blend of the two keys' dials.
    [Serializable]
    public class PoseClip
    {
        public string Key = "";
        public ClipMode Mode = ClipMode.Action;
        // Seconds per loop (Loop clips).
        public float LoopPeriod = 1f;
        // Seconds this state lasts when nothing says otherwise (a flinch, a landing, a parry flourish).
        public float DefaultDuration = 0.5f;
        // For states without frame data: share of the duration used as 'startup' and 'active' for the key phases.
        public float StartupShare = 0.3f;
        public float ActiveShare = 0.3f;
        // Seconds to blend from the previous pose into this one. Strikes clamp it so they still land on time.
        public float FadeIn = 0.1f;
        // Arms, torso and head only: the legs keep doing what locomotion wants (guard while moving, drinking).
        public bool UpperBodyOnly;
        // Arms and prop tilt up/down toward the target (crossbow aim).
        public bool Aims;
        // The body rides something across the floor (a surf on water or earth): the feet glide on purpose, so a fast
        // grounded rush in this clip is never given running steps (FighterAnimator's lunge stride).
        public bool Glides;
        // A flying strike that leaves the ground on purpose (a zip kick, a sprint kick, a leap): its feet are meant to be
        // off the floor, so the landing ground fit (FighterAnimator, J5-04) leaves its legs alone.
        public bool Leaps;
        // A plunge whose recovery lands its own legs (the axe kick's heel chop, the earthquake drop's stamp): the feed starts
        // that recovery just before the impact (PlayerAnimationFeed.PlungeLandShare), so the landing ground fit leaves its
        // legs alone whenever the floor was known on the way down (J6-01). Its first Recovery key at PlungeLandShare is the
        // pose on the floor.
        public bool LandsItself;
        // Idle clips only: how much the arms swing when walking or running (0 = keep the weapon guard up).
        public float ArmSwing = 1f;
        public PoseKeyframe[] Keys = new PoseKeyframe[0];

        public bool IsValid => Keys != null && Keys.Length > 0 && !string.IsNullOrEmpty(Key);

        // Seconds from the start at which key i happens (never earlier than the key before it).
        public float KeyTime(int i, in ClipTiming timing)
        {
            PoseKeyframe k = Keys[i];
            float t;
            switch (k.Phase)
            {
                case KeyPhase.Startup: t = k.At * timing.Startup; break;
                case KeyPhase.Active: t = timing.Startup + k.At * timing.Active; break;
                case KeyPhase.Interval: t = timing.Startup + k.At * Math.Max(0, timing.HitCount - 1) * Math.Max(0f, timing.HitInterval); break;
                case KeyPhase.Recovery: t = timing.LastActiveEnd + k.At * timing.Recovery; break;
                case KeyPhase.Cycle: t = k.At * Math.Max(1e-3f, LoopPeriod); break;
                default: t = k.At; break;
            }
            return AnimMath.IsFinite(t) ? t : 0f;
        }

        // The pose at 'time' seconds into the clip. Allocation-free.
        public void Evaluate(float time, in ClipTiming timing, PoseSpec result)
        {
            if (!IsValid)
            {
                return;
            }
            int count = Keys.Length;
            if (count == 1)
            {
                result.CopyFrom(Keys[0].Pose);
                return;
            }
            if (!AnimMath.IsFinite(time) || time < 0f) time = 0f;

            if (Mode == ClipMode.Loop)
            {
                float period = Math.Max(1e-3f, LoopPeriod);
                time %= period;
                // Wraps: after the last key it blends back into the first one at the end of the period.
                float previousTime = KeyTime(count - 1, timing) - period;
                int previous = count - 1;
                for (int i = 0; i < count; i++)
                {
                    float t = KeyTime(i, timing);
                    if (time < t)
                    {
                        Blend(previous, i, previousTime, t, time, result);
                        return;
                    }
                    previous = i;
                    previousTime = t;
                }
                Blend(count - 1, 0, previousTime, period + KeyTime(0, timing), time, result);
                return;
            }

            float before = float.NegativeInfinity;
            float lastTime = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = Math.Max(KeyTime(i, timing), before);
                if (time < t)
                {
                    if (i == 0)
                    {
                        result.CopyFrom(Keys[0].Pose);
                        return;
                    }
                    Blend(i - 1, i, lastTime, t, time, result);
                    return;
                }
                before = t;
                lastTime = t;
            }
            result.CopyFrom(Keys[count - 1].Pose);
        }

        void Blend(int a, int b, float ta, float tb, float time, PoseSpec result)
        {
            float span = tb - ta;
            float u = span > 1e-5f ? (time - ta) / span : 1f;
            float eased = PoseEasing.Apply(Keys[b].Ease, u);
            PoseSpec.Lerp(Keys[a].Pose, Keys[b].Pose, eased, result);
        }

        public ClipTiming DefaultTiming(float duration)
        {
            if (!(duration > 0f)) duration = DefaultDuration;
            return ClipTiming.FromShares(duration, StartupShare, ActiveShare);
        }
    }
}
