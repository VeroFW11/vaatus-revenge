# Playtest Report 01: Fire combat prototype

- **Date:** 28 Sep 2026
- **Code measured:** game code at commit `a818bd2` (the over-the-shoulder camera commit; nothing under `game/` changed after it). Core fingerprint `sha1 61cf4fcb16`, `OrbitCameraModel.cs 59ceac1928`.
- **How:** a headless playtest harness, `tools/CombatSim/` (see its README). It runs the game's real combat core at 60 fps with scripted inputs and bots that have human reaction times. The Unity adapters were reviewed by reading them.
- **Honest limit:** no Unity Editor was available, so **nothing in this report was seen running in Unity**. Everything below is either measured from the real rules code or found by reading the Unity code. Section 2 says which is which.

---

## 1. Verdict

1. **No blockers found.** The combat core ran 1.2 million fuzzed frames and 2,400 bot duels with zero rule violations (nothing stuck, no NaN, every hitbox and attack token released). Its timings match the spec's numbers.
2. **Fluid should feel responsive and fair against one or two soldiers.** The jab lands in 8 frames, the dodge starts on the press, and every soldier attack can be dodged on reaction. The over-the-shoulder camera also measured clean: no jitter, never inside a wall, and the locked target was always on screen.
3. **It is too easy to win without learning anything.** Mashing light beats one or two Dao Soldiers 100% of the time, and jump-then-plunge spam is the strongest attack in the game, hitting whole groups at once.
4. **Groups with crossbowmen go the other way.** Crossbowmen ignore the two-attacker limit, so in Punishing even frame-perfect dodging still takes 1.8 hits a minute (3.6 against the full sandbox ring).
5. **Recommendation:** keep **Fluid** as the default, and fix the six Major issues before the first human playtest (most are data changes or small rule changes). Treat all of this as unconfirmed in Unity until the checklist in section 6 has been played through.

---

## 2. What I could and couldn't verify

| Area | How it was checked | Confidence |
|---|---|---|
| Combat rules: player moves, dodge, guard/deflect, buffer, stamina, poise, Momentum, fa jin, enemy brains, attack tokens | **Ran the real code.** CombatSim compiles the same `Scripts/Core/` files Unity compiles and drives them frame by frame | **High.** These numbers are the game's own rules |
| Frame loop: script order, hitstop and slow motion, `deltaTime` | Copied into the harness from the adapters and the spec (hitstop 0.02×, slowest slow-mo wins, deltaTime clamp) | Medium-high (it's a copy, not Unity) |
| Hit detection, movement and the arena | Hand-written stand-ins: box geometry copied from `ArenaBuilder`, a simple capsule instead of the `CharacterController` | Medium. Good for reach and timing. Says nothing about slopes, snagging or physics quirks |
| Camera and lock-on | The real `OrbitCameraModel`, `LockOnSelector` and `StickFlickDetector`, plus a copy of the rig's wall probes in the same order | Medium-high for the maths. How it *looks* is unverified |
| Unity adapters (player, enemies, input, camera rig, HUD, VFX, sandbox builder) | Read line by line against a checklist (below). `tools/compile-check.sh` passes | Medium. Reading finds logic bugs, not engine behaviour |
| Visuals, VFX, bloom, animation, sound, rumble, screen shake, HUD legibility | **Not verified** | None. Needs a person in the Editor |
| Real devices and performance: pad dead zones, mouse feel, frame pacing, garbage collection, Unity 6000.6 differences, Jeremy's PC | **Not verified** (code paths read only) | None to low |

**Camera: which behaviour was measured.** All camera numbers come from the over-the-shoulder camera at `a818bd2`: shoulder 0.55 m (0.35 m locked on), distance 3.2 m / 4.0 m, combat pull-back 0.9 m, automatic shoulder swap. Rows marked "centred" use `CameraTuning.CreateCentred()` in the same code, which the camera's author says reproduces the first prototype's framing. I did not run the older camera code itself.

**Volume of testing.**
- Frame data and controls traces for every move.
- 1.2 M fuzzed frames: 6.25 hours of game time at 30, 60 and 144 fps, with hitches and paused frames.
- 2,400 bot duels: 6 play styles × 5 enemy groups × 40 seeds × 2 presets.
- 200 "oracle" minutes (a bot that dodges frame-perfectly).
- The camera scenarios.

Every run is repeatable. A repeated sweep gave identical results for all 48 bot/group/preset rows that the two sweeps shared.

**Recorded sessions** (`tools/CombatSim/replays/`: per-frame positions, states, camera and events as JSON; the README gives the command that re-creates each one):

| Replay | What it shows |
|---|---|
| `punishing-oracle-vs-2-soldiers-crossbow.json` | Frame-perfect dodging still takes 3 hits in 60 s. All three land 20–22 frames into a dodge, after its i-frames, with 2–3 enemies attacking (ENEMY-01) |
| `fluid-anticipate-vs-sandbox-ring.json` | The whole sandbox ring in Fluid: a 49 s win with 6 perfect dodges, 5 counters and Momentum reaching 100. All 8 hits taken were crossbow bolts |
| `fluid-masher-vs-soldier.json` | Mashing light with no defence kills a Dao Soldier in 4.0 s and takes one hit (ENEMY-02) |
| `platform-crossbowman-walks-off.json` | The platform crossbowman steps off its 2.5 m block 1.4 s after it notices the player (ENEMY-03) |

### Terms used

| Term | Meaning |
|---|---|
| Frame (f) | 1/60 s = 16.7 ms. Everything was measured at 60 fps |
| Startup / active / recovery | Before a move can hit / while it can hit / after, when you're committed and open |
| Cancel | Cutting a move's recovery short with another move (e.g. dodge-cancelling a jab) |
| Input buffer | A button pressed while you're busy is remembered briefly (0.25 s) and happens as soon as it can, so presses aren't lost |
| I-frames | "Invincibility frames": the part of a dodge where hits pass through you |
| Perfect dodge | A hit that arrives in the first 0.12 s of a dodge. Fluid only: slow motion, Momentum and a counter window |
| Deflect | Pressing guard within 0.15 s before a hit lands. It costs nothing and staggers the attacker |
| Poise / stagger | Hidden toughness that hits wear down. At zero the fighter is stunned (staggered) |
| Stagger immunity | After a soldier's stagger ends, it can't be staggered again for 1.5 s |
| Hitstop | A tiny freeze on impact that makes hits feel solid |
| Telegraph | An enemy's visible wind-up (the weapon glows) before it strikes |
| Attack tokens | A shared limit on how many enemies may attack at once (2) |
| Fa jin | The heavy's timing reward: release inside 0.70–0.90 s of holding for 1.8× damage and 2× poise damage |
| Momentum | Fire's meter: landing hits fills it, and damage scales ×1.0 to ×1.4 |
| Oracle bot | A bot that knows every attack's timing and dodges frame-perfectly. A hit it still takes can't be avoided by dodging alone |

---

## 3. Is it fun?

What follows is inferred from measurements and bot fights. Whether it *feels* fun is for David and Jeremy to judge by playing, and section 6 lists what to look for.

### What should feel good

| Feel | Evidence |
|---|---|
| **Responsive.** Buttons do things now | Jab hits in 8 f (0.13 s). Flame Step starts on the press frame and is invulnerable from frame 2. Attacks cancel into a dodge from frame 14 |
| **Readable enemies** | A person reacting to the wind-up (0.25–0.35 s reaction) dodges 98–100% of every Dao Soldier attack, sideways or back. The Delayed Thrust gets the longest wind-up (1.15 s) so it can catch panic dodges |
| **The Spider-Man loop exists** | In Fluid, perfect dodges happen in real fights: 21 of 51 dodges for the anticipating bot vs one soldier, 140 of 747 vs the full ring. Each one gives slow-mo, +25 Momentum and a ×1.5 counter |
| **Guard is a real option** | The guard bot deflected 48 times over 40 fights vs one soldier and took 5 damage on average. It blocked every bolt from a lone crossbowman (0 damage) |
| **Momentum builds visibly** | Full in 4.9 s of chaining (damage ×1.4). It starts draining 1.6 s after your last hit |
| **Camera stays out of the way** | 10 fights × 60 s vs 2 soldiers + a crossbowman: target never off-screen, 99% of frames turn less than 7.2°, 0.1 direction reversals per second. In the wall tests it never ended up inside geometry |

### What will get in the way

| Problem | Evidence | Bug |
|---|---|---|
| **You can win without defending** | Mashing light wins 100% vs one or two soldiers in both presets (Fluid: 3.7 s and 7.1 s). A soldier dies in about 4 s | ENEMY-02 |
| **One move beats everything** | Jump + light plunges from 0.14 m up and loops every 0.47 s: 424 damage in 10 s on one target, 1,273 on three (light mashing: 309) | ABIL-01 |
| **Groups with crossbowmen feel cheap** | Up to 4 attackers at once. Frame-perfect dodging takes 0.35–0.7 hits/min in Fluid and 1.8–3.6 in Punishing. Bots win 5–18% vs the full ring in Punishing | ENEMY-01 |
| **Fa jin is harder than it looks** | The on-screen advice "release on the flash" gives 3–42% success at typical reaction times (0.20–0.25 s). After a perfect dodge, the practised 0.8 s hold gives a Partial | HUD-01, ABIL-03, ABIL-04 |
| **The perfect dodge has a hidden rule** | It only counts if you dash through or beside the swing. A well-timed dodge backwards never counts, and the spec says it should | ABIL-02 |
| **Stamina barely matters in Fluid** | From an empty bar, heavies keep 100% and Fire Blast 95% of their full-stamina rate, because any stamina above 0 starts a move and regen restarts after 0.45 s | Tuning (section 5) |

### Fluid or Punishing?

| | Fluid | Punishing |
|---|---|---|
| Dodge starts | on press (0 f) | on release (3–13 f after the press) |
| Dodge i-frames / whole dodge | 13 f / 18 f | 15 f / 29 f |
| Dodge-mashing: share of time invulnerable | 61% | 38% |
| Perfect-dodge reward | yes | off (by design) |
| Frame-perfect dodging, 2 soldiers + crossbowman: hits/min | 0.35 | 1.80 |
| ...vs the full sandbox ring (2 soldiers + 2 crossbowmen) | 0.70 | 3.60 |
| Bot win rates vs 2 soldiers + crossbowman | 68–100% | 35–50% |
| Bot win rates vs the full ring | 25–78% | 5–18% |
| Mashing vs 2 soldiers | 100% win, like every other style | 100% win: *better* than every other style (88–98%) |
| Time at zero stamina, masher and aggressive bots (per minute) | 0–13 s | 19–37 s |

**Recommendation: Fluid as the default for the vertical slice** (it's also the Quest Board's pick).
- It's the only preset with the perfect-dodge loop, and that loop is the "fluid, Spider-Man-2-like" feel the project asked for.
- It stays fair against groups.
- Punishing, as tuned today, rewards mashing over defending against two soldiers.
- Most of Punishing's unfairness comes from ENEMY-01, so compare the presets again after that fix.

Jeremy's Elden Ring instinct that stamina should bite is right, and Fluid's stamina barely does. The suggestion in section 5 borrows Punishing's longer regen pause for Fluid, rather than switching presets.

### Does it feel like Northern Shaolin?

Northern Shaolin is long-range: extended strikes, high and jumping kicks, fast footwork, explosive power. The numbers express some of that well:
- **Reach and kicks.** The Dragon Tail Kick reaches 3.0 m in a 200° arc. The Flying Fire Kick lunges 3.2 m, and the plunge is a jumping axe kick.
- **Forward pressure, partly by accident.** Perfect dodges only come from dashing *into* or beside attacks (6 f window toward, 0 f backwards). Momentum drains when you back off while locked on. "Advance, don't retreat" is a strong identity for Fire. It should be a deliberate choice that the player is told about, not a side effect (ABIL-02).
- **Explosive release.** Fa jin as a timing skill is exactly the right idea. It needs the timing problems fixed (HUD-01, ABIL-03, ABIL-04).

Where it doesn't express the style yet:
- **The plunge crowds out everything else.** It currently beats every ground technique, so the acrobatics replace the martial arts instead of expressing them (ABIL-01).
- **The light chain plays as punch-punch-kick at 2.6–3.0 m.** Spacing barely matters, because mashing wins at any range (ENEMY-02). Once soldiers can punish mashing, the long kicks can become a spacing tool, which is where Northern Shaolin's identity would show.
- **Backing off costs Momentum only while locked on.** Unlocked retreat keeps it, which undercuts the forward-pressure idea. The spec says "while locked on", so this is a design decision to revisit, not a bug.

---

## 4. Bugs

Severity:
- **Blocker:** can't test at all.
- **Major:** breaks a stated purpose of the prototype, or makes a core system feel wrong. Fix before the human playtest.
- **Minor:** noticeable, but with a workaround or small impact.
- **Polish:** nice to have.

"Measured" = reproduced in CombatSim. "Read" = found by reading code. "Suspected" = likely, but needs the Editor to confirm.

| ID | Severity | Status | Title | Main location |
|---|---|---|---|---|
| ENEMY-01 | Major | Measured | Crossbowmen ignore the attack-token limit, so 3–4 enemies attack at once | `Core/Tuning/EnemyTuning.cs:84` |
| ENEMY-02 | Major | Measured | Dao Soldiers don't punish mashing (balance) | `Core/Tuning/EnemyTuning.cs:18-25` |
| ENEMY-03 | Major | Measured | The platform crossbowman walks off its platform, so the spec's camera-pitch test disappears | `Core/AI/RangedEnemyBrain.cs:55-59` |
| ABIL-01 | Major | Measured | A plunge works 1 frame after take-off, so jump-plunge spam is the best damage in the game | `Core/Combat/PlayerCombatModel.Actions.cs:138-143` |
| ABIL-02 | Major | Measured | A perfect dodge only counts if the swing still reaches you mid-dash, so dodging back never counts | `Core/Combat/PlayerCombatModel.Defense.cs:202-218` |
| HUD-01 | Major | Measured | "Release on the flash" misses the fa jin window at normal human reaction times | `Scripts/UI/HudControlsOverlay.cs:121` |
| ABIL-03 | Minor | Measured | After a perfect dodge the charge counts slowed time, so the practised 0.8 s hold gives a Partial | `Core/Combat/PlayerCombatModel.Actions.cs:296-307` |
| ABIL-04 | Minor | Measured | Heavy held during another move: the charge starts at the cancel point, not at the press | `Core/Combat/PlayerCombatModel.Actions.cs:277-294` |
| CTRL-01 | Minor | Measured | A later attack press erases a buffered dodge | `Core/Combat/InputBuffer.cs:20-28` |
| CTRL-02 | Minor | Measured | A queued chain press made at zero stamina can fire up to ~0.5 s later | `Core/Combat/InputBuffer.cs:30-33` |
| CTRL-03 | Minor | Measured | No grace period for the sprint attack | `Core/Combat/PlayerCombatModel.Actions.cs:104` |
| BUILD-01 | Minor | Suspected | The sandbox scene probably renders without bloom, though the VFX and the spec rely on it | `Editor/Sandbox/FireSandboxBuilder.cs:139-140` |
| ABIL-05 | Polish | Measured | Fire Blast doesn't lead moving targets (20–27% hits on a strafing archer at 14 m or more) | `Core/Combat/PlayerCombatModel.Actions.cs:242-257` |
| CTRL-04 | Polish | Read | Shoulder swap on L3 (left-stick click) is easy to press by accident while sprinting | `Scripts/Input/PlayerInputReader.cs:316` |
| CAM-01 | Polish | Measured | 18° one-frame camera swing when a locked target passes over the player | `Core/Camera/OrbitCameraModel.cs:356-368` |
| CAM-02 | Polish | Measured | Instant camera pull-in when a pillar passes between camera and player (by design, can pop) | `Core/Camera/OrbitCameraModel.cs:464-475` |

**Counts:** Blocker 0 · Major 6 · Minor 6 · Polish 4. Paths are relative to `game/Assets/_Project/` unless they start with `game/` or `docs/`. `Core/` means `Scripts/Core/`.

---

### ENEMY-01: Crossbowmen ignore the attack-token limit

**Major · Measured**

- **Where:** `Scripts/Core/Tuning/EnemyTuning.cs:84` sets `UsesAttackToken = false` for the Crossbowman. As a result `Scripts/Core/AI/EnemyBrain.cs:286` (`CanUseTokenNow`) is always true for it, and `RangedEnemyBrain.cs:35` shoots whenever its timer allows.
- **What happens:** The token pool exists so that "only a few attack at once ... dangerous but fair" (`AttackTokenPool.cs:5-9`, spec: "Two of them test that only a limited number attack at once"). Soldiers respect it and crossbowmen don't. So the sandbox ring can have both soldiers swinging while both crossbowmen shoot (measured maximum: 4 attackers at once).
- **Evidence:**

  | Group (oracle bot, 20 × 60 s) | Fluid hits/min | Punishing hits/min |
  |---|---|---|
  | 2 soldiers | 0.00 | 0.00 |
  | 1 crossbowman | 0.05 | 0.40 |
  | 2 soldiers + crossbowman | 0.35 | 1.80 |
  | Full ring (2 soldiers + 2 crossbowmen) | 0.70 | 3.60 |

  - In Punishing against 2 soldiers + a crossbowman, 25 of the 36 hits landed 18–29 frames into a dodge: after its i-frames end (frame 17) but before a new dodge is allowed (frame 29). 33 of the 36 came while 2–3 enemies were attacking at once.
  - Bots win 88–100% against two soldiers alone, but 5–18% against the full ring in Punishing (25–78% in Fluid).
  - Replay: `punishing-oracle-vs-2-soldiers-crossbow.json`.
  - Bolts are counterable on their own: the guard bot took 0 damage from a lone crossbowman. The unfairness comes from stacking.
- **Fix direction:**
  - Let ranged enemies take tokens (`UsesAttackToken = true`), or give them their own small pool (one archer shooting at a time, a number in the encounter tuning).
  - And/or have archers hold fire while both melee tokens are out and the player is within a few metres of a soldier.
  - Re-run `dotnet run --project tools/CombatSim -- fairness` afterwards: Punishing's unavoidable rate should drop toward the two-soldier row.

### ENEMY-02: Dao Soldiers don't punish mashing (balance)

**Major · Measured**

- **Where:** Dao Soldier numbers in `Scripts/Core/Tuning/EnemyTuning.cs:18-25`: health 110, poise 35, stagger 1.0 s, stagger immunity 1.5 s. The light chain is in `Scripts/Core/Tuning/ElementMoveSet.cs:60-90`: 8 / 9 / 15 damage and 8 / 9 / 22 poise.
- **What happens:** A player who only presses light, never dodges and never guards wins every fight against one or two soldiers, in both presets. One jab-cross-kick chain does 39 poise damage against the soldier's 35, so the first chain always staggers. The soldier is usually dead before its stagger immunity matters.
- **Evidence:** masher bot, 40 seeds each:

  | | 1 soldier | 2 soldiers |
  |---|---|---|
  | Fluid | 100% win, 3.7 s, 18 damage taken | 100% win, 7.1 s, 45 damage taken |
  | Punishing | 100% win, 4.7 s, 19 damage taken | 100% win, 12.2 s, 68 damage taken |

  - In Punishing, mashing beats two soldiers more reliably than the defensive bots do: react 88%, anticipate 90%, guard 95%.
  - Against a relentless player the soldier still swings 5–6 times in 20 s, so stagger immunity works as designed. It just rarely gets the chance.
  - Replay: `fluid-masher-vs-soldier.json`.
- **Fix direction:** mostly data (section 5): more health and poise so one chain doesn't stagger from full. Optionally, give the Quick Slash hyper armour from part-way through its wind-up, so mashing into a telegraph gets you hit.

### ENEMY-03: The platform crossbowman walks off its platform

**Major · Measured**

- **Where:** `Scripts/Core/AI/RangedEnemyBrain.cs:55-59` closes in whenever the player is beyond `KeepAwayMax` (14 m) or hidden, with no leash to its spawn. `Scripts/Enemies/EnemyFighter.cs:315-323` (`ApplyMotion`) moves the CharacterController wherever the brain asks, with no ledge check. The spawn is at `Editor/Sandbox/ArenaBuilder.cs:210`.
- **What happens:** The spec says "One stands on the raised platform to test camera pitch" (`docs/Prototype/Fire-Combat-Prototype-Spec.md:53`). Its aggro range is 24 m and its preferred band 8–14 m. From the duel ring (about 18 m away) it walks toward the player and off the edge. When the player is 9–11 m away, it strafes sideways and drifts off the 6 × 6 m block.
- **Evidence:**
  - The fairness scenario put the player at 4 spots and the crossbowman left the platform from 3 of them within 20 s. It only stays if the player stays at the spawn, out of aggro range.
  - Replay `platform-crossbowman-walks-off.json`: it steps off 1.4 s after noticing the player, drops 2.5 m, then shoots from the ground.
- **Fix direction:**
  - Add a data option for ranged spawns (e.g. `HoldPosition` or `LeashRadius` in `EnemyTuning`) so it strafes around its spawn point.
  - Add a ledge check in `EnemyFighter.ApplyMotion`: probe the ground a little ahead and refuse steps that drop more than the step offset. Every future enemy benefits.

### ABIL-01: Jump-plunge spam

**Major · Measured**

- **Where:** `Scripts/Core/Combat/PlayerCombatModel.Actions.cs:88-92` (light while airborne becomes a plunge) and `:138-143` (`TryPlunge` only checks stamina). `Scripts/Core/Tuning/PlungeSettings.cs:7-13` has no minimum height or air time. The Falling Axe Kick data is in `ElementMoveSet.cs:117-128`: 18 damage, 25 poise, a 2.2 m ring all around, 0.35 s landing recovery, 14 stamina.
- **What happens:** Jump, then light on the very next frame, gives a full plunge from 0.14 m up. It hits everything within 2.6 m (centre to centre) all around you, and you're free again 28 frames after the jump. Repeated, it's the best damage in the game, and it hits whole groups.
- **Evidence:**
  - Controls table: plunge possible from 1 f after the jump, landing ring 7 f later, free at 28 f.
  - 10 s against planted dummies: light mashing 309 damage; plunge spam 424 on one target and 1,273 on three. Damage per stamina 3.35 vs 2.79.
- **Fix direction:** add a minimum height or air time to `PlungeSettings` (e.g. `MinHeight` 1.0 m, or `MinAirTime` 0.25 s); below it, a light in the air does nothing or a small air kick. Give the plunge a longer landing recovery (section 5). The jumping axe kick is good Northern Shaolin; it just needs to be a commitment.

### ABIL-02: A perfect dodge only counts if the swing still reaches you

**Major · Measured**

- **Where:** `Scripts/Core/Combat/PlayerCombatModel.Defense.cs:202-218` (`Evade()`, reached from `:150` only when a hit arrives during the i-frames). A hit only arrives if the attack's arc overlaps the player on that frame (`Scripts/Combat/MeleeHitQuery.cs:58-59`).
- **What happens:** The spec (`Fire-Combat-Prototype-Spec.md:44`) says a perfect dodge is when "the hit would have landed in the first moments of the dash". The code only counts it if the swing actually touches you mid-dash. A 4.2 m Flame Step carries you out of reach before the swing's first active frame, so a well-timed dodge *away* never counts, while sideways or through does. Players won't know that rule.
- **Evidence:**
  - Perfect-dodge window measured per Dao Soldier attack, Fluid, player standing where the soldier starts the attack:

    | Dodge direction | Window | Spec |
    |---|---|---|
    | Back | 0 f (Delayed Thrust: 1 f) | 7 f (0.12 s) |
    | Side | 4 f (67 ms) | 7 f (0.12 s) |
    | Toward | 6 f (100 ms) | 7 f (0.12 s) |

  - Dodges made on reaction to the wind-up avoid 98–100% of hits, but only 0–2% are perfect.
- **Fix direction:** decide which rule you want. Both are defensible, and "dash into the attack" suits Fire's forward pressure.
  - **To keep the spec's rule:** when a dodge starts, remember any enemy strike that is due within `PerfectWindow` and whose arc covers the player's starting position. If that strike's active frame comes during the i-frames, award the perfect dodge even if the arc no longer reaches.
  - **To keep the current rule:** update the spec, tell the player in the controls text, and consider a longer window.

### HUD-01: "Release on the flash" misses the fa jin window

**Major · Measured**

- **Where:** the overlay text is at `Scripts/UI/HudControlsOverlay.cs:121`: "Heavy attack (hold, release on the flash)". The flash starts exactly when the sweet spot opens (`Scripts/Core/Combat/PlayerCombatModel.Actions.cs:300-304`; window 0.70–0.90 s in `Scripts/Core/Tuning/ChargeSettings.cs:17-18`). The HUD meter flashes only inside the window (`Scripts/UI/CombatHud.cs:284-291`).
- **What happens:** The window is 0.2 s wide and the flash marks its start. A person needs about 0.2–0.25 s to see a flash and let go, so reacting to it lands at the end of the window or after it.
- **Evidence:** 1,000 simulated releases per row:

  | Strategy | Fa jin success |
  |---|---|
  | React to the flash, 0.17 s reaction | 76% |
  | React to the flash, 0.20 s reaction | 42% |
  | React to the flash, 0.25 s reaction | 3% |
  | Time the hold as a rhythm (aim 0.8 s), error σ 0.05 s | 97% |
  | Time the hold as a rhythm (aim 0.8 s), error σ 0.08 s | 85% |

  The flash rows assume only 1 frame of display lag. Unity's real display lag is higher, so the true numbers are worse.
- **Fix direction:**
  - Teach the rhythm instead: e.g. "hold, and let go as the meter fills the gold band".
  - And/or add a "get ready" cue about 0.25 s before the window (a new `ChargeSettings` field).
  - Or put the flash in the middle of a wider window.
  - Test with people (checklist item 5).

### ABIL-03: Fa jin timing shifts after a perfect dodge

**Minor · Measured**

- **Where:** `Scripts/Core/Combat/PlayerCombatModel.Actions.cs:296-307`. `chargeTime = action.Time` advances with game time, and the slow motion is applied in `Scripts/Player/PlayerController.cs:495-497`.
- **What happens:** A perfect dodge slows time to 0.35× for 0.35 s (real time). The charge clock runs on slowed game time, so a heavy started right after a perfect dodge reaches the sweet spot about 0.23 s later in real time than the player's practised rhythm. That's exactly the moment the game wants you to counter.
- **Evidence:**

  | Heavy held for (real time) | Starting from idle | Starting right after a perfect dodge |
  |---|---|---|
  | 0.8 s | FaJin | Partial |
  | 0.9 s | FaJin | Partial |
  | 1.0 s | Charged | FaJin |
  | 1.1 s | Charged | FaJin |

- **Fix direction:** either run the charge clock on real time (the core would need the unscaled time passed in), or keep the player at normal speed during the slow motion and slow only the enemies (which also sells "the Avatar is fast"). It's Minor because the HUD meter still shows the band, so an attentive player can adapt.

### ABIL-04: Heavy held during another move charges late

**Minor · Measured**

- **Where:** `Scripts/Core/Combat/PlayerCombatModel.Actions.cs:277-294`. `StartCharge` resets `chargeTime` to 0 when the buffered heavy finally runs. The press was buffered at `PlayerCombatModel.cs:239` while the jab played.
- **What happens:** If you press and hold heavy during a jab, the charge only starts at the jab's cancel point (frame 15), so the time you already held doesn't count.
- **Evidence:** holding 0.8 s from the press:

  | Heavy pressed | Charge at release | Result |
  |---|---|---|
  | From idle | 0.800 s | FaJin |
  | At frame 4 of a jab | 0.617 s | Partial |
  | At frame 10 of a jab | 0.717 s | FaJin (barely) |

- **Fix direction:** when a buffered heavy starts while the button is still held, begin the charge at "now minus the press time", capped so it can't jump past the window. Or show the charge meter from the press.

### CTRL-01: A later attack press erases a buffered dodge

**Minor · Measured**

- **Where:** `Scripts/Core/Combat/InputBuffer.cs:20-28`: `Push` replaces whatever is buffered, except a queued light. The code comment at `PlayerCombatModel.cs:234-235` gives the dodge priority only when two buttons go down in the *same frame*.
- **What happens:** The buffer keeps one command, the latest press. Pressing dodge during a jab and then light again, a common nervous double-tap, replaces the dodge with an attack. That's the dangerous direction: the player asked to get out and the game attacks instead.
- **Evidence:**
  - Light at f0, Dodge at f5, Light at f8 → Flame Jab at 0, Flame Cross at 15, no dodge.
  - Light at f0, Dodge at f10, Heavy at f12 → the heavy runs, no dodge.
- **Fix direction:** let a buffered dodge (and guard) survive later attack presses inside the buffer window, while attacks still replace attacks. It's a one-line rule in `InputBuffer.Push` plus a test. "Last press wins" is also a legitimate choice (it's the Elden Ring convention); decide, then write it into the spec.

### CTRL-02: A queued chain press can fire up to ~0.5 s late

**Minor · Measured**

- **Where:** `Scripts/Core/Combat/InputBuffer.cs:30-33` (locked presses never expire) and `Scripts/Core/Combat/PlayerCombatModel.Actions.cs:99-102` (a queued chain move waits for stamina above 0).
- **What happens:** Once a light press has been accepted as the next chain move, it stays queued until stamina rises above zero. That can be half a second later, and it then fires even if the player has moved on.
- **Evidence:** in the 1.2 M fuzzed frames, the oldest press executed in each run was 0.34–0.535 s old (the buffer window is 0.25 s Fluid / 0.20 s Punishing), and all of those were light presses. The `buffer` scenario shows a queued press at zero stamina firing when regen resumes.
- **Fix direction:** expire queued presses too after a cap (e.g. 0.35 s), or drop the queued chain move if stamina is empty at the cancel point.

### CTRL-03: No grace period for the sprint attack

**Minor · Measured**

- **Where:** `Scripts/Core/Combat/PlayerCombatModel.Actions.cs:104`: only in the Sprinting state, after 0.3 s of sprinting.
- **What happens:** Letting go of sprint one frame before pressing light gives a standing Flame Jab while your body is still moving at 5.9 m/s.
- **Evidence:** the `controls` table. Sprint released 1 f or 5 f before light → Flame Jab in both presets, at 5.87 m/s.
- **Fix direction:** allow the sprint attack for about 0.15 s after sprinting ends while speed is still above run speed (a new `ElementMoveSet` field). On a pad you usually keep holding B while pressing R1, so this mostly affects keyboard players.

### BUILD-01: The sandbox probably renders without bloom

**Minor · Suspected**

- **Where:**
  - `Editor/Sandbox/FireSandboxBuilder.cs:139-140` makes a new scene with Unity's default objects and never adds a Volume.
  - `game/Assets/Settings/DefaultVolumeProfile.asset:367-369` has Bloom intensity 0.
  - The builder never turns on post-processing for the camera, and URP creates cameras with it off.
  - The spec says "bloom is already on in the URP volume" (`Fire-Combat-Prototype-Spec.md:198`), and the art expects it: `Scripts/Combat/FireProjectile.cs:389` ("HDR so the trail blooms"), `Scripts/Greybox/GreyboxRig.cs:180`.
- **What happens (suspected):** fire effects and telegraph glows render without their halo. They'll still be bright, just flatter and less readable. Only `SampleScene` has a Volume with bloom (0.25).
- **Evidence:** reading only; I couldn't open the scene.
- **Fix direction:** check first (checklist item 2). If confirmed, have the builder add a global Volume with a bloom profile and enable post-processing on the camera.

### ABIL-05: Fire Blast doesn't lead moving targets

**Polish · Measured**

- **Where:** `Scripts/Core/Combat/PlayerCombatModel.Actions.cs:242-257`: the blast flies along your facing, angled up or down toward the target's chest.
- **Evidence:** vs a crossbowman strafing at 1.6 m/s: 100% hits at 6–10 m, 27% at 14 m, 23% at 20 m, 20% at 25 m. A standing target is hit 100% at every range. The blast flies at 30 m/s.
- **Fix direction:** decide whether Fire Blast should work at long range. If yes, aim where the target will be, add slight homing, or speed it up (e.g. 45 m/s). If the lesson is "close the distance" (spec: the crossbowman "tests ... closing distance"), leave it.

### CTRL-04: Shoulder swap on L3

**Polish · Read**

- **Where:** `Scripts/Input/PlayerInputReader.cs:316`.
- **What happens:** L3 (clicking the left stick) is easy to press by accident when pushing the stick hard while sprinting or panic-dodging, and every click swaps the camera side.
- **Evidence:** reasoning only; this needs a real pad (checklist item 12).
- **Fix direction:** move it to a d-pad direction, require a short hold, or add an option to turn it off.

### CAM-01: Fast camera swing when a target passes overhead

**Polish · Measured**

- **Where:** `Scripts/Core/Camera/OrbitCameraModel.cs:356-368`. Inside a 0.5 m dead zone the camera holds its yaw. When the target leaves it on the far side, the camera swings about 180° from rest with a 0.12 s smoothing time.
- **Evidence:** target passing 2.5 m over the player at 3 m/s: largest yaw step 18.3° in one frame. Every other case stayed at 8.6° or less. No NaN, no spins the long way round.
- **Fix direction:** cap the lock-on turn speed (e.g. 540°/s ≈ 9° per frame). This is rare today, because no enemy jumps.

### CAM-02: Instant pull-in when a pillar passes between camera and player

**Polish · Measured**

- **Where:** `Scripts/Core/Camera/OrbitCameraModel.cs:464-475` (`EaseToLimit`). Walls win instantly on purpose (spec: "pull in instantly, ease back out over ~0.35"), so the camera is never inside geometry.
- **Evidence:** in fights, the largest one-frame distance change was 0.62 m with the shoulder camera and 3.42 m with the centred preset (next to the low corridor). No frame had the camera inside geometry.
- **Fix direction:** optional. Ignore thin blockers such as pillars for a few frames, or pull in over 2–3 frames when only the view is blocked and the camera itself isn't inside a wall.

### Checked and found fine

These items from the review checklist hold up, which is worth knowing too.

| Check | Result |
|---|---|
| Execution order | Matches the spec: TimeScaleController -200, PlayerInputReader -100, SandboxDirector -90, LockOnController -50, PlayerController 0, enemies 10, FireProjectile 20, GreyboxRig 50, FireVfxRunner 60, camera LateUpdate 100 |
| Scaled vs unscaled time | Gameplay uses `Time.deltaTime`. Camera input, HUD, toasts and rumble use unscaled time. The lock-break timer uses game time, so pausing doesn't drop the lock |
| Time scale ownership | Only `TimeScaleController` writes `Time.timeScale`. Hitstop is 0.02× (not 0), the slowest slow-mo wins, and pause is 0 |
| CharacterController | One `Move` per frame. `isGrounded` is read from the last Move. Teleports disable and re-enable the controller. Corpses stop blocking once they land |
| Input System | No legacy `Input.*` calls (the project is Input System only). Taps shorter than a frame still count (`PlayerInputReader.cs:205-228`, `TapHoldResolver.cs:34-40`). Mouse delta isn't scaled by time. Mouse input is ignored for 2 frames after the cursor locks |
| Domain reload off | Statics are reset with `SubsystemRegistration` hooks. The three `Instance` singletons (camera rig, lock-on, input reader) clear themselves in `OnDisable` instead, which also works |
| Hits after death, double hits, friendly fire | Dead fighters, own team and repeat hits from the same attack are skipped. Bolts from a dead archer still land safely (`EnemyStrikes.cs:156-160`). Every hitbox opened was closed, and every token released (fuzz, duels, token audit) |
| Allocations | Hit queries use pooled lists. Bolt handlers are reused. HUD strings are rebuilt a few times a second at most. Projectiles are pooled (64) |
| Sandbox builder | Asks before replacing the scene, refuses to run with unsaved scenes when scripted, never overwrites tuned assets, and reports which step failed |
| Unity 6 API risks | None of the usual suspects (`FindObjectOfType`, `Rigidbody.velocity`, legacy Input). `compile-check.sh` passes, but it uses Unity 2021.3 reference DLLs, so only Unity can confirm 6000.6 |
| Lore pillar | No lightning. Deflect is deliberately not called "redirect" (`GuardSettings.cs:5-6`). Only ancient tech: dao, crossbow, repeating crossbow. Names live in data (`DisplayName` fields, the HUD's heal name is an Inspector field) |
| Data-driven pillar | Every balance number is in tuning assets. One small exception: the hitstop time scale 0.02 is a code constant (`Scripts/Combat/TimeScaleController.cs:20`), which is a feel number that could move to data |

---

## 5. Tuning suggestions

Starting points to try, not answers. The assets are in `game/Assets/_Project/Tuning/`. Every row is a value in one of them, except those marked *(new field)*, which need a small code change to add the field first. After changing values, re-measure with CombatSim (the scenario is in the last column).

| Asset → field | Now | Try | Why | Re-check with |
|---|---|---|---|---|
| `FireMoves_*` → `Plunge.MinHeight` *(new field)* | none | 1.0 m (or `MinAirTime` 0.25 s) | Stops the jump-plunge loop (ABIL-01) | `abilities` (damage loops) |
| `FireMoves_*` → `PlungeAttack.Recovery` / `StaminaCost` | 0.35 s / 14 | 0.55 s / 20 | Makes a plunge a commitment, not a free hit on a whole group | `abilities` |
| `Enemy_DaoSoldier` → `MaxHealth` | 110 | 180 | Mashing kills a soldier in about 4 s (ENEMY-02) | `duels` (masher rows) |
| `Enemy_DaoSoldier` → `MaxPoise` | 35 | 45 | One jab-cross-kick chain (39 poise) staggers from full today. It should take a chain and a bit | `duels`, `abilities` (stagger immunity) |
| `Enemy_Crossbowman` → `UsesAttackToken` | false | true, or a separate pool of 1 | 3–4 attackers at once (ENEMY-01) | `fairness` (unavoidable damage) |
| `Enemy_Crossbowman` → `AttackIntervalMin` / `Max` | 1.5 / 2.5 s | 2.5 / 3.5 s | In the recorded full-ring fight, the two archers fired 33 bolts in 49 s | `fairness`, `duels` |
| `Player_Fluid` → `StaminaRegenDelay` | 0.45 s | 0.6 s | Stamina barely limits heavies and Fire Blast (100% / 95% of the full-stamina rate from empty) | `abilities` (stamina at zero) |
| `Player_*` → `EmptyStaminaRegenDelay` *(new field)* | none | 1.0 s | Emptying the bar should hurt (the Elden Ring feel Jeremy wants) without slowing normal play | `abilities` |
| `FireMoves_*` → `Skill.Damage` or `Skill.StaminaCost` (Fire Blast) | 13 / 18 | 11 / 18, or 13 / 22 | Fire Blast spam out-damages the light chain even at 1.8 m (347 vs 309 in 10 s) while also being ranged | `abilities` (damage loops) |
| `FireMoves_*` → `Charge.SweetSpotStart` / `SweetSpotEnd` | 0.70 / 0.90 s | 0.65 / 0.95 s for the first human tests | A 0.2 s window is tight while HUD-01 and ABIL-03 are unfixed | `abilities` (fa jin success) |
| `FireMoves_*` → `Charge.ChargedDamageMultiplier` | 1.4 | 1.2 | Holding too long is nearly as good as a fa jin (1.8), so the timing skill barely pays | - |
| `FireMoves_Punishing` → `Dodge.EndRecovery` / `Dodge.NextDodgeAt` | 0.12 / 0.48 s | 0.06 / 0.40 s (only if the token fix isn't enough) | 25 of 36 unavoidable hits land in this committed tail | `fairness` |
| `FireMoves_*` → `SprintAttackMinSprintTime` | 0.3 s | 0.2 s | In Fluid every sprint starts with an 18-frame Flame Step, so the Flying Fire Kick needs 36 frames of holding | `controls` |
| `Player_*` → queued-press cap *(new field)* | never expires | 0.35 s | CTRL-02 | `fuzz` (oldest buffered press) |
| `Camera` → `LockOnMaxYawSpeed` *(new field)* | none | 540°/s | CAM-01 | `camera` (overhead pass) |

Design questions rather than numbers:
- **Perfect dodge:** "would have landed" or "dash into it"? (ABIL-02)
- **Momentum when backing off unlocked:** should it drain too? The spec says only while locked on.
- **Dragon Tail Kick Momentum:** it gains +8 like the jab (per spec). A bigger reward for the committed kick (+10 to +12) would push toward the long-range Northern Shaolin finish.
- **Healing through light hits:** a Quick Slash (15 poise) doesn't interrupt the drink, because the player has 30 poise. A Heavy Overhead (40) does. Is drinking through chip damage intended?

---

## 6. Human playtest checklist

Play these in the Editor (`Vaatu's Revenge > Build Fire Combat Sandbox`, then Play). Each item says what to look for and which finding it confirms. Try F5 (Fluid) and F6 (Punishing) where it matters. **Both of you, on both PCs if possible.**

1. **Clean start.** No errors or warnings in the Console after building and pressing Play. `FireCombatSandbox` is first in the scene list (File → Build Profiles).
2. **Bloom (BUILD-01).** Select Main Camera → Rendering → is Post Processing ticked? Is there a Volume in the scene? Throw a Fire Blast and watch a soldier's wind-up: do the glows have a halo?
3. **Responsiveness.** Jab, dodge and dodge-cancel for a minute with V-sync on. Does anything feel late? (Measured: jab 8 f, dodge 0 f in Fluid.)
4. **Perfect dodge direction (ABIL-02).** Against the swinging dummy or a soldier, dodge back, sideways and through the swing, with the same timing. Which ones gave slow motion? Did that match what you expected?
5. **Fa jin (HUD-01, ABIL-03).** Ten heavies released on the flash, then ten by counting a rhythm. Then ten right after a perfect dodge. Count the fa jins in each set.
6. **Heavy during a jab (ABIL-04).** Press and hold heavy in the middle of a jab. Does the release feel like it gave you what you held for?
7. **Plunge spam (ABIL-01).** Stand among the three dummies and repeat jump + light instantly. Is it stronger than anything else? Does it look silly?
8. **Mashing (ENEMY-02).** Fight one soldier, then two, pressing only light. Did you win? Was it fun anyway?
9. **The full ring (ENEMY-01).** Fight both soldiers and both crossbowmen in Fluid, then in Punishing. After each death, ask: was that hit my fault? Keep a tally of "unfair" hits.
10. **Platform crossbowman (ENEMY-03).** Walk to the duel ring and watch the archer on the raised block. Does it stay up there?
11. **Camera near walls.** Back into a wall, walk along a wall with it on the camera's side (it should swap shoulders), weave through the pillars, run the low corridor. Any pops, clipping or dizziness? Lock onto the platform archer from right below it.
12. **L3 (CTRL-04).** Sprint and dodge around for a minute on a gamepad. Did the camera swap shoulders when you didn't mean it to?
13. **Lock-on.** Kill the locked enemy: does the lock go where you expect? Flick the right stick left and right to switch targets. Hide a locked target behind a pillar for about 1.5 s.
14. **Commitment feel.** Heal (X / R) next to a soldier: does the 1 s drink feel fair to punish? Do hits "land" (hitstop, shake)? Does the perfect-dodge slow motion feel good or jarring?
15. **Performance.** Window → Analysis → Profiler during the full-ring fight: steady frame time, and garbage-collection allocation near 0 B per frame?

---

## Appendix: key measurements

All from `dotnet run --project tools/CombatSim -- <scenario>`. Full tables come out of each scenario.

**Frame data** (Fluid / Punishing where they differ; frame 0 = the press, or the release for the heavy):

| Move | First active frame | Free again | Dodge-cancel from | Stamina |
|---|---|---|---|---|
| Flame Jab | 8 f | 32 f | 14 f / 24 f | 9 / 13 |
| Flame Cross (chained) | 8 f | 33 f | 14 f / 26 f | 9 / 13 |
| Dragon Tail Kick (chained) | 12 f | 49 f | 21 f / 37 f | 13 |
| Fa Jin Palm (from release) | 17–18 f | 58–59 f | 24 f / 44 f | 22 / 28 |
| Flying Fire Kick | 11 f, travels 3.21 m | 44 f | - | 16 |
| Falling Axe Kick | landing ring 6–8 f after the light (pressed just after take-off) | about 28 f after the jump | - | 14 |
| Fire Blast | launches at 14 f | 32 f | 18 f / 24 f | 18 |
| Flame Step | starts 0 f / on release | 18 f / 29 f | next dodge 17 f / 29 f | 6 / 16 |

- Flame Step i-frames: frames 2–14 in Fluid, 3–17 in Punishing.
- Heavy charge tiers by frames held: Quick 1–11, Partial 12–41, FaJin 42–54 (0.70–0.90 s), Charged 55+.
- Guard tap deflect window: 9 f.
- Heal: health arrives at 33 f, free at 61 f.
- Run 4.8 m/s, sprint 7.2 m/s.

**Telegraphs vs a 0.25 s reaction:** Quick Slash leaves 0.22 s to spare, Heavy Overhead 0.67 s, Double Slash 0.22 s, Delayed Thrust 0.87 s, Aimed Shot 0.52 s, Repeater Burst 0.72 s, dummy swing 0.32 s. Deflecting always needs learned timing: the press has to come in the last 0.15 s before the strike.

**Fuzz** (8 runs × 150,000 frames, 30 / 60 / 144 fps and hitches):
- 0 violations.
- Longest i-frames 0.205–0.258 s.
- Invulnerable 6–8% of the time under random input.
- Every dodge, hitbox and token balanced; never more than 2 token holders.

**Duels** (40 seeds, 120 s limit). Win rates:

| Bot | Fluid: 1 soldier | Fluid: 2 soldiers + crossbow | Fluid: full ring | Punishing: 1 soldier | Punishing: 2 soldiers + crossbow | Punishing: full ring |
|---|---|---|---|---|---|---|
| masher | 100% | 93% | 55% | 100% | 50% | 18% |
| react | 100% | 98% | 75% | 100% | 48% | 8% |
| anticipate | 100% | 90% | 73% | 100% | 45% | 5% |
| guard | 100% | 68% | 25% | 100% | 43% | 5% |
| aggressive | 100% | 95% | 78% | 100% | 35% | 5% |
| fajin | 100% | 100% | 70% | 100% | 40% | 8% |

**Camera** (over-the-shoulder unless noted):

| Case | Result |
|---|---|
| Locked target circling at 1.2–8 m, up to 360°/s | Never off-screen; max 6° turn per frame; 0 jitter reversals |
| Dashing past a locked target | Max 8.3° per frame, no long-way flips |
| Elevated target (platform archer) 1–8 m away | Both fighters on screen; pitch goes to −40° at 1 m |
| Lock framing | Target centred (x 0.00), player slightly left (x −0.08) |
| Locked target killed | Lock moves to the most central remaining enemy; dropped if the only one is behind you |
| Target switching (flick, wheel) | All 5 cases correct |
| Target hidden behind a pillar | Kept for 1.3 s, dropped at 1.5 s |
| Backing into a wall | Pulls in to 0.76 m, never inside geometry, eases out over 45 f |
| Low corridor | Keeps its full 3.20 m (the centred preset pulls in to 3.83 m of its 4.0 m) |
| Auto shoulder swap | Only when less than half the offset fits: 0.45 m from a wall → 1 swap and 1 swap back; 0.8 m → none; pillar weave → 1 and 1 |
| Combat pull-back | 3.20 → 3.73 m with a foe at 5 m; 3.75–4.10 m with a foe circling the 8 m edge, no pumping; back to 3.20 m after |
| Look input | Mouse turns the same at 30/60/144 fps (72°). Stick 200° per second (180° with heavy hitches, by design). Still turns during hitstop |
