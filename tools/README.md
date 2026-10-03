# tools/

Helpers that run **outside Unity**. Unity never loads anything in this folder.

They exist so a machine without the Unity Editor (for example a cloud Claude session) can still check the game's C#: compile it the way Unity would, run the tests, and play the combat with bot inputs. On your own PC, Unity does all of this for you, so you don't need these day to day.

| Tool | What it does | Run |
|---|---|---|
| `compile-check.sh` | Compiles each game assembly (Core, Game in editor and player-build flavours, Editor) with C# 9 against Unity reference DLLs, then runs `unity-lint.py` | `tools/compile-check.sh` |
| `unity-lint.py` | Catches Unity rules the compiler can't: Core must not touch Unity, component file names must match class names, banned APIs (legacy Input, `Find*`, `Rigidbody`...), C# features newer than Unity supports | run by `compile-check.sh` |
| `run-core-tests.sh` | Runs `game/Assets/_Project/Tests/EditMode` with plain NUnit (the same tests also run in Unity's Test Runner) | `tools/run-core-tests.sh` |
| `CombatSim/` | Headless playtest harness: drives the real combat core with scripted and bot inputs at 60 fps and reports timings, exploits and bugs. Build 05 scenarios: `rhythm`, `dodgeflow`, `switch`, `danger`, `elements` (see its README) | `dotnet run --project tools/CombatSim -c Release -- <scenario>` |
| `CombatSim` scenario `anim` | Plays a scripted fight (stance, 5-hit chain, launcher and air string, air dash, zip strike, abilities, parry/dodge/hit, sword attacks, knockdown, deaths, then a gallery of any animation key not reached) through the real combat core **and** the procedural animator, and writes every frame's joint positions, states, animation keys and fire effects as JSON. Prints strike extension at the first active frame and each dao strike's blade tip vs its reach, and checks (exit 1 on a MISS) that no lunge drags a planted foot out behind or snaps it to the floor, that Earth makes no rock effect while the player is in the air, that no strike slides a frozen pose over 12 m/s or changes speed by over 20 m/s in one frame, that every grounded Earth boulder rose out of the floor and is drawn inside its hit size, and (verify round 6) that no touchdown, plunge or landing drops a foot more than 0.15 m root-relative in one frame and no walk or run crabs sideways while moving straight, and (round 7) that no foot below 0.2 m moves more than 0.3 m over the floor in one frame (kicks aside) | `dotnet run --project tools/CombatSim -- anim --out /tmp/anim.json` |
| `render/render_fight.py` | Turns that JSON into an MP4 or a contact-sheet PNG (3/4 camera following a fighter, limbs, team colours, ground grid, fire effects by EffectKey, move captions), to check the animation without Unity. Needs `pip install matplotlib imageio imageio-ffmpeg`. Keep renders out of git | `python3 tools/render/render_fight.py /tmp/anim.json --mp4 fight.mp4` or `--sheet s.png --scene chain --count 12` (`--follow soldier` to watch an enemy) |
| `gen-unity-meta.py` | Creates missing `.meta` files for new files under `game/Assets/_Project` so asset GUIDs are committed (folders, scripts, text, and since Build 05 `.png` textures: a minimal meta, Unity fills in its default import settings) | `python3 tools/gen-unity-meta.py <new paths>` |

Needs the .NET 8 SDK (`apt install dotnet-sdk-8.0` on Ubuntu). NuGet restores the reference packages on first run.

**Limits, honestly:** the Unity API comes from Unity 2021.3 reference assemblies (plus a 2021.1 `UnityEditor.dll`) and a hand-written stub of the Input System that mirrors the real API. A clean result is strong evidence the code compiles in Unity 6000.6, not proof. The Unity Editor has the last word.
