using System.Numerics;

namespace VaatusRevenge.Core
{
    // Forward kinematics: where every joint ends up in the world for a pose. Start at the pelvis and walk down
    // the hierarchy, each joint = parent position + parent rotation x rest offset. Unity's Transform hierarchy
    // does exactly this for the real body; this copy runs without Unity, for tests and the offline renderer.
    // Buffers are reused (no allocation per call).
    public sealed class ForwardKinematics
    {
        readonly HumanoidSkeleton skeleton;
        public readonly Vector3[] Positions = new Vector3[BodyJoints.Count];
        public readonly Quaternion[] Rotations = new Quaternion[BodyJoints.Count];

        public ForwardKinematics(HumanoidSkeleton skeleton)
        {
            this.skeleton = skeleton ?? HumanoidSkeleton.Create();
        }

        public HumanoidSkeleton Skeleton => skeleton;

        public Vector3 this[BodyJoint joint] => Positions[(int)joint];

        // rootPosition: the fighter's pivot (feet) in the world; rootYaw: its gameplay facing in degrees.
        public void Compute(BodyPose pose, Vector3 rootPosition, float rootYaw)
        {
            Quaternion root = AnimMath.Yaw(rootYaw);
            // The pelvis: rest spot + offset in the fighter's frame; the body's extra RootYaw turns it in place.
            Quaternion hips = Quaternion.Normalize(root * AnimMath.Yaw(pose.RootYaw) * pose.Local[(int)BodyJoint.Hips]);
            Rotations[0] = hips;
            Positions[0] = rootPosition + AnimMath.Rotate(root, skeleton.RestPosition(BodyJoint.Hips) + pose.HipsOffset);
            for (int j = 1; j < BodyJoints.Count; j++)
            {
                int parent = BodyJoints.Parent(j);
                Rotations[j] = Quaternion.Normalize(Rotations[parent] * pose.Local[j]);
                Positions[j] = Positions[parent] + AnimMath.Rotate(Rotations[parent], skeleton.RestOffset(j));
            }
        }

        // Where a held prop's grip is (the right fist) and which way its blade points (unit vector).
        public void Prop(BodyPose pose, out Vector3 grip, out Vector3 direction)
        {
            Quaternion hand = Rotations[(int)BodyJoint.RightHand];
            grip = Positions[(int)BodyJoint.RightHand] + AnimMath.Rotate(hand, new Vector3(skeleton.HandLength * 0.6f, 0f, 0f));
            direction = AnimMath.Rotate(hand * pose.PropLocal, Vector3.UnitZ);
        }

        // Tip of a hand or foot: the knuckles, or the ball of the foot (what actually hits).
        public Vector3 HandTip(BodySide side)
        {
            BodyJoint hand = BodyJoints.Hand(side);
            return Positions[(int)hand] + AnimMath.Rotate(Rotations[(int)hand], HumanoidSkeleton.RestAxis(hand) * skeleton.HandLength);
        }
    }
}
