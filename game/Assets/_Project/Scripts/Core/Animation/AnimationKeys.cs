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

    // Fire effects a move asks for when it goes active (MoveData.EffectKey). "" = the default flame burst.
    public static class EffectKeys
    {
        public const string Burst = "burst";                 // a flame burst from the striking limb along the facing
        public const string Cone = "cone";                   // a wide cone of fire in front (Phoenix Palm)
        public const string Pillar = "pillar";               // a rising column of fire (launcher)
        public const string Whip = "whip";                   // a long arc of flame across the move's range (Fire Whip)
        public const string Wheel = "wheel";                 // a ring of fire all round (Flame Wheel)
        public const string Slam = "slam";                   // a downward burst (tornado kick, axe kick)
        public const string Trail = "trail";                 // fire trails on the striking limb (air kicks, zip kick)
    }
}
