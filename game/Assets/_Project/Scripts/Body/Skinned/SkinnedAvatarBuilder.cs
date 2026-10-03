using System;
using System.Collections.Generic;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The Unity half of putting an imported character model on a fighter (SkinnedAvatarMirror uses it at Play; the
    // "Use Player Avatar Model" menu uses it in edit mode to check a model before David presses Play):
    //   - MapBones:         which of the model's transforms is the hips, the left forearm... (SkinnedRigMapper, pure C#)
    //   - SilenceOwnAnimation: an exported model can carry its own clip; it must never play over our animation
    //   - RestoreBindPose:  put the bones back exactly where the mesh was skinned (the undeformed model)
    //   - FitModel:         stand it up, face it +Z, size it to the procedural body, feet on the ground (SkinnedModelFit)
    //   - PoseAsTPose:      straighten the arms out sideways and the legs down, which is the rest pose Mecanim expects
    //   - BuildAvatar:      a Humanoid Avatar from a bone map (the same recipe as MecanimPoseSource)
    // No glTF-specific code: the model is whatever GameObject the importer (glTFast for .glb, Unity for .fbx) made.
    public static class SkinnedAvatarBuilder
    {
        // Bones closer together than this can't define a direction (a zero-length bone).
        const float MinBoneLength = 1e-4f;

        public delegate void RestPoseOf(BodyJoint joint, Transform bone, out Vector3 localPosition, out Quaternion localRotation);

        // Every transform under root (root itself excluded) with names and parent indices for the mapper.
        public static void CollectHierarchy(Transform root, List<Transform> transforms, List<string> names, List<int> parents)
        {
            transforms.Clear();
            names.Clear();
            parents.Clear();
            var indexOf = new Dictionary<Transform, int>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                indexOf[t] = transforms.Count;
                transforms.Add(t);
                names.Add(t.name);
            }
            for (int i = 0; i < transforms.Count; i++)
                parents.Add(transforms[i].parent != null && indexOf.TryGetValue(transforms[i].parent, out int p) ? p : -1);
        }

        // bones[BodyJoint] = the model's transform for that joint, or null. False (with a readable report naming the
        // missing bones) when the model can't be a humanoid.
        public static bool MapBones(Transform root, out Transform[] bones, out string report)
        {
            bones = new Transform[BodyJoints.Count];
            if (root == null)
            {
                report = "no model";
                return false;
            }
            var transforms = new List<Transform>();
            var names = new List<string>();
            var parents = new List<int>();
            CollectHierarchy(root, transforms, names, parents);
            SkinnedRigMap map = SkinnedRigMapper.Map(names, parents);
            for (int j = 0; j < BodyJoints.Count; j++)
            {
                int bone = map.BoneOf((BodyJoint)j);
                bones[j] = bone >= 0 ? transforms[bone] : null;
            }
            report = map.Describe(names);
            return map.IsComplete;
        }

        // Stops and switches off any Animation (legacy) or Animator the importer added for the model's own clip.
        // Returns how many it silenced. Safe in edit mode (only flags change).
        public static int SilenceOwnAnimation(Transform root)
        {
            int count = 0;
            foreach (Animation legacy in root.GetComponentsInChildren<Animation>(true))
            {
                if (Application.isPlaying) legacy.Stop();
                legacy.playAutomatically = false;
                legacy.enabled = false;
                count++;
            }
            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled = false;
                count++;
            }
            return count;
        }

        // Moves every skinned bone to where it was when the mesh was bound to it. A bone's bind matrix is the inverse
        // of where it sat (relative to the space the mesh was bound in, see BindSpace), so inverting it gives the
        // bone's undeformed position. Parents go first, so a parent moving afterwards can't drag a child out of place.
        public static void RestoreBindPose(IList<SkinnedMeshRenderer> renderers)
        {
            var done = new HashSet<Transform>();
            var order = new List<KeyValuePair<Transform, Matrix4x4>>();
            foreach (SkinnedMeshRenderer smr in renderers)
            {
                if (smr == null || smr.sharedMesh == null) continue;
                Transform[] smrBones = smr.bones;
                Matrix4x4[] bindposes = smr.sharedMesh.bindposes;
                if (smrBones == null || bindposes == null) continue;
                Matrix4x4 bindToWorld = BindSpace(smr);
                int count = Math.Min(smrBones.Length, bindposes.Length);
                for (int i = 0; i < count; i++)
                {
                    Transform bone = smrBones[i];
                    if (bone == null || !done.Add(bone)) continue;
                    order.Add(new KeyValuePair<Transform, Matrix4x4>(bone, bindToWorld * bindposes[i].inverse));
                }
            }
            order.Sort((a, b) => Depth(a.Key).CompareTo(Depth(b.Key)));
            foreach (KeyValuePair<Transform, Matrix4x4> entry in order)
            {
                Matrix4x4 m = entry.Value;
                entry.Key.SetPositionAndRotation(m.GetColumn(3), m.rotation);
            }
        }

        // Where the mesh was bound: the matrix taking its vertices (and its bind matrices) to world space.
        // Unity's own importers bind relative to the skinned mesh's object, but a glTF skin is bound relative to the
        // glTF scene (the spec ignores the mesh node's transform) and glTFast passes it through unchanged. The two
        // differ whenever the mesh sits under a scaled armature, as in a Blender export in centimetres (the player
        // model: a 0.01 "Armature" with the mesh inside it). Taking the mesh object's space there shrank the
        // skeleton a hundredfold, and the fit then blew the whole model up a hundredfold.
        // So this tries the mesh object and each of its parents and keeps the one that puts the bones nearest to
        // where they are now (the wrong spaces miss by about the model's own size). Ties keep the mesh object.
        public static Matrix4x4 BindSpace(SkinnedMeshRenderer smr)
        {
            Matrix4x4 meshToWorld = smr.transform.localToWorldMatrix;
            Transform[] smrBones = smr.bones;
            Matrix4x4[] bindposes = smr.sharedMesh != null ? smr.sharedMesh.bindposes : null;
            if (smrBones == null || bindposes == null) return meshToWorld;
            int count = Math.Min(smrBones.Length, bindposes.Length);
            var bound = new Vector3[count];
            for (int i = 0; i < count; i++) bound[i] = bindposes[i].inverse.GetColumn(3);

            Matrix4x4 best = meshToWorld;
            float bestError = float.PositiveInfinity;
            for (Transform space = smr.transform; space != null; space = space.parent)
            {
                Matrix4x4 toWorld = space.localToWorldMatrix;
                float error = 0f;
                for (int i = 0; i < count; i++)
                    if (smrBones[i] != null) error += (toWorld.MultiplyPoint3x4(bound[i]) - smrBones[i].position).magnitude;
                if (error < bestError * 0.999f)
                {
                    best = toWorld;
                    bestError = error;
                }
            }
            return best;
        }

        // Stands the model (every child of root) up in root's space: see SkinnedModelFit. Returns what it did.
        public static SkinnedFitResult FitModel(Transform root, Transform[] bones, IList<SkinnedMeshRenderer> renderers,
            float targetHeadHeight, float heightScale)
        {
            Transform hips = bones[(int)BodyJoint.Hips], head = bones[(int)BodyJoint.Head];
            Transform leftLeg = bones[(int)BodyJoint.LeftUpperLeg], rightLeg = bones[(int)BodyJoint.RightUpperLeg];
            if (hips == null || head == null || leftLeg == null || rightLeg == null) return SkinnedFitResult.Identity;

            var surface = new List<System.Numerics.Vector3>();
            foreach (SkinnedMeshRenderer smr in renderers)
            {
                if (smr == null || smr.sharedMesh == null) continue;
                Bounds b = smr.sharedMesh.bounds;   // the undeformed mesh, in the space it was bound in
                Matrix4x4 toRoot = root.worldToLocalMatrix * BindSpace(smr);
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                    surface.Add(toRoot.MultiplyPoint3x4(corner).ToNumerics());
                }
            }

            SkinnedFitResult fit = SkinnedModelFit.Compute(Local(root, hips), Local(root, head), Local(root, leftLeg), Local(root, rightLeg),
                surface, targetHeadHeight, heightScale);
            if (fit.IsIdentity) return fit;

            Quaternion rotation = HumanoidBody.ToUnity(fit.Rotation);
            Vector3 translation = fit.Translation.ToUnity();
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                child.localPosition = translation + fit.Scale * (rotation * child.localPosition);
                child.localRotation = rotation * child.localRotation;
                child.localScale *= fit.Scale;
            }
            return fit;
        }

        // The rest pose Mecanim measures muscles from is a T-pose: arms straight out to the sides, legs straight down.
        // A model made in an A-pose (arms angled down) would otherwise play every arm movement 40-odd degrees low.
        // Each bone turns the least it can to point at its target, so its twist (which way the palm faces) is kept.
        public static void PoseAsTPose(Transform root, Transform[] bones)
        {
            Vector3 right = root.right, down = -root.up;
            Point(bones, BodyJoint.LeftUpperArm, BodyJoint.LeftLowerArm, -right);
            Point(bones, BodyJoint.LeftLowerArm, BodyJoint.LeftHand, -right);
            Point(bones, BodyJoint.RightUpperArm, BodyJoint.RightLowerArm, right);
            Point(bones, BodyJoint.RightLowerArm, BodyJoint.RightHand, right);
            Point(bones, BodyJoint.LeftUpperLeg, BodyJoint.LeftLowerLeg, down);
            Point(bones, BodyJoint.LeftLowerLeg, BodyJoint.LeftFoot, down);
            Point(bones, BodyJoint.RightUpperLeg, BodyJoint.RightLowerLeg, down);
            Point(bones, BodyJoint.RightLowerLeg, BodyJoint.RightFoot, down);
        }

        static void Point(Transform[] bones, BodyJoint joint, BodyJoint child, Vector3 direction)
        {
            Transform bone = bones[(int)joint], end = bones[(int)child];
            if (bone == null || end == null) return;
            Vector3 current = end.position - bone.position;
            if (current.sqrMagnitude < MinBoneLength * MinBoneLength) return;
            bone.rotation = Quaternion.FromToRotation(current, direction) * bone.rotation;
        }

        // The procedural body's head joint height above its feet in the rest pose (its rest rotations are all
        // identity, so the joints' rest offsets just add up).
        public static float RestHeadHeight(IHumanoidRig rig)
        {
            BodyJoint[] chain = { BodyJoint.Hips, BodyJoint.Spine, BodyJoint.Chest, BodyJoint.UpperChest, BodyJoint.Neck, BodyJoint.Head };
            float height = 0f;
            foreach (BodyJoint j in chain)
                if (TryHuman(j, out HumanBodyBones human) && rig.GetBone(human) != null) height += rig.GetRestLocalPosition(human).y;
            return height;
        }

        // The procedural body's bones, indexed by BodyJoint like a mapped model's.
        public static Transform[] BonesOf(IHumanoidRig rig)
        {
            var bones = new Transform[BodyJoints.Count];
            for (int j = 0; j < BodyJoints.Count; j++)
                if (TryHuman((BodyJoint)j, out HumanBodyBones human)) bones[j] = rig.GetBone(human);
            return bones;
        }

        // BodyJoint names are HumanBodyBones names (see BodyJoint), so this always succeeds for a real joint.
        public static bool TryHuman(BodyJoint joint, out HumanBodyBones human)
        {
            return Enum.TryParse(BodyJoints.Name(joint), out human);
        }

        // A Humanoid Avatar for the skeleton under root, the way MecanimPoseSource builds one: each mapped bone's
        // HumanBodyBones slot, and the rest pose of every transform from root down to the mapped bones (restOf
        // for mapped bones; the current local pose for the ones in between). Null plus a readable reason when the
        // skeleton can't make a valid human.
        public static Avatar BuildAvatar(Transform root, Transform[] bones, RestPoseOf restOf, out string problem)
        {
            var problems = new List<string>();
            var humanBones = new List<HumanBone>();
            var mapped = new Dictionary<Transform, BodyJoint>();
            var missingRequired = new List<string>();

            for (int j = 0; j < BodyJoints.Count; j++)
            {
                var joint = (BodyJoint)j;
                Transform bone = bones[j];
                if (!TryHuman(joint, out HumanBodyBones human) || (int)human >= HumanTrait.BoneCount) continue;
                if (bone == null)
                {
                    if (HumanTrait.RequiredBone((int)human)) missingRequired.Add(joint.ToString());
                    continue;
                }
                if (bone == root || !bone.IsChildOf(root))
                {
                    problems.Add(joint + " bone '" + bone.name + "' is not under '" + root.name + "'");
                    continue;
                }
                if (mapped.ContainsKey(bone))
                {
                    problems.Add("'" + bone.name + "' is used for both " + mapped[bone] + " and " + joint);
                    continue;
                }
                mapped.Add(bone, joint);
                humanBones.Add(new HumanBone
                {
                    humanName = HumanTrait.BoneName[(int)human],
                    boneName = bone.name,
                    limit = new HumanLimit { useDefaultValues = true }, // Unity's standard human muscle ranges
                });
            }
            if (missingRequired.Count > 0) problems.Add("missing required bones: " + string.Join(", ", missingRequired));

            // The skeleton: the root, every mapped bone and every transform between them, parents first.
            var skeletonSet = new HashSet<Transform> { root };
            foreach (Transform bone in mapped.Keys)
                for (Transform t = bone; t != null && t != root; t = t.parent) skeletonSet.Add(t);
            var skeleton = new List<Transform>(skeletonSet);
            skeleton.Sort((a, b) => Depth(a).CompareTo(Depth(b)));

            // AvatarBuilder finds bones by name, so names in the hierarchy must be unique.
            var names = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                names[t.name] = names.TryGetValue(t.name, out int n) ? n + 1 : 1;
            foreach (Transform t in skeleton)
                if (names.TryGetValue(t.name, out int n) && n > 1) problems.Add("bone name '" + t.name + "' is not unique under '" + root.name + "'");

            if (problems.Count > 0)
            {
                problem = string.Join("; ", problems);
                return null;
            }

            var skeletonBones = new SkeletonBone[skeleton.Count];
            for (int i = 0; i < skeleton.Count; i++)
            {
                Transform t = skeleton[i];
                Vector3 position = t.localPosition;
                Quaternion rotation = t.localRotation;
                if (mapped.TryGetValue(t, out BodyJoint joint) && restOf != null) restOf(joint, t, out position, out rotation);
                skeletonBones[i] = new SkeletonBone { name = t.name, position = position, rotation = rotation, scale = t.localScale };
            }

            var description = new HumanDescription
            {
                human = humanBones.ToArray(),
                skeleton = skeletonBones,
                // Unity's importer defaults, as in MecanimPoseSource: twist shared along the limbs, a little stretch
                // to absorb proportion differences, no extra translation.
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false,
            };

            Avatar built = AvatarBuilder.BuildHumanAvatar(root.gameObject, description);
            if (built == null)
            {
                problem = "AvatarBuilder returned nothing";
                return null;
            }
            if (!built.isValid || !built.isHuman)
            {
                GreyboxShapes.SafeDestroy(built);
                problem = "AvatarBuilder rejected the skeleton (is the rest pose a T-pose, with the hips under '" + root.name + "'?)";
                return null;
            }
            built.name = root.name + " Avatar (runtime)";
            problem = null;
            return built;
        }

        static System.Numerics.Vector3 Local(Transform root, Transform t)
        {
            return root.InverseTransformPoint(t.position).ToNumerics();
        }

        static int Depth(Transform t)
        {
            int depth = 0;
            for (; t != null; t = t.parent) depth++;
            return depth;
        }
    }
}
