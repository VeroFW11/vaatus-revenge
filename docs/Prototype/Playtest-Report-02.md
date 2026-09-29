# Playtest Report 02: re-verifying the fix round

- **Date:** 29 Sep 2026
- **Code measured:** fix-round commit `60cb8ee`. Core fingerprint `sha1 2ec2b9e9a7`, `OrbitCameraModel.cs 8bac1ef3c5`.
- **How:** the same headless harness as report 01 (`tools/CombatSim/`), updated to mirror the new Unity adapters (see the end of this report). Every scenario was re-run in both presets. I also built the harness at the parent commit to check whether anything had changed from before. The Unity side was reviewed by reading the fix diff.
- **Honest limit:** no Unity Editor was available, so none of this was seen running in Unity. `tools/compile-check.sh` and `tools/run-core-tests.sh` (206/206) both pass.

---

## 1. Verdict

1. **Most fixes work in the rules code: 10 of 16 findings are fixed.** Fa jin timing, the input buffer, the sprint-attack grace, lock-on overhead swings, the platform crossbowman and the L3 swap are all done. The fuzz found nothing stuck and no NaN in 1.2 M frames.
2. **One headline fix doesn't reach Unity (NEW-01, Major).** The new "would have landed" perfect-dodge rule needs the enemy code to call `PlayerCombatModel.NotifyEnemyStrike`, and no Unity script does. In the Editor, backward dodges still never count, and sideways ones got *harder* (window 4 f → 1 f) because enemy reach is shorter now.
3. **The difficulty overshot (NEW-02, Major).** Punishing vs two soldiers went from 88–100% bot wins to 0–8% for the dodging styles. Fluid's 3-enemy group also got much harder. The causes are the new empty-stamina pause and the 180 HP soldiers: dodgers spend over half of every minute with an empty bar.
4. **Mashing still wins in Fluid.** It wins 100% vs 1 soldier and 95% vs 2, just slower and with more damage taken. Plunge spam is no longer the best single-target move, but it's still the best crowd move (3.2× light mashing).
5. **The camera "collapse" is real, and it's old (NEW-03, Major).** When you fight with a pillar at your back, the shoulder camera pulls in to 0.00–0.05 m (inside the head), jumping up to 3.9 m in one frame. Walking beside the corridor wall snaps it 3.20 → 0.29 m as you pass the wall's end. The parent commit behaves the same way, so the fix round didn't cause it.
6. **Recommendation:** before the human playtest, wire up `NotifyEnemyStrike`, bring Punishing's empty-stamina pause back to about 0.8–1.0 s, and decide what the camera should do near pillars. After that, play checklist items 1–6 in section 6.

---

## 2. Findings from report 01

"Unity" = what the Editor will do, as mirrored by the harness. "Core" = the pure rules with `--notify-strikes`.

| ID | Sev (01) | Now | Evidence (report 01 → now) |
|---|---|---|---|
| ENEMY-01 | Major | **Partly** | Crossbowmen now share the 2 tokens: at most **2** attackers at once (was 4). Oracle hits/min in Punishing: 2 soldiers + crossbow **1.80 → 0.85**, full ring **3.60 → 1.35**. The 2-soldier row is 0.00, so it's not yet "near" it. What's left lands in Punishing's dodge tail. What-if `--set dodge.EndRecovery=0.06 --set dodge.NextDodgeAt=0.40` gives 0.35 / 0.78. Fluid: 0.35 → 0.20, 0.70 → 0.45 |
| ENEMY-02 | Major | **Partly** | Masher, Fluid: 1 soldier **100% win** (3.7 s → 9.7 s, 18 → 39 damage), 2 soldiers **100% → 95%** (7.1 → 20.1 s, 45 → 116 damage, many heals). Punishing: 98% / 23%. A relentless chain no longer kills a soldier in 20 s (it takes 80–110 damage). In Punishing the masher (23%) still beats react (8%) and anticipate (3%) vs 2 soldiers (see NEW-02) |
| ENEMY-03 | Major | **Fixed** | The platform crossbowman stayed on the block from all **6/6** player spots (was 1/4). Max drift 3.0 m. It shoots from up there (6 shots in 20 s), including point-blank when cornered by the leash |
| ABIL-01 | Major | **Partly** | Plunge needs 15 f of air time (first plunge at 1.27 m up), free again at 59 f (was 28 f), costs 20 stamina. 10 s on 1 dummy: plunge 424 → **197** vs light mash 309 → 187 (par). On 3 dummies: 1,273 → **592** vs 187, still the best crowd move and the best damage per stamina (3.06) |
| ABIL-02 | Major | **Not fixed in Unity** (core fixed) | Perfect window, Unity: back **0 f**, side **1 f** (was 4 f: a regression from the 2.1 m reach), toward 6 f. Core with notify: **6 f in every direction** (Delayed Thrust side 5 f). See NEW-01 |
| HUD-01 | Major | **Fixed** | Reacting to the flash (window now 0.65–0.95 s): **3–42% → 90–100%** at 0.20–0.25 s reaction. The new text teaches rhythm. The new ready cue causes its own problem (NEW-04) |
| ABIL-03 | Minor | **Fixed** | Heavy held 0.8 / 0.9 s of real time right after a perfect dodge: Partial → **FaJin**. The charge clock runs on `RealDeltaTime` |
| ABIL-04 | Minor | **Fixed** | Heavy pressed at frame 4 or 10 of a jab and held 0.8 s: 0.617 / 0.717 s → **0.783 s charged, FaJin** in both presets |
| CTRL-01 | Minor | **Fixed** | L f0, Dodge f5, L f8 → **Dodge @14** (was a Flame Cross). L f0, Dodge f10, Heavy f12 → **Dodge @14**. Guard presses survive too |
| CTRL-02 | Minor | **Fixed** | Oldest press run in 1.2 M fuzz frames: 0.34–0.535 s → **0.335–0.350 s** (the 0.35 s cap) |
| CTRL-03 | Minor | **Fixed** | Sprint released 1 f or 5 f before light → **Flying Fire Kick** (both presets). Minimum hold before a sprint kick: 36 f → 30 f (Fluid) |
| BUILD-01 | Minor | **Fixed (read)** | The builder adds a global Volume (`SampleSceneProfile`: bloom on) and turns on camera post-processing through reflection. It fails with one warning, never an exception. Confirm in the Editor (checklist 2) |
| ABIL-05 | Polish | **Not fixed** (open design question) | No lead. Strafing crossbowman hit rate at 14 / 20 / 25 m: 37 / 33 / 30% (was 27 / 23 / 20%). Still "close the distance" |
| CTRL-04 | Polish | **Fixed** | Swap needs a **0.25 s hold**, and each hold swaps once. Holding 1–14 f: no swap. 15 f or more: exactly 1 swap. Keyboard V needs the hold too. Try it on a pad (checklist 5) |
| CAM-01 | Polish | **Fixed** | Target passing overhead: largest yaw step **18.3° → 8.4°** per frame (540°/s cap). No NaN, no long-way flips |
| CAM-02 | Polish | **Won't fix (by design)**, and worse than it looked | Pull-in stays instant. Near pillars it is frequent and collapses to the head: see NEW-03 |

**Counts (16 findings):** Fixed 10 (BUILD-01 by reading only) · Partly 3 · Not fixed 2 (ABIL-02 in Unity, ABIL-05 as an open design question) · Won't fix (by design) 1 (CAM-02).

---

## 3. New bugs

Same scheme: **Blocker** can't test · **Major** fix before the human playtest · **Minor** · **Polish**. Measured / Read / Suspected as before. Paths are under `game/Assets/_Project/`.

| ID | Sev | Status | Title | Location |
|---|---|---|---|---|
| NEW-01 | **Major** | Read + Measured | `NotifyEnemyStrike` is never called in Unity, so dodging away can never be perfect | `Scripts/Enemies/EnemyFighter.cs:370-373` (and `EnemyStrikes.cs:28`) |
| NEW-02 | **Major** | Measured | Difficulty overshoot: Punishing vs 2 soldiers is near-unwinnable for dodging styles | `Core/Tuning/PlayerTuning.cs:93`, `Core/Tuning/EnemyTuning.cs:18` |
| NEW-03 | **Major** | Measured (pre-existing) | Camera collapses near pillars and snaps at wall ends | `Scripts/Camera/ThirdPersonCameraRig.cs:223-238`, `Core/Camera/OrbitCameraModel.cs:310` |
| NEW-04 | Minor | Measured | Reacting to the fa jin "get ready" cue releases too early | `Core/Tuning/ChargeSettings.cs:19` |
| NEW-05 | Polish | Measured | `EnemyBrain.Reset` swallows the strike's end events | `Core/AI/EnemyBrain.cs:719` |
| NEW-06 | Polish | Measured | Leash radius = platform half-width, so only the ledge check keeps the archer from the edge | `Core/Tuning/EnemyTuning.cs:86` |
| NEW-07 | Polish | Read | A misleading test comment, and a stray empty object if Volume setup half-fails | `Tests/EditMode/CombatSoakTests.cs:190`, `Editor/Sandbox/SandboxPostProcessing.cs:73-79` |

### NEW-01: the perfect-dodge fix isn't wired up in Unity (Major)

- `DodgeProfile.PerfectRule` now defaults to `WouldHaveLanded`. That rule only works when the enemy side calls `PlayerCombatModel.NotifyEnemyStrike(origin, forward, move, attackerFeet)` as each melee strike opens.
- Only tests call it. `grep NotifyEnemyStrike` finds `CombatSoakTests.cs:191` and `CombatPlaytestFixTests.cs`, and nothing under `Scripts/Enemies/`. The soak test's comment ("As the Unity enemy code does") is wrong.
- **Effect in the Editor:** the report-01 rule still applies (back 0 f). Sideways dropped from 4 f to 1 f because the swing reach shrank to 2.1 m.
- **Evidence:** `abilities` prints both tables. In duels the anticipate bot gets 38/122 perfect dodges vs one soldier without the call and 50/120 with it.
- **Fix direction:** in `EnemyFighter.HandleEvents`, when a melee `AttackActiveStart` arrives (or in `EnemyStrikes.OpenMelee`), find the player's `PlayerController.Model` and call `NotifyEnemyStrike(e.Origin, e.Direction, e.Move, transform.position)` before the hit query. Skip bolts. Then correct the test comment.

### NEW-02: the fix round overshot the difficulty (Major)

- **Punishing, win rates vs 2 soldiers (40 seeds):** masher 23%, react **8%**, anticipate **3%**, aggressive **0%**, guard 78%, fajin 98%. Report 01: 88–100%.
- **Punishing, 2 soldiers + crossbow:** 0% for four styles.
- **The dodging bots sit at zero stamina 33–36 s of every minute.** Mashing now beats the dodging styles in Punishing, which is the report-01 complaint in a new form.
- **Fluid, 2 soldiers + crossbow:** masher 93% → 25%, guard 68% → 48%. react, anticipate and fajin stay at 90–100%.
- **What-ifs** (Punishing, 20 seeds, react / anticipate vs 2 soldiers):

  | Change | react | anticipate | masher |
  |---|---|---|---|
  | as committed | 8% | 3% | 23% |
  | `player.EmptyStaminaRegenDelay=1.0` | 65% | 45% | 65% |
  | `player.EmptyStaminaRegenDelay=0.8` (no extra pause) | 80% | 75% | 75% |
  | soldier back to 110 HP / 35 poise | 90% | 85% | 95% |
  | both | 100% | 90% | 95% |

- **Fix direction:** Punishing `EmptyStaminaRegenDelay` 1.4 → ~0.9. Keep 180 HP (it's what stops mashing in Fluid) or meet it halfway (150). Re-run `duels --preset punishing`.
- **Harness caveat:** full-ring win rates are no longer comparable to report 01. The platform archer now stays up, and bots don't climb stairs, so most full-ring runs time out alive. Use deaths instead: Fluid 9–37 of 40, Punishing 38–40 of 40.

### NEW-03: the camera collapses near pillars and at wall ends (Major, pre-existing)

A replay render showed the camera "collapsing" near the low corridor. It's real, and it's worse around the pillars.

| Case (over-the-shoulder default) | Closest | Largest one-frame pull-in | Frames pulling in > 0.3 m |
|---|---|---|---|
| Fight a soldier at the corridor mouth (10 × 30 s) | **0.00 m** | **3.87 m** | 33 |
| Fight a soldier in the pillar field (10 × 30 s) | **0.00 m** | **3.95 m** | 67 |
| Walk north beside the corridor's west wall | 0.29 m | **2.91 m** (at the wall's end) | 1 |
| Walk / strafe through the pillar rows | 1.46 m | 1.36–1.64 m | 3 |
| Orbit at the corridor mouth / inside it | 1.73 / 1.13 m | 0.84 / 0.13 m | 1 / 0 |
| Run straight through the corridor | 3.20 m | 0.00 m | 0 |

- It never ended up inside geometry. The parent commit gives the same numbers for every scripted path, so the fix round didn't cause it. The fights count more pops now only because fights last longer.
- **Wall-end snap:** while the player walks beside a wall, the shoulder probe squeezes the offset to 0.43 m. Once the shoulder point clears the wall's end, the offset eases back out. The distance probe behind it then clips the wall's end face, and the camera jumps from 3.20 to 0.29 m in one frame, then eases out over about 45 frames.
- **Pillar at your back:** the distance probe hits the pillar, and the camera sits 0.05 m behind the shoulder point, which is in or beside the player's head.
- **Fix direction** (a design call for Jeremy):
  - Ease pull-in over 2–3 frames when only the view is blocked and the camera sphere itself is clear.
  - Let the shoulder yield before the distance does: re-probe the shoulder at the camera end.
  - Below about 1 m, fade the player or raise the camera instead of going into the head.
  - Re-run `camera` (the "Camera pops" table).

### NEW-04: the "get ready" cue is too early to react to (Minor)

- The cue comes `ReadyCueLead` = 0.25 s before the window, which is about a human reaction time. So a player who reacts to the cue lets go right at the window's start.

  | Reacting to the ready cue (0.40 s) | fa jin | too early |
  |---|---|---|
  | 0.17 s reaction | 4% | 96% |
  | 0.20 s reaction | 20% | 80% |
  | 0.25 s reaction | 80% | 20% |
  | 0.30 s reaction | 100% | 0% |

- Reacting to the gold flash itself now gives 90–100%, so the cue can teach the wrong habit.
- **Fix direction:** `ReadyCueLead` ≈ 0.10 s (a reaction then lands mid-window), or treat the mark as a rhythm guide only, not something to react to.

### NEW-05: Reset swallows the strike's end events (Polish)

- `EnemyBrain.Reset` now calls `pendingEvents.Clear()` after `ExitAttack()`. A strike that's reset mid-swing never delivers `AttackActiveEnd` / `AttackEnded`.
- Fuzz: enemy hitbox open/close events are unbalanced by 1–8 per run (e.g. 1051/1044). They were balanced in report 01.
- Unity is covered, because `ResetFighter` calls `strikes.EndAll()` and `presenter.OnReset()` first. But any `OnBrainEvent` listener that pairs these events will miss them.
- **Fix direction:** clear the queue *before* `ExitAttack`, or document it.

### NEW-06: the leash is the same size as the platform (Polish)

- `LeashRadius` 3 m equals the block's half-width. The leash alone would let the archer's centre reach the edge.
- The ledge check had to stop it 249–740 times in 20 s.
- If the ledge probe ever misses in Unity (layers, skin width), the archer perches on the edge or slides off.
- **Fix direction:** 2.3 m. Separately, a player standing right under the south edge is never shot (hidden, and the leash holds the archer back). That's fine, but worth knowing.

### NEW-07: smaller items from reading the diff (Polish)

- The `CombatSoakTests.cs:190` comment (see NEW-01).
- `SandboxPostProcessing.AddGlobalVolume` creates the "Global Volume" GameObject before `AddComponent`. If that fails, an empty object is left in the scene.
- Otherwise the reflection code is sound: assembly-qualified names with a fallback search, property-or-field setters, `try/catch`, and one warning with manual steps.

**Also reviewed, found fine:**
- No new statics that domain-reload-off would leak (only consts).
- No legacy `Input`, `FindObjectOfType` or physics API.
- The ledge check uses the existing non-allocating `CombatPhysics`.
- `Undo.AddComponent(GameObject, Type)`, `AssetDatabase.LoadAssetAtPath(string, Type)` and `Mathf.Approximately` all exist in Unity 6.
- The HUD and `PlayerFeedback` read `ChargeTime` every frame, so the ready cue can't be missed.
- `FireProjectile.ClearAll` runs on every reset path.
- As before, `compile-check.sh` uses 2021.3 reference DLLs, so only the Editor can confirm 6000.6.

---

## 4. Fluid vs Punishing, updated

| | Fluid (01 → now) | Punishing (01 → now) |
|---|---|---|
| Dodge i-frames / whole dodge | 13 f / 18 f (same) | 15 f / 29 f (same) |
| Dodge-mashing: share of time invulnerable | 61% → 48% | 38% → 28% |
| Perfect dodge (Unity / core with notify) | back 0 f, side 1 f, toward 6 f / 6 f in every direction | off (by design) |
| Oracle hits/min, 2 soldiers + crossbow | 0.35 → 0.20 | 1.80 → 0.85 |
| Oracle hits/min, full ring | 0.70 → 0.45 | 3.60 → 1.35 |
| Max attackers at once | 4 → 2 | 4 → 2 |
| Bot wins vs 2 soldiers | 100% → 90–100% | 88–100% → **0–98%** (dodgers 0–8%) |
| Bot wins vs 2 soldiers + crossbow | 68–100% → 25–100% | 35–50% → **0–53%** |
| Masher vs 1 / 2 soldiers | 100 / 100% → 100 / 95% | 100 / 100% → 98 / 23% |
| Time at zero stamina, masher and aggressive (s/min) | 0–13 → 24–35 | 19–37 → 43–47 |
| Fa jin by reacting to the flash (0.20–0.25 s) | 3–42% → 90–100% | same |

**Recommendation unchanged: Fluid stays the default.** Punishing needs NEW-02 retuning before it's a fair comparison. Stamina now clearly bites in Fluid (the masher sits at empty for 35 s of every minute), which is Jeremy's Elden Ring point.

---

## 5. Harness changes (`tools/CombatSim`)

- **Mirrors 60cb8ee:**
  - `PlayerWorldState.SelfHeight`, `RealDeltaTime` and `HasNearestEnemy` / `NearestEnemyPosition`.
  - The camera gets `SwapShoulderHeld` (hold to swap).
  - `EnemyFighter`'s ledge check, reading box tops and the ramp.
  - The fuzz clears projectiles on reset, as `SandboxDirector` does.
- **Fixed measurement bugs:**
  - Perfect dodges are counted from the event, one frame later. A `WouldHaveLanded` award has no hit outcome, and events raised during `ReceiveHit` arrive on the next tick.
  - The buffered-heavy table reads the model's own charge clock.
  - The plunge rows press after `MinAirTime`.
  - The fa jin table uses the live window and adds ready-cue rows.
- **New:**
  - `--notify-strikes` (the core as designed).
  - `--set target.Field=value` (what-if tuning).
  - A "Camera pops" table near the corridor and pillars.
  - A hold-to-swap table.
  - Drift and ledge-stop columns for the platform crossbowman.
- Release builds (`-c Release`) run about 10× faster. Every scenario was re-run twice with identical results.

---

## 6. Still needs a person in the Editor

1. **Clean start and bloom (BUILD-01).** Build the sandbox. Is there a "Global Volume" in the scene, and is Post Processing ticked on the Main Camera? Do the fire and wind-up glows have a halo? Any warning in the Console?
2. **Perfect dodge direction (NEW-01 / ABIL-02).** Dodge back, sideways and through a soldier's swing with the same timing. Today expect slow motion only when dashing through. After the wiring fix, every direction should give it.
3. **Pillar camera (NEW-03).** Fight a soldier with your back to a pillar, and walk beside the corridor wall past its end. Does the view jump into the head? Is it dizzying?
4. **Punishing vs 2 soldiers (NEW-02).** Can you win by dodging? How often is the stamina bar empty?
5. **Hold-to-swap (CTRL-04).** On a pad, sprint and dodge for a minute: no accidental swaps? Does the 0.25 s hold feel slow on purpose?
6. **Fa jin (HUD-01 / NEW-04).** Ten heavies on the gold flash, ten reacting to the white ready mark, ten by rhythm. Count the fa jins in each set.
7. **Platform archer (ENEMY-03).** Walk to the duel ring and circle the platform: it should stay up and keep shooting. Check it doesn't perch half off the edge (NEW-06).
8. **Enemy blades (new visuals).** Do strikes land exactly at the tip of the longer dao? Any "can't show the reach" warning in the Console?

Reproduce any number with `dotnet run --project tools/CombatSim -c Release -- <scenario>` (add `--set ...` as in the tables; since round 2 strikes are notified by default, and `--no-notify-strikes` gives the 60cb8ee behaviour).

---

## 7. Round 2 fixes (29 Sep 2026)

Measured with the same harness (Release, 40 seeds) on core fingerprint `sha1 df98bc788b` (`OrbitCameraModel.cs c0331c3d78`). The "before" columns are 60cb8ee with `NotifyEnemyStrike` wired up, so only the tuning differs. `compile-check.sh` passes and `run-core-tests.sh` gives 217/217 (11 new tests). Still not seen in the Editor.

**Tuning assets:** the sandbox's tuning assets are only seeded from these defaults when first created. If you already built the sandbox, run *Vaatu's Revenge > Reset Sandbox Tuning To Defaults* to pick up the new numbers.

### NEW-01: perfect dodge wired into Unity (fixed)
- `EnemyStrikes.OpenMelee` now calls `PlayerController.Instance.Model.NotifyEnemyStrike(...)` (null-safe, skipped when the player is dead) **before** its hit query, for every melee strike. Soldiers and the sparring dummy's swing both go through it. Bolts don't. `EnemyFighter` passes its feet position.
- The harness now notifies by default, like Unity. `--no-notify-strikes` reproduces 60cb8ee. The soak-test comment is fixed.
- `abilities`, perfect window vs each soldier attack: back / side / toward **0 / 1 / 6 f → 6 / 6 / 6 f** (Delayed Thrust side 5 f). The anticipate bot vs one soldier gets 52 perfect dodges out of 121.

### NEW-02: difficulty back to "fair but not free" (data only)
- Punishing: `StaminaRegen` 32 → **38**, `StaminaRegenDelay` 0.8 → **0.65**, `EmptyStaminaRegenDelay` 1.4 → **1.1**. The normal pause now drops more than the empty one, because the masher only ever meets the empty pause and dodgers mostly meet the normal one. That is what lets the defenders pull ahead of the masher. A plain 0.9 s empty pause (the report's suggestion) left the masher level with react and anticipate (70 / 60 / 60%).
- Fluid: `EmptyStaminaRegenDelay` 1.0 → **1.5**. In Fluid only mashing empties the bar (dodges cost 6), so this pause hits the masher and leaves the defenders alone.
- Soldier HP stays at 180.

| Win rate (40 seeds) | Punishing vs 2 soldiers, before → after | Fluid vs 1 soldier | Fluid vs 2 soldiers |
|---|---|---|---|
| masher | 23% → **50%** | 100% → 100% (9.7 → 12.7 s, damage 39 → 54) | 95% → **70%** |
| react | 8% → **78%** | 100% → 100% | 98% → 98% |
| anticipate | 3% → **68%** | 100% → 100% | 100% → 100% |
| guard | 78% → 90% | 100% → 100% | 90% → 90% |
| aggressive | 0% → 25% | 100% → 100% | 93% → 73% |
| fajin | 98% → 95% | 100% → 100% | 98% → 98% |

- Dodging bots' time at empty stamina in Punishing: 33 → 26–28 s per minute.
- Report-01 goals still hold:
  - Oracle hits per minute (Fluid / Punishing, 2 soldiers + crossbow): 0.23 / 0.75 (report 02: 0.20 / 0.85). Full ring: 0.12 / 1.15 (0.45 / 1.35).
  - Against one dummy, plunge spam (197) still trails heavy tap (247) and fa jin (203). Light mashing fell from 187 to 164.
  - Crowd plunge is unchanged (592).
- **Open:** in Fluid the masher still beats one soldier 100% of the time. It just takes longer and costs more. No data-only change got below 100% without wrecking the defenders' fights: even a 2.0 s empty pause plus faster soldiers only raised the damage it takes to 79 of its 235 (health plus heals). Making it losable needs a rule: for example, a soldier hit 3 times in a row answers with an armoured counter. That's Jeremy's call.

### NEW-03: camera near pillars and wall ends
New rules in `OrbitCameraModel`, fed by three extra probes in `ThirdPersonCameraRig` and mirrored in the harness:
- **The shoulder offset must also fit at the camera's end.** This is a sideways probe from `CameraEndCentre`.
- **A wall touching the camera still moves it in on the same frame**, so the camera is never inside geometry.
- **A pillar that only blocks the view** waits `OcclusionGraceTime` 0.1 s, then glides in over `CollisionPullInTime` 0.15 s, never past the pillar's far side. Only the pillar's thickness can be skipped in one frame.
- **A 0.2 m fatter look-ahead probe** (`CollisionLookAhead`) starts that glide before a pillar touches the camera. It never brings the camera closer than 1.1 m.
- **With a wall closer behind than `MinCollisionDistance` 1.1 m**, the camera rises and looks down over the head: `ViewPitch` = `Pitch` + `CollisionRise`, up to 85°. It eases over 0.05 s and never moves the camera faster than 8 m/s. The player's own `Pitch` is untouched.

| Camera pops, shoulder camera (`camera`) | Before: largest one-frame jump / closest distance | After: largest jump / closest distance / closest to the head |
|---|---|---|
| Walk beside the corridor wall past its end | **2.99 m** / 0.29 m | **0.58 m** / 1.10 m / 1.37 m |
| Strafe across the pillar rows | 1.64 m / 1.46 m | **0.42 m** / 1.26 m / 1.28 m |
| Walk through the pillar field, orbiting | 1.39 m / 1.48 m | 0.86 m / 1.41 m / 1.40 m |
| Orbit at the corridor mouth | 0.83 m / 1.73 m | 0.68 m / 1.61 m / 1.66 m |
| Fight at the corridor mouth (10 × 30 s) | **3.90 m** / 0.00 m | **1.89 m** / 0.37 m / 0.38 m |
| Fight in the pillar field (10 × 30 s) | **4.21 m** / 0.00 m | **2.53 m** / 0.36 m / 0.24 m |

- In fights, the old rules measured in the new columns gave 25 / 32 frames with a jump over 1 m and 170 / 21 frames within 0.4 m of the head (corridor / pillars). The new rules give **5 / 13** and **3 / 6**. Those old-rule numbers come from `--set camera.OcclusionGraceTime=0 --set camera.CollisionPullInTime=0 --set camera.CollisionLookAhead=0 --set camera.MinCollisionDistance=0`, which keeps only the new shoulder probe.
- It is still never inside geometry: 0 frames in every case. The 1.2 M-frame fuzz shows no camera NaN and nothing out of range.
- **Left:** jumps of 1.9–2.5 m still happen in fights, about once every 20–25 s. They come when a lock-on swing sweeps a pillar's side face straight into the camera. The only ways out along the view line are through the pillar or in front of it. Stopping those would need a camera that slides around pillars, or fading the pillar out. Jeremy should judge them in the Editor (checklist 3).
- **Other costs:**
  - The camera now sits closer near walls (running through the low corridor: 3.20 → 2.63 m).
  - With the camera-end probe, the shoulder offset can shrink by up to 0.23 m in one frame beside a wall (was 0.07).
  - Rising over the head in the low corridor gives an almost top-down view for a moment.

### NEW-04: fa jin ready cue (fixed)
`ReadyCueLead` 0.25 → **0.12 s**, so the cue now comes at 0.53 s.

| Reacting to the ready cue | 0.17 s | 0.18 s | 0.20 s | 0.22 s | 0.25 s | 0.30 s | 0.35 s |
|---|---|---|---|---|---|---|---|
| Fa jin, before | 4% | - | 20% | - | 80% | 100% | 100% |
| Fa jin, after | **100%** | **100%** | **100%** | **100%** | **100%** | **100%** | **99%** |

Reacting to the flash itself and timing by rhythm are unchanged.

### NEW-05, NEW-06, NEW-07 (fixed)
- **NEW-05:** `EnemyBrain.Reset` keeps queued strike-end events and closes a swing it cuts short, all before `Reset`. Fuzz: enemy hitboxes opened/closed are balanced in all 8 runs (report 02 had them off by 1–8). There are 2 new tests.
- **NEW-06:** crossbowman `LeashRadius` 3 → **2 m**. The platform archer stays up from all 6 spots, drifts at most 2.00 m, and needs **0** ledge stops (was 249–740).
- **NEW-07:** `SandboxPostProcessing` builds the "Global Volume" all or nothing. If adding the component or setting a member fails (or throws), the object is destroyed again, and Undo only records it once it's complete. The soak-test comment now names `EnemyStrikes.OpenMelee`.
