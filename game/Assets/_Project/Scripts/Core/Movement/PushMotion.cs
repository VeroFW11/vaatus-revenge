using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
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
