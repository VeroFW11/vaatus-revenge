using System.Numerics;

namespace VaatusRevenge.Core
{
    // What an enemy brain wants this frame: move the CharacterController by Velocity * deltaTime, turn to
    // FacingYaw, handle Events (valid until the brain's next Tick).
    public struct EnemyTickResult
    {
        public Vector3 Velocity;
        public float FacingYaw;
        public EventList<EnemyEvent> Events;
    }
}
