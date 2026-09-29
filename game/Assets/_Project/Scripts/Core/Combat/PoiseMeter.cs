using System;

namespace VaatusRevenge.Core
{
    // Poise is stagger resistance. Each hit's PoiseDamage wears it down; at zero the fighter is staggered
    // (stunned for a moment) and poise refills. Small hits can be shrugged off, heavy ones break through.
    public sealed class PoiseMeter
    {
        float sinceDamage;

        public float Current { get; private set; }

        public void Reset(float max)
        {
            Current = Math.Max(0f, max);
            sinceDamage = 0f;
        }

        // Returns true when this damage broke poise. The meter refills to max straight away so the next
        // stagger needs a fresh build-up (no stun-locking).
        public bool Damage(float amount, float max)
        {
            if (!(amount > 0f)) return false;
            sinceDamage = 0f;
            Current -= amount;
            if (Current > 0f) return false;
            Current = Math.Max(0f, max);
            return true;
        }

        public void Tick(float dt, float max, float regenDelay, float regenPerSecond)
        {
            if (!(dt > 0f)) return;
            sinceDamage += dt;
            if (sinceDamage >= regenDelay && regenPerSecond > 0f) Current += regenPerSecond * dt;
            Current = Angles.Clamp(Current, 0f, Math.Max(0f, max));
        }
    }
}
