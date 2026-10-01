using System;

namespace VaatusRevenge.Core
{
    // Read-only views for the HUD, the tutorial and the harness: elements, the hit counter, MIX, the beat and the
    // string. Nothing here changes the model.
    public sealed partial class PlayerCombatModel
    {
        public ElementLoadout Loadout => loadout;
        public ElementId ActiveElement => activeElement;

        public bool IsLearned(ElementId element)
        {
            return loadout.IsLearned(element);
        }

        public float SwitchCooldownRemaining => clock < switchCooldownUntil ? (float)(switchCooldownUntil - clock) : 0f;

        // 1 = just switched, 0 = ready (for the element wheel's radial wipe).
        public float SwitchCooldown01
        {
            get
            {
                float cooldown = tuning.ElementSwitch != null ? tuning.ElementSwitch.Cooldown : 0f;
                return cooldown > 0f ? Angles.Clamp(SwitchCooldownRemaining / cooldown, 0f, 1f) : 0f;
            }
        }

        public int ComboCount => comboCount;
        public float ComboTimeRemaining => comboCount > 0 ? Math.Max(0f, comboTimeRemaining) : 0f;

        public float ComboTimeRemaining01
        {
            get
            {
                float timeout = ComboRules.ComboTimeout;
                return timeout > 0f ? Angles.Clamp(ComboTimeRemaining / timeout, 0f, 1f) : 0f;
            }
        }

        public RhythmView Rhythm => BuildRhythmView();

        // The string's next slot if it continues now (-1 = none: the next X starts the string again).
        public int StringNextIndex => IsStringMemoryLive ? stringNext : -1;
        public ComboBranch StringBranch => IsStringMemoryLive ? stringBranch : ComboBranch.Main;

        public int MixLevel => comboCount > 0 ? CountBits(mixMask) : 0;
        public int MixElementsMask => comboCount > 0 ? mixMask : 0;

        // An element's identity meter, 0..1 (Fire: Momentum; the others have none in this build and read 0).
        public float MeterFraction(ElementId element)
        {
            MomentumSettings rules = MomentumRulesOf(element);
            if (!rules.Enabled || !(rules.Max > 0f)) return 0f;
            return Angles.Clamp(MeterOf(element).Current / rules.Max, 0f, 1f);
        }

        static int CountBits(int mask)
        {
            int count = 0;
            for (; mask != 0; mask &= mask - 1) count++;
            return count;
        }
    }
}
