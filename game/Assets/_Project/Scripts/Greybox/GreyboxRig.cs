using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // A primitive humanoid (capsule body, sphere head, floating fists and feet, optional sword) that shows
    // what a fighter is doing without real art or animation: strikes shoot a limb out, wind-ups glow, hits
    // flash, staggered fighters wobble and dead ones topple over.
    //
    // Put it on a fighter whose pivot is at its feet (a 1.8 m CharacterController with its centre at y 0.9),
    // call Build(color, withWeapon) once, then drive it from gameplay code. Build works in edit mode too, so
    // the sandbox builder can save finished fighters into the scene.
    // Everything animates with scaled time: hitstop freezes poses and slow motion slows them.
    [DefaultExecutionOrder(50)] // after the player (0), enemies (10) and projectiles (20): shows this frame's commands
    [DisallowMultipleComponent]
    public class GreyboxRig : MonoBehaviour
    {
        const float TwoPi = Mathf.PI * 2f;

        [SerializeField] private GreyboxRigStyle style = new GreyboxRigStyle();
        [SerializeField] private Color bodyColor = new Color(0.6f, 0.6f, 0.6f);
        [SerializeField] private bool hasWeapon;
        [SerializeField] private GreyboxRigParts parts = new GreyboxRigParts();
        [Tooltip("Created by Build. Each rig has its own materials so a glow never shows up on other fighters.")]
        [SerializeField] private Material bodyMaterial;
        [SerializeField] private Material limbMaterial;

        readonly GreyboxLimbMotion rightFistMotion = new GreyboxLimbMotion();
        readonly GreyboxLimbMotion leftFistMotion = new GreyboxLimbMotion();
        readonly GreyboxLimbMotion rightFootMotion = new GreyboxLimbMotion();
        readonly GreyboxLimbMotion leftFootMotion = new GreyboxLimbMotion();
        MaterialPropertyBlock fistBlock;

        bool runtimeReady;
        bool ownsMaterials; // play mode works on copies, which this rig must destroy
        float animTime;

        float leanDegrees, leanDuration, leanTime;
        float spinDegrees, spinDuration, spinTime;
        Color telegraphColor;
        float telegraphIntensity;
        Color flashColor;
        float flashDuration, flashRemaining;
        bool invulnerable;
        bool staggered;
        float staggerTime;
        bool dead;
        float deadTime;
        float chargeLevel;
        bool chargeSweetSpot;

        bool coloursWritten;
        bool fistBlockSet;
        Color lastBody, lastBodyEmission, lastLimb, lastLimbEmission, lastFistEmission;

        public GreyboxRigStyle Style
        {
            get => style;
            set => style = value ?? new GreyboxRigStyle();
        }

        public Color BodyColor => bodyColor;
        public bool HasWeapon => hasWeapon && parts != null && parts.Weapon != null;
        public bool IsBuilt => parts != null && parts.IsValid;
        public bool IsDead => dead;
        public Transform ChestAnchor => IsBuilt && parts.Chest != null ? parts.Chest : transform;
        public Transform HeadAnchor => IsBuilt && parts.Head != null ? parts.Head : transform;

        // Creates (or recreates) the body under this GameObject. Safe to call again: the old body is removed
        // and this rig's materials are reused, so rebuilding never leaks materials.
        public void Build(Color newBodyColor, bool withWeapon)
        {
            bodyColor = newBodyColor;
            bodyColor.a = 1f;
            hasWeapon = withWeapon;
            RemoveOldVisual();
            PrepareMaterials();
            parts = GreyboxRigParts.Create(transform, withWeapon, bodyMaterial, limbMaterial);
            GreyboxShapes.StripColliders(parts.Visual.gameObject); // only under the visual: the fighter's CharacterController is a Collider too
            GreyboxShapes.SetBaseColor(bodyMaterial, bodyColor);
            GreyboxShapes.SetBaseColor(limbMaterial, LimbColor());
            runtimeReady = Application.isPlaying;
            ResetPose();
        }

        public Material BodyMaterial => bodyMaterial;
        public Material LimbMaterial => limbMaterial;

        // Uses specific materials (e.g. saved assets) instead of the ones Build creates. Build and the glows
        // recolour them, so give each rig its own pair; in play mode the rig always works on copies, so a saved
        // asset is never changed while playing. Call before Build, or after it to swap them on a built rig.
        public void SetMaterials(Material body, Material limbs)
        {
            bool playing = Application.isPlaying;
            Material newBody = body;
            Material newLimbs = limbs;
            if (playing)
            {
                // Work on copies, unless we were handed back our own copies.
                if (newBody != null && !(ownsMaterials && newBody == bodyMaterial)) newBody = new Material(newBody);
                if (newLimbs != null && !(ownsMaterials && newLimbs == limbMaterial)) newLimbs = new Material(newLimbs);
            }
            if (ownsMaterials)
            {
                if (bodyMaterial != newBody) GreyboxShapes.SafeDestroy(bodyMaterial);
                if (limbMaterial != newLimbs) GreyboxShapes.SafeDestroy(limbMaterial);
            }
            bodyMaterial = newBody;
            limbMaterial = newLimbs;
            ownsMaterials = playing;
            if (!IsBuilt) return;
            PrepareMaterials();
            AssignMaterials();
            ApplyColours(true);
        }

        // The transform a strike comes from: follow it with trails, spawn sparks there. Never null.
        // BothFists returns the right fist; Weapon returns the blade tip (or the right fist when unarmed).
        public Transform GetAnchor(Limb limb)
        {
            if (!IsBuilt) return transform;
            switch (limb)
            {
                case Limb.LeftFist: return parts.LeftFist;
                case Limb.RightFoot: return parts.RightFoot;
                case Limb.LeftFoot: return parts.LeftFoot;
                case Limb.Weapon: return parts.WeaponTip != null ? parts.WeaponTip : parts.RightFist;
                default: return parts.RightFist;
            }
        }

        // Shoots a limb out to localTarget (in this fighter's local space: pivot at the feet, +Z forward,
        // e.g. (0.1, 1.35, 0.8) is a jab at chest height), holds, then pulls back. A new strike on a busy limb
        // starts from wherever the limb is, so a wind-up pose can flow straight into the real strike.
        // Weapon moves the sword hand; the blade follows the shoulder-to-fist line, so a raised fist raises it.
        public void Strike(Limb limb, Vector3 localTarget, float extendTime, float holdTime, float retractTime)
        {
            if (!IsBuilt || dead) return;
            switch (limb)
            {
                case Limb.LeftFist:
                    Begin(leftFistMotion, parts.LeftFist, localTarget, extendTime, holdTime, retractTime);
                    break;
                case Limb.RightFoot:
                    Begin(rightFootMotion, parts.RightFoot, localTarget, extendTime, holdTime, retractTime);
                    break;
                case Limb.LeftFoot:
                    Begin(leftFootMotion, parts.LeftFoot, localTarget, extendTime, holdTime, retractTime);
                    break;
                case Limb.BothFists:
                    Vector3 spread = new Vector3(style.BothFistsSpread, 0f, 0f);
                    Begin(rightFistMotion, parts.RightFist, localTarget + spread, extendTime, holdTime, retractTime);
                    Begin(leftFistMotion, parts.LeftFist, localTarget - spread, extendTime, holdTime, retractTime);
                    break;
                default: // RightFist and Weapon
                    Begin(rightFistMotion, parts.RightFist, localTarget, extendTime, holdTime, retractTime);
                    break;
            }
        }

        // Tilts the upper body (positive = forward, negative = back): quick lean in, slower return.
        public void Lean(float forwardDegrees, float duration)
        {
            if (duration <= 0f || dead) return;
            leanDegrees = forwardDegrees;
            leanDuration = duration;
            leanTime = 0f;
        }

        // Spins the whole body around its feet (use +-360 for a full turn, e.g. a spinning kick). Extra to the spec.
        public void Spin(float degrees, float duration)
        {
            if (duration <= 0f || dead) return;
            spinDegrees = degrees;
            spinDuration = duration;
            spinTime = 0f;
        }

        // Steady glow while an attack winds up, so the player can read it coming. Intensity 0 turns it off;
        // around 1-3 reads well with bloom. Hands and weapon glow fully, the body partly.
        public void SetTelegraph(Color color, float intensity)
        {
            telegraphColor = color;
            telegraphIntensity = Mathf.Max(0f, intensity);
        }

        // A short burst of light that fades out (hit taken, deflect, sweet spot reached).
        public void Flash(Color color, float duration)
        {
            if (duration <= 0f) return;
            flashColor = color;
            flashDuration = duration;
            flashRemaining = duration;
        }

        // Cool tint and soft pulse while invincible (dodge i-frames). Opaque on purpose: runtime transparency
        // would need a different shader setup and sorts badly.
        public void SetInvulnerableLook(bool on)
        {
            invulnerable = on;
        }

        // Wobble and dim while staggered. Starting a stagger interrupts the fighter, so it also pulls the limbs
        // back and turns off any wind-up glow or charge. Safe to call every frame with the current state: a
        // stagger that's already showing keeps going (use RestartStagger for a new stagger on top of it).
        public void SetStaggered(bool on)
        {
            if (on && !staggered) BeginStagger();
            staggered = on;
        }

        // A new stagger while already staggered (poise broken again, or deflected): the wobble starts over from
        // its strongest kick, so the second stagger reads as clearly as the first. Extra to the spec.
        public void RestartStagger()
        {
            BeginStagger();
            staggered = true;
        }

        void BeginStagger()
        {
            staggerTime = 0f;
            if (IsBuilt) ReleaseLimbs();
            telegraphIntensity = 0f;
            chargeLevel = 0f;
            chargeSweetSpot = false;
        }

        public void SetDead(bool isDead)
        {
            if (isDead == dead) return;
            if (isDead)
            {
                dead = true;
                deadTime = 0f;
                if (IsBuilt) ReleaseLimbs();
                leanDuration = 0f;
                spinDuration = 0f;
                telegraphIntensity = 0f;
                chargeLevel = 0f;
                chargeSweetSpot = false;
            }
            else
            {
                ResetPose(); // respawn
            }
        }

        // Heavy-attack charge feedback: the fists glow and grow with level01 (0..1). While sweetSpot is true
        // they burn white-gold, and entering the sweet spot flashes the whole body: the player's release cue.
        public void SetCharge(float level01, bool sweetSpot)
        {
            if (dead) return;
            if (sweetSpot && !chargeSweetSpot) Flash(style.SweetSpotColor, style.SweetSpotFlashTime);
            chargeLevel = Mathf.Clamp01(level01);
            chargeSweetSpot = sweetSpot;
        }

        // Snaps back to the rest pose and clears every effect (strikes, glows, stagger, death). Extra to the spec.
        public void ResetPose()
        {
            rightFistMotion.Cancel();
            leftFistMotion.Cancel();
            rightFootMotion.Cancel();
            leftFootMotion.Cancel();
            leanDuration = 0f;
            spinDuration = 0f;
            telegraphIntensity = 0f;
            flashRemaining = 0f;
            invulnerable = false;
            staggered = false;
            dead = false;
            chargeLevel = 0f;
            chargeSweetSpot = false;
            if (!IsBuilt) return;
            ApplyPose(0f);
            ApplyColours(true);
        }

        void Awake()
        {
            PrepareRuntime();
        }

        void Update()
        {
            if (!IsBuilt) return;
            if (!runtimeReady) PrepareRuntime();
            float dt = Time.deltaTime;
            if (dt <= 0f) return; // paused: hold the pose
            animTime += dt;
            flashRemaining = Mathf.Max(0f, flashRemaining - dt);
            if (staggered) staggerTime += dt;
            if (dead) deadTime += dt;
            ApplyPose(dt);
            ApplyColours(false);
        }

        void OnDestroy()
        {
            if (!ownsMaterials) return;
            GreyboxShapes.SafeDestroy(bodyMaterial);
            GreyboxShapes.SafeDestroy(limbMaterial);
        }

        void PrepareRuntime()
        {
            if (!Application.isPlaying || !IsBuilt) return;
            PrepareMaterials();
            AssignMaterials();
            runtimeReady = true;
            ResetPose();
        }

        // In play mode the rig always works on its own copies: a fighter duplicated in the editor shares
        // materials, and glowing one must not light up the other (or change a saved asset).
        void PrepareMaterials()
        {
            if (Application.isPlaying && !ownsMaterials)
            {
                if (bodyMaterial != null) bodyMaterial = new Material(bodyMaterial);
                if (limbMaterial != null) limbMaterial = new Material(limbMaterial);
                ownsMaterials = true;
            }
            if (bodyMaterial == null) bodyMaterial = GreyboxShapes.CreateLit("GreyboxRig_Body", bodyColor, true);
            if (limbMaterial == null) limbMaterial = GreyboxShapes.CreateLit("GreyboxRig_Limbs", LimbColor(), true);
            GreyboxShapes.PrepareEmission(bodyMaterial);
            GreyboxShapes.PrepareEmission(limbMaterial);
            coloursWritten = false;
        }

        void AssignMaterials()
        {
            for (int i = 0; i < parts.BodyRenderers.Length; i++)
            {
                if (parts.BodyRenderers[i] != null) parts.BodyRenderers[i].sharedMaterial = bodyMaterial;
            }
            for (int i = 0; i < parts.LimbRenderers.Length; i++)
            {
                if (parts.LimbRenderers[i] != null) parts.LimbRenderers[i].sharedMaterial = limbMaterial;
            }
        }

        void RemoveOldVisual()
        {
            if (parts != null && parts.Visual != null) DestroyVisual(parts.Visual.gameObject);
            // Also catch a body left behind if the component was removed and added again.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name == GreyboxRigParts.VisualName) DestroyVisual(child.gameObject);
            }
            parts = new GreyboxRigParts();
            fistBlockSet = false;
        }

        static void DestroyVisual(GameObject visual)
        {
            visual.SetActive(false); // Destroy is deferred to the end of the frame in play mode; hide it now
            GreyboxShapes.SafeDestroy(visual);
        }

        void Begin(GreyboxLimbMotion motion, Transform anchor, Vector3 rootTarget, float extend, float hold, float retract)
        {
            // Convert once from fighter space into the limb's parent space, so the strike leans and spins with the body.
            Vector3 target = anchor.parent.InverseTransformPoint(transform.TransformPoint(rootTarget));
            motion.Begin(anchor.localPosition, target, extend, hold, retract);
        }

        void ReleaseLimbs()
        {
            float retract = style.InterruptRetractTime;
            rightFistMotion.Release(parts.RightFist.localPosition, retract);
            leftFistMotion.Release(parts.LeftFist.localPosition, retract);
            rightFootMotion.Release(parts.RightFoot.localPosition, retract);
            leftFootMotion.Release(parts.LeftFoot.localPosition, retract);
        }

        void ApplyPose(float dt)
        {
            parts.RightFist.localPosition = rightFistMotion.Evaluate(dt, GreyboxRigParts.RightFistRest);
            parts.LeftFist.localPosition = leftFistMotion.Evaluate(dt, GreyboxRigParts.LeftFistRest);
            parts.RightFoot.localPosition = rightFootMotion.Evaluate(dt, GreyboxRigParts.RightFootRest);
            parts.LeftFoot.localPosition = leftFootMotion.Evaluate(dt, GreyboxRigParts.LeftFootRest);
            if (parts.Weapon != null) AimSword();

            Vector3 fistScale = GreyboxRigParts.FistScale * (1f + chargeLevel * style.ChargeFistGrow);
            parts.RightFistMesh.localScale = fistScale;
            parts.LeftFistMesh.localScale = fistScale;

            float lean = 0f;
            if (leanDuration > 0f)
            {
                leanTime += dt;
                float u = leanTime / leanDuration;
                if (u >= 1f) leanDuration = 0f;
                else lean = leanDegrees * LeanEnvelope(u);
            }
            parts.Torso.localRotation = Quaternion.Euler(lean, 0f, 0f);

            float spin = 0f;
            if (spinDuration > 0f)
            {
                spinTime += dt;
                float u = spinTime / spinDuration;
                if (u >= 1f) spinDuration = 0f;
                else spin = spinDegrees * GreyboxLimbMotion.EaseOutCubic(u);
            }

            float wobbleX = 0f, wobbleZ = 0f;
            if (staggered && !dead)
            {
                float settle = style.StaggerSettleTime > 0f ? Mathf.Clamp01(staggerTime / style.StaggerSettleTime) : 1f;
                float amplitude = style.StaggerWobbleDegrees * Mathf.Lerp(style.StaggerKick, 1f, settle);
                float phase = staggerTime * style.StaggerWobbleRate * TwoPi;
                wobbleX = -Mathf.Abs(Mathf.Sin(phase)) * amplitude;          // rocks back, as if knocked off balance
                wobbleZ = Mathf.Sin(phase * 0.5f) * amplitude * 0.6f;         // and sways side to side
            }

            float topple = 0f, lift = 0f;
            if (dead)
            {
                float u = style.DeadToppleTime > 0f ? Mathf.Clamp01(deadTime / style.DeadToppleTime) : 1f;
                float fall = u * u; // accelerates like a real fall
                topple = -style.DeadToppleDegrees * fall; // negative pitch tips backwards
                lift = style.DeadLift * fall;
            }
            parts.Visual.localRotation = Quaternion.Euler(topple + wobbleX, spin, wobbleZ);
            parts.Visual.localPosition = new Vector3(0f, lift, 0f);
        }

        // The blade continues the line from the shoulder through the fist, so any fist path reads as a swing.
        void AimSword()
        {
            Vector3 direction = parts.RightFist.localPosition - style.SwordShoulder;
            if (direction.sqrMagnitude < 1e-6f) direction = Vector3.forward;
            direction.Normalize();
            Vector3 up = Mathf.Abs(direction.y) > 0.98f ? Vector3.back : Vector3.up;
            parts.RightFist.localRotation = Quaternion.LookRotation(direction, up);
        }

        float LeanEnvelope(float u)
        {
            float attack = Mathf.Clamp(style.LeanAttackShare, 0.05f, 0.95f);
            if (u < attack) return GreyboxLimbMotion.EaseOutCubic(u / attack);
            return 1f - GreyboxLimbMotion.SmoothStep((u - attack) / (1f - attack));
        }

        Color LimbColor()
        {
            Color limb = bodyColor * style.LimbShade;
            limb.a = 1f;
            return limb;
        }

        void ApplyColours(bool force)
        {
            float dim = 1f;
            if (staggered) dim *= style.StaggerDim;
            if (dead) dim *= style.DeadDim;
            Color body = bodyColor * dim;
            Color limb = LimbColor() * dim;
            Color bodyEmission = Color.black;
            Color limbEmission = Color.black;
            Color fistExtra = Color.black;

            if (!dead)
            {
                if (telegraphIntensity > 0f)
                {
                    Color glow = telegraphColor * telegraphIntensity;
                    limbEmission += glow;
                    bodyEmission += glow * style.TelegraphBodyShare;
                }
                if (invulnerable)
                {
                    body = Color.Lerp(body, style.InvulnerableTint, style.InvulnerableTintAmount);
                    limb = Color.Lerp(limb, style.InvulnerableTint, style.InvulnerableTintAmount);
                    float pulse = 0.5f + 0.5f * Mathf.Sin(animTime * style.InvulnerablePulseRate * TwoPi);
                    Color glow = style.InvulnerableTint * (style.InvulnerableGlow * pulse);
                    bodyEmission += glow;
                    limbEmission += glow;
                }
                if (chargeSweetSpot)
                {
                    float pulse = 0.75f + 0.25f * Mathf.Sin(animTime * style.SweetSpotPulseRate * TwoPi);
                    fistExtra = style.SweetSpotColor * (style.SweetSpotIntensity * pulse);
                }
                else if (chargeLevel > 0f)
                {
                    fistExtra = style.ChargeColor * (style.ChargeIntensity * chargeLevel);
                }
            }
            if (flashRemaining > 0f && flashDuration > 0f)
            {
                float k = flashRemaining / flashDuration;
                Color glow = flashColor * (style.FlashIntensity * k * k); // bright snap, quick falloff
                bodyEmission += glow;
                limbEmission += glow;
            }
            body.a = 1f;
            limb.a = 1f;

            if (force || !coloursWritten || body != lastBody || bodyEmission != lastBodyEmission)
            {
                GreyboxShapes.SetBaseColor(bodyMaterial, body);
                GreyboxShapes.SetEmission(bodyMaterial, bodyEmission);
            }
            if (force || !coloursWritten || limb != lastLimb || limbEmission != lastLimbEmission)
            {
                GreyboxShapes.SetBaseColor(limbMaterial, limb);
                GreyboxShapes.SetEmission(limbMaterial, limbEmission);
            }
            ApplyFistGlow(force, limb, limbEmission, fistExtra);
            lastBody = body;
            lastBodyEmission = bodyEmission;
            lastLimb = limb;
            lastLimbEmission = limbEmission;
            coloursWritten = true;
        }

        // Only the fists show the charge, so they get a property block (a per-renderer override) on top of the
        // shared limb material while charging, and lose it again afterwards.
        void ApplyFistGlow(bool force, Color limb, Color limbEmission, Color fistExtra)
        {
            bool wanted = fistExtra.maxColorComponent > 0f;
            if (!wanted)
            {
                if (fistBlockSet || force)
                {
                    for (int i = 0; i < parts.FistRenderers.Length; i++)
                    {
                        if (parts.FistRenderers[i] != null) parts.FistRenderers[i].SetPropertyBlock(null);
                    }
                    fistBlockSet = false;
                }
                return;
            }
            Color emission = limbEmission + fistExtra;
            if (!force && fistBlockSet && emission == lastFistEmission && limb == lastLimb) return;
            if (fistBlock == null) fistBlock = new MaterialPropertyBlock();
            emission.a = 1f;
            fistBlock.SetColor(GreyboxShapes.BaseColorId, limb);
            fistBlock.SetColor(GreyboxShapes.EmissionColorId, emission);
            for (int i = 0; i < parts.FistRenderers.Length; i++)
            {
                if (parts.FistRenderers[i] != null) parts.FistRenderers[i].SetPropertyBlock(fistBlock);
            }
            lastFistEmission = emission;
            fistBlockSet = true;
        }
    }
}
