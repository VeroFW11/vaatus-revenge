using System;

namespace VaatusRevenge.Core
{
    // How the procedural animator blends and how lively the body is. Purely presentation (no gameplay numbers),
    // kept in data so the feel of the motion can be tuned without code.
    [Serializable]
    public class AnimatorSettings
    {
        // ---- Blending
        public float DefaultFade = 0.1f;             // seconds to blend into a new action or state
        public float MinFade = 0.06f;
        public float MaxFade = 0.2f;
        public float StrikeFadeShare = 0.55f;        // a strike's blend-in is at most this share of its startup, so it lands on time
        public float LocomotionFade = 0.15f;         // seconds to blend between actions and walking/running
        public float StyleFade = 0.22f;              // seconds to blend into another style's stance (the player switching element)
        // A facing change faster than this (degrees per second: an attack snapping round to a target behind you) is
        // shown as a whip-round turn of the body over TurnCatchUpTime seconds instead of a one-frame flip.
        public float VisualTurnRate = 1200f;
        public float TurnCatchUpTime = 0.1f;
        // A blend out of a spinning pose (a spin kick chained on the beat) keeps turning the way it was going instead of
        // unwinding the short way round, when the short way would reverse the spin by more than SpinCarryMinReverse
        // degrees and the body was turning faster than SpinCarryMinSpeed (degrees per second) (J3-02).
        public float SpinCarryMinSpeed = 360f;
        public float SpinCarryMinReverse = 90f;

        // ---- Secondary motion (spring-damped: the body lags a little behind sudden changes and settles)
        public float AccelLeanPerMs2 = 0.9f;         // degrees of forward lean per m/s^2 of acceleration (lean into a sprint start)
        public float MaxAccelLean = 12f;
        public float TurnBankPerDegPerSec = 0.0035f; // degrees of lean into a turn per (deg/s x m/s)
        public float MaxTurnBank = 14f;
        public float LeanSpringFrequency = 3.5f;     // Hz: how quickly the lean follows
        public float LeanSpringDamping = 0.7f;       // 1 = no wobble, lower = a little sway before it settles
        public float HeadLagPerDegPerSec = 0.02f;    // the head trails a fast turn by this many degrees per deg/s...
        public float MaxHeadLag = 15f;               // ...up to this much

        // ---- Hit reactions (an extra spring flinch on top of whatever the body is doing)
        public float FlinchDegrees = 18f;            // how far a clean hit knocks the chest (scaled by the hit's strength)
        public float FlinchSpringFrequency = 5f;
        public float FlinchSpringDamping = 0.35f;    // low: the body rocks back and sways once before settling

        // ---- Charging (fa jin): a tremble that grows with the charge
        public float ChargeTremble = 1.6f;           // degrees at full charge
        public float ChargeTrembleRate = 17f;        // Hz

        // ---- Aiming strikes: at the first active frame the striking fist or foot points at the target. The body
        // turns (and the limb tilts up or down) by however far the authored pose would miss, eased in over the
        // wind-up and out over the recovery. Beyond these limits it doesn't try (the target is behind you).
        public bool AimStrikes = true;
        public float StrikeAimMaxYaw = 80f;
        public float StrikeAimMaxPitch = 55f;
        public float StrikeAimGiveUpYaw = 150f;
        public float StrikeAimMaxDistance = 7f;      // metres: further targets aren't aimed at
        // Seconds after the first active moment that the aim is measured at. The hit shows on the first frame at or
        // after that moment (up to one frame late), so half a 60 fps frame keeps a fast sweeping kick on target.
        public float StrikeAimLead = 1f / 120f;

        // ---- Foot locking: a planted foot stays exactly where it touched down while the body moves over it, and
        // takes a quick step when the pose wants it somewhere else (no skating).
        public bool FootLocks = true;
        public float PlantHeight = 0.025f;           // a foot this close to the floor counts as planted
        public float StepDistance = 0.2f;            // step when the pose wants the foot this far from where it stands
        public float StepTurn = 35f;                 // ...or turned this many degrees from the way it points (a pivot)
        public float StepRetargetShare = 0.5f;       // 0..1: a step's landing spot follows the pose for this share of it
        public float StepSpeed = 5f;                 // m/s of a step (sets its duration)...
        public float StepMinTime = 0.07f;            // ...within these limits
        public float StepMaxTime = 0.16f;
        public float StepLiftPerMetre = 0.35f;       // how high a step lifts the foot, per metre stepped
        public float StepMinLift = 0.045f;           // even a short step clears the floor (it's a step, not a shuffle)
        public float StepMaxLift = 0.12f;
        public float ReleaseRate = 16f;              // how quickly a released foot catches up with the pose (1/s)
        public float LeapSpeed = 6f;                 // a grounded action moving faster than this (m/s) leaps: both feet leave the floor
        // Lunge strides (J3-03): a strike rushing along the ground between StrideMinSpeed and StrideMaxSpeed (a gap-closing
        // opener, a circle walk) runs there in real steps (the walk/run cycle at its speed, starting from the rear foot
        // pushing off) instead of hovering both feet in one frozen pose; only a faster, committed rush leaps. A clip that
        // Glides (a surf) is left alone.
        public bool LungeStrides = true;
        public float StrideMinSpeed = 2.5f;
        public float StrideMaxSpeed = 12f;
        // The dodge strike's dash back in (FighterAnimInput.DashIn) strides up to this instead: it covers
        // PlayerTuning.ArriveLungeMaxSpeed (20 m/s), so it runs in on its legs rather than sliding one crouched pose
        // across the floor (J4-02). Other rushes over StrideMaxSpeed still leap.
        public float DashStrideMaxSpeed = 21f;
        public float StrideBlendSpeed = 1.5f;        // m/s over which the strides fade in above the min (and out above the max)
        public float StrideBlendRate = 30f;          // how quickly the strides take over and hand back (1/s)
        public float StrideRearFootStance = 0.6f;    // 0..1 through its stance the rear foot is when a lunge starts (pushing off)
        public float LeapLiftPerSpeed = 0.03f;       // metres of leap per m/s above LeapSpeed
        public float MaxLeapLift = 0.16f;
        public float JumpKeyMinRise = 0.5f;          // airborne: the "jump" key while rising faster than this (m/s), else "fall"
        public float LeapLiftRiseRate = 25f;         // the leap eases in this fast (1/s)...
        public float LeapLiftFallRate = 15f;         // ...and back down this fast, across the hand-back to locomotion (0 = instant)
        public float LeapLiftLandRate = 45f;         // a strike whose rush has all but stopped (under LeapLandSpeed) lands its feet this
        public float LeapLandSpeed = 1.5f;           // fast (1/s), so the support foot plants as the blow lands instead of hovering
                                                     // (J4-02); still moving faster, the slower fall keeps a planted foot from dragging

        // ---- Props: a weapon never goes through the floor
        public float PropFloorClearance = 0.03f;

        // ---- Landing
        public float LandDuration = 0.22f;           // knees absorb a landing for this long (when nothing else is playing)
        public float LandBlendIn = 0.05f;            // ...blending in over this long
        public float HardLandingSpeed = 9f;          // m/s: landings faster than this sink deeper
        // Ground fit (J5-04): for LandingFitWindow seconds after a touchdown (after more than LandingFitMinAirTime in the
        // air), a pose whose feet would hang over LandingFitTolerance metres above the floor (an air finisher or plunge
        // still in its air pose as the body lands) takes the landing legs at once and sits its pelvis down onto them,
        // handing back to the clip's own legs over LandingFitRelease seconds once they reach the floor themselves.
        public float LandingFitWindow = 0.6f;
        public float LandingFitMinAirTime = 0.05f;
        public float LandingFitTolerance = 0.04f;
        public float LandingFitRelease = 0.12f;
        // J6-01: the fit never jumps: it rises to full weight over LandingFitRise seconds at the fastest (a one-frame rise
        // pulled a foot 0.3-1.2 m down in a single frame), and while falling in an action pose with the floor known
        // (FighterAnimInput.HasFloorBelow) it starts LandingFitLead seconds before the predicted touchdown, so the legs
        // meet the floor with the body instead of after it.
        public float LandingFitRise = 0.1f;
        public float LandingFitLead = 0.12f;
    }

    // The walk / run / sprint cycle, generated from ground speed instead of fixed clips, so a fighter at any
    // speed has matching stride length and cadence (no foot sliding) and blends smoothly between gaits.
    [Serializable]
    public class GaitSettings
    {
        public float WalkSpeed = 1.8f;               // m/s: reference speeds for the key names (walk / run / sprint)
        public float RunSpeed = 4.8f;
        public float SprintSpeed = 7.2f;
        public float MoveThreshold = 0.25f;          // below this the fighter stands (idle stance)

        public float CadenceBase = 0.8f;             // full cycles (two steps) per second at a standstill...
        public float CadencePerSpeed = 0.28f;        // ...plus this much per m/s
        public float MaxCadence = 3.1f;

        public float WalkStance = 0.62f;             // share of the cycle each foot is on the ground at walking pace...
        public float RunStance = 0.44f;              // ...and when running (a short flight phase between steps)
        public float FlightFromRunBlend = 0.85f;     // 0..1: below this share of running pace a foot is always down (no flight)
        public float MaxStanceReach = 0.4f;          // metres a planted foot reaches ahead of / behind the hips

        // Share of the cycle each foot is on the ground: at least half (one foot always down) through the walk and
        // the walk-run transition, dropping below half (both feet briefly off the ground) only near full running pace.
        public float StanceShare(float speed)
        {
            float run = RunBlend(speed);
            float flightFrom = AnimMath.Clamp(FlightFromRunBlend, 0.05f, 1f);
            if (run <= flightFrom) return AnimMath.Lerp(WalkStance, Math.Max(0.5f, RunStance), run / flightFrom);
            return AnimMath.Lerp(Math.Max(0.5f, RunStance), RunStance, (run - flightFrom) / Math.Max(1e-3f, 1f - flightFrom));
        }
        public float WalkLift = 0.07f;               // how high the swinging foot rises (metres)
        public float RunLift = 0.3f;
        public float FootWidth = 0.11f;              // feet this far either side of the centre line
        public float WalkHipsDrop = 0.03f;           // pelvis lower than standing (metres)
        public float RunHipsDrop = 0.09f;
        public float WalkBob = 0.02f;                // pelvis bounce per step
        public float RunBob = 0.045f;
        public float LeanPerSpeed = 2.6f;            // degrees of forward lean per m/s
        public float MaxLean = 22f;
        public float PelvisSwing = 8f;               // pelvis turns with each step (degrees)...
        public float TorsoCounter = 1.2f;            // ...and the chest turns back against it (x pelvis swing)
        public float WalkArmSwing = 22f;             // arm swing (degrees of pitch) counter to the legs
        public float RunArmSwing = 48f;
        public float WalkArmReach = 0.88f;           // arms hang nearly straight walking...
        public float RunArmReach = 0.55f;            // ...and bend to 90 degrees running
        public float StrafeWidth = 0.06f;            // extra stance width when moving sideways
        public float SpeedSmoothing = 10f;           // how quickly the gait follows speed changes (1/s)
        public float ReverseSnapDot = -0.3f;         // the travel direction turning further than this (dot of old and new
                                                     // headings, ~107 degrees) takes the new heading at once instead of
                                                     // sweeping the stride through sideways (J6-02)
        public float StopSmoothing = 30f;            // ...and how quickly when slowing down, so a stop reaches idle at once (0 = SpeedSmoothing)
        public float GroundSpeedSmoothing = 40f;     // ...and how quickly the stride follows the body's true speed (1/s)
        public float HipsHeightSmoothing = 8f;       // how quickly the pelvis height moves between the stance and the gait (1/s,
                                                     // ~0.12 s): a deep stance (Earth) settles instead of dropping (0 = at once)

        public float Cadence(float speed)
        {
            return AnimMath.Clamp(CadenceBase + CadencePerSpeed * Math.Max(0f, speed), 0.1f, Math.Max(0.1f, MaxCadence));
        }

        // 0 at walking pace, 1 at running pace and above.
        public float RunBlend(float speed)
        {
            float span = Math.Max(0.1f, RunSpeed - WalkSpeed);
            return AnimMath.Clamp01((speed - WalkSpeed) / span);
        }
    }
}
