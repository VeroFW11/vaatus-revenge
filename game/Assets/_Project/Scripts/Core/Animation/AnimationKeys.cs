namespace VaatusRevenge.Core
{
    // The ids that connect gameplay to presentation. MoveData.AnimationKey and MoveData.EffectKey hold them, so a
    // move's look is data: the procedural pose library and the pack-clip mapping (FighterAnimationSet) both look
    // clips up by these strings, and adding a move never means a new code path. They are internal ids, never shown
    // to players, and they say what the body does (a jab, a crescent kick), not who does it.
    public static class AnimationKeys
    {
        // ---- Locomotion and states (both the player and enemies) ----
        public const string Idle = "idle";                   // fighting stance, breathing
        public const string Walk = "walk";
        public const string Run = "run";
        public const string Sprint = "sprint";
        public const string Strafe = "strafe";
        public const string Jump = "jump";                   // take-off and rise
        public const string Fall = "fall";
        public const string Land = "land";
        public const string Dodge = "dodge";                 // ground dash (Flame Step)
        public const string AirDash = "air_dash";
        public const string Backstep = "backstep";
        public const string Parry = "parry";                 // the parry / guard stance
        public const string ParrySuccess = "parry_success";
        public const string Block = "block";                 // held block (elements that block)
        public const string Hurt = "hurt";                   // flinch from a clean hit
        public const string Stagger = "stagger";
        public const string Launched = "launched";           // tumbling in the air after a launcher
        public const string Knockdown = "knockdown";         // lying on the ground after a juggle
        public const string GetUp = "get_up";
        public const string Death = "death";
        public const string Heal = "heal";                   // drinking
        public const string Charge = "charge";               // chambering the fa jin palm

        // ---- Player moves (Fire, Northern Shaolin) ----
        public const string Jab = "jab";                     // chain 1: lead straight punch
        public const string Cross = "cross";                 // chain 2: rear straight punch
        public const string SnapKick = "snap_kick";          // chain 3: front snap kick
        public const string SpinKick = "spin_kick";          // chain 4: spinning back / roundhouse kick
        public const string PhoenixPalm = "phoenix_palm";    // chain 5: double palm push, a cone of fire
        public const string Launcher = "launcher";           // rising kick that throws the target up
        public const string AirJab = "air_jab";
        public const string AirCrescent = "air_crescent";    // crescent kick in the air
        public const string AirTornado = "air_tornado";      // tornado kick, slams the target down
        public const string AxeKick = "axe_kick";            // falling axe kick (plunge)
        public const string FaJinPalm = "fajin_palm";
        public const string FireBlast = "fire_blast";        // ranged: a thrusting punch that throws a fireball
        public const string FireWhip = "fire_whip";          // mid range: a sweeping arm whip of flame
        public const string FlameWheel = "flame_wheel";      // close, all round: a spinning low sweep ringed with fire
        public const string ZipKick = "zip_kick";            // zip strike: flying kick at the end of the dash
        public const string SprintKick = "sprint_kick";      // flying fire kick out of a sprint
        public const string SweepKick = "sweep_kick";        // pause chain 1: low spinning sweep
        public const string RisingPhoenixKick = "rising_phoenix_kick"; // pause chain 2: rising kick that throws the foe up
        public const string SpinBackKick = "spin_back_kick"; // dodge strike: spinning back kick

        // ---- Dodges by kind and the element switch (every element) ----
        public const string DodgeSlip = "dodge_slip";        // slip in toward the target
        public const string DodgeSideLeft = "dodge_side_l";  // side-step to the left (also the automatic side-step)
        public const string DodgeSideRight = "dodge_side_r";
        public const string DodgeEvade = "dodge_evade";      // hop back out of reach, still facing the target
        public const string ElementSwitch = "element_switch"; // a short flourish when changing element while free

        // ---- Water (Tai Chi) ----
        public const string PalmWard = "palm_ward";
        public const string PalmRollback = "palm_rollback";
        public const string ForearmPress = "forearm_press";
        public const string TwoPalmPush = "two_palm_push";
        public const string SingleWhip = "single_whip";
        public const string CloudHands = "cloud_hands";
        public const string SplitMane = "split_mane";
        public const string ReturnTide = "return_tide";
        public const string CraneRise = "crane_rise";
        public const string AirBrushPalm = "air_brush_palm";
        public const string AirShuttle = "air_shuttle";
        public const string AirNeedle = "air_needle";
        public const string SnakeDrop = "snake_drop";
        public const string TidePalm = "tide_palm";
        public const string WaterLash = "water_lash";
        public const string TideRing = "tide_ring";
        public const string DartFlick = "dart_flick";
        public const string WaveRide = "wave_ride";
        public const string RushPalm = "rush_palm";

        // ---- Earth (Hung Gar) ----
        public const string HorsePunch = "horse_punch";
        public const string TigerClaw = "tiger_claw";
        public const string StompLine = "stomp_line";
        public const string ButterflyPalms = "butterfly_palms";
        public const string QuakeSlam = "quake_slam";
        public const string BoulderRaise = "boulder_raise";
        public const string BoulderHurl = "boulder_hurl";
        public const string PivotElbow = "pivot_elbow";
        public const string PillarUppercut = "pillar_uppercut";
        public const string AirHammer = "air_hammer";
        public const string AirBackKick = "air_back_kick";
        public const string AirMeteor = "air_meteor";
        public const string QuakeDrop = "quake_drop";
        public const string RootFaJin = "root_fajin";
        public const string SpikeLine = "spike_line";
        public const string StoneTent = "stone_tent";
        public const string BoulderToss = "boulder_toss";
        public const string EarthSurf = "earth_surf";
        public const string ShoulderCharge = "shoulder_charge";

        // ---- Air (Baguazhang) ----
        public const string PiercingPalm = "piercing_palm";
        public const string TurningPalm = "turning_palm";
        public const string SwimSweep = "swim_sweep";
        public const string DoublePalmChange = "double_palm_change";
        public const string GalePalm = "gale_palm";
        public const string CircleWalk = "circle_walk";
        public const string Whirlwind = "whirlwind";
        public const string CircleStepPalm = "circle_step_palm";
        public const string UpdraftPalm = "updraft_palm";
        public const string AirSwipe = "air_swipe";
        public const string AirSpiralKick = "air_spiral_kick";
        public const string AirDownburst = "air_downburst";
        public const string AirLanding = "air_landing";
        public const string HurricanePalm = "hurricane_palm";
        public const string AirBlade = "air_blade";
        public const string AirShield = "air_shield";
        public const string AirBlast = "air_blast";
        public const string WindLeap = "wind_leap";
        public const string WindRunnerKick = "wind_runner_kick";

        // ---- Enemy moves ----
        public const string SwordSlash = "sword_slash";
        public const string SwordOverhead = "sword_overhead";
        public const string SwordDoubleSlash = "sword_double";
        public const string SwordThrust = "sword_thrust";
        public const string Shove = "shove";                 // the soldier's break-out
        public const string CrossbowShot = "crossbow_shot";
        public const string CrossbowBurst = "crossbow_burst";
        public const string PracticeSwing = "practice_swing";
    }

    // The effect a move asks for when it goes active (MoveData.EffectKey). "" = the default burst. A closed set of
    // SHAPES: the element supplies the look (fire, water, stone, wind), so move data may only use these.
    public static class EffectKeys
    {
        public const string Burst = "burst";                 // a flame burst from the striking limb along the facing
        public const string Cone = "cone";                   // a wide cone of fire in front (Phoenix Palm)
        public const string Pillar = "pillar";               // a rising column of fire (launcher)
        public const string Whip = "whip";                   // a long arc of flame across the move's range (Fire Whip)
        public const string Wheel = "wheel";                 // a ring of fire all round (Flame Wheel)
        public const string Slam = "slam";                   // a downward burst (tornado kick, axe kick)
        public const string Trail = "trail";                 // fire trails on the striking limb (air kicks, zip kick)
        public const string Wave = "wave";                   // a travelling wave or surge along the strike
        public const string Line = "line";                   // a straight line along the ground (spikes, a stomp's crack)
        public const string Dome = "dome";                   // a shell round the body (a tent of stone, a shield of air)
        public const string Vortex = "vortex";               // a spinning swirl round the body or the target
        public const string Shards = "shards";               // small fast pieces (ice darts, stone chips)
        public const string Stomp = "stomp";                 // a ground impact round the feet
    }
}
