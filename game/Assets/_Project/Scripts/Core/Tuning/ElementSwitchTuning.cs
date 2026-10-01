using System;

namespace VaatusRevenge.Core
{
    // Changing element (hold RB + a face button). Free when nothing is going on; in the middle of a string it is a
    // switch strike: the next hit comes out in the new element, a little harder. Switching costs no stamina: the
    // cooldown is the limiter. Defaults are the Fluid preset.
    [Serializable]
    public class ElementSwitchTuning
    {
        public float Cooldown = 0.20f;               // seconds after a switch before the next one (short enough for X, switch, X, switch
                                                     // on the beat in every element, Air's quick hits included)
        public float SwitchBufferWindow = 0.25f;     // a switch asked for while busy (charging, healing...) waits this long
        public float SwitchStrikeBeatLateBonus = 0.06f; // a switch strike's beat window stays open this much longer
        public float SwitchStrikeDamageMultiplier = 1.20f; // the switch strike's damage...
        public float SwitchStrikePoiseMultiplier = 1.5f;   // ...and poise damage (that one hit only)

        public static ElementSwitchTuning CreatePunishing()
        {
            return new ElementSwitchTuning
            {
                Cooldown = 0.45f, SwitchBufferWindow = 0.20f, SwitchStrikeBeatLateBonus = 0.04f,
                SwitchStrikeDamageMultiplier = 1.15f, SwitchStrikePoiseMultiplier = 1.3f
            };
        }
    }
}
