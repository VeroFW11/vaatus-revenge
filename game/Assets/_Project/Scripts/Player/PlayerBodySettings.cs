using System;
using UnityEngine;

namespace VaatusRevenge
{
    // The body PlayerController.Spawn builds: capsule size, walking limits, where enemies aim, colour and
    // on-screen name. Only read when the player is built (by the sandbox builder), so rebuild the sandbox
    // after changing these. Every fighter shares the same size so hits, lock-on and the arena's stairs agree.
    [Serializable]
    public class PlayerBodySettings
    {
        [Tooltip("Name shown on screen. Lore names live in data, never in code.")]
        public string DisplayName = "Avatar";
        [Tooltip("Tunic colour (deep crimson, as on the chosen character sheet; the rest of the outfit is BodyLook.Player).")]
        public Color BodyColor = new Color(0.5f, 0.1f, 0.15f);
        [Tooltip("Body height in metres. The pivot is at the feet, so the capsule's centre is at half this height.")]
        public float Height = 1.8f;
        [Tooltip("Body radius in metres: used for walls, hit detection and how close lunges stop.")]
        public float Radius = 0.4f;
        [Tooltip("Tallest step the player walks up without jumping, in metres (the arena stairs are built for 0.3).")]
        public float StepOffset = 0.3f;
        [Tooltip("Steepest slope the player can walk up, in degrees.")]
        public float SlopeLimit = 50f;
        [Tooltip("Height of the aim point (chest) that lock-on markers and enemy bolts aim at, in metres.")]
        public float AimPointHeight = 1.3f;
    }
}
