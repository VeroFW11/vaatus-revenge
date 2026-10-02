using System;

namespace VaatusRevenge.Core
{
    // How the Dodge button is read. The same button also sprints while held.
    public enum DodgeTrigger
    {
        OnPress,   // Sekiro-style: dodge the moment you press; keep holding after the dash to sprint
        OnRelease  // Elden Ring-style: release before TapHoldThreshold = dodge; hold past it = sprint (no dodge)
    }

    // Everything about the player that isn't specific to one element: health, stamina, movement, input
    // feel, poise, healing. The element's attacks and dodge live in ElementMoveSet.
    //
    // Lives inside a PlayerTuningAsset (ScriptableObject) so it can be edited in the Inspector, even in
    // Play mode: PlayerCombatModel keeps a reference to this object, reads it every frame and never writes
    // to it. Defaults are the Fluid preset. Units: seconds, metres, degrees.
    [Serializable]
    public class PlayerTuning
    {
        public string PresetName = "Fluid";          // shown on the HUD (F5 = Fluid, F6 = Punishing)

        // --- Health and stamina ---
        public float MaxHealth = 100f;
        public float MaxStamina = 100f;
        public float StaminaRegen = 45f;             // stamina per second once regen has started
        public float StaminaRegenDelay = 0.6f;       // pause after spending stamina before regen starts
        public float EmptyStaminaRegenDelay = 1.5f;  // longer pause when a spend empties the bar: running dry should hurt.
                                                     // In Fluid, dodges are cheap, so in practice only mashing empties the
                                                     // bar: this pause is what makes mashing a losing plan (report 02, NEW-02)
        public float GuardRegenMultiplier = 0.5f;    // regen speed while guarding (so turtling isn't free)
        public float SprintStaminaDrain = 0f;        // stamina per second while sprinting (0 = free sprint)
        public float SprintResumeStamina = 15f;      // after sprinting dry, sprint returns once stamina is back to this

        // --- Movement ---
        public float StickDeadzone = 0.1f;           // stick tilt below this counts as no input
        public float WalkStickThreshold = 0.5f;      // tilt below this walks, above it runs
        public float WalkSpeed = 2.0f;
        public float RunSpeed = 4.8f;
        public float SprintSpeed = 7.2f;
        public float LockOnStrafeSpeed = 3.8f;       // top speed while locked on (you face the target and strafe)
        public float Acceleration = 30f;             // m/s² when speeding up
        public float Deceleration = 40f;             // m/s² when slowing down or reversing
        public float TurnRate = 900f;                // degrees per second the body turns toward where it's going
        public float AirControl = 0.35f;             // 0..1 share of ground acceleration you keep in the air
        public float JumpHeight = 1.25f;
        public float Gravity = 28f;                  // m/s², stronger than real gravity so jumps feel snappy
        public float MaxFallSpeed = 30f;
        public float GroundStickSpeed = 2f;          // small constant push down while grounded so the controller hugs slopes and steps
        public float CoyoteTime = 0.1f;              // you can still jump this long after walking off a ledge (forgives late presses)

        // --- Input feel ---
        public float InputBufferWindow = 0.25f;      // presses made while busy are remembered this long (see InputBuffer)
        public float TapHoldThreshold = 0.22f;       // Dodge button: shorter = tap (dodge), longer = hold (sprint) in OnRelease mode
        public float QueuedPressMaxAge = 0.35f;      // a light press queued as the next chain move is dropped if it gets older than
                                                     // this before it can fire (e.g. waiting for stamina), so it never fires late
        public DodgeTrigger DodgeTrigger = DodgeTrigger.OnPress;
        // Fluid dodges the moment B goes down and a hold then sprints, so every sprint starts with a dodge. A dodge pressed
        // while RUNNING (at least RunDodgeMinSpeed), with nobody locked on and no strike about to land, is a plain dash
        // along the stick (facing the dash) instead of a slip-in / circle round the nearest enemy, so 'hold B to sprint'
        // never swings you round a foe you were running past. Only a stick pointed within RunDodgeSlipInAngle of that foe
        // still slips in. Locked on, or with a strike coming, the dodge works as always (round 7, J7-05). An asset saved
        // before these fields existed reads false / 0: the old behaviour.
        public bool RunDodgeKeepsHeading = true;
        public float RunDodgeMinSpeed = 3.5f;        // m/s of locomotion when B goes down (walk 2.0, run 4.8, sprint 7.2)
        public float RunDodgeSlipInAngle = 20f;      // degrees between the stick and the foe that still mean "slip in"
        public bool DefensivePressesWin = true;      // a buffered dodge or guard survives later attack presses (attacks still replace
                                                     // attacks), so a nervous extra light press can't cancel your escape.
                                                     // false = the last press always wins (Elden Ring). Pending David (CTRL-01)

        // --- Poise and stagger ---
        public float MaxPoise = 30f;                 // stagger resistance: enemy PoiseDamage wears it down, 0 = staggered
        public float PoiseRegenDelay = 2f;           // poise starts refilling this long after the last poise damage
        public float PoiseRegenRate = 30f;           // poise per second while refilling
        public float StaggerDuration = 0.6f;         // stunned time when poise breaks
        public float ParriedStaggerDuration = 1.0f;  // stunned time when an enemy deflects your attack
        public float KnockbackTime = 0.15f;          // a clean hit's knockback distance is covered over this long

        // --- Healing (Spirit Water, like Elden Ring's flask) ---
        public int HealCharges = 3;
        public float HealDuration = 1.0f;            // the whole drinking action; you're committed for all of it
        public float HealApplyTime = 0.55f;          // health arrives (and the charge is used) at this point
        public float HealAmount = 45f;
        public float HealMoveMultiplier = 0.35f;     // share of normal speed while drinking

        // --- Aiming ---
        public float SoftLockRange = 7f;             // not locked on: attacks aim at the nearest enemy this close...
        public float SoftLockAngle = 60f;            // ...and at most this many degrees off the direction you're aiming...
        public float SoftLockMaxHeightDifference = 2.5f; // ...and no more than this far above or below you (juggled foes count; a ledge too high doesn't)
        public float LungeStopGap = 0.3f;            // lunges stop this far from the target's body so you never run through it
        public float GapCloseDistance = 4.5f;        // free-flow: a light attack at a soft-lock target out of reach lunges up to
                                                     // this much further than its own LungeDistance to close the gap (0 = off)
        public float ArriveLungeMaxSpeed = 20f;      // a stretched lunge (and an own lunge longer than ArriveLungeMinDistance, e.g. a
                                                     // sprint attack) arrives as its strike goes active, never faster than this (m/s)...
        public float ArriveLungeMaxExtraStartup = 0.22f; // ...stretching the startup by up to this many seconds to make room (0 = never;
                                                     // 0.22: room for the eased ramps of J4-02 at 20 m/s)
        public float ArriveLungeMinDistance = 1.5f;  // own lunges longer than this arrive as they strike (0 = only stretched lunges)
        public float ArriveLungeRampIn = 0.05f;      // an arriving lunge eases in from the way you were moving (a dodge's exit) over
                                                     // this long (s), instead of flipping to full speed in one frame (0 = no ease)...
        public float ArriveLungeRampOut = 0.08f;     // ...and slows over its last this-long before the strike (0 = no ease)...
        public float ArriveLungeEndSpeed = 3f;       // ...to arrive at no more than this (m/s), not a dead stop from full speed
        public float ArriveLungeMaxAccel = 300f;     // m/s per second: the ramps are only as long as the speed change needs at this
                                                     // (a short gap-closer from standing barely eases; 0 = always the full ramps)
        public float ArriveLungeEntryCarry = 0.5f;   // 0..1: share of your speed along the dash (backwards out of an evade
                                                     // included) the ease-in starts from

        // --- Combos (Build 05) ---
        public RhythmTuning Rhythm = new RhythmTuning();             // the beat: on-beat presses speed the string up
        public ComboTuning Combo = new ComboTuning();                // the hit counter
        public MixTuning Mix = new MixTuning();                      // the multi-element bonus
        public ElementSwitchTuning ElementSwitch = new ElementSwitchTuning();
        public DangerSenseSettings DangerSense = new DangerSenseSettings();
        public float StringMemoryAfterAction = 0.45f;  // a string survives a dodge, zip, ability or skill: X pressed this soon
                                                       // after it continues at the next hit instead of starting over
        public float DodgeStrikeGrace = 0.20f;         // attack this soon after a dodge ends, at a target: the dodge strike

        // Bumped when the defaults change in a way old assets must not keep (the sandbox builder offers to reset an
        // asset whose DataVersion is behind). New fields read 0 in an asset saved before they existed.
        public int DataVersion = 0;
        public const int CurrentDataVersion = 8;   // 6: Build 05 verify (switch cooldown, arriving lunges); 7: eased arrivals (J4-02)
                                                   // 8: round 7 (a dodge from a run keeps its heading: RunDodgeKeepsHeading)

        public static PlayerTuning CreateFluid()
        {
            return new PlayerTuning { DataVersion = CurrentDataVersion };
        }

        // Elden Ring-like: dodge on release, slower stamina, sprint costs stamina, tighter buffer.
        public static PlayerTuning CreatePunishing()
        {
            var t = new PlayerTuning();
            t.PresetName = "Punishing";
            // Punishing's cost is in the spending (16 per dodge, 13 per light): the regen is tuned so a player who
            // dodges well can keep going, while one who runs the bar dry still waits over a second (report 02, NEW-02:
            // 32/s after 0.8 s with a 1.4 s empty pause left dodging players empty half of every minute).
            t.StaminaRegen = 38f;
            t.StaminaRegenDelay = 0.65f;
            t.EmptyStaminaRegenDelay = 1.1f;
            t.SprintStaminaDrain = 6f;
            t.InputBufferWindow = 0.2f;
            t.DodgeTrigger = DodgeTrigger.OnRelease;
            t.RunDodgeKeepsHeading = false;          // a dodge here is a deliberate tap (a hold sprints without one)
            t.Rhythm = RhythmTuning.CreatePunishing();
            t.Combo = ComboTuning.CreatePunishing();
            t.Mix = MixTuning.CreatePunishing();
            t.ElementSwitch = ElementSwitchTuning.CreatePunishing();
            t.DangerSense = DangerSenseSettings.CreatePunishing();
            t.StringMemoryAfterAction = 0.35f;
            t.DodgeStrikeGrace = 0.10f;
            t.DataVersion = CurrentDataVersion;
            return t;
        }
    }
}
