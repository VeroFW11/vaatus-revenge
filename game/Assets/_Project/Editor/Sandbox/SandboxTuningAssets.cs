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
        public MoveSetAsset MovesFluid;
        public MoveSetAsset MovesPunishing;
        public CameraTuningAsset Camera;
        public EnemyTuningAsset DaoSoldier;
        public EnemyTuningAsset Crossbowman;
        public EnemyTuningAsset SparringDummy;
        public EncounterTuningAsset Encounter;
    }

    // Creates, loads and resets the sandbox's tuning assets in Assets/_Project/Tuning. These ScriptableObject
    // assets hold every gameplay number (health, damage, frame data, stamina costs...), so balancing is
    // editing values in the Inspector rather than code. Code only seeds the first copy: an asset that already
    // exists is never overwritten by a build, so numbers David and Jeremy tuned always survive a rebuild.
    // "Reset Sandbox Tuning To Defaults" is the one deliberate exception.
    public static class SandboxTuningAssets
    {
        public const string Folder = "Assets/_Project/Tuning";

        public const string PlayerFluidName = "Player_Fluid";
        public const string PlayerPunishingName = "Player_Punishing";
        public const string MovesFluidName = "FireMoves_Fluid";
        public const string MovesPunishingName = "FireMoves_Punishing";
        public const string CameraName = "Camera";
        public const string DaoSoldierName = "Enemy_DaoSoldier";
        public const string CrossbowmanName = "Enemy_Crossbowman";
        public const string SparringDummyName = "Enemy_SparringDummy";
        public const string EncounterName = "Encounter";

        const string ResetUndoName = "Reset Sandbox Tuning To Defaults";

        // Every asset name, for dialogs and logs.
        public static readonly string[] AllNames =
        {
            PlayerFluidName, PlayerPunishingName, MovesFluidName, MovesPunishingName, CameraName,
            DaoSoldierName, CrossbowmanName, SparringDummyName, EncounterName,
        };

        // Loads every sandbox tuning asset, creating only the missing ones (saved straight away). createdPaths
        // (optional) receives the path of each asset that was created.
        public static SandboxTuningSet LoadOrCreateAll(List<string> createdPaths)
        {
            SandboxTuningSet set = LoadAll(false, createdPaths, null);
            AssetDatabase.SaveAssets();
            return set;
        }

        // Overwrites the values of every sandbox tuning asset with the starting numbers from code, creating any
        // that are missing. Undoable with Edit > Undo. resetPaths / createdPaths (optional) receive what changed.
        public static SandboxTuningSet ResetAllToDefaults(List<string> resetPaths, List<string> createdPaths)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(ResetUndoName);
            int group = Undo.GetCurrentGroup();
            SandboxTuningSet set = LoadAll(true, createdPaths, resetPaths);
            Undo.CollapseUndoOperations(group);
            AssetDatabase.SaveAssets();
            return set;
        }

        // True when saved tuning assets were made by an older version of the game: the Fire move sets have no animation
        // keys (made before the animated fighters) or a shorter string than the current 5 hits, or the player still has
        // the old 4 m soft lock exactly. Such assets would quietly bring back the old game, so the builder offers to
        // reset them (report 03, V-06). Missing assets aren't stale: they're created fresh.
        public static bool HasStaleAssets(out string reason)
        {
            reason = "";
            int currentChain = ElementMoveSet.CreateFireFluid().LightChain.Length;
            foreach (string name in new[] { MovesFluidName, MovesPunishingName })
            {
                MoveSetAsset moves = AssetDatabase.LoadAssetAtPath<MoveSetAsset>(PathFor(name));
                if (moves == null || moves.MoveSet == null) continue;
                MoveData[] chain = moves.MoveSet.LightChain;
                if (chain == null || chain.Length < currentChain || chain[0] == null || string.IsNullOrEmpty(chain[0].AnimationKey)
                    || moves.MoveSet.Launcher == null || string.IsNullOrEmpty(moves.MoveSet.Launcher.AnimationKey))
                {
                    reason = name + " is from before the 5-hit string and the animated fighters";
                    return true;
                }
            }
            EnemyTuningAsset soldier = AssetDatabase.LoadAssetAtPath<EnemyTuningAsset>(PathFor(DaoSoldierName));
            if (soldier != null && soldier.Tuning != null && soldier.Tuning.BreakOut != null
                && (soldier.Tuning.BreakOut.HitsToTrigger == 3 || soldier.Tuning.MaxPoise == 45f))
            {
                reason = DaoSoldierName + " still has the old 3-hit-chain break-out and poise";
                return true;
            }
            PlayerTuningAsset player = AssetDatabase.LoadAssetAtPath<PlayerTuningAsset>(PathFor(PlayerFluidName));
            if (player != null && player.Tuning != null && player.Tuning.SoftLockRange == 4f)
            {
                reason = PlayerFluidName + " still has the old 4 m auto-aim range";
                return true;
            }
            return false;
        }

        public static string PathFor(string assetName)
        {
            return Folder + "/" + assetName + ".asset";
        }

        static SandboxTuningSet LoadAll(bool resetValues, List<string> created, List<string> reset)
        {
            GreyboxMaterials.EnsureFolder(Folder);
            var set = new SandboxTuningSet();
            // The presets take their display names from the core seeds (PlayerTuning.PresetName), so the name
            // shown on the HUD matches the numbers inside.
            set.PlayerFluid = Get<PlayerTuningAsset>(PlayerFluidName, a => SeedPlayer(a, PlayerTuning.CreateFluid()), resetValues, created, reset);
            set.PlayerPunishing = Get<PlayerTuningAsset>(PlayerPunishingName, a => SeedPlayer(a, PlayerTuning.CreatePunishing()), resetValues, created, reset);
            set.MovesFluid = Get<MoveSetAsset>(MovesFluidName,
                a => SeedMoves(a, ElementMoveSet.CreateFireFluid(), PlayerTuning.CreateFluid().PresetName), resetValues, created, reset);
            set.MovesPunishing = Get<MoveSetAsset>(MovesPunishingName,
                a => SeedMoves(a, ElementMoveSet.CreateFirePunishing(), PlayerTuning.CreatePunishing().PresetName), resetValues, created, reset);
            set.Camera = Get<CameraTuningAsset>(CameraName, SeedCamera, resetValues, created, reset);
            set.DaoSoldier = Get<EnemyTuningAsset>(DaoSoldierName, a => a.Tuning = EnemyTuning.CreateDaoSoldier(), resetValues, created, reset);
            set.Crossbowman = Get<EnemyTuningAsset>(CrossbowmanName, a => a.Tuning = EnemyTuning.CreateCrossbowman(), resetValues, created, reset);
            set.SparringDummy = Get<EnemyTuningAsset>(SparringDummyName, a => a.Tuning = EnemyTuning.CreateSparringDummy(), resetValues, created, reset);
            // The encounter's defaults (e.g. at most 2 enemies attacking at once) are the class's own field values.
            set.Encounter = Get<EncounterTuningAsset>(EncounterName, a => { }, resetValues, created, reset);
            return set;
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

        static void SeedCamera(CameraTuningAsset asset)
        {
            asset.Camera = new CameraTuning();
            asset.LockOn = new LockOnTuning();
        }

        static T Get<T>(string assetName, Action<T> seed, bool resetValues, List<string> created, List<string> reset) where T : ScriptableObject
        {
            string path = PathFor(assetName);
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                if (resetValues)
                {
                    OverwriteWithSeed(existing, seed);
                    if (reset != null) reset.Add(path);
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
