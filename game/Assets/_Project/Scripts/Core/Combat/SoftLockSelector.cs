using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Soft lock: when you're not locked on, attacks still aim at the enemy you're obviously going for,
    // i.e. the nearest one within range and not too far off the direction you're aiming. The Unity side
    // scores each candidate with this and puts the best in PlayerWorldState.SoftTarget*.
    public static class SoftLockSelector
    {
        // Returns false when the candidate isn't eligible. Lower score = better (distance, nudged by angle).
        public static bool TryScore(Vector3 self, float aimYaw, Vector3 candidate, float range, float maxAngle, out float score)
        {
            score = float.MaxValue;
            Vector3 offset = Directions.Flatten(candidate - self);
            float distance = offset.Length();
            if (!(range > 0f) || distance > range) return false;
            float angle = distance < 1e-4f ? 0f : Math.Abs(Angles.Delta(aimYaw, Directions.YawOf(offset, aimYaw)));
            if (angle > maxAngle) return false;
            // Mostly nearest-first; the angle term breaks near-ties in favour of the one you're facing.
            score = distance + angle / 180f;
            return true;
        }

        public static bool TryScore(Vector3 self, float aimYaw, Vector3 candidate, PlayerTuning tuning, out float score)
        {
            if (tuning == null)
            {
                score = float.MaxValue;
                return false;
            }
            return TryScore(self, aimYaw, candidate, tuning.SoftLockRange, tuning.SoftLockAngle, out score);
        }
    }
}
