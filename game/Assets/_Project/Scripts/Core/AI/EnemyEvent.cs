using System.Numerics;

namespace VaatusRevenge.Core
{
    // Things an enemy did this frame, for the Unity side to turn into glows, hit queries, bolts and effects.
    public enum EnemyEventType
    {
        Aggroed,            // noticed the player
        TelegraphStarted,   // Attack, Move, Telegraph, Duration (the wind-up: glow for this long), AttackId
        AttackActiveStart,  // Attack, Move, AttackId, HitIndex, Origin, Direction. Run the arc query now and each frame
                            // while IsAttackActive. Tracking has stopped: the swing goes where it was aimed
        AttackActiveEnd,    // Attack, Move, AttackId, HitIndex. Clear the hit dedupe (MeleeHitQuery.EndAttack)
        ProjectileLaunched, // Attack, Move (Move.Projectile), AttackId (one per bolt), HitIndex, Origin, Direction
        AttackEnded,        // Attack, Move. Recovery finished or the attack was interrupted (telegraph off)
        Damaged,            // Amount, Direction
        Staggered,          // Duration
        StaggerEnded,
        Died,
        HealthRefilled,     // training dummy topped itself up
        Reset               // Reset() was called
    }

    public struct EnemyEvent
    {
        public EnemyEventType Type;
        public EnemyAttackData Attack;
        public MoveData Move;
        public TelegraphKind Telegraph;
        public int AttackId;
        public int HitIndex;              // which strike/bolt of a multi-hit attack (0 = first)
        public Vector3 Origin;
        public Vector3 Direction;
        public float Amount;
        public float Duration;
    }
}
