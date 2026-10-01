using System.Numerics;

namespace VaatusRevenge.Core
{
    // Things that happened to the player this frame, for the Unity side to turn into hit queries,
    // projectiles, effects, sounds, rumble and HUD pops. The fields each type fills are listed per type.
    // Every move event also carries Element (the element the move belongs to) and MoveInstanceId.
    public enum PlayerEventType
    {
        AttackStarted,      // Move, AttackId, AttackKind, IsCounter, ChargeTier (heavy), ChainIndex, Branch, IsFinisher,
                            // PlaybackRate (the move's speed, 1 = as authored), Element, Grade (the press that started it),
                            // MoveInstanceId
        AttackActiveStart,  // Move, AttackId, Origin (strike origin), Direction (strike forward), Element, MoveInstanceId.
                            // Run the melee arc query now and on every frame while IsAttackActive (the AttackId stops double
                            // hits). A multi-hit move (MoveData.HitCount > 1) sends one Start/End pair per sub-hit, each with
                            // its own AttackId and the shared MoveInstanceId
        AttackActiveEnd,    // Move, AttackId, Element, MoveInstanceId. Clear the hit dedupe for this AttackId (MeleeHitQuery.EndAttack)
        ProjectileLaunched, // Move (Move.Projectile = spec), AttackId, Origin, Direction (normalised, may aim up/down), Element,
                            // MoveInstanceId
        PlungeImpact,       // Move, AttackId, Origin (landing point, feet), Radius. One-shot sphere query, then EndAttack
        AttackEnded,        // Move, AttackId, Element, MoveInstanceId. The move finished or was cut short (reset the pose)
        ChargeStarted,      // Move (the heavy)
        ChargeSweetSpot,    // Move. The fa jin window just opened (see also ChargeReadyCue, which comes earlier)
        ChargeCancelled,    // Move
        DodgeStarted,       // Direction (dash direction), Amount (distance), IsBackstep, InAir (an air dash), DodgeKind,
                            // LocalDirection (the dash in facing space: +Z forward, +X right; picks a directional clip), Element
        DodgeEnded,
        PerfectDodge,       // TimeScale, Duration (real-time slow motion to apply), Amount (Momentum gained), Element
        Jumped,
        Landed,             // Amount (downward speed at impact, m/s)
        SprintStarted,
        SprintEnded,
        GuardStarted,
        GuardEnded,
        Blocked,            // Amount (stamina paid)
        GuardBroken,        // Duration (stagger)
        Deflected,          // Amount (Momentum gained), Element. The attacker was told HitOutcome.Parried
        DeflectWhiffed,     // Duration (deflect lockout)
        HealStarted,
        HealApplied,        // Amount (health restored)
        HealInterrupted,    // the charge was not used
        HealFailed,         // pressed heal with no charges left
        Damaged,            // Amount (damage taken), Direction (hit direction)
        Staggered,          // Duration
        StaggerEnded,
        Parried,            // an enemy deflected your attack (a Staggered event follows)
        Died,
        Respawned,
        ChargeReadyCue,     // Move. "Get ready": ChargeSettings.ReadyCueLead before the sweet spot, so a person reacting to
                            // this cue lets go inside the window. Once per charge. (Added last to keep existing numbering.)

        // ---- Build 05 (appended in this order, so the numbering above never changes) ----
        ComboBeatOpened,    // Move, AttackId, ChainIndex, Branch, Element, Duration (beat window length), Amount (seconds from
                            // the window opening to the beat). A string move's beat window is about to open: start the HUD ring
        BeatJudged,         // Grade, Amount (signed press offset from the beat, seconds), Count (OnBeatStreak), Element.
                            // Raised again as Mashed when a second press downgrades an OnBeat
        ComboHit,           // Move, AttackId, MoveInstanceId, AttackKind, Branch, ChainIndex, IsFinisher, Element, Grade (of the
                            // move), Count (ComboCount after this hit), Amount (combo time left), Origin (contact point),
                            // InAir (the target was airborne)
        PerfectString,      // Move (the finisher), Element. Every follow-up of this string was on the beat
        ComboEnded,         // Count (final hit count), EndReason
        ElementSwitched,    // Element, PreviousElement, IsSwitchStrike, Branch, Count (MixLevel), InAir
        ElementSwitchDenied,// Element (requested), DenyReason, Duration (cooldown left)
        MixChanged,         // Count (MixLevel), Amount (MIX damage multiplier), Element (the element just added)
        MixFinisher,        // Count (MixLevel), Move, AttackId, Element. A finisher landed with MIX 2 or more
        DangerWarning,      // AttackerId, Count (hit index), Origin (attacker's feet), Direction (attacker -> player, flat,
                            // normalised), Duration (seconds to impact), MustDodge, IsRanged
        DangerNow,          // same fields as DangerWarning: press now
        DangerCleared,      // AttackerId, Count (hit index). The strike landed, missed or was called off
        DodgeChainLimited   // Duration (cooldown left). Too many dodges in a row: no dodge until it runs out
    }

    public struct PlayerEvent
    {
        public PlayerEventType Type;
        public MoveData Move;
        public PlayerAttackKind AttackKind;
        public int AttackId;
        public Vector3 Origin;
        public Vector3 Direction;
        public float Amount;
        public float Radius;
        public float Duration;
        public float TimeScale;
        public ChargeTier ChargeTier;
        public bool IsCounter;
        public bool IsBackstep;
        public bool InAir;                // DodgeStarted / AttackStarted: it happened in the air

        // Build 05 (see the event list for which events fill what).
        public BeatGrade Grade;
        public int Count;
        public int ChainIndex;
        public ComboBranch Branch;
        public bool IsFinisher;
        public float PlaybackRate;
        public ElementId Element;
        public ElementId PreviousElement;
        public DodgeKind DodgeKind;
        public Vector3 LocalDirection;
        public int AttackerId;
        public int MoveInstanceId;
        public bool MustDodge;
        public bool IsRanged;
        public bool IsSwitchStrike;
        public ComboEndReason EndReason;
        public SwitchDeniedReason DenyReason;
    }
}
