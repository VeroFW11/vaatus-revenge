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
        public float MaxHealth = 180f;
        public bool Unkillable = false;              // training dummy: health never drops below 1
        public float HealthRefillDelay = 0f;         // > 0: health refills after this long without being hit
        public float MaxPoise = 52f;                 // more than one light string plus a jab (43 + 8), so the break-out (6th hit) gets
                                                     // its turn before poise breaks; a longer mash staggers. Was 45 for the old 3-hit
                                                     // chain (report 04, W-01: at 45 the 6th hit staggered and wiped the break-out count)
        public float PoiseRegenDelay = 2f;           // poise refills this long after the last poise damage...
        public float PoiseRegenRate = 60f;           // ...at this many points per second
        public float StaggerDuration = 1.0f;         // stunned time when poise breaks
        public float StaggerImmunity = 1.5f;         // after a stagger ends, poise can't be broken again for this long,
                                                     // so a combo earns one stagger and the enemy gets to swing back
                                                     // (no stun-locking; decided 28 Sep). Deflects still stagger.
        public float ParriedStaggerDuration = 1.3f;  // stunned time when the player deflects this enemy's attack
        public float KnockbackTime = 0.15f;          // a clean hit's knockback distance is covered over this long

        // --- Juggling (launchers and air combos) ---
        public bool Launchable = true;               // a launcher can throw it into the air (bosses and brutes: false)
        public float LaunchedGravity = 22f;          // gravity while launched (a little floatier than normal, so air combos connect)
        public float MaxJuggleTime = 3.5f;           // after this long in the air, air hits stop lifting it (no infinite juggles)
        public float KnockdownTime = 0.9f;           // time on the ground after a juggle lands, before it gets up

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
        public float LeashRadius = 0f;               // > 0: never wanders further than this from its home point (where it stood
                                                     // on its first frame or after Reset), e.g. an archer holding a platform

        // --- Attack rhythm ---
        public float AttackIntervalMin = 1.2f;       // pause between attacks (random in range). The dummy uses Min as its fixed rhythm
        public float AttackIntervalMax = 2.2f;
        public float CircleTimeMin = 0.8f;           // circles one way this long before switching direction
        public float CircleTimeMax = 2.0f;
        public bool UsesAttackToken = true;          // takes a shared token to attack, so only a few enemies attack at once

        public EnemyAttackData[] Attacks = CreateDaoSoldierAttacks();

        // --- Anti-mash counter (see EnemyBreakOutRule) ---
        public EnemyBreakOutRule BreakOut = CreateDaoSoldierBreakOut();

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
            t.AttackIntervalMin = 2.5f;
            t.AttackIntervalMax = 3.5f;
            t.LeashRadius = 2f;                      // smaller than the sandbox platform's half-width (3 m) plus a body radius,
                                                     // so the leash alone keeps it off the edge; the ledge check is only a backup
            t.UsesAttackToken = true;                // shares the encounter's tokens with the soldiers (ENEMY-01, pending David)
            t.BreakOut.Enabled = false;              // it backs away instead; a crossbow has no shove
            t.Attacks = new[]
            {
                new EnemyAttackData
                {
                    Telegraph = TelegraphKind.Aimed, Weight = 2f, MinRange = 2f, MaxRange = 30f, Cooldown = 1.5f,
                    Move = Keyed(Bolt("Aimed Shot", 0.8f, 0.6f, 14f, 12f), AnimationKeys.CrossbowShot)
                },
                new EnemyAttackData
                {
                    Telegraph = TelegraphKind.Aimed, Weight = 1f, MinRange = 2f, MaxRange = 20f, Cooldown = 4f,
                    HitCount = 3, HitInterval = 0.2f,
                    Move = Keyed(Bolt("Repeater Burst", 1.0f, 0.8f, 7f, 5f), AnimationKeys.CrossbowBurst)
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
            t.BreakOut.Enabled = false;              // a practice target: mashing it is the point
            t.Launchable = false;                    // it's a post planted in the ground
            t.Attacks = new[]
            {
                new EnemyAttackData
                {
                    Telegraph = TelegraphKind.Normal, MinRange = 0f, MaxRange = SwordReach,
                    Move = Keyed(Melee("Practice Swing", 0.6f, 0.12f, 0.5f, 5f, 5f, SwordReach, 120f, 0f, 240f), AnimationKeys.PracticeSwing)
                }
            };
            return t;
        }

        // Weapon reach from the enemy's centre to the blade tip, matching what the grey-box fighter draws. The hit
        // test adds the target's body radius, so a swing lands when the tip reaches the body, never from further off.
        const float SwordReach = 2.1f;
        const float ThrustReach = 2.6f;
        const float StrikeOriginForward = 0.3f;

        static EnemyAttackData[] CreateDaoSoldierAttacks()
        {
            MoveData heavy = Melee("Heavy Overhead", 0.95f, 0.14f, 0.95f, 26f, 40f, SwordReach, 60f, 0.6f, 180f);
            heavy.Kind = HitKind.Heavy;
            heavy.AnimationKey = AnimationKeys.SwordOverhead;
            heavy.HyperArmor = true;                 // from halfway through the wind-up: mash into it and you get hit
            heavy.HyperArmorFrom = heavy.Startup * 0.5f;
            heavy.Knockback = 1.2f;
            heavy.Hitstop = 0.09f;
            heavy.GuardStaminaDamage = 35f;
            MoveData thrust = Melee("Delayed Thrust", 1.15f, 0.12f, 0.7f, 18f, 20f, ThrustReach, 30f, 1.0f, 240f);
            thrust.Kind = HitKind.Heavy;
            thrust.AnimationKey = AnimationKeys.SwordThrust;
            thrust.HyperArmor = true;
            thrust.HyperArmorFrom = thrust.Startup * 0.5f;
            return new[]
            {
                new EnemyAttackData
                {
                    Telegraph = TelegraphKind.Normal, Weight = 3f, MaxRange = SwordReach,    // no armour: jab it out of the wind-up
                    Move = Keyed(Melee("Quick Slash", 0.50f, 0.12f, 0.55f, 12f, 15f, SwordReach, 100f, 0.5f, 300f), AnimationKeys.SwordSlash)
                },
                new EnemyAttackData
                {
                    Telegraph = TelegraphKind.Heavy, Weight = 2f, MaxRange = SwordReach, Cooldown = 3f,
                    Move = heavy
                },
                new EnemyAttackData
                {
                    Telegraph = TelegraphKind.Normal, Weight = 2f, MaxRange = SwordReach, Cooldown = 2f,
                    HitCount = 2, HitInterval = 0.35f,
                    Move = Keyed(Melee("Double Slash", 0.50f, 0.12f, 0.60f, 10f, 12f, SwordReach, 100f, 0.5f, 300f), AnimationKeys.SwordDoubleSlash)
                },
                new EnemyAttackData
                {
                    Telegraph = TelegraphKind.Delayed, Weight = 1.5f, MinRange = 1.2f, MaxRange = ThrustReach, Cooldown = 4f,
                    Move = thrust
                }
            };
        }

        // The Dao Soldier's break-out: a wide shoulder-shove-and-slash. Armoured from its first frame (that's what
        // "breaking out" of a combo means), but with a telegraph long enough to react to (0.6 s; human reaction is
        // about 0.25 s, and the extra 0.1 s over the Quick Slash lets a Punishing player finish the light they were
        // in before dodging) and a normal strike that can be dodged, blocked or deflected. It hits hard (30, more than
        // the Heavy Overhead), breaks the player's poise and knocks them back, and recovers quickly; if it landed, the
        // next attack follows straight away. A player who keeps mashing into the glow loses the trade.
        // Tuned in playtest report 02, round 3: masher vs one soldier in Fluid 100% -> 65% wins, the defending bots
        // stay at 98-100%. Weaker settings (14-24 damage, a long recovery the masher could punish) left it at 88-100%.
        static EnemyBreakOutRule CreateDaoSoldierBreakOut()
        {
            MoveData shove = Melee("Break-Out Shove", 0.60f, 0.12f, 0.35f, 30f, 35f, SwordReach, 160f, 0.3f, 360f);
            shove.Kind = HitKind.Heavy;
            shove.AnimationKey = AnimationKeys.Shove;
            shove.HyperArmor = true;
            shove.HyperArmorFrom = 0f;
            shove.Knockback = 2.0f;
            shove.Hitstop = 0.08f;
            shove.GuardStaminaDamage = 25f;
            return new EnemyBreakOutRule
            {
                Enabled = true,
                // 29 Sep (Spider-Man controls): the 5-hit string lands whole (its 5 hits span ~1.3 s); looping back into
                // a 6th hit within 2 s arms the shove. Was 3 hits in 1.2 s for the old 3-hit chain (report 03, V-03).
                HitsToTrigger = 6,
                HitWindow = 2.0f,
                MaxWait = 0.6f,
                Cooldown = 2f,
                FollowUpDelay = 0f,
                RestoresPoise = true,
                CutsWindUp = true,
                Attack = new EnemyAttackData
                {
                    Telegraph = TelegraphKind.BreakOut, Weight = 0f, MaxRange = SwordReach + 0.3f,
                    Move = shove
                }
            };
        }

        static MoveData Keyed(MoveData move, string animationKey)
        {
            move.AnimationKey = animationKey;
            return move;
        }

        // reach = distance from the enemy's centre to the weapon tip (Range is measured from the strike origin).
        static MoveData Melee(string name, float startup, float active, float recovery, float damage, float poise,
            float reach, float arc, float lunge, float tracking)
        {
            return new MoveData
            {
                DisplayName = name, Kind = HitKind.Light, Limb = Limb.Weapon,
                Startup = startup, Active = active, Recovery = recovery,
                Damage = damage, PoiseDamage = poise, GuardStaminaDamage = damage * 1.25f, Knockback = 0.3f, Hitstop = 0.05f,
                OriginForward = StrikeOriginForward, Range = reach - StrikeOriginForward,
                ArcDegrees = arc, LungeDistance = lunge, LungeTime = 0.2f, TrackingTurnRate = tracking,
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
