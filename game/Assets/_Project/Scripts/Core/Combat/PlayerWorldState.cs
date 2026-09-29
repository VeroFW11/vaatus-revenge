using System.Numerics;

namespace VaatusRevenge.Core
{
    // What the Unity side tells the player model about the world each frame. The model never looks at
    // the scene itself, which is what lets the headless harness run it with made-up worlds.
    public struct PlayerWorldState
    {
        public Vector3 Position;            // player's feet
        public bool Grounded;               // CharacterController.isGrounded after last frame's Move
        public float CameraYaw;             // degrees; the move stick is relative to this
        public float SelfRadius;            // player body radius (lunges stop short of targets using it)
        public float SelfHeight;            // player body height (perfect-dodge checks use it; <= 0 = only reach from below is checked)
        public float RealDeltaTime;         // real (unscaled) seconds this frame. The heavy's charge clock runs on it, so a
                                            // practised hold feels the same in slow motion. <= 0 = use the game dt

        public bool HasLockTarget;          // locked on (the lock-on controller decides)
        public Vector3 LockTargetPosition;  // target's feet
        public Vector3 LockTargetAimPoint;  // target's chest: projectiles aim here
        public float LockTargetRadius;

        // Not locked on: the best soft-lock candidate, chosen with SoftLockSelector (nearest enemy within
        // SoftLockRange and SoftLockAngle of PlayerCombatModel.GetAimYaw). Attacks turn toward it.
        public bool HasSoftTarget;
        public Vector3 SoftTargetPosition;
        public Vector3 SoftTargetAimPoint;
        public float SoftTargetRadius;

        // The nearest living enemy (any distance; the model applies MomentumSettings.BackOffRadius). Backing away
        // from it drains Momentum even when not locked on.
        public bool HasNearestEnemy;
        public Vector3 NearestEnemyPosition;
    }
}
