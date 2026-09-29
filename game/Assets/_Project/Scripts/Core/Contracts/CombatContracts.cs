using System.Numerics;

namespace VaatusRevenge.Core
{
    // Shared combat vocabulary. Player, enemies, projectiles and the hit system all speak in these
    // types, so any attacker can hit any defender without knowing what it is.

    public enum Team { Player, Enemy, Neutral }

    // The elements are data, not code paths: each one gets its own move set asset.
    public enum ElementId { None, Fire, Water, Earth, Air }

    public enum HitKind { Light, Heavy, Special, Projectile, Plunge, Sprint }

    // Which body part (or weapon) delivers a strike. The grey-box visuals and, later, the animation
    // system use it to show the move; the rules don't care.
    public enum Limb { RightFist, LeftFist, RightFoot, LeftFoot, BothFists, Weapon }

    // What a hit carries. The defender decides what actually happens (dodged, blocked, deflected...).
    public struct DamageInfo
    {
        public float Damage;              // health removed by a clean hit, before the defender's modifiers
        public float PoiseDamage;         // stagger build-up; enough of it breaks the defender's poise
        public float GuardStaminaDamage;  // stamina a guarding defender pays to block it
        public Vector3 Direction;         // world space, horizontal, attacker -> defender, normalised (Zero if unknown)
        public Vector3 Point;             // world-space contact point, for effects
        public float Knockback;           // metres of push on a clean hit
        public float Hitstop;             // seconds of freeze-frame on a clean hit (the "crunch" of impact)
        public HitKind Kind;
        public Team SourceTeam;
        public int SourceId;              // CombatIds id of the attacker
        public int AttackId;              // CombatIds id of this swing or projectile: one attack hits a target once
        public bool Parryable;
        public bool Unblockable;
        public float LaunchSpeed;         // MoveData.LaunchSpeed: throws a grounded target up (launchable targets only)
        public float AirLift;             // MoveData.AirLift: keeps an airborne target up
        public float SlamSpeed;           // MoveData.SlamSpeed: drives an airborne target down
    }

    public enum HitOutcome
    {
        Ignored,       // same team, already dead, or already hit by this AttackId
        Hit,           // clean hit
        Blocked,       // guard absorbed it (guard stamina paid)
        GuardBroken,   // guard ran out of stamina: defender is staggered
        Parried,       // deflected: the ATTACKER should be staggered (the attacker handles this result)
        Evaded,        // dodge invincibility frames
        PerfectEvade   // dodged at the last moment: the defender earns the perfect-dodge reward
    }

    public struct HitResult
    {
        public HitOutcome Outcome;
        public float DamageDealt;
        public bool PoiseBroken;   // the defender was staggered by this hit
        public bool Killed;        // the defender died from this hit (Outcome is still Hit)

        public static HitResult Ignored => new HitResult { Outcome = HitOutcome.Ignored };
    }

    // Anything that can be hit. Implemented by the Unity components for the player, enemies and dummies.
    public interface IDamageReceiver
    {
        Team Team { get; }
        bool IsAlive { get; }
        HitResult ReceiveHit(in DamageInfo hit);
    }

    // Our own ids for fighters and attacks (Unity's instance ids are being phased out in Unity 6.x).
    public static class CombatIds
    {
        static int next;

        public static int Next()
        {
            next++;
            return next;
        }
    }
}
