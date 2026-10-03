using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Small maths helpers for the procedural animator, in Unity's conventions so poses copy straight across:
    // left-handed, Y up, Z forward, X right; positive yaw turns right, positive pitch tips forward/down.
    // System.Numerics quaternions use the same component layout and the same "a * b applies b first" order
    // as UnityEngine.Quaternion, so a Quaternion(x, y, z, w) here is the same rotation in Unity.
    public static class AnimMath
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);
        const float Epsilon = 1e-6f;

        public static Quaternion AxisAngle(Vector3 axis, float degrees)
        {
            return Quaternion.CreateFromAxisAngle(axis, degrees * Deg2Rad);
        }

        // Same order as UnityEngine.Quaternion.Euler: roll (Z) first, then pitch (X), then yaw (Y).
        public static Quaternion Euler(float pitch, float yaw, float roll)
        {
            Quaternion y = AxisAngle(Vector3.UnitY, yaw);
            Quaternion x = AxisAngle(Vector3.UnitX, pitch);
            Quaternion z = AxisAngle(Vector3.UnitZ, roll);
            return y * x * z;
        }

        public static Quaternion Yaw(float degrees)
        {
            return AxisAngle(Vector3.UnitY, degrees);
        }

        public static Vector3 Rotate(Quaternion q, Vector3 v)
        {
            return Vector3.Transform(v, q);
        }

        // Unit direction from a yaw (0 = +Z, positive = toward +X) and a pitch (positive = up).
        public static Vector3 Direction(float yawDegrees, float pitchDegrees)
        {
            float yaw = yawDegrees * Deg2Rad;
            float pitch = pitchDegrees * Deg2Rad;
            float c = MathF.Cos(pitch);
            return new Vector3(MathF.Sin(yaw) * c, MathF.Sin(pitch), MathF.Cos(yaw) * c);
        }

        // Like Unity's Quaternion.LookRotation: the rotation that turns +Z to forward and +Y (as near as it can) to up.
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            Vector3 z = SafeNormalize(forward, Vector3.UnitZ);
            Vector3 x = Vector3.Cross(up, z);
            if (x.LengthSquared() < Epsilon)
            {
                // up is parallel to forward: pick any perpendicular
                x = Vector3.Cross(MathF.Abs(z.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX, z);
            }
            x = Vector3.Normalize(x);
            Vector3 y = Vector3.Cross(z, x);
            return FromBasis(x, y, z);
        }

        // The rotation whose columns are the given orthonormal axes (where +X, +Y and +Z end up).
        public static Quaternion FromBasis(Vector3 x, Vector3 y, Vector3 z)
        {
            float m00 = x.X, m01 = y.X, m02 = z.X;
            float m10 = x.Y, m11 = y.Y, m12 = z.Y;
            float m20 = x.Z, m21 = y.Z, m22 = z.Z;
            float trace = m00 + m11 + m22;
            Quaternion q;
            if (trace > 0f)
            {
                float s = MathF.Sqrt(trace + 1f) * 2f;
                q = new Quaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s);
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = MathF.Sqrt(1f + m00 - m11 - m22) * 2f;
                q = new Quaternion(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
            }
            else if (m11 > m22)
            {
                float s = MathF.Sqrt(1f + m11 - m00 - m22) * 2f;
                q = new Quaternion((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s);
            }
            else
            {
                float s = MathF.Sqrt(1f + m22 - m00 - m11) * 2f;
                q = new Quaternion((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s);
            }
            return Quaternion.Normalize(q);
        }

        // The rotation that takes a bone's rest frame (its axis and a perpendicular "front") to a new frame.
        public static Quaternion FrameRotation(Vector3 restAxis, Vector3 restFront, Vector3 axis, Vector3 front)
        {
            Quaternion rest = LookRotation(restAxis, restFront);
            Quaternion now = LookRotation(axis, front);
            return Quaternion.Normalize(now * Quaternion.Inverse(rest));
        }

        public static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        {
            float lengthSq = v.LengthSquared();
            if (!(lengthSq > Epsilon) || float.IsInfinity(lengthSq)) return fallback;
            return v / MathF.Sqrt(lengthSq);
        }

        // v with its component along the unit axis removed.
        public static Vector3 Perpendicular(Vector3 v, Vector3 unitAxis)
        {
            return v - unitAxis * Vector3.Dot(v, unitAxis);
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        public static float Clamp01(float x)
        {
            return x < 0f ? 0f : (x > 1f ? 1f : x);
        }

        public static float Clamp(float x, float min, float max)
        {
            return x < min ? min : (x > max ? max : x);
        }

        public static bool IsFinite(float x)
        {
            return !float.IsNaN(x) && !float.IsInfinity(x);
        }

        public static bool IsFinite(Vector3 v)
        {
            return IsFinite(v.X) && IsFinite(v.Y) && IsFinite(v.Z);
        }

        public static bool IsFinite(Quaternion q)
        {
            return IsFinite(q.X) && IsFinite(q.Y) && IsFinite(q.Z) && IsFinite(q.W);
        }

        // Smoothstep: eases in and out.
        public static float SmoothStep(float x)
        {
            x = Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        // Critically damped approach helper: 1 - e^(-rate * dt), safe for any dt.
        public static float ExpBlend(float rate, float dt)
        {
            if (!(dt > 0f) || !(rate > 0f)) return 0f;
            return 1f - MathF.Exp(-rate * dt);
        }
    }

    // How a keyframe eases in from the one before it. Martial arts timing lives in these curves: a strike
    // snaps out fast and settles (Snap), a recovery overshoots a touch and settles back (Back), a wind-up
    // starts slow and speeds up (In).
    public enum PoseEase { Linear, In, Out, InOut, Snap, Back }

    public static class PoseEasing
    {
        public static float Apply(PoseEase ease, float u)
        {
            u = AnimMath.Clamp01(u);
            switch (ease)
            {
                case PoseEase.In: return u * u;
                case PoseEase.Out: return 1f - (1f - u) * (1f - u);
                case PoseEase.InOut: return u * u * (3f - 2f * u);
                case PoseEase.Snap:
                {
                    // ease-out cubic: fast out of the chamber, arriving crisply (a softer quart would look
                    // "already there" several frames before the hit)
                    float inv = 1f - u;
                    return 1f - inv * inv * inv;
                }
                case PoseEase.Back:
                {
                    // overshoots by about 10% and settles back: follow-through
                    const float c1 = 1.70158f;
                    const float c3 = c1 + 1f;
                    float v = u - 1f;
                    return 1f + c3 * v * v * v + c1 * v * v;
                }
                default: return u;
            }
        }
    }
}
