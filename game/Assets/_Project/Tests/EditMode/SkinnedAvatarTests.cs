using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // The imported player model (SkinnedAvatarMirror): its bones are matched to the human joints the right way
    // whatever the tool called them (the spine by where it sits, never by its numbering), a model missing a limb
    // is refused with the missing bones named, and the model is stood up, turned to face +Z, scaled to the
    // procedural body and put on the ground, idempotently.
    public class SkinnedAvatarTests
    {
        // A small builder for test hierarchies: Add(name, parentName).
        sealed class Rig
        {
            public readonly List<string> Names = new List<string>();
            public readonly List<int> Parents = new List<int>();

            public Rig Add(string name, string parent = null)
            {
                Names.Add(name);
                Parents.Add(parent == null ? -1 : Names.IndexOf(parent));
                if (parent != null && Parents[Parents.Count - 1] < 0) throw new ArgumentException("no parent " + parent);
                return this;
            }

            public string BoneOf(SkinnedRigMap map, BodyJoint joint)
            {
                int i = map.BoneOf(joint);
                return i >= 0 ? Names[i] : null;
            }
        }

        // The player model David picked: 24 joints from an AI rig generator, spine numbered from the hips up as
        // Spine02 > Spine01 > Spine, lower-case "neck", end bones head_end and headfront, under a scene node and
        // an armature, next to the mesh object.
        static Rig PlayerAvatarRig()
        {
            var r = new Rig();
            r.Add("player_avatar").Add("Armature", "player_avatar").Add("Mesh", "player_avatar");
            r.Add("Hips", "Armature");
            foreach (string side in new[] { "Left", "Right" })
            {
                r.Add(side + "UpLeg", "Hips").Add(side + "Leg", side + "UpLeg").Add(side + "Foot", side + "Leg").Add(side + "ToeBase", side + "Foot");
            }
            r.Add("Spine02", "Hips").Add("Spine01", "Spine02").Add("Spine", "Spine01");
            foreach (string side in new[] { "Left", "Right" })
            {
                r.Add(side + "Shoulder", "Spine").Add(side + "Arm", side + "Shoulder").Add(side + "ForeArm", side + "Arm").Add(side + "Hand", side + "ForeArm");
            }
            r.Add("neck", "Spine").Add("Head", "neck").Add("head_end", "Head").Add("headfront", "Head");
            return r;
        }

        [Test]
        public void PlayerModelSpineIsMappedByHierarchyNotByName()
        {
            Rig r = PlayerAvatarRig();
            SkinnedRigMap map = SkinnedRigMapper.Map(r.Names, r.Parents);

            Assert.IsTrue(map.IsComplete, map.Describe(r.Names));
            Assert.AreEqual("Spine02", r.BoneOf(map, BodyJoint.Spine), "the first bone above the hips is the Spine");
            Assert.AreEqual("Spine01", r.BoneOf(map, BodyJoint.Chest));
            Assert.AreEqual("Spine", r.BoneOf(map, BodyJoint.UpperChest), "the bone the neck and arms hang from is the UpperChest");
            Assert.AreEqual("neck", r.BoneOf(map, BodyJoint.Neck));
            Assert.AreEqual("Head", r.BoneOf(map, BodyJoint.Head));
        }

        [Test]
        public void PlayerModelLimbsMapToEveryHumanJoint()
        {
            Rig r = PlayerAvatarRig();
            SkinnedRigMap map = SkinnedRigMapper.Map(r.Names, r.Parents);

            var expected = new Dictionary<BodyJoint, string>
            {
                { BodyJoint.Hips, "Hips" },
                { BodyJoint.LeftUpperLeg, "LeftUpLeg" }, { BodyJoint.LeftLowerLeg, "LeftLeg" },
                { BodyJoint.LeftFoot, "LeftFoot" }, { BodyJoint.LeftToes, "LeftToeBase" },
                { BodyJoint.RightUpperLeg, "RightUpLeg" }, { BodyJoint.RightLowerLeg, "RightLeg" },
                { BodyJoint.RightFoot, "RightFoot" }, { BodyJoint.RightToes, "RightToeBase" },
                { BodyJoint.LeftShoulder, "LeftShoulder" }, { BodyJoint.LeftUpperArm, "LeftArm" },
                { BodyJoint.LeftLowerArm, "LeftForeArm" }, { BodyJoint.LeftHand, "LeftHand" },
                { BodyJoint.RightShoulder, "RightShoulder" }, { BodyJoint.RightUpperArm, "RightArm" },
                { BodyJoint.RightLowerArm, "RightForeArm" }, { BodyJoint.RightHand, "RightHand" },
            };
            foreach (KeyValuePair<BodyJoint, string> pair in expected)
                Assert.AreEqual(pair.Value, r.BoneOf(map, pair.Key), pair.Key.ToString());

            // Every joint used once at most (a bone playing two parts makes AvatarBuilder fail).
            var used = new HashSet<int>();
            for (int j = 0; j < BodyJoints.Count; j++)
            {
                int bone = map.BoneOf((BodyJoint)j);
                if (bone >= 0) Assert.IsTrue(used.Add(bone), "bone " + r.Names[bone] + " mapped twice");
            }
        }

        [Test]
        public void SpineNumberedTheOtherWayStillMapsBottomUp()
        {
            var r = new Rig();
            r.Add("Hips").Add("Spine", "Hips").Add("Spine1", "Spine").Add("Spine2", "Spine1");
            r.Add("Neck", "Spine2").Add("Head", "Neck");
            AddArms(r, "Spine2");
            AddLegs(r, "Hips");
            SkinnedRigMap map = SkinnedRigMapper.Map(r.Names, r.Parents);

            Assert.IsTrue(map.IsComplete, map.Describe(r.Names));
            Assert.AreEqual("Spine", r.BoneOf(map, BodyJoint.Spine));
            Assert.AreEqual("Spine1", r.BoneOf(map, BodyJoint.Chest));
            Assert.AreEqual("Spine2", r.BoneOf(map, BodyJoint.UpperChest));
        }

        [Test]
        public void ShortSpineLeavesOptionalJointsEmpty()
        {
            var r = new Rig();
            r.Add("Hips").Add("Torso", "Hips").Add("Head", "Torso");
            AddArms(r, "Torso");
            AddLegs(r, "Hips");
            SkinnedRigMap map = SkinnedRigMapper.Map(r.Names, r.Parents);

            Assert.IsTrue(map.IsComplete, map.Describe(r.Names));
            Assert.AreEqual("Torso", r.BoneOf(map, BodyJoint.Spine));
            Assert.IsNull(r.BoneOf(map, BodyJoint.Chest));
            Assert.IsNull(r.BoneOf(map, BodyJoint.UpperChest));
            Assert.IsNull(r.BoneOf(map, BodyJoint.Neck), "no bone between the torso and the head");
        }

        [Test]
        public void LongSpineUsesFirstSecondAndLastBones()
        {
            var r = new Rig();
            r.Add("Hips").Add("S_a", "Hips").Add("S_b", "S_a").Add("S_c", "S_b").Add("S_d", "S_c");
            r.Add("Head", "S_d");
            AddArms(r, "S_d");
            AddLegs(r, "Hips");
            SkinnedRigMap map = SkinnedRigMapper.Map(r.Names, r.Parents);

            Assert.AreEqual("S_a", r.BoneOf(map, BodyJoint.Spine));
            Assert.AreEqual("S_b", r.BoneOf(map, BodyJoint.Chest));
            Assert.AreEqual("S_d", r.BoneOf(map, BodyJoint.UpperChest));
        }

        [Test]
        public void PrefixedAndSuffixedNamesAreRecognised()
        {
            var r = new Rig();
            r.Add("mixamorig:Hips").Add("mixamorig:Spine", "mixamorig:Hips").Add("mixamorig:Neck", "mixamorig:Spine").Add("mixamorig:Head", "mixamorig:Neck");
            r.Add("clavicle_l", "mixamorig:Spine").Add("upperarm_l", "clavicle_l").Add("lowerarm_l", "upperarm_l").Add("hand_l", "lowerarm_l");
            r.Add("clavicle_r", "mixamorig:Spine").Add("upperarm_r", "clavicle_r").Add("lowerarm_r", "upperarm_r").Add("hand_r", "lowerarm_r");
            r.Add("Thigh.L", "mixamorig:Hips").Add("Calf.L", "Thigh.L").Add("Foot.L", "Calf.L");
            r.Add("Thigh.R", "mixamorig:Hips").Add("Calf.R", "Thigh.R").Add("Foot.R", "Calf.R");
            SkinnedRigMap map = SkinnedRigMapper.Map(r.Names, r.Parents);

            Assert.IsTrue(map.IsComplete, map.Describe(r.Names));
            Assert.AreEqual("clavicle_l", r.BoneOf(map, BodyJoint.LeftShoulder));
            Assert.AreEqual("upperarm_r", r.BoneOf(map, BodyJoint.RightUpperArm));
            Assert.AreEqual("Calf.L", r.BoneOf(map, BodyJoint.LeftLowerLeg));
            Assert.AreEqual("Foot.R", r.BoneOf(map, BodyJoint.RightFoot));
        }

        [Test]
        public void UnnamedCollarbonesAreFoundBetweenSpineAndArm()
        {
            var r = new Rig();
            r.Add("Hips").Add("Spine", "Hips").Add("Chest", "Spine").Add("Head", "Chest");
            r.Add("Bone.001", "Chest").Add("LeftArm", "Bone.001").Add("LeftForeArm", "LeftArm").Add("LeftHand", "LeftForeArm");
            r.Add("Bone.002", "Chest").Add("RightArm", "Bone.002").Add("RightForeArm", "RightArm").Add("RightHand", "RightForeArm");
            AddLegs(r, "Hips");
            SkinnedRigMap map = SkinnedRigMapper.Map(r.Names, r.Parents);

            Assert.IsTrue(map.IsComplete, map.Describe(r.Names));
            Assert.AreEqual("Bone.001", r.BoneOf(map, BodyJoint.LeftShoulder));
            Assert.AreEqual("Bone.002", r.BoneOf(map, BodyJoint.RightShoulder));
            Assert.AreEqual("Chest", r.BoneOf(map, BodyJoint.Chest));
        }

        [Test]
        public void MissingHandIsReportedByName()
        {
            Rig full = PlayerAvatarRig();
            var r = new Rig();
            for (int i = 0; i < full.Names.Count; i++)
            {
                if (full.Names[i] == "LeftHand") continue;
                r.Add(full.Names[i], full.Parents[i] >= 0 ? full.Names[full.Parents[i]] : null);
            }
            SkinnedRigMap map = SkinnedRigMapper.Map(r.Names, r.Parents);

            Assert.IsFalse(map.IsComplete);
            CollectionAssert.AreEqual(new[] { BodyJoint.LeftHand }, map.MissingRequired);
            StringAssert.Contains("LeftHand", map.Describe(r.Names));
        }

        [Test]
        public void LimbNamedUnderTheWrongParentIsDropped()
        {
            var r = new Rig();
            r.Add("Hips").Add("Spine", "Hips").Add("Head", "Spine");
            AddArms(r, "Spine");
            AddLegs(r, "Hips");
            // A "LeftFoot" hanging off the right leg is a broken rig, not a left foot.
            int leftFoot = r.Names.IndexOf("LeftFoot");
            r.Parents[leftFoot] = r.Names.IndexOf("RightLeg");
            SkinnedRigMap map = SkinnedRigMapper.Map(r.Names, r.Parents);

            Assert.IsNull(r.BoneOf(map, BodyJoint.LeftFoot));
            CollectionAssert.Contains(map.MissingRequired, BodyJoint.LeftFoot);
        }

        [Test]
        public void EmptyInputMissesEveryRequiredJoint()
        {
            SkinnedRigMap map = SkinnedRigMapper.Map(new string[0], new int[0]);
            Assert.IsFalse(map.IsComplete);
            Assert.AreEqual(15, map.MissingRequired.Count);
            foreach (BodyJoint j in map.MissingRequired) Assert.IsTrue(SkinnedRigMapper.IsRequired(j));
        }

        [Test]
        public void NormaliseStripsNamespacesSeparatorsAndCase()
        {
            Assert.AreEqual("leftupleg", SkinnedRigMapper.Normalise("mixamorig:LeftUpLeg"));
            Assert.AreEqual("thighl", SkinnedRigMapper.Normalise("Thigh_L"));
            Assert.AreEqual("hips", SkinnedRigMapper.Normalise("Armature|Hips"));
            Assert.AreEqual("", SkinnedRigMapper.Normalise(null));
        }

        static void AddArms(Rig r, string parent)
        {
            foreach (string side in new[] { "Left", "Right" })
                r.Add(side + "Shoulder", parent).Add(side + "Arm", side + "Shoulder").Add(side + "ForeArm", side + "Arm").Add(side + "Hand", side + "ForeArm");
        }

        static void AddLegs(Rig r, string parent)
        {
            foreach (string side in new[] { "Left", "Right" })
                r.Add(side + "UpLeg", parent).Add(side + "Leg", side + "UpLeg").Add(side + "Foot", side + "Leg");
        }

        // ------------------------------------------------------------------------------------------------- fitting

        // A 1.78 m model in its own space: facing +Z, Y up, feet at 0, right leg at +X, head joint at 1.55 m.
        static readonly Vector3 Hips = new Vector3(0f, 0.95f, 0f);
        static readonly Vector3 Head = new Vector3(0f, 1.55f, 0.02f);
        static readonly Vector3 LeftLeg = new Vector3(-0.1f, 0.9f, 0f);
        static readonly Vector3 RightLeg = new Vector3(0.1f, 0.9f, 0f);

        static Vector3[] Box(float height)
        {
            return new[]
            {
                new Vector3(-0.4f, 0f, -0.15f), new Vector3(0.4f, 0f, -0.15f), new Vector3(-0.4f, 0f, 0.2f), new Vector3(0.4f, 0f, 0.2f),
                new Vector3(-0.4f, height, -0.15f), new Vector3(0.4f, height, -0.15f), new Vector3(-0.4f, height, 0.2f), new Vector3(0.4f, height, 0.2f),
            };
        }

        static SkinnedFitResult FitModel(Func<Vector3, Vector3> place, float target = 1.55f, float heightScale = 1f)
        {
            Vector3[] surface = Box(1.78f);
            for (int i = 0; i < surface.Length; i++) surface[i] = place(surface[i]);
            return SkinnedModelFit.Compute(place(Hips), place(Head), place(LeftLeg), place(RightLeg), surface, target, heightScale);
        }

        static void AssertClose(Vector3 expected, Vector3 actual, string what, float tolerance = 1e-3f)
        {
            Assert.Less(Vector3.Distance(expected, actual), tolerance, what + ": expected " + expected + " got " + actual);
        }

        [Test]
        public void FitOfAModelThatAlreadyMatchesChangesNothing()
        {
            SkinnedFitResult fit = FitModel(p => p);
            Assert.IsTrue(fit.IsIdentity, "rotation " + fit.Rotation + " scale " + fit.Scale + " move " + fit.Translation);
            Assert.AreEqual(0f, fit.FacingDegrees, 1e-3f);
            Assert.IsFalse(fit.UpWasSnapped);
        }

        [Test]
        public void FitTurnsABackwardsModelToFacePlusZ()
        {
            Quaternion half = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI);
            Func<Vector3, Vector3> place = p => Vector3.Transform(p, half);
            SkinnedFitResult fit = FitModel(place);

            Assert.AreEqual(180f, Math.Abs(fit.FacingDegrees), 1e-3f);
            AssertClose(RightLeg - Hips, fit.Apply(place(RightLeg)) - fit.Apply(place(Hips)), "right leg back on +X");
            AssertClose(Head, fit.Apply(place(Head)), "head");
        }

        [Test]
        public void FitStandsUpAZUpModelFacingSideways()
        {
            // A raw Blender-style export: Z up, facing +X.
            Quaternion lieDown = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2f);
            Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)Math.PI / 2f);
            Func<Vector3, Vector3> place = p => Vector3.Transform(Vector3.Transform(p, lieDown), turn);
            SkinnedFitResult fit = FitModel(place);

            Assert.IsTrue(fit.UpWasSnapped);
            AssertClose(Head, fit.Apply(place(Head)), "head joint back above the feet");
            AssertClose(RightLeg, fit.Apply(place(RightLeg)), "right leg on +X");
            AssertClose(LeftLeg, fit.Apply(place(LeftLeg)), "left leg on -X");
        }

        [Test]
        public void FitSnapsASlightlyLopsidedRigButKeepsARealTurn()
        {
            float slight = 5f * (float)Math.PI / 180f;
            SkinnedFitResult snapped = FitModel(p => Vector3.Transform(p, Quaternion.CreateFromAxisAngle(Vector3.UnitY, slight)));
            Assert.AreEqual(0f, snapped.FacingDegrees, 1e-3f, "5 degrees off is a lopsided rig, not a turned model");

            float real = 30f * (float)Math.PI / 180f;
            SkinnedFitResult turned = FitModel(p => Vector3.Transform(p, Quaternion.CreateFromAxisAngle(Vector3.UnitY, real)));
            Assert.AreEqual(30f, turned.FacingDegrees, 1e-2f);
        }

        [Test]
        public void FitScalesToTheProceduralHeadHeightAndPutsFeetOnTheGround()
        {
            // Exported in centimetres and floating 2 m up.
            Func<Vector3, Vector3> place = p => p * 100f + new Vector3(30f, 200f, -10f);
            SkinnedFitResult fit = FitModel(place, target: 1.6f, heightScale: 1.1f);

            Assert.AreEqual(1.6f / 155f * 1.1f, fit.Scale, 1e-6f);
            float lowest = float.PositiveInfinity;
            foreach (Vector3 p in Box(1.78f)) lowest = Math.Min(lowest, fit.Apply(place(p)).Y);
            Assert.AreEqual(0f, lowest, 1e-4f, "lowest point of the mesh on the ground");
            Vector3 hips = fit.Apply(place(Hips));
            Assert.AreEqual(0f, hips.X, 1e-4f);
            Assert.AreEqual(0f, hips.Z, 1e-4f);
            Assert.AreEqual(1.6f * 1.1f, fit.Apply(place(Head)).Y, 1e-4f);
        }

        [Test]
        public void FitIsIdempotent()
        {
            Quaternion odd = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 2.2f);
            Func<Vector3, Vector3> place = p => Vector3.Transform(p, odd) * 0.5f + new Vector3(1f, 0.3f, 2f);
            SkinnedFitResult first = FitModel(place);
            Func<Vector3, Vector3> placedTwice = p => first.Apply(place(p));
            SkinnedFitResult second = FitModel(placedTwice);
            Assert.IsTrue(second.IsIdentity, "rotation " + second.Rotation + " scale " + second.Scale + " move " + second.Translation);
        }

        [Test]
        public void FromToTakesOneAxisOntoAnother()
        {
            Vector3[] axes = { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ };
            foreach (Vector3 a in axes)
                foreach (Vector3 b in axes)
                    AssertClose(b, Vector3.Transform(a, SkinnedModelFit.FromTo(a, b)), a + " -> " + b, 1e-5f);
        }
    }
}
