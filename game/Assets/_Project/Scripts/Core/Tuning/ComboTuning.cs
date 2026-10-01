using System;

namespace VaatusRevenge.Core
{
    // The hit counter (Spider-Man 2's combo meter, PlayerCombatModel.ComboCount): every clean hit adds one; taking a
    // hit, being staggered or going ComboTimeout without landing anything ends it. Defaults are the Fluid preset.
    [Serializable]
    public class ComboTuning
    {
        public float ComboTimeout = 3.0f;            // seconds without a clean hit before the combo ends

        public static ComboTuning CreatePunishing()
        {
            return new ComboTuning { ComboTimeout = 2.0f };
        }
    }
}
