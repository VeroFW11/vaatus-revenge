using System;

namespace VaatusRevenge.Core
{
    // Danger sense: a mark above the player's head when an enemy strike is about to land (gold = parry it, red = dodge
    // it), turning white at the moment to press. A UI convention, not an Avatar power; its name lives here as data.
    // The enemy side registers strikes through DangerSenseRelay; the player model times the cues with these leads, so
    // the enemy brains never need to know the player's preset. Defaults are the Fluid preset.
    [Serializable]
    public class DangerSenseSettings
    {
        public bool Enabled = true;
        public string DisplayName = "Danger Sense";  // shown in the tutorial and controls overlay
        public float WarningLead = 0.60f;            // the mark appears this long before the strike lands...
        public float NowLead = 0.30f;                // ...and turns white this long before (0 = no white cue). A person reacting
                                                     // to it after ~0.25 s presses inside the perfect-dodge and deflect windows
        public float ClearAfterImpact = 0.10f;       // a strike is forgotten this long after it should have landed
        public float BoltMissMargin = 0.3f;          // a bolt passing further than this beside your body (sideways) won't hit you:
                                                     // its warning is called off until you step back into its path
        public float AutoEvadeLookahead = 0.45f;     // a neutral-stick dodge side-steps a strike landing within this long
        public int MaxTracked = 8;                   // strikes tracked at once (a fixed array: no allocation)

        public static DangerSenseSettings CreatePunishing()
        {
            return new DangerSenseSettings { WarningLead = 0.45f, NowLead = 0f };
        }
    }
}
