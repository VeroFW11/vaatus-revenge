using System;

namespace VaatusRevenge.Core
{
    // Tuning for anything that flies: fire blasts, crossbow bolts. Lives inside move data assets.
    [Serializable]
    public class ProjectileSpec
    {
        public float Speed = 28f;            // metres per second
        public float Radius = 0.3f;          // collision radius in metres
        public float MaxRange = 25f;         // despawns after travelling this far
        public float Gravity = 0f;           // metres per second squared pulling it down (0 = flies straight)
        public float ExplosionRadius = 0f;   // > 0: also damages everything this close to the impact point
        public float VisualScale = 1f;       // size of the grey-box effect
    }
}
