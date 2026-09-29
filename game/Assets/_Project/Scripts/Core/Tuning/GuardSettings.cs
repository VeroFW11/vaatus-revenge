using System;

namespace VaatusRevenge.Core
{
    // How an element defends with the Guard button. Each element picks its own (it's part of the move set), so
    // Fire can be parry-only while a later element (Earth, say) holds a block.
    public enum DefenseStyle
    {
        // A press parries: for DeflectWindow a parryable hit is deflected, then the stance drops by itself.
        // Holding the button does nothing more, and there is no block: a mistimed parry means you get hit.
        // This is the Spider-Man 2 style the Fire prototype uses.
        ParryOnly,
        // Hold to block hits from the front for stamina; a press just before a hit lands deflects it
        // (Elden Ring / Sekiro style).
        BlockAndParry
    }

    // Guard and Deflect (a parry). Deflect is a well-timed guard, never a "redirect" (lightning redirection is
    // a much later invention in the lore).
    [Serializable]
    public class GuardSettings
    {
        public DefenseStyle Style = DefenseStyle.ParryOnly;
        public float ArcDegrees = 160f;              // BlockAndParry: full width of the protected front
        public float ParryArcDegrees = 360f;         // ParryOnly: full width a parry covers (360 = any direction: there's
                                                     // no lock-on by default, so attacks can come from anywhere)
        public float MoveSpeedMultiplier = 0.45f;    // share of normal speed while guarding or parrying
        public float GuardBreakStagger = 1.0f;       // BlockAndParry: stunned time when a blocked hit costs more stamina than you have
        public float DeflectWindow = 0.15f;          // a guard press this recent deflects a parryable hit
        public float DeflectWhiffLockout = 0.35f;    // a press that deflects nothing disables deflecting this long (no mashing)
        public float DeflectMomentumGain = 25f;

        public bool IsParryOnly => Style == DefenseStyle.ParryOnly;
    }
}
