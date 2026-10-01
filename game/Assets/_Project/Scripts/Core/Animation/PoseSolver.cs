using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Turns a PoseSpec (the dials: stance, twist, where each fist and foot goes) into a BodyPose (a rotation for
    // every joint). Arms and legs use two-bone inverse kinematics ("IK"): given where the hand should be, work
    // out the shoulder and elbow angles that put it there. A pole direction says which way the elbow or knee
    // points, so joints always bend the natural way (elbows down and back, knees forward over the toes).
    //
    // Every limb is aimed as "direction + how far extended" from its root joint, so a key with Reach 1 is a
    // fully straight arm at exactly that moment, which is how strikes are guaranteed to reach full extension
    // on the move's first active frame.
    //
    // Works in the fighter's own "model space" (pivot between the feet, +Z forward). One solver per fighter;
    // it keeps its scratch buffers, so solving never allocates.
    public sealed class PoseSolver
    {
        readonly HumanoidSkeleton skeleton;
        readonly Quaternion[] modelRot = new Quaternion[BodyJoints.Count];
        readonly Vector3[] modelPos = new Vector3[BodyJoints.Count];
        bool grounded;

        public PoseSolver(HumanoidSkeleton skeleton)
        {
            this.skeleton = skeleton ?? HumanoidSkeleton.Create();
        }

        public HumanoidSkeleton Skeleton => skeleton;

        // Model-space results of the last Solve (handy for effects and tests).
        public Vector3 ModelPosition(BodyJoint joint) => modelPos[(int)joint];
        public Quaternion ModelRotation(BodyJoint joint) => modelRot[(int)joint];

        // travel: metres the body has moved forward since the current action began (anchored feet stay behind).
        // aimPitch: degrees up (+) or down toward a target, for clips that aim (crossbow).
        // grounded: the fighter stands on the floor (model y = 0), so a kick aimed down never goes through it.
        public void Solve(PoseSpec s, BodyPose pose, float travel = 0f, float aimPitch = 0f, bool grounded = false)
        {
            this.grounded = grounded;
            float k = skeleton.Scale;
            float rootYaw = s[PoseChannel.RootYaw];
            Quaternion body = AnimMath.Yaw(rootYaw);   // the body's facing frame (includes spins)

            // ---- pelvis
            Quaternion hipsLocal = AnimMath.Euler(s[PoseChannel.PelvisPitch], s[PoseChannel.PelvisYaw], s[PoseChannel.PelvisRoll]);
            pose.RootYaw = rootYaw;
            pose.HipsOffset = new Vector3(s[PoseChannel.HipsX], s[PoseChannel.HipsY], s[PoseChannel.HipsZ]) * k;
            pose[BodyJoint.Hips] = hipsLocal;
            SetModel(BodyJoint.Hips, body * hipsLocal, skeleton.RestPosition(BodyJoint.Hips) + pose.HipsOffset);

            // ---- spine: the twist and lean are shared over three joints, like a real back bending
            float tp = s[PoseChannel.TorsoPitch], ty = s[PoseChannel.TorsoYaw], tr = s[PoseChannel.TorsoRoll];
            SetLocal(pose, BodyJoint.Spine, AnimMath.Euler(tp * 0.3f, ty * 0.3f, tr * 0.3f));
            SetLocal(pose, BodyJoint.Chest, AnimMath.Euler(tp * 0.35f, ty * 0.35f, tr * 0.35f));
            SetLocal(pose, BodyJoint.UpperChest, AnimMath.Euler(tp * 0.35f, ty * 0.35f, tr * 0.35f));

            // ---- neck and head: eyes stay on the opponent unless LookFront is off (spins, tumbling)
            float look = AnimMath.Clamp01(s[PoseChannel.LookFront]);
            float hy = s[PoseChannel.HeadYaw] - (s[PoseChannel.PelvisYaw] + ty) * look;
            float hp = s[PoseChannel.HeadPitch] - (s[PoseChannel.PelvisPitch] + tp) * look * 0.8f;
            float hr = s[PoseChannel.HeadRoll] - (s[PoseChannel.PelvisRoll] + tr) * look * 0.6f;
            hy = AnimMath.Clamp(hy, -80f, 80f);
            hp = AnimMath.Clamp(hp, -70f, 70f);
            hr = AnimMath.Clamp(hr, -45f, 45f);
            SetLocal(pose, BodyJoint.Neck, AnimMath.Euler(hp * 0.4f, hy * 0.4f, hr * 0.4f));
            SetLocal(pose, BodyJoint.Head, AnimMath.Euler(hp * 0.6f, hy * 0.6f, hr * 0.6f));

            // ---- arms
            Quaternion chest = modelRot[(int)BodyJoint.UpperChest];
            Quaternion armFrame = Quaternion.Normalize(Quaternion.Slerp(body, chest, AnimMath.Clamp01(s[PoseChannel.ArmFollow])));
            SolveArm(BodySide.Left, s, pose, armFrame, aimPitch);
            SolveArm(BodySide.Right, s, pose, armFrame, aimPitch);

            // ---- legs
            Quaternion pelvis = modelRot[(int)BodyJoint.Hips];
            Quaternion legFrame = Quaternion.Normalize(Quaternion.Slerp(body, pelvis, AnimMath.Clamp01(s[PoseChannel.LegFrame])));
            SolveLeg(BodySide.Left, s, pose, body, legFrame, travel);
            SolveLeg(BodySide.Right, s, pose, body, legFrame, travel);

            // ---- prop in the right hand
            pose.PropLocal = SolveProp(s, body, aimPitch);
        }

        void SetLocal(BodyPose pose, BodyJoint joint, Quaternion local)
        {
            pose[joint] = local;
            int parent = BodyJoints.Parent((int)joint);
            Quaternion rot = Quaternion.Normalize(modelRot[parent] * local);
            Vector3 pos = modelPos[parent] + AnimMath.Rotate(modelRot[parent], skeleton.RestOffset(joint));
            SetModel(joint, rot, pos);
        }

        void SetModel(BodyJoint joint, Quaternion rot, Vector3 pos)
        {
            modelRot[(int)joint] = rot;
            modelPos[(int)joint] = pos;
        }

        // Sets a joint from its model-space rotation (converting to the local rotation the pose stores).
        void SetFromModel(BodyPose pose, BodyJoint joint, Quaternion model)
        {
            int parent = BodyJoints.Parent((int)joint);
            Quaternion local = Quaternion.Normalize(Quaternion.Inverse(modelRot[parent]) * model);
            SetLocal(pose, joint, local);
        }

        void SolveArm(BodySide side, PoseSpec s, BodyPose pose, Quaternion frame, float aimPitch)
        {
            float sign = (float)side;
            float yaw = s[PoseSpec.Arm(side, 0)];
            float pitch = s[PoseSpec.Arm(side, 1)];
            float reach = AnimMath.Clamp(s[PoseSpec.Arm(side, 2)], 0.2f, 1f);
            float elbowOut = s[PoseSpec.Arm(side, 3)];
            float roll = s[PoseSpec.Arm(side, 4)];
            float wrist = s[PoseSpec.Arm(side, 5)];
            float shrug = s[PoseSpec.Arm(side, 6)];
            float aim = AnimMath.Clamp01(s[PoseChannel.PropAim]);
            pitch += aimPitch * aim;

            Vector3 dir = AnimMath.Rotate(frame, AnimMath.Direction(sign * yaw, pitch));

            // Collarbone: the shoulder rides forward on a reach and lifts when the arm goes up, which adds real
            // reach to a punch and keeps raised arms from looking stuck on.
            float forward = Math.Max(0f, Vector3.Dot(dir, AnimMath.Rotate(frame, Vector3.UnitZ)));
            float protract = 14f * reach * forward;
            float raise = 12f * AnimMath.Clamp01(pitch / 90f) + shrug;
            Quaternion clavicle = AnimMath.AxisAngle(Vector3.UnitY, -sign * protract) * AnimMath.AxisAngle(Vector3.UnitZ, sign * raise);
            SetLocal(pose, BodyJoints.Shoulder(side), clavicle);

            BodyJoint upper = BodyJoints.UpperArm(side);
            BodyJoint lower = BodyJoints.LowerArm(side);
            BodyJoint hand = BodyJoints.Hand(side);
            SetLocal(pose, upper, Quaternion.Identity);   // positions the shoulder joint
            Vector3 shoulder = modelPos[(int)upper];
            float l1 = skeleton.UpperArmLength, l2 = skeleton.ForearmLength;
            Vector3 target = shoulder + dir * (reach * (l1 + l2));

            // Elbow points down and a little back and out; ElbowOut swings it up and out (a hook, a raised guard).
            Vector3 pole = AnimMath.Rotate(frame, Vector3.Normalize(new Vector3(sign * 0.25f, -1f, -0.45f)));
            pole = RotateAround(pole, dir, sign * elbowOut);
            TwoBone(shoulder, target, l1, l2, pole, AnimMath.Rotate(frame, -Vector3.UnitZ), out Vector3 elbow, out Vector3 front);

            Vector3 restAxis = HumanoidSkeleton.RestAxis(upper);
            Vector3 restFront = HumanoidSkeleton.RestFront(upper);
            Vector3 upperAxis = AnimMath.SafeNormalize(elbow - shoulder, dir);
            Quaternion upperModel = AnimMath.FrameRotation(restAxis, restFront, upperAxis, front);
            SetFromModel(pose, upper, upperModel);

            Vector3 lowerAxis = AnimMath.SafeNormalize(target - elbow, upperAxis);
            Vector3 hinge = AnimMath.SafeNormalize(Vector3.Cross(upperAxis, front), Vector3.UnitY);
            Vector3 lowerFront = AnimMath.SafeNormalize(Vector3.Cross(hinge, lowerAxis), front);
            Quaternion lowerModel = AnimMath.FrameRotation(restAxis, restFront, lowerAxis, lowerFront);
            // Forearm roll (turning the fist over) happens around the forearm's own axis.
            lowerModel = lowerModel * AnimMath.AxisAngle(Vector3.UnitX, -roll);
            SetFromModel(pose, lower, lowerModel);

            // Wrist: + bends the hand back (a palm strike shows the heel of the palm).
            SetLocal(pose, hand, AnimMath.AxisAngle(Vector3.UnitZ, sign * wrist));
        }

        void SolveLeg(BodySide side, PoseSpec s, BodyPose pose, Quaternion body, Quaternion frame, float travel)
        {
            float sign = (float)side;
            float k = skeleton.Scale;
            float footX = s[PoseSpec.Leg(side, 0)] * k;
            float footY = s[PoseSpec.Leg(side, 1)] * k;
            float footZ = s[PoseSpec.Leg(side, 2)] * k;
            float kneeOut = s[PoseSpec.Leg(side, 3)];
            float footYaw = s[PoseSpec.Leg(side, 4)];
            float footPitch = s[PoseSpec.Leg(side, 5)];
            float flat = AnimMath.Clamp01(s[PoseSpec.Leg(side, 6)]);
            float reachMode = AnimMath.Clamp01(s[PoseSpec.Leg(side, 7)]);
            float kickYaw = s[PoseSpec.Leg(side, 8)];
            float kickPitch = s[PoseSpec.Leg(side, 9)];
            float kickReach = AnimMath.Clamp(s[PoseSpec.Leg(side, 10)], 0.25f, 1f);
            float anchor = AnimMath.Clamp01(s[PoseSpec.Leg(side, 11)]);

            BodyJoint upper = BodyJoints.UpperLeg(side);
            BodyJoint lower = BodyJoints.LowerLeg(side);
            BodyJoint foot = BodyJoints.Foot(side);
            BodyJoint toes = BodyJoints.Toes(side);
            SetLocal(pose, upper, Quaternion.Identity);
            Vector3 hip = modelPos[(int)upper];
            float l1 = skeleton.ThighLength, l2 = skeleton.ShinLength;

            // Planted target (ankle position in the body's frame), pulled back by the lunge when anchored so the
            // back foot stays on its spot while the body drives forward (no skating), but never past full stretch.
            // The pull-back is capped at about one stride (MaxAnchorPull): Build 05's arriving lunges cover 1-4.7 m, and
            // dragging the foot back by all of it stretched the leg out flat behind at hip height (verify R2-02). Past a
            // stride the foot lock takes over and steps the foot in.
            float anchorBack = anchor * Math.Min(Math.Max(0f, travel), MaxAnchorPull * k);
            // A planted foot tipped onto its toes (a heel pivot, a push-off) rises by exactly as much as it tips, so
            // the ball of the foot stays on the floor instead of sinking into it.
            if (flat > 0.5f && footPitch > 0f && footY < (0.08f + 0.02f) * k) footY = Math.Max(footY, (0.08f + FootTipRise(footPitch)) * k);
            Vector3 planted = AnimMath.Rotate(body, new Vector3(sign * footX, footY, footZ - anchorBack));
            // Aimed target (kicks): a direction and extension from the hip.
            Vector3 kickDir = AnimMath.Rotate(frame, AnimMath.Direction(sign * kickYaw, kickPitch));
            Vector3 aimed = hip + kickDir * (kickReach * (l1 + l2));
            Vector3 target = Vector3.Lerp(planted, aimed, reachMode);
            float floorAnkle = MinAnkleHeight * k;
            if (grounded && reachMode > 0.5f && target.Y < floorAnkle) target.Y = floorAnkle;
            if (reachMode < 0.5f)
            {
                // Planted feet can't reach past a straight leg: the foot lifts rather than the leg stretching.
                Vector3 toTarget = target - hip;
                float max = (l1 + l2) * 0.999f;
                if (toTarget.Length() > max) target = hip + Vector3.Normalize(toTarget) * max;
            }
            Vector3 legDir = AnimMath.SafeNormalize(target - hip, -Vector3.UnitY);

            // Knee points where the toes point (plus KneeOut), so it never bends backwards.
            Vector3 toeDir = AnimMath.Rotate(reachMode > 0.5f ? frame : body, AnimMath.Direction(sign * footYaw, 0f));
            Vector3 pole = RotateAround(toeDir, legDir, -sign * kneeOut);
            TwoBone(hip, target, l1, l2, pole, AnimMath.Rotate(body, Vector3.UnitY), out Vector3 knee, out Vector3 front);
            if (grounded && knee.Y < MinKneeHeight * k)
            {
                // Lying or falling: a knee that would bend through the floor turns its bend upward, by as much as it
                // would dip (so the change is gradual, never a flip).
                float t = AnimMath.Clamp01((MinKneeHeight * k - knee.Y) / (KneeLiftRange * k));
                Vector3 raised = AnimMath.SafeNormalize(Vector3.Lerp(pole, Vector3.UnitY, t), pole);
                TwoBone(hip, target, l1, l2, raised, AnimMath.Rotate(body, Vector3.UnitY), out knee, out front);
            }

            Vector3 restAxis = HumanoidSkeleton.RestAxis(upper);
            Vector3 restFront = HumanoidSkeleton.RestFront(upper);
            Vector3 thighAxis = AnimMath.SafeNormalize(knee - hip, legDir);
            Quaternion thighModel = AnimMath.FrameRotation(restAxis, restFront, thighAxis, front);
            SetFromModel(pose, upper, thighModel);

            Vector3 shinAxis = AnimMath.SafeNormalize(target - knee, thighAxis);
            Vector3 hinge = AnimMath.SafeNormalize(Vector3.Cross(thighAxis, front), Vector3.UnitX);
            Vector3 shinFront = AnimMath.SafeNormalize(Vector3.Cross(hinge, shinAxis), front);
            Quaternion shinModel = AnimMath.FrameRotation(restAxis, restFront, shinAxis, shinFront);
            SetFromModel(pose, lower, shinModel);

            // Foot: level on the ground (flat) or carried by the shin (kicks), then pitched (+ = toes down).
            Quaternion flatFoot = body * AnimMath.Yaw(sign * footYaw) * AnimMath.AxisAngle(Vector3.UnitX, footPitch);
            Quaternion carried = shinModel * AnimMath.AxisAngle(Vector3.UnitX, footPitch);
            Quaternion footModel = Quaternion.Normalize(Quaternion.Slerp(carried, flatFoot, flat));
            SetFromModel(pose, foot, footModel);
            if (grounded) KeepToesAboveFloor(pose, foot, toes, footModel, k);
            // Toes stay on the ground when the heel lifts (push-off), and curl back a little in a front kick.
            float toeBend = flat > 0.5f ? -Math.Max(0f, footPitch) * 0.8f : (footPitch < 0f ? footPitch * 0.4f : 0f);
            SetLocal(pose, toes, AnimMath.AxisAngle(Vector3.UnitX, toeBend));
        }

        // On the ground (kneeling, falling, lying) a pointed foot mustn't dig its toes into the floor: tip the foot up
        // about the ankle, by exactly as much as the toes would sink, so they rest on the floor instead.
        void KeepToesAboveFloor(BodyPose pose, BodyJoint foot, BodyJoint toes, Quaternion footModel, float k)
        {
            Vector3 ankle = modelPos[(int)foot];
            Vector3 v = AnimMath.Rotate(footModel, skeleton.RestOffset(toes));
            float floor = ToesFloorHeight * k;
            if (ankle.Y + v.Y >= floor) return;
            Vector3 axis = AnimMath.Rotate(footModel, Vector3.UnitX);   // the foot's hinge: its left-right axis
            // Rotating v about axis by angle a: y(a) = along.y + across.y cos a + side.y sin a. Solve y(a) = floor.
            Vector3 along = axis * Vector3.Dot(v, axis);
            Vector3 across = v - along;
            Vector3 side = Vector3.Cross(axis, across);
            float c = floor - ankle.Y - along.Y;
            float r = MathF.Sqrt(across.Y * across.Y + side.Y * side.Y);
            if (r < 1e-5f || Math.Abs(c) > r) return;   // can't reach the floor by tipping (the ankle itself is low)
            float phase = MathF.Atan2(side.Y, across.Y);
            float spread = MathF.Acos(AnimMath.Clamp(c / r, -1f, 1f));
            float a1 = Wrap(phase + spread), a2 = Wrap(phase - spread);
            float angle = Math.Abs(a1) < Math.Abs(a2) ? a1 : a2;   // the smaller tip that does it
            Quaternion lifted = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(axis, angle) * footModel);
            SetFromModel(pose, foot, lifted);

            static float Wrap(float radians)
            {
                while (radians > MathF.PI) radians -= 2f * MathF.PI;
                while (radians < -MathF.PI) radians += 2f * MathF.PI;
                return radians;
            }
        }

        // Prop direction: 0 degrees = along the forearm, 90 = straight out of the top of the fist (thumb side);
        // PropAim swings it toward the body's facing (aiming a crossbow).
        Quaternion SolveProp(PoseSpec s, Quaternion body, float aimPitch)
        {
            float blade = s[PoseChannel.Blade] * AnimMath.Deg2Rad;
            Vector3 local = new Vector3(MathF.Cos(blade), 0f, MathF.Sin(blade));   // right hand: +X along the bone, +Z thumb side
            float aim = AnimMath.Clamp01(s[PoseChannel.PropAim]);
            Quaternion hand = modelRot[(int)BodyJoint.RightHand];
            if (aim > 0f)
            {
                Vector3 aimModel = AnimMath.Rotate(body, AnimMath.Direction(0f, aimPitch));
                Vector3 aimLocal = AnimMath.Rotate(Quaternion.Inverse(hand), aimModel);
                local = AnimMath.SafeNormalize(Vector3.Lerp(local, aimLocal, aim), local);
            }
            Vector3 upModel = AnimMath.Rotate(body, Vector3.UnitY);
            Vector3 up = AnimMath.Rotate(Quaternion.Inverse(hand), upModel);
            if (MathF.Abs(Vector3.Dot(up, local)) > 0.95f) up = Vector3.UnitY;
            return AnimMath.LookRotation(local, up);
        }

        // Two-bone IK. Places the middle joint (elbow/knee) so the chain reaches from 'root' toward 'target'
        // (clamped to what the limb can reach), bending toward 'pole'. 'front' is the direction the lower bone
        // folds toward, relative to the upper bone: it fixes the twist of the upper bone.
        static void TwoBone(Vector3 root, Vector3 target, float l1, float l2, Vector3 pole, Vector3 fallbackPole,
            out Vector3 middle, out Vector3 front)
        {
            Vector3 toTarget = target - root;
            float d = toTarget.Length();
            Vector3 dir = d > 1e-5f ? toTarget / d : -Vector3.UnitY;
            float minD = Math.Abs(l1 - l2) + 1e-4f;
            float maxD = l1 + l2;
            d = AnimMath.Clamp(d, minD, maxD);

            Vector3 bend = AnimMath.Perpendicular(pole, dir);
            if (bend.LengthSquared() < 0.01f) bend = AnimMath.Perpendicular(fallbackPole, dir);
            if (bend.LengthSquared() < 1e-6f) bend = AnimMath.Perpendicular(Vector3.UnitZ, dir);
            if (bend.LengthSquared() < 1e-6f) bend = AnimMath.Perpendicular(Vector3.UnitX, dir);
            bend = Vector3.Normalize(bend);

            float a = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
            float h = MathF.Sqrt(Math.Max(0f, l1 * l1 - a * a));
            middle = root + dir * a + bend * h;
            front = AnimMath.SafeNormalize(dir * h - bend * a, -bend);
        }

        // How far the ankle must rise for a foot tipped toes-down by pitch degrees to keep the ball of the foot
        // (13 cm ahead of and 6 cm below the ankle) on the floor.
        const float MinAnkleHeight = 0.06f;   // a kick along the floor: the ankle skims it
        const float MaxAnchorPull = 0.45f;    // an anchored rear foot stays behind by at most about one stride
        const float ToesFloorHeight = 0.02f;  // the ball of the foot's joint on a flat foot (the sole is under it)
        const float MinKneeHeight = 0.05f;    // the front of a knee resting on the floor
        const float KneeLiftRange = 0.25f;    // a knee this far under the floor bends straight up

        public static float FootTipRise(float pitch)
        {
            if (!(pitch > 0f)) return 0f;
            float a = pitch * AnimMath.Deg2Rad;
            return Math.Max(0f, 0.13f * MathF.Sin(a) + 0.06f * MathF.Cos(a) - 0.06f);
        }

        static Vector3 RotateAround(Vector3 v, Vector3 axis, float degrees)
        {
            if (degrees == 0f) return v;
            return AnimMath.Rotate(AnimMath.AxisAngle(axis, degrees), v);
        }
    }
}
