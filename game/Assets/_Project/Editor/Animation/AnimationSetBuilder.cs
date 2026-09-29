using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge.EditorTools
{
    // Vaatu's Revenge > Animation > Build Animation Set From ThirdParty.
    //
    // Looks through the martial-arts packs in Assets/ThirdParty, guesses which clip is which of our moves from the
    // clip names (the table in AnimationSetBuilder.Rules.cs), estimates when each strike is fully extended, and
    // writes the result into two FighterAnimationSet assets: one for the player (Fire) and one for enemies. At play
    // time MecanimPoseSource plays those clips instead of the procedural animation, time-warped so each strike
    // still lands on our frame data. Keys with no matching clip stay procedural.
    //
    // Safe to run again after adding packs: entries someone edited by hand (or ticked Locked) are left alone.
    // Deleting an asset resets it. The assets only hold references to the pack clips, never animation data, so
    // they can be committed; on a machine without the packs the references are just empty.
    public static partial class AnimationSetBuilder
    {
        public const string Folder = "Assets/_Project/Animations";
        public const string FireSetPath = Folder + "/FireFighterAnimations.asset";
        public const string EnemySetPath = Folder + "/EnemyFighterAnimations.asset";

        const string MenuPath = "Vaatu's Revenge/Animation/Build Animation Set From ThirdParty";
        const string UndoName = "Build Animation Set";
        const float MinScore = 3f;

        // For the sandbox builder: the sets if they exist (null otherwise). Pass straight to
        // MecanimPoseSource.AttachIfAvailable, which does nothing when a set is null or has no clips on this machine.
        public static FighterAnimationSet LoadFireSet() { return AssetDatabase.LoadAssetAtPath<FighterAnimationSet>(FireSetPath); }
        public static FighterAnimationSet LoadEnemySet() { return AssetDatabase.LoadAssetAtPath<FighterAnimationSet>(EnemySetPath); }

        [MenuItem(MenuPath, false, 42)]
        public static void BuildFromMenu()
        {
            if (!AnimationCatalogue.EditorIsIdle("build the animation set")) return;
            if (!AnimationCatalogue.ThirdPartyFolderExists()) return;

            var skippedGeneric = new List<string>();
            List<Candidate> candidates;
            try
            {
                candidates = ScanClips(skippedGeneric);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (candidates.Count == 0)
            {
                Debug.LogWarning("Build Animation Set: no Humanoid clips found in " + AnimationCatalogue.ThirdPartyFolder
                    + (skippedGeneric.Count > 0 ? " (" + skippedGeneric.Count + " Generic clips skipped: run 'Set ThirdParty Rigs To Humanoid' first)" : "")
                    + ". Nothing written.");
                return;
            }

            Dictionary<string, Limb> limbs = MoveLimbs();
            var log = new StringBuilder();
            log.Append("Build Animation Set: ").Append(candidates.Count).Append(" Humanoid clips scanned");
            if (skippedGeneric.Count > 0)
                log.Append(", ").Append(skippedGeneric.Count).Append(" Generic clips skipped (run 'Set ThirdParty Rigs To Humanoid' to use them)");
            log.Append(".\n");

            BuildSet(FireSetPath, "Fire (player)", FireKeys, candidates, limbs, log);
            BuildSet(EnemySetPath, "Enemies", EnemyKeys, candidates, limbs, log);
            AssetDatabase.SaveAssets();

            log.Append("\nRebuild the sandbox (Vaatu's Revenge > Build Fire Combat Sandbox) so the fighters pick the sets up. "
                + "Impact times are a guess (peak reach of the striking hand/foot): check each strike in the Animation "
                + "preview and fix any by hand in the asset; hand edits are kept.");
            Debug.Log(log.ToString());
        }

        // ---------------------------------------------------------------------------------------------------------
        // Scanning
        // ---------------------------------------------------------------------------------------------------------

        sealed class Candidate
        {
            public AnimationClip Clip;
            public string Path, Pack, FileName;
            public string NameText, FileText;        // normalised: " front kick r "
            public bool Loops;
            AnimationCatalogue.ReachEstimate reach;
            bool reachRead;

            public AnimationCatalogue.ReachEstimate Reach
            {
                get
                {
                    if (!reachRead)
                    {
                        reachRead = true;
                        try { reach = AnimationCatalogue.EstimateReach(Clip, out string _); }
                        catch (Exception) { reach = null; }
                    }
                    return reach;
                }
            }

            public string Describe() { return (string.IsNullOrEmpty(Pack) ? "" : Pack + " / ") + FileName + " : " + Clip.name; }
        }

        static List<Candidate> ScanClips(List<string> skippedGeneric)
        {
            var result = new List<Candidate>();
            var seen = new HashSet<AnimationClip>();
            List<string> paths = AnimationCatalogue.FindAssetPaths("t:AnimationClip");
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                EditorUtility.DisplayProgressBar("Build Animation Set", "Reading " + Path.GetFileName(path), (float)i / Math.Max(1, paths.Count));
                ModelImporterClipAnimation[] settings = null;
                if (AssetImporter.GetAtPath(path) is ModelImporter importer)
                {
                    settings = importer.clipAnimations;
                    if (settings == null || settings.Length == 0) settings = importer.defaultClipAnimations;
                }

                UnityEngine.Object[] assets;
                try { assets = AssetDatabase.LoadAllAssetsAtPath(path); }
                catch (Exception e) { Debug.LogWarning("Build Animation Set: skipped " + path + ": " + e.Message); continue; }

                foreach (UnityEngine.Object asset in assets)
                {
                    if (!(asset is AnimationClip clip) || !seen.Add(clip)) continue;
                    if (clip.name.StartsWith(AnimationCatalogue.PreviewClipPrefix, StringComparison.Ordinal)) continue;
                    if (!clip.isHumanMotion)
                    {
                        skippedGeneric.Add(path + " : " + clip.name);
                        continue;
                    }

                    string fileName = Path.GetFileNameWithoutExtension(path);
                    var candidate = new Candidate
                    {
                        Clip = clip,
                        Path = path,
                        Pack = AnimationCatalogue.PackName(path),
                        FileName = fileName,
                        NameText = Normalise(clip.name),
                        FileText = Normalise(fileName),
                        Loops = clip.isLooping,
                    };
                    if (settings != null)
                        foreach (ModelImporterClipAnimation s in settings)
                            if (s.name == clip.name) { candidate.Loops |= s.loopTime; break; }

                    if (ContainsAny(candidate.NameText, IgnoredWords)) continue;
                    result.Add(candidate);
                }
            }
            return result;
        }

        // "FrontKick_R01" -> " front kick r 01 ": words split on case changes, digits and punctuation.
        static string Normalise(string raw)
        {
            var sb = new StringBuilder(" ");
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                char prev = i > 0 ? raw[i - 1] : ' ';
                char next = i + 1 < raw.Length ? raw[i + 1] : ' ';
                if (!char.IsLetterOrDigit(c)) { sb.Append(' '); continue; }
                bool boundary = i > 0 && (
                    (char.IsUpper(c) && char.IsLower(prev)) ||                    // frontKick
                    (char.IsUpper(c) && char.IsUpper(prev) && char.IsLower(next)) || // TPose
                    (char.IsDigit(c) != char.IsDigit(prev) && char.IsLetterOrDigit(prev)));  // kick01
                if (boundary) sb.Append(' ');
                sb.Append(char.ToLowerInvariant(c));
            }
            sb.Append(' ');
            string text = sb.ToString();
            while (text.Contains("  ")) text = text.Replace("  ", " ");
            return text;
        }

        // A phrase counts when it starts a word: " kick" matches "front kick" and "kicking", not "sidekick".
        static bool HasPhrase(string text, string phrase) { return text.IndexOf(" " + phrase, StringComparison.Ordinal) >= 0; }

        static bool ContainsAny(string text, string[] phrases)
        {
            foreach (string p in phrases) if (HasPhrase(text, p)) return true;
            return false;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Matching
        // ---------------------------------------------------------------------------------------------------------

        static float Score(KeyRule rule, Candidate c)
        {
            if (ContainsAny(c.NameText, rule.Never)) return 0f;
            int best = 0;
            foreach ((string phrase, int weight) in rule.Phrases) if (weight > best && HasPhrase(c.NameText, phrase)) best = weight;
            float score = best;
            if (best == 0)
            {
                // Clips inside an FBX are often called "Take 001": then the file name says what the move is.
                if (ContainsAny(c.FileText, rule.Never)) return 0f;
                foreach ((string phrase, int weight) in rule.Phrases) if (weight > best && HasPhrase(c.FileText, phrase)) best = weight;
                score = best * 0.6f;
            }
            if (score <= 0f) return 0f;
            if (rule.Loops) score += c.Loops ? 2f : 0f;
            else score += c.Loops ? 0f : 1f;
            if (HasPhrase(c.NameText, "combo")) score -= 4f;
            return score;
        }

        sealed class Match
        {
            public string Key;
            public Candidate Clip;
            public float Score;
            public string FallbackFrom;   // null = matched by name
        }

        static Dictionary<string, Match> MatchKeys(string[] keys, List<Candidate> candidates)
        {
            var wanted = new HashSet<string>(keys, StringComparer.Ordinal);
            var pairs = new List<(float score, int rule, int clip)>();
            for (int r = 0; r < Rules.Length; r++)
            {
                if (!wanted.Contains(Rules[r].Key)) continue;
                for (int c = 0; c < candidates.Count; c++)
                {
                    float s = Score(Rules[r], candidates[c]);
                    if (s >= MinScore) pairs.Add((s, r, c));
                }
            }
            // Best score first; ties go to the rule listed first, then the clip found first (paths are sorted).
            pairs.Sort((a, b) =>
            {
                int bySCore = b.score.CompareTo(a.score);
                if (bySCore != 0) return bySCore;
                int byRule = a.rule.CompareTo(b.rule);
                return byRule != 0 ? byRule : a.clip.CompareTo(b.clip);
            });

            var matches = new Dictionary<string, Match>(StringComparer.Ordinal);
            var usedClips = new HashSet<int>();
            foreach ((float score, int rule, int clip) in pairs)
            {
                string key = Rules[rule].Key;
                if (matches.ContainsKey(key) || usedClips.Contains(clip)) continue;
                matches[key] = new Match { Key = key, Clip = candidates[clip], Score = score };
                usedClips.Add(clip);
            }

            foreach ((string key, string from) in Fallbacks)
            {
                if (!wanted.Contains(key) || matches.ContainsKey(key)) continue;
                if (!matches.TryGetValue(from, out Match source)) continue;
                matches[key] = new Match { Key = key, Clip = source.Clip, Score = source.Score, FallbackFrom = from };
            }
            return matches;
        }

        // Which limb each of our moves strikes with, read from the Fire move set (the asset if it exists, else the
        // defaults in code), so a clip that kicks with the other leg can be mirrored and the right limb timed.
        static Dictionary<string, Limb> MoveLimbs()
        {
            var limbs = new Dictionary<string, Limb>(StringComparer.Ordinal) { { AnimationKeys.Shove, Limb.BothFists } };
            var asset = AssetDatabase.LoadAssetAtPath<MoveSetAsset>(SandboxTuningAssets.PathFor(SandboxTuningAssets.MovesFluidName));
            ElementMoveSet set = asset != null && asset.MoveSet != null ? asset.MoveSet : ElementMoveSet.CreateFireFluid();

            void Add(MoveData move)
            {
                if (move != null && !string.IsNullOrEmpty(move.AnimationKey) && !limbs.ContainsKey(move.AnimationKey))
                    limbs.Add(move.AnimationKey, move.Limb);
            }
            if (set.LightChain != null) foreach (MoveData m in set.LightChain) Add(m);
            if (set.AirChain != null) foreach (MoveData m in set.AirChain) Add(m);
            Add(set.Launcher);
            Add(set.AbilityNorth);
            Add(set.AbilityEast);
            Add(set.Heavy);
            Add(set.SprintAttack);
            Add(set.PlungeAttack);
            Add(set.Skill);
            Add(set.ZipStrike);
            return limbs;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Entry values
        // ---------------------------------------------------------------------------------------------------------

        static readonly string[] LoopingKeys =
        {
            AnimationKeys.Idle, AnimationKeys.Walk, AnimationKeys.Run, AnimationKeys.Sprint, AnimationKeys.Strafe,
            AnimationKeys.Fall, AnimationKeys.Block,
        };

        static FighterAnimationSet.Entry MakeEntry(Match match, Dictionary<string, Limb> limbs, out string note)
        {
            Candidate c = match.Clip;
            var entry = new FighterAnimationSet.Entry
            {
                AnimationKey = match.Key,
                Clip = c.Clip,
                StartNormalizedTime = 0f,
                EndNormalizedTime = 1f,
                ImpactNormalizedTime = 0.5f,
                BlendIn = Array.IndexOf(LoopingKeys, match.Key) >= 0 ? 0.15f
                    : (match.Key == AnimationKeys.Hurt || match.Key == AnimationKeys.Stagger ? 0.04f : 0.06f),
                Source = c.Describe() + (match.FallbackFrom != null ? " (fallback: same clip as " + match.FallbackFrom + ")" : ""),
            };
            note = match.FallbackFrom != null ? "fallback from " + match.FallbackFrom : "";

            if (limbs.TryGetValue(match.Key, out Limb limb))
            {
                AnimationCatalogue.ReachEstimate reach = c.Reach;
                if (reach != null && PickStrikingLimb(reach, limb, out AnimationCatalogue.LimbReach striking, out bool mirror))
                {
                    entry.ImpactNormalizedTime = Round(Mathf.Clamp01(striking.PeakNormalizedTime));
                    entry.Mirror = mirror;
                    note = Join(note, "impact from " + striking.Limb + (mirror ? ", mirrored to match our " + limb : ""));
                }
                else
                {
                    note = Join(note, "no reach curves: impact left at 0.5, set it by hand");
                }
            }

            if (match.Key == AnimationKeys.Walk || match.Key == AnimationKeys.Run || match.Key == AnimationKeys.Sprint
                || match.Key == AnimationKeys.Strafe)
            {
                // The ground speed the clip was made at, so playback can follow the fighter's speed. In-place
                // clips report ~0, so fall back to typical speeds.
                Vector3 v = c.Clip.averageSpeed;
                float speed = new Vector2(v.x, v.z).magnitude;
                if (speed < 0.3f)
                {
                    speed = match.Key == AnimationKeys.Run ? 4f : match.Key == AnimationKeys.Sprint ? 6.5f : 1.5f;
                    note = Join(note, "in-place clip, assumed " + speed.ToString("0.#", CultureInfo.InvariantCulture) + " m/s");
                }
                entry.LoopReferenceSpeed = Round(speed);
            }

            entry.BuilderStamp = Stamp(entry);
            return entry;
        }

        // The limb of the right kind (hands for fists, feet for kicks) that reaches furthest out of its rest pose is
        // the striking one; if it's on the other side from our move's limb, the clip is mirrored.
        static bool PickStrikingLimb(AnimationCatalogue.ReachEstimate reach, Limb limb, out AnimationCatalogue.LimbReach best, out bool mirror)
        {
            best = null;
            mirror = false;
            bool hands = limb == Limb.LeftFist || limb == Limb.RightFist || limb == Limb.BothFists || limb == Limb.Weapon;
            bool feet = limb == Limb.LeftFoot || limb == Limb.RightFoot;
            float bestExtension = float.MinValue;
            foreach (AnimationCatalogue.LimbReach l in reach.Limbs)
            {
                bool isHand = l.Limb.EndsWith("Hand", StringComparison.Ordinal);
                if ((hands && !isHand) || (feet && isHand)) continue;
                float extension = l.PeakDistance - l.RestDistance;
                if (extension > bestExtension) { bestExtension = extension; best = l; }
            }
            if (best == null) return false;

            bool clipLeft = best.Limb.StartsWith("Left", StringComparison.Ordinal);
            if (limb == Limb.LeftFist || limb == Limb.LeftFoot) mirror = !clipLeft;
            else if (limb == Limb.RightFist || limb == Limb.RightFoot) mirror = clipLeft;
            return true;
        }

        // What the builder wrote, as text. An entry whose values no longer match its stamp was edited by a person.
        static string Stamp(FighterAnimationSet.Entry e)
        {
            string clipId = "none";
            if (e.Clip != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(e.Clip, out string guid, out long localId))
                clipId = guid + ":" + localId.ToString(CultureInfo.InvariantCulture);
            return string.Join("|", clipId, F(e.ImpactNormalizedTime), F(e.StartNormalizedTime), F(e.EndNormalizedTime),
                e.Mirror ? "m" : "-", F(e.BlendIn), F(e.LoopReferenceSpeed));
        }

        static bool EditedByHand(FighterAnimationSet.Entry e)
        {
            if (e.Locked) return true;
            if (e.Clip == null && !string.IsNullOrEmpty(e.BuilderStamp)) return false; // pack missing here: ours, replaceable
            return e.BuilderStamp != Stamp(e);
        }

        static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
        static float Round(float v) { return (float)Math.Round(v, 3); }
        static string Join(string a, string b) { return string.IsNullOrEmpty(a) ? b : a + "; " + b; }

        // ---------------------------------------------------------------------------------------------------------
        // Writing the asset
        // ---------------------------------------------------------------------------------------------------------

        static void BuildSet(string path, string label, string[] keys, List<Candidate> candidates, Dictionary<string, Limb> limbs, StringBuilder log)
        {
            Dictionary<string, Match> matches = MatchKeys(keys, candidates);

            var set = AssetDatabase.LoadAssetAtPath<FighterAnimationSet>(path);
            bool created = set == null;
            if (created)
            {
                if (matches.Count == 0)
                {
                    log.Append("\n").Append(label).Append(": no clips matched; ").Append(path).Append(" not created.\n");
                    return;
                }
                if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project", "Animations");
                set = ScriptableObject.CreateInstance<FighterAnimationSet>();
                AssetDatabase.CreateAsset(set, path);
            }
            else
            {
                Undo.RecordObject(set, UndoName);
            }

            List<FighterAnimationSet.Entry> entries = set.Entries;
            var rows = new List<string[]>();
            var unmapped = new List<string>();
            foreach (string key in keys)
            {
                int existingIndex = entries.FindIndex(e => e != null && e.AnimationKey == key);
                FighterAnimationSet.Entry existing = existingIndex >= 0 ? entries[existingIndex] : null;

                if (existing != null && EditedByHand(existing))
                {
                    existing.Locked = true; // make the protection visible in the Inspector
                    rows.Add(Row(key, existing, "kept: edited by hand / locked", limbs.ContainsKey(key)));
                    continue;
                }

                if (!matches.TryGetValue(key, out Match match))
                {
                    if (existing != null) entries.RemoveAt(existingIndex); // our old guess no longer matches anything
                    unmapped.Add(key);
                    continue;
                }

                FighterAnimationSet.Entry entry = MakeEntry(match, limbs, out string note);
                if (existing != null) entries[existingIndex] = entry;
                else entries.Add(entry);
                rows.Add(Row(key, entry, note, limbs.ContainsKey(key)));
            }

            set.RebuildLookup();
            EditorUtility.SetDirty(set);

            log.Append("\n").Append(label).Append(created ? " (created " : " (updated ").Append(path).Append("): ")
                .Append(rows.Count).Append(" keys with clips, ").Append(unmapped.Count).Append(" procedural.\n");
            AppendTable(log, new[] { "key", "clip (pack / file : clip)", "impact", "mirror", "note" }, rows);
            if (unmapped.Count > 0) log.Append("  Procedural (no matching clip): ").Append(string.Join(", ", unmapped)).Append('\n');
        }

        static string[] Row(string key, FighterAnimationSet.Entry e, string note, bool strike)
        {
            string clip = e.Clip != null ? (string.IsNullOrEmpty(e.Source) ? e.Clip.name : e.Source) : "(missing)";
            return new[] { key, clip, strike ? F(e.ImpactNormalizedTime) : "-", e.Mirror ? "yes" : "", note ?? "" };
        }

        static void AppendTable(StringBuilder log, string[] header, List<string[]> rows)
        {
            var width = new int[header.Length];
            for (int i = 0; i < header.Length; i++) width[i] = header[i].Length;
            foreach (string[] r in rows) for (int i = 0; i < r.Length; i++) width[i] = Math.Max(width[i], r[i].Length);
            void Line(string[] cells)
            {
                log.Append("  ");
                for (int i = 0; i < cells.Length; i++) log.Append(cells[i].PadRight(width[i] + 2));
                log.Append('\n');
            }
            Line(header);
            foreach (string[] r in rows) Line(r);
        }
    }
}
