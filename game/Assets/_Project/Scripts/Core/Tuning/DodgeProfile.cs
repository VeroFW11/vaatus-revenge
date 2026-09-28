using System;

namespace VaatusRevenge.Core
{
    // One element's dodge (Fire: Flame Step). Each element gets its own profile, all on the same button.
    // Times are seconds from the start of the dodge.
    [Serializable]
    public class DodgeProfile
    {
        public string DisplayName = "Flame Step";
        public float Distance = 4.2f;                // dash length with the stick pushed
        public float BackstepDistance = 2.2f;        // no stick input = a short hop backwards
        public float Duration = 0.30f;               // time the dash takes
        public float DashEaseOut = 0.6f;             // 0 = constant speed, 1 = explosive start that slows to a stop
        public float EndRecovery = 0f;               // committed, vulnerable time after the dash before you're free

        public float StaminaCost = 6f;

        // Invincibility frames ("i-frames"): hits during this window pass straight through you.
        public float IFrameStart = 0.02f;
        public float IFrameEnd = 0.24f;
        // Guaranteed vulnerable time between one dodge's i-frames and the next, so dodge-spam can never be
        // permanent invincibility. Enforced by the rules even if the numbers above are tuned badly.
        public float ChainIFrameGap = 0.05f;

        public float AttackCancelAt = 0.14f;         // earliest an attack, jump or guard can cut the dodge short
        public float NextDodgeAt = 0.28f;            // earliest a new dodge can start

        // Perfect dodge: a hit arrives in the first moments of the dash, i.e. you dodged at the last instant.
        public bool PerfectDodgeEnabled = true;
        public float PerfectWindow = 0.12f;
        public float PerfectSlowMoScale = 0.35f;     // time scale the Unity side applies (the core only raises the event)
        public float PerfectSlowMoDuration = 0.35f;  // real-time seconds of slow motion
        public float PerfectMomentumGain = 25f;
        public float CounterWindow = 0.8f;           // after a perfect dodge, the first attack started this soon is a counter
        public float CounterDamageMultiplier = 1.5f;

        public float TotalDuration => Duration + EndRecovery;
    }
}
