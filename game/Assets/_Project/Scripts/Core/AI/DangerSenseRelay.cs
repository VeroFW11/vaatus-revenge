using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Danger sense, the enemy side: turns an enemy's events into IncomingStrikes for the player model, which raises
    // the warnings at its own preset's lead times. The enemy brain never learns anything about the player's tuning.
    // Stateless: the Unity side (EnemyStrikes) and the headless harness (SimEnemy) call it for every enemy event.
    //
    //   TelegraphStarted   every strike of the attack is registered: strike h lands Move.Startup + h x HitInterval from
    //                      now (+ the bolt's flight time for a ranged attack, estimated from where the player stands, until
    //                      it touches the body)
    //   ProjectileLaunched that bolt's impact is worked out again from where it really left and how fast it flies
    //   AttackEnded        a melee attack's strikes are called off (any still pending never came); a ranged attack's
    //                      too, if it was cut short (staggered, launched, dead, gave up): bolts already flying keep
    //                      their warning only when the attack ended normally
    //   Died, Reset        everything from this attacker is called off
    public static class DangerSenseRelay
    {
        public static void OnEnemyEvent(in EnemyEvent e, EnemyBrain brain, int attackerId,
                                        Vector3 attackerFeet, Vector3 playerFeet, PlayerCombatModel player)
        {
            if (player == null) return;
            switch (e.Type)
            {
                case EnemyEventType.TelegraphStarted:
                    RegisterAttack(in e, attackerId, attackerFeet, playerFeet, player);
                    break;
                case EnemyEventType.ProjectileLaunched:
                    RegisterBolt(in e, attackerId, attackerFeet, playerFeet, player);
                    break;
                case EnemyEventType.AttackEnded:
                    if (e.Move == null || !e.Move.LaunchesProjectile || WasCutShort(brain)) player.CancelIncomingStrikes(attackerId);
                    break;
                case EnemyEventType.Died:
                case EnemyEventType.Reset:
                    player.CancelIncomingStrikes(attackerId);
                    break;
            }
        }

        static void RegisterAttack(in EnemyEvent e, int attackerId, Vector3 attackerFeet, Vector3 playerFeet, PlayerCombatModel player)
        {
            EnemyAttackData attack = e.Attack;
            MoveData move = e.Move ?? (attack != null ? attack.Move : null);
            if (move == null) return;
            int hits = attack != null ? Math.Max(1, attack.HitCount) : 1;
            float interval = attack != null ? Math.Max(0f, attack.HitInterval) : 0f;
            // A bolt still has to cross the gap: estimated from where the player stands now, refreshed when it flies.
            float flight = move.LaunchesProjectile
                ? FlightTime(move, Directions.Flatten(playerFeet - attackerFeet).Length() - move.OriginForward, player.BodyRadius)
                : 0f;
            for (int h = 0; h < hits; h++)
            {
                IncomingStrike strike = Describe(attack, move, attackerId, e.AttackId, h, attackerFeet, playerFeet);
                strike.ImpactClock = player.Clock + move.Startup + h * interval + flight;
                player.NotifyIncomingStrike(in strike);
            }
        }

        static void RegisterBolt(in EnemyEvent e, int attackerId, Vector3 attackerFeet, Vector3 playerFeet, PlayerCombatModel player)
        {
            MoveData move = e.Move ?? (e.Attack != null ? e.Attack.Move : null);
            if (move == null) return;
            IncomingStrike strike = Describe(e.Attack, move, attackerId, e.AttackId, e.HitIndex, attackerFeet, playerFeet);
            strike.ImpactClock = player.Clock + FlightTime(move, Directions.Flatten(playerFeet - e.Origin).Length(), player.BodyRadius);
            player.NotifyIncomingStrike(in strike);
        }

        static IncomingStrike Describe(EnemyAttackData attack, MoveData move, int attackerId, int attackKey, int hitIndex,
                                       Vector3 attackerFeet, Vector3 playerFeet)
        {
            return new IncomingStrike
            {
                AttackerId = attackerId, AttackKey = attackKey, HitIndex = hitIndex,
                AttackerFeet = attackerFeet,
                StrikeForward = Directions.SafeNormalize(Directions.Flatten(playerFeet - attackerFeet), Vector3.Zero),
                Parryable = move.Parryable, Unblockable = move.Unblockable, Ranged = move.LaunchesProjectile,
                Hidden = attack != null && attack.HideDangerSense,
                LeadScale = attack != null ? attack.DangerLeadScale : 1f
            };
        }

        // Until the bolt touches the body: the gap to the player's centre less both radii.
        static float FlightTime(MoveData move, float distance, float bodyRadius)
        {
            ProjectileSpec bolt = move.Projectile;
            float speed = bolt != null ? bolt.Speed : 0f;
            if (!(speed > 0f)) return 0f;
            float gap = distance - Math.Max(0f, bodyRadius) - Math.Max(0f, bolt.Radius);
            return Math.Max(0f, gap) / speed;
        }

        // The attack stopped before its recovery played out: its bolts that never flew must not keep warning.
        static bool WasCutShort(EnemyBrain brain)
        {
            if (brain == null) return true;
            EnemyState state = brain.State;
            return state == EnemyState.Staggered || state == EnemyState.Launched || state == EnemyState.Dead || state == EnemyState.Idle;
        }
    }
}
