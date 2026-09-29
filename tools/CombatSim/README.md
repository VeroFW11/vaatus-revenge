# CombatSim: headless playtest harness

CombatSim plays the Fire combat prototype without Unity. It compiles the game's real combat core
(`game/Assets/_Project/Scripts/Core/`, pure C#, via `tools/UnityCompileCheck/Core.Check.csproj`) into a
console program. It then drives the core at a fixed 60 fps with scripted inputs and bots that have human
reaction times, and prints Markdown tables: frame data, input buffering, exploits, fairness, camera
behaviour and invariant violations.

It exists because the rules can be measured before anyone opens the Editor. It does **not** replace playing
in Unity. The core runs as it is, but everything Unity does (physics, the CharacterController, rendering,
Input System timing) is a small hand-written stand-in. So treat the numbers as strong evidence about the
rules, not as proof of how the game feels.

## Running it

```
dotnet run --project tools/CombatSim -- <scenario> [options]
dotnet run --project tools/CombatSim -- help
```

| Scenario | What it measures | Time (this container) |
|---|---|---|
| `framedata` | Startup, active, recovery and cancel points of every move, both presets | < 1 s |
| `buffer` | When buffered presses fire, which press wins, stale presses, heavy-during-jab charge | < 1 s |
| `controls` | Dodge tap vs hold, stick dead zone, camera turning during a dodge, sprint attack and plunge conditions, guard/deflect windows, heal | < 1 s |
| `abilities` | Fa jin timing (with human timing noise), fa jin after a perfect dodge, damage loops, stamina at zero, Fire Blast vs a strafing target, Momentum, perfect-dodge windows, stagger immunity | ~2 s |
| `fairness` | Telegraph length vs human reaction, reaction dodges/deflects, unavoidable damage (oracle bot), attack-token release, the platform crossbowman | ~5 min (40 seeds; `--seeds 20` ≈ 2.5 min) |
| `duels` | Six bot styles vs five enemy groups, both presets, many seeds | ~9 min (40 seeds) |
| `duel` | One duel, optionally recorded (`--record`) | seconds |
| `fuzz` | Long random-input runs at 30/60/144 fps, with hitches and paused frames, preset swaps, respawns and resets. Every frame is checked against invariants | ~4 min: 8 runs × 150,000 frames (`--quick`: 60,000) |
| `camera` | Over-the-shoulder camera and lock-on: circling, dash-past, overhead target, elevated target, framing, retarget on kill, switching, line of sight, walls, shoulder swaps (auto and the hold-to-swap button), combat pull-back, look input at several frame rates, real fights, and pops near the low corridor and the pillars | ~11 s |
| `all` | Everything except `duel` (`--quick` caps seeds at 12 and shortens the fuzz) | ~18 min (`--quick`: ~6 min) |

Times above are for Debug builds. `dotnet run --project tools/CombatSim -c Release -- <scenario>` is roughly 10× faster (fairness ~20 s, duels ~1 min, fuzz ~25 s).

Options:

| Option | Meaning | Default |
|---|---|---|
| `--seed N` / `--seeds N` | First seed, and how many seeds per case | 1 / 40 |
| `--preset fluid\|punishing\|both` | Which preset(s) | both (`duel` uses the first) |
| `--fps N` | Frame rate for `duel` and `duels` | 60 |
| `--out file.md` | Also write the output to a file | stdout only |
| `--bot NAME` | `duel` only: `masher`, `react`, `anticipate`, `guard`, `aggressive`, `fajin`, `oracle`, `idle` | `anticipate` |
| `--enemies LIST` | `duel` only: comma list of `soldier`, `crossbow`, `platform`, `dummy` | `soldier` |
| `--seconds N` | `duel` only: time limit | 60 |
| `--record file.json` | `duel` only: write a per-frame replay | off |
| `--quick` | Shorter fuzz (60,000 frames per run), at most 12 seeds for `all` | off |
| `--notify-strikes` | Enemies call `PlayerCombatModel.NotifyEnemyStrike` on every melee strike, so the core's `WouldHaveLanded` perfect-dodge rule works. **No Unity script calls it yet** (report 02, NEW-01), so it's off by default to mirror Unity | off |
| `--set target.Field=value` | What-if tuning without touching game code, repeatable. Targets: `player` (PlayerTuning), `dodge` (DodgeProfile), `charge` (ChargeSettings), `soldier` / `crossbow` (EnemyTuning). E.g. `--set player.EmptyStaminaRegenDelay=0.8 --set soldier.MaxHealth=110` | none |

Examples:

```
dotnet run --project tools/CombatSim -- framedata
dotnet run --project tools/CombatSim -- duels --seeds 10 --preset punishing --out /tmp/duels.md
dotnet run --project tools/CombatSim -- duel --bot oracle --enemies soldier,soldier,crossbow --preset punishing --seed 3 --record replay.json
```

Every run prints a fingerprint of the core sources (`sha1 ...`, plus the camera model's own hash). Results
can then be matched to a version of the code. Runs are deterministic: the same seed and the same code give
the same numbers.

## Bots

Bots see the world one frame late, like a person. They react after a reaction time drawn from
N(0.25 s, 0.04 s) (clamped to 0.15–0.6 s), add timing noise, hold buttons for 0.06–0.12 s and move relative
to the real camera. They lock on by picking the nearest threat directly. Lock-on *selection* is measured
separately in `camera`.

| Bot | Plays like |
|---|---|
| `masher` | Mashes light at a human cadence, walks at the nearest enemy, never defends |
| `react` | Dodges the moment it notices a wind-up (reaction time only), then counterattacks |
| `anticipate` | Knows each attack's timing: dodges ~0.06 s ± 0.05 s before the strike, mostly into or beside it |
| `guard` | Holds guard when something is coming and re-presses to deflect (± 0.05 s) |
| `aggressive` | Stays close, chains, sprint-kicks in, dodges into attacks, fa jin on staggered targets |
| `fajin` | Waits for openings and punishes with a timed fa jin (hold 0.8 s ± 0.05 s) |
| `oracle` | Frame-perfect defence, never attacks. Dodges 5 frames before every strike (7 before a bolt arrives), sideways and away from the group. Hits it still takes can't be avoided by dodging alone |
| `idle` | Does nothing (how fast do enemies kill a passive player?) |

## Replays

`duel --record file.json` writes one JSON object (`"format": "vaatus-combatsim-replay/1"`), so a session
can be inspected or rendered later:

- a header: scenario, `dt`, meta (preset, seed, bot, enemies, level, outcome), the names for the player
  states, enemy states and attack phases, and `fighters` (index, name, team, radius, height; the player
  is index 0);
- `frames`: one array per frame,
  `[frame, realTime, gameTime, timeScale, [fighters], [camera], [projectiles]]`, where
  - player: `[x, y, z, yaw, state, hp, stamina, momentum, invulnerable 0/1, chargeLevel 0..1]`
  - enemy: `[x, y, z, yaw, state, hp, phase, alive 0/1]`
  - camera: `[yaw, pitch, distance, camX, camY, camZ, lockTargetIndex or -1]`
  - projectile: `[x, y, z, isFire 0/1]`
  (positions are the feet, in metres, Unity axes: y up; `state` and `phase` index the header's name lists);
- `events`: `[frame, fighterIndex, type, detail]`, e.g. `player:DodgeStarted`, `enemy:TelegraphStarted`,
  and `hit` with the outcome (Hit, Evaded, PerfectEvade, Blocked, Parried...) for every hit resolved.

The sessions that `docs/Prototype/Playtest-Report-01.md` refers to are in `replays/` (recorded on the report-01 code; re-record them to see the 60cb8ee behaviour). They were recorded
with the command shown for each, so they can be re-created:

| File | Command (`duel ...`) | What it shows |
|---|---|---|
| `punishing-oracle-vs-2-soldiers-crossbow.json` | `--bot oracle --enemies soldier,soldier,crossbow --preset punishing --seed 1 --seconds 60` | Frame-perfect dodging still takes 3 hits in 60 s (a bolt, a Quick Slash, a Heavy Overhead). All three land 20–22 frames into a dodge, after its i-frames end (frame 17), while 2–3 enemies are attacking |
| `fluid-anticipate-vs-sandbox-ring.json` | `--bot anticipate --enemies soldier,soldier,crossbow,platform --preset fluid --seed 4 --seconds 120` | The whole sandbox ring in Fluid: a 49 s win with 6 perfect dodges, 5 counters and Momentum reaching 100. All 8 hits taken are crossbow bolts |
| `fluid-masher-vs-soldier.json` | `--bot masher --enemies soldier --preset fluid --seed 1` | Mashing light, with no defence at all, kills a Dao Soldier in 4.0 s and takes one hit |
| `platform-crossbowman-walks-off.json` | `--bot idle --enemies platform --preset fluid --seed 1 --seconds 20` | The platform crossbowman walks off the 2.5 m block 1.4 s after noticing the player, then shoots from the ground |

## What is mirrored, and what is simplified

The world is stepped in the Unity script execution order the spec pins down (TimeScaleController -200,
LockOnController -50, PlayerController 0, enemies 10, FireProjectile 20, camera LateUpdate 100). Game
`deltaTime` = unscaled time × the time scale set by the end of the previous frame, clamped like Unity's
`Time.maximumDeltaTime` (1/3 s).

| Unity side | In CombatSim | Simplification |
|---|---|---|
| `PlayerController` | `Sim/SimPlayer.cs`: builds `PlayerWorldState` (lock/soft target with line of sight, `SelfHeight`, `RealDeltaTime` = unscaled dt, nearest living enemy), ticks the model, moves, turns events into hit queries, projectiles, slow motion and hitstop | No animation or VFX. Rumble and screen shake are not modelled |
| `EnemyFighter`, `EnemyStrikes`, `TrainingDummy` | `Sim/SimEnemy.cs`: brain tick, the ledge check (`WouldStepOffLedge`), one move per frame, melee sweeps on open and every later active frame, bolts, deflect reactions, corpses stop blocking. `NotifyEnemyStrike` only with `--notify-strikes` | Same rules. No presenter or visuals. The ledge probe reads box tops and the ramp's height field |
| `MeleeHitQuery`, `FireProjectile` | `Sim/SimHits.cs`: arc and sphere queries with per-attack hit records, chest and head line-of-sight rays, swept projectiles | Geometry is boxes only |
| `CharacterController` | `Sim/SimPhysics.cs` `SimController`: kinematic capsule, slides on boxes and other fighters, step offset 0.3, `isGrounded` after each move | No skin width, no slope limit. The ramp is a height field |
| Arena (`ArenaBuilder`) | `SimLevel.SandboxArena()`: floor, walls, pillar field, low corridor, raised platform, stairs, ramp, copied from the builder's constants | Axis-aligned boxes |
| `TimeScaleController` | `SimTime`: hitstop 0.02×, slowest slow-mo wins, pause 0, timers on real time | Same rules |
| `LockOnController`, `ThirdPersonCameraRig` | `Sim/SimCameraRig.cs`: lock-on selection, break and switching, the orbit model's pivot → orientation → lift → shoulder → distance order with sphere probes; the swap button is passed as held (`SwapShoulderHeld`), as the rig does since 60cb8ee | 16:9, 60° vertical FOV for screen positions. No shake or FOV boost |
| `PlayerInputReader` | `Input/Pad.cs`: builds `PlayerInputFrame` press/hold/release flags | Values are what the reader would output after the Input System's own stick dead zone |

## Layout

- `Program.cs`: options, the scenario list, `Session` (a world with a player in a preset, a camera, fixed dt).
- `Sim/`: the world, player, enemies, hits, physics stand-in, camera rig, metrics and invariants, and the replay recorder.
- `Bots/Bot.cs`: the bot players. `Input/Pad.cs`: pad state, a button scheduler, human timing, and a small
  script DSL (`new InputScript().Hold(Button.Light, 0, 3).Stick(0, new Vector2(0, 1))`).
- `Scenarios/`: one file per scenario. `Trace.cs` has helpers for frame-by-frame traces.
- `Report/Table.cs`: Markdown tables and output.

To add a scenario, write a `static void Run(Options o)` in `Scenarios/` and register it in
`Program.Scenarios`. Build a `Session`, step it with `Pad`s, read `s.Model` (the player's
`PlayerCombatModel`), `s.World.Enemies`, `s.World.Metrics` and `s.World.LockOn`, then print with
`Table`.

CombatSim sits outside `game/`, so Unity never sees it, and it isn't part of `tools/compile-check.sh`.
