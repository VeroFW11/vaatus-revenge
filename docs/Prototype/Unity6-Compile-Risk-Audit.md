# Unity 6 compile-risk audit

**29 Sep 2026, round 3.** `tools/compile-check.sh` compiles our code offline against **Unity 2021.3** reference DLLs and a hand-written Input System stub. The project runs on **Unity 6000.6.3f1** (URP 17.6, Input System 1.20). So a clean offline compile can miss two kinds of problem:

- APIs that Unity 6 renamed, or marked `[Obsolete(error: true)]`.
- Names that are only looked up at run time (reflection) and fail silently.

This audit reads every file under `game/Assets/_Project/Scripts/` (Core and Game), `game/Assets/_Project/Editor/`, and the two bootstrap scripts in `game/Assets/Editor/ProjectBootstrap/`, looking for those risks.

**Verdict: nothing found that should fail to compile in Unity 6000.6.** The code already avoids the APIs Unity 6 changed. The spec's bans (section 7) and `tools/unity-lint.py` did most of that work. One domain-reload gap was fixed. A person still needs to open the project in the Editor to confirm.

## APIs Unity 6 changed that we don't use

Checked with grep over all code:

- `Object.FindObjectOfType` / `FindObjectsOfType`: obsolete since 2023.1. The newer `FindObjectsByType` with `FindObjectsSortMode` is also being phased out in 6.x.
- `GetInstanceID` / `InstanceIDToObject` / `Selection.activeInstanceID`: being replaced by `EntityId`. We use `CombatIds` instead.
- `Rigidbody.velocity` / `drag`: renamed to `linearVelocity` / `linearDamping` in Unity 6. We don't use Rigidbody at all.
- `PhysicMaterial`: renamed to `PhysicsMaterial` in Unity 6.
- `GraphicsSettings.renderPipelineAsset`: obsolete in 6, replaced by `defaultRenderPipeline`.
- `PlayerSettings.Get/SetScriptingDefineSymbolsForGroup(BuildTargetGroup)`: obsolete, replaced by `NamedBuildTarget`.
- `Screen.currentResolution.refreshRate`: replaced by `refreshRateRatio`.
- `StaticEditorFlags.NavigationStatic`.
- Legacy `UnityEngine.Input`, `TextMesh` / UGUI `Text`, `SendMessage`.

## Findings

| File:line | API | Risk in 6000.x | Action |
|---|---|---|---|
| `Editor/Sandbox/SandboxPostProcessing.cs:23-24, 129` | Reflection type names `UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime` and `UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime` | **Low.** Both names are unchanged in URP 17.x. If a later URP renames an assembly, the code falls back to searching the loaded assemblies by full name. If that also fails, it logs one warning and builds the sandbox without bloom; it never fails the build. | None. The first Editor build should show no "bloom isn't fully set up" warning (report 02 checklist 1). |
| `Editor/Sandbox/SandboxPostProcessing.cs:59, 87-88` | `Volume.sharedProfile` (a field) and `Volume.isGlobal` (a property in Core RP 14+) | **Low.** `SetMember` handles either a property or a field, so the move between the two across URP versions is already covered. | None. |
| `Editor/Sandbox/SandboxPostProcessing.cs:121` | `UniversalAdditionalCameraData.renderPostProcessing` | **Low.** This property still exists in URP 17. | None. |
| `Editor/Sandbox/FireSandboxBuilder.cs:346` | `EditorBuildSettings.scenes = ...` | **Low, behaviour only.** In Unity 6, if a Build Profile is active and overrides the scene list, this edits the shared list that profile ignores. It still compiles and doesn't warn. | None. If a build starts in the wrong scene, check File > Build Profiles. |
| `Scripts/Combat/CombatPhysics.cs:21, 35, 56` | `Physics.RaycastNonAlloc`, `SphereCastNonAlloc`, `OverlapSphereNonAlloc` | **Low.** Not obsolete in 6000.x. They are the recommended zero-allocation queries. | None. |
| `Scripts/Camera/LockOnController.cs:263` | `Physics.Linecast(..., out hit, mask, QueryTriggerInteraction)` | None. | None. |
| `Scripts/Input/PlayerInputReader.cs:288-341`, `Scripts/Player/PlayerRumble.cs:30-59`, `Scripts/UI/CombatHud.cs:115-119`, `Scripts/Sandbox/SandboxDirector.cs:222-229` | Input System members: `InputActionMap.AddAction(..., expectedControlLayout:)`, `AddBinding`, `AddCompositeBinding("2DVector").With(...)`, `WasPressedThisFrame`, `IsPressed`, `ReadValue<T>`, `activeControl.device is Gamepad`, `Gamepad.current`, `SetMotorSpeeds`, `ResetHaptics`, `added`, `Keyboard.current.fXKey.wasPressedThisFrame` | **Low.** Every member exists with the same signature in Input System 1.20. The stub mirrors them. Active Input Handling is "Input System Package" (`activeInputHandler: 1`). | None. The template's project-wide `Assets/InputSystem_Actions.inputactions` is enabled by Unity at start-up but never read, so it doesn't clash with our own map. |
| `Scripts/Greybox/GreyboxShapes.cs:15-17, 149-150, 185, 197-208` | `Shader.Find("Universal Render Pipeline/Lit" / "Unlit" / "Particles/Unlit")`, `_BaseColor`, `_EmissionColor`, keywords `_EMISSION`, `_SURFACE_TYPE_TRANSPARENT`, `globalIlluminationFlags` | **Low.** The shader names and properties are unchanged in URP 17. A missing shader logs one warning and falls back. Note that `Shader.Find` only finds a shader in a player build if a material uses it or it's in Always Included Shaders. That is an existing, known build (not compile) risk. | None for the Editor sandbox. Before a standalone build, add the three shaders to Always Included Shaders or reference them from a material. |
| `Scripts/Greybox/GreyboxShapes.cs:26-45` | `(int)PrimitiveType` as an array index | **Low.** It is already bounds-checked, so a new enum value would return null instead of throwing. | None. |
| `Scripts/UI/*.cs`, `Scripts/Enemies/EnemyHealthBar.cs`, `Scripts/Camera/LockOnController.cs:345` | IMGUI (`OnGUI`, `GUI.DrawTexture`, `GUIStyle(GUI.skin.label)`, `useGUILayout`) | None. IMGUI is fully supported in Unity 6. | None. |
| `Editor/Sandbox/*.cs` | `Undo.AddComponent(GameObject, Type)`, `Undo.RegisterCreatedObjectUndo`, `EditorSceneManager.NewScene/SaveScene`, `SceneView.LookAt`, `AssetDatabase.LoadAssetAtPath(string, Type)`, `EditorUtility.CopySerialized`, `[MenuItem]` | None. All of these exist in 6000.x. | None. |
| `game/Assets/Editor/ProjectBootstrap/*.cs` | `PackageManager.Client.AddAndRemove`, `PlayerSettings.productName` | **Low.** These are fine in 6000.x. Note that they sit **outside** `_Project`, so the offline check never compiles them. | None. They only run from the command line. |
| `Scripts/Common/Layers.cs:11-12` | Static layer caches without a `SubsystemRegistration` reset | Not a compile risk. It breaks the domain-reload-off rule (spec section 7): a layer renamed between Play sessions would keep its stale number. | **Fixed:** added `ResetStatics()` with `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`. |
| `Scripts/Greybox/GreyboxShapes.cs:26-28` | Static mesh caches (`primitiveMeshes`, `ringBandMesh`) survive between Play sessions | None. This is deliberate: they cache assets, and Unity's `== null` check catches a destroyed mesh. Resetting them would leak one mesh per session. | Left as is. |

## Still needs a person in the Editor

The offline check can't prove any of this. The first time the project opens in 6000.6.3f1:

1. The Console should have no red errors and no `CS0618` / `CS0619` "obsolete" lines from `VaatusRevenge.*`.
2. **Build Fire Combat Sandbox** should log no bloom warning.
3. The sandbox should have a "Global Volume" and Post Processing ticked on the Main Camera.
