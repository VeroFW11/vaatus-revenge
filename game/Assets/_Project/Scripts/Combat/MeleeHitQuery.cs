using System.Collections.Generic;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // "Who did this attack hit?" for melee swings, landing rings and explosions.
    //
    // Call Arc or Sphere every frame while an attack's active window is open, with the same AttackId
    // (CombatIds.Next() once per swing). Each target is hit at most once per AttackId, however many frames it
    // stays inside the swing. Each hit target's IDamageReceiver decides the outcome (hit, blocked, dodged,
    // deflected...) and the attacker gets a HitReport per target to react to (hitstop, sparks, getting
    // staggered by a deflect). Call EndAttack when the active window closes; if that's forgotten, old records
    // are pruned automatically after a while.
    //
    // Rules: allies (same Team as damage.SourceTeam), the attacker itself, dead fighters and targets behind
    // solid geometry are skipped. Reports are appended to 'results' (it isn't cleared) and the return value
    // is how many were added. Receivers that answer Ignored don't produce a report and aren't recorded.
    public static class MeleeHitQuery
    {
        // Heights on the target (fractions of its height) that line-of-sight rays aim for: chest, then head,
        // so low cover can block a hit on the legs without blocking one on the upper body.
        const float SightHeightLow = 0.5f;
        const float SightHeightHigh = 0.85f;
        // Records untouched for this long (scaled seconds) belong to attacks that are long over.
        const float PruneAfterSeconds = 10f;

        sealed class AttackRecord
        {
            public int AttackId;
            public float LastUsed;
            public readonly List<int> TargetIds = new List<int>(8);
        }

        static readonly List<AttackRecord> records = new List<AttackRecord>();
        static readonly Stack<AttackRecord> spareRecords = new Stack<AttackRecord>();
        // One candidate list per nesting level, in case a receiver's reaction starts another query.
        static readonly List<List<Combatant>> candidateLists = new List<List<Combatant>>();
        static int depth;
        static bool warnedMissingAttackId;

        // Horizontal arc in front of origin (e.g. the attacker's chest): range to the target's surface,
        // arcDegrees = full width of the swing (360 = all round), verticalReach = how far above/below origin
        // the swing still connects.
        public static int Arc(Vector3 origin, Vector3 forward, float range, float arcDegrees, float verticalReach,
            DamageInfo damage, List<HitReport> results)
        {
            List<Combatant> candidates = Rent();
            try
            {
                System.Numerics.Vector3 o = origin.ToNumerics();
                System.Numerics.Vector3 f = forward.ToNumerics();
                List<Combatant> all = Combatant.All;
                for (int i = 0; i < all.Count; i++)
                {
                    Combatant target = all[i];
                    if (!CanHit(target, in damage)) continue;
                    if (!HitGeometry.InArc(o, f, range, arcDegrees, verticalReach, target.Feet.ToNumerics(), target.Height, target.Radius)) continue;
                    if (!HasLineOfSight(origin, target)) continue;
                    candidates.Add(target);
                }
                // Hits are applied from a copy: a receiver that dies may disable itself and change Combatant.All.
                int added = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    Combatant target = candidates[i];
                    if (!CanHit(target, in damage)) continue; // an earlier reaction this call may have changed things
                    DamageInfo hit = damage;
                    hit.Direction = HorizontalDirection(origin, target, forward, damage).ToNumerics();
                    hit.Point = ContactPoint(origin, target).ToNumerics();
                    if (Apply(target, in hit, results)) added++;
                }
                return added;
            }
            finally
            {
                Return(candidates);
            }
        }

        // Everything whose body is within radius of center (explosions, landing rings). Line of sight is
        // checked from center, so keep it a little above the floor (e.g. waist height for a landing ring).
        public static int Sphere(Vector3 center, float radius, DamageInfo damage, List<HitReport> results)
        {
            List<Combatant> candidates = Rent();
            try
            {
                System.Numerics.Vector3 c = center.ToNumerics();
                List<Combatant> all = Combatant.All;
                for (int i = 0; i < all.Count; i++)
                {
                    Combatant target = all[i];
                    if (!CanHit(target, in damage)) continue;
                    GetCapsule(target, out System.Numerics.Vector3 bottom, out System.Numerics.Vector3 top);
                    if (HitGeometry.DistanceToSegment(c, bottom, top) > radius + target.Radius) continue;
                    if (!HasLineOfSight(center, target)) continue;
                    candidates.Add(target);
                }
                int added = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    Combatant target = candidates[i];
                    if (!CanHit(target, in damage)) continue; // an earlier reaction this call may have changed things
                    DamageInfo hit = damage;
                    hit.Direction = HorizontalDirection(center, target, Vector3.zero, damage).ToNumerics();
                    hit.Point = ContactPoint(center, target).ToNumerics();
                    if (Apply(target, in hit, results)) added++;
                }
                return added;
            }
            finally
            {
                Return(candidates);
            }
        }

        // Applies an attack to one specific fighter, with the same rules and bookkeeping as Arc/Sphere (used for
        // projectile direct hits). The caller fills in damage.Direction and damage.Point. Extra to the spec.
        public static bool TryHit(Combatant target, DamageInfo damage, List<HitReport> results)
        {
            if (!CanHit(target, in damage)) return false;
            return Apply(target, in damage, results);
        }

        // Clears the "already hit" record of an attack. Call when its active window closes.
        public static void EndAttack(int attackId)
        {
            for (int i = records.Count - 1; i >= 0; i--)
            {
                if (records[i].AttackId == attackId) Recycle(i);
            }
        }

        // The shared target filter: alive, not an ally, not the attacker, not already hit by this attack.
        public static bool CanHit(Combatant target, in DamageInfo damage)
        {
            if (target == null || !target.isActiveAndEnabled) return false;
            if (target.Team == damage.SourceTeam) return false;
            if (damage.SourceId != 0 && target.Id == damage.SourceId) return false;
            if (!target.IsAlive) return false;
            return !HasHit(damage.AttackId, target.Id);
        }

        public static bool HasHit(int attackId, int targetId)
        {
            AttackRecord record = FindRecord(attackId);
            return record != null && record.TargetIds.Contains(targetId);
        }

        static bool Apply(Combatant target, in DamageInfo hit, List<HitReport> results)
        {
            IDamageReceiver receiver = target.Receiver;
            if (receiver == null) return false;
            int targetId = target.Id; // read first: a killed target may disable itself during ReceiveHit
            HitResult result = receiver.ReceiveHit(in hit);
            if (result.Outcome == HitOutcome.Ignored) return false;
            // Dodged and blocked hits are recorded too: otherwise one swing would "perfect dodge" every frame.
            Record(hit.AttackId, targetId);
            results?.Add(new HitReport { Target = target, Result = result, Point = hit.Point.ToUnity() });
            return true;
        }

        static void Record(int attackId, int targetId)
        {
            if (attackId == 0)
            {
                if (!warnedMissingAttackId)
                {
                    warnedMissingAttackId = true;
                    Debug.LogWarning("MeleeHitQuery: attack without an AttackId (use CombatIds.Next() per swing), so it can hit the same target every frame.");
                }
                return;
            }
            Prune();
            AttackRecord record = FindRecord(attackId);
            if (record == null)
            {
                record = spareRecords.Count > 0 ? spareRecords.Pop() : new AttackRecord();
                record.AttackId = attackId;
                record.TargetIds.Clear();
                records.Add(record);
            }
            record.LastUsed = Time.time;
            if (!record.TargetIds.Contains(targetId)) record.TargetIds.Add(targetId);
        }

        static AttackRecord FindRecord(int attackId)
        {
            if (attackId == 0) return null;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].AttackId == attackId) return records[i];
            }
            return null;
        }

        static void Prune()
        {
            float now = Time.time;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                // Time.time restarts at 0 each play session, which also clears stale records (now < LastUsed).
                if (now - records[i].LastUsed > PruneAfterSeconds || now < records[i].LastUsed) Recycle(i);
            }
        }

        static void Recycle(int index)
        {
            AttackRecord record = records[index];
            records.RemoveAt(index);
            record.TargetIds.Clear();
            spareRecords.Push(record);
        }

        static bool HasLineOfSight(Vector3 from, Combatant target)
        {
            Vector3 feet = target.Feet;
            if (!CombatPhysics.IsBlocked(from, feet + Vector3.up * (target.Height * SightHeightLow))) return true;
            return !CombatPhysics.IsBlocked(from, feet + Vector3.up * (target.Height * SightHeightHigh));
        }

        static void GetCapsule(Combatant target, out System.Numerics.Vector3 bottom, out System.Numerics.Vector3 top)
        {
            System.Numerics.Vector3 feet = target.Feet.ToNumerics();
            float radius = target.Radius;
            bottom = feet + new System.Numerics.Vector3(0f, radius, 0f);
            top = feet + new System.Numerics.Vector3(0f, Mathf.Max(radius, target.Height - radius), 0f);
        }

        // Attacker -> defender, horizontal and normalised. Falls back to the swing's forward, then to the
        // direction already in the damage, then to "from the front" when the two stand on the same spot.
        static Vector3 HorizontalDirection(Vector3 from, Combatant target, Vector3 fallbackForward, in DamageInfo damage)
        {
            Vector3 d = target.Feet - from;
            d.y = 0f;
            if (d.sqrMagnitude > 1e-6f) return d.normalized;
            fallbackForward.y = 0f;
            if (fallbackForward.sqrMagnitude > 1e-6f) return fallbackForward.normalized;
            Vector3 given = damage.Direction.ToUnity();
            given.y = 0f;
            if (given.sqrMagnitude > 1e-6f) return given.normalized;
            Vector3 front = -target.transform.forward;
            front.y = 0f;
            return front.sqrMagnitude > 1e-6f ? front.normalized : Vector3.forward;
        }

        // The point on the target's body surface nearest to where the attack came from (for sparks).
        static Vector3 ContactPoint(Vector3 from, Combatant target)
        {
            GetCapsule(target, out System.Numerics.Vector3 bottomN, out System.Numerics.Vector3 topN);
            Vector3 bottom = bottomN.ToUnity();
            Vector3 top = topN.ToUnity();
            Vector3 axis = top - bottom;
            float t = axis.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(from - bottom, axis) / axis.sqrMagnitude) : 0f;
            Vector3 onAxis = bottom + axis * t;
            Vector3 outward = from - onAxis;
            if (outward.sqrMagnitude < 1e-8f) return onAxis;
            return onAxis + outward.normalized * Mathf.Min(target.Radius, outward.magnitude);
        }

        static List<Combatant> Rent()
        {
            if (depth == candidateLists.Count) candidateLists.Add(new List<Combatant>(16));
            List<Combatant> list = candidateLists[depth];
            depth++;
            list.Clear();
            return list;
        }

        static void Return(List<Combatant> list)
        {
            list.Clear();
            depth = Mathf.Max(0, depth - 1);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // Domain reload is off in this project, so statics survive between play sessions: start clean.
            for (int i = records.Count - 1; i >= 0; i--) Recycle(i);
            for (int i = 0; i < candidateLists.Count; i++) candidateLists[i].Clear();
            depth = 0;
            warnedMissingAttackId = false;
        }
    }
}
