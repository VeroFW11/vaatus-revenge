using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VaatusRevenge.EditorTools
{
    // One click to a playable sandbox: Vaatu's Revenge > Build Combat Sandbox, then press Play. Or one click straight into
    // the tutorial: Vaatu's Revenge > Play Combat Tutorial.
    //
    // The build (a) creates any missing tuning assets in Assets/_Project/Tuning (never overwriting tuned ones; assets an
    // older version saved are listed and, with "Update (recommended)", reset), plus the element effects' library and
    // materials (SandboxVfxAssets), (b) makes a fresh scene with a warm low sun, bloom (a global Volume plus
    // post-processing on the camera, see SandboxPostProcessing), the grey-box arena, the player with all four elements
    // (Loadout_Fluid), the camera rig, a "Systems" object (input, lock-on, enemy encounter, sandbox director, HUD,
    // element effects, tutorial), three sparring dummies, two dao soldiers, two crossbowmen and the tutorial's sparring
    // partner (switched off until the tutorial starts), then (c) saves it as Assets/_Project/Scenes/FireCombatSandbox.unity
    // (the file keeps its old name, so nothing that points at it breaks), makes it the first scene in Build Settings and
    // selects the player.
    //
    // Building the scene from code instead of by hand means it can always be rebuilt identically after the code
    // changes, and nobody has to remember which component needs which reference. Tweaks you want to keep belong
    // in the tuning assets (they survive rebuilds); the scene itself is replaced by each rebuild.
    public static class FireSandboxBuilder
    {
        public const string ScenesFolder = "Assets/_Project/Scenes";
        public const string ScenePath = ScenesFolder + "/FireCombatSandbox.unity";

        const string MenuRoot = "Vaatu's Revenge/";
        const string DialogTitle = "Combat Sandbox";
        const string UndoName = "Build Combat Sandbox";

        // Scene lighting is level art, not gameplay tuning: a warm evening sun, low enough for long, readable
        // shadows (they help judge distance and height in a grey-box world) and behind-left of the player's
        // starting view, so fighters are lit from the side rather than flattened.
        static readonly Color SunColor = new Color(1f, 0.84f, 0.66f);
        const float SunIntensity = 1.25f;
        static readonly Vector3 SunAngles = new Vector3(32f, 55f, 0f);
        const float SunShadowStrength = 0.85f;

        // Where the Scene view looks after a build: from behind and above the player, towards the dummies.
        const float SceneViewPitch = 24f;
        const float SceneViewSize = 7f;
        const float SceneViewPivotHeight = 1f;

        [MenuItem(MenuRoot + "Build Combat Sandbox", false, 1)]
        public static void BuildFromMenu()
        {
            if (!EditorIsIdle("build the sandbox")) return;
            if (SceneExists())
            {
                bool rebuild = EditorUtility.DisplayDialog(DialogTitle,
                    "Rebuild the Combat Sandbox?\n\n" + ScenePath + " already exists. Rebuilding replaces it with a fresh "
                    + "copy, so anything you changed or added in that scene is lost.\n\nYour tuning assets in "
                    + SandboxTuningAssets.Folder + " are kept exactly as they are (the next question covers any an older "
                    + "version saved).",
                    "Rebuild", "Cancel");
                if (!rebuild) return;
            }
            // Offer to save whatever is open first: the build replaces the open scene.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            OfferToUpdateStaleTuning();
            BuildScene();
        }

        // Tuning assets saved by an older version would quietly bring the old game back (they're never overwritten by a
        // build), so name them and offer to reset exactly those. Assets you tuned with this version are never touched, and
        // missing ones are simply created by the build.
        static void OfferToUpdateStaleTuning()
        {
            var names = new List<string>();
            var reasons = new List<string>();
            if (!SandboxTuningAssets.FindStaleAssets(names, reasons)) return;
            bool update = EditorUtility.DisplayDialog(DialogTitle,
                "These tuning assets in " + SandboxTuningAssets.Folder + " were saved by an older version of the prototype:\n\n"
                + SandboxTuningAssets.Describe(names, reasons) + "\n\nUpdate them to the current defaults? Recommended: "
                + "otherwise you'll play the old moves and numbers. Only these are reset; values you tuned by hand in them are "
                + "replaced (Edit > Undo can bring them back). Every other asset stays exactly as it is.",
                "Update (recommended)", "Keep mine");
            if (!update) return;
            var resetPaths = new List<string>();
            var createdPaths = new List<string>();
            SandboxTuningAssets.ResetAssetsToDefaults(names, resetPaths, createdPaths);
            Debug.Log("Sandbox tuning updated to the current defaults: " + NamesOf(resetPaths) + ". Undo with Edit > Undo.");
        }

        [MenuItem(MenuRoot + "Open Combat Sandbox", false, 2)]
        public static void OpenFromMenu()
        {
            if (!EditorIsIdle("open the sandbox")) return;
            if (!SceneExists())
            {
                bool build = EditorUtility.DisplayDialog(DialogTitle,
                    "There's no Combat Sandbox yet (" + ScenePath + " doesn't exist).\n\nBuild it now?", "Build it", "Cancel");
                if (build && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) BuildScene();
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("Combat Sandbox opened. Press Play. F1 shows the combos and controls; View (F7) starts the tutorial.");
        }

        // Menu to step 1 in one click: builds the sandbox if there isn't one (or it's from before the tutorial), opens it
        // and enters Play with the tutorial set to start on the first frame (TutorialDirector reads and clears the flag).
        [MenuItem(MenuRoot + "Play Combat Tutorial", false, 3)]
        public static void PlayTutorialFromMenu()
        {
            if (!EditorIsIdle("start the tutorial")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            OfferToUpdateStaleTuning();
            if (!SceneExists())
            {
                if (!BuildScene()) return;
            }
            else
            {
                Scene active = SceneManager.GetActiveScene();
                if (active.path != ScenePath) active = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                if (!HasTutorial(active))
                {
                    bool rebuild = EditorUtility.DisplayDialog(DialogTitle,
                        "This Combat Sandbox was built before the tutorial existed. Rebuild it now? (Anything you changed by hand "
                        + "in the scene is lost; your tuning assets are kept.)", "Rebuild and play", "Cancel");
                    if (!rebuild || !BuildScene()) return;
                }
            }
            PlayerPrefs.SetInt(TutorialDirector.AutoStartPref, 1);
            PlayerPrefs.Save();
            EditorApplication.isPlaying = true;
        }

        // Links the effect pictures in Art/VFX/<Element>/ to the library again (also done on every build and, by the
        // importer, shortly after pictures are added). Handy after unzipping a batch of pictures into the project.
        [MenuItem(MenuRoot + "Refresh VFX Textures", false, 21)]
        public static void RefreshVfxFromMenu()
        {
            if (!EditorIsIdle("refresh the effect pictures")) return;
            AssetDatabase.Refresh();
            var created = new List<string>();
            ElementVfxLibraryAsset library = SandboxVfxAssets.LoadOrCreate(created);
            int found = SandboxVfxAssets.RelinkTextures(library);
            Debug.Log("Effect pictures linked: " + found + " slot(s) have a picture in " + SandboxVfxAssets.Folder
                      + ". Empty slots use the built-in stand-ins.", library);
            EditorGUIUtility.PingObject(library);
        }

        [MenuItem(MenuRoot + "Reset Sandbox Tuning To Defaults", false, 20)]
        public static void ResetTuningFromMenu()
        {
            if (!EditorIsIdle("reset the tuning")) return;
            bool reset = EditorUtility.DisplayDialog(DialogTitle,
                "Reset the sandbox tuning to the starting numbers?\n\nThis overwrites every value in these assets in "
                + SandboxTuningAssets.Folder + " with the defaults from code:\n\n" + string.Join(", ", SandboxTuningAssets.AllNames)
                + "\n\nHandy after experiments. You can undo it with Edit > Undo.",
                "Reset", "Cancel");
            if (!reset) return;

            var resetPaths = new List<string>();
            var createdPaths = new List<string>();
            try
            {
                SandboxTuningAssets.ResetAllToDefaults(resetPaths, createdPaths);
                string message = "Sandbox tuning reset to the starting numbers (" + resetPaths.Count + " assets).";
                if (createdPaths.Count > 0) message += " Created the missing ones: " + NamesOf(createdPaths) + ".";
                Debug.Log(message + " Undo with Edit > Undo.");
            }
            catch (Exception e)
            {
                Debug.LogError("Resetting the sandbox tuning failed: " + e.Message + "\n" + resetPaths.Count
                               + " asset(s) were reset before the problem (Edit > Undo reverts them). Details follow.");
                Debug.LogException(e);
            }
        }

        // For scripts and tools that drive the Editor (e.g. the Pipeline `unity` CLI): builds without any dialogs,
        // replacing an existing sandbox scene. Refuses while an open scene has unsaved changes, so it can never
        // throw someone's work away. Returns true when the scene was built and saved.
        public static bool BuildWithoutPrompts()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Debug.LogError("Combat Sandbox: wait until Play mode has stopped and Unity has finished compiling and importing, then build.");
                return false;
            }
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (!SceneManager.GetSceneAt(i).isDirty) continue;
                Debug.LogError("Combat Sandbox: an open scene has unsaved changes. Save or discard them, then build again.");
                return false;
            }
            var staleNames = new List<string>();
            var staleReasons = new List<string>();
            if (SandboxTuningAssets.FindStaleAssets(staleNames, staleReasons))
            {
                Debug.LogWarning("Combat Sandbox: these tuning assets are from an older version (kept as they are):\n"
                                 + SandboxTuningAssets.Describe(staleNames, staleReasons) + "\nBuild from the menu (Vaatu's Revenge > "
                                 + "Build Combat Sandbox) and pick Update, or reset them all with Reset Sandbox Tuning To Defaults.");
            }
            return BuildScene();
        }

        static bool BuildScene()
        {
            string step = "starting";
            bool sceneCreated = false;
            bool sceneSaved = false;
            var createdAssets = new List<string>();
            try
            {
                step = "creating the tuning assets";
                Progress(step, 0.05f);
                SandboxTuningSet tuning = SandboxTuningAssets.LoadOrCreateAll(createdAssets);

                step = "linking the effect pictures";
                Progress(step, 0.1f);
                ElementVfxLibraryAsset vfxLibrary = SandboxVfxAssets.LoadOrCreate(createdAssets);

                step = "creating a new scene";
                Progress(step, 0.15f);
                // DefaultGameObjects keeps Unity's default sky and ambient light and gives us a camera and a sun.
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                sceneCreated = true;
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(UndoName);
                int undoGroup = Undo.GetCurrentGroup();
                Camera camera;
                Light sun;
                GetDefaultCameraAndSun(scene, out camera, out sun);
                SetUpSun(sun);

                step = "building the arena";
                Progress(step, 0.25f);
                ArenaLayout layout = ArenaBuilder.Build(null);
                Transform playerSpawn = RequireSpawn(layout, ArenaLayout.PlayerSpawn);
                Transform tutorialStart = RequireSpawn(layout, ArenaLayout.TutorialStart);

                step = "spawning the player";
                Progress(step, 0.45f);
                // All four elements (hold RB + B / X / A / Y, or 1-4); F5 / F6 swap the whole loadout.
                PlayerController player = PlayerController.Spawn(null, playerSpawn.position, layout.GetSpawnYaw(ArenaLayout.PlayerSpawn),
                    tuning.PlayerFluid, tuning.LoadoutFluid);
                if (player == null) throw new InvalidOperationException("PlayerController.Spawn returned nothing.");
                // Martial-arts pack clips (if imported and mapped with Build Animation Set From ThirdParty) replace the
                // procedural moves they match; without them this does nothing.
                MecanimPoseSource.AttachIfAvailable(player.gameObject, AnimationSetBuilder.LoadFireSet());
                // The player's skinned model (Art/Characters, see its README) wears the procedural body's pose when it's in
                // the project and chosen with Use Player Avatar Model; without it this does nothing and the grey body shows.
                PlayerAvatarSetup.AttachIfAvailable(player.gameObject);
                Undo.RegisterCreatedObjectUndo(player.gameObject, UndoName);
                Combatant playerFighter = player.GetComponent<Combatant>();

                step = "setting up the camera";
                Progress(step, 0.55f);
                // The rig lives on the camera's own GameObject. No Cinemachine Brain: it would fight the rig.
                ThirdPersonCameraRig rig = camera.GetComponent<ThirdPersonCameraRig>();
                if (rig == null) rig = camera.gameObject.AddComponent<ThirdPersonCameraRig>();
                rig.Configure(tuning.Camera, player.transform);

                step = "turning on bloom";
                // Never fails the build: if URP's types can't be found it logs one warning and the scene has no bloom.
                SandboxPostProcessing.SetUp(camera, UndoName);

                step = "creating the Systems object";
                Progress(step, 0.6f);
                SandboxDirector director = BuildSystems(tuning, vfxLibrary, playerFighter, player, playerSpawn);

                step = "spawning the sparring dummies";
                Progress(step, 0.7f);
                Transform dummies = CreateGroup("Dummies");
                // The middle dummy (straight ahead of the player) swings on a fixed rhythm, to practise dodge and deflect timing.
                SpawnDummy(layout, ArenaLayout.Dummy1, dummies, tuning.SparringDummy, true, director);
                SpawnDummy(layout, ArenaLayout.Dummy2, dummies, tuning.SparringDummy, false, director);
                SpawnDummy(layout, ArenaLayout.Dummy3, dummies, tuning.SparringDummy, false, director);

                step = "spawning the enemies";
                Progress(step, 0.8f);
                Transform enemies = CreateGroup("Enemies");
                SpawnEnemy(layout, ArenaLayout.Soldier1, enemies, tuning.DaoSoldier, director);
                SpawnEnemy(layout, ArenaLayout.Soldier2, enemies, tuning.DaoSoldier, director);
                SpawnEnemy(layout, ArenaLayout.CrossbowGround, enemies, tuning.Crossbowman, director);
                SpawnEnemy(layout, ArenaLayout.CrossbowPlatform, enemies, tuning.Crossbowman, director);

                step = "setting up the tutorial";
                Progress(step, 0.85f);
                EnemyController partner = SpawnTutorialPartner(layout, tuning.TutorialPartner);
                director.GetComponent<TutorialDirector>().Configure(tuning.Tutorial, partner, tutorialStart);

                step = "saving the scene";
                Progress(step, 0.9f);
                GreyboxMaterials.EnsureFolder(ScenesFolder);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                {
                    throw new IOException("Unity couldn't save " + ScenePath + ". Is the file read-only, or locked by another program?");
                }
                sceneSaved = true;
                // Also write any assets the arena and fighter builders created or changed (e.g. materials).
                AssetDatabase.SaveAssets();
                Undo.CollapseUndoOperations(undoGroup);

                step = "adding the scene to Build Settings";
                AddToBuildSettingsFirst(ScenePath);

                step = "selecting the player";
                Selection.activeGameObject = player.gameObject;
                EditorGUIUtility.PingObject(player.gameObject);
                FrameSceneView(player.transform);

                string message = "Combat Sandbox built. Press Play. F1 shows the combos and controls; View (F7) starts the "
                                 + "tutorial.\nSaved to " + ScenePath + " (first scene in Build Settings).";
                message += createdAssets.Count > 0
                    ? "\nCreated tuning assets: " + NamesOf(createdAssets) + "."
                    : "\nKept your existing tuning assets in " + SandboxTuningAssets.Folder + ".";
                Debug.Log(message, player.gameObject);
                return true;
            }
            catch (Exception e)
            {
                string outcome;
                if (sceneSaved) outcome = "The scene itself was built and saved to " + ScenePath + "; only this last step failed.";
                else if (sceneCreated) outcome = "Nothing was saved to " + ScenePath + ". The half-built scene is still open but unsaved, "
                                                 + "so you can look at what went wrong; don't save it.";
                else outcome = "Your open scene wasn't touched.";
                Debug.LogError("Building the Combat Sandbox failed while " + step + ": " + e.Message + "\n" + outcome
                               + " Fix the problem, then run Vaatu's Revenge > Build Combat Sandbox again. Full details follow.");
                Debug.LogException(e);
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // Input, lock-on, the enemy encounter (shared attack tokens), the sandbox director, the HUD, the element effects'
        // library and the tutorial all live on one "Systems" object, so there's a single place to find the sandbox's
        // settings in the Inspector. The tutorial is configured once its partner exists (BuildScene).
        static SandboxDirector BuildSystems(SandboxTuningSet tuning, ElementVfxLibraryAsset vfxLibrary, Combatant playerFighter,
                                            PlayerController player, Transform playerSpawn)
        {
            var systems = new GameObject("Systems");
            Undo.RegisterCreatedObjectUndo(systems, UndoName);
            systems.AddComponent<PlayerInputReader>();
            systems.AddComponent<LockOnController>().Configure(tuning.Camera, playerFighter);
            systems.AddComponent<EnemyEncounter>().Configure(tuning.Encounter);
            SandboxDirector director = systems.AddComponent<SandboxDirector>();
            // The single Fire move sets stay as the fallback for a preset whose loadout field is cleared by hand.
            director.Configure(tuning.PlayerFluid, tuning.MovesFluid, tuning.PlayerPunishing, tuning.MovesPunishing, playerSpawn);
            director.Configure(tuning.PlayerFluid, tuning.LoadoutFluid, tuning.PlayerPunishing, tuning.LoadoutPunishing, playerSpawn);
            director.Player = player;
            systems.AddComponent<CombatHud>();
            systems.AddComponent<ElementVfxBootstrap>().Library = vfxLibrary;
            systems.AddComponent<TutorialDirector>();
            return director;
        }

        // The tutorial's sparring partner: a dao soldier who can't die, saved switched off. The tutorial director wakes it
        // when the tutorial starts; the sandbox director never resets or hides it as one of its enemies.
        static EnemyController SpawnTutorialPartner(ArenaLayout layout, EnemyTuningAsset tuning)
        {
            Transform point = RequireSpawn(layout, ArenaLayout.TutorialPartner);
            Transform group = CreateGroup("Tutorial");
            EnemyController partner = EnemyController.Spawn(group, point.position, layout.GetSpawnYaw(ArenaLayout.TutorialPartner), tuning);
            if (partner == null) throw new InvalidOperationException("EnemyController.Spawn returned nothing for the tutorial partner.");
            MecanimPoseSource.AttachIfAvailable(partner.gameObject, AnimationSetBuilder.LoadEnemySet());
            partner.gameObject.name += " (" + ArenaLayout.TutorialPartner + ")";
            partner.gameObject.SetActive(false);
            return partner;
        }

        // Does this scene have the tutorial (built by this version)? Searched through the scene's own objects.
        static bool HasTutorial(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return false;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].GetComponentInChildren<TutorialDirector>(true) != null) return true;
            }
            return false;
        }

        static void SpawnDummy(ArenaLayout layout, string pointName, Transform parent, EnemyTuningAsset tuning, bool swings, SandboxDirector director)
        {
            Transform point = RequireSpawn(layout, pointName);
            TrainingDummy dummy = TrainingDummy.Spawn(parent, point.position, layout.GetSpawnYaw(pointName), tuning, swings);
            if (dummy == null) throw new InvalidOperationException("TrainingDummy.Spawn returned nothing for spawn point '" + pointName + "'.");
            MecanimPoseSource.AttachIfAvailable(dummy.gameObject, AnimationSetBuilder.LoadEnemySet());
            dummy.gameObject.name += " (" + pointName + ")"; // tells identical fighters apart in the Hierarchy
            director.RegisterDummy(dummy);
        }

        static void SpawnEnemy(ArenaLayout layout, string pointName, Transform parent, EnemyTuningAsset tuning, SandboxDirector director)
        {
            Transform point = RequireSpawn(layout, pointName);
            EnemyController enemy = EnemyController.Spawn(parent, point.position, layout.GetSpawnYaw(pointName), tuning);
            if (enemy == null) throw new InvalidOperationException("EnemyController.Spawn returned nothing for spawn point '" + pointName + "'.");
            MecanimPoseSource.AttachIfAvailable(enemy.gameObject, AnimationSetBuilder.LoadEnemySet());
            enemy.gameObject.name += " (" + pointName + ")";
            director.RegisterEnemy(enemy);
        }

        static Transform RequireSpawn(ArenaLayout layout, string pointName)
        {
            Transform point = layout != null ? layout.GetSpawnPoint(pointName) : null;
            if (point == null)
            {
                throw new InvalidOperationException("the arena has no spawn point called '" + pointName
                                                    + "' (ArenaBuilder in Editor/Sandbox should always create it).");
            }
            return point;
        }

        static Transform CreateGroup(string groupName)
        {
            var group = new GameObject(groupName);
            Undo.RegisterCreatedObjectUndo(group, UndoName);
            return group.transform;
        }

        // The new scene comes with a Main Camera and a Directional Light; reuse them (and make them if missing).
        static void GetDefaultCameraAndSun(Scene scene, out Camera camera, out Light sun)
        {
            camera = null;
            sun = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Camera foundCamera;
                if (camera == null && roots[i].TryGetComponent(out foundCamera)) camera = foundCamera;
                Light foundLight;
                if (sun == null && roots[i].TryGetComponent(out foundLight) && foundLight.type == LightType.Directional) sun = foundLight;
            }
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                Undo.RegisterCreatedObjectUndo(cameraObject, UndoName);
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }
            if (sun == null)
            {
                var sunObject = new GameObject("Directional Light");
                Undo.RegisterCreatedObjectUndo(sunObject, UndoName);
                sun = sunObject.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
        }

        static void SetUpSun(Light sun)
        {
            sun.color = SunColor;
            sun.intensity = SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = SunShadowStrength;
            sun.transform.rotation = Quaternion.Euler(SunAngles);
            // The default sky draws its sun disc where this light points, so sky and shadows agree.
            RenderSettings.sun = sun;
        }

        // Puts the sandbox first (the scene a build starts in) and keeps every other entry after it, once each.
        static void AddToBuildSettingsFirst(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(scenePath, true) };
            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes ?? new EditorBuildSettingsScene[0];
            for (int i = 0; i < existing.Length; i++)
            {
                EditorBuildSettingsScene entry = existing[i];
                if (entry == null || string.IsNullOrEmpty(entry.path) || ContainsPath(scenes, entry.path)) continue;
                scenes.Add(entry);
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static bool ContainsPath(List<EditorBuildSettingsScene> scenes, string path)
        {
            for (int i = 0; i < scenes.Count; i++)
            {
                if (string.Equals(scenes[i].path, path, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static void FrameSceneView(Transform target)
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;
            view.LookAt(target.position + Vector3.up * SceneViewPivotHeight, Quaternion.Euler(SceneViewPitch, target.eulerAngles.y, 0f), SceneViewSize);
            view.Repaint();
        }

        static bool EditorIsIdle(string action)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(DialogTitle, "Stop Play mode first, then " + action + ".", "OK");
                return false;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog(DialogTitle, "Unity is still compiling scripts or importing assets. Wait until it "
                                                         + "finishes (the spinner in the bottom-right corner), then try again.", "OK");
                return false;
            }
            return true;
        }

        static bool SceneExists()
        {
            return File.Exists(ScenePath) || AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null;
        }

        static void Progress(string step, float progress)
        {
            EditorUtility.DisplayProgressBar("Building the Combat Sandbox", char.ToUpperInvariant(step[0]) + step.Substring(1) + "...", progress);
        }

        static string NamesOf(List<string> paths)
        {
            var names = new string[paths.Count];
            for (int i = 0; i < paths.Count; i++) names[i] = Path.GetFileNameWithoutExtension(paths[i]);
            return string.Join(", ", names);
        }
    }
}
