using System;
using System.Collections.Generic;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // What the Unity Combatant + IDamageReceiver pair gives the hit system: a capsule, a team, an id, alive.
    public abstract class SimFighter
    {
        public int Id;
        public string Name = "";
        public Team Team;
        public bool Active = true;          // isActiveAndEnabled
        public readonly SimController Controller = new SimController();
        public float AimHeight = 1.3f;       // PlayerBodySettings.AimPointHeight / EnemyBuilder.AimHeight

        public Vector3 Feet => Controller.Position;
        public float Radius => Controller.Radius;
        public float Height => Controller.Height;
        public Vector3 AimPoint => Feet + new Vector3(0f, AimHeight, 0f);
        public abstract bool IsAlive { get; }
        public abstract float Yaw { get; }
        public abstract HitResult ReceiveHit(in DamageInfo hit);
    }

    public struct SimHitReport
    {
        public SimFighter Target;
        public HitResult Result;
        public Vector3 Point;
    }

    // Mirror of Combat/MeleeHitQuery.cs + CombatPhysics.IsBlocked: arc and sphere queries against every
    // fighter, one hit per AttackId per target (evaded / blocked results are recorded too), line of sight to
    // the target's chest or head through level boxes only.
    public sealed class SimHits
    {
        const float SightHeightLow = 0.5f;
        const float SightHeightHigh = 0.85f;

        readonly SimWorld world;
        readonly Dictionary<int, List<int>> records = new Dictionary<int, List<int>>();
        readonly List<SimFighter> candidates = new List<SimFighter>(16);
        public int QueriesRun;

        public SimHits(SimWorld world)
        {
            this.world = world;
        }

        public int OpenRecords => records.Count;

        public int Arc(Vector3 origin, Vector3 forward, float range, float arcDegrees, float verticalReach, DamageInfo damage,
            List<SimHitReport> results)
        {
            QueriesRun++;
            candidates.Clear();
            IReadOnlyList<SimFighter> all = world.Fighters;
            for (int i = 0; i < all.Count; i++)
            {
                SimFighter target = all[i];
                if (!CanHit(target, in damage)) continue;
                if (!HitGeometry.InArc(origin, forward, range, arcDegrees, verticalReach, target.Feet, target.Height, target.Radius)) continue;
                if (!HasLineOfSight(origin, target)) continue;
                candidates.Add(target);
            }
            int added = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                SimFighter target = candidates[i];
                if (!CanHit(target, in damage)) continue;
                DamageInfo hit = damage;
                hit.Direction = HorizontalDirection(origin, target, forward, damage);
                hit.Point = target.AimPoint;
                if (Apply(target, in hit, results)) added++;
            }
            return added;
        }

        public int Sphere(Vector3 center, float radius, DamageInfo damage, List<SimHitReport> results)
        {
            QueriesRun++;
            candidates.Clear();
            IReadOnlyList<SimFighter> all = world.Fighters;
            for (int i = 0; i < all.Count; i++)
            {
                SimFighter target = all[i];
                if (!CanHit(target, in damage)) continue;
                Vector3 bottom = target.Feet + new Vector3(0f, target.Radius, 0f);
                Vector3 top = target.Feet + new Vector3(0f, Math.Max(target.Radius, target.Height - target.Radius), 0f);
                if (HitGeometry.DistanceToSegment(center, bottom, top) > radius + target.Radius) continue;
                if (!HasLineOfSight(center, target)) continue;
                candidates.Add(target);
            }
            int added = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                SimFighter target = candidates[i];
                if (!CanHit(target, in damage)) continue;
                DamageInfo hit = damage;
                hit.Direction = HorizontalDirection(center, target, Vector3.Zero, damage);
                hit.Point = target.AimPoint;
                if (Apply(target, in hit, results)) added++;
            }
            return added;
        }

        public bool TryHit(SimFighter target, DamageInfo damage, List<SimHitReport> results)
        {
            if (!CanHit(target, in damage)) return false;
            return Apply(target, in damage, results);
        }

        public void EndAttack(int attackId)
        {
            records.Remove(attackId);
        }

        public bool CanHit(SimFighter target, in DamageInfo damage)
        {
            if (target == null || !target.Active) return false;
            if (target.Team == damage.SourceTeam) return false;
            if (damage.SourceId != 0 && target.Id == damage.SourceId) return false;
            if (!target.IsAlive) return false;
            return !HasHit(damage.AttackId, target.Id);
        }

        public bool HasHit(int attackId, int targetId)
        {
            return attackId != 0 && records.TryGetValue(attackId, out List<int> ids) && ids.Contains(targetId);
        }

        bool Apply(SimFighter target, in DamageInfo hit, List<SimHitReport> results)
        {
            HitResult result = target.ReceiveHit(in hit);
            world.OnHitResolved(target, in hit, in result);
            if (result.Outcome == HitOutcome.Ignored) return false;
            if (hit.AttackId != 0)
            {
                if (!records.TryGetValue(hit.AttackId, out List<int> ids))
                {
                    ids = new List<int>(4);
                    records[hit.AttackId] = ids;
                }
                if (!ids.Contains(target.Id)) ids.Add(target.Id);
            }
            results?.Add(new SimHitReport { Target = target, Result = result, Point = hit.Point });
            return true;
        }

        bool HasLineOfSight(Vector3 from, SimFighter target)
        {
            Vector3 feet = target.Feet;
            if (!world.Level.IsBlocked(from, feet + new Vector3(0f, target.Height * SightHeightLow, 0f))) return true;
            return !world.Level.IsBlocked(from, feet + new Vector3(0f, target.Height * SightHeightHigh, 0f));
        }

        static Vector3 HorizontalDirection(Vector3 from, SimFighter target, Vector3 fallbackForward, in DamageInfo damage)
        {
            Vector3 d = Directions.Flatten(target.Feet - from);
            if (d.LengthSquared() > 1e-6f) return Vector3.Normalize(d);
            Vector3 f = Directions.Flatten(fallbackForward);
            if (f.LengthSquared() > 1e-6f) return Vector3.Normalize(f);
            Vector3 g = Directions.Flatten(damage.Direction);
            if (g.LengthSquared() > 1e-6f) return Vector3.Normalize(g);
            Vector3 front = -Directions.FromYaw(target.Yaw);
            return front;
        }
    }

    // Mirror of Combat/FireProjectile.cs: each frame a projectile sweeps its path, walls by sphere cast,
    // fighters by HitGeometry.SweepSphereVsCapsule, earliest contact wins. Dodged (evaded) fighters are
    // flown through, a deflect fizzles it, anything else stops it (no explosion radius on either projectile).
    public sealed class SimProjectiles
    {
        const int MaxSweepsPerFrame = 4;

        public sealed class Projectile
        {
            public Vector3 Position, Velocity;
            public float Radius, MaxRange, Gravity, ExplosionRadius, Travelled;
            public DamageInfo Damage;
            public bool IsFire;
            public Action<SimHitReport> OnHit;
            public readonly List<int> PassedThrough = new List<int>(4);
            public bool Flying, CheckStartOverlap;
            public int LaunchFrame;
            public Vector3 Origin;
            public string EndReason = "";
        }

        readonly SimWorld world;
        public readonly List<Projectile> Flying = new List<Projectile>();
        public readonly List<Projectile> Finished = new List<Projectile>();
        readonly List<SimHitReport> reports = new List<SimHitReport>(8);
        public int Launched;

        public SimProjectiles(SimWorld world)
        {
            this.world = world;
        }

        public Projectile Launch(Vector3 origin, Vector3 direction, ProjectileSpec spec, DamageInfo damage, bool isFire, Action<SimHitReport> onHit)
        {
            spec = spec ?? new ProjectileSpec();
            if (damage.AttackId == 0) damage.AttackId = CombatIds.Next();
            Vector3 dir = direction.LengthSquared() > 1e-8f ? Vector3.Normalize(direction) : new Vector3(0f, 0f, 1f);
            float speed = spec.Speed > 0f ? spec.Speed : 28f;
            var p = new Projectile
            {
                Position = origin, Origin = origin, Velocity = dir * speed, Radius = Math.Max(0.01f, spec.Radius),
                MaxRange = Math.Max(0.1f, spec.MaxRange), Gravity = spec.Gravity, ExplosionRadius = Math.Max(0f, spec.ExplosionRadius),
                Damage = damage, IsFire = isFire, OnHit = onHit, Flying = true, CheckStartOverlap = true, LaunchFrame = world.Frame
            };
            Flying.Add(p);
            Launched++;
            world.Record(-1, "projectile", isFire ? "fire" : "bolt");
            return p;
        }

        // Order 20: after the player and the enemies.
        public void Update(float dt)
        {
            for (int i = 0; i < Flying.Count; i++)
            {
                Projectile p = Flying[i];
                if (p.Flying && dt > 0f) Step(p, dt);
            }
            for (int i = Flying.Count - 1; i >= 0; i--)
            {
                if (!Flying[i].Flying)
                {
                    Finished.Add(Flying[i]);
                    Flying.RemoveAt(i);
                }
            }
            if (Finished.Count > 256) Finished.RemoveRange(0, Finished.Count - 256);
        }

        void Step(Projectile p, float dt)
        {
            if (p.CheckStartOverlap)
            {
                p.CheckStartOverlap = false;
                if (world.Level.IsOverlapping(p.Position, p.Radius))
                {
                    Impact(p, p.Position, false, "launched inside a wall");
                    return;
                }
            }
            p.Velocity.Y -= p.Gravity * dt;
            Vector3 step = p.Velocity * dt;
            float length = step.Length();
            float remaining = p.MaxRange - p.Travelled;
            if (length > remaining) length = remaining;
            if (length <= 1e-6f)
            {
                if (remaining <= 1e-4f) Finish(p, "max range");
                return;
            }
            Vector3 dir = step / step.Length();
            Vector3 from = p.Position;
            for (int sweep = 0; sweep < MaxSweepsPerFrame && p.Flying && length > 1e-6f; sweep++)
            {
                Vector3 to = from + dir * length;
                float wallT = 2f;
                float wallDistance = 0f;
                if (world.Level.SphereCast(from, p.Radius, dir, length, out wallDistance)) wallT = wallDistance / length;

                SimFighter target = null;
                float targetT = 2f;
                IReadOnlyList<SimFighter> all = world.Fighters;
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    SimFighter f = all[i];
                    if (!world.Hits.CanHit(f, in p.Damage) || p.PassedThrough.Contains(f.Id)) continue;
                    float t = HitGeometry.SweepSphereVsCapsule(from, to, p.Radius, f.Feet, f.Height, f.Radius);
                    if (t >= 0f && t < targetT)
                    {
                        targetT = t;
                        target = f;
                    }
                }

                if (target != null && targetT <= wallT)
                {
                    Vector3 contact = Vector3.Lerp(from, to, targetT);
                    p.Travelled += length * targetT;
                    p.Position = contact;
                    if (!HitFighter(p, target, contact, dir)) return;
                    from = contact;
                    length *= 1f - targetT;
                    continue;
                }
                if (wallT <= 1f)
                {
                    p.Travelled += wallDistance;
                    p.Position = from + dir * wallDistance;
                    Impact(p, p.Position, false, "wall");
                    return;
                }
                p.Position = to;
                p.Travelled += length;
                length = 0f;
            }
            if (p.Flying && p.Travelled >= p.MaxRange - 1e-4f) Finish(p, "max range");
        }

        bool HitFighter(Projectile p, SimFighter target, Vector3 contact, Vector3 dir)
        {
            DamageInfo hit = p.Damage;
            Vector3 flat = new Vector3(dir.X, 0f, dir.Z);
            if (flat.LengthSquared() < 1e-6f) flat = Directions.Flatten(target.Feet - contact);
            if (flat.LengthSquared() < 1e-6f) flat = -Directions.FromYaw(target.Yaw);
            hit.Direction = flat.LengthSquared() > 1e-6f ? Vector3.Normalize(flat) : new Vector3(0f, 0f, 1f);
            hit.Point = contact;
            reports.Clear();
            bool reacted = world.Hits.TryHit(target, hit, reports);
            HitOutcome outcome = reacted && reports.Count > 0 ? reports[reports.Count - 1].Result.Outcome : HitOutcome.Ignored;
            if (outcome == HitOutcome.Ignored || outcome == HitOutcome.Evaded || outcome == HitOutcome.PerfectEvade)
            {
                p.PassedThrough.Add(target.Id);
                Notify(p.OnHit);
                return true;
            }
            if (outcome == HitOutcome.Parried)
            {
                Finish(p, "deflected", keepReports: true);
                return false;
            }
            Impact(p, contact, true, "hit " + target.Name);
            return false;
        }

        void Impact(Projectile p, Vector3 point, bool keepReports, string reason)
        {
            if (!keepReports) reports.Clear();
            if (p.ExplosionRadius > 0f) world.Hits.Sphere(point, p.ExplosionRadius, p.Damage, reports);
            Finish(p, reason, keepReports: true);
        }

        void Finish(Projectile p, string reason, bool keepReports = false)
        {
            if (!keepReports) reports.Clear();
            Action<SimHitReport> onHit = p.OnHit;
            p.Flying = false;
            p.OnHit = null;
            p.EndReason = reason;
            world.Hits.EndAttack(p.Damage.AttackId);
            Notify(onHit);
        }

        void Notify(Action<SimHitReport> onHit)
        {
            if (onHit == null || reports.Count == 0) return;
            for (int i = 0; i < reports.Count; i++) onHit(reports[i]);
            reports.Clear();
        }

        public void ClearAll()
        {
            for (int i = 0; i < Flying.Count; i++)
            {
                world.Hits.EndAttack(Flying[i].Damage.AttackId);
                Flying[i].Flying = false;
            }
            Flying.Clear();
        }
    }
}
