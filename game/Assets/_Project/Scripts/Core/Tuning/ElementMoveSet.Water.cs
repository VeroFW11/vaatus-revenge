namespace VaatusRevenge.Core
{
    // Water (Tai Chi): yield, then return the force in a circle. The string is slower and longer-reaching than Fire's,
    // its timing is the most forgiving (a later beat window), it draws enemies in (PullDistance), Water restores you on
    // the finishers (HealOnHit), and it parries with the widest window of the four. The postures are Yang-style Tai Chi:
    // ward off, roll back, press, push, single whip (the "grasp the bird's tail" sequence), cloud hands and part the wild
    // horse's mane. Names live only in DisplayName (data); "redirect" is never used (lightning redirection is far later).
    //
    // Fluid values; Punishing = ApplyPunishing (the same rules for every element). Numbers from the Build 05 spec, 3.2.
    // Light-chain DodgeCancelAt follows the Fluid rule every element shares (C13): hits 1-4 can be dodged out of from
    // their first frame, the finisher from 0.30.
    public partial class ElementMoveSet
    {
        public static ElementMoveSet CreateWaterFluid()
        {
            return new ElementMoveSet
            {
                DisplayName = "Water",
                Element = ElementId.Water,
                AnimationStyle = "water",
                LightChain = CreateWaterLightChain(),
                PauseChain = CreateWaterPauseChain(),
                DodgeStrike = CreateWaterDodgeStrike(),
                Rhythm = CreateWaterRhythm(),
                Launcher = CreateWaterLauncher(),
                AirChain = CreateWaterAirChain(),
                Aerial = new AerialSettings
                {
                    AirAttacksPerJump = 6, AirDashesPerJump = 2, AirAttackGravityScale = 0.28f, AirDashDistance = 3.6f
                },
                AbilityNorth = CreateWaterWhip(),
                AbilityEast = CreateWaterTideRing(),
                Heavy = CreateWaterHeavy(),
                // Ocean Palm: a longer charge with a wider, later sweet spot (Tai Chi's patient, rooted release).
                Charge = new ChargeSettings
                {
                    MaxChargeTime = 1.4f, SweetSpotStart = 0.70f, SweetSpotEnd = 1.10f, FaJinDamageMultiplier = 1.6f, FaJinPoiseMultiplier = 2f
                },
                SprintAttack = CreateWaterSprintAttack(),
                PlungeAttack = CreateWaterPlunge(),
                Plunge = new PlungeSettings { HangTime = 0.10f, FallSpeed = 16f, RingRadius = 2.8f },
                Skill = CreateWaterSkill(),
                ZipStrike = CreateWaterZipStrike(),
                Zip = new ZipStrikeSettings { Range = 16f, AngleDegrees = 50f, MaxHeightDifference = 3f },
                Dodge = CreateWaterDodge(),
                // The widest parry of the four: Tai Chi meets force softly and early.
                Guard = new GuardSettings { Style = DefenseStyle.ParryOnly, DeflectWindow = 0.20f, DeflectWhiffLockout = 0.30f, ParryArcDegrees = 360f },
                Momentum = new MomentumSettings { Enabled = false },   // only Fire has an identity meter in this build
                DataVersion = CurrentDataVersion
            };
        }

        public static ElementMoveSet CreateWaterPunishing()
        {
            ElementMoveSet set = CreateWaterFluid();
            ApplyPunishing(set);
            return set;
        }

        // Grasp the bird's tail, then single whip: the lead arm wards off, both hands roll back (drawing the foe in), the
        // lead forearm presses, both palms push, then the hooked rear hand and the long lead palm open out (single whip),
        // a long lash of water that restores you. Poise 6 + 6 + 8 + 9 + 10 = 39, plus hit 1 = 45: under a soldier's 52.
        static MoveData[] CreateWaterLightChain()
        {
            var wardOff = new MoveData
            {
                DisplayName = "Ward Off", Kind = HitKind.Light, Limb = Limb.LeftFist,
                AnimationKey = AnimationKeys.PalmWard, EffectKey = EffectKeys.Burst,
                Startup = 0.14f, Active = 0.14f, Recovery = 0.30f,
                Damage = 7f, PoiseDamage = 6f, GuardStaminaDamage = 7f, Knockback = 0.3f, Hitstop = 0.035f,
                Range = 3.0f, ArcDegrees = 110f, LungeDistance = 0.35f,
                ComboWindowStart = 0.18f, ComboWindowEnd = 0.52f, ChainCancelAt = 0.30f, DodgeCancelAt = 0f,
                StaminaCost = 8f, MomentumGain = 0f
            };
            var rollBack = new MoveData
            {
                DisplayName = "Roll Back", Kind = HitKind.Light, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.PalmRollback, EffectKey = EffectKeys.Whip,
                Startup = 0.14f, Active = 0.14f, Recovery = 0.32f,
                Damage = 7f, PoiseDamage = 6f, GuardStaminaDamage = 7f, Knockback = 0f, Hitstop = 0.035f,
                Range = 3.6f, ArcDegrees = 90f, LungeDistance = 0f,
                PullDistance = 0.8f,                         // yield and draw the foe in
                ComboWindowStart = 0.18f, ComboWindowEnd = 0.54f, ChainCancelAt = 0.30f, DodgeCancelAt = 0f,
                StaminaCost = 8f, MomentumGain = 0f
            };
            var press = new MoveData
            {
                DisplayName = "Press", Kind = HitKind.Light, Limb = Limb.LeftFist,
                AnimationKey = AnimationKeys.ForearmPress, EffectKey = EffectKeys.Wave,
                Startup = 0.16f, Active = 0.12f, Recovery = 0.34f,
                Damage = 9f, PoiseDamage = 8f, GuardStaminaDamage = 9f, Knockback = 0.5f, Hitstop = 0.045f,
                Range = 3.4f, ArcDegrees = 80f, LungeDistance = 0.45f,
                ComboWindowStart = 0.20f, ComboWindowEnd = 0.56f, ChainCancelAt = 0.32f, DodgeCancelAt = 0f,
                StaminaCost = 9f, MomentumGain = 0f
            };
            var push = new MoveData
            {
                DisplayName = "Push", Kind = HitKind.Light, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.TwoPalmPush, EffectKey = EffectKeys.Wave,
                Startup = 0.18f, Active = 0.14f, Recovery = 0.38f,
                Damage = 11f, PoiseDamage = 9f, GuardStaminaDamage = 12f, Knockback = 0.8f, Hitstop = 0.05f,
                Range = 3.8f, ArcDegrees = 100f, LungeDistance = 0.4f,
                ComboWindowStart = 0.30f, ComboWindowEnd = 0.66f, ChainCancelAt = 0.40f, DodgeCancelAt = 0f,
                StaminaCost = 10f, MomentumGain = 0f
            };
            var singleWhip = new MoveData
            {
                DisplayName = "Single Whip", Kind = HitKind.Light, Limb = Limb.LeftFist,
                AnimationKey = AnimationKeys.SingleWhip, EffectKey = EffectKeys.Whip,
                Startup = 0.22f, Active = 0.18f, Recovery = 0.46f,
                Damage = 17f, PoiseDamage = 10f, GuardStaminaDamage = 17f, Knockback = 1.2f, Hitstop = 0.065f,
                Range = 5.5f, ArcDegrees = 150f, OriginHeight = 1.2f, VerticalReach = 1.4f, LungeDistance = 0.2f,
                HealOnHit = 2f,
                ComboWindowStart = 0.50f, ComboWindowEnd = 0.84f, ChainCancelAt = 0.62f, DodgeCancelAt = 0.30f,
                StaminaCost = 13f, MomentumGain = 0f
            };
            return new[] { wardOff, rollBack, press, push, singleWhip };
        }

        // The pause branch (X X, wait, X): cloud hands (the arms circle, four soft hits all round that keep drawing the foe
        // in), then part the wild horse's mane, a long diagonal sweep that throws it back and restores you.
        // Poise 6 + 6 + 2x4 + 14 = 34, plus hit 1 = 40.
        static MoveData[] CreateWaterPauseChain()
        {
            var cloudHands = new MoveData
            {
                DisplayName = "Cloud Hands", Kind = HitKind.Light, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.CloudHands, EffectKey = EffectKeys.Vortex,
                Startup = 0.16f, Active = 0.48f, Recovery = 0.30f,
                Damage = 3f, PoiseDamage = 2f, GuardStaminaDamage = 3f, Knockback = 0.1f, Hitstop = 0.025f,
                Range = 3.6f, ArcDegrees = 360f, OriginForward = 0f, LungeDistance = 0f,
                HitCount = 4, HitInterval = 0.12f, PullDistance = 0.3f,
                ComboWindowStart = 0.56f, ComboWindowEnd = 0.90f, ChainCancelAt = 0.66f, DodgeCancelAt = 0.24f,
                StaminaCost = 12f, MomentumGain = 0f
            };
            var splitMane = new MoveData
            {
                DisplayName = "Part the Wild Horse's Mane", Kind = HitKind.Light, Limb = Limb.LeftFist,
                AnimationKey = AnimationKeys.SplitMane, EffectKey = EffectKeys.Wave,
                Startup = 0.20f, Active = 0.14f, Recovery = 0.44f,
                Damage = 15f, PoiseDamage = 14f, GuardStaminaDamage = 15f, Knockback = 2.0f, Hitstop = 0.065f,
                Range = 4.5f, ArcDegrees = 180f, LungeDistance = 0.3f,
                HealOnHit = 2f,
                ComboWindowStart = 0.52f, ComboWindowEnd = 0.80f, ChainCancelAt = 0.60f, DodgeCancelAt = 0.30f,
                StaminaCost = 12f, MomentumGain = 0f
            };
            return new[] { cloudHands, splitMane };
        }

        // Return the Tide: out of a dodge, the body turns back in and both hands send the wave home.
        static MoveData CreateWaterDodgeStrike()
        {
            return new MoveData
            {
                DisplayName = "Return the Tide", Kind = HitKind.Light, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.ReturnTide, EffectKey = EffectKeys.Wave,
                Startup = 0.10f, Active = 0.12f, Recovery = 0.30f,
                Damage = 12f, PoiseDamage = 6f, GuardStaminaDamage = 12f, Knockback = 0.6f, Hitstop = 0.05f,
                Range = 2.0f, ArcDegrees = 120f, LungeDistance = 0.6f,
                HealOnHit = 2f,
                ComboWindowStart = 0.12f, ComboWindowEnd = 0.40f, ChainCancelAt = 0.24f, DodgeCancelAt = 0f,
                StaminaCost = 0f, MomentumGain = 0f
            };
        }

        // Tai Chi is legato and forgiving: a later beat window, a gentler speed-up, and each on-beat press gives stamina back.
        static ElementRhythm CreateWaterRhythm()
        {
            return new ElementRhythm { BeatLateDelta = 0.04f, OnBeatPlaybackRate = 1.10f, OnBeatStaminaRefund = 3f };
        }

        // White Crane Spreads Wings: the rear hand sweeps up past the face as a column of water lifts the foe, and you rise
        // after it.
        static MoveData CreateWaterLauncher()
        {
            return new MoveData
            {
                DisplayName = "White Crane Spreads Wings", Kind = HitKind.Special, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.CraneRise, EffectKey = EffectKeys.Pillar,
                Startup = 0.18f, Active = 0.12f, Recovery = 0.36f,
                Damage = 8f, PoiseDamage = 12f, GuardStaminaDamage = 8f, Knockback = 0.1f, Hitstop = 0.06f,
                Range = 3.2f, ArcDegrees = 90f, VerticalReach = 1.6f, LungeDistance = 0.3f,
                LaunchSpeed = 11f, SelfLift = 9.5f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.30f, DodgeCancelAt = 0.30f,
                StaminaCost = 12f, MomentumGain = 0f
            };
        }

        // The air string: brush knee palm, fair lady works the shuttles (a turning upward block-and-palm), then needle at
        // sea bottom, a downward stab that drives the foe into the floor and restores you.
        static MoveData[] CreateWaterAirChain()
        {
            var brushKnee = new MoveData
            {
                DisplayName = "Brush Knee Palm", Kind = HitKind.Light, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.AirBrushPalm, EffectKey = EffectKeys.Trail,
                Startup = 0.09f, Active = 0.10f, Recovery = 0.22f,
                Damage = 6f, PoiseDamage = 5f, GuardStaminaDamage = 6f, Knockback = 0.1f, Hitstop = 0.035f,
                Range = 2.8f, ArcDegrees = 100f, VerticalReach = 1.8f, LungeDistance = 0.3f,
                AirLift = 3.2f, SelfLift = 3.2f,
                ComboWindowStart = 0.12f, ComboWindowEnd = 0.38f, ChainCancelAt = 0.19f, DodgeCancelAt = 0.16f,
                StaminaCost = 6f, MomentumGain = 0f
            };
            var shuttles = new MoveData
            {
                DisplayName = "Fair Lady Works the Shuttles", Kind = HitKind.Light, Limb = Limb.LeftFist,
                AnimationKey = AnimationKeys.AirShuttle, EffectKey = EffectKeys.Vortex,
                Startup = 0.12f, Active = 0.14f, Recovery = 0.26f,
                Damage = 7f, PoiseDamage = 7f, GuardStaminaDamage = 7f, Knockback = 0.15f, Hitstop = 0.045f,
                Range = 3.0f, ArcDegrees = 160f, VerticalReach = 1.8f, LungeDistance = 0.3f,
                AirLift = 3.4f, SelfLift = 3.4f,
                ComboWindowStart = 0.16f, ComboWindowEnd = 0.44f, ChainCancelAt = 0.26f, DodgeCancelAt = 0.20f,
                StaminaCost = 7f, MomentumGain = 0f
            };
            var needle = new MoveData
            {
                DisplayName = "Needle at Sea Bottom", Kind = HitKind.Light, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.AirNeedle, EffectKey = EffectKeys.Slam,
                Startup = 0.16f, Active = 0.12f, Recovery = 0.34f,
                Damage = 11f, PoiseDamage = 18f, GuardStaminaDamage = 11f, Knockback = 0.5f, Hitstop = 0.07f,
                Range = 2.8f, ArcDegrees = 120f, VerticalReach = 2.2f, LungeDistance = 0.2f,
                SlamSpeed = 16f, SelfLift = 0f, HealOnHit = 2f,   // J3-S05: a slam does not lift you over the foe
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.40f, DodgeCancelAt = 0.30f,
                StaminaCost = 9f, MomentumGain = 0f
            };
            return new[] { brushKnee, shuttles, needle };
        }

        // Water Whip (mid range): a long, narrow lash that wraps the foe and hauls it right in (PullDistance 5).
        static MoveData CreateWaterWhip()
        {
            return new MoveData
            {
                DisplayName = "Water Whip", Kind = HitKind.Special, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.WaterLash, EffectKey = EffectKeys.Whip,
                Startup = 0.22f, Active = 0.14f, Recovery = 0.40f,
                Damage = 9f, PoiseDamage = 10f, GuardStaminaDamage = 9f, Knockback = 0f, Hitstop = 0.05f,
                Range = 7.5f, ArcDegrees = 40f, OriginHeight = 1.2f, VerticalReach = 1.5f, LungeDistance = 0f,
                TrackingTurnRate = 900f, PullDistance = 5f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.42f, DodgeCancelAt = 0.34f,
                StaminaCost = 14f, MomentumGain = 0f
            };
        }

        // Tide Ring (close, all round): both arms circle and a ring of water sweeps round you twice, restoring you as it hits.
        static MoveData CreateWaterTideRing()
        {
            return new MoveData
            {
                DisplayName = "Tide Ring", Kind = HitKind.Special, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.TideRing, EffectKey = EffectKeys.Vortex,
                Startup = 0.20f, Active = 0.24f, Recovery = 0.42f,
                Damage = 7f, PoiseDamage = 10f, GuardStaminaDamage = 7f, Knockback = 1.4f, Hitstop = 0.05f,
                Range = 3.6f, ArcDegrees = 360f, OriginForward = 0f, VerticalReach = 1.2f, LungeDistance = 0f,
                HitCount = 2, HitInterval = 0.12f, HealOnHit = 4f, HealPerMoveMax = 8f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.60f, DodgeCancelAt = 0.36f,
                StaminaCost = 20f, MomentumGain = 0f
            };
        }

        // Ocean Palm (the charged heavy): sink back onto the rear leg, gather, then one long palm that sends a heavy wave.
        static MoveData CreateWaterHeavy()
        {
            return new MoveData
            {
                DisplayName = "Ocean Palm", Kind = HitKind.Heavy, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.TidePalm, EffectKey = EffectKeys.Wave,
                Startup = 0.30f, Active = 0.16f, Recovery = 0.50f,
                Damage = 18f, PoiseDamage = 26f, GuardStaminaDamage = 22f, Knockback = 2.2f, Hitstop = 0.08f,
                Range = 4.5f, ArcDegrees = 100f, LungeDistance = 0.3f, TrackingTurnRate = 720f,
                HealOnHit = 4f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.90f, DodgeCancelAt = 0.38f,
                StaminaCost = 20f, MomentumGain = 0f
            };
        }

        static MoveData CreateWaterSprintAttack()
        {
            return new MoveData
            {
                DisplayName = "Tidal Rush Palm", Kind = HitKind.Sprint, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.RushPalm, EffectKey = EffectKeys.Wave,
                Startup = 0.20f, Active = 0.14f, Recovery = 0.40f,
                Damage = 14f, PoiseDamage = 18f, GuardStaminaDamage = 14f, Knockback = 1.0f, Hitstop = 0.06f,
                Range = 3.0f, ArcDegrees = 100f, LungeDistance = 3.4f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.50f, DodgeCancelAt = 0.32f,
                StaminaCost = 14f, MomentumGain = 0f
            };
        }

        // Snake Creeps Down (plunge): drop into the low snake posture and land in a spreading wave.
        static MoveData CreateWaterPlunge()
        {
            return new MoveData
            {
                DisplayName = "Snake Creeps Down", Kind = HitKind.Plunge, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.SnakeDrop, EffectKey = EffectKeys.Wave,
                Startup = 0f, Active = 0f, Recovery = 0.50f,
                Damage = 15f, PoiseDamage = 22f, GuardStaminaDamage = 15f, Knockback = 1.4f, Hitstop = 0.07f,
                Range = 0f, ArcDegrees = 360f, LungeDistance = 0f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.50f, DodgeCancelAt = 0.30f,
                StaminaCost = 16f, MomentumGain = 0f
            };
        }

        // Ice Dart (ranged): a flick of the fingers throws one fast shard of ice (the fan of darts is a later build).
        static MoveData CreateWaterSkill()
        {
            return new MoveData
            {
                DisplayName = "Ice Dart", Kind = HitKind.Projectile, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.DartFlick, EffectKey = EffectKeys.Shards,
                Startup = 0.18f, Active = 0f, Recovery = 0.28f,
                Damage = 12f, PoiseDamage = 9f, GuardStaminaDamage = 12f, Knockback = 0.3f, Hitstop = 0.03f,
                Range = 0f, ArcDegrees = 0f, OriginHeight = 1.3f, OriginForward = 0.5f, LungeDistance = 0f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.34f, DodgeCancelAt = 0.26f,
                StaminaCost = 16f, MomentumGain = 0f,
                LaunchesProjectile = true,
                Projectile = new ProjectileSpec { Speed = 34f, Radius = 0.25f, MaxRange = 22f, VisualScale = 0.8f }
            };
        }

        // Wave Ride Strike (zip): ride a surge of water across the gap and arrive with both palms.
        static MoveData CreateWaterZipStrike()
        {
            return new MoveData
            {
                DisplayName = "Wave Ride Strike", Kind = HitKind.Sprint, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.WaveRide, EffectKey = EffectKeys.Wave,
                Startup = 0.34f, Active = 0.12f, Recovery = 0.36f,
                Damage = 11f, PoiseDamage = 16f, GuardStaminaDamage = 11f, Knockback = 0.9f, Hitstop = 0.05f,
                Range = 2.6f, ArcDegrees = 100f, VerticalReach = 1.6f, LungeDistance = 0f, TrackingTurnRate = 1080f,
                AirLift = 3.2f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.46f, DodgeCancelAt = 0.38f,
                StaminaCost = 13f, MomentumGain = 0f
            };
        }

        // Flowing Step: a longer, gliding dodge that eases out slowly, with the most forgiving perfect-dodge window.
        static DodgeProfile CreateWaterDodge()
        {
            return new DodgeProfile
            {
                DisplayName = "Flowing Step",
                EvadeOutDistance = 4.6f, SideSlipDistance = 3.6f, SlipInMaxDistance = 3.0f, BackstepDistance = 2.4f,
                Duration = 0.28f, DashEaseOut = 0.65f,   // eases out far enough that the exit flows into a run (verify J-08)
                IFrameStart = 0f, IFrameEnd = 0.20f,
                EvadeAttackCancelAt = 0.10f, SlipInAttackCancelAt = 0.06f, NextDodgeAt = 0.22f,
                ChainMax = 3,
                PerfectWindow = 0.16f, CounterWindow = 1.0f, CounterDamageMultiplier = 1.4f
            };
        }
    }
}
