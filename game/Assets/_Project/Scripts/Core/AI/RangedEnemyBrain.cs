using System.Numerics;

namespace VaatusRevenge.Core
{
    // A ranged fighter (e.g. the Crossbowman). Keeps between KeepAwayMin and KeepAwayMax from the player:
    // backs off quickly (RetreatSpeed) when the player gets within RetreatTriggerDistance, walks back when a
    // little too close, closes in when too far or when it can't see the player, and strafes while in its band.
    // Shoots when its attack timer allows, the player is visible and within an attack's range. It won't
    // shoot while it's escaping, unless it has been retreating for ApproachTimeout (cornered).
    public sealed class RangedEnemyBrain : EnemyBrain
    {
        float circleSign = 1f;
        float circleTimer;
        float retreatingFor;

        public RangedEnemyBrain(EnemyTuning tuning, AttackTokenPool tokens, int ownerId, int seed, float facingYaw = 0f)
            : base(tuning, tokens, ownerId, seed, facingYaw)
        {
        }

        protected override void Think(float dt, in EnemyWorldState world)
        {
            if (!IsAggro || !world.HasTarget)
            {
                SetState(EnemyState.Idle);
                return;
            }
            EnemyTuning t = Tuning;
            float range = RangeTo(world);
            Vector3 toTarget = DirectionTo(world);
            bool tooClose = range < t.RetreatTriggerDistance;
            retreatingFor = tooClose ? retreatingFor + dt : 0f;
            bool cornered = retreatingFor > t.ApproachTimeout;

            if (AttackTimer <= 0f && !world.TargetHidden && (!tooClose || cornered) && CanUseTokenNow)
            {
                int index = PickAttack(range, true);
                if (index >= 0 && ReserveAttack(index))
                {
                    StartAttack(index, world);
                    return;
                }
            }

            if (tooClose)
            {
                SetState(EnemyState.Retreat);
                Desire(-toTarget * t.RetreatSpeed);
            }
            else if (range < t.KeepAwayMin)
            {
                SetState(EnemyState.Retreat);
                Desire(-toTarget * t.WalkSpeed);
            }
            else if (range > t.KeepAwayMax || world.TargetHidden)
            {
                SetState(EnemyState.Approach);
                Desire(toTarget * t.ChaseSpeed);
            }
            else
            {
                SetState(EnemyState.Circle);
                circleTimer -= dt;
                if (circleTimer <= 0f)
                {
                    circleSign = Random.Chance(0.5f) ? 1f : -1f;
                    circleTimer = Random.Range(t.CircleTimeMin, t.CircleTimeMax);
                }
                Desire(Directions.RightFromYaw(Directions.YawOf(toTarget, FacingYaw)) * (circleSign * t.StrafeSpeed));
            }
        }

        protected override void OnReset()
        {
            circleSign = 1f;
            circleTimer = 0f;
            retreatingFor = 0f;
        }
    }
}
