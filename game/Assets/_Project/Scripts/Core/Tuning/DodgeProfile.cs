using System;

namespace VaatusRevenge.Core
{
    // What counts as a perfect dodge (Playtest Report 01, ABIL-02). Pending David's decision: flip it here.
    public enum PerfectDodgeRule
    {
        SwingMustReachYou,  // only a strike that actually reaches you during the i-frames counts (dodging away never does)
        WouldHaveLanded     // a strike going off within PerfectWindow of the dodge that would have hit where you started
                            // counts, whichever way you dashed (needs PlayerCombatModel.NotifyEnemyStrike from the enemy side)
    }

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
        public PerfectDodgeRule PerfectRule = PerfectDodgeRule.WouldHaveLanded;
        public float PerfectWindow = 0.12f;
        public float PerfectSlowMoScale = 0.35f;     // time scale the Unity side applies (the core only raises the event)
        public float PerfectSlowMoDuration = 0.35f;  // real-time seconds of slow motion
        public float PerfectMomentumGain = 25f;
        public float PerfectTowardBonus = 10f;       // extra Momentum when the perfect dodge dashed toward or through the attacker
        public float PerfectTowardMaxAngle = 60f;    // "toward" = the dash within this many degrees of the direction to the attacker
        public float CounterWindow = 0.8f;           // after a perfect dodge, the first attack started this soon is a counter
        public float CounterDamageMultiplier = 1.5f;

        public float TotalDuration => Duration + EndRecovery;
    }
}
