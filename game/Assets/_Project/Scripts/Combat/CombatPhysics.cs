using UnityEngine;

namespace VaatusRevenge
{
    // Environment queries for the hit system: "is there a wall in the way?". They only look at solid world
    // geometry (Layers.EnvironmentMask) and ignore trigger volumes. As a safety net they also skip any
    // fighter collider, so a fighter accidentally left on the Default layer can't block every hit on itself.
    // Buffers are reused, so none of this allocates memory per frame.
    public static class CombatPhysics
    {
        const int BufferSize = 16;
        static readonly RaycastHit[] hits = new RaycastHit[BufferSize];
        static readonly Collider[] overlaps = new Collider[BufferSize];

        // True when solid geometry blocks the straight line between two points.
        public static bool IsBlocked(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-4f) return false;
            int count = Physics.RaycastNonAlloc(from, delta / distance, hits, distance, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (!IsFighterCollider(hits[i].collider)) return true;
            }
            return false;
        }

        // Nearest solid geometry a sphere moving from origin along direction would touch within distance.
        // Like Physics.SphereCast it misses geometry the sphere already overlaps at the start (see IsOverlapping).
        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, float distance, out RaycastHit nearest)
        {
            nearest = default;
            if (distance <= 0f || direction.sqrMagnitude < 1e-8f) return false;
            int count = Physics.SphereCastNonAlloc(origin, radius, direction.normalized, hits, distance, Layers.EnvironmentMask,
                QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (IsFighterCollider(hit.collider)) continue;
                // A zero distance with a zero point means "overlapping at the start", which a cast can't place.
                if (hit.distance <= 0f && hit.point == Vector3.zero) continue;
                if (!found || hit.distance < nearest.distance)
                {
                    nearest = hit;
                    found = true;
                }
            }
            return found;
        }

        // Distance from a fighter's feet straight down to solid floor, within maxDistance (the animator's legs reach for the
        // floor as a fall nears it, J6-01). The ray starts a little above the feet so a foot resting on the floor still
        // finds it; fighter colliders are skipped.
        public static bool FloorBelow(Vector3 feet, float maxDistance, out float distance)
        {
            const float lift = 0.05f;
            distance = 0f;
            if (!(maxDistance > 0f)) return false;
            int count = Physics.RaycastNonAlloc(feet + Vector3.up * lift, Vector3.down, hits, maxDistance + lift, Layers.EnvironmentMask,
                QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (IsFighterCollider(hits[i].collider)) continue;
                if (hits[i].distance < best) best = hits[i].distance;
            }
            if (best == float.MaxValue) return false;
            distance = Mathf.Max(0f, best - lift);
            return true;
        }

        // True when a sphere at this position already overlaps solid geometry (e.g. launched inside a wall).
        public static bool IsOverlapping(Vector3 position, float radius)
        {
            int count = Physics.OverlapSphereNonAlloc(position, radius, overlaps, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (!IsFighterCollider(overlaps[i])) return true;
            }
            return false;
        }

        public static bool IsFighterCollider(Collider collider)
        {
            if (collider == null) return true;
            return collider is CharacterController || collider.GetComponentInParent<Combatant>() != null;
        }
    }
}
