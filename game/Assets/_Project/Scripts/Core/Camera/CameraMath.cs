using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Small guards shared by the camera and lock-on code. One NaN reaching a camera transform turns the
    // whole screen black, so anything that comes from outside (input, positions, tuning) is checked first.
    internal static class CameraMath
    {
        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool IsFinite(Vector2 v)
        {
            return IsFinite(v.X) && IsFinite(v.Y);
        }

        public static bool IsFinite(Vector3 v)
        {
            return IsFinite(v.X) && IsFinite(v.Y) && IsFinite(v.Z);
        }

        public static float HorizontalLength(Vector3 v)
        {
            return (float)Math.Sqrt(v.X * v.X + v.Z * v.Z);
        }

        // A time step that is safe to integrate with: negative or NaN becomes 0 (nothing moves).
        public static float SafeDeltaTime(float deltaTime)
        {
            return deltaTime > 0f && !float.IsInfinity(deltaTime) ? deltaTime : 0f;
        }
    }
}
