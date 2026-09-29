using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VaatusRevenge.EditorTools
{
    // Writes a text catalogue of the animation packs in Assets/ThirdParty so someone WITHOUT the files (a cloud
    // Claude session, or a teammate who hasn't imported the packs yet) can still plan which clip goes with which
    // combat move: Vaatu's Revenge > Animation > Write Animation Catalogue.
    //
    // Why a catalogue instead of committing the packs: the repo is public and the Asset Store licence forbids
    // redistributing the files, so Assets/ThirdParty is git-ignored and every teammate imports the packs from their
    // own Unity account. The catalogue (docs/Prototype/Animation-Catalogue.json) holds metadata only: names, GUIDs,
    // lengths, frame rates, loop flags, and a rough "when does the strike land" estimate. No keyframes, no meshes,
    // so it is safe to commit.
    //
    // Why the GUIDs matter: Unity identifies every asset by the GUID in its .meta file, and every clip inside an FBX
    // by a "local file ID". Asset Store packs ship their .meta files, so those IDs are the same on David's and
    // Jeremy's machines. That means a tool or a scene can refer to "clip X in pack Y" reliably, even though the
    // files themselves never go through git.
    //
    // The catalogue never changes import settings. The second menu item (Set ThirdParty Rigs To Humanoid) is the
    // only thing here that does, and it asks first.
    public static class AnimationCatalogue
    {
        public const string ThirdPartyFolder = "Assets/ThirdParty";
        public const string OutputRelativePath = "docs/Prototype/Animation-Catalogue.json";

        const string MenuRoot = "Vaatu's Revenge/Animation/";
        const string DialogTitle = "Animation Catalogue";

        // Unity creates hidden preview clips inside some model files; they aren't real animations.
        const string PreviewClipPrefix = "__preview__";

        // The humanoid "IK goal" curves we read for the reach estimate. On a Humanoid clip Unity stores, besides the
        // muscle curves, where each hand and foot ends up ("LeftHandT" = left hand translation) and where the body's
        // centre is ("RootT"). Distance from body centre to hand/foot = how far that limb is extended.
        static readonly string[] ReachLimbs = { "LeftHand", "RightHand", "LeftFoot", "RightFoot" };
        const string RootGoal = "RootT";

        const string ReachHeuristicNote =
            "peakReachHeuristic is a guess at a strike's impact moment: for each hand/foot, the time its humanoid IK goal is "
            + "furthest from the body centre (RootT). Distances are in Unity's humanoid-normalised units (scaled by the "
            + "character's height, roughly metres for an adult), not exact metres. Good for punches/kicks, meaningless for "
            + "idles and locomotion. Confirm by eye in the Animation preview before relying on it.";

        // ---------------------------------------------------------------------------------------------------------
        // Menu item 1: write the catalogue
        // ---------------------------------------------------------------------------------------------------------

        [MenuItem(MenuRoot + "Write Animation Catalogue", false, 40)]
        public static void WriteCatalogueFromMenu()
        {
            if (!EditorIsIdle("write the animation catalogue")) return;
            if (!ThirdPartyFolderExists()) return;

            var catalogue = new Catalogue();
            try
            {
                ScanModels(catalogue);
                ScanStandaloneClips(catalogue);
            }
            finally
            {
                // Always clear the bar, even if something threw, or it stays stuck on screen.
                EditorUtility.ClearProgressBar();
            }

            string outputPath = OutputFullPath();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                File.WriteAllText(outputPath, catalogue.ToJson(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogError("Animation catalogue: couldn't write " + outputPath + ": " + e.Message);
                return;
            }

            Debug.Log("Animation catalogue written to " + outputPath + "\n"
                + catalogue.Models.Count + " models (" + catalogue.CountModels(ModelImporterAnimationType.Human) + " Humanoid, "
                + catalogue.CountModels(ModelImporterAnimationType.Generic) + " Generic, "
                + catalogue.CountModels(ModelImporterAnimationType.Legacy) + " Legacy, "
                + catalogue.CountModels(ModelImporterAnimationType.None) + " no rig), "
                + catalogue.ClipsInModels + " clips inside models, " + catalogue.StandaloneClips.Count + " standalone .anim clips, "
                + catalogue.ClipsWithReach + " clips with a reach estimate"
                + (catalogue.Failed.Count > 0 ? ", " + catalogue.Failed.Count + " assets failed (see warnings above)" : "") + ".");
        }

        static void ScanModels(Catalogue catalogue)
        {
            List<string> paths = FindAssetPaths("t:Model");
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                EditorUtility.DisplayProgressBar(DialogTitle, "Reading " + Path.GetFileName(path), (float)i / Math.Max(1, paths.Count));
                try
                {
                    if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) continue;
                    catalogue.Models.Add(ReadModel(path, importer));
                }
                catch (Exception e)
                {
                    catalogue.Failed.Add(new FailedAsset { Path = path, Error = e.Message });
                    Debug.LogWarning("Animation catalogue: skipped " + path + ": " + e.Message);
                }
            }
        }

        static void ScanStandaloneClips(Catalogue catalogue)
        {
            // FindAssets("t:AnimationClip") also returns clips that live inside FBX files; those were covered by
            // ScanModels, so only assets that aren't models (i.e. .anim files) are read here.
            List<string> paths = FindAssetPaths("t:AnimationClip");
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                EditorUtility.DisplayProgressBar(DialogTitle, "Reading " + Path.GetFileName(path), (float)i / Math.Max(1, paths.Count));
                try
                {
                    if (AssetImporter.GetAtPath(path) is ModelImporter) continue;
                    foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        if (asset is AnimationClip clip && !clip.name.StartsWith(PreviewClipPrefix, StringComparison.Ordinal))
                            catalogue.StandaloneClips.Add(ReadClip(clip, path, null));
                    }
                }
                catch (Exception e)
                {
                    catalogue.Failed.Add(new FailedAsset { Path = path, Error = e.Message });
                    Debug.LogWarning("Animation catalogue: skipped " + path + ": " + e.Message);
                }
            }
        }

        static ModelInfo ReadModel(string path, ModelImporter importer)
        {
            var model = new ModelInfo
            {
                Path = path,
                Pack = PackName(path),
                Guid = AssetDatabase.AssetPathToGUID(path),
                AnimationType = importer.animationType,
                AvatarSetup = importer.avatarSetup.ToString(),
            };

            // A SkinnedMeshRenderer is a mesh bent by a skeleton: if the model has one, it's a usable character,
            // not just a file of animations.
            if (AssetDatabase.LoadMainAssetAtPath(path) is GameObject root)
                model.SkinnedMeshCount = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;

            // The clip settings the importer uses: the ones someone edited, or the defaults read from the file.
            ModelImporterClipAnimation[] clipSettings = importer.clipAnimations;
            if (clipSettings == null || clipSettings.Length == 0) clipSettings = importer.defaultClipAnimations;

            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Avatar avatar)
                {
                    model.HasAvatar = true;
                    model.AvatarIsValid = avatar.isValid;
                    model.AvatarIsHuman = avatar.isHuman;
                }
                else if (asset is AnimationClip clip && !clip.name.StartsWith(PreviewClipPrefix, StringComparison.Ordinal))
                {
                    model.Clips.Add(ReadClip(clip, path, clipSettings));
                }
            }
            return model;
        }

        static ClipInfo ReadClip(AnimationClip clip, string assetPath, ModelImporterClipAnimation[] clipSettings)
        {
            var info = new ClipInfo
            {
                Name = clip.name,
                AssetPath = assetPath,
                AssetGuid = AssetDatabase.AssetPathToGUID(assetPath),
                Length = clip.length,
                FrameRate = clip.frameRate,
                IsLooping = clip.isLooping,
                IsHumanMotion = clip.isHumanMotion,
                HasGenericRootTransform = clip.hasGenericRootTransform,
                HasMotionCurves = clip.hasMotionCurves,
                HasRootCurves = clip.hasRootCurves,
                EventCount = AnimationUtility.GetAnimationEvents(clip).Length,
            };

            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string _, out long localId))
                info.LocalFileId = localId;

            // Inside a model file, "loop" is an import setting ("Loop Time"), so trust that when we can find it.
            if (clipSettings != null)
            {
                foreach (ModelImporterClipAnimation setting in clipSettings)
                {
                    if (setting.name != clip.name) continue;
                    info.IsLooping |= setting.loopTime;
                    break;
                }
            }

            info.Reach = EstimateReach(clip, out info.ReachUnavailableReason);
            return info;
        }

        // For each hand and foot, finds when it is furthest from the body centre. For a punch or kick that is
        // usually the moment of impact, which is when the hitbox should be live. Only a heuristic: see the note.
        static ReachEstimate EstimateReach(AnimationClip clip, out string unavailableReason)
        {
            unavailableReason = null;
            if (!clip.isHumanMotion)
            {
                unavailableReason = "Not a Humanoid clip: IK goal curves only exist on Humanoid clips. Run "
                    + "'Set ThirdParty Rigs To Humanoid', then write the catalogue again.";
                return null;
            }
            if (clip.length <= 0f)
            {
                unavailableReason = "Clip has zero length.";
                return null;
            }

            // Humanoid curves live on the Animator with an empty path; collect just the ones we need.
            var curves = new Dictionary<string, AnimationCurve>();
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Animator) || !string.IsNullOrEmpty(binding.path)) continue;
                if (IsWantedGoalCurve(binding.propertyName))
                    curves[binding.propertyName] = AnimationUtility.GetEditorCurve(clip, binding);
            }

            if (!TryGetGoalCurves(curves, RootGoal, out AnimationCurve[] rootCurves))
            {
                unavailableReason = "Humanoid clip has no RootT.x/y/z curves, so there's no body centre to measure from.";
                return null;
            }

            float frameRate = clip.frameRate > 0f ? clip.frameRate : 30f;
            int samples = Math.Max(2, Mathf.CeilToInt(clip.length * frameRate) + 1);
            var estimate = new ReachEstimate { SampleCount = samples, SampleRate = frameRate };

            foreach (string limb in ReachLimbs)
            {
                if (!TryGetGoalCurves(curves, limb + "T", out AnimationCurve[] limbCurves)) continue;

                var reach = new LimbReach { Limb = limb };
                reach.PeakDistance = float.MinValue;
                for (int i = 0; i < samples; i++)
                {
                    float t = Mathf.Min(i / frameRate, clip.length);
                    float distance = Vector3.Distance(Evaluate(limbCurves, t), Evaluate(rootCurves, t));
                    if (i == 0) reach.RestDistance = distance;
                    if (distance > reach.PeakDistance)
                    {
                        reach.PeakDistance = distance;
                        reach.PeakTime = t;
                    }
                }
                reach.PeakNormalizedTime = reach.PeakTime / clip.length;
                estimate.Limbs.Add(reach);
            }

            if (estimate.Limbs.Count == 0)
            {
                unavailableReason = "Humanoid clip has no hand/foot IK goal curves (LeftHandT, RightHandT, LeftFootT, RightFootT).";
                return null;
            }
            return estimate;
        }

        static bool IsWantedGoalCurve(string propertyName)
        {
            if (propertyName.StartsWith(RootGoal + ".", StringComparison.Ordinal)) return true;
            foreach (string limb in ReachLimbs)
                if (propertyName.StartsWith(limb + "T.", StringComparison.Ordinal)) return true;
            return false;
        }

        static bool TryGetGoalCurves(Dictionary<string, AnimationCurve> curves, string goal, out AnimationCurve[] xyz)
        {
            xyz = new AnimationCurve[3];
            return curves.TryGetValue(goal + ".x", out xyz[0]) && xyz[0] != null
                && curves.TryGetValue(goal + ".y", out xyz[1]) && xyz[1] != null
                && curves.TryGetValue(goal + ".z", out xyz[2]) && xyz[2] != null;
        }

        static Vector3 Evaluate(AnimationCurve[] xyz, float time)
        {
            return new Vector3(xyz[0].Evaluate(time), xyz[1].Evaluate(time), xyz[2].Evaluate(time));
        }

        // ---------------------------------------------------------------------------------------------------------
        // Menu item 2: switch Generic rigs to Humanoid
        // ---------------------------------------------------------------------------------------------------------

        // Why Humanoid matters: Unity's animation system (Mecanim) can "retarget" Humanoid animation. A Humanoid
        // clip is stored as muscle movements of a standard human body instead of rotations of one specific
        // skeleton's bones, so ANY Humanoid clip plays on ANY Humanoid character: a punch from pack A, a kick from
        // pack B and a Mixamo roll all work on our one player model. A Generic clip only plays on the exact
        // skeleton it was made for. Humanoid clips also carry the hand/foot curves the reach estimate reads.
        //
        // Some files can't become Humanoid (odd bone names, missing bones, animation-only files meant to borrow
        // another model's avatar). Those are put back to Generic so nothing is left broken, and listed in the log;
        // they need hand-setup in the Rig tab (e.g. "Copy From Other Avatar").
        [MenuItem(MenuRoot + "Set ThirdParty Rigs To Humanoid", false, 41)]
        public static void SetRigsToHumanoidFromMenu()
        {
            if (!EditorIsIdle("change rig settings")) return;
            if (!ThirdPartyFolderExists()) return;

            var generic = new List<string>();
            foreach (string path in FindAssetPaths("t:Model"))
            {
                if (AssetImporter.GetAtPath(path) is ModelImporter importer && importer.animationType == ModelImporterAnimationType.Generic)
                    generic.Add(path);
            }
            if (generic.Count == 0)
            {
                EditorUtility.DisplayDialog(DialogTitle, "No Generic rigs found in " + ThirdPartyFolder + ". Nothing to change.", "OK");
                return;
            }

            bool go = EditorUtility.DisplayDialog(DialogTitle,
                "Switch " + generic.Count + " model(s) in " + ThirdPartyFolder + " from Generic to Humanoid?\n\n"
                + "Humanoid lets any humanoid clip play on any humanoid character (retargeting). Each file is reimported, "
                + "which can take a while. Files that can't map to a human skeleton are put back to Generic and listed "
                + "in the Console.\n\nThis only changes the .meta import settings of the pack files, which are not in git.",
                "Switch to Humanoid", "Cancel");
            if (!go) return;

            var changed = new List<string>();
            var failed = new List<string>();
            try
            {
                for (int i = 0; i < generic.Count; i++)
                {
                    string path = generic[i];
                    EditorUtility.DisplayProgressBar(DialogTitle, "Reimporting " + Path.GetFileName(path), (float)i / generic.Count);
                    try
                    {
                        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                        importer.animationType = ModelImporterAnimationType.Human;
                        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                        importer.SaveAndReimport();

                        // Unity reports bone-mapping problems as Console warnings during the reimport; the reliable
                        // test afterwards is whether a valid human avatar came out.
                        if (HasValidHumanAvatar(path))
                        {
                            changed.Add(path);
                        }
                        else
                        {
                            importer = (ModelImporter)AssetImporter.GetAtPath(path);
                            importer.animationType = ModelImporterAnimationType.Generic;
                            importer.SaveAndReimport();
                            failed.Add(path + " (no valid human avatar; see the import warnings above; put back to Generic)");
                        }
                    }
                    catch (Exception e)
                    {
                        failed.Add(path + " (" + e.Message + ")");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (changed.Count > 0)
                Debug.Log("Set to Humanoid (" + changed.Count + "):\n" + string.Join("\n", changed));
            if (failed.Count > 0)
                Debug.LogWarning("Couldn't set to Humanoid (" + failed.Count + "):\n" + string.Join("\n", failed));
            Debug.Log("Rig switch done: " + changed.Count + " changed, " + failed.Count + " failed. "
                + "Run 'Write Animation Catalogue' again to refresh the catalogue.");
        }

        static bool HasValidHumanAvatar(string path)
        {
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is Avatar avatar && avatar.isValid && avatar.isHuman) return true;
            return false;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------------------------------------

        static bool EditorIsIdle(string action)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog(DialogTitle, "Can't " + action + " while in Play mode or while scripts compile. Try again in a moment.", "OK");
                return false;
            }
            return true;
        }

        static bool ThirdPartyFolderExists()
        {
            if (AssetDatabase.IsValidFolder(ThirdPartyFolder)) return true;
            EditorUtility.DisplayDialog(DialogTitle,
                "There's no " + ThirdPartyFolder + " folder yet.\n\n"
                + "1. Window > Package Manager, choose 'My Assets' at the top left.\n"
                + "2. For each animation pack: Download, then Import (import everything).\n"
                + "3. In the Project window, create the folder Assets/ThirdParty and drag each pack's imported folder into it.\n\n"
                + "Git ignores Assets/ThirdParty on purpose: the Asset Store licence doesn't allow the files in our public repo, "
                + "so everyone imports the packs from their own Unity account.",
                "OK");
            return false;
        }

        static List<string> FindAssetPaths(string filter)
        {
            var paths = new List<string>();
            var seen = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { ThirdPartyFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && seen.Add(path)) paths.Add(path);
            }
            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        // "Assets/ThirdParty/Brawler Animations/Fbx/Punch.fbx" -> "Brawler Animations".
        static string PackName(string path)
        {
            string rest = path.Substring(Math.Min(path.Length, ThirdPartyFolder.Length + 1));
            int slash = rest.IndexOf('/');
            return slash > 0 ? rest.Substring(0, slash) : "";
        }

        // Application.dataPath is <repo>/game/Assets, so the repo root is two folders up.
        static string OutputFullPath()
        {
            string repoRoot = Directory.GetParent(Application.dataPath).Parent.FullName;
            return Path.Combine(repoRoot, OutputRelativePath);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Catalogue data and JSON. Hand-written JSON (not JsonUtility) so fields can be left out when they don't
        // apply and numbers are rounded to readable lengths.
        // ---------------------------------------------------------------------------------------------------------

        class Catalogue
        {
            public readonly List<ModelInfo> Models = new List<ModelInfo>();
            public readonly List<ClipInfo> StandaloneClips = new List<ClipInfo>();
            public readonly List<FailedAsset> Failed = new List<FailedAsset>();

            public int ClipsInModels
            {
                get { int n = 0; foreach (ModelInfo m in Models) n += m.Clips.Count; return n; }
            }

            public int ClipsWithReach
            {
                get
                {
                    int n = 0;
                    foreach (ModelInfo m in Models) foreach (ClipInfo c in m.Clips) if (c.Reach != null) n++;
                    foreach (ClipInfo c in StandaloneClips) if (c.Reach != null) n++;
                    return n;
                }
            }

            public int CountModels(ModelImporterAnimationType type)
            {
                int n = 0;
                foreach (ModelInfo m in Models) if (m.AnimationType == type) n++;
                return n;
            }

            public string ToJson()
            {
                int humanClips = 0;
                foreach (ModelInfo m in Models) foreach (ClipInfo c in m.Clips) if (c.IsHumanMotion) humanClips++;
                foreach (ClipInfo c in StandaloneClips) if (c.IsHumanMotion) humanClips++;
                int withMesh = 0;
                foreach (ModelInfo m in Models) if (m.SkinnedMeshCount > 0) withMesh++;

                var w = new JsonWriter();
                w.BeginObject();
                w.Key("header"); w.BeginObject();
                w.Field("generatedAt", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                w.Field("unityVersion", Application.unityVersion);
                w.Field("scannedFolder", ThirdPartyFolder);
                w.Field("note", "Metadata only: names, IDs, timings and flags. Contains no animation or mesh data, so it is safe "
                    + "to commit. The packs themselves are Asset Store files and stay out of git.");
                w.Field("peakReachHeuristicNote", ReachHeuristicNote);
                w.Key("counts"); w.BeginObject();
                w.Field("models", Models.Count);
                w.Field("humanoidModels", CountModels(ModelImporterAnimationType.Human));
                w.Field("genericModels", CountModels(ModelImporterAnimationType.Generic));
                w.Field("legacyModels", CountModels(ModelImporterAnimationType.Legacy));
                w.Field("noRigModels", CountModels(ModelImporterAnimationType.None));
                w.Field("modelsWithSkinnedMesh", withMesh);
                w.Field("clipsInModels", ClipsInModels);
                w.Field("standaloneClips", StandaloneClips.Count);
                w.Field("humanoidClips", humanClips);
                w.Field("clipsWithPeakReach", ClipsWithReach);
                w.Field("failedAssets", Failed.Count);
                w.EndObject();
                w.EndObject();

                w.Key("models"); w.BeginArray();
                foreach (ModelInfo m in Models) m.Write(w);
                w.EndArray();

                w.Key("standaloneClips"); w.BeginArray();
                foreach (ClipInfo c in StandaloneClips) c.Write(w);
                w.EndArray();

                w.Key("failed"); w.BeginArray();
                foreach (FailedAsset f in Failed)
                {
                    w.BeginObject(); w.Field("path", f.Path); w.Field("error", f.Error); w.EndObject();
                }
                w.EndArray();
                w.EndObject();
                return w.ToString();
            }
        }

        class ModelInfo
        {
            public string Path, Pack, Guid, AvatarSetup;
            public ModelImporterAnimationType AnimationType;
            public int SkinnedMeshCount;
            public bool HasAvatar, AvatarIsValid, AvatarIsHuman;
            public readonly List<ClipInfo> Clips = new List<ClipInfo>();

            public void Write(JsonWriter w)
            {
                w.BeginObject();
                w.Field("pack", Pack);
                w.Field("path", Path);
                w.Field("guid", Guid);
                w.Field("animationType", AnimationType.ToString());
                w.Field("avatarSetup", AvatarSetup);
                w.Field("hasSkinnedMesh", SkinnedMeshCount > 0);
                w.Field("skinnedMeshCount", SkinnedMeshCount);
                w.Key("avatar"); w.BeginObject();
                w.Field("present", HasAvatar);
                w.Field("isValid", AvatarIsValid);
                w.Field("isHuman", AvatarIsHuman);
                w.EndObject();
                w.Key("clips"); w.BeginArray();
                foreach (ClipInfo c in Clips) c.Write(w);
                w.EndArray();
                w.EndObject();
            }
        }

        class ClipInfo
        {
            public string Name, AssetPath, AssetGuid;
            public long LocalFileId;
            public float Length, FrameRate;
            public bool IsLooping, IsHumanMotion, HasGenericRootTransform, HasMotionCurves, HasRootCurves;
            public int EventCount;
            public ReachEstimate Reach;
            public string ReachUnavailableReason;

            public void Write(JsonWriter w)
            {
                w.BeginObject();
                w.Field("name", Name);
                w.Field("assetPath", AssetPath);
                w.Field("assetGuid", AssetGuid);
                if (LocalFileId != 0) w.Field("localFileId", LocalFileId);
                w.Field("length", Length);
                w.Field("frameRate", FrameRate);
                w.Field("frameCount", Mathf.RoundToInt(Length * FrameRate));
                w.Field("isLooping", IsLooping);
                w.Field("isHumanMotion", IsHumanMotion);
                w.Field("hasGenericRootTransform", HasGenericRootTransform);
                w.Field("hasMotionCurves", HasMotionCurves);
                w.Field("hasRootCurves", HasRootCurves);
                w.Field("eventCount", EventCount);
                if (Reach != null)
                {
                    w.Key("peakReachHeuristic"); w.BeginObject();
                    w.Field("sampleRate", Reach.SampleRate);
                    w.Field("sampleCount", Reach.SampleCount);
                    w.Key("limbs"); w.BeginArray();
                    foreach (LimbReach l in Reach.Limbs)
                    {
                        w.BeginObject();
                        w.Field("limb", l.Limb);
                        w.Field("restDistance", l.RestDistance);
                        w.Field("peakDistance", l.PeakDistance);
                        w.Field("peakTime", l.PeakTime);
                        w.Field("peakNormalizedTime", l.PeakNormalizedTime);
                        w.EndObject();
                    }
                    w.EndArray();
                    w.EndObject();
                }
                else if (!string.IsNullOrEmpty(ReachUnavailableReason))
                {
                    w.Field("peakReachUnavailable", ReachUnavailableReason);
                }
                w.EndObject();
            }
        }

        class ReachEstimate
        {
            public float SampleRate;
            public int SampleCount;
            public readonly List<LimbReach> Limbs = new List<LimbReach>();
        }

        class LimbReach
        {
            public string Limb;
            public float RestDistance, PeakDistance, PeakTime, PeakNormalizedTime;
        }

        class FailedAsset
        {
            public string Path, Error;
        }

        // A tiny pretty-printing JSON writer: objects, arrays, strings, numbers, booleans. Enough for the catalogue.
        class JsonWriter
        {
            readonly StringBuilder sb = new StringBuilder();
            // One entry per open object/array: has it had a first element yet (so the next one needs a comma)?
            readonly List<bool> hasItems = new List<bool>();
            bool afterKey;

            public void BeginObject() { BeginValue(); sb.Append('{'); hasItems.Add(false); }
            public void EndObject() { Close('}'); }
            public void BeginArray() { BeginValue(); sb.Append('['); hasItems.Add(false); }
            public void EndArray() { Close(']'); }

            public void Key(string key)
            {
                BeginValue();
                AppendString(key);
                sb.Append(": ");
                afterKey = true;
            }

            public void Field(string key, string value) { Key(key); BeginValue(); AppendString(value ?? ""); }
            public void Field(string key, bool value) { Key(key); BeginValue(); sb.Append(value ? "true" : "false"); }
            public void Field(string key, int value) { Key(key); BeginValue(); sb.Append(value.ToString(CultureInfo.InvariantCulture)); }
            public void Field(string key, long value) { Key(key); BeginValue(); sb.Append(value.ToString(CultureInfo.InvariantCulture)); }

            public void Field(string key, float value)
            {
                Key(key); BeginValue();
                if (float.IsNaN(value) || float.IsInfinity(value)) sb.Append("null");
                else sb.Append(Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture));
            }

            // Writes the comma/newline/indent before a value, unless the value directly follows its key.
            void BeginValue()
            {
                if (afterKey) { afterKey = false; return; }
                int depth = hasItems.Count;
                if (depth == 0) return;
                if (hasItems[depth - 1]) sb.Append(',');
                hasItems[depth - 1] = true;
                NewLine(depth);
            }

            void Close(char bracket)
            {
                int depth = hasItems.Count;
                bool hadItems = hasItems[depth - 1];
                hasItems.RemoveAt(depth - 1);
                if (hadItems) NewLine(depth - 1);
                sb.Append(bracket);
            }

            void NewLine(int depth)
            {
                sb.Append('\n');
                sb.Append(' ', depth * 2);
            }

            void AppendString(string s)
            {
                sb.Append('"');
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else sb.Append(c);
                            break;
                    }
                }
                sb.Append('"');
            }

            public override string ToString() { return sb.ToString() + "\n"; }
        }
    }
}
