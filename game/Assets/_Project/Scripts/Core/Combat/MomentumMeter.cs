using System;

namespace VaatusRevenge.Core
{
    // Fire's Momentum meter (rules in MomentumSettings): gains reset the decay delay; after the delay it
    // drains; backing away from a locked target drains it faster straight away.
    public sealed class MomentumMeter
    {
        float sinceGain;

        public float Current { get; private set; }

        public void Reset()
        {
            Current = 0f;
            sinceGain = 0f;
        }

        public void Gain(float amount, MomentumSettings settings)
        {
            if (settings == null || !settings.Enabled || !(amount > 0f)) return;
            Current = Math.Min(Math.Max(0f, settings.Max), Current + amount);
            sinceGain = 0f;
        }

        public void Tick(float dt, MomentumSettings settings, bool backingOff)
        {
            if (!(dt > 0f)) return;
            if (settings == null || !settings.Enabled)
            {
                Current = 0f;
                return;
            }
            sinceGain += dt;
            float rate = 0f;
            if (backingOff) rate = settings.BackOffDrainRate;
            if (sinceGain >= settings.DecayDelay) rate = Math.Max(rate, settings.DecayRate);
            Current = Angles.Clamp(Current - Math.Max(0f, rate) * dt, 0f, Math.Max(0f, settings.Max));
        }

        // x1.0 at zero Momentum up to MaxDamageMultiplier when full.
        public float DamageMultiplier(MomentumSettings settings)
        {
            if (settings == null || !settings.Enabled || !(settings.Max > 0f)) return 1f;
            float fill = Angles.Clamp(Current / settings.Max, 0f, 1f);
            return 1f + (settings.MaxDamageMultiplier - 1f) * fill;
        }
    }
}
