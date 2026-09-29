using System;

namespace VaatusRevenge.Core
{
    // Angle helpers in degrees, matching Unity's Mathf versions.
    public static class Angles
    {
        // Wraps to the range (-180, 180].
        public static float Wrap180(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            else if (degrees <= -180f) degrees += 360f;
            return degrees;
        }

        // Shortest signed difference from current to target, in (-180, 180]. Same as Mathf.DeltaAngle.
        // Use this for any turning, or a character at 179 degrees turning to -179 spins the long way round.
        public static float Delta(float current, float target)
        {
            return Wrap180(target - current);
        }

        // Rotates current towards target by at most maxDelta degrees, the short way round.
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            float delta = Delta(current, target);
            if (Math.Abs(delta) <= maxDelta) return current + delta;
            return current + Math.Sign(delta) * maxDelta;
        }

        public static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
