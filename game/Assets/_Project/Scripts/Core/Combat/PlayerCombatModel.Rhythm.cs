using System;

namespace VaatusRevenge.Core
{
    // Rhythm combos, string memory and the pause branch.
    public sealed partial class PlayerCombatModel
    {
        readonly RhythmTuning fallbackRhythm = new RhythmTuning { Enabled = false };

        int stringNext = -1;          // the string's next slot after an interruption (-1 = none)
        ComboBranch stringBranch;     // ...in this chain (Main, Pause or Air)
        double stringMemoryUntil = double.NegativeInfinity;
        bool stringHeld;              // an action that keeps the string (dodge, zip, ability, skill, heavy) is running

        RhythmTuning RhythmRules => tuning.Rhythm ?? fallbackRhythm;

        bool IsStringMemoryLive => stringNext >= 0 && (stringHeld || clock <= stringMemoryUntil + 1e-6);

        void ClearStringMemory()
        {
            stringNext = -1;
            stringBranch = ComboBranch.Main;
            stringMemoryUntil = double.NegativeInfinity;
            stringHeld = false;
        }

        RhythmView BuildRhythmView()
        {
            return new RhythmView { PlaybackRate = 1f };
        }
    }
}
