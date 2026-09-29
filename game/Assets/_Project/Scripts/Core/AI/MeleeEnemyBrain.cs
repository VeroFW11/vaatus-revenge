using System.Numerics;

namespace VaatusRevenge.Core
{
    // A sword fighter (e.g. the Dao Soldier). Loop once it has noticed the player:
    //   circle at PreferredDistance (strafing, switching direction now and then) until the attack timer
    //   runs out -> take an attack token (if none is free, keep circling) -> pick an attack -> close in
    //   until it's in range -> telegraph, strike, recover -> sometimes step back -> circle again.
    // Gives up an approach after ApproachTimeout so a fleeing player can't make it hog a token forever.
    public sealed class MeleeEnemyBrain : EnemyBrain
    {
        float circleSign = 1f;
        float circleTimer;
        float retreatTimer;
        float approachTimer;

        public MeleeEnemyBrain(EnemyTuning tuning, AttackTokenPool tokens, int ownerId, int seed, float facingYaw = 0f)
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

            // Committed to an attack: get into its range, then swing.
            EnemyAttackData pending = PendingAttack;
            if (pending != null)
            {
                approachTimer += dt;
                if (approachTimer > t.ApproachTimeout)
                {
                    CancelPendingAttack();
                    AttackTimer = NextAttackDelay();
                }
                else if (range > pending.MaxRange)
                {
                    SetState(EnemyState.Approach);
                    Desire(toTarget * t.ChaseSpeed);
                    return;
                }
                else if (range < pending.MinRange)
                {
                    SetState(EnemyState.Retreat);
                    Desire(-toTarget * t.WalkSpeed);
                    return;
                }
                else
                {
                    StartAttack(PendingAttackIndex, world);
                    return;
                }
            }

            // Stepping back after an attack.
            if (retreatTimer > 0f)
            {
                retreatTimer -= dt;
                SetState(EnemyState.Retreat);
                Desire(-toTarget * t.WalkSpeed);
                return;
            }

            // Spacing: close in when far, circle at the preferred distance, step out when crowded.
            float error = range - t.PreferredDistance;
            if (error > t.SpacingTolerance)
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
                Vector3 sideways = Directions.RightFromYaw(Directions.YawOf(toTarget, FacingYaw)) * circleSign;
                Vector3 outward = error < -t.SpacingTolerance ? -toTarget * t.WalkSpeed : Vector3.Zero;
                Desire(sideways * t.StrafeSpeed + outward);
            }

            if (AttackTimer <= 0f && CanUseTokenNow)
            {
                int index = PickAttack(range, false);
                if (index >= 0 && ReserveAttack(index)) approachTimer = 0f;
            }
        }

        protected override void OnAttackFinished()
        {
            EnemyTuning t = Tuning;
            if (Random.Chance(t.BackOffChance))
            {
                retreatTimer = Random.Range(t.BackOffTimeMin, t.BackOffTimeMax);
                SetState(EnemyState.Retreat);
            }
            else
            {
                SetState(EnemyState.Circle);
            }
        }

        protected override void OnReset()
        {
            circleSign = 1f;
            circleTimer = 0f;
            retreatTimer = 0f;
            approachTimer = 0f;
        }
    }
}
