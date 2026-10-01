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
    //
    // Spider-Man 2 style (Build 05): you never turn your back on the fight. With an enemy to focus on (lock target,
    // the attacker about to hit you, the soft-lock target, or the nearest enemy within FocusRadius) you keep facing it,
    // and the stick against the direction to it picks the dodge (DodgeKind): toward = SlipIn, away = EvadeOut, sideways
    // = SideSlip, neutral = AutoEvade (a strike about to land) or Backstep. With nobody near, you dash where you push.
    [Serializable]
    public class DodgeProfile
    {
        public string DisplayName = "Flame Step";
        public float Duration = 0.24f;               // time the dash takes
        public float DashEaseOut = 0.70f;            // 0 = constant speed, 1 = explosive start that slows to a stop
        public float EndRecovery = 0f;               // committed, vulnerable time after the dash before you're free

        // --- Distances per kind ---
        public float EvadeOutDistance = 4.0f;        // stick away from the target (and a Traverse along the stick)
        public float SideSlipDistance = 3.0f;        // stick sideways (a circle round the target), and the automatic side-step
        public float SideSlipMaxDegrees = 100f;      // a side-slip goes at most this far round the target (0 = no limit)
        public float SlipInMaxDistance = 3.4f;       // stick toward the target: at most this far...
        public float SlipInStopGap = 0.6f;           // ...stopping this far from its body
        public float BackstepDistance = 2.2f;        // neutral stick, nothing incoming: a short hop backwards
        public float SlipInAngle = 50f;              // stick within this many degrees of the target = SlipIn...
        public float EvadeOutAngle = 120f;           // ...this many or more = EvadeOut; in between = SideSlip

        // --- Facing ---
        public float FocusRadius = 7f;               // the nearest enemy this close counts as the focus target
        public float FocusTurnRate = 900f;           // degrees per second you keep turning to the focus target while dodging
        public bool AutoEvadeOnNeutral = true;       // neutral stick with a strike about to land = automatic side-step (false = backstep)

        // --- Costs ---
        public float StaminaCost = 0f;
        public bool RequiresStamina = false;         // false: an empty bar still dodges (Fluid); true: no stamina, no dodge

        // Invincibility frames ("i-frames"): hits during this window pass straight through you.
        public float IFrameStart = 0f;
        public float IFrameEnd = 0.18f;
        // Guaranteed vulnerable time between one dodge's i-frames and the next, so dodge-spam can never be
        // permanent invincibility. Enforced by the rules even if the numbers above are tuned badly.
        public float ChainIFrameGap = 0.06f;

        // --- Cancels ---
        public float EvadeAttackCancelAt = 0.08f;    // earliest an attack, jump or guard can cut the dodge short (every kind but SlipIn)
        public float SlipInAttackCancelAt = 0.05f;   // the same for a slip-in (you're already close: hit sooner)
        public float NextDodgeAt = 0.18f;            // earliest a new dodge can start

        // --- Chains: a few quick dodges, then a breather ---
        public int ChainMax = 3;                     // dodges in a row (each starting within ChainLink of the last one ending)...
        public float ChainLink = 0.12f;
        public float ChainCooldown = 0.30f;          // ...then no dodge for this long (DodgeChainLimited)
        public float ExitSpeedCarry = 1f;            // stick held as the dodge ends: you leave it at RunSpeed x this (no re-accelerating)

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

        // Old names, kept so older code still compiles (Build 05 renamed them).
        public float Distance => EvadeOutDistance;
        public float AttackCancelAt => EvadeAttackCancelAt;

        // The Punishing preset's dodge (Elden Ring commitment), the same for every element.
        public void ApplyPunishing()
        {
            Duration = 0.36f;
            DashEaseOut = 0.60f;
            EvadeOutDistance = 4.2f;
            SideSlipDistance = 3.2f;
            SlipInMaxDistance = 3.0f;
            SlipInStopGap = 0.6f;
            SlipInAngle = 50f;
            EvadeOutAngle = 120f;
            BackstepDistance = 2.2f;
            FocusRadius = 7f;
            FocusTurnRate = 600f;
            AutoEvadeOnNeutral = false;
            IFrameStart = 0.04f;
            IFrameEnd = 0.30f;
            ChainIFrameGap = 0.05f;
            EvadeAttackCancelAt = 0.48f;
            SlipInAttackCancelAt = 0.48f;
            NextDodgeAt = 0.48f;
            EndRecovery = 0.12f;
            ChainMax = 2;
            ChainLink = 0.12f;
            ChainCooldown = 0.45f;
            StaminaCost = 16f;
            RequiresStamina = true;
            ExitSpeedCarry = 0.5f;
            PerfectDodgeEnabled = false;
        }
    }
}
