using System;
using System.Collections.Generic;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // One fighter that could be locked onto, as seen this frame. The Unity side fills these in from
    // Combatant.All (with a physics line-of-sight test), so the choosing rules below need no Unity.
    public struct LockOnCandidate
    {
        public int Id;               // Combatant.Id
        public Vector3 Point;        // its aim point (chest), world space
        public bool IsAlive;
        public bool HasLineOfSight;  // no wall between the player (or camera) and the aim point
    }

    public enum LockOnBreakReason { None, TargetDead, TooFar, LostSight }

    // Lock-on rules: which enemy to pick when lock-on is pressed, which one a left/right switch moves to,
    // and when an existing lock should break.
    public sealed class LockOnSelector
    {
        LockOnTuning tuning;
        float timeWithoutSight;

        public LockOnSelector(LockOnTuning tuning)
        {
            Tuning = tuning;
        }

        public LockOnTuning Tuning
        {
            get { return tuning; }
            set { tuning = value ?? new LockOnTuning(); }
        }

        // How long the current target has been hidden behind walls.
        public float TimeWithoutSight => timeWithoutSight;

        // Returns the index of the best candidate to lock onto, or -1 if none qualifies.
        // Candidates must be alive, visible, within AcquireRange of the player and within AcquireMaxAngle of the
        // centre of the screen. Among those, being near the centre wins, with distance as a secondary factor
        // (the player usually points the camera at the enemy they mean).
        public int PickInitial(IReadOnlyList<LockOnCandidate> candidates, Vector3 playerPoint, Vector3 cameraPosition,
            Vector3 cameraForward)
        {
            if (candidates == null) return -1;
            Vector3 forward = Directions.SafeNormalize(cameraForward, new Vector3(0f, 0f, 1f));
            float range = Math.Max(0f, tuning.AcquireRange);
            float maxAngle = Angles.Clamp(tuning.AcquireMaxAngle, 0f, 180f);

            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                LockOnCandidate candidate = candidates[i];
                if (!candidate.IsAlive || !candidate.HasLineOfSight || !CameraMath.IsFinite(candidate.Point)) continue;

                float distance = Vector3.Distance(playerPoint, candidate.Point);
                if (!(distance <= range)) continue;

                float angle = AngleFromForward(candidate.Point - cameraPosition, forward);
                if (angle > maxAngle) continue;

                float score = tuning.CentreWeight * angle / Math.Max(1f, maxAngle)
                              + tuning.DistanceWeight * distance / Math.Max(0.01f, range);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        // Returns the index of the candidate to switch to, or -1 to keep the current target.
        // direction: +1 = right, -1 = left, as seen on screen from a camera facing cameraYaw. Picks the
        // candidate nearest to the current target on that side (smallest sideways step), like Elden Ring.
        // currentPoint is the current target's aim point (used even if that target just died).
        public int PickNext(IReadOnlyList<LockOnCandidate> candidates, int currentId, Vector3 currentPoint, int direction,
            Vector3 playerPoint, Vector3 cameraPosition, float cameraYaw)
        {
            if (candidates == null || direction == 0 || !CameraMath.IsFinite(cameraYaw)) return -1;
            int side = direction > 0 ? 1 : -1;
            float range = Math.Max(0f, tuning.AcquireRange);
            float maxAngle = Angles.Clamp(tuning.SwitchMaxAngle, 0f, 180f);
            float currentAngle = CameraMath.IsFinite(currentPoint) ? ScreenAngle(currentPoint - cameraPosition, cameraYaw) : 0f;

            int best = -1;
            float bestStep = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                LockOnCandidate candidate = candidates[i];
                if (candidate.Id == currentId || !candidate.IsAlive || !candidate.HasLineOfSight) continue;
                if (!CameraMath.IsFinite(candidate.Point)) continue;
                if (!(Vector3.Distance(playerPoint, candidate.Point) <= range)) continue;

                Vector3 fromCamera = candidate.Point - cameraPosition;
                if (CameraMath.HorizontalLength(fromCamera) < 1e-3f) continue; // right on top of the camera: no side

                float angle = ScreenAngle(fromCamera, cameraYaw);
                if (Math.Abs(angle) > maxAngle) continue;

                float step = (angle - currentAngle) * side; // positive = on the requested side
                if (step <= 0f) continue;
                if (step < bestStep)
                {
                    bestStep = step;
                    best = i;
                }
            }
            return best;
        }

        // Call every frame while locked on. deltaTime is game time: the line-of-sight grace period is a game
        // rule, so it pauses during hitstop and stretches in slow motion like everything else.
        public LockOnBreakReason UpdateBreak(bool targetAlive, bool hasLineOfSight, float distance, float deltaTime)
        {
            if (!targetAlive) return LockOnBreakReason.TargetDead;
            if (!(distance <= Math.Max(0f, tuning.BreakRange))) return LockOnBreakReason.TooFar; // NaN counts as too far

            if (hasLineOfSight) timeWithoutSight = 0f;
            else timeWithoutSight += CameraMath.SafeDeltaTime(deltaTime);

            if (timeWithoutSight > Math.Max(0f, tuning.LineOfSightGraceTime)) return LockOnBreakReason.LostSight;
            return LockOnBreakReason.None;
        }

        // Call whenever a new target is locked.
        public void ResetBreakTimer()
        {
            timeWithoutSight = 0f;
        }

        // Degrees between the camera's forward and the direction to a point (0 = dead centre of the screen).
        static float AngleFromForward(Vector3 fromCamera, Vector3 forward)
        {
            Vector3 direction = Directions.SafeNormalize(fromCamera, forward);
            float cos = Angles.Clamp(Vector3.Dot(direction, forward), -1f, 1f);
            return (float)Math.Acos(cos) * Directions.Rad2Deg;
        }

        // Horizontal angle of a point relative to the camera's facing: negative = left of centre, positive = right.
        static float ScreenAngle(Vector3 fromCamera, float cameraYaw)
        {
            return Angles.Delta(cameraYaw, Directions.YawOf(fromCamera, cameraYaw));
        }
    }
}
