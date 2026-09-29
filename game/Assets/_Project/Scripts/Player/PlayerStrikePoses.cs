using System;
using UnityEngine;

namespace VaatusRevenge
{
    // How the player's fire follows the martial arts. The body's poses themselves now come from the procedural
    // animator's pose library (Core/Animation, timed to each move's frame data); these settings place the fire on
    // top of them. (The class keeps its old name so tuning assets that already have a "Poses" block still load.)
    [Serializable]
    public class PlayerStrikePoses
    {
        [Header("Fire on the striking limb")]
        [Tooltip("Flames lick off the striking fist or foot from the start of a move until its active frames end.")]
        public bool LimbFlames = true;
        [Tooltip("Moves that throw fire of their own (cone, whip, wheel...) keep their limb flame this much shorter, so the big effect reads.")]
        [Range(0f, 1f)] public float BigEffectLimbFlameShare = 0.6f;

        [Header("Launcher")]
        [Tooltip("The Rising Dragon Kick's column of fire rises this high (metres) at this share of its reach in front of you.")]
        public float PillarHeight = 2.6f;
        [Range(0f, 1f)] public float PillarReachShare = 0.6f;

        [Header("Fire Whip")]
        [Tooltip("The lash keeps sweeping this long after the active frames end (seconds), then thins away.")]
        public float WhipLinger = 0.1f;

        [Header("Fire jets from the feet")]
        [Tooltip("Air dashes and the zip strike's flight are pushed by jets of fire from both feet.")]
        public bool FootJets = true;
    }
}
