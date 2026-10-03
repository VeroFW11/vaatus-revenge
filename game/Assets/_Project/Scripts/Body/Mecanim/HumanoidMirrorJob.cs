using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;

namespace VaatusRevenge
{
    // Plays a Humanoid clip left-right mirrored (a right-leg kick becomes a left-leg kick). Unity's Animator
    // states have a "Mirror" tick box, but the Playables API we use has no such switch, so this small animation
    // job does what that tick box does: it swaps every left muscle with its right twin, flips the sign of the
    // centre-line side-to-side muscles (spine / chest / neck / head / jaw "Left-Right" and "Twist Left-Right"),
    // and mirrors the body position/rotation and the hand/foot IK goals across the character's centre plane.
    //
    // Humanoid muscles are defined symmetrically (a left arm raised 0.3 looks like a right arm raised 0.3), so the
    // swap is exact; that symmetry is why Humanoid clips can be mirrored at all.
    public struct HumanoidMirrorJob : IAnimationJob
    {
        [ReadOnly] public NativeArray<MuscleHandle> Muscles;
        [ReadOnly] public NativeArray<int> SourceIndex;   // muscle i takes its value from muscle SourceIndex[i]...
        [ReadOnly] public NativeArray<float> Sign;        // ...multiplied by this (+1 or -1)
        public NativeArray<float> Values;                 // scratch, one per job

        public void ProcessRootMotion(AnimationStream stream)
        {
            // The fighter's CharacterController moves it (Animator.applyRootMotion is off), so root motion is unused.
        }

        public void ProcessAnimation(AnimationStream stream)
        {
            AnimationStream input = stream.GetInputStream(0);
            if (!input.isValid || !input.isHumanStream || !stream.isHumanStream) return;

            AnimationHumanStream source = input.AsHuman();
            AnimationHumanStream target = stream.AsHuman();

            int count = Muscles.Length;
            for (int i = 0; i < count; i++) Values[i] = source.GetMuscle(Muscles[i]);
            for (int i = 0; i < count; i++) target.SetMuscle(Muscles[i], Values[SourceIndex[i]] * Sign[i]);

            target.bodyLocalPosition = MirrorPosition(source.bodyLocalPosition);
            target.bodyLocalRotation = MirrorRotation(source.bodyLocalRotation);

            MirrorGoalPair(source, target, AvatarIKGoal.LeftFoot, AvatarIKGoal.RightFoot);
            MirrorGoalPair(source, target, AvatarIKGoal.LeftHand, AvatarIKGoal.RightHand);
        }

        static void MirrorGoalPair(AnimationHumanStream source, AnimationHumanStream target, AvatarIKGoal left, AvatarIKGoal right)
        {
            Vector3 leftPosition = source.GetGoalLocalPosition(left);
            Quaternion leftRotation = source.GetGoalLocalRotation(left);
            Vector3 rightPosition = source.GetGoalLocalPosition(right);
            Quaternion rightRotation = source.GetGoalLocalRotation(right);
            target.SetGoalLocalPosition(left, MirrorPosition(rightPosition));
            target.SetGoalLocalRotation(left, MirrorRotation(rightRotation));
            target.SetGoalLocalPosition(right, MirrorPosition(leftPosition));
            target.SetGoalLocalRotation(right, MirrorRotation(leftRotation));
        }

        // Mirror across the character's YZ plane (x = sideways).
        static Vector3 MirrorPosition(Vector3 p) { return new Vector3(-p.x, p.y, p.z); }
        static Quaternion MirrorRotation(Quaternion q) { return new Quaternion(q.x, -q.y, -q.z, q.w); }

        // ---------------------------------------------------------------------------------------------------------
        // Tables (built once per fighter, shared read-only by that fighter's mirror jobs)
        // ---------------------------------------------------------------------------------------------------------

        public sealed class Tables
        {
            public NativeArray<MuscleHandle> Muscles;
            public NativeArray<int> SourceIndex;
            public NativeArray<float> Sign;

            public bool IsCreated => Muscles.IsCreated;

            public static Tables Create()
            {
                int count = MuscleHandle.muscleHandleCount;
                var handles = new MuscleHandle[count];
                MuscleHandle.GetMuscleHandles(handles);

                var names = new string[count];
                for (int i = 0; i < count; i++) names[i] = handles[i].name ?? "";

                var tables = new Tables
                {
                    Muscles = new NativeArray<MuscleHandle>(handles, Allocator.Persistent),
                    SourceIndex = new NativeArray<int>(count, Allocator.Persistent),
                    Sign = new NativeArray<float>(count, Allocator.Persistent),
                };
                for (int i = 0; i < count; i++)
                {
                    string twin = TwinName(names[i]);
                    int source = i;
                    if (twin != null)
                    {
                        for (int j = 0; j < count; j++)
                        {
                            if (names[j] == twin) { source = j; break; }
                        }
                    }
                    tables.SourceIndex[i] = source;
                    // A centre-line muscle that bends or twists sideways flips; everything else keeps its sign.
                    bool centreLine = twin == null;
                    tables.Sign[i] = centreLine && names[i].Contains("Left-Right") ? -1f : 1f;
                }
                return tables;
            }

            // "Left Arm Down-Up" -> "Right Arm Down-Up", "LeftHand.Index.1 Stretched" -> "RightHand...". Null for
            // centre-line muscles ("Spine Left-Right" has a side word, but not as its first word).
            static string TwinName(string name)
            {
                if (name.StartsWith("Left", System.StringComparison.Ordinal)) return "Right" + name.Substring(4);
                if (name.StartsWith("Right", System.StringComparison.Ordinal)) return "Left" + name.Substring(5);
                return null;
            }

            public void Dispose()
            {
                if (Muscles.IsCreated) Muscles.Dispose();
                if (SourceIndex.IsCreated) SourceIndex.Dispose();
                if (Sign.IsCreated) Sign.Dispose();
            }
        }
    }
}
