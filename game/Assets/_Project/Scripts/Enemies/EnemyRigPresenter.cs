using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Turns what an enemy brain does into grey-box body language on its GreyboxRig. This is where "readable
    // and fair" lives: every attack has a wind-up pose held for its whole telegraph, a glow in the telegraph's
    // colour (yellow = normal, red = heavy or delayed) that builds up and flares just before the strike, a
    // swing that arrives as each hit window opens, and a sword trail while the blade swings in. Hits flash,
    // staggers flash and wobble, deaths flash and topple.
    //
    // The glow's build-up is the timing cue: players learn to dodge or deflect on the flare rather than on
    // the pose, which is what makes the delayed thrust (a held pose that baits panic dodges) fair.
    // The blade shows the reach: when a strike lands, its tip sits on the edge of what the attack can hit
    // (see FitStrikeHand), so nothing hits you from beyond the visible blade.
    //
    // Plain C# owned by an EnemyFighter, which forwards the brain's events and calls Tick once per frame after
    // them. Every rig call is guarded, so a fighter without a built rig simply shows nothing.
    public sealed class EnemyRigPresenter
    {
        enum AimMode { Rest, Tracking, Held }

        const float MinSeconds = 1e-3f;          // an instant (0 s) wind-up or trail must not divide by zero
        const float SwishVertexSpacing = 0.02f;  // metres between trail points: smooth enough, cheap enough
        const float Epsilon = 1e-4f;

        GreyboxRig rig;
        Transform crossbow;
        Renderer[] crossbowRenderers;
        TrailRenderer swish;
        Material swishMaterial;

        // The running attack, from its telegraph until it ends. attackTime follows the brain's own attack clock
        // (0 on the frame the telegraph starts, then + dt each frame), so swings can start just before each hit.
        bool attackRunning;
        bool attackJustStarted;
        bool attackRanged;
        bool telegraphing;                       // still winding up: the glow is on
        float attackTime;
        float telegraphDuration;
        float telegraphPeak;
        Color telegraphColor;
        TelegraphKind attackKind;
        EnemyAttackData attack;
        int nextSwing;                           // the first strike whose swing hasn't started yet
        bool warnedReach;

        AimMode aimMode;
        float aimPitch;
        float heldPitch;

        readonly EnemyPoseSettings defaultPoses = new EnemyPoseSettings(); // if a settings block was cleared from code

        public bool IsTelegraphing => telegraphing;

        // Hooks up the rig (and the crossbow model, if any). In play mode this also creates the sword trail.
        public void Bind(GreyboxRig newRig, Transform newCrossbow, EnemyFeedbackSettings feedback)
        {
            rig = newRig;
            crossbow = newCrossbow;
            crossbowRenderers = crossbow != null ? crossbow.GetComponentsInChildren<Renderer>(true) : null;
            SyncCrossbowMaterial();
            EnemyBuilder.SetWeaponLength(rig, PosesOf(feedback).WeaponLength);
            if (swish == null && feedback.Swish && rig != null && rig.HasWeapon && Application.isPlaying) CreateSwish(feedback);
        }

        // Frees the trail material (the trail object itself goes with the fighter).
        public void Dispose()
        {
            GreyboxShapes.SafeDestroy(swishMaterial);
            swishMaterial = null;
        }

        // ---------------------------------------------------------------- attack events

        public void OnTelegraphStarted(in EnemyEvent e, EnemyFeedbackSettings feedback)
        {
            attackRunning = true;
            attackJustStarted = true;
            attackTime = 0f;
            telegraphing = true;
            telegraphDuration = Mathf.Max(MinSeconds, e.Duration);
            attackKind = e.Telegraph;
            attack = e.Attack;
            attackRanged = e.Move != null && e.Move.LaunchesProjectile;
            nextSwing = 0;
            telegraphColor = feedback.TelegraphColor(e.Telegraph);
            telegraphPeak = Mathf.Max(0f, feedback.TelegraphIntensity(e.Telegraph));
            ApplyTelegraphGlow(feedback);
            if (attackRanged)
            {
                aimMode = AimMode.Tracking;
                SyncCrossbowMaterial();
            }
            if (rig == null) return;

            EnemyPoseSettings poses = PosesOf(feedback);
            EnemyBuilder.SetWeaponLength(rig, poses.WeaponLength); // picks up live edits of the length
            WindUpPose(e.Telegraph, attackRanged, poses, out Vector3 pose, out float lean);
            float rise = Mathf.Clamp(poses.WindUpRiseTime, 0f, telegraphDuration * poses.WindUpRiseMaxShare);
            // Rise, then hold for the whole wind-up: the strike takes over from wherever the hand is, and an
            // interrupted wind-up is sent back to guard by OnAttackEnded.
            rig.Strike(Limb.Weapon, pose, rise, telegraphDuration, poses.ReturnToGuardTime);
            // Lean back while winding up and be upright again just as the swing starts, so the swing lands
            // exactly where FitStrikeHand aimed it.
            float snap = Mathf.Max(0f, poses.StrikeSnapTime);
            if (lean != 0f) rig.Lean(lean, Mathf.Max(rise, telegraphDuration - snap));
        }

        // A melee strike's active window opened: the glow goes out, and the swing starts now unless it already
        // started just before the window (see Tick).
        public void OnStrike(in EnemyEvent e, EnemyFeedbackSettings feedback)
        {
            StopTelegraph();
            if (e.Attack != null) attack = e.Attack;
            attackKind = e.Telegraph;
            if (nextSwing > e.HitIndex) return;
            BeginSwing(e.HitIndex, feedback);
            nextSwing = e.HitIndex + 1;
        }

        public void OnStrikeEnd()
        {
            StopSwish(false);
        }

        public void OnBoltLaunched(in EnemyEvent e, EnemyFeedbackSettings feedback)
        {
            StopTelegraph();
            EnemyPoseSettings poses = PosesOf(feedback);
            float maxPitch = Mathf.Abs(poses.MaxAimPitch);
            heldPitch = Mathf.Clamp(Directions.PitchOf(e.Direction), -maxPitch, maxPitch); // the aim is committed now
            aimMode = AimMode.Held;
            FireVfx.HitSpark(e.Origin.ToUnity(), feedback.ShotSparkColor);
            if (rig == null) return;

            // Kick back with each bolt. During a burst, hold the kicked pose until the next bolt so the crossbow
            // stays up; after the last one, lower it over the recovery.
            EnemyAttackData bolts = e.Attack;
            bool moreToCome = bolts != null && e.HitIndex < bolts.HitCount - 1;
            float hold = moreToCome ? Mathf.Max(0f, bolts.HitInterval) : 0f;
            float lower = moreToCome ? poses.ReturnToGuardTime : RecoveryPullBack(e.Move, poses);
            float kick = Mathf.Max(0f, poses.RecoilKickTime);
            rig.Strike(Limb.Weapon, poses.AimPose + poses.RecoilOffset, kick, hold, lower);
            if (poses.RecoilLean != 0f && kick > 0f) rig.Lean(poses.RecoilLean, kick / LeanPeakShare());
        }

        // Recovery finished, or the attack was cut short (lost interest, stagger, death, reset).
        public void OnAttackEnded(EnemyFeedbackSettings feedback)
        {
            bool cutShortInWindUp = telegraphing;
            attackRunning = false;
            StopTelegraph();
            StopSwish(false);
            aimMode = AimMode.Rest;
            // A finished swing is already pulling back on its own; a wind-up that never struck drops to guard.
            if (cutShortInWindUp && rig != null) rig.Strike(Limb.Weapon, GuardPose, PosesOf(feedback).ReturnToGuardTime, 0f, 0f);
        }

        // ---------------------------------------------------------------- reactions

        public void OnAggroed(EnemyFeedbackSettings feedback)
        {
            if (rig != null) rig.Flash(feedback.AggroFlashColor, feedback.AggroFlashTime);
        }

        // busy: the enemy is mid-attack (a flinch would fight the attack's own lean). hitDirection is attacker -> us.
        public void OnDamaged(System.Numerics.Vector3 hitDirection, Vector3 forward, bool busy, EnemyFeedbackSettings feedback)
        {
            if (rig == null) return;
            rig.Flash(feedback.HitFlashColor, feedback.HitFlashTime);
            if (busy || feedback.FlinchDegrees == 0f) return;
            // Knocked away from the blow: back when hit from the front, forward when hit from behind.
            bool fromBehind = Vector3.Dot(hitDirection.ToUnity(), forward) > 0f;
            float degrees = Mathf.Abs(feedback.FlinchDegrees);
            rig.Lean(fromBehind ? degrees : -degrees, feedback.FlinchTime);
        }

        public void OnStaggered(EnemyFeedbackSettings feedback)
        {
            attackRunning = false;
            StopTelegraph();
            StopSwish(false);
            aimMode = AimMode.Rest;
            if (rig == null) return;
            rig.RestartStagger(); // wobble and dim, from the strongest kick even if it was already staggered
            rig.Flash(feedback.StaggerFlashColor, feedback.StaggerFlashTime);
        }

        public void OnStaggerEnded()
        {
            if (rig != null) rig.SetStaggered(false);
        }

        public void OnDied(EnemyFeedbackSettings feedback)
        {
            attackRunning = false;
            StopTelegraph();
            StopSwish(false);
            aimMode = AimMode.Rest;
            if (rig == null) return;
            rig.SetDead(true); // topple and dim
            rig.Flash(feedback.DeathFlashColor, feedback.DeathFlashTime);
        }

        public void OnHealthRefilled(EnemyFeedbackSettings feedback)
        {
            if (rig != null) rig.Flash(feedback.RefillFlashColor, feedback.RefillFlashTime);
        }

        // Back to the rest pose with every effect cleared (respawn / reset).
        public void OnReset()
        {
            attackRunning = false;
            attackJustStarted = false;
            telegraphing = false;
            attack = null;
            nextSwing = 0;
            StopSwish(true);
            aimMode = AimMode.Rest;
            aimPitch = 0f;
            if (crossbow != null) crossbow.localRotation = Quaternion.identity;
            if (rig != null) rig.ResetPose();
        }

        // The fighter was switched off: nothing may keep glowing or trailing.
        public void OnDisabled()
        {
            attackRunning = false;
            StopTelegraph();
            StopSwish(true);
        }

        // ---------------------------------------------------------------- per frame

        // Call once per frame after the brain's events have been handled.
        public void Tick(float dt, in EnemyWorldState world, EnemyFeedbackSettings feedback)
        {
            if (!(dt > 0f)) return; // paused: hold everything
            if (attackRunning)
            {
                if (attackJustStarted) attackJustStarted = false;
                else attackTime += dt;
                if (!attackRanged) StartSwingsEarly(feedback);
                if (telegraphing) ApplyTelegraphGlow(feedback);
            }
            UpdateCrossbowAim(dt, in world, PosesOf(feedback));
        }

        // ---------------------------------------------------------------- helpers

        // Each swing starts a snap-time before its strike's hit window opens, so the blade arrives just as the
        // hit can land instead of teleporting there on the first dangerous frame.
        void StartSwingsEarly(EnemyFeedbackSettings feedback)
        {
            float snap = Mathf.Max(0f, PosesOf(feedback).StrikeSnapTime);
            if (!(snap > 0f) || attack == null || attack.Move == null) return;
            int hits = Mathf.Max(1, attack.HitCount);
            float interval = Mathf.Max(0f, attack.HitInterval);
            while (nextSwing < hits && attackTime >= attack.Move.Startup + nextSwing * interval - snap)
            {
                BeginSwing(nextSwing, feedback);
                nextSwing++;
            }
        }

        void BeginSwing(int hitIndex, EnemyFeedbackSettings feedback)
        {
            if (rig == null) return;
            EnemyPoseSettings poses = PosesOf(feedback);
            MoveData move = attack != null ? attack.Move : null;
            StrikeAim(attackKind, hitIndex, poses, out Vector2 aim, out float lean);
            Vector3 hand = FitStrikeHand(aim, move, poses);
            float snap = Mathf.Max(0f, poses.StrikeSnapTime);
            float active = move != null ? Mathf.Max(0f, move.Active) : 0f;
            float interval = attack != null ? Mathf.Max(0f, attack.HitInterval) : 0f;
            // Between the strikes of a combo, drift back until the next one; after the last, pull back over the
            // recovery (the slow pull-back is the visible punish window).
            bool moreToCome = attack != null && hitIndex < attack.HitCount - 1;
            float after = moreToCome ? Mathf.Max(0f, interval - active) : RecoveryPullBack(move, poses);
            rig.Strike(Limb.Weapon, hand, snap, active, after);
            // Lean into the strike. Mid-combo the lean is upright again by the next swing, so that swing lands
            // where it's aimed too.
            float leanTime = moreToCome ? interval : (snap + active) / LeanPeakShare();
            if (lean != 0f && leanTime > 0f) rig.Lean(lean, leanTime);
            StartSwish(feedback);
        }

        // Where the weapon hand must go (fighter space) so the blade tip lands exactly on the edge of the attack's
        // hit area. The rig points the blade along the line from the shoulder through the hand, so the tip sits at
        // shoulder + (arm + blade) * direction. The hit area is a circle of radius Range around the strike origin,
        // OriginForward ahead of the feet (the hit test adds the target's radius, so a tip on that circle touches
        // the body of a target at the very edge of the reach). Solving |shoulder + d * direction - origin| = Range
        // for d (flat, ignoring height) gives the tip distance; the arm is what's left after the blade.
        Vector3 FitStrikeHand(Vector2 aim, MoveData move, EnemyPoseSettings poses)
        {
            Vector3 shoulder = GreyboxRigParts.TorsoPivot + rig.Style.SwordShoulder;
            Vector3 direction = Directions.FromYawPitch(aim.x, aim.y).ToUnity();
            float armMin = Mathf.Max(0f, poses.StrikeArmMin);
            float armMax = Mathf.Max(armMin, poses.StrikeArmMax);
            float flatSq = direction.x * direction.x + direction.z * direction.z;
            if (move == null || !(flatSq > Epsilon)) return shoulder + direction * armMin; // straight down: nothing to fit

            float range = Mathf.Max(0f, move.Range);
            float fromOriginX = shoulder.x;
            float fromOriginZ = shoulder.z - move.OriginForward;
            float b = 2f * (direction.x * fromOriginX + direction.z * fromOriginZ);
            float c = fromOriginX * fromOriginX + fromOriginZ * fromOriginZ - range * range;
            float tipDistance = (-b + Mathf.Sqrt(Mathf.Max(0f, b * b - 4f * flatSq * c))) / (2f * flatSq);
            float arm = tipDistance - BladeLength();
            if (arm < armMin || arm > armMax)
            {
                WarnReachOnce(move, arm, armMin, armMax);
                arm = Mathf.Clamp(arm, armMin, armMax);
            }
            return shoulder + direction * arm;
        }

        // Hand to tip, as the rig actually draws it (0 without a weapon: the fist itself is the tip).
        float BladeLength()
        {
            if (!rig.HasWeapon) return 0f;
            Transform tip = rig.GetAnchor(Limb.Weapon);
            Transform hand = rig.GetAnchor(Limb.RightFist);
            return tip != hand ? Vector3.Distance(hand.position, tip.position) : 0f;
        }

        void WarnReachOnce(MoveData move, float arm, float armMin, float armMax)
        {
            if (warnedReach) return;
            warnedReach = true;
            Debug.LogWarning(string.Format(CultureInfo.InvariantCulture,
                "{0}: the blade can't show the reach of '{1}' exactly (the hand would have to be {2:0.00} m from the shoulder; "
                + "the limit is {3:0.00}-{4:0.00} m). Change WeaponLength (or StrikeArmMin/Max) in its EnemyTuningAsset: Feedback > Poses.",
                rig.name, move.DisplayName, arm, armMin, armMax), rig);
        }

        void ApplyTelegraphGlow(EnemyFeedbackSettings feedback)
        {
            if (rig == null) return;
            float u = Mathf.Clamp01(attackTime / telegraphDuration);
            float intensity = telegraphPeak * Mathf.Lerp(feedback.TelegraphStartShare, 1f, u * u);
            if (feedback.TelegraphFlareTime > 0f && telegraphDuration - attackTime <= feedback.TelegraphFlareTime)
                intensity *= Mathf.Max(1f, feedback.TelegraphFlareBoost);
            rig.SetTelegraph(telegraphColor, intensity);
        }

        void StopTelegraph()
        {
            telegraphing = false;
            if (rig != null) rig.SetTelegraph(telegraphColor, 0f);
        }

        void UpdateCrossbowAim(float dt, in EnemyWorldState world, EnemyPoseSettings poses)
        {
            if (crossbow == null) return;
            if (aimMode == AimMode.Rest && aimPitch == 0f) return; // resting: leave the transform alone
            float maxPitch = Mathf.Abs(poses.MaxAimPitch);
            float target = 0f;
            if (aimMode == AimMode.Held) target = heldPitch;
            else if (aimMode == AimMode.Tracking && world.HasTarget)
                target = Mathf.Clamp(Directions.PitchOf(world.TargetAimPoint - crossbow.position.ToNumerics()), -maxPitch, maxPitch);
            aimPitch = Mathf.MoveTowards(aimPitch, target, Mathf.Max(0f, poses.AimTurnRate) * dt);
            crossbow.localRotation = Quaternion.Euler(aimPitch, 0f, 0f);
        }

        // The crossbow shares the rig's limb material so it glows with the telegraph. In play mode the rig swaps
        // in its own material copies (in its Awake), so the crossbow is pointed at the current one again.
        void SyncCrossbowMaterial()
        {
            if (crossbowRenderers == null || rig == null) return;
            Material limbs = rig.LimbMaterial;
            if (limbs == null) return;
            for (int i = 0; i < crossbowRenderers.Length; i++)
            {
                Renderer part = crossbowRenderers[i];
                if (part != null && part.sharedMaterial != limbs) part.sharedMaterial = limbs;
            }
        }

        void CreateSwish(EnemyFeedbackSettings feedback)
        {
            Transform tip = rig.GetAnchor(Limb.Weapon);
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
            swish.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)); // tapers to a point
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
            GreyboxShapes.SetBaseColor(swishMaterial, feedback.SwishColor); // read live, like the rest of the feel data
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
            return feedback.Poses ?? defaultPoses;
        }

        float LeanPeakShare()
        {
            // GreyboxRig.Lean peaks after this share of its duration (the rig clamps it the same way), so
            // dividing a time by it makes the lean peak exactly then.
            return Mathf.Clamp(rig.Style.LeanAttackShare, 0.05f, 0.95f);
        }

        static float RecoveryPullBack(MoveData move, EnemyPoseSettings poses)
        {
            return move != null ? Mathf.Max(0f, move.Recovery * poses.RecoverShare) : poses.ReturnToGuardTime;
        }

        // The rig's rest position for the weapon hand, in fighter space.
        static Vector3 GuardPose => GreyboxRigParts.TorsoPivot + GreyboxRigParts.RightFistRest;

        static void WindUpPose(TelegraphKind kind, bool ranged, EnemyPoseSettings poses, out Vector3 pose, out float lean)
        {
            if (ranged)
            {
                pose = poses.AimPose;
                lean = 0f;
                return;
            }
            switch (kind)
            {
                case TelegraphKind.Heavy:
                    pose = poses.OverheadWindUp;
                    lean = poses.OverheadWindUpLean;
                    return;
                case TelegraphKind.Delayed:
                    pose = poses.ThrustWindUp;
                    lean = poses.ThrustWindUpLean;
                    return;
                default:
                    pose = poses.SlashWindUp;
                    lean = poses.SlashWindUpLean;
                    return;
            }
        }

        static void StrikeAim(TelegraphKind kind, int hitIndex, EnemyPoseSettings poses, out Vector2 aim, out float lean)
        {
            if (hitIndex % 2 == 1)
            {
                aim = poses.BackhandStrikeAim; // combos alternate direction, so each strike reads separately
                lean = poses.SlashStrikeLean;
                return;
            }
            switch (kind)
            {
                case TelegraphKind.Heavy:
                    aim = poses.OverheadStrikeAim;
                    lean = poses.OverheadStrikeLean;
                    return;
                case TelegraphKind.Delayed:
                    aim = poses.ThrustStrikeAim;
                    lean = poses.ThrustStrikeLean;
                    return;
                default:
                    aim = poses.SlashStrikeAim;
                    lean = poses.SlashStrikeLean;
                    return;
            }
        }
    }
}
