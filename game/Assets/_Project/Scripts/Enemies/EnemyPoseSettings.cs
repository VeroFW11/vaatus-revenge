using System;
using UnityEngine;

namespace VaatusRevenge
{
    // The grey-box "animation" for enemy attacks: where the weapon hand goes during each wind-up and strike,
    // and how fast. Purely visual (no gameplay numbers), kept in data so poses can be tweaked in the Inspector.
    //
    // Readability is the point: each attack type has its own silhouette, so players learn to tell them apart
    // before they land. Slash = sword cocked over the shoulder, overhead = sword straight up with the body
    // rearing back, thrust = sword drawn in level and pointing at you, then a dead-still pause.
    //
    // Hand positions are in the fighter's own space, measured from its feet: x right, y up, z forward (the same
    // space as GreyboxRig.Strike). The rig points the blade along the line from the shoulder to the hand, so
    // raising the hand raises the blade.
    [Serializable]
    public class EnemyPoseSettings
    {
        [Header("Timing (seconds)")]
        [Tooltip("How fast the weapon rises into its wind-up pose. It's then held until the strike, so a long wind-up reads as a held threat.")]
        public float WindUpRiseTime = 0.22f;
        [Tooltip("The rise never takes more than this share of a short wind-up, so there's always a visible hold.")]
        [Range(0.1f, 1f)] public float WindUpRiseMaxShare = 0.5f;
        [Tooltip("How fast a strike snaps from the wind-up pose to its end pose. Short = a whip-fast swing.")]
        public float StrikeSnapTime = 0.06f;
        [Tooltip("Share of the recovery spent pulling back to guard. The slow pull-back is the punish window, so it should look open.")]
        [Range(0f, 1f)] public float RecoverShare = 0.8f;
        [Tooltip("How fast an interrupted wind-up drops back to guard.")]
        public float ReturnToGuardTime = 0.2f;

        [Header("Sword hand (fighter space from the feet: x right, y up, z forward)")]
        public Vector3 SlashWindUp = new Vector3(0.42f, 1.62f, -0.05f);
        [Tooltip("Strikes end fully extended so the blade shows the attack's reach (keep them in step with the moves' Range).")]
        public Vector3 SlashStrike = new Vector3(-0.25f, 1.22f, 0.72f);
        [Tooltip("Every second strike of a combo swings back the other way.")]
        public Vector3 BackhandStrike = new Vector3(0.52f, 1.3f, 0.68f);
        public Vector3 OverheadWindUp = new Vector3(0.12f, 2.0f, -0.15f);
        public Vector3 OverheadStrike = new Vector3(0.08f, 0.88f, 0.8f);
        public Vector3 ThrustWindUp = new Vector3(0.22f, 1.42f, 0.12f);
        public Vector3 ThrustStrike = new Vector3(0.12f, 1.38f, 1.0f);

        [Header("Body lean in degrees (+ forward, - back)")]
        public float SlashWindUpLean = -5f;
        public float SlashStrikeLean = 8f;
        public float OverheadWindUpLean = -10f;
        public float OverheadStrikeLean = 14f;
        public float ThrustWindUpLean = -9f;
        public float ThrustStrikeLean = 10f;

        [Header("Crossbow")]
        [Tooltip("Where the crossbow hand goes while aiming: raised to the chest, near where the bolt leaves.")]
        public Vector3 AimPose = new Vector3(0.08f, 1.42f, 0.3f);
        [Tooltip("Each shot kicks the crossbow back and up by this much.")]
        public Vector3 RecoilOffset = new Vector3(0f, 0.04f, -0.1f);
        public float RecoilKickTime = 0.04f;
        public float RecoilLean = -4f;
        [Tooltip("The crossbow tilts up or down toward the player while aiming (e.g. from the raised platform), at most this many degrees.")]
        public float MaxAimPitch = 50f;
        [Tooltip("Degrees per second the crossbow tilts toward its aim.")]
        public float AimTurnRate = 240f;
    }
}
