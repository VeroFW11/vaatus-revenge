using System;

namespace VaatusRevenge.Core
{
    // Everything one element can do on the standard buttons: the light chain, the charged heavy, sprint
    // and jump attacks, the ranged skill, the zip strike, the dodge, guard/parry (and whether this element
    // blocks at all: Guard.Style) and the element's identity mechanic.
    // Switching element = switching move set, so the same buttons do that element's moves (ElementLoadout holds
    // all four; the other elements' data lives in ElementMoveSet.Water/.Earth/.Air.cs).
    //
    // Lives inside a MoveSetAsset (ScriptableObject). The model reads it live and never writes to it.
    // Field defaults are Fire with the Fluid preset.
    [Serializable]
    public partial class ElementMoveSet
    {
        public string DisplayName = "Fire";
        public ElementId Element = ElementId.Fire;
        public string AnimationStyle = "";                       // the body's style for this element's stance and clip
                                                                 // overrides ("" = Fire, "water", "earth", "air")

        public MoveData[] LightChain = CreateFireLightChain();   // light presses walk through this; after the last it loops
        public MoveData[] PauseChain = CreateFirePauseChain();   // X X, wait, X: the pause branch (after it, back to LightChain[0])
        public MoveData DodgeStrike = CreateFireDodgeStrike();   // X late in a dodge: a counter that takes the string's next slot
        public ElementRhythm Rhythm = CreateFireRhythm();        // how this element's martial art changes the beat
        public MoveData Launcher = CreateFireLauncher();         // attack held on the ground: throws the target up, you follow
        public MoveData[] AirChain = CreateFireAirChain();       // attack in the air: walks through this (doesn't loop)
        public AerialSettings Aerial = new AerialSettings();
        public MoveData AbilityNorth = CreateFireWhip();         // hold LB + Y: mid-range ability
        public MoveData AbilityEast = CreateFireWheel();         // hold LB + B: close, all-round ability
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
        // as the gap to the target; it covers the startup frames and arrives as the kick goes active.
        public MoveData ZipStrike = CreateFireZipStrike();
        public ZipStrikeSettings Zip = new ZipStrikeSettings();
        public DodgeProfile Dodge = new DodgeProfile();
        public GuardSettings Guard = new GuardSettings();
        public MomentumSettings Momentum = new MomentumSettings();

        // Bumped when the defaults change in a way old assets must not keep (the sandbox builder offers to reset an
        // asset whose DataVersion is behind). A field missing from an old asset keeps its initialiser, so it reads 0.
        public int DataVersion = 0;
        public const int CurrentDataVersion = 6;   // 6: Build 05 verify (dodge strike reach, Water/Air dash ease, side-slip circle)

        public static ElementMoveSet CreateFireFluid()
        {
            return new ElementMoveSet { DataVersion = CurrentDataVersion };
        }

        // Elden Ring-like commitment: costlier attacks and dodge, no dodge-cancelling into attacks, no
        // perfect-dodge reward, and attacks can only be dodge-cancelled late in their recovery.
        public static ElementMoveSet CreateFirePunishing()
        {
            ElementMoveSet set = CreateFireFluid();
            ApplyPunishing(set);
            return set;
        }

        // Turns a Fluid move set into its Punishing version, the same rules for every element: stamina costs by slot,
        // every move dodge-cancellable only late in its recovery, a smaller air game, no dodging out of a charge or a
        // plunge landing, and the Punishing dodge (DodgeProfile.ApplyPunishing).
        public static void ApplyPunishing(ElementMoveSet set)
        {
            if (set == null) return;
            SetStamina(set.LightChain, 13f);
            SetStamina(set.PauseChain, 13f);
            SetStamina(set.AirChain, 11f);
            SetStamina(set.DodgeStrike, 11f);
            SetStamina(set.Heavy, 28f);
            SetStamina(set.ZipStrike, 20f);
            SetStamina(set.Launcher, 18f);
            SetStamina(set.AbilityNorth, 26f);
            SetStamina(set.AbilityEast, 28f);
            LateDodgeCancel(set.LightChain);
            LateDodgeCancel(set.PauseChain);
            LateDodgeCancel(set.AirChain);
            LateDodgeCancel(set.DodgeStrike, set.Heavy, set.SprintAttack, set.Skill, set.ZipStrike, set.Launcher, set.AbilityNorth,
                set.AbilityEast);
            if (set.Aerial != null)
            {
                set.Aerial.AirAttacksPerJump = 4;
                set.Aerial.AirDashesPerJump = 0;
            }
            if (set.Charge != null) set.Charge.CanDodgeCancelCharge = false;
            if (set.PlungeAttack != null) set.PlungeAttack.DodgeCancelAt = set.PlungeAttack.Recovery;   // no dodging out of a landing
            if (set.Dodge != null) set.Dodge.ApplyPunishing();
        }

        static void SetStamina(MoveData[] moves, float cost)
        {
            if (moves == null) return;
            for (int i = 0; i < moves.Length; i++) SetStamina(moves[i], cost);
        }

        static void SetStamina(MoveData move, float cost)
        {
            if (move != null) move.StaminaCost = cost;
        }

        static void LateDodgeCancel(params MoveData[] moves)
        {
            if (moves == null) return;
            foreach (MoveData move in moves)
            {
                if (move != null) move.DodgeCancelAt = move.ActiveEnd + move.Recovery * 0.6f;
            }
        }

        // Northern Shaolin's five-strike string: lead punch, rear punch, front snap kick (tan tui), a spinning kick and
        // a double-palm push that throws a cone of fire. Each hit reaches a little further and hits a little harder,
        // so the string walks you forward: Fire's relentless pressure.
        // Fluid: hits 1-4 can be dodged out of from their first frame (you can always dodge the start of a hit); the
        // finisher commits for a moment (DodgeCancelAt 0.30). Punishing overwrites these (ApplyPunishing).
        // Poise damage over the whole string is 43, just under a Dao Soldier's 45: one full string never staggers a
        // fresh soldier by itself (playtest report 01, a rule the tests pin); a string and a bit does.
        static MoveData[] CreateFireLightChain()
        {
            var jab = new MoveData
            {
                DisplayName = "Flame Jab", Kind = HitKind.Light, Limb = Limb.LeftFist,
                AnimationKey = AnimationKeys.Jab, EffectKey = EffectKeys.Burst,
                Startup = 0.12f, Active = 0.10f, Recovery = 0.30f,
                Damage = 8f, PoiseDamage = 8f, GuardStaminaDamage = 8f, Knockback = 0.2f, Hitstop = 0.035f,
                Range = 2.6f, ArcDegrees = 70f, LungeDistance = 0.4f,
                ComboWindowStart = 0.14f, ComboWindowEnd = 0.40f, ChainCancelAt = 0.24f, DodgeCancelAt = 0f,
                StaminaCost = 9f, MomentumGain = 8f
            };
            var cross = new MoveData
            {
                DisplayName = "Flame Cross", Kind = HitKind.Light, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.Cross, EffectKey = EffectKeys.Burst,
                Startup = 0.13f, Active = 0.10f, Recovery = 0.32f,
                Damage = 9f, PoiseDamage = 9f, GuardStaminaDamage = 9f, Knockback = 0.25f, Hitstop = 0.035f,
                Range = 2.7f, ArcDegrees = 70f, LungeDistance = 0.45f,
                ComboWindowStart = 0.15f, ComboWindowEnd = 0.42f, ChainCancelAt = 0.25f, DodgeCancelAt = 0f,
                StaminaCost = 9f, MomentumGain = 8f
            };
            var snap = new MoveData
            {
                DisplayName = "Rising Snap Kick", Kind = HitKind.Light, Limb = Limb.RightFoot,
                AnimationKey = AnimationKeys.SnapKick, EffectKey = EffectKeys.Burst,
                Startup = 0.14f, Active = 0.10f, Recovery = 0.32f,
                Damage = 10f, PoiseDamage = 7f, GuardStaminaDamage = 10f, Knockback = 0.3f, Hitstop = 0.045f,
                Range = 2.9f, ArcDegrees = 60f, LungeDistance = 0.5f,
                ComboWindowStart = 0.18f, ComboWindowEnd = 0.46f, ChainCancelAt = 0.28f, DodgeCancelAt = 0f,
                StaminaCost = 9f, MomentumGain = 7f
            };
            var spin = new MoveData
            {
                DisplayName = "Dragon Tail Kick", Kind = HitKind.Light, Limb = Limb.RightFoot,
                AnimationKey = AnimationKeys.SpinKick, EffectKey = EffectKeys.Trail,
                Startup = 0.18f, Active = 0.12f, Recovery = 0.40f,
                Damage = 13f, PoiseDamage = 8f, GuardStaminaDamage = 13f, Knockback = 0.6f, Hitstop = 0.055f,
                Range = 3.0f, ArcDegrees = 200f, OriginForward = 0f, LungeDistance = 0.5f,
                ComboWindowStart = 0.30f, ComboWindowEnd = 0.62f, ChainCancelAt = 0.42f, DodgeCancelAt = 0f,
                StaminaCost = 11f, MomentumGain = 8f
            };
            var palm = new MoveData
            {
                DisplayName = "Phoenix Palm", Kind = HitKind.Light, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.PhoenixPalm, EffectKey = EffectKeys.Cone,
                Startup = 0.22f, Active = 0.12f, Recovery = 0.50f,
                Damage = 18f, PoiseDamage = 11f, GuardStaminaDamage = 18f, Knockback = 1.4f, Hitstop = 0.07f,
                Range = 4.5f, ArcDegrees = 90f, LungeDistance = 0.4f,
                ComboWindowStart = 0.50f, ComboWindowEnd = 0.80f, ChainCancelAt = 0.60f, DodgeCancelAt = 0.30f,
                StaminaCost = 14f, MomentumGain = 12f                // the committed finisher earns a little more
            };
            return new[] { jab, cross, snap, spin, palm };
        }

        // The pause branch (X X, wait, X): a low spinning sweep that takes the legs, then a rising kick that throws the
        // foe up. Poise 8 + 9 + 10 + 12 = 39 with the first two chain hits, plus a jab = 47: still under a soldier's 52.
        static MoveData[] CreateFirePauseChain()
        {
            var sweep = new MoveData
            {
                DisplayName = "Sweeping Flame Kick", Kind = HitKind.Light, Limb = Limb.RightFoot,
                AnimationKey = AnimationKeys.SweepKick, EffectKey = EffectKeys.Trail,
                Startup = 0.16f, Active = 0.12f, Recovery = 0.34f,
                Damage = 11f, PoiseDamage = 10f, GuardStaminaDamage = 11f, Knockback = 0.4f, Hitstop = 0.05f,
                Range = 3.0f, ArcDegrees = 160f, OriginForward = 0f, LungeDistance = 0.6f,
                ComboWindowStart = 0.20f, ComboWindowEnd = 0.48f, ChainCancelAt = 0.30f, DodgeCancelAt = 0f,
                StaminaCost = 10f, MomentumGain = 8f
            };
            var rising = new MoveData
            {
                DisplayName = "Rising Phoenix Kick", Kind = HitKind.Light, Limb = Limb.RightFoot,
                AnimationKey = AnimationKeys.RisingPhoenixKick, EffectKey = EffectKeys.Pillar,
                Startup = 0.20f, Active = 0.12f, Recovery = 0.46f,
                Damage = 14f, PoiseDamage = 12f, GuardStaminaDamage = 14f, Knockback = 0.6f, Hitstop = 0.065f,
                Range = 2.8f, ArcDegrees = 100f, VerticalReach = 1.6f, LungeDistance = 0.4f,
                LaunchSpeed = 10f, SelfLift = 0f,
                // Combo window opens at 0.50 (the spec table's 0.52 would open after the 0.50 cancel point, which the beat
                // validation forbids: a press could chain before its window).
                ComboWindowStart = 0.50f, ComboWindowEnd = 0.80f, ChainCancelAt = 0.50f, DodgeCancelAt = 0.30f,
                StaminaCost = 12f, MomentumGain = 12f
            };
            return new[] { sweep, rising };
        }

        // Turning Heel Counter: out of a dodge, a spinning back kick that dashes back in. It takes the string's next slot.
        static MoveData CreateFireDodgeStrike()
        {
            return new MoveData
            {
                DisplayName = "Turning Heel Counter", Kind = HitKind.Light, Limb = Limb.RightFoot,
                AnimationKey = AnimationKeys.SpinBackKick, EffectKey = EffectKeys.Burst,
                Startup = 0.10f, Active = 0.10f, Recovery = 0.28f,
                Damage = 11f, PoiseDamage = 7f, GuardStaminaDamage = 11f, Knockback = 0.5f, Hitstop = 0.05f,
                Range = 2.0f, ArcDegrees = 140f, LungeDistance = 0.6f,
                ComboWindowStart = 0.12f, ComboWindowEnd = 0.38f, ChainCancelAt = 0.22f, DodgeCancelAt = 0f,
                StaminaCost = 0f, MomentumGain = 8f
            };
        }

        // Northern Shaolin keeps a steady, relentless beat: every on-beat press feeds Momentum.
        static ElementRhythm CreateFireRhythm()
        {
            return new ElementRhythm { OnBeatMomentumBonus = 3f };
        }

        // Rising Dragon Kick: a rising front kick that throws the target up and carries you after it.
        static MoveData CreateFireLauncher()
        {
            return new MoveData
            {
                DisplayName = "Rising Dragon Kick", Kind = HitKind.Special, Limb = Limb.RightFoot,
                AnimationKey = AnimationKeys.Launcher, EffectKey = EffectKeys.Pillar,
                Startup = 0.16f, Active = 0.12f, Recovery = 0.36f,
                Damage = 9f, PoiseDamage = 12f, GuardStaminaDamage = 9f, Knockback = 0.1f, Hitstop = 0.06f,
                Range = 2.6f, ArcDegrees = 90f, VerticalReach = 1.6f, LungeDistance = 0.3f,
                LaunchSpeed = 11f, SelfLift = 9.5f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.26f, DodgeCancelAt = 0.26f,
                StaminaCost = 12f, MomentumGain = 8f
            };
        }

        // The air string: jab, crescent kick, then a tornado kick that slams the target down. The first two lift you
        // both a little (AirLift / SelfLift), which is what keeps a juggle going.
        static MoveData[] CreateFireAirChain()
        {
            var airJab = new MoveData
            {
                DisplayName = "Sky Jab", Kind = HitKind.Light, Limb = Limb.LeftFist,
                AnimationKey = AnimationKeys.AirJab, EffectKey = EffectKeys.Burst,
                Startup = 0.08f, Active = 0.08f, Recovery = 0.22f,
                Damage = 6f, PoiseDamage = 5f, GuardStaminaDamage = 6f, Knockback = 0.1f, Hitstop = 0.035f,
                Range = 2.4f, ArcDegrees = 80f, VerticalReach = 1.8f, LungeDistance = 0.3f,
                AirLift = 3.2f, SelfLift = 3.2f,
                ComboWindowStart = 0.10f, ComboWindowEnd = 0.34f, ChainCancelAt = 0.16f, DodgeCancelAt = 0.16f,
                StaminaCost = 6f, MomentumGain = 6f
            };
            var crescent = new MoveData
            {
                DisplayName = "Crescent Flame Kick", Kind = HitKind.Light, Limb = Limb.RightFoot,
                AnimationKey = AnimationKeys.AirCrescent, EffectKey = EffectKeys.Trail,
                Startup = 0.12f, Active = 0.10f, Recovery = 0.26f,
                Damage = 8f, PoiseDamage = 8f, GuardStaminaDamage = 8f, Knockback = 0.15f, Hitstop = 0.045f,
                Range = 2.6f, ArcDegrees = 140f, VerticalReach = 1.8f, LungeDistance = 0.3f,
                AirLift = 3.2f, SelfLift = 3.2f,
                ComboWindowStart = 0.14f, ComboWindowEnd = 0.40f, ChainCancelAt = 0.22f, DodgeCancelAt = 0.20f,
                StaminaCost = 7f, MomentumGain = 7f
            };
            var tornado = new MoveData
            {
                DisplayName = "Tornado Slam Kick", Kind = HitKind.Light, Limb = Limb.RightFoot,
                AnimationKey = AnimationKeys.AirTornado, EffectKey = EffectKeys.Slam,
                Startup = 0.16f, Active = 0.12f, Recovery = 0.34f,
                Damage = 12f, PoiseDamage = 20f, GuardStaminaDamage = 12f, Knockback = 0.6f, Hitstop = 0.07f,
                Range = 2.8f, ArcDegrees = 200f, OriginForward = 0f, VerticalReach = 2.0f, LungeDistance = 0.2f,
                SlamSpeed = 16f, SelfLift = 1.5f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.40f, DodgeCancelAt = 0.30f,
                StaminaCost = 9f, MomentumGain = 10f
            };
            return new[] { airJab, crescent, tornado };
        }

        // Fire Whip (mid range): the arm sweeps and a long lash of flame follows it, hitting everything in a wide arc
        // out to 6.5 m. Fire whips are canon firebending.
        static MoveData CreateFireWhip()
        {
            return new MoveData
            {
                DisplayName = "Fire Whip", Kind = HitKind.Special, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.FireWhip, EffectKey = EffectKeys.Whip,
                Startup = 0.24f, Active = 0.14f, Recovery = 0.45f,
                Damage = 12f, PoiseDamage = 12f, GuardStaminaDamage = 12f, Knockback = 0.8f, Hitstop = 0.05f,
                Range = 6.5f, ArcDegrees = 110f, OriginHeight = 1.2f, VerticalReach = 1.5f, LungeDistance = 0f,
                TrackingTurnRate = 720f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.55f, DodgeCancelAt = 0.36f,
                StaminaCost = 18f, MomentumGain = 8f
            };
        }

        // Flame Wheel (close, all round): a low spinning sweep that throws a ring of fire, for when you're surrounded.
        static MoveData CreateFireWheel()
        {
            return new MoveData
            {
                DisplayName = "Flame Wheel", Kind = HitKind.Special, Limb = Limb.RightFoot,
                AnimationKey = AnimationKeys.FlameWheel, EffectKey = EffectKeys.Wheel,
                Startup = 0.20f, Active = 0.20f, Recovery = 0.45f,
                Damage = 14f, PoiseDamage = 22f, GuardStaminaDamage = 14f, Knockback = 1.6f, Hitstop = 0.06f,
                Range = 3.4f, ArcDegrees = 360f, OriginForward = 0f, VerticalReach = 1.2f, LungeDistance = 0f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.60f, DodgeCancelAt = 0.40f,
                StaminaCost = 20f, MomentumGain = 10f
            };
        }

        static MoveData CreateFireHeavy()
        {
            return new MoveData
            {
                DisplayName = "Fa Jin Palm", Kind = HitKind.Heavy, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.FaJinPalm, EffectKey = EffectKeys.Cone,
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
                AnimationKey = AnimationKeys.SprintKick, EffectKey = EffectKeys.Trail,
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
                AnimationKey = AnimationKeys.AxeKick, EffectKey = EffectKeys.Slam,
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
                AnimationKey = AnimationKeys.ZipKick, EffectKey = EffectKeys.Trail,
                Startup = 0.30f, Active = 0.12f, Recovery = 0.38f,
                Damage = 12f, PoiseDamage = 18f, GuardStaminaDamage = 12f, Knockback = 0.9f, Hitstop = 0.05f,
                Range = 2.4f, ArcDegrees = 90f, VerticalReach = 1.6f, LungeDistance = 0f, TrackingTurnRate = 1080f,
                AirLift = 3.2f,                              // zipping into a juggled enemy keeps it up
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.44f, DodgeCancelAt = 0.36f,
                StaminaCost = 14f, MomentumGain = 10f
            };
        }

        static MoveData CreateFireSkill()
        {
            return new MoveData
            {
                DisplayName = "Fire Blast", Kind = HitKind.Projectile, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.FireBlast, EffectKey = EffectKeys.Burst,
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
