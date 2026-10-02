using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge.EditorTools
{
    // The sandbox's tuning assets, all in one place for the builder.
    public sealed class SandboxTuningSet
    {
        public PlayerTuningAsset PlayerFluid;
        public PlayerTuningAsset PlayerPunishing;
        public MoveSetAsset MovesFluid;          // Fire
        public MoveSetAsset MovesPunishing;      // Fire
        public MoveSetAsset WaterFluid, WaterPunishing;
        public MoveSetAsset EarthFluid, EarthPunishing;
        public MoveSetAsset AirFluid, AirPunishing;
        public ElementLoadoutAsset LoadoutFluid;
        public ElementLoadoutAsset LoadoutPunishing;
        public CameraTuningAsset Camera;
        public EnemyTuningAsset DaoSoldier;
        public EnemyTuningAsset Crossbowman;
        public EnemyTuningAsset SparringDummy;
        public EnemyTuningAsset TutorialPartner;
        public EncounterTuningAsset Encounter;
        public TutorialScriptAsset Tutorial;
    }

    // Creates, loads and resets the sandbox's tuning assets in Assets/_Project/Tuning. These ScriptableObject
    // assets hold every gameplay number (health, damage, frame data, stamina costs...), so balancing is
    // editing values in the Inspector rather than code. Code only seeds the first copy: an asset that already
    // exists is never overwritten by a build, so numbers David and Jeremy tuned always survive a rebuild.
    // The two deliberate exceptions: "Reset Sandbox Tuning To Defaults" (everything), and the build's "Update
    // (recommended)" for assets an older version of the game saved (only those: see FindStaleAssets).
    //
    // Build 05 adds a move set per element and preset (WaterMoves_Fluid ...), the two loadouts that group them
    // (Loadout_Fluid / _Punishing, which hold references to the move set assets, never copies), the tutorial's sparring
    // partner and the tutorial itself. Missing ones are created silently, so an older sandbox just gains them.
    public static class SandboxTuningAssets
    {
        public const string Folder = "Assets/_Project/Tuning";

        public const string PlayerFluidName = "Player_Fluid";
        public const string PlayerPunishingName = "Player_Punishing";
        public const string MovesFluidName = "FireMoves_Fluid";
        public const string MovesPunishingName = "FireMoves_Punishing";
        public const string WaterFluidName = "WaterMoves_Fluid";
        public const string WaterPunishingName = "WaterMoves_Punishing";
        public const string EarthFluidName = "EarthMoves_Fluid";
        public const string EarthPunishingName = "EarthMoves_Punishing";
        public const string AirFluidName = "AirMoves_Fluid";
        public const string AirPunishingName = "AirMoves_Punishing";
        public const string LoadoutFluidName = "Loadout_Fluid";
        public const string LoadoutPunishingName = "Loadout_Punishing";
        public const string CameraName = "Camera";
        public const string DaoSoldierName = "Enemy_DaoSoldier";
        public const string CrossbowmanName = "Enemy_Crossbowman";
        public const string SparringDummyName = "Enemy_SparringDummy";
        public const string TutorialPartnerName = "Enemy_TutorialPartner";
        public const string EncounterName = "Encounter";
        public const string TutorialName = "Tutorial";

        const string ResetUndoName = "Reset Sandbox Tuning To Defaults";
        // The text YAML line every Build 05 player and move set asset has (see SavedBefore).
        const string DataVersionField = "DataVersion:";

        // Every asset name, for dialogs and logs. Move sets come before the loadouts that point at them.
        public static readonly string[] AllNames =
        {
            PlayerFluidName, PlayerPunishingName, MovesFluidName, MovesPunishingName, WaterFluidName, WaterPunishingName,
            EarthFluidName, EarthPunishingName, AirFluidName, AirPunishingName, LoadoutFluidName, LoadoutPunishingName,
            CameraName, DaoSoldierName, CrossbowmanName, SparringDummyName, TutorialPartnerName, EncounterName, TutorialName,
        };

        // Loads every sandbox tuning asset, creating only the missing ones (saved straight away). Loadout slots left
        // empty are pointed back at their element's default move set asset (a reference, never a copy of values).
        // createdPaths (optional) receives the path of each asset that was created.
        public static SandboxTuningSet LoadOrCreateAll(List<string> createdPaths)
        {
            SandboxTuningSet set = LoadAll(null, createdPaths, null);
            AssetDatabase.SaveAssets();
            return set;
        }

        // Overwrites the values of every sandbox tuning asset with the starting numbers from code, creating any
        // that are missing. Undoable with Edit > Undo. resetPaths / createdPaths (optional) receive what changed.
        public static SandboxTuningSet ResetAllToDefaults(List<string> resetPaths, List<string> createdPaths)
        {
            return ResetAssetsToDefaults(AllNames, resetPaths, createdPaths);
        }

        // The same for just these assets (by name, e.g. the stale ones FindStaleAssets listed); the rest are loaded as
        // they are (missing ones created). Undoable with Edit > Undo.
        public static SandboxTuningSet ResetAssetsToDefaults(IReadOnlyList<string> names, List<string> resetPaths, List<string> createdPaths)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(ResetUndoName);
            int group = Undo.GetCurrentGroup();
            var toReset = new HashSet<string>(names ?? new string[0]);
            SandboxTuningSet set = LoadAll(toReset, createdPaths, resetPaths);
            Undo.CollapseUndoOperations(group);
            AssetDatabase.SaveAssets();
            return set;
        }

        // Saved tuning assets that an older version of the game made: playing them would quietly bring the old moves and
        // numbers back (they're never overwritten by a build), so the builder offers to reset exactly these. Fills names
        // (asset names) and reasons (one plain line each); returns true when there are any. Missing assets aren't stale:
        // they're created fresh. An asset tuned by hand but saved by this version is never listed.
        //   * a player or move set asset saved before Build 05 or before its verify fixes (DataVersion behind, or no
        //     DataVersion line in the file);
        //   * a move set whose element doesn't match its file name (WaterMoves_* holding Fire moves, say);
        //   * the Fire move sets from before the 5-hit string and the animated fighters;
        //   * the player with the old 4 m auto-aim, the Dao Soldier with the old 3-hit-chain break-out (reports 03 / 04);
        //   * the Dao Soldier or tutorial partner saved before EnemyTuning.DataVersion existed (round 7: the red thrust);
        //   * a tutorial saved by an older version of its steps.
        // The DataVersion Build 05 first shipped with: older is "before Build 05", newer but behind current is "before the fixes".
        const int Build05PlayerVersion = 5;
        const int Build05MoveSetVersion = 5;

        public static bool FindStaleAssets(List<string> names, List<string> reasons)
        {
            names.Clear();
            reasons.Clear();
            int currentChain = ElementMoveSet.CreateFireFluid().LightChain.Length;

            foreach (string name in new[] { PlayerFluidName, PlayerPunishingName })
            {
                PlayerTuningAsset player = AssetDatabase.LoadAssetAtPath<PlayerTuningAsset>(PathFor(name));
                if (player == null) continue;
                if (player.Tuning == null || player.Tuning.DataVersion < Build05PlayerVersion || SavedBefore(name))
                    Add(names, reasons, name, "saved before Build 05 (rhythm, combo, MIX, element switching, danger sense)");
                else if (player.Tuning.DataVersion < PlayerTuning.CurrentDataVersion)
                    Add(names, reasons, name, "saved before the Build 05 fixes (shorter element switch cooldown, dashes that arrive as they strike, "
                                              + "a dodge from a run keeps its heading so holding B to sprint never swings you round a foe)");
                else if (name == PlayerFluidName && player.Tuning.SoftLockRange == 4f)
                    Add(names, reasons, name, "still has the old 4 m auto-aim range");
            }

            foreach (string name in MoveSetNames())
            {
                MoveSetAsset moves = AssetDatabase.LoadAssetAtPath<MoveSetAsset>(PathFor(name));
                if (moves == null) continue;
                ElementMoveSet set = moves.MoveSet;
                ElementId expected = ElementOfName(name);
                if (set == null || set.DataVersion < Build05MoveSetVersion || SavedBefore(name))
                {
                    Add(names, reasons, name, "saved before Build 05 (pause finisher, dodge strike, the element's own moves)");
                }
                else if (set.DataVersion < ElementMoveSet.CurrentDataVersion)
                {
                    Add(names, reasons, name, "saved before the Build 05 fixes (dodge strike reach, Water/Air dodge exits, side-slip circle, "
                                              + "slam finishers that drop you with the foe, Tiger Claw Rake's swipe)");
                }
                else if (set.Element != expected)
                {
                    Add(names, reasons, name, "holds " + set.Element + " moves, not " + expected);
                }
                else if (expected == ElementId.Fire
                         && (set.LightChain == null || set.LightChain.Length < currentChain || set.LightChain[0] == null
                             || string.IsNullOrEmpty(set.LightChain[0].AnimationKey) || set.Launcher == null
                             || string.IsNullOrEmpty(set.Launcher.AnimationKey)))
                {
                    Add(names, reasons, name, "from before the 5-hit string and the animated fighters");
                }
            }

            EnemyTuningAsset soldier = AssetDatabase.LoadAssetAtPath<EnemyTuningAsset>(PathFor(DaoSoldierName));
            if (soldier != null && soldier.Tuning != null && soldier.Tuning.BreakOut != null
                && soldier.Tuning.BreakOut.HitsToTrigger == 3 && soldier.Tuning.BreakOut.HitWindow == 1.2f)
            {
                // Both old values together: someone tuning just one of them back on purpose isn't nagged (report 04, X-01).
                Add(names, reasons, DaoSoldierName, "still has the old 3-hit-chain break-out");
            }

            // The soldiers saved before round 7 have no must-dodge attack (danger sense's red never showed) and glow red on
            // attacks that can be parried (J7-03). Updating also brings the new telegraph colours (EnemyFeedbackSettings).
            foreach (string name in new[] { DaoSoldierName, TutorialPartnerName })
            {
                EnemyTuningAsset enemy = AssetDatabase.LoadAssetAtPath<EnemyTuningAsset>(PathFor(name));
                if (enemy == null || enemy.Tuning == null) continue;
                if (enemy.Tuning.DataVersion < EnemyTuning.CurrentDataVersion)
                    Add(names, reasons, name, "saved before the Build 05 round 7 fixes (the Delayed Thrust can't be parried, so danger "
                                              + "sense shows red for it; parryable heavies glow amber, only must-dodge attacks glow red)");
            }

            TutorialScriptAsset tutorial = AssetDatabase.LoadAssetAtPath<TutorialScriptAsset>(PathFor(TutorialName));
            if (tutorial != null && (tutorial.Script == null || tutorial.Script.DataVersion < TutorialScript.CurrentDataVersion))
                Add(names, reasons, TutorialName, "saved by an older version of the tutorial's steps");
            return names.Count > 0;
        }

        // One line per stale asset ("Player_Fluid: saved before Build 05 ..."), for dialogs and logs.
        public static string Describe(List<string> names, List<string> reasons)
        {
            var lines = new string[names.Count];
            for (int i = 0; i < names.Count; i++) lines[i] = names[i] + ": " + reasons[i];
            return string.Join("\n", lines);
        }

        public static string PathFor(string assetName)
        {
            return Folder + "/" + assetName + ".asset";
        }

        static IEnumerable<string> MoveSetNames()
        {
            yield return MovesFluidName;
            yield return MovesPunishingName;
            yield return WaterFluidName;
            yield return WaterPunishingName;
            yield return EarthFluidName;
            yield return EarthPunishingName;
            yield return AirFluidName;
            yield return AirPunishingName;
        }

        // "WaterMoves_Fluid" -> Water. The file name says which element a move set asset is for.
        static ElementId ElementOfName(string assetName)
        {
            if (assetName.StartsWith("Water", StringComparison.Ordinal)) return ElementId.Water;
            if (assetName.StartsWith("Earth", StringComparison.Ordinal)) return ElementId.Earth;
            if (assetName.StartsWith("Air", StringComparison.Ordinal)) return ElementId.Air;
            return ElementId.Fire;
        }

        // True when the asset's file (text YAML) has no DataVersion line: it was saved before the field existed. This
        // backs up the DataVersion < current check without relying on how Unity fills a field missing from the file.
        static bool SavedBefore(string assetName)
        {
            string file = PathFor(assetName);
            if (!File.Exists(file)) return false;
            string text = File.ReadAllText(file);
            return text.StartsWith("%YAML", StringComparison.Ordinal) && text.IndexOf(DataVersionField, StringComparison.Ordinal) < 0;
        }

        static void Add(List<string> names, List<string> reasons, string name, string reason)
        {
            if (names.Contains(name)) return;
            names.Add(name);
            reasons.Add(reason);
        }

        // reset: the asset names to overwrite with their defaults (null = none).
        static SandboxTuningSet LoadAll(HashSet<string> reset, List<string> created, List<string> resetPaths)
        {
            GreyboxMaterials.EnsureFolder(Folder);
            var set = new SandboxTuningSet();
            PlayerTuning fluid = PlayerTuning.CreateFluid();
            PlayerTuning punishing = PlayerTuning.CreatePunishing();
            // The presets take their display names from the core seeds (PlayerTuning.PresetName), so the name
            // shown on the HUD matches the numbers inside.
            set.PlayerFluid = Get<PlayerTuningAsset>(PlayerFluidName, a => SeedPlayer(a, PlayerTuning.CreateFluid()), reset, created, resetPaths);
            set.PlayerPunishing = Get<PlayerTuningAsset>(PlayerPunishingName, a => SeedPlayer(a, PlayerTuning.CreatePunishing()), reset, created, resetPaths);

            set.MovesFluid = Moves(MovesFluidName, ElementMoveSet.CreateFireFluid, fluid.PresetName, reset, created, resetPaths);
            set.MovesPunishing = Moves(MovesPunishingName, ElementMoveSet.CreateFirePunishing, punishing.PresetName, reset, created, resetPaths);
            set.WaterFluid = Moves(WaterFluidName, ElementMoveSet.CreateWaterFluid, fluid.PresetName, reset, created, resetPaths);
            set.WaterPunishing = Moves(WaterPunishingName, ElementMoveSet.CreateWaterPunishing, punishing.PresetName, reset, created, resetPaths);
            set.EarthFluid = Moves(EarthFluidName, ElementMoveSet.CreateEarthFluid, fluid.PresetName, reset, created, resetPaths);
            set.EarthPunishing = Moves(EarthPunishingName, ElementMoveSet.CreateEarthPunishing, punishing.PresetName, reset, created, resetPaths);
            set.AirFluid = Moves(AirFluidName, ElementMoveSet.CreateAirFluid, fluid.PresetName, reset, created, resetPaths);
            set.AirPunishing = Moves(AirPunishingName, ElementMoveSet.CreateAirPunishing, punishing.PresetName, reset, created, resetPaths);

            set.LoadoutFluid = Get<ElementLoadoutAsset>(LoadoutFluidName,
                a => SeedLoadout(a, fluid.PresetName, set.MovesFluid, set.WaterFluid, set.EarthFluid, set.AirFluid), reset, created, resetPaths);
            set.LoadoutPunishing = Get<ElementLoadoutAsset>(LoadoutPunishingName,
                a => SeedLoadout(a, punishing.PresetName, set.MovesPunishing, set.WaterPunishing, set.EarthPunishing, set.AirPunishing),
                reset, created, resetPaths);
            RelinkEmptySlots(set.LoadoutFluid, set.MovesFluid, set.WaterFluid, set.EarthFluid, set.AirFluid);
            RelinkEmptySlots(set.LoadoutPunishing, set.MovesPunishing, set.WaterPunishing, set.EarthPunishing, set.AirPunishing);

            set.Camera = Get<CameraTuningAsset>(CameraName, SeedCamera, reset, created, resetPaths);
            set.DaoSoldier = Get<EnemyTuningAsset>(DaoSoldierName, a => a.Tuning = EnemyTuning.CreateDaoSoldier(), reset, created, resetPaths);
            set.Crossbowman = Get<EnemyTuningAsset>(CrossbowmanName, a => a.Tuning = EnemyTuning.CreateCrossbowman(), reset, created, resetPaths);
            set.SparringDummy = Get<EnemyTuningAsset>(SparringDummyName, a => a.Tuning = EnemyTuning.CreateSparringDummy(), reset, created, resetPaths);
            set.TutorialPartner = Get<EnemyTuningAsset>(TutorialPartnerName, a => a.Tuning = EnemyTuning.CreateTutorialPartner(), reset, created, resetPaths);
            // The encounter's defaults (e.g. at most 2 enemies attacking at once) are the class's own field values.
            set.Encounter = Get<EncounterTuningAsset>(EncounterName, a => { }, reset, created, resetPaths);
            set.Tutorial = Get<TutorialScriptAsset>(TutorialName, a => a.Script = TutorialScript.CreateDefault(), reset, created, resetPaths);
            return set;
        }

        static MoveSetAsset Moves(string assetName, Func<ElementMoveSet> factory, string presetName, HashSet<string> reset,
                                  List<string> created, List<string> resetPaths)
        {
            return Get<MoveSetAsset>(assetName, a => SeedMoves(a, factory(), presetName), reset, created, resetPaths);
        }

        static void SeedPlayer(PlayerTuningAsset asset, PlayerTuning tuning)
        {
            asset.Tuning = tuning;
            asset.PresetName = tuning.PresetName;
        }

        static void SeedMoves(MoveSetAsset asset, ElementMoveSet moves, string presetName)
        {
            asset.MoveSet = moves;
            asset.PresetName = presetName;
        }

        static void SeedLoadout(ElementLoadoutAsset asset, string presetName, MoveSetAsset fire, MoveSetAsset water,
                                MoveSetAsset earth, MoveSetAsset air)
        {
            asset.PresetName = presetName;
            asset.Fire = fire;
            asset.Water = water;
            asset.Earth = earth;
            asset.Air = air;
            asset.Starting = ElementId.Fire;
            asset.LearnedFire = asset.LearnedWater = asset.LearnedEarth = asset.LearnedAir = true;
        }

        // A loadout slot left empty (a deleted or never-set move set) points back at the element's default asset. Only
        // empty slots: one pointing at another asset on purpose is kept.
        static void RelinkEmptySlots(ElementLoadoutAsset loadout, MoveSetAsset fire, MoveSetAsset water, MoveSetAsset earth, MoveSetAsset air)
        {
            if (loadout == null) return;
            bool changed = false;
            if (loadout.Fire == null) { loadout.Fire = fire; changed = true; }
            if (loadout.Water == null) { loadout.Water = water; changed = true; }
            if (loadout.Earth == null) { loadout.Earth = earth; changed = true; }
            if (loadout.Air == null) { loadout.Air = air; changed = true; }
            if (changed) EditorUtility.SetDirty(loadout);
        }

        static void SeedCamera(CameraTuningAsset asset)
        {
            asset.Camera = new CameraTuning();
            asset.LockOn = new LockOnTuning();
        }

        static T Get<T>(string assetName, Action<T> seed, HashSet<string> reset, List<string> created, List<string> resetPaths)
            where T : ScriptableObject
        {
            string path = PathFor(assetName);
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                if (reset != null && reset.Contains(assetName))
                {
                    OverwriteWithSeed(existing, seed);
                    if (resetPaths != null) resetPaths.Add(path);
                }
                return existing;
            }

            // Something else is in the way (another kind of asset, or one whose script no longer loads). Never
            // overwrite a file we didn't recognise: stop and say what to do instead.
            if (File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                throw new InvalidOperationException(path + " exists but isn't a " + typeof(T).Name + ". Rename or move that file "
                                                    + "(the builder never overwrites files it doesn't recognise), then try again.");
            }

            T asset = ScriptableObject.CreateInstance<T>();
            seed(asset);
            AssetDatabase.CreateAsset(asset, path);
            if (created != null) created.Add(path);
            return asset;
        }

        // Copies a freshly seeded object's values over the existing asset, keeping the asset itself (and so every
        // scene reference to it) in place.
        static void OverwriteWithSeed<T>(T existing, Action<T> seed) where T : ScriptableObject
        {
            T fresh = ScriptableObject.CreateInstance<T>();
            try
            {
                seed(fresh);
                fresh.name = existing.name; // CopySerialized copies the name too; keep the asset's own
                Undo.RecordObject(existing, ResetUndoName);
                EditorUtility.CopySerialized(fresh, existing);
                EditorUtility.SetDirty(existing);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fresh);
            }
        }
    }
}
