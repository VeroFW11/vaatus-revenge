using System;
using System.Collections.Generic;

namespace VaatusRevenge.Core
{
    // Every animation the procedural animator can play, looked up by AnimationKeys id, plus the settings for
    // blending, secondary motion and the walk/run cycle. The defaults are authored in code
    // (PoseLibraryDefaults) and can be copied into a PoseLibraryAsset in Unity to tweak in the Inspector.
    //
    // Style variants: a clip named "style:key" (e.g. "sword:idle") replaces "key" for fighters of that style,
    // so a dao soldier's guard holds a sword while the player's guard is empty-handed, with no code per enemy.
    [Serializable]
    public class PoseLibrary
    {
        public const char StyleSeparator = ':';

        public AnimatorSettings Settings = new AnimatorSettings();
        public GaitSettings Gait = new GaitSettings();
        public PoseClip[] Clips = PoseLibraryDefaults.CreateClips();

        [NonSerialized] Dictionary<string, PoseClip> map;
        [NonSerialized] PoseClip[] mappedFrom;

        static PoseLibrary defaultLibrary;

        // The built-in library (created once, never modified). Unity resets it when play starts
        // (domain reload is off in this project), see BodyAnimatorDriver.
        public static PoseLibrary Default => defaultLibrary ?? (defaultLibrary = new PoseLibrary());

        public static void ResetDefault()
        {
            defaultLibrary = null;
        }

        // A neutral standing pose, for a missing clip.
        public static readonly PoseSpec Neutral = PoseLibraryDefaults.Neutral();

        public bool Has(string key)
        {
            return Get(key) != null;
        }

        public PoseClip Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            EnsureMap();
            return map.TryGetValue(key, out PoseClip clip) ? clip : null;
        }

        // Clips for a style: "style:key" entries under their plain key (built once per animator, not per frame).
        public Dictionary<string, PoseClip> StyleOverrides(string style)
        {
            var overrides = new Dictionary<string, PoseClip>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(style) || Clips == null) return overrides;
            string prefix = style + StyleSeparator;
            for (int i = 0; i < Clips.Length; i++)
            {
                PoseClip clip = Clips[i];
                if (clip == null || !clip.IsValid || !clip.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                overrides[clip.Key.Substring(prefix.Length)] = clip;
            }
            return overrides;
        }

        // Call after editing Clips (the Inspector does it through the asset's OnValidate).
        public void Invalidate()
        {
            map = null;
            mappedFrom = null;
        }

        void EnsureMap()
        {
            if (map != null && ReferenceEquals(mappedFrom, Clips)) return;
            map = new Dictionary<string, PoseClip>(StringComparer.Ordinal);
            mappedFrom = Clips;
            if (Clips == null) return;
            for (int i = 0; i < Clips.Length; i++)
            {
                PoseClip clip = Clips[i];
                if (clip == null || !clip.IsValid) continue;
                for (int k = 0; k < clip.Keys.Length; k++)
                {
                    if (clip.Keys[k] != null && clip.Keys[k].Pose != null) clip.Keys[k].Pose.EnsureSize();
                }
                map[clip.Key] = clip;
            }
        }
    }
}
