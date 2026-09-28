using System;
using UnityEngine;

namespace VaatusRevenge
{
    // Where the grey-box fists and feet go for each kind of move, so strikes read clearly without real
    // animation. Positions are in the player's local space: x = right, y = up from the feet, z = forward
    // (the player is 1.8 m tall). They're written for the right side; left-hand and left-foot moves mirror x.
    // Timings of attacks come from the move's own frame data (startup = limb goes out, active = holds,
    // recovery = comes back), so only the extra, purely visual times live here.
    [Serializable]
    public class PlayerStrikePoses
    {
        [Header("Strikes")]
        [Tooltip("Straight punch at chest height (light chain fists).")]
        public Vector3 Punch = new Vector3(0.08f, 1.35f, 0.85f);
        [Tooltip("Front kick at waist height.")]
        public Vector3 Kick = new Vector3(0.12f, 0.95f, 0.8f);
        [Tooltip("Flying kick at head height (sprint attack).")]
        public Vector3 HighKick = new Vector3(0.1f, 1.55f, 0.95f);
        [Tooltip("Where the foot sweeps to during a spinning kick (the body turns underneath it).")]
        public Vector3 SpinKick = new Vector3(0.3f, 1.05f, 0.75f);
        [Tooltip("Kicks at least this wide (degrees of arc) spin the body and throw a low ring of fire.")]
        public float SpinKickMinArc = 180f;
        [Tooltip("How far a spinning kick turns the body, in degrees.")]
        public float SpinDegrees = 360f;
        [Tooltip("Two-handed palm strike (the charged heavy).")]
        public Vector3 Palm = new Vector3(0f, 1.25f, 0.9f);
        [Tooltip("Both-hand push that launches the ranged skill.")]
        public Vector3 Blast = new Vector3(0f, 1.3f, 0.85f);
        [Tooltip("Moves with no active time (a projectile launch) still hold the pose this long so it reads.")]
        public float MinStrikeHold = 0.06f;
        [Tooltip("Seconds for a limb to ease back when its move is cut short.")]
        public float InterruptRetractTime = 0.1f;

        [Header("Heavy charge")]
        [Tooltip("While charging the heavy, the striking fist is pulled back to the hip here.")]
        public Vector3 Chamber = new Vector3(0.22f, 1.0f, -0.12f);
        [Tooltip("Seconds to pull the fist back.")]
        public float ChamberTime = 0.15f;

        [Header("Falling axe kick")]
        [Tooltip("The kicking foot rises above the head while you drop.")]
        public Vector3 AxeKickRaise = new Vector3(0.1f, 1.75f, 0.45f);
        [Tooltip("...and slams down in front of you on landing.")]
        public Vector3 AxeKickDrop = new Vector3(0.1f, 0.08f, 0.75f);
        public float AxeKickDropTime = 0.05f;
        public float AxeKickDropHold = 0.12f;

        [Header("Guard and healing")]
        [Tooltip("Both fists up in front of the face while guarding.")]
        public Vector3 Guard = new Vector3(0f, 1.5f, 0.4f);
        public float GuardRaiseTime = 0.06f;
        public float GuardLowerTime = 0.12f;
        [Tooltip("Both hands to the mouth while drinking spirit water.")]
        public Vector3 Drink = new Vector3(0f, 1.55f, 0.3f);
        public float DrinkRaiseTime = 0.15f;
        public float DrinkLowerTime = 0.2f;

        [Header("Body lean (degrees; positive = forward)")]
        public float StrikeLeanDegrees = 8f;
        public float HeavyLeanDegrees = 16f;
        [Tooltip("Lean into a dash (forward dash leans forward, a backstep leans back, a sidestep stays upright).")]
        public float DodgeLeanDegrees = 14f;
        [Tooltip("Rocked back when a blocked hit lands on the guard.")]
        public float BlockLeanDegrees = 6f;
        [Tooltip("Flinch back when hit.")]
        public float HurtLeanDegrees = 10f;
        [Tooltip("Seconds a block or hurt flinch lasts.")]
        public float ReactionLeanTime = 0.2f;
    }
}
