using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Body measurements of a 1.8 m athletic adult, in metres, for the rest pose (a T-pose: standing straight,
    // arms out to the sides, palms down). Data, not code, so a heavier soldier or a lankier character is a
    // change of numbers. The fighter's pivot is between its feet on the ground; +Z is forward.
    [Serializable]
    public class SkeletonProportions
    {
        public float HipHeight = 0.99f;          // pelvis centre above the ground
        public float HipJointDrop = 0.05f;       // hip joints sit this far below the pelvis centre...
        public float HipJointWidth = 0.095f;     // ...and this far to each side
        public float ThighLength = 0.43f;
        public float ShinLength = 0.43f;
        public float AnkleHeight = 0.08f;        // ankle joint above the sole
        public float FootLength = 0.13f;         // ankle to the ball of the foot (the toes joint)
        public float ToesDrop = 0.06f;
        public float SpineLength = 0.10f;        // pelvis centre to the Spine joint
        public float ChestLength = 0.14f;
        public float UpperChestLength = 0.14f;
        public float NeckBase = 0.14f;           // UpperChest to the base of the neck
        public float NeckLength = 0.09f;         // neck base to the head joint (base of the skull)
        public float ClavicleHeight = 0.09f;     // collarbone roots above the UpperChest joint...
        public float ClavicleWidth = 0.04f;      // ...and to each side
        public float ClavicleLength = 0.15f;     // collarbone to the shoulder joint (shoulder width = 2 x (this + ClavicleWidth))
        public float UpperArmLength = 0.29f;
        public float ForearmLength = 0.26f;
        public float HandLength = 0.09f;         // wrist to the knuckles (where a fist or a sword grip sits)
        public float HeadHeight = 0.22f;         // head joint to the crown

        public SkeletonProportions Clone()
        {
            return (SkeletonProportions)MemberwiseClone();
        }
    }

    // The rest pose (T-pose) hierarchy: each joint's parent and its offset from the parent. Every rest rotation
    // is identity, so a joint's offset is simply where it sits relative to its parent when standing in a T-pose.
    // Built once per fighter (Create), then shared read-only by the pose solver, the forward kinematics and the
    // Unity body builder. Scale makes bigger enemies or a smaller dummy without new measurements.
    public sealed class HumanoidSkeleton
    {
        readonly Vector3[] restOffsets = new Vector3[BodyJoints.Count];
        readonly Vector3[] restPositions = new Vector3[BodyJoints.Count];

        public float Scale { get; private set; }
        public SkeletonProportions Proportions { get; private set; }

        public float UpperArmLength { get; private set; }
        public float ForearmLength { get; private set; }
        public float ArmLength => UpperArmLength + ForearmLength;   // shoulder to wrist, arm straight
        public float ThighLength { get; private set; }
        public float ShinLength { get; private set; }
        public float LegLength => ThighLength + ShinLength;         // hip to ankle, leg straight
        public float AnkleHeight { get; private set; }
        public float HandLength { get; private set; }
        public float HeadHeight { get; private set; }
        public float HipHeight { get; private set; }
        public float Height { get; private set; }                   // ground to crown, standing

        HumanoidSkeleton()
        {
        }

        public static HumanoidSkeleton Create(SkeletonProportions proportions = null, float scale = 1f)
        {
            var s = new HumanoidSkeleton();
            s.Build(proportions ?? new SkeletonProportions(), scale);
            return s;
        }

        // Offset from the parent joint in the rest pose (model space, metres).
        public Vector3 RestOffset(BodyJoint joint) => restOffsets[(int)joint];
        public Vector3 RestOffset(int joint) => restOffsets[joint];

        // Where the joint sits in the rest pose, measured from the fighter's pivot (between the feet).
        public Vector3 RestPosition(BodyJoint joint) => restPositions[(int)joint];
        public Vector3 RestPosition(int joint) => restPositions[joint];

        // Bone length from a joint to its child along the limb (used by the body builder for mesh sizes).
        public float BoneLength(BodyJoint joint)
        {
            switch (joint)
            {
                case BodyJoint.LeftUpperArm:
                case BodyJoint.RightUpperArm: return UpperArmLength;
                case BodyJoint.LeftLowerArm:
                case BodyJoint.RightLowerArm: return ForearmLength;
                case BodyJoint.LeftUpperLeg:
                case BodyJoint.RightUpperLeg: return ThighLength;
                case BodyJoint.LeftLowerLeg:
                case BodyJoint.RightLowerLeg: return ShinLength;
                case BodyJoint.LeftHand:
                case BodyJoint.RightHand: return HandLength;
                case BodyJoint.Head: return HeadHeight;
                default:
                {
                    // spine segments: distance to the next joint up
                    for (int j = 0; j < BodyJoints.Count; j++)
                    {
                        if (BodyJoints.Parent(j) == (int)joint) return restOffsets[j].Length();
                    }
                    return 0f;
                }
            }
        }

        void Build(SkeletonProportions p, float scale)
        {
            Proportions = p;
            Scale = scale > 0f && AnimMath.IsFinite(scale) ? scale : 1f;
            float k = Scale;
            UpperArmLength = p.UpperArmLength * k;
            ForearmLength = p.ForearmLength * k;
            ThighLength = p.ThighLength * k;
            ShinLength = p.ShinLength * k;
            AnkleHeight = p.AnkleHeight * k;
            HandLength = p.HandLength * k;
            HeadHeight = p.HeadHeight * k;
            HipHeight = p.HipHeight * k;

            Set(BodyJoint.Hips, new Vector3(0f, p.HipHeight, 0f) * k);
            Set(BodyJoint.Spine, new Vector3(0f, p.SpineLength, 0f) * k);
            Set(BodyJoint.Chest, new Vector3(0f, p.ChestLength, 0f) * k);
            Set(BodyJoint.UpperChest, new Vector3(0f, p.UpperChestLength, 0f) * k);
            Set(BodyJoint.Neck, new Vector3(0f, p.NeckBase, 0f) * k);
            Set(BodyJoint.Head, new Vector3(0f, p.NeckLength, 0f) * k);
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                BodySide bodySide = s == 0 ? BodySide.Left : BodySide.Right;
                Set(BodyJoints.Shoulder(bodySide), new Vector3(side * p.ClavicleWidth, p.ClavicleHeight, 0f) * k);
                Set(BodyJoints.UpperArm(bodySide), new Vector3(side * p.ClavicleLength, 0f, 0f) * k);
                Set(BodyJoints.LowerArm(bodySide), new Vector3(side * p.UpperArmLength, 0f, 0f) * k);
                Set(BodyJoints.Hand(bodySide), new Vector3(side * p.ForearmLength, 0f, 0f) * k);
                Set(BodyJoints.UpperLeg(bodySide), new Vector3(side * p.HipJointWidth, -p.HipJointDrop, 0f) * k);
                Set(BodyJoints.LowerLeg(bodySide), new Vector3(0f, -p.ThighLength, 0f) * k);
                Set(BodyJoints.Foot(bodySide), new Vector3(0f, -p.ShinLength, 0f) * k);
                Set(BodyJoints.Toes(bodySide), new Vector3(0f, -p.ToesDrop, p.FootLength) * k);
            }
            for (int j = 0; j < BodyJoints.Count; j++)
            {
                int parent = BodyJoints.Parent(j);
                restPositions[j] = parent < 0 ? restOffsets[j] : restPositions[parent] + restOffsets[j];
            }
            Height = restPositions[(int)BodyJoint.Head].Y + HeadHeight;
        }

        void Set(BodyJoint joint, Vector3 offset)
        {
            restOffsets[(int)joint] = offset;
        }

        // Rest-pose axis of a limb bone (the direction from the joint to its child) and the side its child
        // folds toward when the joint bends: forearms fold forward, shins fold back. The pose solver builds
        // every limb rotation from these two directions.
        public static Vector3 RestAxis(BodyJoint joint)
        {
            switch (joint)
            {
                case BodyJoint.LeftShoulder:
                case BodyJoint.LeftUpperArm:
                case BodyJoint.LeftLowerArm:
                case BodyJoint.LeftHand: return -Vector3.UnitX;
                case BodyJoint.RightShoulder:
                case BodyJoint.RightUpperArm:
                case BodyJoint.RightLowerArm:
                case BodyJoint.RightHand: return Vector3.UnitX;
                case BodyJoint.LeftUpperLeg:
                case BodyJoint.LeftLowerLeg:
                case BodyJoint.RightUpperLeg:
                case BodyJoint.RightLowerLeg: return -Vector3.UnitY;
                case BodyJoint.LeftFoot:
                case BodyJoint.RightFoot:
                case BodyJoint.LeftToes:
                case BodyJoint.RightToes: return Vector3.UnitZ;
                default: return Vector3.UnitY;
            }
        }

        public static Vector3 RestFront(BodyJoint joint)
        {
            switch (joint)
            {
                case BodyJoint.LeftUpperLeg:
                case BodyJoint.LeftLowerLeg:
                case BodyJoint.RightUpperLeg:
                case BodyJoint.RightLowerLeg: return -Vector3.UnitZ;
                case BodyJoint.LeftFoot:
                case BodyJoint.RightFoot:
                case BodyJoint.LeftToes:
                case BodyJoint.RightToes: return Vector3.UnitY;
                default: return Vector3.UnitZ;
            }
        }
    }
}
