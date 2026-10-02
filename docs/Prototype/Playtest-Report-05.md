# Playtest Report 05: the four elements, rhythm combos, the Spider-Man 2 dodge, danger sense and the tutorial

> Final judge's report on Build 05, `claude/dazzling-clarke-njm3m5` at **`fc211ff`** (2 Oct 2026), after seven verify rounds against the [Build 05 spec](Build-05-Spec.md) and [How-To-Play](How-To-Play.md).
>
> How it was checked:
> - Everything is headless: `tools/compile-check.sh`, `tools/run-core-tests.sh`, every CombatSim scenario in Release in an isolated build folder, the `anim` trace rendered to contact sheets with `tools/render/render_fight.py` and looked at, and the Unity-side scripts read by hand.
> - The judge changed no game code, tests, tools or docs other than this file.
> - **Nothing has run in the Unity Editor yet.** Section 9 lists what only the Editor can prove.
>
> The round 7 judge pass was made on `3992823` and said **not yet**, with five Majors (J7-01 to J7-05). The round 7 fix commit `fc211ff` landed after that pass; this report re-checks each of the five on `fc211ff` (section 4) and judges that commit.

## 1. For David, in plain words

**Is it ready?** Yes, for its first real playtest in the Unity Editor: **PASS**. Everything a machine can check is green: the code compiles the way Unity would (5/5), all 677 core tests pass, the nine CombatSim scenarios that cover the brief all exit 0 with no MISS, the animation trace shows 111/111 animation keys with every built-in check OK, and the five problems the last judge pass found are fixed and re-verified here. What nobody has done yet is press Play in Unity. That is the next step, and section 9 says exactly what to look for when you do.

**What already feels like Spider-Man 2 (measured, not hoped):**
- **Dodging never breaks your combo.** Across 40 seeds in every element on both presets, X after a dodge, a zip strike or an ability continues the string 100 % of the time, on the keyboard path and on a real Xbox pad path. While you dodge you keep facing the fight (P95 facing error 0.7-0.8 degrees). Push toward the enemy and you slip in to 0.60 m every time; push away and you hop 3.0-4.8 m out; push sideways and you circle them (150-178 degrees over two slips, staying within 0.12 m of the same distance). X late in a dodge is the **dodge strike**, which dashes back in and lands with the bodies 0.30-0.38 m apart.
- **Rhythm combos.** A bot that presses on the beat does 2.68x (Fluid) / 2.06x (Punishing) the string damage of a bot that mashes, and the masher never triggers the pause finisher by accident (0 in 40 x 60 s). The gold ring that closes on the circle is drawn 0.28-0.57 s before each beat, so you can anticipate it.
- **Launchers and juggles.** Hold X for the launcher, three hits in the air, a slam to bring them down; switching element in the air works too (391 airborne switch strikes in the switch scenario, 0 over the air-attack cap).
- **The part Spider-Man 2 doesn't have: element switching mid-string.** Hold RB, then the colour-matched face button (B red Fire, X blue Water, A green Earth, Y yellow Air). The string carries on in the new element with its next hit, the switch strike hits harder, and landing more elements in one combo gives MIX 2 / 3 / 4 (harder hits, a launch, a Momentum top-up or a guard break). The switcher bot reaches a MIX 4 finisher in 100 % of seeds against the sparring partner on both presets. On a real pad path, 2,743 RB + face chords produced 0 stray jumps / dodges / zips / skills and 0 lost picks, and the chord adds 0 frames of latency.
- **Danger sense.** The mark above your head comes within 1 frame of the right moment, every warning resolves, and a bot that dodges on the white flash takes far less damage than one that reacts to the swing. Since round 7 the **red** mark is real: the soldier's Delayed Thrust can't be parried, so it shows red on your head and glows red on the soldier; every parryable attack is gold / amber.

**What to try first (gamepad, Fluid):**
1. Start the sandbox and press **View** for the 11-step tutorial. If the sandbox builder offers to **Update** the player, tutorial or enemy assets, say yes: round 7 bumped their data versions (`PlayerTuning` 8, `TutorialScript` 7, `EnemyTuning` 1).
2. **X X X X X** pressing as each gold ring touches the circle. It should get visibly faster and the last hit should knock the partner up.
3. **X X, B away, X.** The X should be a dodge strike that dashes back in as hit 3, and the hit counter should keep counting.
4. **X X, hold RB then X, let go, X X.** Two Fire hits, three Water hits ending on Water's finisher.
5. **X, hold RB then X (Water), X, hold RB then A (Earth), X.** Three elements landed, so Earth's finisher throws them up (MIX 3).
6. **X X, wait for the blue circle, X X.** The pause finisher.
7. Let the partner attack (tutorial step 10): parry the gold ones with LB, dodge the red held thrust with B on the white flash.
8. Run past the partner holding B: on Fluid a sprint starts with a dash, and since round 7 it keeps going the way you were running instead of swinging round the enemy.

**Two things to know before you judge the feel:** the masher bot still beats one Dao Soldier (an accepted balance item for Jeremy, section 7), and two round 7 design calls were made on Jeremy's behalf and need his yes (the red thrust and the sprint dash, section 7).

## 2. Baseline (tools, on `fc211ff`)

| Check | Result |
|---|---|
| `tools/compile-check.sh` (isolated `VR_BUILD_DIR`) | **PASS** Core, Game (editor), Game (player build), Editor, project lint; no FAIL line |
| `tools/run-core-tests.sh` | **677 / 677** passed (644 at the round 7 judge pass; 33 new tests in round 7) |
| CombatSim `framedata`, `elements`, `buffer`, `controls` | exit 0, 0 MISS. Pad path adds 0 frames to input-to-action; every element's 5-hit string DPS inside the 22-32 band |
| CombatSim `rhythm` (40 seeds) | exit 0. On-beat: rhythm bot 99 % / 92 %, masher 3 % / 2 %; string DPS rhythm / masher **2.68** Fluid, **2.06** Punishing (targets 1.30 / 1.25); masher pause moves 0; chimes never exceed the on-beat rate |
| CombatSim `dodgeflow` (40 seeds, keyboard and pad paths) | exit 0, every cell OK (section 1 numbers) |
| CombatSim `switch` (40 seeds) | exit 0. Cooldown denials that dropped the string 0 / 0; air cap exceeded 0; MIX 4 by `switcher` 100 % vs the partner (both presets), 88 % in a Fluid duel, 33 % in a Punishing duel (accepted); real pad 0 stray actions, 0 lost picks |
| CombatSim `danger` (40 seeds, keyboard and pad paths) | exit 0. Melee warning error P95 / max 1.00 / 1.00 frame, 0 unwarned, 100 % of warnings resolved in every row; Fluid `sense` vs `react` perfect-dodge rate +51 / +47 / +23 points with 24-82 less damage (target +20), identical on the pad path; panic dodges on the Delayed Thrust 0 % everywhere |
| CombatSim `anim` | exit 0. 11,160 frames, **111 / 111** animation keys, all 17 built-in checks OK including the new round 7 one (foot below 0.2 m moving over 0.3 m along the floor in one frame: 0 of 16,722 foot-frames), no NaN |
| CombatSim `fairness` (20 seeds) | exit 0. The frame-perfect oracle takes 0 hits / min against one or two soldiers on both presets; in the 3-4 enemy groups 0.75-2.35 hits / min, all bolts or a second attacker's strike (as in reports 01-04). Heavy Overhead and Delayed Thrust still show "must anticipate" against a pure reaction |
| CombatSim `duels` (20 seeds, both presets, 9 bots x 5 groups) | exit 0, no invariant violations; pad vs keyboard rows all OK. Masher beats one soldier 100 % (accepted); guard-only bot numbers in section 7 |
| CombatSim `fuzz --quick` | 8 runs (30 / 60 / 144 fps, spikes and pauses, pad chords and keys), **0 violations**, no NaN, every dodge and hitbox closed |
| Repo hygiene | `git status` clean before this report; no stray files at the filesystem root (the round 1 `/anim.md`, `/fuzz.md` are gone) |

## 3. Verdict checklist

| # | Item | Result | Evidence |
|---|---|---|---|
| 1 | Compiles (5/5) and all tests green | **PASS** | Section 2 |
| 2 | Dodge never breaks the combo and keeps you facing the fight | **PASS** | `dodgeflow`: "Dodge (evade-out) then X" 100 % for all 8 preset x element rows on keyboard and real-pad paths; zip strike then X and ability then X 100 %; facing error P95 0.7-0.8 deg (target 30). Tests `DodgeMidStringKeepsChainIndex`, `DodgeKeepsFacingEngagedEnemy` |
| 3 | Dodge-in / dodge-out / dodge strike work | **PASS** | Slip-in stops at a 0.60 m gap 100 %; evade-out 3.0-4.8 m per element; side-slips circle within 0.00-0.12 m over 154-178 deg; dodge strike body gap 0.30-0.38 m; exits brake with no dead stop (largest one-frame drop 1.73 m/s). A point-blank slip-in is still a stationary duck (S7-04, section 6) |
| 4 | Rhythm on-beat bonus works and mashing is worse | **PASS** | `rhythm`: 2.68 / 2.06 DPS ratio; masher graded Mashed 62 % / 55 % with 0 accidental pauses; `reactpress` (pressing on the flash) grades Late 93 %, which is why the docs teach anticipation |
| 5 | Pause finisher per element | **PASS** | `CombatPauseBranchTests`, `Step3PassesDoingExactlyWhatThePromptSays` x4; pauser bot 37-44 pause moves / min, masher 0; the anim trace plays Rising Phoenix Kick, Part the Wild Horse's Mane, Boulder Hurl and Whirlwind as pause moves |
| 6 | Launcher + air string + juggles | **PASS** | `AerialCombatTests` green; `switch`: 1,299 jumps / 1,341 air attacks, 0 over the cap; anim scenes "launcher, air string, plunge" for all four elements with landing checks OK; `Step7LaunchAndJuggleCompletes` |
| 7 | Mid-string element switch continues the string in all 4 elements | **PASS** | `ElementSwitchTests` green; `switch`: 1,121 / 889 switch strikes, 0 dropped strings; Fluid one X between switches works in all 24 orders (`FluidOneXBetweenSwitchesWorksInEveryOrder`); the switch scene contact sheet shows Flame Cross -> Push (MIX 2) -> Mountain Quake (MIX 3) in one string |
| 8 | MIX levels | **PASS** | `ElementMixTests` green; switcher MIX 4 100 % vs the partner; `HowToPlayFullMixRecipeBreaksGuard`. MIX 3's launch on Fluid is hard to tell from the perfect-string launch (S7-08, Jeremy's tuning call) |
| 9 | 5-hit string + close / mid / long range per element | **PASS** | `elements`: every element has LightChain[0..4] with DPS 23.4-29.5, poise budgets under 52, beat data 0 failures; the anim strike table plays each element's ranged skill, mid-range and close all-round moves plus zip and sprint attacks (73 distinct moves, extension 0.89-1.00) |
| 10 | Each element plays like its martial art | **PASS** | Data: Water wider windows, pulls, heal-on-hit; Earth 11-21 damage per hit, armour, held block; Air 2-4 sub-hits per move, orbits; Fire Momentum. Contact sheets: Fire upright guard and straight strikes, Earth low horse stance, Air circle-walking with turning palms, Water flowing palms |
| 11 | VFX for every effect key in all 4 elements | **PASS** (rendering unverifiable) | 13 effect keys, all dispatched by `ElementMoveEffects` / `PlayerFeedback`; `ElementMoveSetDataTests` pins every move's key; a style per element; round 7 spread the switch-flash hues at least 40 deg apart (`ElementSwitchPalette`, test) and made Earth's hit pop dust |
| 12 | Humanoid full-limb fighters with martial-arts poses and no glaring animation bugs | **PASS** | Full-limb humanoids with distinct stances in the contact sheets; 111/111 keys; all 17 anim checks OK. **J7-02 re-checked:** the round 7 judge's own foot scan, which found about 90 sliding foot-frames in the round 6 trace, finds 2 on `fc211ff` (both kicks) and 0 over 0.5 m; the rendered dodge starts (Fire evade-out frames 925-933, side-slip 849-854, sprint dash 184-188) are tucked hops with both feet off the floor from the first moving frame. Left: the trailing foot slides ~0.45 m on a hard stop (S7-05) |
| 13 | Danger sense | **PASS** | `danger`: warning error within 1 frame of the right moment, 100 % resolved, `sense` beats `react` by +51 / +47 / +23 points on Fluid on both input paths, 0 % panic dodges on the thrust. **J7-03 re-checked:** `EnemyTuning.cs` sets `thrust.Parryable = false` on Delayed Thrust; `TelegraphLook.GlowsMustDodgeRed` is the one rule for the glow and the mark, and `EnemyRigPresenter.cs:67` calls `feedback.TelegraphColor(e.Telegraph, e.Move)`; parryable heavies now glow amber; `SandboxRosterHasAMustDodgeAttackAndOnlyItGlowsRed` and the step 10 parry + dodge test are green |
| 14 | Tutorial passable end to end with correct Xbox prompts | **PASS** | `TutorialTrackerTests` Step1..Step11 green, including step 10 needing one parry and one dodge; prompts use the colour-matched layout and "hold {RB} then ..., let go, {X}" (S7-09); step 6's hint says the finisher comes after hit 4 (S7-10); every hint under the 140-character budget |
| 15 | How-To-Play recipes true | **PASS** | `HowToPlayThreeElementLaunchRecipeLaunches`, `HowToPlayFullMixRecipeBreaksGuard` green. **J7-04 re-checked:** the MIX section now says two X between switches on Punishing (or the pause), and `PunishingOneXBetweenSwitchesIsRefusedWithFireOrAirInTheMiddle` documents why. **J7-05 re-checked:** "Hold B to sprint" is described honestly and the `controls` run-past table is 0.00 m sideways pull at 45 / 90 / -45 / -90 deg (target 0.5 m), dodge kind Traverse, then Sprinting |
| 16 | No likely Unity compile / runtime errors in new code | **PASS** | Compile 5/5 in editor and player-build flavours plus `unity-lint`; every mutable static outside Core has a `RuntimeInitializeOnLoadMethod` reset; no Unity API in Core. The round 7 Unity-only change (the legs follow the character controller's real velocity, S7-06) is a two-line feed change in `PlayerController.cs` / `PlayerAnimationFeed.cs` and compiles; its feel is Editor-only |
| 17 | Lore-safe text | **PASS** | No lightning, redirection, metal, blood, later Avatars, pro-bending, lava, combustion or mecha outside the canon notes that forbid them; 83 move names are Tai Chi / Hung Gar / Bagua / Northern Shaolin postures or canon basics; Earth in the air is dust only (0 rock in 16 airborne Earth effects). The flame-pendant question for David is now recorded in `docs/David's Plans/01-Lore-and-Universe.md` (S7-15) |

## 4. The round 7 Majors, re-checked on `fc211ff`

| ID | Was | Fix | Re-check (judge's own evidence) |
|---|---|---|---|
| J7-01 | Hold RB (the element modifier), tap LB to parry: the ranged skill fired when RB was let go, cancelling the guard; the hit landed on both presets | `PadChordReader.Read()` step 3: LB pressed while RB is held marks the hold as used | `RbHeldThenLbTapThenReleaseFiresNoSkill` (0 skills; a later lone RB tap still fires 1), `ParryWithRbHeldStillDeflects` (both presets: state stays Guarding, `HitFromFront(20)` = Parried, 0 attacks started), `EarthBlockWithRbReleasedMidHoldStaysUp` (both presets). All green in the 677. The lone-RB-tap tests still pass |
| J7-02 | Every grounded dodge left both feet planted on its first moving frame, then teleported them 0.4-0.7 m | `FighterAnimator.LimitFeetGlide`: a foot under `ReleaseStepHeight` (0.2 m) moves at most `MaxReleaseStep` (0.25 m) over the floor per frame, is lifted clear and catches up in the air (both data in `AnimatorSettings`); the `anim` scenario now fails on it | New anim check 0 / 16,722 foot-frames; my scan of the trace (same method as the round 7 finding) 2 frames over 0.3 m (both kicks), 0 over 0.5 m, was ~90; `AnimationTests.DodgeStartsNeverJumpAFootAlongTheFloor` green; the rendered frames look right (section 3, item 12) |
| J7-03 | The red "must dodge" mark could never appear while Heavy Overhead and Delayed Thrust glowed red and step 10 / F1 / How-To-Play taught "red = B dodge" | Option (a): Delayed Thrust `Parryable = false`; one rule (`TelegraphLook`) for glow and mark, parryable heavies amber; tutorial step 10 needs a parry and a dodge; `EnemyTuning.DataVersion` so the builder offers the asset update | Code read as in section 3 item 13; tests green; `danger` scenario exit 0 with the sense-vs-react margins unchanged from the round 7 pass. Balance side effect for Jeremy: the guard-only bot, which never dodges, now eats the thrust (section 7) |
| J7-04 | How-To-Play said one X between switches is usually enough on Punishing; 12 / 24 orders were refused | How-To-Play MIX section reworded to two X (or the pause); a test documents the refusal | Text verified in the file; `PunishingOneXBetweenSwitchesIsRefusedWithFireOrAirInTheMiddle` and `PunishingTwoXBetweenSwitchesWorksInEveryOrder` green; `ElementSwitchTuning` Punishing cooldown unchanged at 0.45 s (Jeremy may lower it to 0.25 s and flip the test and the docs instead) |
| J7-05 | On Fluid, holding B to sprint always started with a focus-steered dodge (a 4.16 m side-slip round a foe 45 deg off) | `PlanDodge`: from a run (>= `RunDodgeMinSpeed` 3.5 m/s), nobody locked, no strike coming and the stick more than `RunDodgeSlipInAngle` (20 deg) off the foe, the dodge is a plain Traverse along the stick; Punishing unchanged (`RunDodgeKeepsHeading` false there); `PlayerTuning.CurrentDataVersion` 8 | `controls` "Hold B while running past an enemy": Fluid 45 / 90 / -45 / -90 deg all Traverse, 0.00 m sideways, 6.90 m forward, then Sprinting (OK); Punishing: no dodge, sprint only (OK). `HoldBWhileRunningPastAnEnemyKeepsTheHeading` (6 cases) and `RunDodgeStillSlipsInAtTheEnemyAndLockOnKeepsTheCircle` green; the anim trace's sprint-start dodge at frame 185 is captioned Traverse. Tutorial step 5's slip-in from a standstill is unchanged |

## 5. Rounds history: what was found and fixed

Seven verify rounds ran on this branch after the Build 05 merge (`f8bd9b9`). Each round was an independent judge pass (reproducing every claim headless) followed by a fix commit; the next round re-verified the fixes and hunted regressions. The spec's decision log (sections 8.2 to 8.8) records every call made.

| Round | Fix commit | Majors found / fixed | Headline |
|---|---|---|---|
| 1 | `8b17342` | 12 / 12 | The pause finisher was taught as X X (wait) X but needs X X (wait) X X; "half a second" and "press on the flash" replaced by the blue pause cue and anticipation teaching; the spec's old Y-Fire / B-Water layout replaced by the colour-matched one; same-element and cooldown denials now shake the wheel; switch cooldown 0.30 -> 0.20 s (Fluid) so one X between switches works in every order; dodge strikes and sprint attacks arrive as they strike instead of hitting from 2-2.7 m; chained dodges restart their clip; dodge exits brake instead of stopping dead; the gait hands back to stance; side-slips circle the foe; the air dash drifts; Earth is dust only in the air |
| 2 | `5aa7a4c` | 6 / 6 | A same-frame RB + face chord no longer fires the ranged skill on release (pure-C# `PadChordReader`); lunges no longer drag the rear leg into a "Superman" pose; every airborne Earth path is dust; tutorial hints wrap; a Late beat press shows LATE; the Punishing MIX recipe can launch |
| 3 | `f43c2f8` | 8 / 8 | The beat ring is predictive (0.28-0.57 s ahead); Spiral Kick no longer snaps 59 deg; gap-closing lunges stride instead of skating; Water / Earth / Air effects drawn at their true hit radius; the air-string vortex follows the chest; airborne switch to Earth is dust; grounded Earth stone rises from the floor, never from the body; Air Shield and Stone Tent keep clear of the camera |
| 4 | `e59edcc` | 3 / 3 | A face button 1-4 frames before RB no longer jumps / dodges / zips on top of the switch (thumb-first chords); the dodge strike no longer skates; Boulder Toss / Hurl rise out of the ground at their hit size |
| 5 | `d2c7115` | (round 5 set) | Modifier-first RB chords with 0 added pad latency (the round 4 hold-back removed), Late switch strikes, air finishers land on the floor, real-pad CombatSim variants of `dodgeflow`, `switch` and `danger` |
| 6 | `3992823` | (round 6 set) | Landings without leg snaps, the gait faces the way it goes, the finisher's hold is kept, an air switch after the finisher, the on-beat chime only once confirmed |
| 7 | `fc211ff` | 5 / 5 | Section 4: a parry with RB held, no foot jumps at dodge starts, a real red danger mark, honest Punishing switch advice, hold-B sprint keeps its heading; plus 13 of the 18 should-fixes (S7-01, 02, 06, 09-18) |

Judge findings that were rejected or merged along the way are listed in each round's decision-log entry; in round 7 the only corrections were a merge of two duplicate findings and a recount (12 of 24 distinct element orders refused, not 27 of 36 ordered pairs with repeats).

## 6. Remaining known issues

Should-fixes left open after round 7 (none blocks the Editor playtest; all are polish or Jeremy's call):

| ID | Issue | Where | Suggested fix |
|---|---|---|---|
| S7-03 | One-frame dead stop at the start of every dodge and sprint attack (re-measured on `fc211ff`: run 4.8 -> 0.0 -> 27.5 m/s across the dodge's first frame at anim frame 185) | `PlayerCombatModel.Defense.cs` `StartDodge`, `action.Begin()` | Advance the action clock by that frame's dt on the start tick, or carry the incoming locomotion velocity for that one frame; add a `dodgeflow` / `framedata` check |
| S7-04 | Dodging toward an enemy from point blank is a stationary 0.25 s duck (the slip-in's travel clamps to 0 inside `SlipInStopGap`; anim frames 812-817 travel 0.0 m) | `PlanDodge` SlipIn branch | Jeremy's call: make it a slip-past to the flank (reuse `CircleStep`) or a short vault behind the foe, so a dodge always relocates you as in Spider-Man 2 |
| S7-05 | Stopping a walk or run slides the trailing foot ~0.45 m along the floor in 2-3 frames (38 such frames in Locomotion / idle) | `FighterAnimator` stop blend | Route a free foot with more than `StepDistance` to go through the step logic instead of the `StopSmoothing` blend; add an anim check for a free foot below 0.12 m moving faster than ~6 m/s |
| S7-07 | A launched enemy pops 0.18 m up and lifts both feet 0.27 m in one frame, then hangs through hitstop; Mountain Quake shoves it 0.34 m sideways in that frame | `EnemyAnimationFeed` / `FighterAnimator` | Crossfade into the launched clip over 2-3 frames (even during hitstop) and apply knockback as velocity over a few frames |
| S7-08 | On Fluid the MIX 3 "finisher throws them up" is invisible: a plain on-beat string already launches at 9 m/s (`RhythmTuning.PerfectStringLaunchSpeed`) and MIX 3 at 10 m/s (`MixTuning.FinisherLaunchSpeed`); Earth's pause route launches on Raise the Boulder before the MIX finisher | Tuning | Jeremy's call: a clearly higher MIX 3 launch (e.g. 13 m/s) with its own VFX, or drop the perfect-string launch so the launch is MIX's reward |
| R6-S03 / S04 / S05 | From round 6: the backstep's planted-foot skate, a reduced-speed turn after an evade-out with the stick held, side-slip and zip-strike polish | Animator / dodge | Listed in spec 8.7 |
| Art | The generated player model (`.glb`) is lost: no generator, account or job was recorded. The character README now recommends the Blender route (S7-14) | `game/Assets/_Project/Art/Characters/README.md` | Make one in Blender, or regenerate and record the source |
| Lore | The chosen character sheet's flame-swirl pendant and all-crimson outfit read as a Fire-born Avatar, which would place him in the cycle (S7-15). The procedural body is neutral today; the risk arrives with the textured model | `docs/David's Plans/01-Lore-and-Universe.md` | David to answer before the model is textured |

Accepted (not re-reported): the masher bot beats one basic Dao Soldier (section 7); the Punishing switcher's MIX 4 rate in a duel (33 %); CombatSim-only bolt warning timing when the player moves (the Unity side re-times every frame); nothing has run in the Unity Editor.

## 7. Jeremy's balance list

Design and balance calls made on Jeremy's behalf during the seven rounds, or left open for him. All are data (tuning assets / `Create*` defaults), not code, unless marked.

| # | Item | Current value / state | Jeremy's call |
|---|---|---|---|
| 1 | **Red thrust (J7-03, round 7, please confirm).** The Dao Soldier's Delayed Thrust is now unparryable so the red mark and red glow are real; Earth's block still soaks it | `EnemyTuning.CreateDaoSoldierAttacks()` `thrust.Parryable = false` | Keep (a), or revert to all-parryable and recolour the red glows gold (b). Side effect measured in `duels` (20 seeds, Fluid): the **guard-only bot**, which never dodges, now takes the thrust. Fixer's 40-seed before / after (Fluid): vs 2 soldiers 98 % -> 88 % wins, damage 82 -> 98; vs 3 enemies 75 % -> 70 %; vs the full ring 43 % -> 28 %, deaths 23 -> 29. My 20-seed run on `fc211ff`: 100 / 80 / 65 / 20 % (Fluid), 100 / 85 / 45 / 5 % (Punishing). A player who parries and dodges (the `sense` and `react` bots) is unchanged |
| 2 | **Sprint dash keeps its heading (J7-05, round 7, please confirm).** On Fluid a dodge from a run with nobody locked and nothing incoming is a plain dash along the stick | `PlayerTuning.RunDodgeKeepsHeading` true, `RunDodgeMinSpeed` 3.5 m/s, `RunDodgeSlipInAngle` 20 deg; Punishing false | Keep, or widen / narrow the slip-in angle. The alternative (sprint on L3, B a pure dodge) is in the round 7 review if the dash-into-sprint itself is the problem |
| 3 | **The masher beats one Dao Soldier** | `duels` (20 seeds): masher vs one soldier wins **100 %** on Fluid (12.8 s, 59 damage taken) and **100 %** on Punishing (13.1 s, 68 damage); vs two soldiers 65 % / 45 %; vs three or more 0-15 % | Soldier damage / poise / break-out are the levers (`Enemy_DaoSoldier` -> `BreakOut`); the break-out fires since report 04 but one soldier still dies first |
| 4 | **Punishing switch cooldown** | `ElementSwitchTuning.CreatePunishing().Cooldown` 0.45 s: two X between switches, or the pause | Lower to 0.25 s if one X should work on Punishing (clears Air's 0.26 s gap); flip `PunishingOneXBetweenSwitchesIsRefusedWithFireOrAirInTheMiddle`, How-To-Play and spec 8.2 item 3 |
| 5 | **Fluid `BeatLate` 0.10 -> 0.15 s** (open since round 1) | 0.10 s; a press in reaction to the flash grades Late 93 % | Loosen it to forgive reaction presses, or keep teaching anticipation |
| 6 | **Punishing pause band** (round 2) | Air 0.25 s, Water 0.28 s: about one reaction long | Raise Punishing `PauseGrace` to ~0.35 s? |
| 7 | **Punishing X pressed early in a dodge expires** (round 2) | 0.2 s buffer vs a dodge that can't be cut before 0.48 s (`CombatTimingTests` pins it) | Keep as Punishing's rule, or lengthen the buffer |
| 8 | **MIX 3 launch vs the perfect-string launch on Fluid** (S7-08) | 10 m/s vs 9 m/s | See section 6 |
| 9 | **MIX 4 reward** | Launch + Momentum top-up; guard break only on a foe too heavy to launch | Whether MIX 4 should break guard instead of launching (spec 8.2 open question 2) |
| 10 | **Free Fluid dodges, three-dodge chain** | Dodges cost no stamina on Fluid; chain max Earth 2, Fire / Water 3, Air 4 (2 on Punishing), then `ChainCooldown` | Shipped as specified (spec 8.1 item 5) |
| 11 | **Mashing is far worse than "a little slower"** | DPS ratio 2.68 / 2.06 vs the 1.30 target; mashing drains Earth / Air stamina | Decide whether the gap is the intended punishment or should narrow |
| 12 | **Point-blank slip-in** (S7-04) | A stationary duck | Slip-past or vault (section 6) |
| 13 | **Gold means two things** (round 2, with David) | The beat ring and the parry mark are both gold | Give the beat its own colour, or the parry mark another |

## 8. Things only the Editor can show (what to look for when you press Play)

Everything below compiled and was read by hand, but has never been rendered or felt:

- **The HUD and VFX as drawn.** The element wheel and its shake / amber dots, the beat ring (gold, blue while the pause band is open, LATE under it), the danger mark (gold / red / white), the enemy's wind-up glow (yellow / amber / **red only on the Delayed Thrust** / violet break-out), the tutorial panel wrapping and keeping clear of the player bars at 4:3 and 16:10, the F1 / pause pages. Check the glow and the mark never disagree in colour.
- **Element VFX.** Rendering of all 13 effect keys per element with and without the effect images; Earth's dust-only hit pop; the switch flash hues; Air Shield's dome and Stone Tent's slabs staying clear of the camera.
- **Animation on the real body.** The procedural humanoid in the Editor, the chosen painterly look, and the optional rigged GLB through the avatar mirror (the generated model is lost; a Blender one is recommended). Round 7's "legs follow the real velocity" (S7-06): walk into a wall or a soldier and the legs should stop striding instead of skating.
- **Input System timing on a real Xbox pad.** The modifier-first RB chord, the 0.08 s grace for a face button just before RB, LB during an RB hold (J7-01), hold B to sprint, and rumble. CombatSim's pad is a hand-written stand-in that mirrors the reader's rules.
- **Sandbox builder prompts.** The **Update** offer for `Player` (DataVersion 8), `Tutorial` (7) and the enemy assets (`EnemyTuning.DataVersion` 1, new): on an older project the thrust stays parryable until the update is accepted.
- **Audio.** Beat chimes (only once a hit on the beat has started), the pass chime in the tutorial, the heal cue.
- **Package `com.unity.cloud.gltfast` 6.19.0** in `manifest.json`: its existence could not be verified offline and it has no `packages-lock.json` entry (open since round 1, S-24).
- **Performance** of the procedural animator, the VFX and the HUD at 60 fps on David's RTX 4070, and on Jeremy's still-unknown PC.

## 9. How this report was produced

```
export VR_BUILD_DIR=$(mktemp -d)/vr && export TMPDIR=$(mktemp -d)
tools/compile-check.sh                                   # 5 PASS lines, no FAIL
tools/run-core-tests.sh                                  # 677 / 677
dotnet run --project tools/CombatSim -c Release -- framedata | elements | buffer | controls | rhythm | dodgeflow | switch
dotnet run --project tools/CombatSim -c Release -- anim --out $TMPDIR/anim.json   # 111 / 111, all checks OK
dotnet run --project tools/CombatSim -c Release -- danger
dotnet run --project tools/CombatSim -c Release -- fairness --seeds 20
dotnet run --project tools/CombatSim -c Release -- duels --seeds 20
dotnet run --project tools/CombatSim -c Release -- fuzz --quick
python3 tools/render/render_fight.py $TMPDIR/anim.json --sheet evade.png --frames 925,926,927,928,929,930,931,933
python3 tools/render/render_fight.py $TMPDIR/anim.json --sheet sprint_side.png --frames 184,185,186,187,188,849,851,852,853,854
```

Plus a frame-by-frame read of the anim trace (foot heights and per-frame foot steps at every dodge start, root speed across dodge and sprint starts) and the round 7 diff of `PadChordReader.cs`, `FighterAnimator.cs`, `EnemyTuning.cs`, `EnemyAttackData.cs`, `EnemyFeedbackSettings.cs`, `EnemyRigPresenter.cs`, `PlayerTuning.cs`, `PlayerCombatModel.Defense.cs`, `TutorialScript.cs`, `TutorialTracker.cs`, `PlayerInputReader.cs`, `PlayerController.cs`, `PlayerAnimationFeed.cs` and the round 7 tests.
