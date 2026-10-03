using System.Collections.Generic;

namespace VaatusRevenge.Core
{
    // Stand-in clips for animation keys that don't have their own poses yet: each borrows the nearest existing clip under
    // its own key, so every key plays something sensible from the day it exists (and AnimationTests stays green). A
    // real clip for the key always wins: a placeholder is only added when no clip with that key was built. Entries are
    // removed from this table as the real clips arrive; when it is empty (as now), every key has its own animation.
    public static partial class PoseLibraryDefaults
    {
        // { new key, the existing clip it borrows }. A method, not a static field: Core keeps no static state.
        // Empty since Build 05 package B: every animation key has its own clip (Fire, the dodges, Water, Earth and Air).
        // A key added later can borrow a clip here until its real one is authored.
        static string[,] PlaceholderTable()
        {
            return new string[0, 2];
        }

        // How many keys are still borrowing another clip (0 = every key has its own; the tests pin it).
        public static int PlaceholderCount => PlaceholderTable().GetLength(0);

        static void AddPlaceholders(List<PoseClip> clips)
        {
            var byKey = new Dictionary<string, PoseClip>(clips.Count);
            for (int i = 0; i < clips.Count; i++)
            {
                if (clips[i] != null && clips[i].IsValid) byKey[clips[i].Key] = clips[i];
            }
            string[,] table = PlaceholderTable();
            for (int i = 0; i < table.GetLength(0); i++)
            {
                string key = table[i, 0];
                if (byKey.ContainsKey(key) || !byKey.TryGetValue(table[i, 1], out PoseClip source)) continue;
                PoseClip copy = CopyClip(source, key);
                clips.Add(copy);
                byKey[key] = copy;
            }
        }

        // A deep copy under another key (the keyframes are copied too, so editing one clip never changes the other).
        static PoseClip CopyClip(PoseClip source, string key)
        {
            var keys = new PoseKeyframe[source.Keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                PoseKeyframe k = source.Keys[i];
                keys[i] = k == null ? null : new PoseKeyframe { Phase = k.Phase, At = k.At, Ease = k.Ease, Pose = k.Pose != null ? k.Pose.Clone() : null };
            }
            return new PoseClip
            {
                Key = key, Mode = source.Mode, LoopPeriod = source.LoopPeriod, DefaultDuration = source.DefaultDuration,
                StartupShare = source.StartupShare, ActiveShare = source.ActiveShare, FadeIn = source.FadeIn,
                UpperBodyOnly = source.UpperBodyOnly, Aims = source.Aims, ArmSwing = source.ArmSwing, Keys = keys
            };
        }
    }
}
