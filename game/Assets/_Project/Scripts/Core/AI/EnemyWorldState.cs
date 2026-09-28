using System.Numerics;

namespace VaatusRevenge.Core
{
    // What the Unity side tells an enemy brain about the world each frame.
    public struct EnemyWorldState
    {
        public Vector3 Position;          // this enemy's feet
        public bool Grounded;             // CharacterController.isGrounded after last frame's Move
        public float SelfRadius;

        public bool HasTarget;            // the player exists and is alive
        public Vector3 TargetPosition;    // player's feet
        public Vector3 TargetAimPoint;    // player's chest: bolts aim here (Zero = feet + the move's origin height)
        public float TargetRadius;
        public bool TargetHidden;         // a wall blocks line of sight (ranged enemies won't shoot, and reposition)
    }
}
