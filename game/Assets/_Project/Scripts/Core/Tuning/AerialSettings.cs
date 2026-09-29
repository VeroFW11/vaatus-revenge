using System;

namespace VaatusRevenge.Core
{
    // The aerial game (Spider-Man style): hold attack on the ground to launch an enemy and follow it up, attack in
    // the air for an air combo that keeps you both up, dash once in the air, and finish with a slam or a plunge.
    [Serializable]
    public class AerialSettings
    {
        public float LauncherHoldTime = 0.25f;       // attack held this long (on the ground, during the first chain hit) = launcher
        public float AirAttackGravityScale = 0.3f;   // gravity while an air attack runs: you hang in the air as you strike
        public int AirAttacksPerJump = 6;
        public float AirLiftMaxHeightAboveTarget = 0.6f; // air strikes stop lifting you once your feet are this far above the target's
                                                     // (a standing foe: you don't float over its head; a juggled one rises with you)            // air strikes allowed before touching the ground again (no infinite hovering)
        public int AirDashesPerJump = 1;             // Flame Step dashes allowed in the air before landing (0 = none)
        public float AirDashDistance = 3.4f;
        public float AirDashDuration = 0.22f;        // no gravity while dashing
        public float AirDashIFrameEnd = 0.14f;       // invincible from the start of an air dash until this
    }
}
