using System;

namespace VaatusRevenge.Core
{
    // Jump attack (Fire: Falling Axe Kick): hang for a moment, drop fast, burst a ring of fire on landing.
    [Serializable]
    public class PlungeSettings
    {
        public float HangTime = 0.08f;               // brief pause in the air before the drop (sells the wind-up)
        public float FallSpeed = 18f;                // metres per second straight down
        public float RingRadius = 2.2f;              // landing burst hits everything this close
        public float MaxFallTime = 2f;               // safety net: after falling this long without landing, the plunge gives up
    }
}
