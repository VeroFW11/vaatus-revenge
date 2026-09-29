using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // World-direction helpers that follow Unity's conventions exactly, so core maths and Unity
    // transforms always agree:
    //   +Y is up, +Z is forward, +X is right (left-handed).
    //   Yaw is degrees clockwise from +Z when seen from above: yaw 90 faces +X.
    //   Pitch is degrees downward: pitch 30 looks down, pitch -30 looks up.
    //   Same result as Unity's Quaternion.Euler(pitch, yaw, 0) * Vector3.forward.
    // Don't use System.Numerics.Quaternion/Matrix4x4 rotation helpers in the core: they assume the
    // opposite rotation direction, which silently mirrors cameras and movement.
    public static class Directions
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);

        // Horizontal forward for a yaw.
        public static Vector3 FromYaw(float yawDegrees)
        {
            float r = yawDegrees * Deg2Rad;
            return new Vector3((float)Math.Sin(r), 0f, (float)Math.Cos(r));
        }

        // Horizontal right for a yaw (forward rotated 90 degrees clockwise).
        public static Vector3 RightFromYaw(float yawDegrees)
        {
            float r = yawDegrees * Deg2Rad;
            return new Vector3((float)Math.Cos(r), 0f, -(float)Math.Sin(r));
        }

        // Full look direction for a yaw and a downward pitch.
        public static Vector3 FromYawPitch(float yawDegrees, float pitchDegrees)
        {
            float y = yawDegrees * Deg2Rad;
            float p = pitchDegrees * Deg2Rad;
            float cp = (float)Math.Cos(p);
            return new Vector3((float)Math.Sin(y) * cp, -(float)Math.Sin(p), (float)Math.Cos(y) * cp);
        }

        // Yaw of a direction (ignores height). Returns fallbackYaw when the direction has no
        // horizontal length (e.g. straight up), instead of producing garbage.
        public static float YawOf(Vector3 direction, float fallbackYaw = 0f)
        {
            if (direction.X * direction.X + direction.Z * direction.Z < 1e-8f) return fallbackYaw;
            return (float)Math.Atan2(direction.X, direction.Z) * Rad2Deg;
        }

        // Downward pitch of a direction. Returns 0 for a zero vector.
        public static float PitchOf(Vector3 direction)
        {
            float horizontal = (float)Math.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
            if (horizontal < 1e-6f && Math.Abs(direction.Y) < 1e-6f) return 0f;
            return (float)Math.Atan2(-direction.Y, horizontal) * Rad2Deg;
        }

        public static Vector3 Flatten(Vector3 v)
        {
            return new Vector3(v.X, 0f, v.Z);
        }

        // Normalises, or returns fallback when the vector is (nearly) zero. Never returns NaN.
        public static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        {
            float lengthSq = v.LengthSquared();
            if (lengthSq < 1e-10f) return fallback;
            return v / (float)Math.Sqrt(lengthSq);
        }

        // Turns a move stick into a world-space horizontal direction relative to the camera's yaw.
        // Keeps the stick's magnitude (so half-tilt walks) but never exceeds 1.
        // Uses only the camera's yaw, so looking straight down never breaks movement.
        public static Vector3 CameraRelative(Vector2 stick, float cameraYawDegrees)
        {
            Vector3 world = RightFromYaw(cameraYawDegrees) * stick.X + FromYaw(cameraYawDegrees) * stick.Y;
            float lengthSq = world.LengthSquared();
            if (lengthSq > 1f) world /= (float)Math.Sqrt(lengthSq);
            return world;
        }
    }
}
