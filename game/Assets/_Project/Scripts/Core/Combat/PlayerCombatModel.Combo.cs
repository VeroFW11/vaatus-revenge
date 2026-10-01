using System;

namespace VaatusRevenge.Core
{
    // The hit counter (Spider-Man 2's combo meter) and MIX (the multi-element bonus).
    public sealed partial class PlayerCombatModel
    {
        readonly ComboTuning fallbackCombo = new ComboTuning();
        readonly MixTuning fallbackMix = new MixTuning();

        int comboCount;
        float comboTimeRemaining;
        int mixMask;                  // bit (1 << (int)ElementId) for every element that landed a clean hit in this combo

        ComboTuning ComboRules => tuning.Combo ?? fallbackCombo;
        MixTuning MixRules => tuning.Mix ?? fallbackMix;

        void ResetCombo()
        {
            comboCount = 0;
            comboTimeRemaining = 0f;
            mixMask = 0;
        }
    }
}
