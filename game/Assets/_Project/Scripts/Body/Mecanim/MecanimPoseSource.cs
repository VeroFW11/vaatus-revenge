using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Plays real mocap clips (the Asset Store martial-arts packs) on our procedural humanoid body, in place of the
    // procedural animation, for every AnimationKey that has a clip in the FighterAnimationSet. Keys without a clip
    // keep the procedural pose. Without the packs imported, the set has no clips and this component does nothing.
    //
    // How it works, for first-time readers:
    //  - Mecanim (Unity's animation system) can "retarget" Humanoid clips: a clip stored as human muscle movements
    //    plays on any skeleton that has a Humanoid Avatar. Our body is built in code, so there's no imported model
    //    to make that Avatar from; we build one at runtime from the body's bones and their T-pose (AvatarBuilder).
    //  - We don't use an Animator Controller (the state-machine asset). Our combat rules already decide what the
    //    body is doing and when (IHumanoidRig.CurrentActionCue / CurrentLocomotionCue), so we drive the clips
    //    directly with the Playables API: a tiny graph of clip players feeding one mixer, evaluated by hand each
    //    frame at exactly the times we choose. That is what keeps the clips locked to the frame data (see
    //    MecanimTimeWarp) and frozen during hitstop.
    //  - Each frame the clip pose is written to the bones and IHumanoidRig.ExternalPoseActive is set, so the
    //    procedural animator skips its own pose for that frame.
    //
    // Timing: runs in LateUpdate at ExecutionOrder. The body must build its cues before that (in Update, or in a
    // LateUpdate with a lower execution order) and apply/skip its procedural pose after it (a higher order).
    [DefaultExecutionOrder(ExecutionOrder)]
    [DisallowMultipleComponent]
    public class MecanimPoseSource : MonoBehaviour
    {
        public const int ExecutionOrder = 45; // after gameplay (0-20); before the body's pose application (50+)

        const float MinWeight = 0.001f;
        const float MinLoopRate = 0.25f, MaxLoopRate = 3f;

        [Tooltip("Which pack clips play for which moves. Made by Vaatu's Revenge > Animation > Build Animation Set From ThirdParty.")]
        [SerializeField] private FighterAnimationSet animationSet;

        // One warning for the whole session, not one per fighter (domain reload is off, so reset on play).
        static bool warnedInvalidAvatar;
        static bool warnedException;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            warnedInvalidAvatar = false;
            warnedException = false;
        }

        enum State { Waiting, Ready, Failed }

        IHumanoidRig rig;
        State state = State.Waiting;
        Avatar avatar;               // built at runtime, destroyed with this component
        bool avatarFailed;
        Animator animator;
        bool addedAnimator;
        Transform boundHips;         // the Hips the avatar was built for; if the body is rebuilt, rebuild too
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        HumanoidMirrorJob.Tables mirrorTables;
        readonly List<NativeArray<float>> mirrorScratch = new List<NativeArray<float>>();

        // Per set entry: its mixer input (-1 = entry has no clip) and clip player.
        int[] inputOfEntry = Array.Empty<int>();
        AnimationClipPlayable[] clipOfEntry = Array.Empty<AnimationClipPlayable>();

        // Two channels, each able to cross-fade from its previous clip into its current one.
        readonly Channel locomotion = new Channel();
        readonly Channel action = new Channel();
        readonly int[] weightedInputs = new int[4];
        int weightedCount;

        sealed class Channel
        {
            public int Current = -1, Previous = -1;   // entry indices
            public float Blend = 1f;                  // 0..1: fade from Previous into Current
            public float LastCueTime;
            public string LastKey;
            public float Phase;                       // locomotion: 0..1 of the trimmed cycle

            public void Reset()
            {
                Current = Previous = -1;
                Blend = 1f;
                LastCueTime = 0f;
                LastKey = null;
            }
        }

        public FighterAnimationSet AnimationSet
        {
            get => animationSet;
            set
            {
                if (animationSet == value) return;
                animationSet = value;
                if (isActiveAndEnabled) { TeardownGraph(); state = State.Waiting; }
            }
        }

        // For debug panels: is a pack clip on screen this frame, and which ones.
        public bool IsDriving { get; private set; }
        public string ActionClipName => ClipName(action.Current);
        public string LocomotionClipName => ClipName(locomotion.Current);

        // Adds a MecanimPoseSource next to the fighter's IHumanoidRig (its body component) if the set has at least
        // one clip on this machine; otherwise changes nothing and returns null. Safe in edit mode (the sandbox
        // builder) and play mode. The Avatar and Animator are only made at runtime, so a saved scene stays clean.
        public static MecanimPoseSource AttachIfAvailable(GameObject fighter, FighterAnimationSet set)
        {
            if (fighter == null || set == null || !set.HasAnyClip) return null;
            IHumanoidRig rig = fighter.GetComponentInChildren<IHumanoidRig>(true);
            if (!(rig is Component rigComponent)) return null;

            var source = rigComponent.GetComponent<MecanimPoseSource>();
            if (source == null) source = rigComponent.gameObject.AddComponent<MecanimPoseSource>();
            source.AnimationSet = set;
            return source;
        }

        void OnEnable()
        {
            state = State.Waiting;
        }

        void OnDisable()
        {
            TeardownGraph();
            IsDriving = false;
            state = State.Waiting;
        }

        void OnDestroy()
        {
            TeardownGraph();
            if (animator != null && addedAnimator)
            {
                animator.avatar = null;
                Destroy(animator);
            }
            if (avatar != null) Destroy(avatar);
            avatar = null;
        }

        void LateUpdate()
        {
            if (state == State.Failed) return;
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                // Never let a presentation problem break the frame: go back to procedural for good.
                if (!warnedException)
                {
                    warnedException = true;
                    Debug.LogWarning("MecanimPoseSource on '" + name + "' stopped and fell back to procedural animation: " + e);
                }
                TeardownGraph();
                IsDriving = false;
                state = State.Failed;
            }
        }

        void Tick()
        {
            IsDriving = false;
            if (animationSet == null) return;
            if (state == State.Waiting && !TryInitialise()) return;
            if (!graph.IsValid() || animator == null) { TeardownGraph(); state = State.Waiting; return; }
            if (rig.GetBone(HumanBodyBones.Hips) != boundHips) { ForgetAvatar(); return; } // body rebuilt: new bones

            AnimationCue actionCue = rig.CurrentActionCue;
            AnimationCue locoCue = rig.CurrentLocomotionCue;
            int actionEntry = actionCue.IsValid ? UsableEntry(actionCue.Key) : -1;
            int locoEntry = locoCue.IsValid ? UsableEntry(locoCue.Key) : -1;
            bool actionShows = actionCue.IsValid && actionCue.Weight > MinWeight;

            UpdateLocomotion(locoCue, locoEntry); // keeps the cycle phase moving even while an action shows

            if (actionShows && actionEntry < 0)
            {
                // A move with no clip: the procedural animator shows the whole body (it can't be mixed per bone
                // with a Mecanim pose), so step aside.
                action.Reset();
                ClearWeights();
                return;
            }
            if (actionShows) UpdateAction(actionCue, actionEntry);
            else action.Reset();

            float actionWeight = actionShows ? (locoEntry >= 0 ? Mathf.Clamp01(actionCue.Weight) : 1f) : 0f;
            float locoWeight = locoEntry >= 0 ? 1f - actionWeight : 0f;
            if (actionWeight + locoWeight <= MinWeight)
            {
                ClearWeights();
                return;
            }

            ClearWeights();
            ApplyChannelWeights(action, actionWeight);
            ApplyChannelWeights(locomotion, locoWeight);

            graph.Evaluate(0f);           // times were set by hand above; 0 = don't advance them
            rig.ExternalPoseActive = true;
            IsDriving = true;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Channels
        // ---------------------------------------------------------------------------------------------------------

        void UpdateAction(AnimationCue cue, int entryIndex)
        {
            FighterAnimationSet.Entry entry = animationSet.GetEntry(entryIndex);
            bool newMove = entryIndex != action.Current;
            bool restarted = !newMove && cue.Time + 1e-4f < action.LastCueTime;
            if (newMove)
            {
                action.Previous = action.Current;  // fades out, frozen on its last frame
                action.Current = entryIndex;
            }
            else if (restarted)
            {
                action.Previous = -1;              // same clip again: one player can't be in two places, so cut
            }

            if (action.Previous >= 0)
            {
                action.Blend = entry.BlendIn > 1e-4f ? Mathf.Clamp01(cue.Time / entry.BlendIn) : 1f;
                if (action.Blend >= 1f) action.Previous = -1;
            }
            else
            {
                action.Blend = 1f;
            }

            AnimationClip clip = entry.Clip;
            entry.GetTimes(out float start, out float impact, out float end);
            float normalised = MecanimTimeWarp.ActionNormalizedTime(cue.Time, cue.Duration, cue.ImpactTime,
                start, impact, end, clip.length, clip.isLooping);
            SetClipTime(entryIndex, normalised);
            action.LastCueTime = cue.Time;
            action.LastKey = cue.Key;
        }

        void UpdateLocomotion(AnimationCue cue, int entryIndex)
        {
            if (entryIndex < 0)
            {
                locomotion.Reset();
                return;
            }

            // Advance by how much the cue's own clock moved (scaled time: hitstop and slow motion follow).
            float dt;
            if (cue.Key == locomotion.LastKey && cue.Time >= locomotion.LastCueTime) dt = cue.Time - locomotion.LastCueTime;
            else dt = Mathf.Max(0f, cue.Time); // a new cue: its Time counts from its own start

            if (entryIndex != locomotion.Current)
            {
                // Walk -> run: keep the cycle phase so the feet stay in step, and fade from the old cycle.
                locomotion.Previous = locomotion.Current;
                locomotion.Current = entryIndex;
                locomotion.Blend = locomotion.Previous >= 0 ? 0f : 1f;
            }

            FighterAnimationSet.Entry entry = animationSet.GetEntry(entryIndex);
            if (locomotion.Previous >= 0)
            {
                locomotion.Blend = entry.BlendIn > 1e-4f ? Mathf.Clamp01(locomotion.Blend + dt / entry.BlendIn) : 1f;
                if (locomotion.Blend >= 1f) locomotion.Previous = -1;
            }

            entry.GetTimes(out float start, out float _, out float end);
            float cycleSeconds = Mathf.Max(1e-3f, (end - start) * entry.Clip.length);
            float rate = 1f;
            if (entry.LoopReferenceSpeed > 0.01f && cue.Speed > 0.01f)
                rate = Mathf.Clamp(cue.Speed / entry.LoopReferenceSpeed, MinLoopRate, MaxLoopRate);
            locomotion.Phase = Mathf.Repeat(locomotion.Phase + dt * rate / cycleSeconds, 1f);

            SetClipTime(entryIndex, MecanimTimeWarp.LoopNormalizedTime(locomotion.Phase, start, end));
            locomotion.LastCueTime = cue.Time;
            locomotion.LastKey = cue.Key;
        }

        void ApplyChannelWeights(Channel channel, float channelWeight)
        {
            if (channelWeight <= MinWeight || channel.Current < 0) return;
            if (channel.Previous >= 0)
            {
                SetWeight(channel.Previous, channelWeight * (1f - channel.Blend));
                SetWeight(channel.Current, channelWeight * channel.Blend);
            }
            else
            {
                SetWeight(channel.Current, channelWeight);
            }
        }

        void SetWeight(int entryIndex, float weight)
        {
            int input = inputOfEntry[entryIndex];
            if (input < 0) return;
            mixer.SetInputWeight(input, weight);
            if (weightedCount < weightedInputs.Length) weightedInputs[weightedCount++] = input;
        }

        // Zero only the inputs weighted last frame (no per-frame loop over every clip).
        void ClearWeights()
        {
            for (int i = 0; i < weightedCount; i++) mixer.SetInputWeight(weightedInputs[i], 0f);
            weightedCount = 0;
        }

        void SetClipTime(int entryIndex, float normalised)
        {
            AnimationClipPlayable clip = clipOfEntry[entryIndex];
            float length = clip.GetAnimationClip().length;
            // Stay just short of the end, or a clip imported with Loop Time wraps back to its first frame.
            double time = Math.Min(Mathf.Clamp01(normalised) * length, Math.Max(0.0, length - 1e-4));
            clip.SetTime(time);
        }

        int UsableEntry(string key)
        {
            if (StyledProcedurally(key)) return -1;
            int index = animationSet.IndexOf(key);
            return index >= 0 && index < inputOfEntry.Length && inputOfEntry[index] >= 0 ? index : -1;
        }

        // The player's element styles (Water's Tai Chi stance, Earth's charge...) are procedural clips ("water:idle"). The
        // pack set is one set of clips for every element, so for a key the current style overrides, the procedural pose
        // wins: otherwise every element would stand in the same pack idle.
        bool StyledProcedurally(string key)
        {
            if (procedural == null) procedural = GetComponent<BodyAnimatorDriver>();
            FighterAnimator body = procedural != null ? procedural.Animator : null;
            if (body == null || string.IsNullOrEmpty(body.Style)) return false;
            return !ReferenceEquals(body.Clip(key), body.Library.Get(key));
        }

        BodyAnimatorDriver procedural;

        string ClipName(int entryIndex)
        {
            FighterAnimationSet.Entry entry = animationSet != null ? animationSet.GetEntry(entryIndex) : null;
            return entry != null && entry.Clip != null ? entry.Clip.name : "";
        }

        // ---------------------------------------------------------------------------------------------------------
        // Set-up: Avatar, Animator and the playable graph
        // ---------------------------------------------------------------------------------------------------------

        bool TryInitialise()
        {
            if (!animationSet.HasAnyClip) return false;            // packs not imported here: stay procedural, quietly
            if (rig == null) rig = GetComponent<IHumanoidRig>();
            if (rig == null)
            {
                if (!warnedInvalidAvatar)
                {
                    warnedInvalidAvatar = true;
                    Debug.LogWarning("MecanimPoseSource on '" + name + "' needs an IHumanoidRig (the HumanoidBody) on the same "
                        + "GameObject; pack clips are off for it.");
                }
                state = State.Failed;
                return false;
            }
            if (!rig.IsBuilt || rig.Root == null) return false;       // body not built yet: try again next frame
            if (avatarFailed) { state = State.Failed; return false; }

            if (avatar == null)
            {
                avatar = BuildAvatar(rig, out string problem);
                if (avatar == null)
                {
                    avatarFailed = true;
                    if (!warnedInvalidAvatar)
                    {
                        warnedInvalidAvatar = true;
                        Debug.LogWarning("Pack animations are off: couldn't build a Humanoid Avatar for '" + name + "' ("
                            + problem + "). Fighters use procedural animation.");
                    }
                    state = State.Failed;
                    return false;
                }
            }

            GameObject rootObject = rig.Root.gameObject;
            if (animator == null)
            {
                animator = rootObject.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = rootObject.AddComponent<Animator>();
                    addedAnimator = true;
                }
            }
            animator.applyRootMotion = false;                        // the CharacterController moves the fighter
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (animator.avatar != avatar)
            {
                animator.avatar = avatar;
                animator.Rebind();
            }

            boundHips = rig.GetBone(HumanBodyBones.Hips);
            BuildGraph();
            state = State.Ready;
            return true;
        }

        void ForgetAvatar()
        {
            TeardownGraph();
            if (animator != null) animator.avatar = null;
            if (avatar != null) Destroy(avatar);
            avatar = null;
            boundHips = null;
            state = State.Waiting;
        }

        void BuildGraph()
        {
            TeardownGraph();
            graph = PlayableGraph.Create("MecanimPoseSource " + name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);     // we set every time ourselves, then Evaluate

            List<FighterAnimationSet.Entry> entries = animationSet.Entries;
            inputOfEntry = new int[entries.Count];
            clipOfEntry = new AnimationClipPlayable[entries.Count];
            int usable = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                FighterAnimationSet.Entry e = entries[i];
                bool ok = e != null && e.IsUsable && animationSet.IndexOf(e.AnimationKey) == i;
                inputOfEntry[i] = ok ? usable++ : -1;
            }

            mixer = AnimationMixerPlayable.Create(graph, usable);
            for (int i = 0; i < entries.Count; i++)
            {
                if (inputOfEntry[i] < 0) continue;
                FighterAnimationSet.Entry e = entries[i];
                AnimationClipPlayable clip = AnimationClipPlayable.Create(graph, e.Clip);
                clip.SetApplyFootIK(true);   // plants the feet where the clip put them, after retargeting
                clipOfEntry[i] = clip;

                Playable node = clip;
                if (e.Mirror) node = CreateMirror(clip);
                graph.Connect(node, 0, mixer, inputOfEntry[i]);
                mixer.SetInputWeight(inputOfEntry[i], 0f);
            }

            var output = AnimationPlayableOutput.Create(graph, "Pose", animator);
            output.SetSourcePlayable(mixer);

            locomotion.Reset();
            action.Reset();
            weightedCount = 0;
        }

        Playable CreateMirror(AnimationClipPlayable clip)
        {
            if (mirrorTables == null || !mirrorTables.IsCreated) mirrorTables = HumanoidMirrorJob.Tables.Create();
            var scratch = new NativeArray<float>(mirrorTables.Muscles.Length, Allocator.Persistent);
            mirrorScratch.Add(scratch);
            var job = new HumanoidMirrorJob
            {
                Muscles = mirrorTables.Muscles,
                SourceIndex = mirrorTables.SourceIndex,
                Sign = mirrorTables.Sign,
                Values = scratch,
            };
            AnimationScriptPlayable mirror = AnimationScriptPlayable.Create(graph, job, 1);
            mirror.SetProcessInputs(false);  // the job reads its input stream itself
            graph.Connect(clip, 0, mirror, 0);
            mirror.SetInputWeight(0, 1f);
            return mirror;
        }

        void TeardownGraph()
        {
            if (graph.IsValid()) graph.Destroy();
            // Native memory is freed only after the graph (whose jobs use it) is gone.
            for (int i = 0; i < mirrorScratch.Count; i++)
                if (mirrorScratch[i].IsCreated) mirrorScratch[i].Dispose();
            mirrorScratch.Clear();
            if (mirrorTables != null) mirrorTables.Dispose();
            mirrorTables = null;
            inputOfEntry = Array.Empty<int>();
            clipOfEntry = Array.Empty<AnimationClipPlayable>();
            weightedCount = 0;
            locomotion.Reset();
            action.Reset();
        }

        // ---------------------------------------------------------------------------------------------------------
        // Avatar from the body's bones
        // ---------------------------------------------------------------------------------------------------------

        // Maps each HumanBodyBones slot to our bone's transform name and records the T-pose, which is what a
        // Humanoid Avatar needs: which transform is "LeftUpperArm", and how every bone sits at rest. Returns null
        // and a readable reason when the skeleton can't make a valid human avatar.
        static Avatar BuildAvatar(IHumanoidRig rig, out string problem)
        {
            Transform root = rig.Root;
            var problems = new List<string>();
            var humanBones = new List<HumanBone>();
            var mapped = new Dictionary<Transform, HumanBodyBones>();
            var missingRequired = new List<string>();

            int boneCount = Math.Min(HumanTrait.BoneCount, (int)HumanBodyBones.LastBone);
            for (int i = 0; i < boneCount; i++)
            {
                var slot = (HumanBodyBones)i;
                Transform bone = rig.GetBone(slot);
                if (bone == null)
                {
                    if (HumanTrait.RequiredBone(i)) missingRequired.Add(slot.ToString());
                    continue;
                }
                if (bone == root || !bone.IsChildOf(root))
                {
                    problems.Add(slot + " bone '" + bone.name + "' is not under the rig Root");
                    continue;
                }
                if (mapped.ContainsKey(bone))
                {
                    problems.Add("'" + bone.name + "' is used for both " + mapped[bone] + " and " + slot);
                    continue;
                }
                mapped.Add(bone, slot);
                humanBones.Add(new HumanBone
                {
                    humanName = HumanTrait.BoneName[i],
                    boneName = bone.name,
                    limit = new HumanLimit { useDefaultValues = true }, // Unity's standard human muscle ranges
                });
            }
            if (missingRequired.Count > 0) problems.Add("missing required bones: " + string.Join(", ", missingRequired));

            // The skeleton: the root, every mapped bone and every transform between them, parents first.
            var skeletonSet = new HashSet<Transform> { root };
            foreach (Transform bone in mapped.Keys)
                for (Transform t = bone; t != null && t != root; t = t.parent) skeletonSet.Add(t);
            var skeleton = new List<Transform>(skeletonSet);
            skeleton.Sort((a, b) => Depth(a, root).CompareTo(Depth(b, root)));

            // AvatarBuilder finds bones by name, so names in the hierarchy must be unique.
            var names = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                names[t.name] = names.TryGetValue(t.name, out int n) ? n + 1 : 1;
            foreach (Transform t in skeleton)
                if (names.TryGetValue(t.name, out int n) && n > 1) problems.Add("bone name '" + t.name + "' is not unique under Root");

            if (problems.Count > 0)
            {
                problem = string.Join("; ", problems);
                return null;
            }

            var skeletonBones = new SkeletonBone[skeleton.Count];
            for (int i = 0; i < skeleton.Count; i++)
            {
                Transform t = skeleton[i];
                bool isHuman = mapped.TryGetValue(t, out HumanBodyBones slot);
                skeletonBones[i] = new SkeletonBone
                {
                    name = t.name,
                    position = isHuman ? rig.GetRestLocalPosition(slot) : t.localPosition,
                    rotation = isHuman ? rig.GetRestLocalRotation(slot) : t.localRotation,
                    scale = t.localScale,
                };
            }

            var description = new HumanDescription
            {
                human = humanBones.ToArray(),
                skeleton = skeletonBones,
                // Unity's importer defaults: how twist is shared along the limbs, a little stretch to absorb
                // proportion differences, no extra translation.
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false,
            };

            Avatar built = AvatarBuilder.BuildHumanAvatar(root.gameObject, description);
            if (built == null)
            {
                problem = "AvatarBuilder returned nothing";
                return null;
            }
            if (!built.isValid || !built.isHuman)
            {
                Destroy(built);
                problem = "AvatarBuilder rejected the skeleton (is the rest pose a T-pose, with Hips under Root?)";
                return null;
            }
            built.name = root.name + " Avatar (runtime)";
            problem = null;
            return built;
        }

        static int Depth(Transform t, Transform root)
        {
            int depth = 0;
            for (; t != null && t != root; t = t.parent) depth++;
            return depth;
        }
    }
}
