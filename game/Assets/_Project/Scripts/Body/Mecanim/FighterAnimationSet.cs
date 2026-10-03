using System;
using System.Collections.Generic;
using UnityEngine;

namespace VaatusRevenge
{
    // Which real (mocap) animation clip plays for which AnimationKey, and how to line it up with our frame data.
    // MecanimPoseSource reads it: a key with a clip here plays that clip; any other key stays procedural.
    //
    // The clips come from the Asset Store packs in Assets/ThirdParty, which are NOT in git (licence). This asset
    // only stores references (asset GUIDs), so it is safe to commit: on a machine that has imported the packs the
    // references resolve, and on one that hasn't they are simply empty and every move stays procedural.
    //
    // Made and refreshed by Vaatu's Revenge > Animation > Build Animation Set From ThirdParty. Edit any entry by
    // hand and the builder leaves it alone from then on (it notices the change, or tick Locked to be explicit).
    [CreateAssetMenu(menuName = "Vaatu's Revenge/Animation/Fighter Animation Set", fileName = "FighterAnimations")]
    public class FighterAnimationSet : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("The AnimationKeys id this clip plays for (e.g. jab, snap_kick, hurt, walk).")]
            public string AnimationKey = "";

            public AnimationClip Clip;

            [Tooltip("When the strike is fully extended in the clip (0 = clip start, 1 = clip end). The clip is time-warped "
                + "so this moment lands exactly on the move's impact frame (the end of Startup).")]
            [Range(0f, 1f)] public float ImpactNormalizedTime = 0.5f;

            [Tooltip("Trim: the part of the clip that is used starts here (0..1).")]
            [Range(0f, 1f)] public float StartNormalizedTime = 0f;

            [Tooltip("Trim: ...and ends here (0..1).")]
            [Range(0f, 1f)] public float EndNormalizedTime = 1f;

            [Tooltip("Play the clip left-right mirrored (e.g. the clip kicks with the right leg, our move uses the left).")]
            public bool Mirror;

            [Tooltip("Seconds to cross-fade from the previous clip into this one.")]
            [Min(0f)] public float BlendIn = 0.08f;

            [Tooltip("Locomotion only: the ground speed (m/s) the clip was made at. Playback rate = fighter speed / this. "
                + "0 = play at the clip's own speed.")]
            [Min(0f)] public float LoopReferenceSpeed;

            [Tooltip("Ticked: the Build Animation Set tool never changes this entry.")]
            public bool Locked;

            [Tooltip("Where the builder found the clip (pack / file), for people reading the asset.")]
            public string Source = "";

            // What the builder last wrote, so it can tell a hand edit apart from its own values. Not for people.
            [HideInInspector] public string BuilderStamp = "";

            public bool IsUsable => Clip != null && !string.IsNullOrEmpty(AnimationKey);

            // Trim and impact, sanitised so Start <= Impact <= End and End > Start.
            public void GetTimes(out float start, out float impact, out float end)
            {
                start = Mathf.Clamp01(StartNormalizedTime);
                end = Mathf.Clamp01(EndNormalizedTime);
                if (end <= start + 0.001f) { start = 0f; end = 1f; }
                impact = Mathf.Clamp(ImpactNormalizedTime, start, end);
            }
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        // Built on enable (and after Inspector edits), so runtime lookups never allocate.
        [NonSerialized] private Dictionary<string, int> lookup;

        public List<Entry> Entries => entries;
        public int Count => entries.Count;

        // Index of the usable entry for this key, or -1 (no entry, or its clip is missing).
        public int IndexOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return -1;
            if (lookup == null) RebuildLookup();
            return lookup.TryGetValue(key, out int index) ? index : -1;
        }

        public Entry GetEntry(int index)
        {
            return index >= 0 && index < entries.Count ? entries[index] : null;
        }

        public bool TryGet(string key, out Entry entry)
        {
            entry = GetEntry(IndexOf(key));
            return entry != null;
        }

        // True when at least one entry has a clip, i.e. the packs are imported on this machine.
        public bool HasAnyClip
        {
            get
            {
                if (lookup == null) RebuildLookup();
                return lookup.Count > 0;
            }
        }

        public void RebuildLookup()
        {
            if (lookup == null) lookup = new Dictionary<string, int>(StringComparer.Ordinal);
            else lookup.Clear();
            if (entries == null) entries = new List<Entry>();
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (e == null || !e.IsUsable) continue;
                if (!lookup.ContainsKey(e.AnimationKey)) lookup.Add(e.AnimationKey, i); // first one wins
            }
        }

        void OnEnable() { RebuildLookup(); }
        void OnValidate() { RebuildLookup(); }
    }
}
