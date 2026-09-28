# game/ (Unity project)

## First-time setup

1. Install **Unity Hub**, then **Unity 6** through it.
2. In Unity Hub: **Add > Add project from disk** and pick this `game/` folder.
3. Unity generates the rest (`Library/`, `ProjectSettings/`, etc.). Commit `ProjectSettings/` and `Packages/`. `Library/` is ignored.

## Where things go

Everything of ours lives in `Assets/_Project/` so it never mixes with plugins or Unity packages.

| Folder | What goes in it |
|---|---|
| `Scenes/` | Levels. One person per scene at a time |
| `Prefabs/` | Reusable objects (player, boss, campfire) |
| `Materials/` | Grey-box colours for now |
| `Models/` | FBX exports from Blender (`git lfs lock` before editing) |
| `Tuning/` | The tuning assets: every gameplay number |
| `Scripts/Core/` | Shared systems (health, damage, save) |
| `Scripts/Player/` | Movement, camera, input |
| `Scripts/Combat/` | Attacks, dodge, hit detection, lock-on |
| `Scripts/Boss/` | Boss AI and attack patterns |
| `Scripts/Tuning/` | The ScriptableObject definitions behind `Tuning/` |

## Prototype milestones

1. Capsule player that moves, with a camera
2. Attack, dodge, stamina, health
3. Lock-on
4. One capsule boss with 3 telegraphed attacks
5. Die, respawn at a campfire, recover runes
