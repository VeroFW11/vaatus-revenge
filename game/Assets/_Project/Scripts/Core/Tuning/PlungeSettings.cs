using System;

namespace VaatusRevenge.Core
{
    // Jump attack (Fire: Falling Axe Kick): hang for a moment, drop fast, burst a ring of fire on landing.
    [Serializable]
    public class PlungeSettings
    {
        public float MinAirTime = 0.25f;             // light/heavy in the air only plunges after this long off the ground; earlier
                                                     // presses do nothing, so jump-plunge can't be spammed from a hop
        public float HangTime = 0.08f;               // brief pause in the air before the drop (sells the wind-up)
        public float FallSpeed = 18f;                // metres per second straight down
        public float RingRadius = 2.2f;              // landing burst hits everything this close
        public float MaxFallTime = 2f;               // safety net: after falling this long without landing, the plunge gives up
    }
}
