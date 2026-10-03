using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The contract between a fighter's humanoid body and whatever poses it. The procedural martial-arts animator
    // poses the bones every LateUpdate; a pack-clip player (Unity's Mecanim, with an Avatar built from these bones)
    // can take over by setting ExternalPoseActive, in which case the procedural pose is not applied that frame.
    // Bones are named and addressed with Unity's HumanBodyBones so Mecanim's Humanoid retargeting maps them 1:1.
    public interface IHumanoidRig
    {
        Transform Root { get; }                           // the fighter's feet pivot (the body hangs under it)
        bool IsBuilt { get; }
        Transform GetBone(HumanBodyBones bone);           // null if this body doesn't have that bone
        // Local rotation of each bone in the rest pose (a T-pose), for building a Mecanim Avatar.
        Quaternion GetRestLocalRotation(HumanBodyBones bone);
        Vector3 GetRestLocalPosition(HumanBodyBones bone);
        bool ExternalPoseActive { get; set; }             // set each frame by an external pose source (reset every LateUpdate)
        AnimationCue CurrentActionCue { get; }            // what the body is doing right now (for the external source to follow)
        AnimationCue CurrentLocomotionCue { get; }
    }
}
