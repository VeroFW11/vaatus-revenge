# Fire Combat Prototype: Build Spec

> **Status:** being built (28 Sep 2026) by a team of Claude agents while David is away. This is the single source of truth the agents build from. Starting numbers here are **first guesses to tune by playtesting**, not decisions.

The goal is a grey-box sandbox where you can **fight with firebending and judge whether it's fun**: Marvel's Spider-Man 2 controls and free-flow targeting (David's call, 29 Sep: attacks aim where you point, a zip strike to far enemies, tap to parry, no lock-on needed), with the Elden Ring-style move set, jump and sprint attacks, one element-flavoured dodge, an optional lock-on, and a few enemies built to test dodging, parrying and pressure. Everything is capsules and cubes; the point is timing and feel.

**How David and Jeremy will use it:** pull the branch, open Unity, click **Vaatu's Revenge ▸ Build Fire Combat Sandbox**, press Play.

---

## 1. What the player gets

### Controls

Spider-Man 2's layout (David, 29 Sep). The shoulder buttons double as modifiers, the way Spider-Man 2 does abilities (hold L1 + a face button) and gadgets (hold R1 + a face button).

| Action | Gamepad (Xbox / PlayStation) | Keyboard + mouse |
|---|---|---|
| Move | Left stick | WASD |
| Camera | Right stick | Mouse |
| Swap camera shoulder (hold) | Hold L3 (left-stick click) | Hold V |
| Attack (3-hit chain; aims at the enemy your stick points at and lunges to close the gap) | X / Square | Left mouse |
| Zip strike (Flame Step Strike: dash across to a far enemy and kick it) | Y / Triangle | F |
| Fa Jin Palm (ability slot: hold to charge, release on the flash) | Hold LB / L1, then hold X / Square | Hold Q, then hold left mouse |
| Dodge (tap) / Sprint (hold) | B / Circle | Left Shift |
| Jump | A / Cross | Space |
| Parry (tap just before a hit lands) | Tap LB / L1 | Q |
| Fire Blast (ranged skill) | Tap RB / R1 (fires on release) | Right mouse |
| Drink spirit water (heal) | D-pad down | R |
| Element select | Hold RB / R1 + Y / B / A / X (Y = Fire; others "not learned yet") | 1-4 |
| Lock on / off (optional; off by default) | R3 | Middle mouse or Tab |
| Switch target | Flick right stick while locked | Mouse wheel, or Z / C |

- **Empty ability slots:** LB + Y / B / A have no ability yet, so they still zip, dodge and jump as normal.
- **Fire Blast fires on release** so RB can also be the element modifier. A tap longer than 0.35 s (`PlayerInputReader.skillTapMaxTime`) counts as a cancelled element pick and fires nothing.
- **Defence style is per element** (`GuardSettings.Style`): Fire is **parry only** (Spider-Man 2). A later element can switch to `BlockAndParry` (hold to block, press on time to deflect) in its move set, with no code change.

Sandbox keys (keyboard): **F1** controls overlay, **F3** debug panel (state, frame data, buffered input, i-frames), **F2** slow motion (0.25x) for studying moves, **F5** Fluid preset, **F6** Punishing preset, **F4** respawn player, **T** reset enemies, **Esc** release the mouse / pause (R is Heal).

### Moves (Fire, based on Northern Shaolin)

Northern Shaolin is long, extended strikes, powerful kicks and fast footwork with relentless forward pressure. Fire's identity mechanic is **Momentum**: landing hits builds it and it boosts damage; backing off or going quiet drains it.

| Move | Input | Idea |
|---|---|---|
| Flame Jab → Flame Cross → Dragon Tail Kick | Light x3 | Quick, fire-extended punches, then a wide spinning kick finisher |
| Fa Jin Palm | Hold LB + X (ability slot) | Charge, then release. Releasing inside the **sweet spot** (the fighter flashes) gives a fa jin burst: huge damage and stagger. Tests the open question "where does fa jin fit?" as a timing reward |
| Flying Fire Kick | Attack while sprinting | Long lunging kick that closes distance |
| Flame Step Strike | Zip strike | Fire from the feet carries you across up to 14 m into a flying kick (Spider-Man 2's web strike). Needs a target where you're aiming; with none, nothing happens and it costs nothing |
| Falling Axe Kick | Attack or Fa Jin in the air | Drops fast, bursts a ring of fire on landing |
| Fire Blast | Skill | Ranged fireball, costs more stamina |
| Flame Step (dodge) | Dodge tap | A short fire-assisted dash with invincibility frames. Can cancel into attacks. A **perfect dodge** (the hit would have landed in the first moments of the dash) slows time briefly, gives Momentum and opens a counter window |
| Flame Parry | Parry | Tap just before a hit lands to deflect it (any direction) and stagger the attacker. Fire has no block: a mistimed parry means you take the hit, and unparryable attacks must be dodged |
| Spirit Water | Heal | 3 charges, slow to drink, punishable, like Elden Ring's flask |

### Enemies

| Enemy | Why it's in the sandbox |
|---|---|
| **Dao Soldier** (melee) | Readable telegraphs (weapon glows during wind-up), a quick slash, a slow heavy overhead, a two-hit combo and a delayed thrust that punishes panic dodging, plus a violet armoured "break-out" shove if you mash three hits into it. Two of them test that only a limited number attack at once (attack tokens) |
| **Crossbowman** (ranged, repeating crossbow) | Keeps distance, fires aimed shots and 3-bolt bursts. Tests lock-on switching, dodging projectiles and closing distance. One stands on the raised platform to test camera pitch |
| **Sparring Dummy** | Takes hits, shows damage, combo count and DPS, resets its health. Can swing on a fixed rhythm to practise dodge and deflect timing |

### The two presets (the fluid vs punishing question)

The Quest Board picked **Spider-Man fluid** dodging; Jeremy should confirm it by playing. Both feels ship as tuning presets, switchable live with F5 / F6 so they can be compared in the same fight:

| | **Fluid** (default) | **Punishing** (Elden Ring-like) |
|---|---|---|
| Dodge stamina | ~6 | ~16 |
| Dodge triggers | on press | on release (like Elden Ring) |
| Dodge cancels into attacks | yes, early | no, only after it ends |
| Perfect dodge reward | slow-mo + Momentum + counter window | off |
| Stamina regen | fast, short delay | slower, longer delay |

---

## 2. Starting numbers (tune these!)

Seconds unless noted; distances in metres. At 60 fps, 0.1 s = 6 frames. All of these live in tuning assets; code only seeds the first copy.

**Player (Fluid):** HP 100 · stamina 100, regen 45/s after 0.6 s (1.5 s when a spend empties the bar; in Fluid only mashing does, so this pause is the anti-mash lever, report 02 NEW-02) · walk 2.0 (stick under half) · run 4.8 · sprint 7.2 · accel 30 m/s² · decel 40 m/s² · turn 900°/s · lock-on strafe 3.8 · air control 0.35 · jump height 1.25 · gravity 28 m/s² · coyote time 0.1 · input buffer 0.25 · tap-vs-hold threshold 0.22 · poise 30.

**Light chain:**

| | Startup | Active | Recovery | Dmg | Poise | Range | Arc° | Lunge | Stamina | Combo window (from move start) |
|---|---|---|---|---|---|---|---|---|---|---|
| Flame Jab | 0.12 | 0.10 | 0.30 | 8 | 8 | 2.6 | 70 | 0.4 | 9 | 0.14-0.40 |
| Flame Cross | 0.13 | 0.10 | 0.32 | 9 | 9 | 2.7 | 70 | 0.45 | 9 | 0.15-0.42 |
| Dragon Tail Kick | 0.20 | 0.14 | 0.46 | 15 | 22 | 3.0 | 200 | 0.6 | 13 | loops back to Jab after |

**Heavy (Fa Jin Palm):** startup 0.28 after release · active 0.12 · recovery 0.55 · dmg 20 · poise 30 · range 3.4 · arc 80 · stamina 22 · charge up to 1.2 · release before 0.2 = quick heavy · **sweet spot 0.65-0.95 = fa jin (x1.8 dmg, x2 poise, +0.12 hitstop)** · a "get ready" cue 0.12 before it, so reacting to the cue (0.18-0.25 s) releases mid-window (report 02 NEW-04) · after the sweet spot = charged (x1.2 dmg) · holding to 1.2 auto-releases as charged.

**Flying Fire Kick** (after sprinting ≥ 0.3): startup 0.18 · active 0.14 · recovery 0.40 · lunge 3.2 · dmg 16 · poise 22 · stamina 16.
**Falling Axe Kick:** hang 0.08, fall at 18 m/s, landing ring radius 2.2 · dmg 18 · poise 25 · stamina 14 · landing recovery 0.35.
**Fire Blast:** startup 0.22 · recovery 0.30 · stamina 18 · speed 30 · dmg 13 · poise 10 · range 26.
**Flame Step (Fluid):** 4.2 m over 0.30 · i-frames 0.02-0.24 · stamina 6 · cancel into attack after 0.14 · next dodge allowed after 0.28 (**chained dodges must leave an i-frame gap**, ≥ 0.05 s, so dodge-spam isn't permanent invincibility) · perfect window 0.12 · reward: 0.35x slow-mo for 0.35 s (real time), +25 Momentum, 0.8 s counter window at x1.5 damage · no stick input = short backstep (2.2 m).
**Flame Step (Punishing):** 16 stamina · 0.36 duration · i-frames 0.04-0.30 · no attack cancel until the end + 0.12 recovery · next dodge after 0.48 · no perfect reward · triggers on release · regen 38/s after 0.65 s (1.1 s when emptied; report 02 NEW-02) · light attacks cost 13, heavy 28 · buffer 0.2.
**Parry (Fire, `GuardSettings.Style = ParryOnly`):** a press opens the deflect window below over 360° and the stance drops by itself when it closes (holding does nothing). Anything it doesn't catch lands cleanly.
**Guard (only for an element with `Style = BlockAndParry`):** front arc 160° · blocks all damage, pays GuardStaminaDamage in stamina · guard break (not enough stamina) = 1.0 s stagger · move at 45% speed while guarding. **Deflect:** guard pressed ≤ 0.15 before a parryable hit → attacker staggered 1.3 s, +25 Momentum, no stamina cost. A deflect press that catches nothing locks deflect out for 0.35 s (no mashing).
**Heal:** 3 charges · 1.0 s action · heals 45 HP at 0.55 s (charge used only then) · 35% move speed while drinking.
**Momentum:** 0-100 · +8 light hit, +14 heavy/sprint/plunge, +20 fa jin, +25 perfect dodge or deflect · starts draining 1.6 s after the last gain at 20/s · drains 35/s while locked on and moving away from the target · damage x1.0 at 0 up to x1.4 at 100.
**Hitstop** (freeze-frame on impact): light 0.035, finisher 0.06, heavy 0.08, fa jin 0.2.
**Attack tracking:** attacks turn towards the stick (or the lock-on target) quickly during startup, then commit. Enemies do the same: **they stop tracking when their active frames start**, which is what makes dodging possible.
**Soft lock and free-flow lunge:** when not locked on, an attack aims at the nearest enemy within 7 m and 60° of where you're aiming (the stick, else your facing). A light attack at a target out of reach lunges up to 4.5 m further than its own step (`PlayerTuning.GapCloseDistance`), straight at the target, stopping 0.3 m short of it.
**Flame Step Strike (zip strike):** target within 14 m, 50° of where you're aiming and 3 m up or down (`ElementMoveSet.Zip`) · dash covers the whole gap during startup 0.30 + active 0.12 · recovery 0.38 · dmg 12 · poise 18 · range 2.4 · arc 90 · stamina 14 (Punishing 20).

**Camera (over the shoulder, like Marvel's Spider-Man 2; David's call):** shoulder offset 0.55 m to the right, so the fighter sits left of centre (0.35 while locked on), eased over 0.2 · hold L3 / V for 0.25 to swap shoulders (one swap per hold, so an accidental stick click while sprinting does nothing); a wall on the shoulder side (less than half the offset fits) swaps automatically, back once that side has been clear for 1.5 · distance 3.2 free / 4.0 locked · min 1.0 · pivot height 1.6 · combat pull-back: not locked on and a living foe within 8 m → ease out 0.9 m and 0.2 m higher (smooth time 0.4), back in 1.5 after the last foe leaves the radius · pitch -40 (up) to 65 (down), default 12 · stick 200°/s yaw, 140°/s pitch, response curve exponent 1.6 · mouse 0.12°/pixel · invert Y off · lock-on yaw smoothing 0.12 and at most 540°/s (a target passing overhead can't whip the view round), aimed from the shoulder so the target sits on the centre line and the fighter off to the side (correction capped at 15° at melee range); pitch aims at ~18° plus framing so a tall or elevated target stays on screen · collision radius 0.25, probed pivot → up → shoulder → back so the shoulder side can't clip a wall; the shoulder offset must also fit at the camera's end, so walking past a wall's end doesn't drag the camera into it. Walls touching the camera pull it in instantly (never inside geometry) and it eases back out over ~0.35; a pillar that only blocks the view waits 0.1, then glides in over ~0.15 up to its far side (only its thickness is skipped in one frame); a 0.2 m fatter look-ahead probe starts that glide before a pillar touches the camera, never closer than 1.1; a wall closer behind than 1.1 makes the camera rise and look down over the head (up to 85°, never moving faster than 8 m/s) instead of sliding into it (report 02 NEW-03). While the camera turns or travels, 3 sweep probes look 0.3 ahead along the swing (at most 60° round, from where the shoulder point is heading), so a pillar face about to be swept into the camera starts the glide early, never closer than 0.6 on its own (report 02 round 3) · FOV 60 (+5 while sprinting). **Centred preset** for comparison (`CameraTuning.CreateCentred()`, the first prototype's Elden Ring framing): offsets 0, distance 4.0 / 4.6, pivot 1.55, no pull-back.
**Lock-on:** acquire within 22 m, prefer targets near the centre of the screen · break beyond 30 m or after 1.2 s without line of sight · flick threshold 0.75, 0.3 s between switches · on a kill, move to the next target.

**Dao Soldier:** HP 110 · poise 35 (regens after 2 s) · walk 2.0, chase 4.2, strafe 1.6 · attacks: Quick Slash (startup 0.50, active 0.12, recovery 0.55, dmg 12, range 2.4, arc 100) · Heavy Overhead (0.95 / 0.14 / 0.95, dmg 26, poise 40, range 2.6, arc 60) · Double Slash (two slashes 0.35 apart) · Delayed Thrust (1.15 with a visible pause, range 3.2, arc 30, dmg 18) · 1.2-2.2 between attacks, circles for 0.8-2.0 · at most 2 enemies attacking at once · **Break-Out** (anti-mash rule, `EnemyTuning.BreakOut`, report 02 round 3): 3 clean hits within 1.2 while not staggered arm an armoured counter. Hits on its recovery count only if its swing landed on you first (a trade, not a punish). It needs a token, waits while another enemy is attacking, gives up after 0.6 of waiting and has a 2 cooldown, measured from the break-out's start, that always holds: hits on the break-out itself (wind-up, shove, recovery) never count, and hits after it ends are banked but only arm it once the cooldown is over and they are still inside the 1.2 window (so break-outs can't chain into a shove-lock). It may cut short a wind-up that isn't armoured yet, and refills its poise. Break-Out Shove: violet telegraph 0.6, hyper armour from frame 0, active 0.12, recovery 0.35, dmg 30, poise 35, knockback 2.0, arc 160, reach 2.1, deflectable and blockable. If it lands, the next attack follows at once. `BreakOut.Enabled` switches it off.
**Crossbowman:** HP 70 · poise 20 · keeps 8-14 m away, backs off at 3.0 when you're within 5 m · never strays more than 2 m from where it spawned (LeashRadius), so the platform one stays on its 6 m block with room for its body (report 02 NEW-06) · Aimed Shot (startup 0.8, bolt 32 m/s, dmg 14) · Repeater Burst (startup 1.0, then 3 bolts 0.2 apart, dmg 7 each).
**Sparring Dummy:** unlimited health (refills after 3 s without hits) · optional swing every 2.5 s (startup 0.6, dmg 5).

---

## 3. Lore and IP guardrails

- **Ancient era only.** No lightning, and never call the deflect a "redirect" (lightning redirection is Iroh's invention). Fire-assisted dashing and kicks are canon firebending; full jet flight is left for a hidden master.
- **Ancient technology only:** dao swords and repeating crossbows are fine.
- **Names are data.** Move, enemy and element display names live in tuning assets. Class names stay generic (`MeleeEnemyBrain`, not a character name) so the world can be swapped without touching systems.

---

## 4. Architecture

### Two layers

1. **Core** (`Scripts/Core/`, assembly `VaatusRevenge.Core`, `noEngineReferences: true`): the *rules*, in plain C# with no Unity: combat state machines, frame data, input buffering, stamina, damage, Momentum, enemy AI decisions, camera maths, lock-on choice. Uses `System.Numerics` vectors. It's deterministic: the same inputs give the same result.
2. **Game** (`Scripts/**` except Core, assembly `VaatusRevenge.Game`): Unity *adapters*. They read input, feed the core, move `CharacterController`s, run hit queries, spawn effects and draw the HUD.

**Why:** the core runs without Unity, so the offline harness in `tools/` can compile it, unit-test it and **play it with bot inputs** (that's how the verification agent playtests), and so balancing can be checked automatically. It's also how bigger studios keep gameplay testable.

### Folders and owners

| Path (under `game/Assets/_Project/`) | Assembly | Owner |
|---|---|---|
| `Scripts/Core/Contracts/`, `Scripts/Core/Math/`, `Scripts/Common/` | Core / Game | Lead (shared contracts, already written) |
| `Scripts/Core/Combat/`, `Scripts/Core/Movement/`, `Scripts/Core/AI/`, `Scripts/Core/Tuning/` | Core | **core-engineer** |
| `Scripts/Core/Camera/`, `Scripts/Camera/`, `Scripts/Input/` | Core / Game | **camera-engineer** |
| `Scripts/Greybox/`, `Scripts/Bending/`, `Scripts/Combat/`, `Editor/Sandbox/ArenaBuilder.cs`, `Editor/Sandbox/GreyboxMaterials.cs` | Game / Editor | **greybox-engineer** |
| `Scripts/Player/` | Game | **player-engineer** (phase 2) |
| `Scripts/Enemies/` | Game | **enemy-engineer** (phase 2) |
| `Scripts/UI/`, `Scripts/Sandbox/`, `Editor/Sandbox/FireSandboxBuilder.cs` | Game / Editor | **integrator** (phase 2) |
| `Tests/EditMode/` | Tests | each owner adds `<Area>Tests.cs` files |
| `Tuning/`, `Scenes/`, `Materials/`, `Prefabs/` | assets | created by the sandbox builder menu in Unity |
| `tools/CombatSim/` (repo root) | offline | **verifier** |

Editor code uses the namespace `VaatusRevenge.EditorTools` (a namespace ending in `.Editor` would clash with Unity's `Editor` class).

### One frame

Script execution order via `[DefaultExecutionOrder(n)]`:

| Order | Component | Does |
|---|---|---|
| -200 | `TimeScaleController` | Advances hitstop / slow-mo timers in real time |
| -100 | `PlayerInputReader` | Samples devices into a `PlayerInputFrame` |
| -50 | `LockOnController` | Toggles / switches / breaks lock using the frame |
| 0 | `PlayerController` | Ticks the core, moves the `CharacterController` once, fires hits and effects |
| 10 | `EnemyController`, `TrainingDummy` | Tick their brains, move, attack |
| 20 | `FireProjectile` | Sweeps projectiles, applies hits |
| LateUpdate, 100 | `ThirdPersonCameraRig` | Follows the player after everything moved (no jitter) |
| OnGUI | `CombatHud`, health bars, reticle | Draws grey-box UI |

Input is read in `Update` only (presses are missed in `FixedUpdate`). No physics simulation is needed: no rigidbodies, `CharacterController`s move in `Update`.

---

## 5. Shared contracts (already in the repo)

Read these first; build on them rather than inventing parallel types.

- `Scripts/Core/Contracts/PlayerInputFrame.cs`: `ButtonState` (Held/Pressed/Released) and `PlayerInputFrame` (Move, Look, LookIsMouse, Light, Heavy, Dodge, Jump, Guard, Skill, Heal, LockOn, SwapShoulder (camera only), SwitchTargetDelta, ElementSelect).
- `Scripts/Core/Contracts/CombatContracts.cs`: `Team`, `ElementId`, `HitKind`, `Limb`, `DamageInfo`, `HitOutcome`, `HitResult`, `IDamageReceiver`, `CombatIds`.
- `Scripts/Core/Contracts/ProjectileSpec.cs`: speed, radius, range, gravity, explosion radius, visual scale.
- `Scripts/Core/Math/`: `Directions` (Unity-convention yaw/pitch maths, camera-relative movement, safe normalise), `Angles` (wrap, shortest delta), `Smooth` (SmoothDamp port, frame-rate independent smoothing), `HitGeometry` (arc test, projectile sweep vs fighter capsule).
- `Scripts/Common/`: `Combatant` (fighter registry: `Combatant.All`, `Combatant.Player`, Team, Radius, Height, AimPoint, Feet, Id, Receiver, IsAlive), `HitReport`, `Layers` (Player = 8, Enemy = 9, `EnvironmentMask`, `SetRecursively`), `NumericsExtensions` (`ToUnity()` / `ToNumerics()`).

Rules: a hit is **accepted or rejected by the defender** (`IDamageReceiver.ReceiveHit`), and the **attacker reacts to the returned result** (e.g. `Parried` means the attacker staggers itself). One `AttackId` hits each target at most once.

---

## 6. Team interfaces

Names and signatures below are requirements so phase-2 and phase-3 agents can build on phase-1 work. Internals are up to the owner. If you must change a public API listed here, keep it backward compatible and list the change at the top of your report.

### core-engineer (`Scripts/Core/Combat`, `Movement`, `AI`, `Tuning`)

- **Tuning data** (`[Serializable]` plain classes, public fields, defaults = the Fluid numbers above): `PlayerTuning`, `MoveData`, `DodgeProfile`, `ElementMoveSet` (light chain, heavy + charge/fa jin settings, sprint attack, jump attack, skill, dodge, guard/deflect), `EnemyTuning` (+ attack list with weights, ranges, cooldowns). Seed factories: `PlayerTuning.CreateFluid()`, `PlayerTuning.CreatePunishing()`, `ElementMoveSet.CreateFireFluid()`, `ElementMoveSet.CreateFirePunishing()`, `EnemyTuning.CreateDaoSoldier()`, `EnemyTuning.CreateCrossbowman()`, `EnemyTuning.CreateSparringDummy()`. `MoveData` carries a display name, `HitKind`, `Limb`, frame data, damage/poise/guard-stamina damage, range/arc/vertical reach, lunge, knockback, hitstop, combo and cancel windows, Momentum gain, parryable/unblockable, and an optional `ProjectileSpec`.
- **`PlayerCombatModel`**: the whole player rulebook, one instance per player. `Tick(float dt, in PlayerInputFrame input, in PlayerWorldState world)` returns a `PlayerTickResult`: desired velocity (x/z from locomotion, dodges and lunges; y from jump/gravity), desired facing yaw, and a list of **events** for the frame (dodge started, perfect dodge, attack started with its `MoveData`, attack active window opened/closed with an `AttackId`, projectile launch, heal applied, deflect success, charge sweet-spot reached, landed, died...). `PlayerWorldState` gives it: grounded, camera yaw, lock-on target (has target, position), soft-lock candidate, current position. Incoming hits: `HitResult ReceiveHit(in DamageInfo hit, Vector3 facing)` handles i-frames, perfect evade, guard arc, deflect window, poise, health, death. Attacker feedback: `OnAttackLanded(in HitResult result)` (Momentum, counter window) and `OnParried()`. Read-only state for the HUD and harness: Health, MaxHealth, Stamina, MaxStamina, Momentum, HealCharges, State, CurrentMove, phase and phase timer, IsInvulnerable, IsGuarding, buffered command, charge level. `ApplyTuning(PlayerTuning, ElementMoveSet)` swaps presets live. `Respawn()`.
- **Enemy brains**: `MeleeEnemyBrain` and `RangedEnemyBrain` (or one brain with archetype data), `Tick(float dt, in EnemyWorldState world)` → `EnemyTickResult` (desired velocity, facing yaw, events: telegraph/attack started, active window open/close with `AttackId`, projectile launch, stagger...). `ReceiveHit(in DamageInfo hit, Vector3 facing)`, `OnParried()`, `Reset()`. Shared `AttackTokenPool` limits simultaneous attackers. Seeded `DeterministicRandom` for choices so bot playtests are repeatable.
- Reusable pieces as you see fit: `Stamina`, `Health`, `Poise`, `Momentum`, `InputBuffer`, `TapHoldResolver`, `ActionTimeline` (frame data runner shared by player and enemies).
- Tests: `Tests/EditMode/Combat*Tests.cs`, `Movement*Tests.cs`, `EnemyAi*Tests.cs`.

### camera-engineer (`Scripts/Core/Camera`, `Scripts/Camera`, `Scripts/Input`)

- Core: `CameraTuning`, `LockOnTuning` (`[Serializable]`), `OrbitCameraModel` (free look + lock-on framing + over-the-shoulder offset and swap + combat pull-back + collision; optional `UpdateLift` / `UpdateShoulder` probe steps between `UpdateOrientation` and `UpdateDistance`), `LockOnSelector` (pick initial target, pick next left/right, should-break), `StickFlickDetector`. Tests in `Tests/EditMode/Camera*Tests.cs`: wrap-around, pitch clamps, target directly above/below or on top of the player (no NaN), switching picks the correct side, mouse vs stick scaling.
- `PlayerInputReader` (`[DefaultExecutionOrder(-100)]`): `static PlayerInputReader Instance`, `PlayerInputFrame Frame` (this frame's input), `bool UsingGamepad`, `void SetGameplayEnabled(bool enabled)` (paused = empty frames). Code-defined `InputActionMap` with the bindings in section 1 (no `.inputactions` asset, no `PlayerInput` component). Locks the cursor on click in the Game view, Esc releases it. Mouse wheel and Z/C feed `SwitchTargetDelta`; D-pad / 1-4 feed `ElementSelect`.
- `ThirdPersonCameraRig` (LateUpdate, order 100): `static ThirdPersonCameraRig Instance`, `float Yaw`, `float Pitch`, `Camera Camera`, `void SetFollowTarget(Transform target)`, `void SnapBehindTarget()`, `void SetFovBoost(float degrees)` (the player sets +5 while sprinting; eased), `void SwapShoulder()`, `int ShoulderSide`, and a `CameraTuningAsset` reference. Collision uses `Layers.EnvironmentMask`. Look input: stick × deltaTime × speed; **mouse delta is never multiplied by deltaTime**; uses unscaled time for input so the camera stays responsive during hitstop / slow-mo.
- `CameraShake` (static): `CameraShake.Add(float amplitude, float duration)`; the rig applies it.
- `LockOnController` (order -50): `static LockOnController Instance`, `Combatant Target`, `bool IsLocked`, `event System.Action<Combatant> TargetChanged`, `void ClearLock()`. Candidates from `Combatant.All` (Team.Enemy, alive), line of sight via `Layers.EnvironmentMask`. Draws the lock-on reticle (OnGUI) on the target's `AimPoint`.
- `CameraTuningAsset : ScriptableObject` holding `CameraTuning` and `LockOnTuning`.

### greybox-engineer (`Scripts/Greybox`, `Scripts/Bending`, `Scripts/Combat`, `Editor/Sandbox/ArenaBuilder.cs`, `Editor/Sandbox/GreyboxMaterials.cs`)

- `GreyboxRig : MonoBehaviour`: builds a primitive humanoid under its GameObject (body capsule, head, fists, feet, optional weapon box on the right hand) with **no colliders**. `void Build(Color bodyColor, bool withWeapon)`, `Transform GetAnchor(Limb limb)`, `void Strike(Limb limb, Vector3 localTarget, float extendTime, float holdTime, float retractTime)`, `void Lean(float forwardDegrees, float duration)`, `void SetTelegraph(Color color, float intensity)` (wind-up glow; intensity 0 = off), `void Flash(Color color, float duration)`, `void SetInvulnerableLook(bool on)` (i-frame flicker or tint, **no runtime transparency**), `void SetStaggered(bool on)`, `void SetDead(bool dead)`, `void SetCharge(float level01, bool sweetSpot)`.
- `FireVfx` (static): `Burst(Vector3 position, Vector3 direction, float scale)`, `Explosion(Vector3 position, float radius)`, `Ring(Vector3 center, float radius)` (landing ring), `FireVfxHandle Trail(Transform follow, float duration)`, `FireVfxHandle ChargeGlow(Transform anchor)` (handle has `Stop()` and `SetLevel(float)`), `HitSpark(Vector3 position, Color color)`, `Muzzle(Vector3 position, Vector3 direction)`. Pooled, cheap, emissive-coloured primitives plus a short-lived point light; bloom is already on in the URP volume.
- `TimeScaleController` (order -200, auto-creates itself): `static void Hitstop(float seconds)`, `static void SlowMotion(float seconds, float scale)`, `static void SetPaused(bool paused)`, `static void SetDebugSlowMotion(bool on)` (F2, 0.25x), `static bool IsHitstopActive`. It is the **only** code that writes `Time.timeScale`; overlapping requests resolve sensibly; timers run in real (unscaled) time.
- `MeleeHitQuery` (static): `int Arc(Vector3 origin, Vector3 forward, float range, float arcDegrees, float verticalReach, DamageInfo damage, List<HitReport> results)` and `int Sphere(Vector3 center, float radius, DamageInfo damage, List<HitReport> results)`. Candidates come from `Combatant.All` (skip same team as `damage.SourceTeam`, dead, already hit by `damage.AttackId`); geometry via `HitGeometry`; a line-of-sight ray against `Layers.EnvironmentMask` so hits don't go through walls; fills `Direction` and `Point` per target; calls `Receiver.ReceiveHit`; appends reports. `void EndAttack(int attackId)` clears the dedupe record.
- `FireProjectile` (order 20, pooled): `static void Launch(Vector3 origin, Vector3 direction, ProjectileSpec spec, DamageInfo damage, ProjectileVisual visual, System.Action<HitReport> onHit)`, with `ProjectileVisual { Fire, Bolt }`. Sweeps each frame: walls via `Physics.SphereCast` on `Layers.EnvironmentMask`, fighters via `HitGeometry.SweepSphereVsCapsule`; `ExplosionRadius` > 0 uses `MeleeHitQuery.Sphere`.
- Editor: `GreyboxMaterials.GetOrCreate(string name, Color color, bool emissive)` saves URP Lit materials in `Assets/_Project/Materials/`; `ArenaBuilder.Build(Transform parent)` returns an `ArenaLayout` with named spawn points (`Player`, `Dummy_1..3`, `Soldier_1..2`, `Crossbow_Ground`, `Crossbow_Platform`) and builds floor, walls, a pillar field, a low-ceiling corridor, a raised platform with ramp and stairs, and a duel ring marking. Geometry on the Default layer with colliders; no fighters.

### player-engineer (phase 2, `Scripts/Player`)

- `PlayerController : MonoBehaviour, IDamageReceiver` (requires `CharacterController`, `Combatant`, `GreyboxRig`): owns a `PlayerCombatModel`; each Update reads `PlayerInputReader.Instance.Frame`, camera yaw, lock target; ticks the model; one `CharacterController.Move`; turns to the facing yaw; turns events into hit queries, projectiles, effects, rig animation, hitstop, camera shake and gamepad rumble (always reset rumble on disable, death and quit). HUD getters (Health01, Stamina01, Momentum01, HealCharges, state and move names, debug info). `void Respawn(Vector3 position, float yaw)`, `void ApplyTuning(PlayerTuningAsset tuning, MoveSetAsset moveSet)`, `string PresetName`.
- `PlayerTuningAsset` and `MoveSetAsset` (`ScriptableObject`s with `[CreateAssetMenu]`, wrapping `PlayerTuning` / `ElementMoveSet`).
- **Factory:** `public static PlayerController Spawn(Transform parent, Vector3 position, float yaw, PlayerTuningAsset tuning, MoveSetAsset moveSet)` on `PlayerController`: builds the whole player GameObject (layer Player, `CharacterController` sized for a 1.8 m fighter with its pivot at the feet, `Combatant` as Team.Player with an aim point child at chest height, `GreyboxRig` built in the player colour). Works in edit mode (called by the sandbox builder, so it can't rely on Awake) and at runtime.
- Element select (RB + face button / 1-4): Fire is the only learned element; other directions show a "not learned yet" message (event or property the HUD reads).

### enemy-engineer (phase 2, `Scripts/Enemies`)

- `EnemyController : MonoBehaviour, IDamageReceiver` (requires `CharacterController`, `Combatant`, `GreyboxRig`): wraps the core brain for its `EnemyTuningAsset`; telegraphs every attack with `GreyboxRig.SetTelegraph` during startup (yellow for normal, red for heavy); attacks via `MeleeHitQuery` / `FireProjectile` (Bolt); stagger and death visuals; `void ResetEnemy()` back to its spawn point.
- `TrainingDummy : MonoBehaviour, IDamageReceiver`: refilling health, combo counter, DPS readout, optional rhythmic swing.
- `EnemyHealthBar` (OnGUI world-space bar + name, shows when damaged or targeted).
- `EnemyTuningAsset : ScriptableObject` wrapping `EnemyTuning`.
- **Factories:** `public static EnemyController Spawn(Transform parent, Vector3 position, float yaw, EnemyTuningAsset tuning)` on `EnemyController` and `public static TrainingDummy Spawn(Transform parent, Vector3 position, float yaw, EnemyTuningAsset tuning, bool swings)` on `TrainingDummy`: build the whole fighter (layer Enemy, `CharacterController`, `Combatant` as Team.Enemy with display name from the tuning, `GreyboxRig` in the enemy colour, weapon for soldiers). Work in edit mode and at runtime. Enemies remember their spawn point for `ResetEnemy()`.
- `static IReadOnlyList<EnemyController> EnemyController.All` (registered in OnEnable/OnDisable) so the director can reset everyone.

### integrator (phase 2, in parallel with player and enemy; `Scripts/UI`, `Scripts/Sandbox`, `Editor/Sandbox/FireSandboxBuilder.cs`)

Builds against the factory and HUD APIs above while they're being written, then does the final integration pass once they land.

- Menu **Vaatu's Revenge ▸ Build Fire Combat Sandbox**: creates missing tuning assets in `Assets/_Project/Tuning/` (Fluid and Punishing player + fire move sets, camera, three enemies) without overwriting edited ones, materials, the arena, player, camera rig, input reader, lock-on, enemies at the spawn points, HUD and director; saves `Assets/_Project/Scenes/FireCombatSandbox.unity`, adds it to Build Settings, opens it. Asks before replacing an existing scene. Also **Open Fire Combat Sandbox**.
- `CombatHud` (OnGUI): health, stamina, Momentum, heal charges, element, charge meter with the sweet-spot mark, target health, preset name, F1 controls overlay, F3 debug panel.
- `SandboxDirector`: the sandbox keys from section 1, death screen and auto-respawn.

---

## 7. Engineering rules

**Unity and C#**
- Unity **6000.6.3f1**, URP. **C# 9 at most**: block `namespace X { }` (no file-scoped namespaces), no `global using`, no records, no `init`, no `required`, no raw strings, no default interface methods. Nullable reference types off.
- **Input System only** (Active Input Handling = Input System Package). Never use `UnityEngine.Input` (it throws in this project) or the `PlayerInput` component. Only use Input System members that exist in `tools/UnityCompileCheck/InputSystemStub/InputSystemStub.cs`; they mirror the real API.
- **Banned:** `Rigidbody`, `Find*`/`FindObject*` scene searches (use serialized references, `Instance` singletons or `Combatant.All`), `GetInstanceID` (use `CombatIds`), `SendMessage`, `async void`, legacy text components. `tools/unity-lint.py` enforces these.
- A `MonoBehaviour`/`ScriptableObject` class lives in a file with **exactly its name**. One per file.
- Namespaces: `VaatusRevenge.Core` (core), `VaatusRevenge` or `VaatusRevenge.<Area>` (game), `VaatusRevenge.EditorTools` (editor), `VaatusRevenge.Tests` (tests).
- `GameObject.CreatePrimitive` adds a collider: **remove it** from anything that isn't solid world geometry (visuals, effects), or the camera and `CharacterController`s will bump into it. Use `Destroy` at runtime, `DestroyImmediate` in editor code.
- URP materials: shader `"Universal Render Pipeline/Lit"` (colour `_BaseColor`, emission `_EmissionColor` + keyword `_EMISSION`) or `"Universal Render Pipeline/Unlit"` (`_BaseColor`). Cache materials; don't create one per frame. Use `MaterialPropertyBlock` or shared materials for flashes.
- Use `Time.deltaTime` for gameplay, `Time.unscaledDeltaTime` for camera input, UI and time-scale timers. Guard `dt <= 0` (paused) everywhere.
- Every `Gamepad.SetMotorSpeeds` must have a matching reset (`ResetHaptics`) on stop, disable and quit.
- **Domain reload is off** in this project (Project Settings > Editor > Enter Play Mode Options: domain and scene reload disabled, for fast Play). Static fields keep their values from the previous Play session, so every static field (registries, `Instance`, caches, static events) must be reset in a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` method, and `Instance` must be cleared in `OnDisable`/`OnDestroy`.
- **All gameplay numbers in tuning data.** The only numbers allowed in code are defaults inside tuning classes and seed factories, maths constants and tiny epsilons.

**Style**
- Match the existing code: 4-space indent, braces on new lines, `[SerializeField] private` camelCase fields in components, PascalCase public members, public fields in `[Serializable]` data classes with `[Tooltip]`s where the meaning isn't obvious.
- Comments explain the **why** for first-time game developers (what i-frames, buffering, hitstop or coyote time are and why they matter), briefly. No commented-out code, no TODO without a reason.
- Small, readable classes. Prefer clarity over cleverness: David and Jeremy will read and tweak this.

**Git**
- Don't run `git commit`, `push`, `checkout`, `stash` or `reset`: the lead commits. Only edit files you own (plus additive, clearly reported changes to shared contracts when unavoidable). Don't create `.meta` files (the lead generates them).

---

## 8. Quality gates

Before reporting done, every builder runs:

1. `tools/compile-check.sh`: must print PASS for all four assemblies and the lint. (A clean offline compile is strong evidence, not proof: it uses Unity 2021.3 reference DLLs and an Input System stub.)
2. `tools/run-core-tests.sh`: all tests pass (core owners add tests for their rules).
3. A self-review of your diff against sections 4-7, looking for: NaN/zero-length vectors, division by zero, state that can get stuck, events that fire twice or never, missing null checks on `Instance` singletons, per-frame allocations in hot paths.

Report back with: files created/changed, the public API you provided (exact signatures), deviations from this spec and why, known gaps, and anything the next phase must know.

---

## 9. Phase 2 pinned API (player, enemies, integrator build against exactly this)

Phase 1 is done: core rules (`Scripts/Core/Combat`, `AI`, `Movement`, `Tuning`), camera/lock-on/input and grey-box/VFX/hit system are in the repo. Phase 2's three agents work at the same time, so these names are fixed. Add extra members freely; don't rename or drop these.

```csharp
// ---- Scripts/Player (player-engineer) ----
[CreateAssetMenu(menuName = "Vaatu's Revenge/Tuning/Player")]
public class PlayerTuningAsset : ScriptableObject
{
    public string PresetName = "Fluid";
    public PlayerTuning Tuning = PlayerTuning.CreateFluid();
    public PlayerFeedbackSettings Feedback = new PlayerFeedbackSettings(); // shake, rumble, flash (Unity-side feel data)
}

[CreateAssetMenu(menuName = "Vaatu's Revenge/Tuning/Move Set")]
public class MoveSetAsset : ScriptableObject
{
    public string PresetName = "Fluid";
    public ElementMoveSet MoveSet = ElementMoveSet.CreateFireFluid();
}

[DefaultExecutionOrder(0)]
public class PlayerController : MonoBehaviour, IDamageReceiver
{
    public static PlayerController Instance { get; }
    public static PlayerController Spawn(Transform parent, Vector3 position, float yaw, PlayerTuningAsset tuning, MoveSetAsset moveSet);
    public void Configure(PlayerTuningAsset tuning, MoveSetAsset moveSet);   // edit-time wiring
    public void ApplyTuning(PlayerTuningAsset tuning, MoveSetAsset moveSet); // live preset swap (F5/F6)
    public void Respawn(Vector3 position, float yaw);
    public event System.Action Died;
    public PlayerCombatModel Model { get; }          // null until the model exists
    public PlayerTuningAsset TuningAsset { get; }
    public MoveSetAsset MoveSetAsset { get; }
    public string PresetName { get; }
    public bool IsDead { get; }
    public float Health01 { get; }
    public float Stamina01 { get; }
    public float Momentum01 { get; }
    public float MomentumMultiplier { get; }
    public int HealCharges { get; }
    public int MaxHealCharges { get; }
    public bool IsCharging { get; }
    public float ChargeLevel01 { get; }
    public bool InSweetSpot { get; }
    public float SweetSpotStart01 { get; }
    public float SweetSpotEnd01 { get; }
    public ElementId CurrentElement { get; }
    public string ElementMessage { get; }            // "" unless a locked element was just picked (shown ~2 s)
    public string StateName { get; }
    public string MoveName { get; }
    public string DebugText { get; }                  // multi-line, for the F3 panel
}

// ---- Scripts/Enemies (enemy-engineer) ----
[CreateAssetMenu(menuName = "Vaatu's Revenge/Tuning/Enemy")]
public class EnemyTuningAsset : ScriptableObject { public EnemyTuning Tuning = EnemyTuning.CreateDaoSoldier(); }

[CreateAssetMenu(menuName = "Vaatu's Revenge/Tuning/Encounter")]
public class EncounterTuningAsset : ScriptableObject { public int MaxSimultaneousAttackers = 2; }

public class EnemyEncounter : MonoBehaviour            // one per scene; owns the shared AttackTokenPool
{
    public static EnemyEncounter Instance { get; }
    public AttackTokenPool Tokens { get; }
    public void Configure(EncounterTuningAsset tuning);
}

[DefaultExecutionOrder(10)]
public class EnemyController : MonoBehaviour, IDamageReceiver
{
    public static IReadOnlyList<EnemyController> All { get; }
    public static EnemyController Spawn(Transform parent, Vector3 position, float yaw, EnemyTuningAsset tuning);
    public void Configure(EnemyTuningAsset tuning);
    public void ResetEnemy();                         // back to its spawn point, full health, alive
    public EnemyTuningAsset TuningAsset { get; }
    public bool IsDead { get; }
    public float Health01 { get; }
    public string StateName { get; }
    public string DebugText { get; }
}

[DefaultExecutionOrder(10)]
public class TrainingDummy : MonoBehaviour, IDamageReceiver
{
    public static IReadOnlyList<TrainingDummy> All { get; }
    public static TrainingDummy Spawn(Transform parent, Vector3 position, float yaw, EnemyTuningAsset tuning, bool swings);
    public void Configure(EnemyTuningAsset tuning, bool swings);
    public void ResetDummy();
    public bool SwingEnabled { get; set; }
    public float Health01 { get; }
    public int ComboHits { get; }
    public float ComboDamage { get; }
    public float ComboDps { get; }
    public float LastHitDamage { get; }
}
// EnemyHealthBar (OnGUI name + bar above the head) is added by the Spawn factories.

// ---- Scripts/UI, Scripts/Sandbox, Editor/Sandbox (integrator) ----
public class CombatHud : MonoBehaviour { }              // OnGUI; reads PlayerController.Instance, LockOnController.Instance
public class SandboxDirector : MonoBehaviour
{
    public void Configure(PlayerTuningAsset fluidTuning, MoveSetAsset fluidMoves,
                          PlayerTuningAsset punishingTuning, MoveSetAsset punishingMoves, Transform playerSpawn);
}
```

Fighter conventions: all fighters are 1.8 m tall `CharacterController`s (radius 0.4, centre y 0.9, step offset 0.3 so the arena stairs work, slope limit 50) with their pivot at the feet, on the Player (8) or Enemy (9) layer. Colours: player warm orange-red; Dao Soldier steel blue-grey (with weapon); Crossbowman olive green; Sparring Dummy straw tan. Dummies are `Team.Enemy`. Enemy telegraph glow: yellow for normal attacks, red for heavy/delayed ones.
