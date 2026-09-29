namespace VaatusRevenge.Core
{
    // The joints of a fighter's skeleton. The names are exactly Unity's HumanBodyBones names, so the Unity side
    // maps them by name and a Mecanim Humanoid Avatar built from these bones lines up 1:1 with pack animations.
    // Order matters: every joint comes after its parent, so walking the list top to bottom is a valid
    // forward-kinematics order (parents are always solved before their children).
    public enum BodyJoint
    {
        Hips,
        Spine,
        Chest,
        UpperChest,
        Neck,
        Head,
        LeftShoulder,
        LeftUpperArm,
        LeftLowerArm,
        LeftHand,
        RightShoulder,
        RightUpperArm,
        RightLowerArm,
        RightHand,
        LeftUpperLeg,
        LeftLowerLeg,
        LeftFoot,
        LeftToes,
        RightUpperLeg,
        RightLowerLeg,
        RightFoot,
        RightToes,
    }

    public enum BodySide { Left = -1, Right = 1 }

    public static class BodyJoints
    {
        public const int Count = 22;

        static readonly int[] parents =
        {
            -1,                                              // Hips (the root of the skeleton)
            (int)BodyJoint.Hips,                             // Spine
            (int)BodyJoint.Spine,                            // Chest
            (int)BodyJoint.Chest,                            // UpperChest
            (int)BodyJoint.UpperChest,                       // Neck
            (int)BodyJoint.Neck,                             // Head
            (int)BodyJoint.UpperChest,                       // LeftShoulder (collarbone)
            (int)BodyJoint.LeftShoulder,                     // LeftUpperArm
            (int)BodyJoint.LeftUpperArm,                     // LeftLowerArm
            (int)BodyJoint.LeftLowerArm,                     // LeftHand
            (int)BodyJoint.UpperChest,                       // RightShoulder
            (int)BodyJoint.RightShoulder,                    // RightUpperArm
            (int)BodyJoint.RightUpperArm,                    // RightLowerArm
            (int)BodyJoint.RightLowerArm,                    // RightHand
            (int)BodyJoint.Hips,                             // LeftUpperLeg
            (int)BodyJoint.LeftUpperLeg,                     // LeftLowerLeg
            (int)BodyJoint.LeftLowerLeg,                     // LeftFoot
            (int)BodyJoint.LeftFoot,                         // LeftToes
            (int)BodyJoint.Hips,                             // RightUpperLeg
            (int)BodyJoint.RightUpperLeg,                    // RightLowerLeg
            (int)BodyJoint.RightLowerLeg,                    // RightFoot
            (int)BodyJoint.RightFoot,                        // RightToes
        };

        static readonly string[] names = System.Enum.GetNames(typeof(BodyJoint));

        // -1 for Hips.
        public static int Parent(int joint)
        {
            return parents[joint];
        }

        public static BodyJoint Parent(BodyJoint joint)
        {
            return (BodyJoint)parents[(int)joint];
        }

        // The HumanBodyBones name (cached: no allocation).
        public static string Name(BodyJoint joint)
        {
            return names[(int)joint];
        }

        public static BodyJoint UpperArm(BodySide side) => side == BodySide.Left ? BodyJoint.LeftUpperArm : BodyJoint.RightUpperArm;
        public static BodyJoint LowerArm(BodySide side) => side == BodySide.Left ? BodyJoint.LeftLowerArm : BodyJoint.RightLowerArm;
        public static BodyJoint Hand(BodySide side) => side == BodySide.Left ? BodyJoint.LeftHand : BodyJoint.RightHand;
        public static BodyJoint Shoulder(BodySide side) => side == BodySide.Left ? BodyJoint.LeftShoulder : BodyJoint.RightShoulder;
        public static BodyJoint UpperLeg(BodySide side) => side == BodySide.Left ? BodyJoint.LeftUpperLeg : BodyJoint.RightUpperLeg;
        public static BodyJoint LowerLeg(BodySide side) => side == BodySide.Left ? BodyJoint.LeftLowerLeg : BodyJoint.RightLowerLeg;
        public static BodyJoint Foot(BodySide side) => side == BodySide.Left ? BodyJoint.LeftFoot : BodyJoint.RightFoot;
        public static BodyJoint Toes(BodySide side) => side == BodySide.Left ? BodyJoint.LeftToes : BodyJoint.RightToes;
    }
}
