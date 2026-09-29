using System;

namespace VaatusRevenge.Core
{
    // Guard (hold) and Deflect (press just before a hit lands). Deflect is a well-timed guard, never a
    // "redirect" (lightning redirection is a much later invention in the lore).
    [Serializable]
    public class GuardSettings
    {
        public float ArcDegrees = 160f;              // full width of the protected front
        public float MoveSpeedMultiplier = 0.45f;    // share of normal speed while guarding
        public float GuardBreakStagger = 1.0f;       // stunned time when a blocked hit costs more stamina than you have
        public float DeflectWindow = 0.15f;          // a guard press this recent deflects a parryable hit
        public float DeflectWhiffLockout = 0.35f;    // a press that deflects nothing disables deflecting this long (no mashing)
        public float DeflectMomentumGain = 25f;
    }
}
