using System;

namespace VaatusRevenge.Core
{
    // Which brain drives the enemy (see EnemyBrain.Create).
    public enum EnemyArchetype { Melee, Ranged, Dummy }

    // One enemy type's numbers. Lives inside an EnemyTuningAsset (ScriptableObject) that several enemies
    // of the same type may share, so the brains read it live and never write to it.
    // Field defaults are the Dao Soldier. Distances are from the enemy's centre to the target's body.
    [Serializable]
    public class EnemyTuning
    {
        public string DisplayName = "Dao Soldier";   // shown on health bars; lore names live here, never in code
        public EnemyArchetype Archetype = EnemyArchetype.Melee;

        // --- Health, poise, stagger ---
        public float MaxHealth = 110f;
        public bool Unkillable = false;              // training dummy: health never drops below 1
        public float HealthRefillDelay = 0f;         // > 0: health refills after this long without being hit
        public float MaxPoise = 35f;
        public float PoiseRegenDelay = 2f;           // poise refills this long after the last poise damage...
        public float PoiseRegenRate = 60f;           // ...at this many points per second
        public float StaggerDuration = 1.0f;         // stunned time when poise breaks
        public float ParriedStaggerDuration = 1.3f;  // stunned time when the player deflects this enemy's attack
        public float KnockbackTime = 0.15f;          // a clean hit's knockback distance is covered over this long

        // --- Awareness ---
        public float AggroRange = 15f;               // notices the player this close (or when hit)
        public float LoseAggroRange = 35f;           // gives up beyond this

        // --- Movement ---
        public float WalkSpeed = 2.0f;
        public float ChaseSpeed = 4.2f;
        public float StrafeSpeed = 1.6f;             // circling speed while waiting to attack
        public float RetreatSpeed = 3.0f;
        public float Acceleration = 20f;
        public float TurnRate = 360f;                // degrees/second when not attacking
        public float Gravity = 28f;
        public float GroundStickSpeed = 2f;

        // --- Melee spacing ---
        public float PreferredDistance = 3.2f;       // circles at about this distance between attacks
        public float SpacingTolerance = 0.75f;       // ...give or take this much before it steps in or out
        public float BackOffChance = 0.35f;          // chance to step back after an attack
        public float BackOffTimeMin = 0.5f;
        public float BackOffTimeMax = 0.9f;
        public float ApproachTimeout = 3f;           // melee: gives up chasing for an attack after this long (returns its token);
                                                     // ranged: shoots anyway after retreating this long (cornered)

        // --- Ranged spacing ---
        public float KeepAwayMin = 8f;               // ranged: tries to stay between these distances
        public float KeepAwayMax = 14f;
        public float RetreatTriggerDistance = 5f;    // ranged: backs off at RetreatSpeed when the player is this close

        // --- Attack rhythm ---
        public float AttackIntervalMin = 1.2f;       // pause between attacks (random in range). The dummy uses Min as its fixed rhythm
        public float AttackIntervalMax = 2.2f;
        public float CircleTimeMin = 0.8f;           // circles one way this long before switching direction
        public float CircleTimeMax = 2.0f;
        public bool UsesAttackToken = true;          // takes a shared token to attack, so only a few enemies attack at once

        public EnemyAttackData[] Attacks = CreateDaoSoldierAttacks();

        public static EnemyTuning CreateDaoSoldier()
        {
            return new EnemyTuning();
        }

        public static EnemyTuning CreateCrossbowman()
        {
            var t = new EnemyTuning();
            t.DisplayName = "Crossbowman";
            t.Archetype = EnemyArchetype.Ranged;
            t.MaxHealth = 70f;
            t.MaxPoise = 20f;
            t.AggroRange = 24f;
            t.ChaseSpeed = 3.0f;
            t.AttackIntervalMin = 1.5f;
            t.AttackIntervalMax = 2.5f;
            t.UsesAttackToken = false;
            t.Attacks = new[]
            {
                new EnemyAttackData
                {
                    Telegraph = TelegraphKind.Aimed, Weight = 2f, MinRange = 2f, MaxRange = 30f, Cooldown = 1.5f,
                    Move = Bolt("Aimed Shot", 0.8f, 0.6f, 14f, 12f)
                },
                new EnemyAttackData
                {
                    Telegraph = TelegraphKind.Aimed, Weight = 1f, MinRange = 2f, MaxRange = 20f, Cooldown = 4f,
                    HitCount = 3, HitInterval = 0.2f,
                    Move = Bolt("Repeater Burst", 1.0f, 0.8f, 7f, 5f)
                }
            };
            return t;
        }

        public static EnemyTuning CreateSparringDummy()
        {
            var t = new EnemyTuning();
            t.DisplayName = "Sparring Dummy";
            t.Archetype = EnemyArchetype.Dummy;
            t.MaxHealth = 200f;
            t.Unkillable = true;
            t.HealthRefillDelay = 3f;
            t.MaxPoise = 30f;
            t.AggroRange = 6f;                       // only swings when you're close enough to practise on
            t.WalkSpeed = 0f;
            t.ChaseSpeed = 0f;
            t.StrafeSpeed = 0f;
            t.RetreatSpeed = 0f;
            t.TurnRate = 180f;
            t.AttackIntervalMin = 2.5f;              // one swing every 2.5 s, like a metronome
            t.AttackIntervalMax = 2.5f;
            t.BackOffChance = 0f;
            t.UsesAttackToken = false;
            t.Attacks = new[]
            {
                new EnemyAttackData
                {
                    Telegraph = TelegraphKind.Normal, MinRange = 0f, MaxRange = 3f,
                    Move = Melee("Practice Swing", 0.6f, 0.12f, 0.5f, 5f, 5f, 2.4f, 120f, 0f, 240f)
                }
            };
            return t;
        }

        static EnemyAttackData[] CreateDaoSoldierAttacks()
        {
            MoveData heavy = Melee("Heavy Overhead", 0.95f, 0.14f, 0.95f, 26f, 40f, 2.6f, 60f, 0.6f, 180f);
            heavy.Kind = HitKind.Heavy;
            heavy.HyperArmor = true;                 // can't be jabbed out of it: dodge or deflect
            heavy.Knockback = 1.2f;
            heavy.Hitstop = 0.09f;
            heavy.GuardStaminaDamage = 35f;
            MoveData thrust = Melee("Delayed Thrust", 1.15f, 0.12f, 0.7f, 18f, 20f, 3.2f, 30f, 1.0f, 240f);
            thrust.Kind = HitKind.Heavy;
            return new[]
            {
                new EnemyAttackData
                {
                    Telegraph = TelegraphKind.Normal, Weight = 3f, MaxRange = 2.4f,
                    Move = Melee("Quick Slash", 0.50f, 0.12f, 0.55f, 12f, 15f, 2.4f, 100f, 0.5f, 300f)
                },
                new EnemyAttackData
                {
                    Telegraph = TelegraphKind.Heavy, Weight = 2f, MaxRange = 2.6f, Cooldown = 3f,
                    Move = heavy
                },
                new EnemyAttackData
                {
                    Telegraph = TelegraphKind.Normal, Weight = 2f, MaxRange = 2.4f, Cooldown = 2f,
                    HitCount = 2, HitInterval = 0.35f,
                    Move = Melee("Double Slash", 0.50f, 0.12f, 0.60f, 10f, 12f, 2.4f, 100f, 0.5f, 300f)
                },
                new EnemyAttackData
                {
                    Telegraph = TelegraphKind.Delayed, Weight = 1.5f, MinRange = 1.2f, MaxRange = 3.2f, Cooldown = 4f,
                    Move = thrust
                }
            };
        }

        static MoveData Melee(string name, float startup, float active, float recovery, float damage, float poise,
            float range, float arc, float lunge, float tracking)
        {
            return new MoveData
            {
                DisplayName = name, Kind = HitKind.Light, Limb = Limb.Weapon,
                Startup = startup, Active = active, Recovery = recovery,
                Damage = damage, PoiseDamage = poise, GuardStaminaDamage = damage * 1.25f, Knockback = 0.3f, Hitstop = 0.05f,
                Range = range, ArcDegrees = arc, LungeDistance = lunge, LungeTime = 0.2f, TrackingTurnRate = tracking,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = startup + active + recovery, DodgeCancelAt = startup + active + recovery,
                StaminaCost = 0f, MomentumGain = 0f
            };
        }

        static MoveData Bolt(string name, float startup, float recovery, float damage, float poise)
        {
            return new MoveData
            {
                DisplayName = name, Kind = HitKind.Projectile, Limb = Limb.Weapon,
                Startup = startup, Active = 0f, Recovery = recovery,
                Damage = damage, PoiseDamage = poise, GuardStaminaDamage = damage, Knockback = 0.2f, Hitstop = 0.03f,
                Range = 0f, ArcDegrees = 0f, OriginHeight = 1.4f, OriginForward = 0.5f, LungeDistance = 0f,
                TrackingTurnRate = 200f, ComboWindowStart = 0f, ComboWindowEnd = 0f,
                ChainCancelAt = startup + recovery, DodgeCancelAt = startup + recovery,
                StaminaCost = 0f, MomentumGain = 0f,
                LaunchesProjectile = true,
                Projectile = new ProjectileSpec { Speed = 32f, Radius = 0.12f, MaxRange = 40f, VisualScale = 0.5f }
            };
        }
    }
}
