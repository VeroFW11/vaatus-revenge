#if UNITY_EDITOR
// Editor-only, and compiled only in the Editor: this folder sits inside the game's runtime assembly
// (VaatusRevenge.Game, Scripts/), where a folder named "Editor" has no special meaning, so the #if keeps UnityEditor
// out of player builds. The sandbox builder (VaatusRevenge.Editor references VaatusRevenge.Game) calls it directly.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VaatusRevenge.EditorTools
{
    // Puts the painted player model (the GLB David chose) on the player, in place of the grey-box body.
    //
    //   Vaatu's Revenge > Use Player Avatar Model   adds it to the player in the open scene (and remembers the choice,
    //                                               so Build Fire Combat Sandbox adds it every time it rebuilds).
    //   Vaatu's Revenge > Use Procedural Body       takes it off again (and remembers that too).
    //
    // What gets added: an "AvatarModel" child of the player holding the imported model, and a SkinnedAvatarMirror on
    // the player that copies the procedural body's pose onto the model at Play (see SkinnedAvatarMirror). Nothing
    // else on the player changes, so taking it off brings back exactly what was there before.
    //
    // The model file is imported by glTFast (com.unity.cloud.gltfast in Packages/manifest.json); this code only ever
    // sees the GameObject the importer made, so it compiles and works without referencing glTFast.
    public static class PlayerAvatarSetup
    {
        public const string ModelPath = "Assets/_Project/Art/Characters/Player/player_avatar.glb";

        const string MenuRoot = "Vaatu's Revenge/";
        const string UseModelMenu = MenuRoot + "Use Player Avatar Model";
        const string UseProceduralMenu = MenuRoot + "Use Procedural Body";
        const string DialogTitle = "Player Avatar Model";
        const string UndoName = "Use Player Avatar Model";
        // Per machine, like any view preference: David can look at the model while Jeremy tests on the grey box.
        const string PreferenceKey = "VaatusRevenge.UsePlayerAvatarModel";

        // On by default: once the model file is in the project, rebuilt sandboxes use it.
        public static bool UseModel
        {
            get => EditorPrefs.GetBool(PreferenceKey, true);
            set => EditorPrefs.SetBool(PreferenceKey, value);
        }

        public static GameObject LoadModel()
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        }

        // For the sandbox builder: puts the model on this freshly spawned player if the model file is in the project
        // and nobody switched it off with "Use Procedural Body". Without the file it does nothing and says nothing
        // (the art bundle simply hasn't been unzipped on this machine). Returns true when the model was added.
        public static bool AttachIfAvailable(GameObject playerRoot)
        {
            if (playerRoot == null || !UseModel) return false;
            GameObject model = LoadModel();
            if (model == null) return false;
            if (Attach(playerRoot, model, false, out string report))
            {
                Debug.Log("Player avatar model added to '" + playerRoot.name + "': " + report);
                return true;
            }
            Debug.LogWarning("The player avatar model (" + ModelPath + ") can't be used, so the player keeps the procedural body: "
                             + report);
            return false;
        }

        [MenuItem(UseModelMenu, false, 10)]
        static void UseModelFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(DialogTitle, "Stop Play mode first, then try again.", "OK");
                return;
            }
            UseModel = true;
            GameObject model = LoadModel();
            if (model == null)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "The player model isn't in the project yet.\n\nExpected it at:\n" + ModelPath + "\n\n"
                    + "Unzip the art bundle into the repository folder (so the file lands at that path), wait for Unity to "
                    + "import it, then run this again. If the file is there but this still appears, check the Console: "
                    + "glTFast (Window > Package Manager) must be installed to import .glb files.\n\n"
                    + "From now on, Build Fire Combat Sandbox will add the model by itself once the file is there.",
                    "OK");
                return;
            }
            PlayerController player = FindPlayer();
            if (player == null)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "There's no player in the open scene.\n\nRun Vaatu's Revenge > Build Fire Combat Sandbox: it adds the model "
                    + "to the player by itself. Or open a scene that has the player and run this again.",
                    "OK");
                return;
            }
            if (!Attach(player.gameObject, model, true, out string report))
            {
                EditorUtility.DisplayDialog(DialogTitle, "The model can't be used, so the player keeps the procedural body.\n\n"
                                                         + report + "\n\nThe full details are in the Console.", "OK");
                Debug.LogWarning("Player avatar model: " + report);
                return;
            }
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            Selection.activeGameObject = player.gameObject;
            Debug.Log("Player avatar model added to '" + player.name + "': " + report);
            EditorUtility.DisplayDialog(DialogTitle,
                "The model is on the player. Save the scene (Ctrl+S) and press Play.\n\n"
                + "In Play mode the grey-box shapes disappear and the model moves with every attack and dodge. "
                + "To go back, use Vaatu's Revenge > Use Procedural Body.", "OK");
        }

        [MenuItem(UseProceduralMenu, false, 11)]
        static void UseProceduralFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(DialogTitle, "Stop Play mode first, then try again.", "OK");
                return;
            }
            UseModel = false;
            PlayerController player = FindPlayer();
            bool removed = player != null && Detach(player.gameObject, true);
            if (removed) EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            EditorUtility.DisplayDialog(DialogTitle,
                (removed ? "The model is off the player and the procedural body is back. Save the scene (Ctrl+S).\n\n"
                         : "The player in the open scene already uses the procedural body.\n\n")
                + "Rebuilding the sandbox keeps the procedural body until you choose Use Player Avatar Model again.", "OK");
        }

        // Menu tick marks show which one is in use on this machine.
        [MenuItem(UseModelMenu, true)]
        static bool UseModelValidate()
        {
            Menu.SetChecked(UseModelMenu, UseModel);
            Menu.SetChecked(UseProceduralMenu, !UseModel);
            return true;
        }

        [MenuItem(UseProceduralMenu, true)]
        static bool UseProceduralValidate()
        {
            return UseModelValidate();
        }

        // Adds (or replaces) the model on a player. False with a readable reason when it can't be used; the player is
        // then left with the procedural body. withUndo: true from the menu (Edit > Undo takes it off again); false from the
        // sandbox builder, whose whole new scene is one undo step anyway.
        public static bool Attach(GameObject playerRoot, GameObject modelAsset, bool withUndo, out string report)
        {
            HumanoidBody body = playerRoot != null ? playerRoot.GetComponentInChildren<HumanoidBody>(true) : null;
            if (body == null || !body.IsBuilt)
            {
                report = "'" + (playerRoot != null ? playerRoot.name : "(none)") + "' has no built HumanoidBody to copy the pose from";
                return false;
            }
            if (modelAsset == null)
            {
                report = "no model";
                return false;
            }
            Detach(body.gameObject, withUndo);

            var container = new GameObject(SkinnedAvatarMirror.ModelRootName);
            container.layer = body.gameObject.layer;
            container.transform.SetParent(body.transform, false);
            container.transform.SetPositionAndRotation(body.Root.position, body.Root.rotation);
            var instance = PrefabUtility.InstantiatePrefab(modelAsset, container.transform) as GameObject;
            if (instance == null) instance = Object.Instantiate(modelAsset, container.transform);
            instance.name = modelAsset.name;
            SetLayer(instance.transform, body.gameObject.layer);
            GreyboxShapes.StripColliders(instance);   // the camera and fighters collide with the CharacterController only

            SkinnedAvatarMirror mirror = body.GetComponent<SkinnedAvatarMirror>();
            if (mirror == null) mirror = withUndo ? Undo.AddComponent<SkinnedAvatarMirror>(body.gameObject) : body.gameObject.AddComponent<SkinnedAvatarMirror>();
            mirror.ModelRoot = container.transform;
            if (!mirror.PrepareModel(out report))
            {
                Object.DestroyImmediate(container);
                if (withUndo) Undo.DestroyObjectImmediate(mirror);
                else Object.DestroyImmediate(mirror);
                return false;
            }
            EditorUtility.SetDirty(mirror);
            if (withUndo)
            {
                Undo.RegisterCreatedObjectUndo(container, UndoName);
                Undo.SetCurrentGroupName(UndoName);
            }
            return true;
        }

        // Takes the model off: removes the AvatarModel child and the mirror. True if there was anything to remove.
        public static bool Detach(GameObject playerRoot, bool withUndo)
        {
            HumanoidBody body = playerRoot != null ? playerRoot.GetComponentInChildren<HumanoidBody>(true) : null;
            if (body == null) return false;
            bool removed = false;
            SkinnedAvatarMirror mirror = body.GetComponent<SkinnedAvatarMirror>();
            if (mirror != null)
            {
                if (withUndo) Undo.DestroyObjectImmediate(mirror);
                else Object.DestroyImmediate(mirror);
                removed = true;
            }
            for (int i = body.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = body.transform.GetChild(i).gameObject;
                if (child.name != SkinnedAvatarMirror.ModelRootName) continue;
                if (withUndo) Undo.DestroyObjectImmediate(child);
                else Object.DestroyImmediate(child);
                removed = true;
            }
            return removed;
        }

        // The player in the open scenes: the selected one if a player is selected, else the first one found by
        // walking the scenes' root objects (no scene-wide Find, per the project lint).
        static PlayerController FindPlayer()
        {
            if (Selection.activeGameObject != null)
            {
                PlayerController selected = Selection.activeGameObject.GetComponentInParent<PlayerController>();
                if (selected != null) return selected;
            }
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    PlayerController player = root.GetComponentInChildren<PlayerController>(true);
                    if (player != null) return player;
                }
            }
            return null;
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }
    }
}
#endif
