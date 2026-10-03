using System;

namespace VaatusRevenge
{
    // Maps "how far into the move we are" (our frame data) onto "which moment of the mocap clip to show".
    //
    // The combat rules decide when a strike can hit (MoveData.Startup = the impact moment). A mocap jab has its own
    // idea of when the fist is fully out, and its own length. If we simply played the clip at its natural speed,
    // the fist would be extended visibly early or late compared with when the hit registers, and players read that
    // as "the hitbox is wrong". So the clip is time-warped, piecewise-linearly, around the impact moment:
    //
    //   cue time [0, ImpactTime]        -> clip [Start, Impact]    (the wind-up, sped up or slowed down)
    //   cue time [ImpactTime, Duration] -> clip [Impact, End]      (the follow-through)
    //
    // so the clip's fully-extended frame (Impact) shows exactly on the frame the hit becomes live. Start and End are
    // the trim; all clip times here are normalised (0..1 of the clip's length).
    //
    // Cue times already follow scaled time (hitstop freezes them, slow motion slows them), so the clip does too.
    public static class MecanimTimeWarp
    {
        // Normalised clip time for an action cue. clipLength (seconds) is used only when the cue is open-ended
        // (Duration <= 0, e.g. a held block): past the impact, or with no impact, the clip then runs at its own speed.
        public static float ActionNormalizedTime(float cueTime, float duration, float impactTime,
            float start, float impact, float end, float clipLength, bool loops)
        {
            float t = Math.Max(0f, cueTime);
            bool hasImpact = impactTime > 0f;

            if (hasImpact && t <= impactTime)
                return Lerp(start, impact, t / impactTime);

            if (duration > 0f)
            {
                if (hasImpact)
                {
                    float followThrough = duration - impactTime;
                    if (followThrough <= 1e-5f) return impact;              // move ends at the impact: hold it
                    return Lerp(impact, end, (t - impactTime) / followThrough);
                }
                return Lerp(start, end, t / duration);                     // no impact moment: stretch the whole trim
            }

            // Open-ended: natural speed from the impact (or from the start), holding the last frame or looping.
            float from = hasImpact ? impact : start;
            float elapsed = hasImpact ? t - impactTime : t;
            float normalised = from + (clipLength > 1e-4f ? elapsed / clipLength : 0f);
            if (normalised <= end) return normalised;
            if (!loops) return end;
            float span = end - start;
            if (span <= 1e-4f) return end;
            return start + Repeat(normalised - start, span);
        }

        // Normalised clip time for a looping locomotion cycle at a phase (0..1 of the trimmed cycle).
        public static float LoopNormalizedTime(float phase, float start, float end)
        {
            return start + Repeat(phase, 1f) * (end - start);
        }

        static float Lerp(float a, float b, float t)
        {
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            return a + (b - a) * t;
        }

        static float Repeat(float value, float length)
        {
            float r = value - (float)Math.Floor(value / length) * length;
            return r < 0f ? 0f : (r >= length ? 0f : r);
        }
    }
}
