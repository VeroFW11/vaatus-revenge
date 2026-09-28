using UnityEngine;

namespace VaatusRevenge
{
    // One limb's procedural strike: shoots out to a target with a fast ease-out, holds, then eases back to
    // rest. Positions are in the limb's parent space. The rig ticks it with scaled time, so during hitstop
    // a punch stays frozen at full extension, which is a big part of what makes an impact feel solid.
    public class GreyboxLimbMotion
    {
        Vector3 from;
        Vector3 target;
        float extendTime;
        float holdTime;
        float retractTime;
        float time;

        public bool Active { get; private set; }

        public void Begin(Vector3 current, Vector3 targetLocal, float extend, float hold, float retract)
        {
            from = current;
            target = targetLocal;
            extendTime = Mathf.Max(0f, extend);
            holdTime = Mathf.Max(0f, hold);
            retractTime = Mathf.Max(0f, retract);
            time = 0f;
            Active = true;
        }

        // Eases back to rest from wherever the limb is now (used when a fighter is interrupted).
        public void Release(Vector3 current, float retract)
        {
            if (!Active) return;
            Begin(current, current, 0f, 0f, retract);
        }

        public void Cancel()
        {
            Active = false;
        }

        public Vector3 Evaluate(float deltaTime, Vector3 rest)
        {
            if (!Active) return rest;
            time += Mathf.Max(0f, deltaTime);
            if (time < extendTime) return Vector3.LerpUnclamped(from, target, EaseOutQuart(time / extendTime));
            float t = time - extendTime;
            if (t < holdTime) return target;
            t -= holdTime;
            if (t < retractTime) return Vector3.LerpUnclamped(target, rest, SmoothStep(t / retractTime));
            Active = false;
            return rest;
        }

        // Fast start, soft landing: the limb covers most of the distance in the first third of the time.
        public static float EaseOutQuart(float x)
        {
            float inverse = 1f - Mathf.Clamp01(x);
            return 1f - inverse * inverse * inverse * inverse;
        }

        public static float EaseOutCubic(float x)
        {
            float inverse = 1f - Mathf.Clamp01(x);
            return 1f - inverse * inverse * inverse;
        }

        public static float SmoothStep(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
