using System;

namespace VaatusRevenge.Core
{
    // Everything one element can do on the standard buttons: the light chain, the charged heavy, sprint
    // and jump attacks, the ranged skill, the zip strike, the dodge, guard/parry (and whether this element
    // blocks at all: Guard.Style) and the element's identity mechanic.
    // Switching element = switching move set, so the same buttons do that element's moves.
    //
    // Lives inside a MoveSetAsset (ScriptableObject). The model reads it live and never writes to it.
    // Field defaults are Fire with the Fluid preset.
    [Serializable]
    public class ElementMoveSet
    {
        public string DisplayName = "Fire";
        public ElementId Element = ElementId.Fire;

        public MoveData[] LightChain = CreateFireLightChain();   // light presses walk through this; after the last it loops
        public MoveData Heavy = CreateFireHeavy();               // Startup counts from the release of the charge
        public ChargeSettings Charge = new ChargeSettings();
        public MoveData SprintAttack = CreateFireSprintAttack();
        public float SprintAttackMinSprintTime = 0.2f;           // light while sprinting at least this long = sprint attack
        public float SprintAttackGrace = 0.15f;                  // ...or this soon after such a sprint ends, while still moving at
                                                                 // full running speed (let go of sprint a moment early, still kick)
        // Jump attack. Its Startup, Active, Range and Arc are not used: it hangs for Plunge.HangTime, falls until
        // it lands, bursts in a Plunge.RingRadius ring, then Recovery is the landing recovery. Its cancel times
        // (ChainCancelAt, DodgeCancelAt) count from the landing.
        public MoveData PlungeAttack = CreateFirePlunge();
        public PlungeSettings Plunge = new PlungeSettings();
        public MoveData Skill = CreateFireSkill();               // ranged skill: launches Skill.Projectile when startup ends
        // Dash to a far target and hit it (see ZipStrikeSettings). Its LungeDistance is ignored: the dash is as long
        // as the gap to the target; it covers the startup and active frames (or the last LungeTime of them).
        public MoveData ZipStrike = CreateFireZipStrike();
        public ZipStrikeSettings Zip = new ZipStrikeSettings();
        public DodgeProfile Dodge = new DodgeProfile();
        public GuardSettings Guard = new GuardSettings();
        public MomentumSettings Momentum = new MomentumSettings();

        public static ElementMoveSet CreateFireFluid()
        {
            return new ElementMoveSet();
        }

        // Elden Ring-like commitment: costlier attacks and dodge, no dodge-cancelling into attacks, no
        // perfect-dodge reward, and attacks can only be dodge-cancelled late in their recovery.
        public static ElementMoveSet CreateFirePunishing()
        {
            var set = new ElementMoveSet();
            for (int i = 0; i < set.LightChain.Length; i++) set.LightChain[i].StaminaCost = 13f;
            set.Heavy.StaminaCost = 28f;
            set.ZipStrike.StaminaCost = 20f;
            MoveData[] all = { set.LightChain[0], set.LightChain[1], set.LightChain[2], set.Heavy, set.SprintAttack, set.Skill, set.ZipStrike };
            foreach (MoveData move in all) move.DodgeCancelAt = move.ActiveEnd + move.Recovery * 0.6f;
            set.Charge.CanDodgeCancelCharge = false;
            set.PlungeAttack.DodgeCancelAt = set.PlungeAttack.Recovery;   // no dodging out of a landing

            DodgeProfile d = set.Dodge;
            d.StaminaCost = 16f;
            d.Duration = 0.36f;
            d.IFrameStart = 0.04f;
            d.IFrameEnd = 0.30f;
            d.EndRecovery = 0.12f;
            d.AttackCancelAt = 0.48f;
            d.NextDodgeAt = 0.48f;
            d.PerfectDodgeEnabled = false;
            return set;
        }

        static MoveData[] CreateFireLightChain()
        {
            var jab = new MoveData
            {
                DisplayName = "Flame Jab", Kind = HitKind.Light, Limb = Limb.LeftFist,
                Startup = 0.12f, Active = 0.10f, Recovery = 0.30f,
                Damage = 8f, PoiseDamage = 8f, GuardStaminaDamage = 8f, Knockback = 0.2f, Hitstop = 0.035f,
                Range = 2.6f, ArcDegrees = 70f, LungeDistance = 0.4f,
                ComboWindowStart = 0.14f, ComboWindowEnd = 0.40f, ChainCancelAt = 0.24f, DodgeCancelAt = 0.22f,
                StaminaCost = 9f, MomentumGain = 8f
            };
            var cross = new MoveData
            {
                DisplayName = "Flame Cross", Kind = HitKind.Light, Limb = Limb.RightFist,
                Startup = 0.13f, Active = 0.10f, Recovery = 0.32f,
                Damage = 9f, PoiseDamage = 9f, GuardStaminaDamage = 9f, Knockback = 0.25f, Hitstop = 0.035f,
                Range = 2.7f, ArcDegrees = 70f, LungeDistance = 0.45f,
                ComboWindowStart = 0.15f, ComboWindowEnd = 0.42f, ChainCancelAt = 0.25f, DodgeCancelAt = 0.23f,
                StaminaCost = 9f, MomentumGain = 8f
            };
            var kick = new MoveData
            {
                DisplayName = "Dragon Tail Kick", Kind = HitKind.Light, Limb = Limb.RightFoot,
                Startup = 0.20f, Active = 0.14f, Recovery = 0.46f,
                Damage = 15f, PoiseDamage = 22f, GuardStaminaDamage = 15f, Knockback = 0.8f, Hitstop = 0.06f,
                Range = 3.0f, ArcDegrees = 200f, OriginForward = 0f, LungeDistance = 0.6f,
                ComboWindowStart = 0.36f, ComboWindowEnd = 0.70f, ChainCancelAt = 0.50f, DodgeCancelAt = 0.34f,
                StaminaCost = 13f, MomentumGain = 10f                // the committed finisher earns a little more
            };
            return new[] { jab, cross, kick };
        }

        static MoveData CreateFireHeavy()
        {
            return new MoveData
            {
                DisplayName = "Fa Jin Palm", Kind = HitKind.Heavy, Limb = Limb.RightFist,
                Startup = 0.28f, Active = 0.12f, Recovery = 0.55f,
                Damage = 20f, PoiseDamage = 30f, GuardStaminaDamage = 25f, Knockback = 1.2f, Hitstop = 0.08f,
                Range = 3.4f, ArcDegrees = 80f, LungeDistance = 0.6f, TrackingTurnRate = 720f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.95f, DodgeCancelAt = 0.40f,
                StaminaCost = 22f, MomentumGain = 14f, HyperArmor = true
            };
        }

        static MoveData CreateFireSprintAttack()
        {
            return new MoveData
            {
                DisplayName = "Flying Fire Kick", Kind = HitKind.Sprint, Limb = Limb.RightFoot,
                Startup = 0.18f, Active = 0.14f, Recovery = 0.40f,
                Damage = 16f, PoiseDamage = 22f, GuardStaminaDamage = 16f, Knockback = 1.0f, Hitstop = 0.06f,
                Range = 2.8f, ArcDegrees = 90f, LungeDistance = 3.2f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.50f, DodgeCancelAt = 0.32f,
                StaminaCost = 16f, MomentumGain = 14f
            };
        }

        static MoveData CreateFirePlunge()
        {
            return new MoveData
            {
                DisplayName = "Falling Axe Kick", Kind = HitKind.Plunge, Limb = Limb.RightFoot,
                Startup = 0f, Active = 0f, Recovery = 0.55f,             // a long landing: the axe kick is a commitment
                Damage = 18f, PoiseDamage = 25f, GuardStaminaDamage = 18f, Knockback = 1.0f, Hitstop = 0.07f,
                Range = 0f, ArcDegrees = 360f, LungeDistance = 0f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.55f, DodgeCancelAt = 0.35f,
                StaminaCost = 20f, MomentumGain = 14f
            };
        }

        // Flame Step Strike: fire from the feet carries the Avatar across the gap into a flying kick. A fire-assisted
        // dash, like the Flame Step dodge (canon firebending per the spec's lore guardrails), not full jet flight.
        static MoveData CreateFireZipStrike()
        {
            return new MoveData
            {
                DisplayName = "Flame Step Strike", Kind = HitKind.Sprint, Limb = Limb.RightFoot,
                Startup = 0.30f, Active = 0.12f, Recovery = 0.38f,
                Damage = 12f, PoiseDamage = 18f, GuardStaminaDamage = 12f, Knockback = 0.9f, Hitstop = 0.05f,
                Range = 2.4f, ArcDegrees = 90f, LungeDistance = 0f, TrackingTurnRate = 1080f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.44f, DodgeCancelAt = 0.36f,
                StaminaCost = 14f, MomentumGain = 10f
            };
        }

        static MoveData CreateFireSkill()
        {
            return new MoveData
            {
                DisplayName = "Fire Blast", Kind = HitKind.Projectile, Limb = Limb.BothFists,
                Startup = 0.22f, Active = 0f, Recovery = 0.30f,
                Damage = 13f, PoiseDamage = 10f, GuardStaminaDamage = 13f, Knockback = 0.4f, Hitstop = 0.03f,
                Range = 0f, ArcDegrees = 0f, OriginHeight = 1.3f, OriginForward = 0.5f, LungeDistance = 0f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.40f, DodgeCancelAt = 0.30f,
                StaminaCost = 22f, MomentumGain = 8f,
                LaunchesProjectile = true,
                Projectile = new ProjectileSpec { Speed = 30f, Radius = 0.3f, MaxRange = 26f, VisualScale = 1f }
            };
        }
    }
}
