using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Pure geometry for "did this attack reach that fighter?". Fighters are treated as upright
    // capsules (a vertical line from feet to head, plus a radius). The Unity hit system adds the
    // line-of-sight check against walls; everything here is testable without Unity.
    public static class HitGeometry
    {
        // Is a fighter inside a horizontal arc in front of the attacker?
        //   origin/forward: where the strike starts and which way it faces (forward is flattened).
        //   range: reach in metres, measured to the target's surface (target radius is added).
        //   arcDegrees: full width of the swing (e.g. 90 = 45 degrees either side). 360 = all round.
        //   targetFeet/targetHeight: the fighter's capsule. The strike must overlap it vertically
        //   within verticalReach metres above/below the origin.
        public static bool InArc(Vector3 origin, Vector3 forward, float range, float arcDegrees, float verticalReach,
            Vector3 targetFeet, float targetHeight, float targetRadius)
        {
            Vector3 toTarget = Directions.Flatten(targetFeet - origin);
            float distance = toTarget.Length();
            if (distance > range + targetRadius) return false;

            float bottom = targetFeet.Y;
            float top = targetFeet.Y + targetHeight;
            if (origin.Y + verticalReach < bottom || origin.Y - verticalReach > top) return false;

            if (arcDegrees >= 359.9f || distance < 1e-4f) return true;
            Vector3 flatForward = Directions.SafeNormalize(Directions.Flatten(forward), new Vector3(0f, 0f, 1f));
            float angle = (float)Math.Acos(Angles.Clamp(Vector3.Dot(flatForward, toTarget / distance), -1f, 1f)) * Directions.Rad2Deg;
            // Let the swing clip the edge of a wide target instead of needing its centre.
            float edgeAllowance = (float)Math.Atan2(targetRadius, Math.Max(distance, 1e-4f)) * Directions.Rad2Deg;
            return angle <= arcDegrees * 0.5f + edgeAllowance;
        }

        // Does a sphere moving from 'from' to 'to' (e.g. a projectile this frame) touch a fighter's capsule?
        // Returns the fraction 0..1 along the path of the first contact, or -1 for a miss.
        public static float SweepSphereVsCapsule(Vector3 from, Vector3 to, float sphereRadius,
            Vector3 targetFeet, float targetHeight, float targetRadius)
        {
            float combined = sphereRadius + targetRadius;
            Vector3 capBottom = targetFeet + new Vector3(0f, targetRadius, 0f);
            Vector3 capTop = targetFeet + new Vector3(0f, Math.Max(targetRadius, targetHeight - targetRadius), 0f);

            // Sample the path finely enough that nothing tunnels through (step <= a quarter of the combined radius).
            float pathLength = (to - from).Length();
            int steps = Math.Max(1, (int)Math.Ceiling(pathLength / Math.Max(0.01f, combined * 0.25f)));
            steps = Math.Min(steps, 256);
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector3 p = Vector3.Lerp(from, to, t);
                if (DistanceToSegment(p, capBottom, capTop) <= combined) return t;
            }
            return -1f;
        }

        // Shortest distance from a point to a line segment.
        public static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lengthSq = ab.LengthSquared();
            if (lengthSq < 1e-10f) return (point - a).Length();
            float t = Angles.Clamp(Vector3.Dot(point - a, ab) / lengthSq, 0f, 1f);
            return (point - (a + ab * t)).Length();
        }

        // Horizontal distance between two fighters' surfaces (negative when overlapping).
        public static float SurfaceDistance(Vector3 feetA, float radiusA, Vector3 feetB, float radiusB)
        {
            return Directions.Flatten(feetB - feetA).Length() - radiusA - radiusB;
        }
    }
}
