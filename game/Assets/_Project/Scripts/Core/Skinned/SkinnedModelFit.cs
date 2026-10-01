using System;
using System.Collections.Generic;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // How to stand an imported character model where the procedural fighter stands: upright, facing the same way,
    // the same height and with its feet on the ground. Models made outside Unity arrive any which way (a raw Blender
    // export lies on its back with Z up, a generated one may face the camera along -Z, sizes are whatever the tool
    // chose), so this measures the model's own skeleton instead of trusting its import settings.
    //
    //   Up       = the world axis nearest to "hips to head" (snapped, so a model that leans a little isn't tilted).
    //   Facing   = from the hips: the model's right leg is on its right, so forward = right x up. A facing within
    //              SnapDegrees of a quarter turn is snapped to it, so a slightly lopsided rig isn't turned a few
    //              degrees; anything else is used exactly.
    //   Height   = matched at the head joint (the base of the skull), so hair, hats and topknots don't skew it.
    //   Ground   = the lowest point of the mesh sits at y = 0, the hips centred over the origin.
    //
    // All positions are in the space the model is placed in (its container, whose origin is the fighter's feet).
    // Applying the result is p' = Translation + Scale * Rotate(p). It's idempotent: fitting a fitted model again
    // returns no change, so it's safe to run in edit mode (for a preview) and again on Play.
    public struct SkinnedFitResult
    {
        public Quaternion Rotation;
        public float Scale;
        public Vector3 Translation;
        public float FacingDegrees;     // the yaw that was taken out (0 = the model already faced +Z)
        public bool UpWasSnapped;        // the model wasn't Y-up (e.g. a Z-up export)

        public static SkinnedFitResult Identity => new SkinnedFitResult { Rotation = Quaternion.Identity, Scale = 1f };

        public Vector3 Apply(Vector3 p)
        {
            return Translation + Scale * Vector3.Transform(p, Rotation);
        }

        // True when applying it would visibly change nothing (within a tenth of a degree, a millimetre, 0.1 %).
        public bool IsIdentity
        {
            get
            {
                float dot = Math.Abs(Quaternion.Dot(Rotation, Quaternion.Identity));
                return dot > 0.9999996f && Math.Abs(Scale - 1f) < 1e-3f && Translation.Length() < 1e-3f;
            }
        }
    }

    public static class SkinnedModelFit
    {
        // A model facing within this many degrees of a quarter turn faces exactly that way (rigs are rarely
        // perfectly symmetric, and a few degrees of accidental turn reads as a fighter looking past the enemy).
        public const float SnapDegrees = 12f;
        const float Epsilon = 1e-5f;

        static readonly Vector3[] axes =
        {
            Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ,
        };

        // hips, head, leftUpperLeg, rightUpperLeg: those joints in the rest pose. surface: points on or around the
        // mesh (its bounds' corners are enough); the lowest one becomes the ground. targetHeadHeight: how high the
        // procedural body's head joint is above its feet; heightScale multiplies the matched size (1 = same).
        public static SkinnedFitResult Compute(Vector3 hips, Vector3 head, Vector3 leftUpperLeg, Vector3 rightUpperLeg,
            IReadOnlyList<Vector3> surface, float targetHeadHeight, float heightScale)
        {
            SkinnedFitResult result = SkinnedFitResult.Identity;

            // Up: the world axis closest to hips -> head.
            Vector3 up = Vector3.UnitY;
            Vector3 spine = head - hips;
            if (spine.LengthSquared() > Epsilon)
            {
                float best = float.NegativeInfinity;
                foreach (Vector3 axis in axes)
                {
                    float d = Vector3.Dot(spine, axis);
                    if (d > best) { best = d; up = axis; }
                }
            }
            Quaternion toUp = FromTo(up, Vector3.UnitY);
            result.UpWasSnapped = up != Vector3.UnitY;

            // Facing: forward = right x up, measured after standing the model up.
            Vector3 right = Vector3.Transform(rightUpperLeg - leftUpperLeg, toUp);
            right.Y = 0f;
            float yaw = 0f;
            if (right.LengthSquared() > Epsilon)
            {
                right = Vector3.Normalize(right);
                Vector3 forward = Vector3.Cross(right, Vector3.UnitY);
                yaw = RadToDeg((float)Math.Atan2(forward.X, forward.Z));
                float quarter = (float)Math.Round(yaw / 90f) * 90f;
                if (Math.Abs(Angles.Delta(quarter, yaw)) <= SnapDegrees) yaw = quarter;
                yaw = Angles.Wrap180(yaw);
            }
            result.FacingDegrees = yaw;
            Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, DegToRad(-yaw));
            result.Rotation = Quaternion.Normalize(Quaternion.Concatenate(toUp, turn));   // stand up, then turn

            // Height, matched at the head joint above the lowest point of the mesh.
            Vector3 hipsR = Vector3.Transform(hips, result.Rotation);
            Vector3 headR = Vector3.Transform(head, result.Rotation);
            float ground = LowestY(surface, result.Rotation, Math.Min(hipsR.Y, headR.Y));
            float headHeight = headR.Y - ground;
            float scale = 1f;
            if (headHeight > Epsilon && targetHeadHeight > Epsilon) scale = targetHeadHeight / headHeight;
            if (heightScale > Epsilon) scale *= heightScale;
            result.Scale = scale;

            // Feet on the ground, hips over the origin.
            result.Translation = new Vector3(-hipsR.X * scale, -ground * scale, -hipsR.Z * scale);
            return result;
        }

        static float LowestY(IReadOnlyList<Vector3> points, Quaternion rotation, float fallback)
        {
            if (points == null || points.Count == 0) return fallback;
            float lowest = float.PositiveInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                float y = Vector3.Transform(points[i], rotation).Y;
                if (y < lowest) lowest = y;
            }
            return lowest;
        }

        // The shortest rotation taking unit vector 'from' onto unit vector 'to' (any half turn for opposites).
        public static Quaternion FromTo(Vector3 from, Vector3 to)
        {
            float dot = Vector3.Dot(from, to);
            if (dot > 1f - Epsilon) return Quaternion.Identity;
            if (dot < -1f + Epsilon)
            {
                Vector3 axis = Vector3.Cross(Vector3.UnitX, from);
                if (axis.LengthSquared() < Epsilon) axis = Vector3.Cross(Vector3.UnitZ, from);
                return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), (float)Math.PI);
            }
            Vector3 cross = Vector3.Cross(from, to);
            return Quaternion.Normalize(new Quaternion(cross, 1f + dot));
        }

        static float DegToRad(float degrees) => degrees * ((float)Math.PI / 180f);
        static float RadToDeg(float radians) => radians * (180f / (float)Math.PI);
    }
}
