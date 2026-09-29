# Playtest Report 04: second verification pass

> Independent re-check of `claude/dazzling-clarke-njm3m5` at `6cbb6ff` (29 Sep 2026), after the fixes for [Report 03](Playtest-Report-03.md).
>
> How it was checked:
> - Every Report 03 finding was re-run against its original reproduction, with the same harness and metric scripts.
> - Then the fixes themselves were hunted for regressions.
>
> Rules this pass followed:
> - The verifier changed no game code, tests or tools.
> - Everything is headless: the real combat core plus the procedural animator at 60 fps.
> - Nothing has been seen in the Unity Editor yet.
>
> Note: 2 of the 4 fix commits are titled "WIP ... in progress", but the tree builds, the tests pass and every claimed fix is present.

## 1. Verdict

**Almost there.** All 6 Majors from Report 03 are fixed and hold up under their original reproductions. The owner's checklist now passes on every item.

**One Major regression:** the Dao Soldier's anti-mash break-out never fires any more (W-01). The fix meant "the 6th hit arms it", but the 6th hit breaks the soldier's poise first, and a stagger wipes the count. So mashing light beats a soldier 100% of the time again, in both presets. It's a number to change, not code, but it's Jeremy's balance call and it should be settled before hand-back.

**Also new:** 5 Minor and 4 Polish findings.

## 2. Baseline

| Check | Result |
|---|---|
| `tools/compile-check.sh` | PASS (Core, Game editor + player, Editor, lint) |
| `tools/run-core-tests.sh` | **277 / 277** (13 new, incl. `VerificationFixTests.cs`) |
| `CombatSim fuzz --quick` | 8 runs, **0 violations**, no NaN. The unmatched counts (1368/1367, 576/575) are hitboxes still open when a run stops, as in reports 02-03 |
| `CombatSim duels` (masher / aggressive / react / guard vs 1-2 soldiers) | No invariant violations. Win rates in W-01 |
| `CombatSim abilities` | Full juggles 20/20; zip hits 2-13 m and within 45°; range mix unchanged |
| `CombatSim anim` | 4135 frames, 46/46 keys. It now prints an aim column (V-22): every strike aims within 0-15° |

## 3. Acceptance checklist

| # | Owner's item | Result | Evidence |
|---|---|---|---|
| 1 | Controls fixed (Spider-Man 2) | **PASS** (Minor W-02, W-05) | LB + X/Y/B chords give the ability in **18/18** timing cases (same frame, 3 f, 8 f; idle and mid-jab). No parry whiff or lockout. A parry after a whip chord deflects 3/3 (22, 30, 40 f later). Stick flick: 36/36 hits, including 135° behind (was 3 wrong-way lunges). Side dodge → neutral-stick counter: 23 dmg on all 4 soldier attacks (was 0). Parry window unchanged at 9 frames on all 4 attacks |
| 2 | Spider-Man aerial feel | **PASS** | Launcher → 3 air hits → slam: 20/20 seeds, and at 0° / 35° with or without stick. Launcher vs 2 soldiers: 3/3. Launch → air dash back → zip to the juggled soldier (hits at 1.19 m, arrives level at 2.38 vs 2.36 m) → air string 3/3. Relaunch loop is still finite (5 launches, 7 soldier attacks in 20 s). Zip vs **moving** crossbowmen and soldiers at 4-13 m: 80/80 hits, 1.05-1.92 m centre distance at the first active frame (was about 2.3 m) |
| 3 | Martial-arts packs | **PASS** (bridge; Unity only) | Unchanged wiring, plus a 0.15 s hand-off fade (`BodyAnimatorDriver.cs` LateUpdate, `HumanoidBody.BlendBonesFrom`) |
| 4 | Human models, full limbs | **PASS** | Bone lengths constant; 0 backward knees (the single-axis flags in the death poses are pointed toes while kneeling, checked visually) |
| 5 | Fluid motion | **PASS** (Minor W-03) | Worst single-frame limb jump **0.67 m** (a launcher kick, 40 m/s), down from 1.91 m. Soldier spine pitch through launch → slam → knockdown → get-up changes at most **19°/frame** (was 145°), with no upright flash. No joint or weapon below the floor. Planted-foot skating is gone except ≤ 0.13 m/frame in death / axe-kick recovery |
| 6 | Effects | **PASS** | One pillar (under the launched soldier). The slam ring (1.6 m) appears at the soldier's feet when it lands (f781). Fa jin cone 3.4 m = hit. Every move still emits its effect at active start |
| 7 | Martial-arts movements | **PASS** | At impact: spin kick 5-6°, launcher 2°, crescent 6°, tornado 12°. Foot 0.10-0.30 m from the body (was 0.8-1.1 m). Contact sheet `sheet_strikes.png` shows every strike meeting its target |
| 8 | 5 firebending combo moves | **PASS** | All 5 land on a Dao Soldier at 1.8 / 3 / 5.5 m, passive or active, and a second string lands too. Against 2 soldiers (12 layouts, neutral stick or pushed): 5/5 on the chosen soldier, and no hits taken during the string |
| 9 | Close / mid / long | **PASS** | Wheel ≤ 3.8 m all round (3 soldiers), whip 2-7.2 m, blast ≤ 26 m, zip ≤ 13-14 m |

## 4. Report 03 findings, re-checked

| ID | Status | Re-check |
|---|---|---|
| V-01 chords vs parry | **Fixed** | 18/18 abilities, 0 whiffs; parry after chord works (harness `chord`, `parryafter`) |
| V-02 plunge in the air | **Fixed** | LB+X after an air jab or during an air dash gives the Falling Axe Kick (18 dmg). Whip / wheel / blast wait for the ground; see W-04 |
| V-03 string vs soldier | **Fixed**, but see W-01 | 5/5 land in all 6 runs |
| V-04 zip reach | **Fixed** | Foot 0.32 m from the body at impact (anim f1033); 80/80 vs moving targets |
| V-05 slam / knockdown pops | **Fixed** | ≤ 19°/frame, nothing under the floor |
| V-06 stale hand-over | **Fixed**, see W-06 | README branch is current; the builder offers **Update (recommended)** for stale move sets / player tuning |
| V-07 kick aim | **Fixed** | 0-15° at impact |
| V-08 lunge loses target | **Fixed** | 36/36 |
| V-09 neutral counter | **Fixed**, see W-02 | 23 dmg ×4 |
| V-10 air string over a standing foe | **Fixed** | Air-dash string peaks at 1.27 m and descends (was 2.38 m) |
| V-11 axe landing snap | **Fixed** | Worst axe-kick frame jump 0.60 m, on the rising kick (was 1.48 m on the landing) |
| V-12 skating feet | **Fixed** | Replaced by steps and "leap" hops; see W-03, W-07 |
| V-13 LB held after parry | **Fixed** (reader, Unity only) | Chord window 0.5 s; see W-05 |
| V-14 effect placement | **Fixed** | See checklist row 6 |
| V-15 pack-clip cuts | **Fixed in code** | 0.15 s fade; needs the Editor with packs |
| V-16 through the floor | **Fixed** | 0 weapon or joint frames below the floor (was 4 kinds) |
| V-17 crossbow wind-up | **Fixed** | Hand rises 0.99 → 1.43 m and the bow levels to aim during the shot |
| V-18 corpse overlap | Not addressed | 26 cm torso overlap on the dying crossbowman (Polish) |
| V-19 jab reach | Unchanged (by design) | Jab tip 0.76-0.94 m from the body when it registers |
| V-20 statics | **Fixed** | `GreyboxShapes.ResetStatics` |
| V-21 stale numbers | **Mostly fixed** | Spec HP 180 / poise 45 / break-out 6 in 2.0 s; comment and label fixed. README still says the masher wins 65% (W-09) |
| V-22 aim column | **Fixed** | `anim` prints aim and tip-to-body |

## 5. New findings

| ID | Severity | Summary |
|---|---|---|
| W-01 | **Major** | The break-out never fires: the 6th hit staggers instead of arming it; masher wins 100% |
| W-02 | Minor | A jab at an enemy directly behind (≥ ~170°) whiffs, and the neutral-stick soft lock now picks that enemy in groups |
| W-03 | Minor | After a jump lands, both feet rise 21-22 cm off the floor for ~7 frames |
| W-04 | Minor | An ability or Fire Blast pressed in the air is silently dropped if you land more than 0.25 s later |
| W-05 | Minor (Unity only) | Hold LB/Q more than 0.5 s before the face button and the chord becomes a normal attack / zip / dodge |
| W-06 | Minor | The stale-tuning check ignores enemy assets |
| W-07 | Polish | Both feet off the floor during walk↔run and soldier strafe (≤ 10 cm); lunge / dodge "leaps" up to 22 cm |
| W-08 | Polish | `AerialSettings.cs:12-14`: the `AirAttacksPerJump` comment got merged onto the next field's line |
| W-09 | Polish | `docs/Prototype/README.md:64` still says the masher wins 65% against one soldier |
| W-10 | Polish | V-18 and V-19 carried over |

### W-01 · Major · The Dao Soldier's break-out never fires
- **Reproduce:** mash light (every 6 frames) into one active soldier for 30 s (harness `mashbo`, 3 seeds × 2 presets). Or run `CombatSim duels --groups soldier --bots masher`.
- **Saw:**
  - 0, 0, 0 break-out shoves in Fluid; 0, 0, 1 in Punishing. The soldier dies in 9.6-13.2 s; the player takes 52-80 damage and never heals.
  - Masher vs one soldier: **100%** in both presets. Report 02 §9 shipped 78% (Fluid) and 60% (Punishing).
  - In the timed-string test, hit 6 (the next jab) lands at f136 and the soldier goes `Staggered` on that same frame.
- **Expected:** "a 6th hit arms it" (the lead's fix note and the spec).
- **Cause:**
  - The whole string does 43 poise damage and the soldier has `MaxPoise` 45, so the next jab (+8) breaks poise on hit 6.
  - Rule order: "A stagger wipes the count" (`EnemyBrain`, break-out rule), then 1.5 s of stagger immunity. The count restarts and 6 more hits rarely fit in 2.0 s again before stamina runs out.
  - Settings: `EnemyTuning.cs:218-219` (`HitsToTrigger = 6`, `HitWindow = 2.0`) against `MaxPoise = 45` (line 21).
- **Fix (Jeremy's call; all data except the last option):**
  - Evaluate the break-out *before* the stagger on the arming hit.
  - Or raise `MaxPoise` above one string plus a jab (≥ 52), so hit 6 arms rather than staggers.
  - Or let the count survive a poise stagger.
  - Then re-run `duels` and check the masher lands near the old 65-78%.

### W-02 · Minor · A jab at an enemy directly behind whiffs; the neutral-stick soft lock now picks that enemy in groups
- **Reproduce:** harness `behind` / `behind2` / `turn`. Soldier 3.5 m in front and another 2.2 m directly behind; stick neutral; press attack.
- **Saw:**
  - The new all-round neutral soft lock scores the one behind as best (2.2 m + 1.0 angle penalty < 3.5 m).
  - The jab turns only 0 → 120° before its active frame, while the homing lunge drags the player backward toward the target.
  - The jab swings 60° wide: **nobody is hit** (before V-09's fix it hit the soldier in front).
  - A single enemy at 180° also whiffs with the stick pushed at it. At 120-165° jabs hit, and whip / blast / fa jin hit at every angle.
- **Cause:** `AttackAimYaw` + the jab's `TrackingTurnRate` can't turn 180° in its 0.12 s startup (`PlayerCombatModel.Locomotion.cs:260-269`). The neutral soft lock at 180° (`PlayerController.FindSoftTarget`, `SimPlayer.FindSoftTarget`) now selects such targets.
- **Fix:** when a homing lunge's target is outside the turn the startup allows, snap the facing at attack start (Spider-Man turns instantly) or scale the turn rate. Or, with a neutral stick, prefer targets within ~120° unless nothing is there.

### W-03 · Minor · The landing floats
- **Reproduce:** anim frames 382-389 (the jump in "Stance and footwork"); `side_float.png`.
- **Saw:** after touchdown both feet *rise*: 0.05 / 0.10 → 0.21 / 0.22 m at f385-386, back to the floor by f389, in state Locomotion / key `land`.
- **Cause:** the new air → ground landing blend (`FighterAnimator`, the "a landing never snaps" block using `LandBlendIn`) keeps blending from the tucked jump pose after contact.
- **Fix:** plant the feet first (start the blend at the ground pose's leg channels), or clamp the foot height to the floor while `Grounded`.

### W-04 · Minor · Abilities and Fire Blast pressed in the air vanish
- **Reproduce:** jump, air jab, press LB+Y (or LB+B, or RB) during the jab (harness `fajin`).
- **Saw:** nothing fires: landing came 27 frames (0.45 s) after the press, so the 0.25 s input buffer had dropped it. There's no feedback.
- **Cause:** `TryAbility` / `TrySkill` now return while `Aloft` (correct), relying on the buffer (`PlayerCombatModel.Actions.cs:213, 232`).
- **Fix:** keep that press until the landing (up to ~0.6 s), or show a small "on the ground only" cue.

### W-05 · Minor (Unity only) · The 0.5 s chord window can eat a slow chord
- **Saw (code):** `PlayerInputReader.cs`, `abilityChordWindow = 0.5f`.
  - If LB/Q has been held longer than 0.5 s, X is a light attack instead of the fa jin, Y is a zip strike instead of the whip, and B is a dodge instead of the wheel.
  - The F1 overlay says "Hold LB / L1, then hold X" with no time limit.
  - Keyboard players holding Q and moving the mouse to click are the most likely to hit it.
- **Fix:**
  - Lengthen the window (0.8-1.0 s), or reset it only after a parry actually happened on this hold (the V-13 case).
  - Mention the limit in the overlay.

### W-06 · Minor · The stale-tuning check ignores enemy assets
- **Saw:** `SandboxTuningAssets.HasStaleAssets` checks the Fire move sets and the player's soft-lock range only. Anyone who built at `859fd61` has current move sets but an `Enemy_DaoSoldier` asset with break-out 3 hits in 1.2 s. They get no prompt, and Report 03's V-03 comes back on their machine.
- **Fix:** also compare `DaoSoldier.BreakOut.HitsToTrigger` / `HitWindow` (or add a version number to the tuning assets).

### Polish (W-07 to W-10)
- **W-07:** both feet leave the floor in walk ↔ run transitions (≤ 10 cm, f289-292) and in soldier strafe (12 frames). The new lunge / dodge "leap" lifts both feet up to 22 cm for 14 frames. That's intentional (`AnimatorSettings.LeapSpeed` 6 m/s), but check in the Editor that the dodge doesn't read as floating.
- **W-08:** `AerialSettings.cs:12-14`: the `AirAttacksPerJump` comment got merged onto the end of the `AirLiftMaxHeightAboveTarget` comment.
- **W-09:** `docs/Prototype/README.md:64` still says "masher wins 65% vs one soldier".
- **W-10:** V-18 (the string walks into the dying crossbowman, 26 cm torso overlap) and V-19 (the jab registers 0.76-0.94 m before contact) carry over.

## 6. Unity-only notes

- **Pack-clip hand-off** (`BodyAnimatorDriver` LateUpdate at 50): it captures every bone each frame and slerps from a frozen copy for 0.15 s when the source changes. Cheap, and frozen during hitstop (it uses `Time.deltaTime`). It can only be proven with the packs imported.
- **Chord window** (W-05) lives in the reader. The core side (`PlayerCombatModel.ReadInput` chord block) is covered by the harness and the new tests.
- **Stale-asset dialog:** runs before the build. "Keep mine" builds with the old assets, as before.
- **Unchanged from report 03:** execution order, statics reset, and the VFX pools / materials.

## 7. Files (scratchpad, not in Git)

`/tmp/claude-0/-home-user-vaatus-revenge/08c08173-6fe0-59c1-a069-7a487d39a9a7/scratchpad/`:
- `verify4/`:
  - Runs: `anim.json`, `abilities.md`, `fuzz.md`, `duels.md`, `compile.log`, `tests.log`
  - Sheets: `sheet_strikes.png` (every strike at impact), `sheet_death.png` and `sheet_death_p.png` (death poses), `side_float.png` (W-03 / W-07)
  - Metric scripts: `py/` (knees, aim, pops, feet, interpenetration, side renderer)
- `verify/harness/`: the playtest harness. New for this pass: `mashbo`, `str2`, `zipmove`, `zipair`, `behind`, `behind2`, `turn`. Run as `dotnet bin/Release/net8.0/Harness.dll <test>`.
- No videos were rendered.

## Lead's fixes after this pass (29 Sep)

| Finding | Fix | Evidence |
|---|---|---|
| W-01 (Major) | Soldier poise 45 → 52 (the string plus a jab no longer staggers first), and an **armed break-out braces the soldier**: poise can't break until the shove has happened, so a 7th mash hit can't stagger it and wipe the counter it just earned (`EnemyBrain.ReceiveHit`). | Harness `mashbo`: shoves 0/0/0 (Fluid) and 0/0/1 (Punishing) → **1 in every run** (6/6). Tests pin both the bracing and a plain stagger without a break-out. |
| W-02 | String, air and launcher strikes snap your facing to their target at the start (Spider-Man's free-flow snap). | Harness `behind`: an enemy 2.2 m behind with a neutral stick is now hit ("turned round"). |
| W-05 | F1 overlay: "face button within 0.5 s of LB". | — |
| W-06 | The stale-tuning check also flags a saved `Enemy_DaoSoldier` with the old 3-hit break-out or poise 45. | — |
| Polish | `AerialSettings` comment fixed; README's old 65% masher line replaced. | — |
| W-03 and the walk/run foot polish | With the body engineer. | — |
| W-04 | Left as is: an ability or Fire Blast pressed in the air waits in the input buffer (0.25 s) for the landing, like every buffered press. | — |

**Balance note for Jeremy (not a bug):** duels vs soldiers after these fixes, 40 seeds. The shove now fires, but the 5-hit string does more damage than the old 3-hit chain (56 vs 32 per string), so a lone soldier dies before mashing is punished much.

| Bot | Fluid, 1 soldier | Fluid, 2 soldiers | Punishing, 1 soldier | Punishing, 2 soldiers |
|---|---|---|---|---|
| masher | 98% (damage taken 71) | 45% | 95% (82) | 33% |
| react / anticipate / guard / fajin | 98–100% | 98–100% | 100% | 50–95% |
| aggressive | 100% | 80% | 100% | 23% |

Levers if mashing one soldier should lose more often: `Enemy_DaoSoldier` → MaxHealth, `BreakOut.Attack.Move.Damage` (30), `BreakOut.Cooldown` (2 s), or `BreakOut.HitsToTrigger` (6).
