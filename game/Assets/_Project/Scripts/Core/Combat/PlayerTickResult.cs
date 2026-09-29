using System.Numerics;

namespace VaatusRevenge.Core
{
    // What the player model wants this frame. The Unity side moves the CharacterController once by
    // Velocity * deltaTime, turns the body to FacingYaw and handles the events.
    public struct PlayerTickResult
    {
        public Vector3 Velocity;            // m/s. X/Z from locomotion, dodges, lunges, knockback; Y from jump/gravity
        public float FacingYaw;             // degrees, Unity convention (clockwise from +Z)
        public EventList<PlayerEvent> Events;  // valid until the next Tick
    }
}
