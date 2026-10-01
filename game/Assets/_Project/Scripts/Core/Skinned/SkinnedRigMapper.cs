using System;
using System.Collections.Generic;
using System.Text;

namespace VaatusRevenge.Core
{
    // Works out which bone of an imported, rigged character model (a GLB or FBX made outside Unity) plays which part
    // of the human body, so the procedural fighter's animation can be copied onto it (see SkinnedAvatarMirror).
    //
    // Why not just match names: every tool names bones its own way. Limbs are easy (LeftUpLeg, thigh_l and
    // mixamorig:LeftUpLeg all mean "left upper leg"), so they're matched against a list of common spellings. The
    // spine is not: one generator numbers it Spine02 > Spine01 > Spine from the hips up, another the other way round.
    // So the spine is mapped by where the bones sit, never by their names: walking up from the hips, the first bone
    // is Spine, the second Chest and the last one before the neck and arms branch off is UpperChest.
    //
    // Pure C#: the model's bones come in as names plus parent indices (any order), so this is unit tested without
    // Unity. The result is a BodyJoint per bone; BodyJoint names are Unity's HumanBodyBones names.
    public sealed class SkinnedRigMap
    {
        readonly int[] boneOfJoint = new int[BodyJoints.Count];

        public SkinnedRigMap()
        {
            for (int i = 0; i < boneOfJoint.Length; i++) boneOfJoint[i] = -1;
        }

        // Required human joints with no bone (the model can't be animated as a human without them).
        public List<BodyJoint> MissingRequired { get; } = new List<BodyJoint>();
        // Things worth telling the artist that didn't stop the mapping (an ambiguous name, an odd hierarchy).
        public List<string> Notes { get; } = new List<string>();

        public bool IsComplete => MissingRequired.Count == 0;

        // The model bone (index into the names passed to Map) for this joint, or -1.
        public int BoneOf(BodyJoint joint)
        {
            return boneOfJoint[(int)joint];
        }

        internal void Set(BodyJoint joint, int bone)
        {
            boneOfJoint[(int)joint] = bone;
        }

        // "Missing: LeftHand, RightHand" style, for warnings.
        public string Describe(IReadOnlyList<string> names)
        {
            var text = new StringBuilder();
            if (MissingRequired.Count > 0)
            {
                text.Append("missing required bones: ");
                for (int i = 0; i < MissingRequired.Count; i++)
                {
                    if (i > 0) text.Append(", ");
                    text.Append(BodyJoints.Name(MissingRequired[i]));
                }
            }
            for (int i = 0; i < Notes.Count; i++)
            {
                if (text.Length > 0) text.Append("; ");
                text.Append(Notes[i]);
            }
            if (names != null)
            {
                if (text.Length > 0) text.Append(". ");
                text.Append("Mapped: ");
                bool first = true;
                for (int j = 0; j < BodyJoints.Count; j++)
                {
                    int bone = boneOfJoint[j];
                    if (bone < 0) continue;
                    if (!first) text.Append(", ");
                    first = false;
                    text.Append(BodyJoints.Name((BodyJoint)j)).Append('=').Append(names[bone]);
                }
            }
            return text.ToString();
        }
    }

    public static class SkinnedRigMapper
    {
        // The joints Unity's Humanoid Avatar can't do without (HumanTrait.RequiredBone). Neck, Chest, UpperChest,
        // shoulders and toes are optional: Mecanim spreads their motion over the neighbours when they're missing.
        static readonly BodyJoint[] required =
        {
            BodyJoint.Hips, BodyJoint.Spine, BodyJoint.Head,
            BodyJoint.LeftUpperArm, BodyJoint.LeftLowerArm, BodyJoint.LeftHand,
            BodyJoint.RightUpperArm, BodyJoint.RightLowerArm, BodyJoint.RightHand,
            BodyJoint.LeftUpperLeg, BodyJoint.LeftLowerLeg, BodyJoint.LeftFoot,
            BodyJoint.RightUpperLeg, BodyJoint.RightLowerLeg, BodyJoint.RightFoot,
        };

        // Common spellings, after Normalise (lower case, no namespace prefix, no separators). Limb names are written
        // once without a side; "left"/"l" is added in front or behind (leftupleg, lupleg, uplegl) and the right side
        // is the same with "right"/"r". Covers Mixamo, Blender Rigify-style exports, Unreal-style and the AI rig
        // generators we've seen (Hips / LeftUpLeg / LeftLeg / LeftArm / LeftForeArm / neck / Head).
        static readonly string[] hipsNames = { "hips", "hip", "pelvis" };
        static readonly string[] neckNames = { "neck", "neck1", "neck01", "neckbase" };
        static readonly string[] headNames = { "head", "head1", "head01" };

        struct LimbNames
        {
            public BodyJoint Left, Right;
            public string[] Bases;
        }

        static readonly LimbNames[] limbs =
        {
            new LimbNames { Left = BodyJoint.LeftShoulder, Right = BodyJoint.RightShoulder, Bases = new[] { "shoulder", "clavicle", "collar" } },
            new LimbNames { Left = BodyJoint.LeftUpperArm, Right = BodyJoint.RightUpperArm, Bases = new[] { "arm", "upperarm", "uparm" } },
            new LimbNames { Left = BodyJoint.LeftLowerArm, Right = BodyJoint.RightLowerArm, Bases = new[] { "forearm", "lowerarm", "elbow" } },
            new LimbNames { Left = BodyJoint.LeftHand, Right = BodyJoint.RightHand, Bases = new[] { "hand", "wrist" } },
            new LimbNames { Left = BodyJoint.LeftUpperLeg, Right = BodyJoint.RightUpperLeg, Bases = new[] { "upleg", "upperleg", "thigh" } },
            new LimbNames { Left = BodyJoint.LeftLowerLeg, Right = BodyJoint.RightLowerLeg, Bases = new[] { "leg", "lowerleg", "calf", "shin", "knee" } },
            new LimbNames { Left = BodyJoint.LeftFoot, Right = BodyJoint.RightFoot, Bases = new[] { "foot", "ankle" } },
            new LimbNames { Left = BodyJoint.LeftToes, Right = BodyJoint.RightToes, Bases = new[] { "toebase", "toes", "toe", "ball" } },
        };

        public static bool IsRequired(BodyJoint joint)
        {
            return Array.IndexOf(required, joint) >= 0;
        }

        // "mixamorig:LeftUpLeg" -> "leftupleg", "Thigh_L" -> "thighl", "Armature|Hips" -> "hips".
        public static string Normalise(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int cut = Math.Max(name.LastIndexOf(':'), name.LastIndexOf('|'));
            var text = new StringBuilder(name.Length);
            for (int i = cut + 1; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '_' || c == '-' || c == ' ' || c == '.') continue;
                text.Append(char.ToLowerInvariant(c));
            }
            return text.ToString();
        }

        // names[i] is bone i's name, parents[i] its parent's index (-1 for a top-level transform). Pass every
        // transform under the model (meshes and empties included): the hierarchy is what places the spine.
        public static SkinnedRigMap Map(IReadOnlyList<string> names, IReadOnlyList<int> parents)
        {
            var map = new SkinnedRigMap();
            if (names == null || parents == null || names.Count != parents.Count)
            {
                map.Notes.Add("no bones to map");
                foreach (BodyJoint j in required) map.MissingRequired.Add(j);
                return map;
            }

            var normalised = new string[names.Count];
            for (int i = 0; i < names.Count; i++) normalised[i] = Normalise(names[i]);

            int hips = FindByName(map, names, normalised, parents, hipsNames, -1, "Hips");
            map.Set(BodyJoint.Hips, hips);

            // Limbs by name, preferring bones under the hips (a mesh object can share a bone's name).
            foreach (LimbNames limb in limbs)
            {
                map.Set(limb.Left, FindByName(map, names, normalised, parents, SideNames(limb.Bases, "left", "l"), hips, BodyJoints.Name(limb.Left)));
                map.Set(limb.Right, FindByName(map, names, normalised, parents, SideNames(limb.Bases, "right", "r"), hips, BodyJoints.Name(limb.Right)));
            }

            int head = FindByName(map, names, normalised, parents, headNames, hips, "Head");
            int neck = FindByName(map, names, normalised, parents, neckNames, hips, "Neck");
            map.Set(BodyJoint.Head, head);

            if (hips >= 0 && head >= 0) MapSpineByHierarchy(map, names, parents, hips, neck, head);
            else if (neck >= 0) map.Set(BodyJoint.Neck, neck);

            CheckChains(map, names, parents);

            foreach (BodyJoint j in required)
                if (map.BoneOf(j) < 0) map.MissingRequired.Add(j);
            return map;
        }

        // Spine, Chest and UpperChest by position in the hierarchy; Neck by name, or the bone between the branch point
        // and the head; shoulders, when not named, are the bone between the branch point and an upper arm.
        static void MapSpineByHierarchy(SkinnedRigMap map, IReadOnlyList<string> names, IReadOnlyList<int> parents, int hips, int neck, int head)
        {
            List<int> hipsToHead = PathDown(parents, hips, head);
            if (hipsToHead == null)
            {
                map.Notes.Add("'" + names[head] + "' is not under '" + names[hips] + "'");
                return;
            }

            // The branch point: the deepest bone on the hips-to-head path that the arms also hang from.
            int branch = -1;
            int leftArm = map.BoneOf(BodyJoint.LeftUpperArm), rightArm = map.BoneOf(BodyJoint.RightUpperArm);
            for (int i = hipsToHead.Count - 2; i >= 1 && branch < 0; i--)
            {
                int candidate = hipsToHead[i];
                if (candidate == neck) continue;
                bool holdsLeft = leftArm >= 0 && IsAncestor(parents, candidate, leftArm);
                bool holdsRight = rightArm >= 0 && IsAncestor(parents, candidate, rightArm);
                if (holdsLeft || holdsRight) branch = candidate;
            }
            int neckOnPath = neck >= 0 ? hipsToHead.IndexOf(neck) : -1;
            if (neck >= 0 && neckOnPath < 0)
            {
                map.Notes.Add("'" + names[neck] + "' is not between the hips and the head, ignored");
                neck = -1;
            }
            if (branch < 0)
            {
                // No arms to tell us: the spine ends below the neck (or below the head when there's no neck).
                int end = neckOnPath > 0 ? neckOnPath - 1 : hipsToHead.Count - 2;
                branch = end >= 1 ? hipsToHead[end] : -1;
            }

            // The spine chain: everything after the hips up to and including the branch point.
            var chain = new List<int>();
            if (branch >= 0)
            {
                for (int i = 1; i < hipsToHead.Count - 1; i++)
                {
                    chain.Add(hipsToHead[i]);
                    if (hipsToHead[i] == branch) break;
                }
            }
            if (chain.Count >= 1) map.Set(BodyJoint.Spine, chain[0]);
            if (chain.Count >= 2) map.Set(BodyJoint.Chest, chain[1]);
            if (chain.Count >= 3) map.Set(BodyJoint.UpperChest, chain[chain.Count - 1]);
            if (chain.Count > 3) map.Notes.Add((chain.Count - 3) + " extra spine bone(s) left to follow their parents");

            // Neck: the named one, else the first bone between the branch point and the head.
            if (neck < 0 && branch >= 0)
            {
                int after = hipsToHead.IndexOf(branch) + 1;
                if (after < hipsToHead.Count - 1) neck = hipsToHead[after];
            }
            if (neck >= 0 && neck != map.BoneOf(BodyJoint.Spine) && neck != map.BoneOf(BodyJoint.Chest) && neck != map.BoneOf(BodyJoint.UpperChest))
                map.Set(BodyJoint.Neck, neck);

            // Shoulders not found by name: the collarbone is the bone between the branch point and the upper arm.
            if (branch >= 0)
            {
                InferShoulder(map, parents, BodyJoint.LeftShoulder, leftArm, branch);
                InferShoulder(map, parents, BodyJoint.RightShoulder, rightArm, branch);
            }
        }

        static void InferShoulder(SkinnedRigMap map, IReadOnlyList<int> parents, BodyJoint shoulder, int upperArm, int branch)
        {
            if (map.BoneOf(shoulder) >= 0 || upperArm < 0) return;
            int parent = parents[upperArm];
            if (parent >= 0 && parent != branch && parents[parent] == branch) map.Set(shoulder, parent);
        }

        // Each limb bone must hang below the previous one (a wrongly matched name would otherwise make a broken
        // avatar); a bone that doesn't is dropped with a note, which then shows up as missing if it's required.
        static void CheckChains(SkinnedRigMap map, IReadOnlyList<string> names, IReadOnlyList<int> parents)
        {
            BodyJoint[][] chains =
            {
                new[] { BodyJoint.Hips, BodyJoint.LeftUpperLeg, BodyJoint.LeftLowerLeg, BodyJoint.LeftFoot, BodyJoint.LeftToes },
                new[] { BodyJoint.Hips, BodyJoint.RightUpperLeg, BodyJoint.RightLowerLeg, BodyJoint.RightFoot, BodyJoint.RightToes },
                new[] { BodyJoint.Hips, BodyJoint.LeftShoulder, BodyJoint.LeftUpperArm, BodyJoint.LeftLowerArm, BodyJoint.LeftHand },
                new[] { BodyJoint.Hips, BodyJoint.RightShoulder, BodyJoint.RightUpperArm, BodyJoint.RightLowerArm, BodyJoint.RightHand },
            };
            foreach (BodyJoint[] chain in chains)
            {
                int above = map.BoneOf(chain[0]);
                for (int i = 1; i < chain.Length; i++)
                {
                    int bone = map.BoneOf(chain[i]);
                    if (bone < 0) continue;
                    if (above >= 0 && !IsAncestor(parents, above, bone))
                    {
                        map.Notes.Add("'" + names[bone] + "' (" + BodyJoints.Name(chain[i]) + ") is not under '" + names[above] + "', ignored");
                        map.Set(chain[i], -1);
                        continue;
                    }
                    above = bone;
                }
            }
        }

        static string[] SideNames(string[] bases, string word, string letter)
        {
            var result = new string[bases.Length * 3];
            for (int i = 0; i < bases.Length; i++)
            {
                result[i * 3] = word + bases[i];
                result[i * 3 + 1] = letter + bases[i];
                result[i * 3 + 2] = bases[i] + letter;
            }
            return result;
        }

        // The first bone whose name matches, earlier spellings first. With several matches, one under 'under'
        // (when given) wins, then the one closest to the top of the hierarchy; a note records the ambiguity.
        static int FindByName(SkinnedRigMap map, IReadOnlyList<string> names, string[] normalised, IReadOnlyList<int> parents,
            string[] spellings, int under, string what)
        {
            for (int s = 0; s < spellings.Length; s++)
            {
                int best = -1, matches = 0;
                for (int i = 0; i < normalised.Length; i++)
                {
                    if (normalised[i] != spellings[s]) continue;
                    if (under >= 0 && i != under && !IsAncestor(parents, under, i)) continue;
                    matches++;
                    if (best < 0 || Depth(parents, i) < Depth(parents, best)) best = i;
                }
                if (best < 0) continue;
                if (matches > 1) map.Notes.Add(matches + " bones could be " + what + ", used '" + names[best] + "'");
                return best;
            }
            return -1;
        }

        // The bones from 'top' down to 'bottom' (both included), or null if bottom isn't under top.
        static List<int> PathDown(IReadOnlyList<int> parents, int top, int bottom)
        {
            var path = new List<int>();
            int guard = parents.Count + 1;
            for (int b = bottom; b >= 0 && guard-- > 0; b = parents[b])
            {
                path.Add(b);
                if (b == top)
                {
                    path.Reverse();
                    return path;
                }
            }
            return null;
        }

        // True if 'ancestor' is a parent, grandparent... of 'bone' (not the bone itself).
        public static bool IsAncestor(IReadOnlyList<int> parents, int ancestor, int bone)
        {
            int guard = parents.Count + 1;
            for (int b = bone >= 0 ? parents[bone] : -1; b >= 0 && guard-- > 0; b = parents[b])
                if (b == ancestor) return true;
            return false;
        }

        static int Depth(IReadOnlyList<int> parents, int bone)
        {
            int depth = 0, guard = parents.Count + 1;
            for (int b = parents[bone]; b >= 0 && guard-- > 0; b = parents[b]) depth++;
            return depth;
        }
    }
}
