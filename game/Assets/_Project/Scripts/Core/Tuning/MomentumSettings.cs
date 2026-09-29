using System;

namespace VaatusRevenge.Core
{
    // Fire's identity mechanic, from Northern Shaolin's relentless forward pressure: landing hits builds
    // Momentum and Momentum boosts damage; going quiet or backing off drains it. Other elements can turn
    // it off (Enabled = false) and get their own identity mechanic later.
    [Serializable]
    public class MomentumSettings
    {
        public bool Enabled = true;
        public float Max = 100f;
        public float DecayDelay = 1.6f;              // draining starts this long after the last gain...
        public float DecayRate = 20f;                // ...at this many points per second
        public float BackOffDrainRate = 35f;         // drain per second while locked on and moving away from the target
        public float BackOffSpeedThreshold = 1.0f;   // m/s away from the target that counts as backing off
        public float BackOffRadius = 6f;             // not locked on: backing away from the nearest enemy this close also drains
        public float MaxDamageMultiplier = 1.4f;     // damage x1.0 at 0 Momentum, rising evenly to this at Max
    }
}
