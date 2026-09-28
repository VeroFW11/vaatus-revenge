using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Frame-rate independent smoothing. Using these instead of "Lerp(a, b, 0.1f)" each frame means the
    // feel is the same at 30, 60 or 144 fps, and the headless harness matches the real game.
    public static class Smooth
    {
        // Critically damped spring, same algorithm as Unity's Mathf.SmoothDamp (Game Programming Gems 4).
        public static float Damp(float current, float target, ref float velocity, float smoothTime, float deltaTime,
            float maxSpeed = float.PositiveInfinity)
        {
            if (deltaTime <= 0f) return current;
            smoothTime = Math.Max(0.0001f, smoothTime);
            float omega = 2f / smoothTime;
            float x = omega * deltaTime;
            float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            float change = current - target;
            float originalTarget = target;
            float maxChange = maxSpeed * smoothTime;
            change = Angles.Clamp(change, -maxChange, maxChange);
            target = current - change;
            float temp = (velocity + omega * change) * deltaTime;
            velocity = (velocity - omega * temp) * exp;
            float output = target + (change + temp) * exp;
            // Don't overshoot.
            if (originalTarget - current > 0f == output > originalTarget)
            {
                output = originalTarget;
                velocity = (output - originalTarget) / deltaTime;
            }
            return output;
        }

        // Damp for angles in degrees: always takes the short way round.
        public static float DampAngle(float current, float target, ref float velocity, float smoothTime, float deltaTime,
            float maxSpeed = float.PositiveInfinity)
        {
            target = current + Angles.Delta(current, target);
            return Damp(current, target, ref velocity, smoothTime, deltaTime, maxSpeed);
        }

        public static Vector3 Damp(Vector3 current, Vector3 target, ref Vector3 velocity, float smoothTime, float deltaTime)
        {
            float vx = velocity.X, vy = velocity.Y, vz = velocity.Z;
            var result = new Vector3(
                Damp(current.X, target.X, ref vx, smoothTime, deltaTime),
                Damp(current.Y, target.Y, ref vy, smoothTime, deltaTime),
                Damp(current.Z, target.Z, ref vz, smoothTime, deltaTime));
            velocity = new Vector3(vx, vy, vz);
            return result;
        }

        // Exponential approach: after halfLife seconds you're half way to the target, whatever the frame rate.
        public static float Towards(float current, float target, float halfLife, float deltaTime)
        {
            if (halfLife <= 0f) return target;
            return target + (current - target) * (float)Math.Pow(2.0, -deltaTime / halfLife);
        }

        public static Vector3 Towards(Vector3 current, Vector3 target, float halfLife, float deltaTime)
        {
            if (halfLife <= 0f) return target;
            return target + (current - target) * (float)Math.Pow(2.0, -deltaTime / halfLife);
        }

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta) return target;
            return current + Math.Sign(target - current) * maxDelta;
        }

        public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDistance)
        {
            Vector3 delta = target - current;
            float distance = delta.Length();
            if (distance <= maxDistance || distance < 1e-6f) return target;
            return current + delta / distance * maxDistance;
        }
    }
}
