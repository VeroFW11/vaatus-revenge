using System;

namespace VaatusRevenge.Core
{
    // How an element's martial art changes the beat (added on top of the preset's RhythmTuning):
    //   Fire (Northern Shaolin): a steady, relentless beat; on-beat hits feed Momentum.
    //   Water (Tai Chi): legato and forgiving: a later window, on-beat hits give stamina back.
    //   Earth (Hung Gar): strict and rooted: a tighter window, on-beat hits strike harder and can't be knocked out of.
    //   Air (Baguazhang): the step is the beat: a dodge between hits keeps the streak going.
    [Serializable]
    public class ElementRhythm
    {
        public float BeatEarlyDelta = 0f;            // added to RhythmTuning.BeatEarly
        public float BeatLateDelta = 0f;             // added to RhythmTuning.BeatLate
        public float OnBeatPlaybackRate = 0f;        // the element's own on-beat speed-up (0 = the preset's)
        public float OnBeatDamageBonus = 0f;         // added to RhythmTuning.OnBeatDamageMultiplier
        public float OnBeatMomentumBonus = 0f;       // Momentum gained by each on-beat press
        public float OnBeatStaminaRefund = 0f;       // stamina given back by each on-beat press
        public bool OnBeatHyperArmor = false;        // the move an on-beat press starts has hyper armour until its active frames end
        public bool DodgeKeepsBeat = false;          // a dodge between hits keeps the streak; the dodge strike is always on the beat
        public int PauseAfterIndex = 1;              // the pause branch opens after this main-chain hit (1 = the 2nd: X X, wait, X X)
    }
}
