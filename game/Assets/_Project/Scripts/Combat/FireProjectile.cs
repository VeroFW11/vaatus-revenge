using System.Collections.Generic;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Flying attacks: fire blasts and crossbow bolts. Call FireProjectile.Launch from anywhere in play mode;
    // the first call creates a hidden, pooled projectile system.
    //
    // Every frame each projectile sweeps along its path (it doesn't just jump, so a fast bolt can't skip
    // through a thin wall or a fighter between frames): walls via a sphere cast, fighters via their capsules.
    // The earliest contact wins. A fighter that dodges (i-frames) lets it fly on through; a deflect makes it
    // fizzle; anything else stops it, with an explosion if ExplosionRadius > 0 (same AttackId, so the fighter
    // hit directly isn't hit twice). onHit gets one HitReport per fighter touched. Give each projectile its
    // own AttackId (CombatIds.Next()); the record is cleared automatically when the projectile is gone.
    [DefaultExecutionOrder(20)]
    [AddComponentMenu("")]
    public class FireProjectile : MonoBehaviour
    {
        const int MaxProjectiles = 64;
        const int MaxSweepsPerFrame = 4; // passes through dodging fighters before giving up for this frame

        static FireProjectile instance;
        static bool quitting;
        static readonly ProjectileSpec DefaultSpec = new ProjectileSpec();
        static bool warnedFull;

        readonly List<FireProjectileSlot> slots = new List<FireProjectileSlot>();
        readonly List<HitReport> reports = new List<HitReport>(8);
        Material fireMaterial;
        Material boltMaterial;
        Material trailMaterial;
        Gradient fireTrail;
        Gradient boltTrail;
        bool updating;
        float time;

        public static int ActiveCount
        {
            get
            {
                if (instance == null) return 0;
                int count = 0;
                for (int i = 0; i < instance.slots.Count; i++)
                {
                    if (instance.slots[i].Flying) count++;
                }
                return count;
            }
        }

        // origin: where it starts (e.g. the casting hand). direction: which way it flies (normalised here).
        // spec: speed, radius, range, gravity, explosion (null = defaults). damage: filled in like a melee hit;
        // Direction and Point are set per target. onHit: optional, called once per fighter touched.
        public static void Launch(Vector3 origin, Vector3 direction, ProjectileSpec spec, DamageInfo damage,
            ProjectileVisual visual, System.Action<HitReport> onHit)
        {
            FireProjectile system = GetOrCreate();
            if (system != null) system.LaunchInternal(origin, direction, spec ?? DefaultSpec, damage, visual, onHit);
        }

        // Removes every projectile at once (e.g. on a sandbox reset). Extra to the spec.
        public static void ClearAll()
        {
            if (instance == null) return;
            for (int i = 0; i < instance.slots.Count; i++)
            {
                FireProjectileSlot slot = instance.slots[i];
                if (slot.Flying) MeleeHitQuery.EndAttack(slot.Damage.AttackId);
                slot.Hide();
            }
        }

        static FireProjectile GetOrCreate()
        {
            if (instance != null) return instance;
            if (!Application.isPlaying || quitting) return null;
            var go = new GameObject("FireProjectile (pool)");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            instance = go.AddComponent<FireProjectile>();
            instance.CreateMaterials();
            return instance;
        }

        // Domain reload is off in this project, so statics survive between play sessions: reset them.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
            quitting = false;
            warnedFull = false;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        static void OnQuitting()
        {
            quitting = true;
        }

        void LaunchInternal(Vector3 origin, Vector3 direction, ProjectileSpec spec, DamageInfo damage, ProjectileVisual visual,
            System.Action<HitReport> onHit)
        {
            FireProjectileSlot slot = AcquireSlot();
            if (slot == null) return;
            if (damage.AttackId == 0) damage.AttackId = CombatIds.Next(); // without an id it could hit a target every frame

            Vector3 dir = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.forward;
            float speed = spec.Speed > 0f ? spec.Speed : DefaultSpec.Speed;
            slot.Flying = true;
            slot.Fading = false;
            slot.SkipThisFrame = updating;
            slot.LaunchTime = Time.time;
            slot.Position = origin;
            slot.Velocity = dir * speed;
            slot.Radius = Mathf.Max(0.01f, spec.Radius);
            slot.MaxRange = Mathf.Max(0.1f, spec.MaxRange);
            slot.Gravity = spec.Gravity;
            slot.ExplosionRadius = Mathf.Max(0f, spec.ExplosionRadius);
            slot.VisualScale = spec.VisualScale > 0f ? spec.VisualScale : 1f;
            slot.Travelled = 0f;
            slot.Damage = damage;
            slot.Visual = visual;
            slot.OnHit = onHit;
            slot.PassedThrough.Clear();

            FireVfxStyle style = FireVfx.Style;
            bool fire = visual == ProjectileVisual.Fire;
            if (slot.HasVisuals)
            {
                slot.ShowAt(origin, dir, fire ? fireTrail : boltTrail, style.TrailTime * (fire ? 1.5f : 1f),
                    slot.Radius * 2f * slot.VisualScale * (fire ? 0.9f : 0.35f));
            }
            slot.CheckStartOverlap = true;
        }

        FireProjectileSlot AcquireSlot()
        {
            FireProjectileSlot chosen = null;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].IsFree)
                {
                    chosen = slots[i];
                    break;
                }
            }
            if (chosen == null && slots.Count < MaxProjectiles)
            {
                chosen = new FireProjectileSlot();
                slots.Add(chosen);
            }
            if (chosen == null)
            {
                // Full: take a slot whose trail is only fading out, or else the oldest projectile.
                for (int i = 0; i < slots.Count && chosen == null; i++)
                {
                    if (slots[i].Fading) chosen = slots[i];
                }
                if (chosen == null)
                {
                    chosen = slots[0];
                    for (int i = 1; i < slots.Count; i++)
                    {
                        if (slots[i].LaunchTime < chosen.LaunchTime) chosen = slots[i];
                    }
                    MeleeHitQuery.EndAttack(chosen.Damage.AttackId);
                    if (!warnedFull)
                    {
                        warnedFull = true;
                        Debug.LogWarning("FireProjectile: more than " + MaxProjectiles + " projectiles at once; recycling the oldest.");
                    }
                }
                chosen.Hide();
            }
            if (!chosen.HasVisuals && fireMaterial != null && boltMaterial != null && trailMaterial != null)
            {
                chosen.CreateVisuals(transform, fireMaterial, boltMaterial, trailMaterial);
            }
            return chosen;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            time += dt;
            updating = true;
            try
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    FireProjectileSlot slot = slots[i];
                    if (slot.Fading)
                    {
                        slot.FadeRemaining -= dt;
                        if (slot.FadeRemaining <= 0f) slot.Hide();
                        continue;
                    }
                    if (!slot.Flying) continue;
                    if (slot.SkipThisFrame)
                    {
                        slot.SkipThisFrame = false;
                        continue;
                    }
                    if (dt > 0f) Step(slot, dt);
                    if (slot.Flying && slot.HasVisuals) slot.MoveVisual(time);
                }
            }
            finally
            {
                updating = false;
            }
        }

        void Step(FireProjectileSlot slot, float dt)
        {
            if (slot.CheckStartOverlap)
            {
                // Launched from inside a wall (e.g. casting while pressed against it): burst right away instead
                // of silently flying through, because a sphere cast can't see what it starts inside.
                slot.CheckStartOverlap = false;
                if (CombatPhysics.IsOverlapping(slot.Position, slot.Radius))
                {
                    Impact(slot, slot.Position);
                    return;
                }
            }
            slot.Velocity.y -= slot.Gravity * dt;
            Vector3 step = slot.Velocity * dt;
            float length = step.magnitude;
            float remainingRange = slot.MaxRange - slot.Travelled;
            if (length > remainingRange) length = remainingRange;
            if (length <= 1e-6f)
            {
                if (remainingRange <= 1e-4f) Expire(slot);
                return;
            }
            Vector3 dir = step / step.magnitude;
            Vector3 from = slot.Position;

            for (int sweep = 0; sweep < MaxSweepsPerFrame && slot.Flying && length > 1e-6f; sweep++)
            {
                Vector3 to = from + dir * length;
                float wallT = 2f;
                RaycastHit wallHit = default;
                if (CombatPhysics.SphereCast(from, slot.Radius, dir, length, out wallHit)) wallT = wallHit.distance / length;

                Combatant target = null;
                float targetT = 2f;
                FindFirstFighter(slot, from, to, ref target, ref targetT);

                if (target != null && targetT <= wallT)
                {
                    Vector3 contact = Vector3.LerpUnclamped(from, to, targetT);
                    slot.Travelled += length * targetT;
                    slot.Position = contact;
                    if (!HitFighter(slot, target, contact, dir)) return; // stopped (hit, blocked or deflected)
                    // Flew through a dodging fighter: keep sweeping the rest of this frame's path.
                    from = contact;
                    length *= 1f - targetT;
                    continue;
                }
                if (wallT <= 1f)
                {
                    slot.Travelled += wallHit.distance;
                    slot.Position = from + dir * wallHit.distance; // sphere centre at contact, just off the wall
                    Impact(slot, slot.Position);
                    return;
                }
                slot.Position = to;
                slot.Travelled += length;
                length = 0f;
            }
            if (slot.Flying && slot.Travelled >= slot.MaxRange - 1e-4f) Expire(slot);
        }

        void FindFirstFighter(FireProjectileSlot slot, Vector3 from, Vector3 to, ref Combatant target, ref float targetT)
        {
            System.Numerics.Vector3 a = from.ToNumerics();
            System.Numerics.Vector3 b = to.ToNumerics();
            List<Combatant> all = Combatant.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                Combatant fighter = all[i];
                if (!MeleeHitQuery.CanHit(fighter, in slot.Damage) || slot.PassedThrough.Contains(fighter.Id)) continue;
                float t = HitGeometry.SweepSphereVsCapsule(a, b, slot.Radius, fighter.Feet.ToNumerics(), fighter.Height, fighter.Radius);
                if (t >= 0f && t < targetT)
                {
                    targetT = t;
                    target = fighter;
                }
            }
        }

        // Returns true when the projectile keeps flying (the fighter dodged or ignored it).
        bool HitFighter(FireProjectileSlot slot, Combatant target, Vector3 contact, Vector3 dir)
        {
            DamageInfo hit = slot.Damage;
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude < 1e-6f) flat = new Vector3(target.Feet.x - contact.x, 0f, target.Feet.z - contact.z);
            if (flat.sqrMagnitude < 1e-6f) flat = -target.transform.forward;
            flat.y = 0f;
            hit.Direction = (flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward).ToNumerics();
            hit.Point = contact.ToNumerics();

            int targetId = target.Id;
            reports.Clear();
            bool reacted = MeleeHitQuery.TryHit(target, hit, reports);
            HitOutcome outcome = reacted && reports.Count > 0 ? reports[reports.Count - 1].Result.Outcome : HitOutcome.Ignored;
            if (outcome == HitOutcome.Ignored || outcome == HitOutcome.Evaded || outcome == HitOutcome.PerfectEvade)
            {
                slot.PassedThrough.Add(targetId);
                Notify(slot.OnHit);
                return true;
            }
            if (outcome == HitOutcome.Parried)
            {
                Fizzle(slot, contact);
                return false;
            }
            Impact(slot, contact, true);
            return false;
        }

        // Hit a wall, the floor, or a fighter who didn't dodge: explode (if it has a blast radius) and stop.
        void Impact(FireProjectileSlot slot, Vector3 point, bool keepReports = false)
        {
            if (!keepReports) reports.Clear();
            if (slot.ExplosionRadius > 0f) MeleeHitQuery.Sphere(point, slot.ExplosionRadius, slot.Damage, reports);
            if (slot.Visual == ProjectileVisual.Fire)
            {
                FireVfx.Explosion(point, slot.ExplosionRadius > 0f ? slot.ExplosionRadius : slot.Radius * 2f * slot.VisualScale);
            }
            else
            {
                FireVfx.HitSpark(point, new Color(0.9f, 0.85f, 0.7f));
            }
            Finish(slot);
        }

        // Deflected: the projectile is snuffed out without exploding.
        void Fizzle(FireProjectileSlot slot, Vector3 point)
        {
            FireVfx.HitSpark(point, slot.Visual == ProjectileVisual.Fire ? new Color(1f, 0.6f, 0.3f) : new Color(0.8f, 0.8f, 0.8f));
            Finish(slot);
        }

        // Reached its maximum range.
        void Expire(FireProjectileSlot slot)
        {
            reports.Clear();
            if (slot.Visual == ProjectileVisual.Fire) FireVfx.Burst(slot.Position, slot.Velocity, 0.4f * slot.VisualScale);
            Finish(slot);
        }

        void Finish(FireProjectileSlot slot)
        {
            System.Action<HitReport> onHit = slot.OnHit;
            int attackId = slot.Damage.AttackId;
            slot.BeginFade(); // state first, so a callback that launches or clears projectiles sees a consistent pool
            if (!slot.HasVisuals) slot.Hide();
            MeleeHitQuery.EndAttack(attackId);
            Notify(onHit);
        }

        void Notify(System.Action<HitReport> onHit)
        {
            if (onHit == null || reports.Count == 0) return;
            for (int i = 0; i < reports.Count; i++)
            {
                try
                {
                    onHit(reports[i]);
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e); // one broken callback must not stop every other projectile this frame
                }
            }
            reports.Clear();
        }

        void CreateMaterials()
        {
            FireVfxStyle style = FireVfx.Style;
            fireMaterial = GreyboxShapes.CreateUnlit("FireProjectile_Ball", style.FlameColor);
            boltMaterial = GreyboxShapes.CreateUnlit("FireProjectile_Bolt", new Color(0.35f, 0.25f, 0.15f));
            trailMaterial = GreyboxShapes.CreateAdditive("FireProjectile_Trail", new Color(2f, 2f, 2f, 1f)); // HDR so the trail blooms
            fireTrail = MakeGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.35f, 0.05f));
            boltTrail = MakeGradient(new Color(1f, 1f, 0.9f), new Color(0.6f, 0.6f, 0.6f));
        }

        static Gradient MakeGradient(Color head, Color tail)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(head, 0f), new GradientColorKey(tail, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            if (fireMaterial != null) Destroy(fireMaterial);
            if (boltMaterial != null) Destroy(boltMaterial);
            if (trailMaterial != null) Destroy(trailMaterial);
        }
    }
}
