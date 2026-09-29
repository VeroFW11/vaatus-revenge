using System;

namespace VaatusRevenge.Core
{
    // Stamina with the souls-like rules:
    //  - any stamina above zero lets an action start, and the cost then clamps at zero (never negative),
    //    so you can always squeeze out one last move but you'll be empty afterwards;
    //  - spending pauses regeneration for a moment (the "regen delay"), which is what makes mashing costly;
    //  - emptying the bar pauses it for longer (the "empty regen delay"), so running dry is a real mistake.
    // Limits and rates are passed in on every call so live tuning edits apply immediately.
    public sealed class StaminaMeter
    {
        float regenPause;

        public float Current { get; private set; }
        public bool CanAct => Current > 0f;

        public void Reset(float max)
        {
            Current = Math.Max(0f, max);
            regenPause = 0f;
        }

        public void Set(float value, float max)
        {
            Current = Angles.Clamp(value, 0f, Math.Max(0f, max));
        }

        public void Spend(float amount, float regenDelay)
        {
            Spend(amount, regenDelay, regenDelay);
        }

        public void Spend(float amount, float regenDelay, float emptyRegenDelay)
        {
            if (!(amount > 0f)) return;
            Current = Math.Max(0f, Current - amount);
            float pause = Current > 0f ? regenDelay : Math.Max(regenDelay, emptyRegenDelay);
            regenPause = Math.Max(regenPause, pause);
        }

        // Continuous spending (sprinting): keeps regen paused while it lasts.
        public void Drain(float perSecond, float dt, float regenDelay)
        {
            Drain(perSecond, dt, regenDelay, regenDelay);
        }

        public void Drain(float perSecond, float dt, float regenDelay, float emptyRegenDelay)
        {
            if (!(perSecond > 0f) || !(dt > 0f)) return;
            Spend(perSecond * dt, regenDelay, emptyRegenDelay);
        }

        public void Tick(float dt, float max, float regenPerSecond)
        {
            if (!(dt > 0f)) return;
            if (regenPause > 0f)
            {
                regenPause -= dt;
                if (regenPause > 0f) return;
                dt = -regenPause;   // regen for the part of the frame after the pause ended
                regenPause = 0f;
            }
            if (regenPerSecond > 0f) Current += regenPerSecond * dt;
            Current = Angles.Clamp(Current, 0f, Math.Max(0f, max));
        }
    }
}
