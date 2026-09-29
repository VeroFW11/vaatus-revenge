using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // One frame of a fighter's body: every joint's rotation relative to its parent (in the rest T-pose they're
    // all identity), where the pelvis sits relative to its rest spot, and an extra yaw for the whole body (a
    // spinning kick turns the body a full circle while the fighter's gameplay facing stays put).
    // Also the rotation of a held prop (sword, crossbow) relative to the right hand.
    //
    // A BodyPose is a reusable buffer: create it once and overwrite it every frame (no per-frame allocation).
    public sealed class BodyPose
    {
        public readonly Quaternion[] Local = new Quaternion[BodyJoints.Count];
        public Vector3 HipsOffset;          // metres from the rest pelvis position, in the fighter's own space
        public float RootYaw;               // degrees, turns the whole body around the pelvis
        public Quaternion PropLocal = Quaternion.Identity;   // prop (weapon) rotation in the right hand's space; its +Z is the blade

        public BodyPose()
        {
            SetIdentity();
        }

        public Quaternion this[BodyJoint joint]
        {
            get => Local[(int)joint];
            set => Local[(int)joint] = value;
        }

        // The rest T-pose.
        public void SetIdentity()
        {
            for (int i = 0; i < Local.Length; i++) Local[i] = Quaternion.Identity;
            HipsOffset = Vector3.Zero;
            RootYaw = 0f;
            PropLocal = Quaternion.Identity;
        }

        public void CopyFrom(BodyPose other)
        {
            if (other == null || other == this) return;
            Array.Copy(other.Local, Local, Local.Length);
            HipsOffset = other.HipsOffset;
            RootYaw = other.RootYaw;
            PropLocal = other.PropLocal;
        }

        // result = a blended toward b by t (0 = a, 1 = b). Rotations slerp, so blends never shrink or skew the
        // body the way averaging positions would. result may be a or b.
        public static void Blend(BodyPose a, BodyPose b, float t, BodyPose result)
        {
            t = AnimMath.Clamp01(t);
            for (int i = 0; i < BodyJoints.Count; i++) result.Local[i] = Quaternion.Normalize(Quaternion.Slerp(a.Local[i], b.Local[i], t));
            result.HipsOffset = Vector3.Lerp(a.HipsOffset, b.HipsOffset, t);
            result.RootYaw = AnimMath.Lerp(a.RootYaw, b.RootYaw, t);
            result.PropLocal = Quaternion.Normalize(Quaternion.Slerp(a.PropLocal, b.PropLocal, t));
        }

        // Adds another pose on top of this one as a layer: each of its joint rotations is applied after ours,
        // scaled by weight (0 = no effect). An additive layer is written as a change from the rest pose, e.g.
        // a hit flinch that tips the chest back 15 degrees whatever the fighter is doing.
        public void AddAdditive(BodyPose additive, float weight)
        {
            if (additive == null || !(weight > 0f)) return;
            weight = AnimMath.Clamp01(weight);
            for (int i = 0; i < BodyJoints.Count; i++)
            {
                Quaternion layer = Quaternion.Slerp(Quaternion.Identity, additive.Local[i], weight);
                Local[i] = Quaternion.Normalize(Local[i] * layer);
            }
            HipsOffset += additive.HipsOffset * weight;
            RootYaw += additive.RootYaw * weight;
        }

        public bool IsFinite()
        {
            for (int i = 0; i < Local.Length; i++)
            {
                if (!AnimMath.IsFinite(Local[i])) return false;
            }
            return AnimMath.IsFinite(HipsOffset) && AnimMath.IsFinite(RootYaw) && AnimMath.IsFinite(PropLocal);
        }
    }
}
