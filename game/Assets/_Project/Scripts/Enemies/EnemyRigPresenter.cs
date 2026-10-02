using UnityEngine;
using UnityEngine.Rendering;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The "readable and fair" layer of an enemy: everything you see besides the body's movement (which the
    // procedural animator does, from EnemyAnimationFeed). Every attack glows in its telegraph colour for its whole
    // wind-up (yellow = normal, red = heavy or delayed, violet = the break-out), building up and flaring just
    // before the strike; the blade leaves a swish trail as it swings; hits flash, staggers flash and dim, deaths
    // flash; a launcher's victim rises on a column of the element that launched it (fire, a water spout, a pillar
    // of rock, an updraft) and trails that element while it tumbles.
    //
    // The glow's build-up is the timing cue: players learn to dodge or deflect on the flare rather than on the
    // pose, which is what makes the delayed thrust (a held pose that baits panic dodges) fair.
    // The blade shows the reach: the dao is WeaponLength long and the sword clips swing it at full arm's length,
    // so its tip sweeps the edge of what the attack can hit (the `anim` CombatSim scenario measures it).
    //
    // Plain C# owned by an EnemyFighter, which forwards the brain's events and calls Tick once per frame.
    public sealed class EnemyRigPresenter
    {
        const float MinSeconds = 1e-3f;
        const float SwishVertexSpacing = 0.02f;

        HumanoidBody body;
        TrailRenderer swish;
        Material swishMaterial;
        FireVfxHandle embers;

        bool attackRunning;
        bool attackJustStarted;
        bool telegraphing;
        float attackTime;
        float telegraphDuration;
        float telegraphPeak;
        Color telegraphColor;

        readonly EnemyPoseSettings defaultPoses = new EnemyPoseSettings();

        public bool IsTelegraphing => telegraphing;

        // Hooks up the body. In play mode this also creates the sword trail.
        public void Bind(HumanoidBody newBody, EnemyFeedbackSettings feedback)
        {
            body = newBody;
            if (body != null) body.SetWeaponLength(PosesOf(feedback).WeaponLength);
            if (swish == null && feedback.Swish && body != null && body.HasWeapon && body.Look.Weapon != BodyWeapon.Crossbow && Application.isPlaying)
                CreateSwish(feedback);
        }

        public void Dispose()
        {
            GreyboxShapes.SafeDestroy(swishMaterial);
            swishMaterial = null;
            StopEmbers();
        }

        // ---------------------------------------------------------------- attack events

        public void OnTelegraphStarted(in EnemyEvent e, EnemyFeedbackSettings feedback)
        {
            attackRunning = true;
            attackJustStarted = true;
            attackTime = 0f;
            telegraphing = true;
            telegraphDuration = Mathf.Max(MinSeconds, e.Duration);
            telegraphColor = feedback.TelegraphColor(e.Telegraph, e.Move);
            telegraphPeak = Mathf.Max(0f, feedback.TelegraphIntensity(e.Telegraph));
            if (body != null) body.SetWeaponLength(PosesOf(feedback).WeaponLength);   // picks up live edits
            ApplyTelegraphGlow(feedback);
        }

        // A melee strike's active window opened: the glow goes out and the blade trails.
        public void OnStrike(in EnemyEvent e, EnemyFeedbackSettings feedback)
        {
            StopTelegraph();
            StartSwish(feedback);
        }

        public void OnStrikeEnd()
        {
            StopSwish(false);
        }

        public void OnBoltLaunched(in EnemyEvent e, EnemyFeedbackSettings feedback)
        {
            StopTelegraph();
            FireVfx.HitSpark(e.Origin.ToUnity(), feedback.ShotSparkColor);
        }

        // Recovery finished, or the attack was cut short.
        public void OnAttackEnded()
        {
            attackRunning = false;
            StopTelegraph();
            StopSwish(false);
        }

        // ---------------------------------------------------------------- reactions

        public void OnAggroed(EnemyFeedbackSettings feedback)
        {
            if (body != null) body.Flash(feedback.AggroFlashColor, feedback.AggroFlashTime);
        }

        public void OnDamaged(EnemyFeedbackSettings feedback)
        {
            if (body != null) body.Flash(feedback.HitFlashColor, feedback.HitFlashTime);
        }

        public void OnStaggered(EnemyFeedbackSettings feedback)
        {
            attackRunning = false;
            StopTelegraph();
            StopSwish(false);
            if (body == null) return;
            body.SetStaggered(true);
            body.Flash(feedback.StaggerFlashColor, feedback.StaggerFlashTime);
        }

        public void OnStaggerEnded()
        {
            if (body != null) body.SetStaggered(false);
        }

        // Thrown up by a launcher: a column of the element that launched it under it (fire, a water spout, a pillar of
        // rock, an updraft) and that element trailing it while it tumbles (embers, droplets, dust, wisps).
        public void OnLaunched(Vector3 feet, float height)
        {
            OnLaunched(feet, height, ElementId.Fire);
        }

        public void OnLaunched(Vector3 feet, float height, ElementId element)
        {
            attackRunning = false;
            StopTelegraph();
            StopSwish(false);
            ElementVfx.Pillar(element, feet, height);
            StopEmbers();
            if (body != null) embers = ElementVfx.LaunchTrail(element, body.ChestAnchor, 0f);
        }

        // Landed from a juggle or a slam: a downward burst and a ring racing out where it hits the floor, at the moment
        // it hits (the slam itself only throws the element down from the limb), in the element that put it there.
        public void OnKnockedDown(Vector3 feet, float ringRadius)
        {
            OnKnockedDown(feet, ringRadius, ElementId.Fire);
        }

        public void OnKnockedDown(Vector3 feet, float ringRadius, ElementId element)
        {
            StopEmbers();
            ElementVfx.Slam(element, feet + Vector3.up * KnockdownBurstHeight, ringRadius);
        }

        const float KnockdownBurstHeight = 0.3f;   // metres above the feet the burst starts, so it reads as hitting the floor

        public void OnDied(EnemyFeedbackSettings feedback)
        {
            attackRunning = false;
            StopTelegraph();
            StopSwish(false);
            StopEmbers();
            if (body == null) return;
            body.SetDead(true);
            body.Flash(feedback.DeathFlashColor, feedback.DeathFlashTime);
        }

        public void OnHealthRefilled(EnemyFeedbackSettings feedback)
        {
            if (body != null) body.Flash(feedback.RefillFlashColor, feedback.RefillFlashTime);
        }

        // Back to normal with every effect cleared (respawn / reset).
        public void OnReset()
        {
            attackRunning = false;
            attackJustStarted = false;
            telegraphing = false;
            StopSwish(true);
            StopEmbers();
            if (body != null) body.ResetLook();
        }

        public void OnDisabled()
        {
            attackRunning = false;
            StopTelegraph();
            StopSwish(true);
            StopEmbers();
        }

        // ---------------------------------------------------------------- per frame

        public void Tick(float dt, EnemyBrain brain, EnemyFeedbackSettings feedback)
        {
            if (!(dt > 0f)) return;
            if (attackRunning)
            {
                if (attackJustStarted) attackJustStarted = false;
                else attackTime += dt;
                if (telegraphing) ApplyTelegraphGlow(feedback);
            }
            // The embers go out once it's back on the ground.
            if (brain != null && !brain.IsLaunched && embers.IsAlive) StopEmbers();
        }

        // Degrees up toward the target, for the crossbow (clamped to what a person can aim).
        public float AimPitch(Vector3 from, Vector3 target, EnemyFeedbackSettings feedback)
        {
            float max = Mathf.Abs(PosesOf(feedback).MaxAimPitch);
            return Mathf.Clamp(Directions.PitchOf((target - from).ToNumerics()), -max, max);
        }

        // ---------------------------------------------------------------- helpers

        void ApplyTelegraphGlow(EnemyFeedbackSettings feedback)
        {
            if (body == null) return;
            float u = Mathf.Clamp01(attackTime / telegraphDuration);
            float intensity = telegraphPeak * Mathf.Lerp(feedback.TelegraphStartShare, 1f, u * u);
            if (feedback.TelegraphFlareTime > 0f && telegraphDuration - attackTime <= feedback.TelegraphFlareTime)
                intensity *= Mathf.Max(1f, feedback.TelegraphFlareBoost);
            body.SetTelegraph(telegraphColor, intensity);
        }

        void StopTelegraph()
        {
            telegraphing = false;
            if (body != null) body.SetTelegraph(telegraphColor, 0f);
        }

        void StopEmbers()
        {
            embers.Stop();
            embers = FireVfxHandle.None;
        }

        void CreateSwish(EnemyFeedbackSettings feedback)
        {
            Transform tip = body.GetAnchor(Limb.Weapon);
            if (tip == null) return;
            if (swishMaterial == null) swishMaterial = GreyboxShapes.CreateAdditive("EnemySwish", feedback.SwishColor);
            if (swishMaterial == null) return; // URP shaders missing: GreyboxShapes has already warned once
            var trailObject = new GameObject("WeaponSwish");
            trailObject.layer = tip.gameObject.layer;
            trailObject.transform.SetParent(tip, false);
            swish = trailObject.AddComponent<TrailRenderer>();
            swish.sharedMaterial = swishMaterial;
            swish.shadowCastingMode = ShadowCastingMode.Off;
            swish.receiveShadows = false;
            swish.autodestruct = false;
            swish.emitting = false;
            swish.minVertexDistance = SwishVertexSpacing;
            swish.numCapVertices = 2;
            swish.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(Mathf.Clamp01(feedback.SwishColor.a), 0f), new GradientAlphaKey(0f, 1f) });
            swish.colorGradient = fade;
        }

        void StartSwish(EnemyFeedbackSettings feedback)
        {
            if (swish == null || !feedback.Swish) return;
            swish.time = Mathf.Max(MinSeconds, feedback.SwishTime);
            swish.widthMultiplier = Mathf.Max(0f, feedback.SwishWidth);
            GreyboxShapes.SetBaseColor(swishMaterial, feedback.SwishColor);
            swish.Clear();
            swish.emitting = true;
        }

        void StopSwish(bool clear)
        {
            if (swish == null) return;
            swish.emitting = false;
            if (clear) swish.Clear();
        }

        EnemyPoseSettings PosesOf(EnemyFeedbackSettings feedback)
        {
            return feedback != null && feedback.Poses != null ? feedback.Poses : defaultPoses;
        }
    }
}
