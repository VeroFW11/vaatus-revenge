using System;

namespace VaatusRevenge.Core
{
    // Rhythm combos (the beat). Every string move has a beat: the moment its strike lands (its first active frame).
    // A follow-up press near the beat is OnBeat: the next move plays faster and hits harder. A press well before it is
    // a mash (Early): the next move still comes, but slower and dearer. A press after the window is Late: neutral. So
    // mashing always works, slowly, and watching the hits land is rewarded (Spider-Man's rhythm, Elden Ring's
    // deliberate timing). The beat window is measured on the game clock: hitstop freezes it, slow motion stretches it.
    // The element adds small deltas on top (ElementRhythm). Defaults are the Fluid preset.
    [Serializable]
    public class RhythmTuning
    {
        public bool Enabled = true;                  // false = exactly the old chain timing (no beat, no playback rate)
        public float BeatEarly = 0.06f;              // the window opens this long before the beat...
        public float BeatLate = 0.10f;               // ...and closes this long after it (seconds of game time)
        public float OnBeatPlaybackRate = 1.15f;     // the next move after an on-beat press plays this much faster...
        public float OffBeatPlaybackRate = 0.85f;    // ...and this much slower after a mash (Early or Mashed)
        public float MinPlaybackRate = 0.6f;         // safety clamp for every playback rate
        public float MaxPlaybackRate = 1.4f;
        public float OnBeatDamageMultiplier = 1.10f; // damage of the move an on-beat press started
        public float MashStaminaSurcharge = 4f;      // extra stamina a mashed move costs
        public float PauseGrace = 0.35f;             // the pause band (X X, wait, X) lasts until this long after the move ends
        public float PerfectStringDamageMultiplier = 1.20f; // a finisher whose whole string was on the beat hits this much harder...
        public float PerfectStringLaunchSpeed = 9f;  // ...and knocks a launchable foe up at this speed (0 = no launch)
        public float BeatInputOffset = 0f;           // calibration: added to every press time (a display or pad with lag)

        public static RhythmTuning CreatePunishing()
        {
            return new RhythmTuning
            {
                BeatEarly = 0.04f, BeatLate = 0.07f, OnBeatPlaybackRate = 1.12f, OffBeatPlaybackRate = 0.80f,
                MashStaminaSurcharge = 3f, PauseGrace = 0.25f, PerfectStringDamageMultiplier = 1.15f, PerfectStringLaunchSpeed = 0f
            };
        }
    }
}
