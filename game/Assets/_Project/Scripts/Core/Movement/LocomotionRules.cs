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

    public static class MotionCurves
    {
        // Share of a dash or lunge covered at progress u (0..1). ease 0 = constant speed, 1 = quadratic
        // ease-out (fast start, slows to a stop). Always 0 at u = 0 and exactly 1 at u = 1, so the total
        // distance is exact at any frame rate when each frame moves by the difference between two samples.
        public static float EaseOut(float u, float ease)
        {
            u = Angles.Clamp(u, 0f, 1f);
            float eased = 1f - (1f - u) * (1f - u);
            float k = Angles.Clamp(ease, 0f, 1f);
            return u + (eased - u) * k;
        }

        // Progress of a window [start, end] at time t, eased. Zero-length windows jump straight to 1.
        public static float WindowProgress(float t, float start, float end, float ease)
        {
            if (t <= start) return end <= start && t >= start ? 1f : 0f;
            if (end <= start || t >= end) return 1f;
            return EaseOut((t - start) / (end - start), ease);
        }
    }

    // A push that covers Distance along Direction over Duration (knockback). Step returns this frame's
    // displacement; summed over the push it equals exactly Distance.
    public struct PushMotion
    {
        public Vector3 Direction;
        public float Distance;
        public float Duration;
        public float Elapsed;

        public bool IsActive => Distance > 0f && Elapsed < Duration;

        public void Start(Vector3 direction, float distance, float duration)
        {
            Direction = Directions.SafeNormalize(Directions.Flatten(direction), Vector3.Zero);
            Distance = Direction == Vector3.Zero ? 0f : Math.Max(0f, distance);
            Duration = Math.Max(1e-4f, duration);
            Elapsed = 0f;
        }

        public void Stop()
        {
            Distance = 0f;
            Elapsed = 0f;
        }

        public Vector3 Step(float dt)
        {
            if (!IsActive || !(dt > 0f)) return Vector3.Zero;
            float before = MotionCurves.EaseOut(Elapsed / Duration, 1f);
            Elapsed = Math.Min(Duration, Elapsed + dt);
            float after = MotionCurves.EaseOut(Elapsed / Duration, 1f);
            return Direction * (Distance * (after - before));
        }
    }
}
