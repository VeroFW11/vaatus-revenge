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
        public float StaminaRegenDelay = 0.45f;      // pause after spending stamina before regen starts
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
        public DodgeTrigger DodgeTrigger = DodgeTrigger.OnPress;

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
        public float SoftLockRange = 4f;             // not locked on: attacks aim at the nearest enemy this close...
        public float SoftLockAngle = 60f;            // ...and at most this many degrees off the direction you're aiming
        public float LungeStopGap = 0.3f;            // lunges stop this far from the target's body so you never run through it

        public static PlayerTuning CreateFluid()
        {
            return new PlayerTuning();
        }

        // Elden Ring-like: dodge on release, slower stamina, sprint costs stamina, tighter buffer.
        public static PlayerTuning CreatePunishing()
        {
            var t = new PlayerTuning();
            t.PresetName = "Punishing";
            t.StaminaRegen = 32f;
            t.StaminaRegenDelay = 0.8f;
            t.SprintStaminaDrain = 6f;
            t.InputBufferWindow = 0.2f;
            t.DodgeTrigger = DodgeTrigger.OnRelease;
            return t;
        }
    }
}
