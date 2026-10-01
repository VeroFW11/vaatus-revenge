using System;
using System.Collections.Generic;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Dresses a fighter in an imported, rigged character model (the painted player Avatar, a GLB) while everything
    // underneath stays exactly as it was: the procedural HumanoidBody keeps its bones, the procedural animator and
    // any pack clips (MecanimPoseSource) keep posing them, hit boxes, effect anchors and the camera keep reading them.
    // Only the procedural body's shapes are hidden, and every frame its pose is copied onto the model.
    //
    // How the copy works, for first-time readers:
    //   - The two skeletons don't match bone for bone: different bone names, different proportions, different rest
    //     poses (ours is a T-pose; the model was made in an A-pose) and different bone axes. Copying rotations
    //     straight across would twist every limb.
    //   - Unity's Mecanim solves exactly this for animation clips ("Humanoid retargeting"). A Humanoid Avatar
    //     describes a skeleton as a human: which bone is the left forearm, and how the body stands at rest. With an
    //     Avatar for each skeleton, a HumanPoseHandler reads a pose from one as a set of human "muscle" values (elbow
    //     bent this much, spine twisted that much) and writes the same muscles to the other, whatever its bones.
    //   - Both Avatars are built here at runtime (AvatarBuilder), like MecanimPoseSource does: ours from the body's
    //     T-pose, the model's from its bones after mapping them (SkinnedRigMapper; the spine by hierarchy) and
    //     straightening its A-pose into a T-pose (SkinnedAvatarBuilder.PoseAsTPose).
    //   - Before that the model is stood on the fighter's feet, facing +Z, at the procedural body's height
    //     (SkinnedModelFit), and any animation it came with is switched off.
    //
    // If anything is wrong (no model, a missing bone, an Avatar Unity rejects) it logs one warning that says what,
    // hides the model and leaves the procedural body showing: the game is never left without a visible player.
    //
    // Timing: LateUpdate at ExecutionOrder, after the body's bones are posed for this frame (the driver and the body
    // run at 50, pack clips at 45) and before effects (60) and the camera (100).
    [DefaultExecutionOrder(ExecutionOrder)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HumanoidBody))]
    public class SkinnedAvatarMirror : MonoBehaviour
    {
        public const int ExecutionOrder = 55;
        public const string ModelRootName = "AvatarModel";

        [Tooltip("The container holding the imported model (an empty child of the fighter with the model inside it). "
                 + "Vaatu's Revenge > Use Player Avatar Model sets this up.")]
        [SerializeField] private Transform modelRoot;
        [Tooltip("1 = the model's head is exactly as high as the procedural body's. Nudge it if the model looks too big or small.")]
        [SerializeField, Range(0.8f, 1.25f)] private float heightScale = 1f;
        [Tooltip("How the procedural body's glows and darkening show on the model.")]
        [SerializeField] private SkinnedAvatarTint.Settings tint = new SkinnedAvatarTint.Settings();

        // One warning per session, not one per frame or per fighter (domain reload is off, so reset on play).
        static bool warnedFailure;
        static bool warnedException;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            warnedFailure = false;
            warnedException = false;
        }

        enum State { Waiting, Ready, Failed }

        HumanoidBody body;
        State state = State.Waiting;
        Avatar sourceAvatar, targetAvatar;      // built at runtime, destroyed with this component
        HumanPoseHandler sourcePose, targetPose;
        HumanPose pose;
        Transform boundHips;                    // the procedural Hips the source Avatar was built for
        SkinnedMeshRenderer[] skins = Array.Empty<SkinnedMeshRenderer>();
        readonly List<Renderer> hiddenParts = new List<Renderer>();
        SkinnedAvatarTint tintLayer;

        public Transform ModelRoot
        {
            get => modelRoot;
            set
            {
                if (modelRoot == value) return;
                modelRoot = value;
                if (isActiveAndEnabled) { Teardown(); state = State.Waiting; }
            }
        }

        public float HeightScale
        {
            get => heightScale;
            set => heightScale = value;
        }

        // For debug panels and the "did it work" check: the model is on screen and following the body.
        public bool IsShowingModel => state == State.Ready;
        public string Problem { get; private set; }

        void OnEnable()
        {
            state = State.Waiting;
            Problem = null;
        }

        void OnDisable()
        {
            // Switching the component off is the in-game way back to the procedural body.
            Teardown();
            if (modelRoot != null) modelRoot.gameObject.SetActive(false);
            state = State.Waiting;
        }

        void OnDestroy()
        {
            Teardown();
        }

        void LateUpdate()
        {
            if (state == State.Failed) return;
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                // Never let a presentation problem break the frame: back to the procedural body for good.
                if (!warnedException)
                {
                    warnedException = true;
                    Debug.LogWarning("SkinnedAvatarMirror on '" + name + "' stopped and fell back to the procedural body: " + e, this);
                }
                Fail(null);
            }
        }

        void Tick()
        {
            if (state == State.Waiting && !TryInitialise()) return;
            if (body.GetBone(HumanBodyBones.Hips) != boundHips && !RebindSource()) return;   // the body was rebuilt

            // The model stands where the procedural body stands (its Root is the fighter's feet).
            Transform root = body.Root;
            modelRoot.SetPositionAndRotation(root.position, root.rotation);

            sourcePose.GetHumanPose(ref pose);
            targetPose.SetHumanPose(ref pose);
            tintLayer?.Update();
        }

        // ---------------------------------------------------------------------------------------------------------
        // Set-up
        // ---------------------------------------------------------------------------------------------------------

        bool TryInitialise()
        {
            if (body == null) body = GetComponent<HumanoidBody>();
            if (body == null || !body.IsBuilt || body.Root == null) return false;   // not built yet: try next frame
            if (modelRoot == null) return Fail("no model is assigned (run Vaatu's Revenge > Use Player Avatar Model)");

            modelRoot.gameObject.SetActive(true);
            modelRoot.SetPositionAndRotation(body.Root.position, body.Root.rotation);
            skins = ModelSkins(modelRoot);
            if (skins.Length == 0) return Fail("'" + modelRoot.name + "' has no skinned mesh (was the model exported with its armature and skin?)");

            SkinnedAvatarBuilder.SilenceOwnAnimation(modelRoot);
            SkinnedAvatarBuilder.RestoreBindPose(skins);
            if (!SkinnedAvatarBuilder.MapBones(modelRoot, out Transform[] bones, out string report))
                return Fail("its bones don't make a human skeleton: " + report);

            SkinnedFitResult fit = SkinnedAvatarBuilder.FitModel(modelRoot, bones, skins, SkinnedAvatarBuilder.RestHeadHeight(body), heightScale);
            SkinnedAvatarBuilder.PoseAsTPose(modelRoot, bones);

            targetAvatar = SkinnedAvatarBuilder.BuildAvatar(modelRoot, bones, CurrentPose, out string problem);
            if (targetAvatar == null) return Fail("Unity couldn't build a Humanoid Avatar for it (" + problem + ")");
            targetPose = new HumanPoseHandler(targetAvatar, modelRoot);

            if (!RebindSource()) return false;
            if (!tintLayer.HasSamples)
                Debug.LogWarning("Player avatar model: couldn't find the procedural body's shapes to read hit flashes and glows "
                                 + "from (" + SkinnedAvatarTint.BodySampleName + "); the model shows its own colours only.", this);

            state = State.Ready;
            Debug.Log("Player avatar model on for '" + name + "': " + skins.Length + " skinned mesh(es), "
                      + Describe(fit) + ". Untick SkinnedAvatarMirror to see the procedural body again.", this);
            return true;
        }

        // (Re)builds the procedural side: its Avatar and pose reader, and hides its shapes. Needed again whenever the
        // body is rebuilt, because Build makes new bones and new shapes.
        bool RebindSource()
        {
            ShowProceduralBody();
            sourcePose?.Dispose();
            sourcePose = null;
            if (sourceAvatar != null) GreyboxShapes.SafeDestroy(sourceAvatar);
            sourceAvatar = null;

            Transform[] bones = SkinnedAvatarBuilder.BonesOf(body);
            sourceAvatar = SkinnedAvatarBuilder.BuildAvatar(body.Root, bones, ProceduralRestPose, out string problem);
            if (sourceAvatar == null) return Fail("the procedural body's Avatar couldn't be built (" + problem + ")");
            sourcePose = new HumanPoseHandler(sourceAvatar, body.Root);
            boundHips = body.GetBone(HumanBodyBones.Hips);
            HideProceduralBody();

            // The look layer reads the body's (new) shapes.
            tintLayer?.Dispose();
            tintLayer = new SkinnedAvatarTint(body.Root, skins, tint);
            return true;
        }

        void ProceduralRestPose(BodyJoint joint, Transform bone, out Vector3 localPosition, out Quaternion localRotation)
        {
            SkinnedAvatarBuilder.TryHuman(joint, out HumanBodyBones human);
            localPosition = body.GetRestLocalPosition(human);
            localRotation = body.GetRestLocalRotation(human);
        }

        static void CurrentPose(BodyJoint joint, Transform bone, out Vector3 localPosition, out Quaternion localRotation)
        {
            localPosition = bone.localPosition;
            localRotation = bone.localRotation;
        }

        // Hides the procedural shapes, keeping a held weapon visible (it rides the procedural hand, which the model's
        // hand follows). Only shapes that exist now are touched, so effects parented later are never hidden.
        void HideProceduralBody()
        {
            hiddenParts.Clear();
            Transform weapon = body.WeaponPivot;
            foreach (Renderer r in body.Root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) || !r.enabled) continue;
                if (weapon != null && r.transform.IsChildOf(weapon)) continue;
                r.enabled = false;
                hiddenParts.Add(r);
            }
        }

        void ShowProceduralBody()
        {
            for (int i = 0; i < hiddenParts.Count; i++)
                if (hiddenParts[i] != null) hiddenParts[i].enabled = true;
            hiddenParts.Clear();
        }

        bool Fail(string reason)
        {
            if (reason != null)
            {
                Problem = reason;
                if (!warnedFailure)
                {
                    warnedFailure = true;
                    Debug.LogWarning("Player avatar model is off for '" + name + "': " + reason + ". Showing the procedural body instead.", this);
                }
            }
            Teardown();
            if (modelRoot != null) modelRoot.gameObject.SetActive(false);
            state = State.Failed;
            return false;
        }

        void Teardown()
        {
            ShowProceduralBody();
            if (tintLayer != null) tintLayer.Dispose();
            tintLayer = null;
            sourcePose?.Dispose();
            targetPose?.Dispose();
            sourcePose = targetPose = null;
            if (sourceAvatar != null) GreyboxShapes.SafeDestroy(sourceAvatar);
            if (targetAvatar != null) GreyboxShapes.SafeDestroy(targetAvatar);
            sourceAvatar = targetAvatar = null;
            boundHips = null;
        }

        // The model's own skinned meshes (not the glow copies SkinnedAvatarTint adds next to them).
        static SkinnedMeshRenderer[] ModelSkins(Transform root)
        {
            var list = new List<SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (!smr.name.EndsWith(SkinnedAvatarTint.OverlaySuffix, StringComparison.Ordinal)) list.Add(smr);
            return list.ToArray();
        }

        static string Describe(SkinnedFitResult fit)
        {
            if (fit.IsIdentity) return "already upright, facing forward and to scale";
            string text = "scaled " + fit.Scale.ToString("0.###") + "x";
            if (Mathf.Abs(fit.FacingDegrees) > 0.05f) text += ", turned " + fit.FacingDegrees.ToString("0.#") + " degrees to face forward";
            if (fit.UpWasSnapped) text += ", stood upright (it was exported lying down)";
            return text;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Edit mode (the "Use Player Avatar Model" menu)
        // ---------------------------------------------------------------------------------------------------------

        // Checks the model's bones and stands it on the fighter (upright, facing forward, to scale) so it looks right
        // in the Scene view before Play. Changes only the model's top-level transforms; Play repeats the fit, which
        // then changes nothing. False with a readable reason when the model can't be used.
        public bool PrepareModel(out string report)
        {
            if (body == null) body = GetComponent<HumanoidBody>();
            if (modelRoot == null) { report = "no model is assigned"; return false; }
            if (body == null || !body.IsBuilt) { report = "the fighter's procedural body isn't built"; return false; }
            SkinnedMeshRenderer[] found = ModelSkins(modelRoot);
            if (found.Length == 0) { report = "'" + modelRoot.name + "' has no skinned mesh (was the model exported with its armature and skin?)"; return false; }

            SkinnedAvatarBuilder.SilenceOwnAnimation(modelRoot);
            modelRoot.SetPositionAndRotation(body.Root.position, body.Root.rotation);
            if (!SkinnedAvatarBuilder.MapBones(modelRoot, out Transform[] bones, out string mapping))
            {
                report = "its bones don't make a human skeleton: " + mapping;
                return false;
            }
            SkinnedFitResult fit = SkinnedAvatarBuilder.FitModel(modelRoot, bones, found, SkinnedAvatarBuilder.RestHeadHeight(body), heightScale);
            report = found.Length + " skinned mesh(es), " + Describe(fit) + ". " + mapping;
            return true;
        }
    }
}
