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
    //
    // Each element's projectile has its own look (ProjectileVisual) and bursts in its element on impact: a fireball, a
    // sliver of ice that splashes, a tumbling boulder that breaks into rock and dust, a swirling ball of wind.
    // An enemy bolt launched with a danger template also re-times the player's danger sense every frame while it flies,
    // from where it really is and where the player really stands, and calls the warning off once it has passed.
    [DefaultExecutionOrder(20)]
    [AddComponentMenu("")]
    public class FireProjectile : MonoBehaviour
    {
        const int MaxProjectiles = 64;
        const int MaxSweepsPerFrame = 4; // passes through dodging fighters before giving up for this frame
        const double CallOffMargin = 1.0; // a bolt that passed is reported as landed this long ago: the model drops it

        static FireProjectile instance;
        static bool quitting;
        static readonly ProjectileSpec DefaultSpec = new ProjectileSpec();
        static bool warnedFull;

        readonly List<FireProjectileSlot> slots = new List<FireProjectileSlot>();
        readonly List<HitReport> reports = new List<HitReport>(8);
        Material fireMaterial;
        Material boltMaterial;
        Material trailMaterial;
        Material waterMaterial;
        Material rockMaterial;
        Material airMaterial;
        Material dustTrailMaterial;
        Gradient fireTrail;
        Gradient boltTrail;
        Gradient waterTrail;
        Gradient rockTrail;
        Gradient airTrail;
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
            if (system != null) system.LaunchInternal(origin, direction, spec ?? DefaultSpec, damage, visual, onHit, false, default);
        }

        // An enemy's projectile that the player's danger sense follows: danger describes the strike (attacker, hit index,
        // parryable...); its ImpactClock is worked out here every frame.
        public static void Launch(Vector3 origin, Vector3 direction, ProjectileSpec spec, DamageInfo damage,
            ProjectileVisual visual, System.Action<HitReport> onHit, in IncomingStrike danger)
        {
            FireProjectile system = GetOrCreate();
            if (system != null) system.LaunchInternal(origin, direction, spec ?? DefaultSpec, damage, visual, onHit, true, danger);
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
            System.Action<HitReport> onHit, bool tracksDanger, in IncomingStrike danger)
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
            slot.TracksDanger = tracksDanger;
            slot.DangerOffPath = false;
            slot.DangerStrike = danger;

            if (slot.HasVisuals) ShowVisual(slot, origin, dir);
            slot.CheckStartOverlap = true;
        }

        // The model, trail and trail colours of each visual. Fire and the bolt look exactly as they always have.
        void ShowVisual(FireProjectileSlot slot, Vector3 origin, Vector3 dir)
        {
            FireVfxStyle style = FireVfx.Style;
            float diameter = slot.Radius * 2f * slot.VisualScale;
            switch (slot.Visual)
            {
                case ProjectileVisual.Fire:
                    slot.ShowAt(origin, dir, fireMaterial, trailMaterial, fireTrail, style.TrailTime * 1.5f, diameter * 0.9f);
                    break;
                case ProjectileVisual.WaterOrb:
                    slot.ShowAt(origin, dir, waterMaterial, trailMaterial, waterTrail, style.TrailTime * 1.2f, diameter * 0.5f);
                    break;
                case ProjectileVisual.Rock:
                    slot.ShowAt(origin, dir, null, dustTrailMaterial != null ? dustTrailMaterial : trailMaterial, rockTrail,
                        style.TrailTime * 2f, diameter * 0.8f);
                    break;
                case ProjectileVisual.AirBall:
                    slot.ShowAt(origin, dir, airMaterial, trailMaterial, airTrail, style.TrailTime * 1.5f, diameter * 0.7f);
                    break;
                default:
                    slot.ShowAt(origin, dir, null, trailMaterial, boltTrail, style.TrailTime, diameter * 0.35f);
                    break;
            }
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
            if (!chosen.HasVisuals && fireMaterial != null && boltMaterial != null && rockMaterial != null && trailMaterial != null)
            {
                chosen.CreateVisuals(transform, fireMaterial, boltMaterial, rockMaterial, trailMaterial);
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
                    if (slot.Flying && slot.TracksDanger) RefreshDanger(slot);
                    if (slot.Flying && slot.HasVisuals) slot.MoveVisual(time, dt);
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

        // Hit a wall, the floor, or a fighter who didn't dodge: explode (if it has a blast radius) and stop. Each element
        // bursts in its own look (a fireball, a splash, rock and dust, a ring of wind); a bolt just sparks.
        void Impact(FireProjectileSlot slot, Vector3 point, bool keepReports = false)
        {
            if (!keepReports) reports.Clear();
            if (slot.ExplosionRadius > 0f) MeleeHitQuery.Sphere(point, slot.ExplosionRadius, slot.Damage, reports);
            ElementId element = ProjectileVisuals.ElementOf(slot.Visual);
            if (element != ElementId.None)
            {
                ElementVfx.Explosion(element, point, slot.ExplosionRadius > 0f ? slot.ExplosionRadius : slot.Radius * 2f * slot.VisualScale);
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
            ElementId element = ProjectileVisuals.ElementOf(slot.Visual);
            if (element == ElementId.Fire || element == ElementId.None)
                FireVfx.HitSpark(point, slot.Visual == ProjectileVisual.Fire ? new Color(1f, 0.6f, 0.3f) : new Color(0.8f, 0.8f, 0.8f));
            else
                ElementVfx.HitSpark(element, point, ElementVfx.StyleOf(element).HitSparkColor);
            Finish(slot);
        }

        // Reached its maximum range.
        void Expire(FireProjectileSlot slot)
        {
            reports.Clear();
            ElementId element = ProjectileVisuals.ElementOf(slot.Visual);
            // A thrown boulder at the end of its flight breaks apart into falling chunks, like its impact does (V5-06: a
            // dust puff alone made the rock vanish in mid-air). Water and air just spray.
            if (slot.Visual == ProjectileVisual.Rock) ElementVfx.Explosion(element, slot.Position, slot.Radius * slot.VisualScale);
            else if (element != ElementId.None) ElementVfx.Burst(element, slot.Position, slot.Velocity, 0.4f * slot.VisualScale);
            Finish(slot);
        }

        void Finish(FireProjectileSlot slot)
        {
            System.Action<HitReport> onHit = slot.OnHit;
            int attackId = slot.Damage.AttackId;
            if (slot.TracksDanger) CallOffDanger(slot);
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

        // ---- Danger sense ------------------------------------------------------------------------------------------

        // When the bolt reaches the player's body if both keep going as they are: the distance still to cover along its
        // flight, less both radii, at its speed. DangerSenseRelay made the first estimate when it was loosed (from where
        // the player stood then); refreshing it every frame keeps the warning and the white "now" cue on the real impact
        // when the player moves. Once the bolt has flown past, the warning is called off.
        void RefreshDanger(FireProjectileSlot slot)
        {
            PlayerController player = PlayerController.Instance;
            PlayerCombatModel model = player != null ? player.Model : null;
            if (model == null) return;
            Vector3 flight = new Vector3(slot.Velocity.x, 0f, slot.Velocity.z);
            float speed = flight.magnitude;
            if (speed < 1e-3f) return;
            Vector3 toPlayer = player.transform.position - slot.Position;
            toPlayer.y = 0f;
            float along = Vector3.Dot(toPlayer, flight / speed);
            if (along < 0f)
            {
                CallOffDanger(slot);
                slot.TracksDanger = false;
                return;
            }
            // Sideways out of its path (a side-step): it will miss, so no "now" flash for it.
            float lateral = (toPlayer - flight / speed * along).magnitude;
            DangerSenseSettings rules = model.Tuning != null ? model.Tuning.DangerSense : null;
            float margin = rules != null ? Mathf.Max(0f, rules.BoltMissMargin) : 0.3f;
            if (lateral > model.BodyRadius + slot.Radius + margin)
            {
                if (!slot.DangerOffPath) CallOffDanger(slot);
                slot.DangerOffPath = true;
                return;
            }
            slot.DangerOffPath = false;
            float gap = Mathf.Max(0f, along - model.BodyRadius - slot.Radius);
            IncomingStrike strike = slot.DangerStrike;
            strike.ImpactClock = model.Clock + gap / speed;
            model.NotifyIncomingStrike(in strike);
        }

        // The bolt landed, missed or is gone: the model drops its warning now (with DangerCleared if one was shown),
        // instead of keeping it until the old estimate runs out.
        static void CallOffDanger(FireProjectileSlot slot)
        {
            PlayerController player = PlayerController.Instance;
            PlayerCombatModel model = player != null ? player.Model : null;
            if (model == null) return;
            DangerSenseSettings rules = model.Tuning != null ? model.Tuning.DangerSense : null;
            double clearAfter = rules != null ? System.Math.Max(0f, rules.ClearAfterImpact) : 0.0;
            IncomingStrike strike = slot.DangerStrike;
            strike.ImpactClock = model.Clock - clearAfter - CallOffMargin;
            model.NotifyIncomingStrike(in strike);
        }

        // ---- Looks ---------------------------------------------------------------------------------------------------

        void CreateMaterials()
        {
            FireVfxStyle style = FireVfx.Style;
            fireMaterial = GreyboxShapes.CreateUnlit("FireProjectile_Ball", style.FlameColor);
            boltMaterial = GreyboxShapes.CreateUnlit("FireProjectile_Bolt", new Color(0.35f, 0.25f, 0.15f));
            trailMaterial = GreyboxShapes.CreateAdditive("FireProjectile_Trail", new Color(2f, 2f, 2f, 1f)); // HDR so the trail blooms
            fireTrail = MakeGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.35f, 0.05f));
            boltTrail = MakeGradient(new Color(1f, 1f, 0.9f), new Color(0.6f, 0.6f, 0.6f));
            // Ice that glows a little (it must read at range), a lit stone, a pale ball of wind.
            waterMaterial = GreyboxShapes.CreateUnlit("ElementProjectile_Ice", new Color(0.75f, 1.1f, 1.5f));
            rockMaterial = GreyboxShapes.CreateLit("ElementProjectile_Rock", new Color(0.46f, 0.36f, 0.25f), false);
            airMaterial = GreyboxShapes.CreateUnlitTransparent("ElementProjectile_Wind", new Color(1.2f, 1.25f, 1.3f, 0.55f), true);
            dustTrailMaterial = GreyboxShapes.CreateAlphaBlend("ElementProjectile_Dust", Color.white);
            waterTrail = MakeGradient(new Color(0.75f, 0.95f, 1f), new Color(0.2f, 0.5f, 1f));
            rockTrail = MakeGradient(new Color(0.7f, 0.6f, 0.45f), new Color(0.5f, 0.42f, 0.32f));
            airTrail = MakeGradient(new Color(1f, 1f, 1f), new Color(0.8f, 0.88f, 1f));
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
            DestroyMaterial(fireMaterial);
            DestroyMaterial(boltMaterial);
            DestroyMaterial(trailMaterial);
            DestroyMaterial(waterMaterial);
            DestroyMaterial(rockMaterial);
            DestroyMaterial(airMaterial);
            DestroyMaterial(dustTrailMaterial);
        }

        static void DestroyMaterial(Material material)
        {
            if (material != null) Destroy(material);
        }
    }
}
