using System.Numerics;

namespace VaatusRevenge.Core
{
    // Things that happened to the player this frame, for the Unity side to turn into hit queries,
    // projectiles, effects, sounds, rumble and HUD pops. The fields each type fills are listed per type.
    public enum PlayerEventType
    {
        AttackStarted,      // Move, AttackId, AttackKind, IsCounter, ChargeTier (heavy)
        AttackActiveStart,  // Move, AttackId, Origin (strike origin), Direction (strike forward). Run the melee arc query
                            // now and on every frame while IsAttackActive (the AttackId stops double hits)
        AttackActiveEnd,    // Move, AttackId. Clear the hit dedupe for this AttackId (MeleeHitQuery.EndAttack)
        ProjectileLaunched, // Move (Move.Projectile = spec), AttackId, Origin, Direction (normalised, may aim up/down)
        PlungeImpact,       // Move, AttackId, Origin (landing point, feet), Radius. One-shot sphere query, then EndAttack
        AttackEnded,        // Move, AttackId. The move finished or was cut short (reset the pose)
        ChargeStarted,      // Move (the heavy)
        ChargeSweetSpot,    // Move. The fa jin window just opened (see also ChargeReadyCue, which comes earlier)
        ChargeCancelled,    // Move
        DodgeStarted,       // Direction (dash direction), Amount (distance), IsBackstep, InAir (an air dash)
        DodgeEnded,
        PerfectDodge,       // TimeScale, Duration (real-time slow motion to apply), Amount (Momentum gained)
        Jumped,
        Landed,             // Amount (downward speed at impact, m/s)
        SprintStarted,
        SprintEnded,
        GuardStarted,
        GuardEnded,
        Blocked,            // Amount (stamina paid)
        GuardBroken,        // Duration (stagger)
        Deflected,          // Amount (Momentum gained). The attacker was told HitOutcome.Parried
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
        ChargeReadyCue      // Move. "Get ready": ChargeSettings.ReadyCueLead before the sweet spot, so a person reacting to
                            // this cue lets go inside the window. Once per charge. (Added last to keep existing numbering.)
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
    }
}
