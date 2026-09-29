using System;
using UnityEngine;

namespace VaatusRevenge
{
    // The grey-box "animation" for enemy attacks: where the weapon hand goes during each wind-up and strike,
    // and how fast. Purely visual (no gameplay numbers), kept in data so poses can be tweaked in the Inspector.
    //
    // Readability is the point: each attack type has its own silhouette, so players learn to tell them apart
    // before they land. Slash = sword cocked over the shoulder, overhead = sword straight up with the body
    // rearing back, thrust = sword drawn in level and pointing at you, then a dead-still pause, break-out (the
    // anti-mash counter) = shoulder dropped and blade swept low behind, a crouch before a wide shove-and-slash.
    //
    // Fairness is the other point: when a strike lands, the blade tip sits exactly on the edge of what the
    // attack can hit (its OriginForward + Range, read from the attack data), so a hit never comes from beyond
    // the visible blade. Strikes are set as a direction; how far the hand reaches out is worked out from the
    // attack's reach, so retuning a Range moves the blade with it.
    //
    // Hand positions are in the fighter's own space, measured from its feet: x right, y up, z forward (the same
    // space as GreyboxRig.Strike). The rig points the blade along the line from the shoulder to the hand, so
    // raising the hand raises the blade.
    [Serializable]
    public class EnemyPoseSettings
    {
        public const float DefaultWeaponLength = 1.35f;

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

        [Header("Weapon")]
        [Tooltip("Dao / practice stick length from the hand to the tip, in metres. Long enough that the hand doesn't have to float far from the body to show the attacks' reach.")]
        public float WeaponLength = DefaultWeaponLength;

        [Header("Wind-ups: where the sword hand goes (fighter space from the feet: x right, y up, z forward)")]
        public Vector3 SlashWindUp = new Vector3(0.42f, 1.62f, -0.05f);
        public Vector3 OverheadWindUp = new Vector3(0.12f, 2.0f, -0.15f);
        public Vector3 ThrustWindUp = new Vector3(0.22f, 1.42f, 0.12f);
        [Tooltip("Break-out counter: blade swept low and back, so it reads differently from every normal wind-up.")]
        public Vector3 BreakOutWindUp = new Vector3(0.5f, 1.1f, -0.3f);

        [Header("Strikes: where the blade points as the hit lands (x = degrees right of straight ahead, y = degrees below level)")]
        [Tooltip("How far the hand reaches out is worked out from the attack's reach, so the blade tip lands exactly on the edge of what the attack can hit.")]
        public Vector2 SlashStrikeAim = new Vector2(-28f, 8f);
        [Tooltip("Every second strike of a combo swings back the other way.")]
        public Vector2 BackhandStrikeAim = new Vector2(30f, 8f);
        public Vector2 OverheadStrikeAim = new Vector2(3f, 10f);
        [Tooltip("The thrust's reach is longest, so the hand drives furthest out.")]
        public Vector2 ThrustStrikeAim = new Vector2(-3f, 4f);
        [Tooltip("Break-out counter: a wide, low sweep across the body.")]
        public Vector2 BreakOutStrikeAim = new Vector2(-40f, 14f);
        [Tooltip("How far the hand may reach from the shoulder, in metres. If an attack's reach needs more or less, the blade can't match it and a warning says to change WeaponLength.")]
        public float StrikeArmMin = 0.3f;
        public float StrikeArmMax = 1.4f;

        [Header("Body lean in degrees (+ forward, - back)")]
        public float SlashWindUpLean = -5f;
        public float SlashStrikeLean = 5f;
        public float OverheadWindUpLean = -10f;
        public float OverheadStrikeLean = 8f;
        public float ThrustWindUpLean = -9f;
        public float ThrustStrikeLean = 6f;
        public float BreakOutWindUpLean = 10f;        // hunched forward into the shove (the others lean back)
        public float BreakOutStrikeLean = 12f;

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
