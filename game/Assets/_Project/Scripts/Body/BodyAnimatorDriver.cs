using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Animates a HumanoidBody with the procedural martial-arts animator (FighterAnimator, pure C#).
    //
    // Each frame:
    //   Update (the fighter's controller already ran):  the controller has handed over what the fighter is doing
    //     (SetInput, built by PlayerAnimationFeed / EnemyAnimationFeed from the combat rules); the animator turns it
    //     into a pose and the cues a pack-clip player reads.
    //   LateUpdate (order 50, after MecanimPoseSource at 45): the pose is written to the bones, unless a pack clip
    //     already posed them this frame (IHumanoidRig.ExternalPoseActive), and the flag is cleared for next frame.
    // It runs on scaled time, so hitstop freezes the body mid-strike and slow motion slows it.
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HumanoidBody))]
    public class BodyAnimatorDriver : MonoBehaviour
    {
        [Tooltip("Optional: an edited copy of the pose library. Empty = the built-in poses.")]
        [SerializeField] private PoseLibraryAsset poseLibrary;

        HumanoidBody body;
        FighterAnimator animator;
        FighterAnimInput input;
        bool hasInput;
        bool warnedNoInput;

        public AnimationCue ActionCue => animator != null ? animator.ActionCue : default;
        public AnimationCue LocomotionCue => animator != null ? animator.LocomotionCue : default;
        public FighterAnimator Animator => animator;

        public PoseLibraryAsset PoseLibrary
        {
            get => poseLibrary;
            set
            {
                poseLibrary = value;
                animator = null;
            }
        }

        // Called by the fighter's controller every Update with what the body should show.
        public void SetInput(in FighterAnimInput frameInput)
        {
            input = frameInput;
            hasInput = true;
        }

        // Back to the idle stance straight away (spawn, respawn, reset).
        public void ResetPose()
        {
            if (!EnsureAnimator()) return;
            animator.Reset();
            body.ApplyPose(animator.Pose);
        }

        // The body was rebuilt (new bones, maybe a new size): start a fresh animator and stand in guard.
        public void OnBodyRebuilt()
        {
            animator = null;
            ResetPose();
        }

        // Domain reload is off: the built-in library is rebuilt each Play session so code changes show up.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Core.PoseLibrary.ResetDefault();
        }

        void Awake()
        {
            body = GetComponent<HumanoidBody>();
        }

        void Update()
        {
            if (!EnsureAnimator()) return;
            if (!hasInput)
            {
                // Nobody told us anything this frame (a fighter without a controller, or one that's switched off):
                // stand in guard, keeping time.
                if (!warnedNoInput && Application.isPlaying && GetComponent<Combatant>() != null && Time.frameCount > 2)
                {
                    warnedNoInput = true;
                    Debug.LogWarning(name + ": BodyAnimatorDriver got no animation input this frame; showing the idle stance.", this);
                }
                input = new FighterAnimInput { Grounded = true, ActionKey = "" };
            }
            input.DeltaTime = Time.deltaTime;
            animator.Update(input);
            hasInput = false;
        }

        void LateUpdate()
        {
            if (animator == null || body == null) return;
            if (!body.ExternalPoseActive) body.ApplyPose(animator.Pose);
            else body.ApplyProp(animator.Pose);   // a pack clip posed the bones; the weapon angle is still ours
            body.ExternalPoseActive = false;
        }

        bool EnsureAnimator()
        {
            if (body == null) body = GetComponent<HumanoidBody>();
            if (body == null || !body.IsBuilt) return false;
            if (animator != null) return true;
            Core.PoseLibrary library = poseLibrary != null && poseLibrary.Library != null ? poseLibrary.Library : Core.PoseLibrary.Default;
            animator = new FighterAnimator(library, HumanoidSkeleton.Create(null, body.Scale), body.AnimationStyle);
            return true;
        }
    }
}
