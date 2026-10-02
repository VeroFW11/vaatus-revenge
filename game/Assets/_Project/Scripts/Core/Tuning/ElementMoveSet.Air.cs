namespace VaatusRevenge.Core
{
    // Air (Baguazhang): circle walking and palm changes, never standing still. The fastest string, built of many small
    // hits (HitCount) whose lunges curve round the target (OrbitDegrees), the biggest knockback, the longest dodge and zip,
    // and the best air game. In Bagua the step is the beat, so a dodge between hits keeps the rhythm streak. The forms are
    // Bagua's single and double palm changes, piercing palm and swimming body. Air Blade is a gust shaped by a chop of the
    // hand (seen in the original series); no flight, no scooter, no suffocation (Build 05 spec, 7).
    //
    // Fluid values; Punishing = ApplyPunishing (the same rules for every element). Numbers from the Build 05 spec, 3.4.
    // Light-chain DodgeCancelAt follows the Fluid rule every element shares (C13): hits 1-4 can be dodged out of from
    // their first frame, the finisher from 0.30. Startups are all at least 0.09 s, so the beat window opens after the
    // move has started even at Air's 1.20 on-beat rate.
    public partial class ElementMoveSet
    {
        public static ElementMoveSet CreateAirFluid()
        {
            return new ElementMoveSet
            {
                DisplayName = "Air",
                Element = ElementId.Air,
                AnimationStyle = "air",
                LightChain = CreateAirLightChain(),
                PauseChain = CreateAirPauseChain(),
                DodgeStrike = CreateAirDodgeStrike(),
                Rhythm = CreateAirRhythm(),
                Launcher = CreateAirLauncher(),
                AirChain = CreateAirAirChain(),
                // The best air game: the most strikes per jump, two long dashes, and you hang in the air longest.
                Aerial = new AerialSettings
                {
                    AirAttacksPerJump = 9, AirDashesPerJump = 2, AirAttackGravityScale = 0.18f, AirDashDistance = 4.2f
                },
                AbilityNorth = CreateAirBlade(),
                AbilityEast = CreateAirShield(),
                Heavy = CreateAirHeavy(),
                // Hurricane Palm: a short charge with an early, tight sweet spot (Air doesn't wait).
                Charge = new ChargeSettings
                {
                    MaxChargeTime = 0.9f, SweetSpotStart = 0.40f, SweetSpotEnd = 0.60f, FaJinDamageMultiplier = 1.6f, FaJinPoiseMultiplier = 1.6f
                },
                SprintAttack = CreateAirSprintAttack(),
                PlungeAttack = CreateAirPlunge(),
                Plunge = new PlungeSettings { HangTime = 0.12f, FallSpeed = 14f, RingRadius = 3.6f },
                Skill = CreateAirSkill(),
                ZipStrike = CreateAirZipStrike(),
                // The longest zip of the four, and it reaches foes well above or below you.
                Zip = new ZipStrikeSettings { Range = 18f, AngleDegrees = 60f, MaxHeightDifference = 6f },
                Dodge = CreateAirDodge(),
                Guard = new GuardSettings { Style = DefenseStyle.ParryOnly, DeflectWindow = 0.18f, DeflectWhiffLockout = 0.30f, ParryArcDegrees = 360f },
                Momentum = new MomentumSettings { Enabled = false },   // only Fire has an identity meter in this build
                DataVersion = CurrentDataVersion
            };
        }

        public static ElementMoveSet CreateAirPunishing()
        {
            ElementMoveSet set = CreateAirFluid();
            ApplyPunishing(set);
            return set;
        }

        // Palm changes while circling: a double piercing palm, a turning palm, the swimming-body sweep, the double palm
        // change that carries you half way round the foe (Orbit 150), then the gale palm, a gust that blows it far away.
        // Each hit is two or three quick sub-hits. Poise 4 + 4 + 6 + 9 + 12 = 35, plus hit 1 = 39.
        static MoveData[] CreateAirLightChain()
        {
            var piercing = new MoveData
            {
                DisplayName = "Piercing Palm", Kind = HitKind.Light, Limb = Limb.LeftFist,
                AnimationKey = AnimationKeys.PiercingPalm, EffectKey = EffectKeys.Burst,
                Startup = 0.09f, Active = 0.10f, Recovery = 0.22f,
                Damage = 3f, PoiseDamage = 2f, GuardStaminaDamage = 3f, Knockback = 0.15f, Hitstop = 0.025f,
                Range = 3.0f, ArcDegrees = 60f, LungeDistance = 0.6f,
                HitCount = 2, HitInterval = 0.06f, OrbitDegrees = 20f,
                ComboWindowStart = 0.10f, ComboWindowEnd = 0.36f, ChainCancelAt = 0.19f, DodgeCancelAt = 0f,
                StaminaCost = 7f, MomentumGain = 0f
            };
            var turning = new MoveData
            {
                DisplayName = "Turning Palm", Kind = HitKind.Light, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.TurningPalm, EffectKey = EffectKeys.Trail,
                Startup = 0.09f, Active = 0.12f, Recovery = 0.22f,
                Damage = 3f, PoiseDamage = 2f, GuardStaminaDamage = 3f, Knockback = 0.15f, Hitstop = 0.025f,
                Range = 3.0f, ArcDegrees = 120f, LungeDistance = 0.6f,
                HitCount = 2, HitInterval = 0.08f, OrbitDegrees = 35f,
                ComboWindowStart = 0.12f, ComboWindowEnd = 0.38f, ChainCancelAt = 0.21f, DodgeCancelAt = 0f,
                StaminaCost = 7f, MomentumGain = 0f
            };
            var swimSweep = new MoveData
            {
                DisplayName = "Swimming Body Sweep", Kind = HitKind.Light, Limb = Limb.LeftFist,
                AnimationKey = AnimationKeys.SwimSweep, EffectKey = EffectKeys.Trail,
                Startup = 0.10f, Active = 0.14f, Recovery = 0.26f,
                Damage = 3f, PoiseDamage = 2f, GuardStaminaDamage = 3f, Knockback = 0.2f, Hitstop = 0.025f,
                Range = 3.4f, ArcDegrees = 160f, LungeDistance = 0.4f,
                HitCount = 3, HitInterval = 0.05f, OrbitDegrees = 30f,
                ComboWindowStart = 0.14f, ComboWindowEnd = 0.44f, ChainCancelAt = 0.24f, DodgeCancelAt = 0f,
                StaminaCost = 8f, MomentumGain = 0f
            };
            var doubleChange = new MoveData
            {
                DisplayName = "Double Palm Change", Kind = HitKind.Light, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.DoublePalmChange, EffectKey = EffectKeys.Vortex,
                Startup = 0.12f, Active = 0.18f, Recovery = 0.28f,
                Damage = 3f, PoiseDamage = 3f, GuardStaminaDamage = 3f, Knockback = 0.25f, Hitstop = 0.03f,
                Range = 3.0f, ArcDegrees = 200f, LungeDistance = 0.8f,
                HitCount = 3, HitInterval = 0.07f, OrbitDegrees = 150f,
                ComboWindowStart = 0.22f, ComboWindowEnd = 0.52f, ChainCancelAt = 0.30f, DodgeCancelAt = 0f,
                StaminaCost = 9f, MomentumGain = 0f
            };
            var gale = new MoveData
            {
                DisplayName = "Gale Palm", Kind = HitKind.Light, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.GalePalm, EffectKey = EffectKeys.Cone,
                Startup = 0.16f, Active = 0.12f, Recovery = 0.40f,
                Damage = 14f, PoiseDamage = 12f, GuardStaminaDamage = 14f, Knockback = 4.0f, Hitstop = 0.07f,
                Range = 5.0f, ArcDegrees = 70f, LungeDistance = 0.3f,
                ComboWindowStart = 0.40f, ComboWindowEnd = 0.68f, ChainCancelAt = 0.52f, DodgeCancelAt = 0.30f,
                StaminaCost = 11f, MomentumGain = 0f
            };
            return new[] { piercing, turning, swimSweep, doubleChange, gale };
        }

        // The pause branch (X X, wait, X X): walk the circle all the way round the foe striking as you go (Orbit 180), then a
        // whirlwind that lifts it into the air. Poise 4 + 4 + 2x4 + 3x3 = 25, plus hit 1 = 29.
        static MoveData[] CreateAirPauseChain()
        {
            var circleWalk = new MoveData
            {
                DisplayName = "Circle Walk Flurry", Kind = HitKind.Light, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.CircleWalk, EffectKey = EffectKeys.Trail,
                Startup = 0.09f, Active = 0.40f, Recovery = 0.24f,
                Damage = 3f, PoiseDamage = 2f, GuardStaminaDamage = 3f, Knockback = 0.15f, Hitstop = 0.025f,
                Range = 3.0f, ArcDegrees = 120f, LungeDistance = 0.4f,
                HitCount = 4, HitInterval = 0.10f, OrbitDegrees = 180f,
                ComboWindowStart = 0.44f, ComboWindowEnd = 0.72f, ChainCancelAt = 0.49f, DodgeCancelAt = 0.14f,
                StaminaCost = 10f, MomentumGain = 0f
            };
            var whirlwind = new MoveData
            {
                DisplayName = "Whirlwind", Kind = HitKind.Light, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.Whirlwind, EffectKey = EffectKeys.Vortex,
                Startup = 0.18f, Active = 0.30f, Recovery = 0.40f,
                Damage = 3f, PoiseDamage = 3f, GuardStaminaDamage = 3f, Knockback = 0.3f, Hitstop = 0.03f,
                Range = 3.8f, ArcDegrees = 360f, OriginForward = 0f, VerticalReach = 1.6f, LungeDistance = 0f,
                HitCount = 3, HitInterval = 0.10f, LaunchSpeed = 9f, AirLift = 4f,
                ComboWindowStart = 0.52f, ComboWindowEnd = 0.80f, ChainCancelAt = 0.70f, DodgeCancelAt = 0.30f,
                StaminaCost = 14f, MomentumGain = 0f
            };
            return new[] { circleWalk, whirlwind };
        }

        // Circle Step Palm: out of a dodge, step round the foe (Orbit 90) with three quick palms. In Bagua the step is the
        // beat: Air's dodge strike always counts as on the beat (ElementRhythm.DodgeKeepsBeat).
        static MoveData CreateAirDodgeStrike()
        {
            return new MoveData
            {
                DisplayName = "Circle Step Palm", Kind = HitKind.Light, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.CircleStepPalm, EffectKey = EffectKeys.Trail,
                Startup = 0.09f, Active = 0.14f, Recovery = 0.24f,
                Damage = 3f, PoiseDamage = 2f, GuardStaminaDamage = 3f, Knockback = 0.2f, Hitstop = 0.025f,
                Range = 2.0f, ArcDegrees = 140f, LungeDistance = 0.7f,
                HitCount = 3, HitInterval = 0.05f, OrbitDegrees = 90f,
                ComboWindowStart = 0.12f, ComboWindowEnd = 0.38f, ChainCancelAt = 0.24f, DodgeCancelAt = 0f,
                StaminaCost = 0f, MomentumGain = 0f
            };
        }

        // Baguazhang: the step is the beat. The quickest on-beat speed-up, and a dodge between hits keeps the streak.
        static ElementRhythm CreateAirRhythm()
        {
            return new ElementRhythm { OnBeatPlaybackRate = 1.20f, DodgeKeepsBeat = true };
        }

        // Updraft Palm: an upward palm and a rising gust that throws the foe high and carries you up after it.
        static MoveData CreateAirLauncher()
        {
            return new MoveData
            {
                DisplayName = "Updraft Palm", Kind = HitKind.Special, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.UpdraftPalm, EffectKey = EffectKeys.Pillar,
                Startup = 0.12f, Active = 0.10f, Recovery = 0.32f,
                Damage = 6f, PoiseDamage = 8f, GuardStaminaDamage = 6f, Knockback = 0.1f, Hitstop = 0.05f,
                Range = 3.0f, ArcDegrees = 100f, VerticalReach = 1.8f, LungeDistance = 0.3f,
                LaunchSpeed = 12f, SelfLift = 10.5f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.24f, DodgeCancelAt = 0.24f,
                StaminaCost = 10f, MomentumGain = 0f
            };
        }

        // The air string: a double swipe, a spiralling kick (three hits as the body turns), then a downburst palm that
        // drives the foe to the floor.
        static MoveData[] CreateAirAirChain()
        {
            var swipe = new MoveData
            {
                DisplayName = "Air Swipe", Kind = HitKind.Light, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.AirSwipe, EffectKey = EffectKeys.Trail,
                Startup = 0.09f, Active = 0.10f, Recovery = 0.20f,
                Damage = 3f, PoiseDamage = 2f, GuardStaminaDamage = 3f, Knockback = 0.1f, Hitstop = 0.025f,
                Range = 3.0f, ArcDegrees = 110f, VerticalReach = 1.8f, LungeDistance = 0.3f,
                HitCount = 2, HitInterval = 0.05f, AirLift = 3.2f, SelfLift = 3.2f,
                ComboWindowStart = 0.10f, ComboWindowEnd = 0.34f, ChainCancelAt = 0.19f, DodgeCancelAt = 0.12f,
                StaminaCost = 5f, MomentumGain = 0f
            };
            var spiral = new MoveData
            {
                DisplayName = "Spiral Kick", Kind = HitKind.Light, Limb = Limb.RightFoot,
                AnimationKey = AnimationKeys.AirSpiralKick, EffectKey = EffectKeys.Vortex,
                Startup = 0.10f, Active = 0.14f, Recovery = 0.22f,
                Damage = 3f, PoiseDamage = 2f, GuardStaminaDamage = 3f, Knockback = 0.1f, Hitstop = 0.025f,
                Range = 3.0f, ArcDegrees = 200f, VerticalReach = 1.8f, LungeDistance = 0.3f,
                HitCount = 3, HitInterval = 0.05f, AirLift = 3.4f, SelfLift = 3.4f,
                ComboWindowStart = 0.14f, ComboWindowEnd = 0.40f, ChainCancelAt = 0.24f, DodgeCancelAt = 0.14f,
                StaminaCost = 6f, MomentumGain = 0f
            };
            var downburst = new MoveData
            {
                DisplayName = "Downburst Palm", Kind = HitKind.Light, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.AirDownburst, EffectKey = EffectKeys.Slam,
                Startup = 0.14f, Active = 0.10f, Recovery = 0.32f,
                Damage = 10f, PoiseDamage = 16f, GuardStaminaDamage = 10f, Knockback = 0.6f, Hitstop = 0.065f,
                Range = 3.2f, ArcDegrees = 140f, VerticalReach = 2.4f, LungeDistance = 0.2f,
                SlamSpeed = 18f, SelfLift = 0f,   // J3-S05: a slam does not lift you over the foe
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.36f, DodgeCancelAt = 0.24f,
                StaminaCost = 8f, MomentumGain = 0f
            };
            return new[] { swipe, spiral, downburst };
        }

        // Air Blade (mid range): a chop of the hand sends a thin, fast blade of air straight down the line.
        static MoveData CreateAirBlade()
        {
            return new MoveData
            {
                DisplayName = "Air Blade", Kind = HitKind.Special, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.AirBlade, EffectKey = EffectKeys.Line,
                Startup = 0.16f, Active = 0.12f, Recovery = 0.34f,
                Damage = 12f, PoiseDamage = 8f, GuardStaminaDamage = 12f, Knockback = 0.6f, Hitstop = 0.05f,
                Range = 10f, ArcDegrees = 10f, OriginHeight = 1.2f, VerticalReach = 2.0f, LungeDistance = 0f,
                TrackingTurnRate = 900f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.40f, DodgeCancelAt = 0.28f,
                StaminaCost = 16f, MomentumGain = 0f
            };
        }

        // Air Shield (close, all round): both arms sweep round and a sphere of spinning air bursts out from you twice,
        // throwing foes back. Unshakeable while it spins (reflecting projectiles is a later build).
        static MoveData CreateAirShield()
        {
            return new MoveData
            {
                DisplayName = "Air Shield", Kind = HitKind.Special, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.AirShield, EffectKey = EffectKeys.Dome,
                Startup = 0.04f, Active = 0.50f, Recovery = 0.30f,
                Damage = 4f, PoiseDamage = 5f, GuardStaminaDamage = 4f, Knockback = 2.4f, Hitstop = 0.04f,
                Range = 2.8f, ArcDegrees = 360f, OriginForward = 0f, VerticalReach = 1.4f, LungeDistance = 0f,
                HitCount = 2, HitInterval = 0.25f, HyperArmor = true, HyperArmorFrom = 0f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.56f, DodgeCancelAt = 0.20f,
                StaminaCost = 16f, MomentumGain = 0f
            };
        }

        // Hurricane Palm (the charged heavy): a turning step and both palms released as a narrow, violent gust.
        static MoveData CreateAirHeavy()
        {
            return new MoveData
            {
                DisplayName = "Hurricane Palm", Kind = HitKind.Heavy, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.HurricanePalm, EffectKey = EffectKeys.Cone,
                Startup = 0.20f, Active = 0.14f, Recovery = 0.42f,
                Damage = 16f, PoiseDamage = 22f, GuardStaminaDamage = 16f, Knockback = 5.0f, Hitstop = 0.07f,
                Range = 7.0f, ArcDegrees = 30f, LungeDistance = 0.2f, TrackingTurnRate = 720f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.70f, DodgeCancelAt = 0.30f,
                StaminaCost = 20f, MomentumGain = 0f
            };
        }

        static MoveData CreateAirSprintAttack()
        {
            return new MoveData
            {
                DisplayName = "Wind Runner Kick", Kind = HitKind.Sprint, Limb = Limb.RightFoot,
                AnimationKey = AnimationKeys.WindRunnerKick, EffectKey = EffectKeys.Trail,
                Startup = 0.14f, Active = 0.12f, Recovery = 0.34f,
                Damage = 12f, PoiseDamage = 14f, GuardStaminaDamage = 12f, Knockback = 1.2f, Hitstop = 0.055f,
                Range = 2.8f, ArcDegrees = 100f, LungeDistance = 4.0f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.40f, DodgeCancelAt = 0.26f,
                StaminaCost = 12f, MomentumGain = 0f
            };
        }

        // Air Burst Landing (plunge): a long, soft hang, then a cushion of air bursts out in a wide ring as you land.
        static MoveData CreateAirPlunge()
        {
            return new MoveData
            {
                DisplayName = "Air Burst Landing", Kind = HitKind.Plunge, Limb = Limb.BothFists,
                AnimationKey = AnimationKeys.AirLanding, EffectKey = EffectKeys.Wave,
                Startup = 0f, Active = 0f, Recovery = 0.30f,
                Damage = 10f, PoiseDamage = 14f, GuardStaminaDamage = 10f, Knockback = 2.6f, Hitstop = 0.06f,
                Range = 0f, ArcDegrees = 360f, LungeDistance = 0f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.30f, DodgeCancelAt = 0.18f,
                StaminaCost = 12f, MomentumGain = 0f
            };
        }

        // Air Blast (ranged): a snapping palm throws a fast ball of air that knocks the foe back.
        static MoveData CreateAirSkill()
        {
            return new MoveData
            {
                DisplayName = "Air Blast", Kind = HitKind.Projectile, Limb = Limb.RightFist,
                AnimationKey = AnimationKeys.AirBlast, EffectKey = EffectKeys.Burst,
                Startup = 0.14f, Active = 0f, Recovery = 0.22f,
                Damage = 8f, PoiseDamage = 10f, GuardStaminaDamage = 8f, Knockback = 2.5f, Hitstop = 0.03f,
                Range = 0f, ArcDegrees = 0f, OriginHeight = 1.3f, OriginForward = 0.5f, LungeDistance = 0f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.26f, DodgeCancelAt = 0.20f,
                StaminaCost = 14f, MomentumGain = 0f,
                LaunchesProjectile = true,
                Projectile = new ProjectileSpec { Speed = 40f, Radius = 0.45f, MaxRange = 28f, VisualScale = 1.2f }
            };
        }

        // Wind Leap Strike (zip): a gust-assisted leap (a jump, not flight) across the gap into a flying kick.
        static MoveData CreateAirZipStrike()
        {
            return new MoveData
            {
                DisplayName = "Wind Leap Strike", Kind = HitKind.Sprint, Limb = Limb.RightFoot,
                AnimationKey = AnimationKeys.WindLeap, EffectKey = EffectKeys.Trail,
                Startup = 0.26f, Active = 0.10f, Recovery = 0.30f,
                Damage = 9f, PoiseDamage = 12f, GuardStaminaDamage = 9f, Knockback = 0.9f, Hitstop = 0.05f,
                Range = 2.6f, ArcDegrees = 110f, VerticalReach = 2.0f, LungeDistance = 0f, TrackingTurnRate = 1080f,
                AirLift = 3.4f,
                ComboWindowStart = 0f, ComboWindowEnd = 0f, ChainCancelAt = 0.38f, DodgeCancelAt = 0.30f,
                StaminaCost = 10f, MomentumGain = 0f
            };
        }

        // Circle Step: the longest dodge, curving light and quick to repeat, four in a row before a breather.
        static DodgeProfile CreateAirDodge()
        {
            return new DodgeProfile
            {
                DisplayName = "Circle Step",
                EvadeOutDistance = 4.8f, SideSlipDistance = 3.6f, SlipInMaxDistance = 3.4f, BackstepDistance = 2.6f,
                Duration = 0.28f, DashEaseOut = 0.65f,   // eases out far enough that the exit flows into a run (verify J-08)
                IFrameStart = 0f, IFrameEnd = 0.20f,
                EvadeAttackCancelAt = 0.08f, SlipInAttackCancelAt = 0.05f, NextDodgeAt = 0.16f,
                ChainMax = 4,
                PerfectWindow = 0.12f, CounterWindow = 0.8f, CounterDamageMultiplier = 1.3f
            };
        }
    }
}
