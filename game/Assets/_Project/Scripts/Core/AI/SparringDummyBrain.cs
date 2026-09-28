using System;

namespace VaatusRevenge.Core
{
    // Training dummy: never moves, can't die (Unkillable), refills its health after HealthRefillDelay
    // without being hit, and keeps simple combo stats for the readout. With SwingEnabled it turns to face
    // you and swings on a fixed rhythm (every AttackIntervalMin seconds, start to start) while you're
    // within AggroRange, for practising dodge and deflect timing.
    public sealed class SparringDummyBrain : EnemyBrain
    {
        double comboStart;
        double lastHit;
        double lastSwingStart = double.NegativeInfinity;

        public SparringDummyBrain(EnemyTuning tuning, AttackTokenPool tokens, int ownerId, int seed, float facingYaw = 0f)
            : base(tuning, tokens, ownerId, seed, facingYaw)
        {
        }

        public bool SwingEnabled { get; set; }
        public int ComboHits { get; private set; }
        public float ComboDamage { get; private set; }
        public float LastHitDamage { get; private set; }

        // Damage per second across the current combo (from its first hit to its latest).
        public float ComboDps
        {
            get
            {
                if (ComboHits < 2) return 0f;
                double span = lastHit - comboStart;
                return span > 1e-3 ? (float)(ComboDamage / span) : 0f;
            }
        }

        protected override void Think(float dt, in EnemyWorldState world)
        {
            if (!IsAggro || !world.HasTarget)
            {
                SetState(EnemyState.Idle);
                return;
            }
            SetState(EnemyState.Circle);   // "ready": stands still and faces you
            if (!SwingEnabled || AttackTimer > 0f || !CanUseTokenNow) return;
            int index = PickAttack(RangeTo(world), true);
            if (index >= 0 && ReserveAttack(index)) StartAttack(index, world);
        }

        protected override void OnAttackStarted()
        {
            lastSwingStart = Clock;
        }

        // Keeps a steady beat measured from swing start to swing start.
        protected override float NextAttackDelay()
        {
            float sinceSwing = (float)(Clock - lastSwingStart);
            return Math.Max(0f, Tuning.AttackIntervalMin - sinceSwing);
        }

        protected override void OnDamaged(float damage)
        {
            if (ComboHits == 0) comboStart = Clock;
            ComboHits++;
            ComboDamage += damage;
            LastHitDamage = damage;
            lastHit = Clock;
        }

        protected override void OnHealthRefilled()
        {
            ComboHits = 0;
            ComboDamage = 0f;
        }

        protected override void OnReset()
        {
            ComboHits = 0;
            ComboDamage = 0f;
            LastHitDamage = 0f;
            lastSwingStart = double.NegativeInfinity;
        }
    }
}
