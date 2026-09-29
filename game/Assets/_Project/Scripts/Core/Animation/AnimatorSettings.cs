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

        // ---- Landing
        public float LandDuration = 0.22f;           // knees absorb a landing for this long (when nothing else is playing)
        public float HardLandingSpeed = 9f;          // m/s: landings faster than this sink deeper
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
        public float RunStance = 0.36f;              // ...and when running (both feet leave the ground: a flight phase)
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
