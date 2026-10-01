using System;

namespace VaatusRevenge.Core
{
    // MIX: the multi-element combo bonus. Every distinct element that LANDS a clean hit in the current combo raises the
    // MIX level (1-4); switching alone earns nothing, and going back and forth between two elements stays at 2. While
    // the combo lives, all player damage is multiplied by the level's value; a string finisher at MIX 2+ hits harder,
    // at 3+ launches, at 4 breaks the foe's guard (poise) and refills the elements' identity meters. Poise is never
    // multiplied by MIX. Defaults are the Fluid preset.
    [Serializable]
    public class MixTuning
    {
        public float[] DamageByLevel = { 1.00f, 1.10f, 1.20f, 1.30f };   // index 0 = MIX 1 ... index 3 = MIX 4
        public float FinisherDamage = 1.20f;         // MIX >= 2: main or pause finisher damage multiplier
        public float FinisherLaunchSpeed = 10f;      // MIX >= 3: a finisher launches a launchable foe at this speed
        public bool FinisherPoiseBreak = true;       // MIX 4: a finisher breaks poise (stagger immunity and break-out armour still hold)
        public float FinisherMeterRefill = 0.5f;     // MIX 4: each element's identity meter refills by this share of its maximum

        // The damage multiplier for a MIX level (0 or 1 = none).
        public float DamageFor(int level)
        {
            if (DamageByLevel == null || DamageByLevel.Length == 0 || level <= 0) return 1f;
            int index = Math.Min(level, DamageByLevel.Length) - 1;
            float value = DamageByLevel[index];
            return value > 0f ? value : 1f;
        }

        public static MixTuning CreatePunishing()
        {
            return new MixTuning
            {
                DamageByLevel = new[] { 1.00f, 1.08f, 1.15f, 1.22f },
                FinisherDamage = 1.15f, FinisherLaunchSpeed = 8f, FinisherPoiseBreak = true, FinisherMeterRefill = 0.3f
            };
        }
    }
}
