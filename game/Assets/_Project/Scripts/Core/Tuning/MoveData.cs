using System;

namespace VaatusRevenge.Core
{
    // One attack's numbers: frame data, damage, reach, movement and cancel rules. Used for the player's
    // moves (inside ElementMoveSet) and for enemy attacks (inside EnemyAttackData).
    //
    // Frame data is the fighting-game way of describing an attack in three phases:
    //   Startup  - the wind-up. Nothing can be hit yet; this is what defenders read and react to.
    //   Active   - the strike can hit.
    //   Recovery - the follow-through. The attacker is committed and open to punishment.
    // At 60 fps, 0.1 s = 6 frames. All times are seconds from the start of the move.
    [Serializable]
    public class MoveData
    {
        public string DisplayName = "Strike";        // shown on the HUD and debug panel; lore names live here, never in code
        public HitKind Kind = HitKind.Light;
        public Limb Limb = Limb.RightFist;           // which fist/foot/weapon the visuals animate

        // --- Presentation (ids looked up in data, never code paths) ---
        public string AnimationKey = "";             // which body animation plays (AnimationKeys; a pack clip mapped to this key wins)
        public string EffectKey = "";                // which fire effect plays when the strike goes active (EffectKeys; "" = default burst)

        // --- Frame data ---
        public float Startup = 0.12f;
        public float Active = 0.10f;
        public float Recovery = 0.30f;

        // --- What a clean hit does ---
        public float Damage = 8f;
        public float PoiseDamage = 8f;
        public float GuardStaminaDamage = 8f;        // stamina a guarding defender pays to block it
        public float Knockback = 0.25f;              // metres of push
        public float Hitstop = 0.035f;               // freeze-frame on impact; sells the weight of the hit
        public bool Parryable = true;                // can be deflected
        public bool Unblockable = false;             // ignores guard (can still be dodged)
        // Danger sense's red mark (and the enemy's red wind-up glow): this one has to be dodged. Gold otherwise.
        public bool MustDodge => !Parryable || Unblockable;

        // --- Reach (the strike is an arc in front of the attacker) ---
        public float Range = 2.6f;                   // metres from the strike origin to the target's body
        public float ArcDegrees = 70f;               // full width of the swing (360 = all round)
        public float VerticalReach = 1.0f;           // how far above/below the origin it still connects
        public float OriginHeight = 1.1f;            // strike/projectile origin above the feet
        public float OriginForward = 0.3f;           // ...and in front of the body

        // --- Movement during the move ---
        public float LungeDistance = 0.4f;           // forward step; stops short of the target
        public float LungeTime = 0f;                 // 0 = lunge across startup + active; > 0 = only this long, ending when active ends
        public float TrackingTurnRate = 900f;        // degrees/second the attacker still turns toward its target during startup;
                                                     // tracking stops when the active frames start (the attack commits)

        // --- Chaining and cancelling (seconds from move start) ---
        public float ComboWindowStart = 0.14f;       // a light press in this window continues the chain; later presses restart it
        public float ComboWindowEnd = 0.40f;
        public float ChainCancelAt = 0.24f;          // "cancel point": earliest the next attack (buffered or chained) can start
        public float DodgeCancelAt = 0.22f;          // earliest a dodge, jump or guard can cut the move short

        // --- Air: launching and juggling (Spider-Man-style aerial combat) ---
        public float LaunchSpeed = 0f;               // > 0: a clean hit on a grounded, launchable target throws it upward at this speed (m/s)
        public float AirLift = 0f;                   // > 0: a clean hit on an airborne target sets its upward speed to at least this (keeps it juggled)
        public float SlamSpeed = 0f;                 // > 0: a clean hit on an airborne target drives it down at this speed (ends the juggle)
        public float SelfLift = 0f;                  // > 0: the attacker's own upward speed when the move starts (a launcher's jump, hanging in the air)

        // --- Costs and rewards ---
        public float StaminaCost = 9f;
        public float MomentumGain = 8f;              // Momentum earned when this move lands a clean hit
        public bool HyperArmor = false;              // true = poise can't break from HyperArmorFrom until active ends (damage still hurts)
        public float HyperArmorFrom = 0f;            // seconds from move start when hyper armour begins (0 = from the start)

        // --- Element mechanics (Build 05) ---
        public int HitCount = 1;                     // > 1: sub-hits spread over the active frames, sub-hit k live from
        public float HitInterval = 0f;               // ActiveStart + k x HitInterval. Damage and poise are per sub-hit; each has its
                                                     // own AttackId (Active must cover (HitCount - 1) x HitInterval)
        public float PullDistance = 0f;              // > 0: a clean hit draws the target toward the attacker by up to this (Water)
        public float OrbitDegrees = 0f;              // > 0: the lunge curves round the target, ending this many degrees round it (Air)
        public float HealOnHit = 0f;                 // health the attacker gets back per clean hit (Water restores you)...
        public float HealPerMoveMax = 0f;            // ...at most this much per move (0 = HealOnHit)

        // --- Projectile (Fire Blast, crossbow bolts) ---
        public bool LaunchesProjectile = false;      // true = launches Projectile when startup ends instead of a melee arc
        public ProjectileSpec Projectile = new ProjectileSpec();

        public float ActiveStart => Startup;
        public float ActiveEnd => Startup + Active;
        public float TotalDuration => Startup + Active + Recovery;
    }
}
