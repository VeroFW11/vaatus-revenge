using System;
using UnityEngine;

namespace VaatusRevenge
{
    // Enemy body settings that aren't animation keyframes (those live in the pose library, with every attack's
    // wind-up held for its whole telegraph and each swing landing on its hit window). Purely visual.
    //
    // Fairness: the dao is drawn WeaponLength long, and the sword clips swing it at full arm's length, so when a
    // strike lands the blade tip sits on the edge of what the attack can hit (EnemyTuning's SwordReach, 2.1 m from
    // the soldier's centre). The CombatSim `anim` scenario prints the measured tip distance for every sword
    // strike: if you change an attack's Range, check it there and adjust this length.
    [Serializable]
    public class EnemyPoseSettings
    {
        public const float DefaultWeaponLength = 1.3f;

        [Tooltip("Dao / practice stick length from the hand to the tip, in metres (matches the soldier's reach).")]
        public float WeaponLength = DefaultWeaponLength;

        [Tooltip("The crossbow tilts up or down toward the player while aiming (e.g. from the raised platform), at most this many degrees.")]
        public float MaxAimPitch = 50f;
    }
}
