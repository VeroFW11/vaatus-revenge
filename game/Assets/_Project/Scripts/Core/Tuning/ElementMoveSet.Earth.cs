namespace VaatusRevenge.Core
{
    // Earth (Hung Gar): rooted horse stance, iron-bridge arms, crushing strikes. Slow and heavy: big damage per hit, guard
    // damage that breaks blocks, hyper armour on the later string hits (rooting, never stone on the body), a real held
    // block, and the strictest beat window of the four. The forms are Hung Gar's: the horse-stance punch, the tiger claw,
    // the butterfly palms. Earth's air string is pure martial strikes with dust (canon earthbenders need rock underfoot or
    // in hand to bend), the Meteor Drop's spikes rise only on landing (Build 05 spec, 7 and 8.1).
    //
    // Fluid values; Punishing = ApplyPunishing (the same rules for every element). Numbers from the Build 05 spec, 3.3.
    // Light-chain DodgeCancelAt follows the Fluid rule every element shares (C13): hits 1-4 can be dodged out of from
    // their first frame, the finisher from 0.30.
    public partial class ElementMoveSet
    {
        public static ElementMoveSet CreateEarthFluid()
        {
            return new ElementMoveSet
            {
                DisplayName = "Earth",
                Element = ElementId.Earth,
                AnimationStyle = "earth",
                LightChain = CreateEarthLightChain(),
                PauseChain = CreateEarthPauseChain(),
                DodgeStrike = CreateEarthDodgeStrike(),
                Rhythm = CreateEarthRhythm(),
                Launcher = CreateEarthLauncher(),
                AirChain = CreateEarthAirChain(),
                // A short, heavy air game: few strikes, one dash, you fall fast.
                Aerial = new AerialSettings
                {
                    AirAttacksPerJump = 4, AirDashesPerJump = 1, AirAttackGravityScale = 0.45f, AirDashDistance = 2.6f
                },
                AbilityNorth = CreateEarthSpikeLine(),
                AbilityEast = CreateEarthStoneTent(),
                Heavy = CreateEarthHeavy(),
                // Mountain Fa Jin: the slowest charge and the biggest payoff.
                Charge = new ChargeSettings
                {
                    MaxChargeTime = 1.5f, SweetSpotStart = 0.90f, SweetSpotEnd = 1.15f, FaJinDamageMultiplier = 2f, FaJinPoiseMultiplier = 2f
                },
                SprintAttack = CreateEarthSprintAttack(),
                PlungeAttack = CreateEarthPlunge(),
                Plunge = new PlungeSettings { HangTime = 0.06f, FallSpeed = 22f, RingRadius = 2.6f },
                Skill = CreateEarthSkill(),
                ZipStrike = CreateEarthZipStrike(),
                // Earth Surf hugs the ground: it can't reach a target far above or below you.
                Zip = new ZipStrikeSettings { Range = 12f, AngleDegrees = 45f, MaxHeightDifference = 1.0f },
                Dodge = CreateEarthDodge(),
                // The only element that holds a block: a narrow, slow-moving guard that breaks hard, and the tightest parry.
                Guard = new GuardSettings
                {
                    Style = DefenseStyle.BlockAndParry, ArcDegrees = 180f, MoveSpeedMultiplier = 0.35f, GuardBreakStagger = 1.2f,
                    DeflectWindow = 0.12f, DeflectWhiffLockout = 0.40f
                },
                Momentum = new MomentumSettings { Enabled = false },   // only Fire has an identity meter in this build
                DataVersion = CurrentDataVersion
            };
        }

        public static ElementMoveSet CreateEarthPunishing()
        {
            ElementMoveSet set = CreateEarthFluid();
            ApplyPunishing(set);
            return set;
        }

        // From the horse stance: a straight punch, a tiger-claw rake, a stomp that cracks a line along the ground, the
        // butterfly palms (both palms together) and Mountain Quake, both fists driven into the earth all round. The later
        // hits are rooted (hyper armour): Earth trades blows. Poise 7 + 8 + 9 + 9 + 11 = 44, plus hit 1 = 51: still under
        // a soldier's 52.
        static MoveData[] CreateEarthLightChain()
        {
            var stoneFist = new MoveData
            {
                DisplayName = "Horse Stance Punch", Kind = HitKind.Light, Limb = Limb.LeftFist,
                AnimationKey = AnimationKeys.HorsePunch, EffectKey = EffectKeys.Burst,
                Startup = 0.18f, Active = 0.10f, Recovery = 0.36f,
                Damage = 11f, PoiseDamage = 7f, GuardStaminaDamage = 16f, Knockback = 0.3f, Hitstop = 0.05f,
                Range = 2.6f, ArcDegrees = 70f, LungeDistance = 0.25f,
                ComboWindowStart = 0.22f, ComboWindowEnd = 0.54f, ChainCancelAt = 0.30f, DodgeCancelAt = 0f,
                StaminaCost = 11f, MomentumGain = 0f
            };
            var tigerClaw = new MoveData
            {
                DisplayName = "Tiger Claw Rake", Kind = HitKind.Light, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.TigerClaw, EffectKey = EffectKeys.Trail,   // a raking swipe, not a straight line (J3-S10)
                Startup = 0.18f, Active = 0.12f, Recovery = 0.38f,
                Damage = 12f, PoiseDamage = 8f, GuardStaminaDamage = 18f, Knockback = 0.35f, Hitstop = 0.05f,
                Range = 2.8f, ArcDegrees = 100f, LungeDistance = 0.25f,
                ComboWindowStart = 0.24f, ComboWindowEnd = 0.58f, ChainCancelAt = 0.32f, DodgeCancelAt = 0f,
                StaminaCost = 11f, MomentumGain = 0f
            };
            var stomp = new MoveData
            {
                DisplayName = "Rooted Stomp", Kind = HitKind.Light, Limb = Limb.RightFoot,
                AnimationKey = AnimationKeys.StompLine, EffectKey = EffectKeys.Line,
                Startup = 0.22f, Active = 0.12f, Recovery = 0.40f,
                Damage = 13f, PoiseDamage = 9f, GuardStaminaDamage = 18f, Knockback = 0.4f, Hitstop = 0.055f,
                Range = 4.5f, ArcDegrees = 25f, LungeDistance = 0f,
                HyperArmor = true, HyperArmorFrom = 0.10f,
                ComboWindowStart = 0.28f, ComboWindowEnd = 0.62f, ChainCancelAt = 0.36f, DodgeCancelAt = 0f,
                StaminaCost = 12f, MomentumGain = 0f
            };
            var butterfly = new MoveData
            {
                DisplayName = "Butterfly Palms", Kind = HitKind.Light, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.ButterflyPalms, EffectKey = EffectKeys.Burst,
                Startup = 0.24f, Active = 0.12f, Recovery = 0.44f,
                Damage = 15f, PoiseDamage = 9f, GuardStaminaDamage = 22f, Knockback = 1.0f, Hitstop = 0.06f,
                Range = 3.4f, ArcDegrees = 70f, LungeDistance = 0.3f,
                HyperArmor = true, HyperArmorFrom = 0.08f,
                ComboWindowStart = 0.36f, ComboWindowEnd = 0.72f, ChainCancelAt = 0.46f, DodgeCancelAt = 0f,
                StaminaCost = 13f, MomentumGain = 0f
            };
            var quake = new MoveData
            {
                DisplayName = "Mountain Quake", Kind = HitKind.Light, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.QuakeSlam, EffectKey = EffectKeys.Stomp,
                Startup = 0.30f, Active = 0.14f, Recovery = 0.56f,
                Damage = 21f, PoiseDamage = 11f, GuardStaminaDamage = 30f, Knockback = 1.6f, Hitstop = 0.08f,
                Range = 3.6f, ArcDegrees = 360f, OriginForward = 0f, VerticalReach = 1.2f, LungeDistance = 0f,
                HyperArmor = true, HyperArmorFrom = 0f,
                ComboWindowStart = 0.58f, ComboWindowEnd = 0.94f, ChainCancelAt = 0.72f, DodgeCancelAt = 0.30f,
                StaminaCost = 16f, MomentumGain = 0f
            };
            return new[] { stoneFist, tigerClaw, stomp, butterfly, quake };
        }

        // The pause branch (X X, wait, X): both arms heave a boulder up out of the ground (it pops the foe up a little), then
        // hurl it. Poise 7 + 8 + 10 + 14 = 39, plus hit 1 = 46.
        static MoveData[] CreateEarthPauseChain()
        {
            var raise = new MoveData
            {
                DisplayName = "Raise the Boulder", Kind = HitKind.Light, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.BoulderRaise, EffectKey = EffectKeys.Pillar,
                Startup = 0.24f, Active = 0.12f, Recovery = 0.30f,
                Damage = 10f, PoiseDamage = 10f, GuardStaminaDamage = 10f, Knockback = 0.2f, Hitstop = 0.06f,
                Range = 2.6f, ArcDegrees = 120f, VerticalReach = 1.6f, LungeDistance = 0f,
                LaunchSpeed = 6f, HyperArmor = true, HyperArmorFrom = 0.10f,
                ComboWindowStart = 0.36f, ComboWindowEnd = 0.66f, ChainCancelAt = 0.40f, DodgeCancelAt = 0.28f,
                StaminaCost = 12f, MomentumGain = 0f
            };
            var hurl = new MoveData
            {
                DisplayName = "Boulder Hurl", Kind = HitKind.Light, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.BoulderHurl, EffectKey = EffectKeys.Burst,
                Startup = 0.26f, Active = 0f, Recovery = 0.48f,
                Damage = 20f, PoiseDamage = 14f, GuardStaminaDamage = 26f, Knockback = 1.8f, Hitstop = 0.07f,
                Range = 0f, ArcDegrees = 0f, OriginHeight = 1.4f, OriginForward = 0.6f, LungeDistance = 0f,
                HyperArmor = true, HyperArmorFrom = 0f,
                ComboWindowStart = 0.52f, ComboWindowEnd = 0.80f, ChainCancelAt = 0.56f, DodgeCancelAt = 0.36f,
                StaminaCost = 14f, MomentumGain = 0f,
                LaunchesProjectile = true,
                Projectile = new ProjectileSpec { Speed = 24f, Radius = 0.6f, MaxRange = 18f, Gravity = 6f, ExplosionRadius = 2.5f, VisualScale = 1.8f }
            };
            return new[] { raise, hurl };
        }

        // Pivot Elbow: out of a dodge, pivot on the planted foot and swing the elbow round, the forearm snapping out after
        // it. Rooted from its first frame.
        static MoveData CreateEarthDodgeStrike()
        {
            return new MoveData
            {
                DisplayName = "Pivot Elbow", Kind = HitKind.Light, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.PivotElbow, EffectKey = EffectKeys.Burst,
                Startup = 0.12f, Active = 0.10f, Recovery = 0.34f,
                Damage = 14f, PoiseDamage = 7f, GuardStaminaDamage = 18f, Knockback = 0.6f, Hitstop = 0.055f,
                Range = 2.0f, ArcDegrees = 120f, LungeDistance = 0.5f,
                HyperArmor = true, HyperArmorFrom = 0f,
                ComboWindowStart = 0.14f, ComboWindowEnd = 0.44f, ChainCancelAt = 0.26f, DodgeCancelAt = 0f,
                StaminaCost = 0f, MomentumGain = 0f
            };
        }

        // Hung Gar is strict and rooted: a tighter beat window, and an on-beat press hits harder and can't be knocked out of.
        static ElementRhythm CreateEarthRhythm()
        {
            return new ElementRhythm
            {
                BeatEarlyDelta = -0.02f, BeatLateDelta = -0.03f, OnBeatPlaybackRate = 1.10f, OnBeatDamageBonus = 0.10f, OnBeatHyperArmor = true
            };
        }

        // Rising Pillar: a rooted uppercut as a column of rock throws the foe up, and you ride it after them.
        static MoveData CreateEarthLauncher()
        {
            return new MoveData
            {
                DisplayName = "Rising Pillar", Kind = HitKind.Special, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.PillarUppercut, EffectKey = EffectKeys.Pillar,
                Startup = 0.20f, Active = 0.12f, Recovery = 0.40f,
                Damage = 10f, PoiseDamage = 14f, GuardStaminaDamage = 10f, Knockback = 0.1f, Hitstop = 0.065f,
                Range = 2.8f, ArcDegrees = 90f, VerticalReach = 1.6f, LungeDistance = 0.2f,
                LaunchSpeed = 11f, SelfLift = 9.5f, HyperArmor = true, HyperArmorFrom = 0.08f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.32f, DodgeCancelAt = 0.32f,
                StaminaCost = 13f, MomentumGain = 0f
            };
        }

        // The air string, pure Hung Gar with dust only: a hammer fist, a tiger-tail back kick, then Meteor Drop, both fists
        // driving the foe into the floor (its spikes rise only when it lands). Canon (spec 8.1): no rock in the air. The
        // EffectKeys below pick the shape; ElementMoveEffects draws any Earth strike made in the air as dust only.
        static MoveData[] CreateEarthAirChain()
        {
            var hammer = new MoveData
            {
                DisplayName = "Hammer Fist", Kind = HitKind.Light, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.AirHammer, EffectKey = EffectKeys.Burst,
                Startup = 0.10f, Active = 0.10f, Recovery = 0.26f,
                Damage = 8f, PoiseDamage = 7f, GuardStaminaDamage = 8f, Knockback = 0.15f, Hitstop = 0.045f,
                Range = 2.4f, ArcDegrees = 80f, VerticalReach = 1.8f, LungeDistance = 0.3f,
                AirLift = 3.0f, SelfLift = 3.0f,
                ComboWindowStart = 0.14f, ComboWindowEnd = 0.40f, ChainCancelAt = 0.20f, DodgeCancelAt = 0.18f,
                StaminaCost = 7f, MomentumGain = 0f
            };
            var tigerTail = new MoveData
            {
                DisplayName = "Tiger Tail Kick", Kind = HitKind.Light, Limb = Limb.RightFoot,
                AnimationKey = AnimationKeys.AirBackKick, EffectKey = EffectKeys.Trail,
                Startup = 0.14f, Active = 0.10f, Recovery = 0.30f,
                Damage = 10f, PoiseDamage = 9f, GuardStaminaDamage = 10f, Knockback = 0.2f, Hitstop = 0.05f,
                Range = 2.6f, ArcDegrees = 120f, VerticalReach = 1.8f, LungeDistance = 0.3f,
                AirLift = 3.0f, SelfLift = 3.0f,
                ComboWindowStart = 0.18f, ComboWindowEnd = 0.46f, ChainCancelAt = 0.26f, DodgeCancelAt = 0.22f,
                StaminaCost = 8f, MomentumGain = 0f
            };
            var meteor = new MoveData
            {
                DisplayName = "Meteor Drop", Kind = HitKind.Light, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.AirMeteor, EffectKey = EffectKeys.Slam,
                Startup = 0.18f, Active = 0.12f, Recovery = 0.38f,
                Damage = 15f, PoiseDamage = 22f, GuardStaminaDamage = 15f, Knockback = 0.6f, Hitstop = 0.08f,
                Range = 2.8f, ArcDegrees = 160f, VerticalReach = 2.2f, LungeDistance = 0.2f,
                SlamSpeed = 20f, SelfLift = 0f,   // J3-S05: a slam does not lift you over the foe
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.44f, DodgeCancelAt = 0.32f,
                StaminaCost = 10f, MomentumGain = 0f
            };
            return new[] { hammer, tigerTail, meteor };
        }

        // Stone Spike Line (mid range): a stamp and a driving punch send a line of spikes along the ground that pop the foe up.
        static MoveData CreateEarthSpikeLine()
        {
            return new MoveData
            {
                DisplayName = "Stone Spike Line", Kind = HitKind.Special, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.SpikeLine, EffectKey = EffectKeys.Line,
                Startup = 0.26f, Active = 0.20f, Recovery = 0.44f,
                Damage = 14f, PoiseDamage = 16f, GuardStaminaDamage = 18f, Knockback = 0.4f, Hitstop = 0.06f,
                Range = 8.0f, ArcDegrees = 18f, LungeDistance = 0f,
                LaunchSpeed = 7f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.62f, DodgeCancelAt = 0.40f,
                StaminaCost = 18f, MomentumGain = 0f
            };
        }

        // Stone Tent (close, all round): both arms heave up and a tent of rock bursts out round you, throwing foes back.
        // Rooted throughout (the shell that guards you is a later build).
        static MoveData CreateEarthStoneTent()
        {
            return new MoveData
            {
                DisplayName = "Stone Tent", Kind = HitKind.Special, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.StoneTent, EffectKey = EffectKeys.Dome,
                Startup = 0.30f, Active = 0.12f, Recovery = 0.36f,
                Damage = 14f, PoiseDamage = 20f, GuardStaminaDamage = 14f, Knockback = 2.0f, Hitstop = 0.07f,
                Range = 3.0f, ArcDegrees = 360f, OriginForward = 0f, VerticalReach = 1.2f, LungeDistance = 0f,
                HyperArmor = true, HyperArmorFrom = 0f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.50f, DodgeCancelAt = 0.20f,
                StaminaCost = 20f, MomentumGain = 0f
            };
        }

        // Mountain Fa Jin (the charged heavy): sink deep into the horse, then one rooted punch sends a shock along the ground.
        static MoveData CreateEarthHeavy()
        {
            return new MoveData
            {
                DisplayName = "Mountain Fa Jin", Kind = HitKind.Heavy, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.RootFaJin, EffectKey = EffectKeys.Line,
                Startup = 0.30f, Active = 0.16f, Recovery = 0.60f,
                Damage = 24f, PoiseDamage = 34f, GuardStaminaDamage = 40f, Knockback = 1.8f, Hitstop = 0.09f,
                Range = 6.0f, ArcDegrees = 30f, LungeDistance = 0.2f, TrackingTurnRate = 720f,
                HyperArmor = true,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 1.0f, DodgeCancelAt = 0.44f,
                StaminaCost = 24f, MomentumGain = 0f
            };
        }

        // Avalanche Shoulder (sprint attack): a lowered shoulder and a driving lead palm, rooted, that bowls the foe over.
        static MoveData CreateEarthSprintAttack()
        {
            return new MoveData
            {
                DisplayName = "Avalanche Shoulder", Kind = HitKind.Sprint, Limb = Limb.LeftFist,
                AnimationKey = AnimationKeys.ShoulderCharge, EffectKey = EffectKeys.Burst,
                Startup = 0.20f, Active = 0.14f, Recovery = 0.44f,
                Damage = 18f, PoiseDamage = 24f, GuardStaminaDamage = 18f, Knockback = 1.2f, Hitstop = 0.07f,
                Range = 2.8f, ArcDegrees = 90f, LungeDistance = 3.0f,
                HyperArmor = true,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.54f, DodgeCancelAt = 0.34f,
                StaminaCost = 16f, MomentumGain = 0f
            };
        }

        // Earthquake Drop (plunge): drop like a stone, rooted, and land in a ground-shaking stamp.
        static MoveData CreateEarthPlunge()
        {
            return new MoveData
            {
                DisplayName = "Earthquake Drop", Kind = HitKind.Plunge, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.QuakeDrop, EffectKey = EffectKeys.Stomp,
                Startup = 0f, Active = 0f, Recovery = 0.60f,
                Damage = 22f, PoiseDamage = 30f, GuardStaminaDamage = 22f, Knockback = 1.2f, Hitstop = 0.08f,
                Range = 0f, ArcDegrees = 360f, LungeDistance = 0f,
                HyperArmor = true,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.60f, DodgeCancelAt = 0.40f,
                StaminaCost = 18f, MomentumGain = 0f
            };
        }

        // Boulder Toss (ranged): both palms heave a boulder up out of the ground and push it in an arc that bursts on impact.
        static MoveData CreateEarthSkill()
        {
            return new MoveData
            {
                DisplayName = "Boulder Toss", Kind = HitKind.Projectile, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.BoulderToss, EffectKey = EffectKeys.Burst,
                Startup = 0.30f, Active = 0f, Recovery = 0.36f,
                Damage = 16f, PoiseDamage = 18f, GuardStaminaDamage = 20f, Knockback = 0.8f, Hitstop = 0.05f,
                Range = 0f, ArcDegrees = 0f, OriginHeight = 1.4f, OriginForward = 0.6f, LungeDistance = 0f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.46f, DodgeCancelAt = 0.34f,
                StaminaCost = 22f, MomentumGain = 0f,
                LaunchesProjectile = true,
                Projectile = new ProjectileSpec { Speed = 26f, Radius = 0.5f, MaxRange = 22f, Gravity = 4f, ExplosionRadius = 1.8f, VisualScale = 1.6f }
            };
        }

        // Earth Surf Charge (zip): ride a surging wave of earth across the ground into a rooted punch.
        static MoveData CreateEarthZipStrike()
        {
            return new MoveData
            {
                DisplayName = "Earth Surf Charge", Kind = HitKind.Sprint, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.EarthSurf, EffectKey = EffectKeys.Wave,
                Startup = 0.32f, Active = 0.12f, Recovery = 0.40f,
                Damage = 14f, PoiseDamage = 22f, GuardStaminaDamage = 14f, Knockback = 1.0f, Hitstop = 0.06f,
                Range = 2.4f, ArcDegrees = 100f, VerticalReach = 1.2f, LungeDistance = 0f, TrackingTurnRate = 1080f,
                AirLift = 0f, HyperArmor = true, HyperArmorFrom = 0f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.48f, DodgeCancelAt = 0.40f,
                StaminaCost = 15f, MomentumGain = 0f
            };
        }

        // Stone Slide: short and sharp, an explosive start that stops dead, two in a row at most, the tightest perfect-dodge
        // window and the biggest counter.
        static DodgeProfile CreateEarthDodge()
        {
            return new DodgeProfile
            {
                DisplayName = "Stone Slide",
                EvadeOutDistance = 3.0f, SideSlipDistance = 2.2f, SlipInMaxDistance = 2.4f, BackstepDistance = 1.8f,
                Duration = 0.22f, DashEaseOut = 0.80f,
                IFrameStart = 0f, IFrameEnd = 0.14f,
                EvadeAttackCancelAt = 0.06f, SlipInAttackCancelAt = 0.05f, NextDodgeAt = 0.20f,
                ChainMax = 2,
                PerfectWindow = 0.10f, CounterWindow = 0.8f, CounterDamageMultiplier = 1.8f
            };
        }
    }
}
