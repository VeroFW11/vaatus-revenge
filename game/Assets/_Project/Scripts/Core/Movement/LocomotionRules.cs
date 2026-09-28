using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Small, frame-rate independent movement rules shared by the player and enemies.
    public static class LocomotionRules
    {
        // Moves a horizontal velocity toward the desired one. Speeding up uses 'accel'; slowing down or
        // reversing uses 'decel' (usually higher, so stopping and turning feel tight).
        public static Vector3 Accelerate(Vector3 current, Vector3 desired, float accel, float decel, float dt)
        {
            if (!(dt > 0f)) return current;
            current = Directions.Flatten(current);
            desired = Directions.Flatten(desired);
            float rate = desired.LengthSquared() > current.LengthSquared() ? accel : decel;
            return Smooth.MoveTowards(current, desired, Math.Max(0f, rate) * dt);
        }

        // Turns a yaw toward a target yaw by at most rate * dt degrees, the short way round.
        public static float Turn(float yaw, float targetYaw, float degreesPerSecond, float dt)
        {
            if (!(dt > 0f)) return yaw;
            return Angles.Wrap180(Angles.MoveTowards(yaw, targetYaw, Math.Max(0f, degreesPerSecond) * dt));
        }

        // Launch speed that reaches 'height' under 'gravity' (v = sqrt(2gh)).
        public static float JumpSpeed(float height, float gravity)
        {
            if (!(height > 0f) || !(gravity > 0f)) return 0f;
            return (float)Math.Sqrt(2.0 * gravity * height);
        }

        public static float ApplyGravity(float verticalVelocity, float gravity, float maxFallSpeed, float dt)
        {
            if (!(dt > 0f)) return verticalVelocity;
            verticalVelocity -= Math.Max(0f, gravity) * dt;
            if (maxFallSpeed > 0f && verticalVelocity < -maxFallSpeed) verticalVelocity = -maxFallSpeed;
            return verticalVelocity;
        }

        // Walk below the threshold, run above it, nothing inside the deadzone.
        public static float StickSpeed(float stickMagnitude, float deadzone, float walkThreshold, float walkSpeed, float runSpeed)
        {
            if (stickMagnitude <= deadzone) return 0f;
            return stickMagnitude < walkThreshold ? walkSpeed : runSpeed;
        }
    }
}
