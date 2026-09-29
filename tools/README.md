# tools/

Helpers that run **outside Unity**. Unity never loads anything in this folder.

They exist so a machine without the Unity Editor (for example a cloud Claude session) can still check the game's C#: compile it the way Unity would, run the tests, and play the combat with bot inputs. On your own PC, Unity does all of this for you, so you don't need these day to day.

| Tool | What it does | Run |
|---|---|---|
| `compile-check.sh` | Compiles each game assembly (Core, Game in editor and player-build flavours, Editor) with C# 9 against Unity reference DLLs, then runs `unity-lint.py` | `tools/compile-check.sh` |
| `unity-lint.py` | Catches Unity rules the compiler can't: Core must not touch Unity, component file names must match class names, banned APIs (legacy Input, `Find*`, `Rigidbody`...), C# features newer than Unity supports | run by `compile-check.sh` |
| `run-core-tests.sh` | Runs `game/Assets/_Project/Tests/EditMode` with plain NUnit (the same tests also run in Unity's Test Runner) | `tools/run-core-tests.sh` |
| `CombatSim/` | Headless playtest harness: drives the real combat core with scripted and bot inputs at 60 fps and reports timings, exploits and bugs | `dotnet run --project tools/CombatSim` |
| `gen-unity-meta.py` | Creates missing `.meta` files for new files under `game/Assets/_Project` so asset GUIDs are committed | `python3 tools/gen-unity-meta.py` |

Needs the .NET 8 SDK (`apt install dotnet-sdk-8.0` on Ubuntu). NuGet restores the reference packages on first run.

**Limits, honestly:** the Unity API comes from Unity 2021.3 reference assemblies (plus a 2021.1 `UnityEditor.dll`) and a hand-written stub of the Input System that mirrors the real API. A clean result is strong evidence the code compiles in Unity 6000.6, not proof. The Unity Editor has the last word.
