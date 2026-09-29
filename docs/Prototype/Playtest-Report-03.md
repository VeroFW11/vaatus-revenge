# Playtest Report 03: humanoid fighters, martial-arts animation, aerial combat

> Independent verification of commit `859fd61` on `claude/dazzling-clarke-njm3m5` (29 Sep 2026), against the owner's request: *"Fix and redo the controls ... Spider-Man, aerial combat feel ... martial arts packages ... models closer to human figures ... motion should be fluid ... effects, basic character models with full limbs, using martial arts movements, 5 combo movements implementing firebending. A mix of close range, mid range, and long range attacks."*
>
> The verifier didn't build any of this and changed no game code, tests or tools. Everything below was measured headless: the real combat core and procedural animator, driven by scripted inputs at 60 fps. **Nothing has been seen in the Unity Editor yet.** Section 6 lists what only the Editor can prove.

## 1. Verdict

Most of what the owner asked for is there, and a lot of it works well:

- **Fighters.** They are jointed humans with all their limbs.
- **The string.** The five-strike Northern Shaolin string reads as jab, cross, snap kick, spinning kick and double palm, and every strike throws fire.
- **The juggle.** Launcher → air string → slam lands 20 times out of 20 and never loops forever.
- **Range.** The close / mid / long mix is real.
- **Pack bridge.** It is wired, and it does nothing (safely) without the packs.

It is **not ready to hand back yet**. Six Major problems hit exactly the things the owner will try first:

- **LB ability chords.** They often come out as a parry instead of the ability (V-01).
- **Plunge after the air string.** It gives a mid-air ground charge instead (V-02).
- **The five-hit string.** It never finishes on a Dao Soldier without a parry (V-03).
- **The zip strike.** It connects from about 2 m away (V-04).
- **The juggle's finish.** It pops visibly (V-05).
- **The hand-over steps.** They can leave the owner playing the old 3-hit build (V-06).

None is hard to fix.

## 2. Baseline (tools)

| Check | Result |
|---|---|
| `tools/compile-check.sh` | PASS: Core, Game (editor + player), Editor, lint |
| `tools/run-core-tests.sh` | 264 / 264 passed |
| `CombatSim abilities` | Runs. Full juggles 20/20; zip reaches 14 m / 50°; range mix as expected (see `abilities.md`) |
| `CombatSim fuzz --quick` | 8 runs (30/60/144 fps, dt spikes), **0 violations**, no NaN, every dodge, hitbox and i-frame window closes (player hitbox 1348/1347 and 1325/1324 are swings still open at the end of a run, as in report 02) |
| `CombatSim anim` | 4131 frames, 46/46 animation keys shown; the tool's "strike extension" is 1.000 for every move (but see V-07: that metric doesn't check *where* the limb points) |

## 3. Acceptance checklist

| # | Owner's item | Result | Evidence |
|---|---|---|---|
| 1 | Controls fixed and redone (Spider-Man 2 layout) | **FAIL** (Major V-01) | Bindings in `PlayerInputReader.BuildActions` and the F1 overlay match the spec table exactly. But an LB+X/Y/B chord pressed on the same frame gives **only a parry** (6/6 same-frame cases in 3.2), fa jin 3 frames after LB mid-string is lost, and every chord leaves a whiffed parry that blocks a real parry ~0.5 s later. Plus V-08, V-09, V-13 |
| 2 | Spider-Man aerial combat feel | **PARTIAL** | Hold-to-launch → Sky Jab → Crescent → Tornado Slam: 3/3 air hits, peak 2.81 m, slam + knockdown, 20/20 seeds and at 0° / 35° off-axis. The juggled soldier drifts ≤ 0.50 m from the launch point (no escape). Air dash → air string hits 3/3. Zip to a juggled soldier after an air dash hits (12.4 dmg at 2.3 m). No infinite juggle: 5 launches, 7 soldier attacks in 20 s of relaunching. Broken: V-02 (plunge), V-04 (zip reach), V-05 (slam/knockdown pops) |
| 3 | Martial-arts packages implemented | **PASS** (bridge only) | `MecanimPoseSource.AttachIfAvailable` adds nothing unless the set has a clip. Avatar/Animator are built only at runtime and any exception falls back to procedural for good. The sandbox builder attaches it to the player, the dummies and the enemies (`FireSandboxBuilder.cs:163, 266, 276`). Clip playback can't be verified here |
| 4 | Models closer to human figures, full limbs | **PASS** | 22-joint skeleton; torso, head, upper/lower arms, hands, thighs, shins and feet are separate tapered-capsule / rounded-box meshes (`HumanoidBody.cs:465-551`). Bone lengths are constant in all 4131 frames. Player: upper arm 0.29, forearm 0.26, thigh 0.43, shin 0.43 m; hips 0.89 m, head joint 1.50 m. **0 backward-bending knees** (hinge-axis test on both pelvis and foot axes, every fighter-frame). No forward-jutting elbows outside heal / death / get-up poses |
| 5 | Motion should be fluid | **PARTIAL** | Most moves are smooth: fist / foot peak 30-38 m/s, which is realistic. But V-05 (pose flip 145° in one frame; one-frame standing flash at knockdown), V-11 (1.48 m one-frame leg snap after the axe kick) and V-12 (feet skate during lunges) |
| 6 | Effects | **PASS** (Minor V-14) | Every strike emits its EffectKey at its first active frame: burst ×3, trail, cone, pillar (plus a pillar under the launched soldier), slam, whip, wheel, muzzle, and foot jets on dodge / air dash / zip. The drawn size is the hit size: whip 6.5 m / 110°, wheel 3.4 m, Phoenix Palm cone 4.5 m / 90°. The VFX pools are capped (160 pieces, 24 trails, 16 emitters, 4 whips, 4 lights) |
| 7 | Uses martial-arts movements | **PASS** (Minor V-07) | Contact sheets read as the named techniques: jab and cross from a bow stance, a chambered front snap kick at hip height, a spinning kick, a double palm push, a vertical rising kick, an air jab, a crescent kick, a tornado kick, an axe kick, a whip lash, a low crouched sweep, a flying side kick and a fa jin palm from horse stance. Enemy wind-ups are readable (the overhead raises the dao; the thrust is held aimed, then extends 1.29 → 2.19 m) |
| 8 | 5 combo movements implementing firebending | **PARTIAL** (Major V-03) | All 5 land, each with fire, on dummies and on the crossbowman (anim frames 487-576, 2339-2428). **On a fresh Dao Soldier, hit 3 always arms the break-out shove.** Hit 4 lands into armour and Phoenix Palm is always interrupted (player staggered at f90, 30 dmg), unless the player parries the shove and restarts the string |
| 9 | Close / mid / long mix | **PASS** | Still soldier, centre distance: Flame Wheel hits to 3.8 m (all round, 3 soldiers at once), Fire Whip 2-7.2 m (not at 8), string first hit to 6 m (gap closer), Fire Blast to 26 m (not 27.5), zip to 14 m |

### 3.1 Scripted playtests (real core, `Session` / `SimWorld`)

| Test | What happened |
|---|---|
| 5-hit string on a passive soldier at 1.8 / 3 / 5.5 m | The first jab lunges up to 2.6 m to close. Distance stays 1.1-1.7 m through the string (no push-out). **Break-out armed on hit 3 every time** → Phoenix Palm interrupted (V-03) |
| Same, break-out off | All 5 land, then a second string lands (palm knockback is re-closed by the gap-closer); the soldier staggers on hit 6; stamina hits 0 at the second palm |
| Parry the violet shove 2 / 5 / 8 frames early | Deflected every time, soldier staggered 1.3 s; restarting the string then lands all 5 |
| Launcher vs 2 active soldiers | Juggle completes (3 air hits). The second soldier waits (tokens) and swings when you land |
| 20 s relaunch loop, Fluid / Punishing | 5 / 4 launches, the soldier still starts 7 / 6 attacks, 26% / 22% of frames airborne: **no infinite juggle** |
| Zip to juggled soldier (launch, air-dash back 3.4 m, zip) | Rises to 2.14 m and hits at 2.3 m centre distance (see V-04) |
| Air dash toward a soldier 5 m away, then air string | 3/3 hits, but the player ends 2.4 m up kicking a *standing* soldier (V-10) |
| Fire Whip at 5 / 6 / 6.5 / 6.9 / 7.2 m | Hits all; misses at 8 m and at 60° off-axis (cone is ±55°) |
| Flame Wheel, soldiers left/right at 1.5 m and behind at 2.5 m | All three hit on the first active frame |
| Fire Blast | Hits at 2-26 m straight ahead, still or passive target |
| Fa jin on the ground (hold 0.8 s) | FaJin tier: 36 dmg, stagger |
| Heavy while simply airborne | Falling Axe Kick, ring hit 18 dmg ✔ |
| Heavy during an air-string move or an air dash | **Starts a ground charge in mid-air** (V-02) |
| Whip / Wheel / Blast during an air jab | All start in mid-air and hang at 30% gravity (V-02) |
| Parry timing vs each soldier attack | Deflect window is 9 frames (0.15 s) before the first active frame for Quick Slash, Heavy Overhead, Double Slash and Delayed Thrust; a full 5-hit counter then lands (68 dmg) |
| Side dodge 4 f before each attack, then attack | Perfect dodge ✔, counter flag ✔, **0 damage**: with a neutral stick the counter goes where the dodge faced (V-09) |
| Mashing light every 6 frames vs an active soldier, 10 s | Shoved twice, stamina 0 after ~3.5 s, then lone jabs; player ends at 20 HP; no stuck state |
| Chaining whip / wheel / blast in the air | Airborne 1.27 s vs ~0.6 s for a plain jump; not an infinite hover |
| Fuzz (random input, 4 frame rates) | No soft-locks, stuck states, NaN or huge velocities |

### 3.2 Chord behaviour through the core (inputs built exactly as `PlayerInputReader` builds them)

| LB then face button, gap | Idle | During a jab |
|---|---|---|
| X (fa jin), 0 f | **parry only** | **parry only** |
| X, 3 f | parry, then fa jin | **parry only** |
| X, 8 f | parry, then fa jin | parry, then fa jin |
| Y (whip) / B (wheel), 0 f | **parry only** | **parry only** |
| Y / B, 3 f | parry, then ability | ability (no parry) |
| Y / B, 8 f | parry, then ability | parry, then ability |

Every "parry" above is a **whiff** that locks deflecting out. A whip chord 30 or 22 frames before a Quick Slash, then a perfectly timed LB tap 5 frames before it lands: **hit** both times. With 40 frames: deflected.

## 4. Animation measurements (from `anim` JSON, 4131 frames)

| Check | Result |
|---|---|
| Bone lengths constant | Yes, every bone, every fighter (< 1 cm variation) |
| Knees bending backwards | 0 leg-frames (both hinge axes must agree; single-axis flags on the launcher, axe kick and crescent were checked visually and are turned-out legs, not hyperextension) |
| Strike limb straight at first active frame | Yes (arm 0.55 / 0.55 m, leg 0.87-1.00 m) |
| Strike limb **aimed** at the target at first active frame | Jab 5°, cross 13°, snap kick 12°, palm 12°, air jab 1°, fa jin 4° ✔. **Spin kick 63°, launcher 46°, crescent 44°, tornado 56° away**, foot 0.8-1.1 m from the body (V-07) |
| One-frame pops (> 0.25 m / frame outside dashes) | Worst: launched soldier flips (toes 1.91 m, f773), knockdown flash (1.76 m, f782), axe-kick landing (1.48 m, f817). Spinning kicks' 0.3-0.45 m/frame is real rotation speed, not a pop |
| Peak limb speed relative to the body | Player strikes 30-38 m/s (fast but human for kicks); axe kick 89 m/s and soldier launched/knockdown 105-115 m/s are the pops above |
| Feet on the floor when grounded | Within ±3 cm almost everywhere. Run/sprint float up to 9 cm and sink up to 5 cm; Flame Wheel toes 7 cm under the floor; knockdown flash 41 cm under (f782) |
| Planted-foot sliding | During gap-closing lunges the stance glides: jab 0.19, palm 0.21, spin kick 0.28 m/frame; dodge 0.64 m/frame; walk 0.18 m/frame worst (V-12) |
| Body interpenetration | Torso-in-torso only with the dying crossbowman (corpse stops blocking), worst 26 cm (Polish) |
| Weapon through the floor | Soldier dao tip to −0.95 m (get-up), −0.54 m (stagger); dummy pole −0.38 m; crossbow −0.53 m on death (Polish) |

## 5. Findings

| ID | Severity | Summary |
|---|---|---|
| V-01 | Major | LB ability chords come out as a parry; every chord whiffs a parry and locks out deflects |
| V-02 | Major | LB+X after an air-string move or air dash starts a mid-air ground charge, not the plunge; abilities and the skill run in mid-air |
| V-03 | Major | The 5-hit string can't finish on a Dao Soldier: break-out arms on hit 3 |
| V-04 | Major | Zip strike connects ~2 m before the foot arrives |
| V-05 | Major | Launched soldier flips on the slam and flashes upright at the knockdown |
| V-06 | Major | Hand-over can show the old build: stale tuning assets keep a 3-hit chain; README names an old branch |
| V-07 | Minor | Spin, launcher, crescent and tornado kicks point 44-63° away from the target at impact |
| V-08 | Minor | A homing lunge loses its target if the stick is released during startup |
| V-09 | Minor | Neutral-stick counter after a dodge whiffs |
| V-10 | Minor | Air string on a standing enemy lifts the player above its head |
| V-11 | Minor | Axe-kick landing snaps the leg 1.48 m in one frame |
| V-12 | Minor | Feet skate during lunges and dodges |
| V-13 | Minor | LB still held after a parry turns B (dodge) into Flame Wheel and Y (zip) into Fire Whip |
| V-14 | Minor | Some effects are placed or sized differently from the hit |
| V-15 | Minor | (Unity only) pack clip ↔ procedural switches are hard cuts |
| V-16 | Polish | Weapon tips through the floor; run/sprint feet ±5-9 cm; wheel toes in the floor |
| V-17 | Polish | Crossbow wind-up barely differs from idle |
| V-18 | Polish | Player walks into the dying crossbowman's body |
| V-19 | Polish | Jab/cross/palm register 0.6-0.9 m before contact |
| V-20 | Polish | `GreyboxShapes` statics aren't reset |
| V-21 | Polish | Stale numbers and labels in docs and comments |
| V-22 | Polish | The anim tool's "strike extension" metric can't catch mis-aimed strikes |

### V-01 · Major · LB ability chords come out as a parry
- **Reproduce:** hold nothing, press LB and X on the same frame (or LB, then X 3 frames later while a jab is running). Harness: `harness chord`.
- **Saw:** a parry (whiffed), no fa jin / whip / wheel. Even when the ability does come out, the LB press has already started a parry that whiffs. That locks deflecting out, so a correctly timed parry 22-30 frames later fails and you take the hit.
- **Expected:** LB + face = the ability, with no parry. (Spider-Man 2: L1 is the parry *and* the modifier, but the chord never costs you the ability or your next parry.)
- **Cause:**
  - `PlayerInputReader.cs:167` reports LB as `Guard.Pressed` on the frame it goes down, before any face button can make it a chord.
  - `PlayerCombatModel.cs:269` pushes Guard *after* Heavy in the same frame, so it replaces it.
  - `InputBuffer.cs:33`: a buffered defensive press beats a later Heavy (Heavy is in `IsAttack`; AbilityNorth/East aren't, which is why whip/wheel survive the 3-frame mid-string case).
  - The whiff lockout (`GuardSettings.DeflectWhiffLockout` 0.35 s after the 0.15 s window) then applies.
- **Fix:**
  - In the reader, treat an LB press as the chord's modifier. Either hold the Guard press back for a short window (e.g. 3-4 frames) and drop it if a face button arrives, or cancel it retroactively: a new `PlayerInputFrame` flag telling the model "that guard was a modifier" clears the buffered Guard and any whiff lockout.
  - In the core, a chord's Heavy should replace a buffered Guard pressed ≤ N frames earlier.
  - Add a core test for "Guard then Heavy within 3 f → charge, no DeflectWhiffed".

### V-02 · Major · Plunge after the air string gives a mid-air ground charge; abilities and the skill run in mid-air
- **Reproduce:** jump, attack (Sky Jab), hold LB+X. Or: jump, air dash, hold LB+X. Harness: `harness fajin`.
- **Saw:** the player enters `Charging` at 1.73 m up, sinks, and a ground Fa Jin Palm fires on landing (36 dmg), instead of the Falling Axe Kick the spec and F1 overlay promise ("Air combo / plunge: X / hold LB + X"). Whip, Wheel and Fire Blast pressed during an air jab also start in mid-air at 1.7-1.9 m and hang at 30% gravity. The code's own rules say abilities are "grounded" and "Skill waits for the ground".
- **Cause:**
  - `PlayerCombatModel.Actions.cs:220`: `TryHeavy` only plunges when `state == Airborne`, not when `Aloft` (an air attack or air dash is `Attacking`/`Dodging` with `!grounded`). Then `CanStartAttack()` (line 69) accepts it at the air move's ChainCancelAt.
  - `TryAbility` / `TrySkill` (lines 211, 230) have no ground check at all.
  - `Locomotion.cs:235` applies the air-strike gravity scale to *any* `Attacking` state.
- **Fix:** in `TryHeavy` use `Aloft` → `TryPlunge`; in `TryAbility` / `TrySkill` return (keep buffered) while `Aloft`; limit the gravity scale to `attackKind == Air` (and the launcher / zip). Add tests.

### V-03 · Major · The 5-hit string can't be finished on a Dao Soldier
- **Reproduce:** 5 timed attack presses at a fresh soldier (passive or not, 1.8-5.5 m). Harness: `harness string`.
- **Saw:**
  - Hits at f13, f30, f48; hit 3 arms the break-out (`EnemyTuning.BreakOut.HitsToTrigger = 3`, within 1.2 s).
  - Dragon Tail Kick lands on the armoured wind-up.
  - The shove hits at f90, 2 frames into Phoenix Palm's startup: 30 dmg and a stagger. The palm never comes out.
  - Every mashing / timed-string run shows the same.
- **Expected:** the owner's headline "5 combo movements" should play out on the main enemy. The string only completes if you parry the shove (works 3/3), and then you have to start again from the jab.
- **Cause:** the rule was tuned for the old 3-hit chain (report 02, round 3). The string is now 5 hits in ~1.6 s.
- **Fix (Jeremy's design call):** the numbers are data. Options:
  - `HitsToTrigger` 5 (or 6), so one full string lands and a *second* string is the mash;
  - or don't count hits inside the string's own combo windows;
  - or let the shove arm only after the string's last hit.
  - Re-run `duels` for the masher numbers after.

### V-04 · Major · Zip strike connects ~2 m before the foot arrives
- **Reproduce:** zip at anything 5+ m away. Anim scenario frame 1033; harness `zip`.
- **Saw:** at the first active frame the kicking foot is **2.15 m** from the crossbowman's body. Harness hits land at 2.2-2.3 m centre distance. The target reacts while the flying kick is still well short.
- **Cause:** the dash covers the gap over Startup + Active (0.30 + 0.12 s, `ElementMoveSet.cs:280-282`), but the hitbox opens at Startup with Range 2.4. For a long zip the player is still ~30% of the gap short when it opens, so it hits as soon as the target is within 2.4 m + radius.
- **Fix:** make the dash arrive by the first active frame (end the lunge at `Startup`, or set `LungeTime` so the window ends there), and/or cut the zip's Range to ~1.3 m so it only hits on arrival.

### V-05 · Major · The launched soldier flips on the slam and flashes upright at the knockdown
- **Reproduce:** anim scenario, follow the soldier, frames 766-786 (`sheet_pop_soldier.png`).
- **Saw:** spine pitch 151° → 79° at the slam (f767→768), then 69° → −76° → 166° over f772-774, which is a full flip back and forth. At landing, 143° → 101° → **13° (standing upright) → −63°** over f780-783, with the body 41 cm under the floor on f782. Limb jumps up to 1.91 m in one frame.
- **Cause:** `PoseSpec.Lerp` (`PoseSpec.cs:85`) interpolates angle channels linearly. `FighterAnimator.BeginFade` wraps the start pose to ±180° (`FighterAnimator.cs:335, 359`), so a tumble at about −217° becomes +143° and fades to the knockdown's −90° *through 0* (upright). The slam's `Juggled` reaction (`EnemyAnimationFeed.cs:88`) re-fades mid-tumble the same way.
- **Fix:** blend rotation channels (RootYaw, Pelvis pitch/yaw/roll) by shortest angle, or unwrap the start value to within 180° of the *target* before a linear blend. Add an animator test: fade from pitch −217 to −90 never passes |pitch| < 90.

### V-06 · Major · The hand-over can show the owner the old game
- **Saw:**
  - `docs/Prototype/README.md:7` tells David to `git switch claude/wizardly-hamilton-b7ncq5`, not this branch.
  - *Build Fire Combat Sandbox* keeps existing tuning assets. A `MoveSetAsset` saved before `68a66a6` still has the 3-entry `LightChain` (no Rising Snap Kick, no Phoenix Palm) and empty AnimationKey/EffectKey (generic burst only). A `Enemy_DaoSoldier` asset keeps its old numbers.
  - The README only tells you to reset tuning "for the Spider-Man controls update".
- **Fix:**
  - Point step 1 at the current branch.
  - Add "after pulling this update, run *Reset Sandbox Tuning To Defaults*".
  - Better: the builder warns (or offers a reset) when a move set's LightChain length or keys don't match the defaults.

### V-07 · Minor · Some strikes point away from their target at impact
- **Saw:** at the first active frame, the limb is 63° off-target for the spin kick (foot 1.14 m from the body), 46° for the launcher (foot straight up over the head, the soldier 1.2 m in front), 44° for the crescent and 56° for the tornado kick. The hits register anyway.
- **Cause:** the clips' Active key poses in `PoseLibraryDefaults.cs` are authored in the fighter's frame, with no aim toward the target. Aerial ones don't account for the height difference.
- **Fix:** move the Active key so the foot passes through the target direction at ActiveStart, or add a small aim IK / pelvis-yaw offset toward `input.TargetDirection` for kicks.

### V-08 · Minor · A homing lunge loses its target if the stick is released during startup
- **Reproduce:** harness `flick`. Soldier 135° behind, 3-6.5 m; push the stick at it for 2 frames with the attack press, then let go.
- **Saw:** the attack lunges 1.7-4.5 m **away** and ends 3.7-8.5 m from the target. Holding the stick 6+ frames works.
- **Cause:** `Locomotion.cs:319-326`: the lunge target is re-read every frame from the soft lock, which is re-picked from the *current* stick or facing. When it drops out, `LungeDirection` falls back to the half-turned facing with the full stretched distance.
- **Fix:** remember the target's `Combatant` id at `StartAttack` (`PlanLunge`) and keep homing on it for that attack.

### V-09 · Minor · Neutral-stick counter after a dodge whiffs
- **Saw:**
  - A side dodge then attack with the stick centred jabs along the dodge direction: 0 damage in 4/4 soldier attacks.
  - In the `anim` scenario's own punish (frames 2158-2205), the jab and cross fire 127-145° away from the soldier.
  - Locked on it works.
- **Cause:** with no stick, the soft lock looks only within 60° of the facing (`SoftLockSelector`), and the facing is the dodge direction.
- **Fix:** with a neutral stick, fall back to the nearest enemy within soft-lock range at any angle (Spider-Man behaviour), or keep the pre-dodge facing for aim.

### V-10 · Minor · Air string on a standing enemy lifts the player above its head
- **Saw:** jump + air dash + air string on a standing soldier: each strike lifts the player (SelfLift 3.2); by the tornado the player's feet are at 2.38 m, above the 1.8 m soldier's head, and all 3 hits register (VerticalReach 1.8-2.0).
- **Fix:** apply SelfLift only when the strike connects with a launched target, or cap the vertical reach when the target is grounded.

### V-11 · Minor · The axe-kick landing snaps the leg in one frame
- **Saw:** right foot moves 1.48 m between f816 and f817 (`sheet_pop_axe.png`).
- **Cause:** `PoseLibraryDefaults.cs:596`, the `Recovery 0.08, PoseEase.Snap` key.
- **Fix:** use `Out` over ~3 frames.

### V-12 · Minor · Feet skate during lunges and dodges
- **Saw:** a gap-closing jab (up to 2.6 m in 0.22 s) keeps both feet planted in the bow stance, so the feet glide up to 0.19 m/frame (palm 0.21, spin kick 0.28). The dodge slides a grounded foot 0.64 m/frame. Walk has 0.18 m/frame foot slips.
- **Fix:** for a stretched lunge, play a step or leap (lift the rear foot, or use a short flying-lunge pose above ~1.5 m).

### V-13 · Minor · LB still held after a parry changes B and Y
- **Saw:** if LB is still down after a parry, B gives Flame Wheel instead of a dodge (no i-frames, 0.85 s committed) and Y gives Fire Whip instead of the zip (`PlayerInputReader.cs:236`). A panic parry → dodge sequence can go wrong.
- **Fix:** only start a chord if LB went down within the last ~0.4 s, or if no parry happened on this hold.

### V-14 · Minor · Some effects are placed or sized differently from the hit
- **Saw:**
  - The Tornado Slam draws its 2.8 m ring on the floor below the airborne kick at kick time (`PlayerFeedback.cs:230-231`), not where and when the soldier lands (frame 776).
  - The launcher draws a second pillar 1.56 m in front of the player (`PlayerFeedback.cs:214-219`), next to the correct one under the launched soldier.
  - The fa jin cone is drawn 15% longer than it hits (3.9 vs 3.4 m, line 212).
- **Fix:** put the slam ring on the enemy's `KnockedDown` event; drop the player-side pillar; drop the 1.15 factor or add it to the hit range.

### V-15 · Minor (Unity only) · Pack clip ↔ procedural switches are hard cuts
- **Saw:** by design, when the action changes from a move with a pack clip to one without, `MecanimPoseSource` just stops writing (`MecanimPoseSource.cs:185-191`) and the procedural pose appears that frame. Once packs are imported this will pop on every mixed string (e.g. a mocap jab into a procedural Dragon Tail Kick).
- **Fix:** before a hand-off, sample the last Mecanim pose into the procedural animator's `lastShown` so its normal fade starts from it.

### Polish (V-16 to V-22)
- **V-16:** weapon tips go through the floor during stagger / get-up / death (to −0.95 m). Run and sprint feet float 9 cm and sink 5 cm; Flame Wheel toes sit 7 cm under the floor.
- **V-17:** the crossbow shot's wind-up pose is almost the same as idle. Readability rests entirely on the glow.
- **V-18:** the string walks into the dying crossbowman's body (26 cm torso overlap), because the corpse stops blocking.
- **V-19:** jab, cross and palm register 0.6-0.9 m before contact (fire-extended by design; the burst covers the gap). Fine, but it adds to V-04's "hit from afar" feel.
- **V-20:** `GreyboxShapes.ringBandMesh` and `reportedMissing` aren't reset in `SubsystemRegistration` (CLAUDE.md rule). They're harmless today.
- **V-21:** stale numbers and labels:
  - The spec says soldier HP 110; the code is 180.
  - The `EnemyTuning.cs:21` comment says a chain is 39 poise; it's 43.
  - The `AbilitiesScenario.cs:174` label says "3-hit chain".
- **V-22:** the `anim` tool's "strike extension" metric can't catch V-07 (it reads 1.000 for mis-aimed kicks). Add an aim-angle / tip-to-target column.

## 6. Unity-only review (what the offline tools can't prove)

| Area | Finding |
|---|---|
| Execution order | Input −100 → director −90 → lock-on −50 → player 0 → enemies 10 → projectiles 20 → `MecanimPoseSource` LateUpdate 45 → `BodyAnimatorDriver` LateUpdate 50 → `FireVfxRunner` LateUpdate 60 → camera 100. That's consistent: the clip pose lands before the procedural pose would, and effects and the camera follow the final bones. `HumanoidBody` and `BodyAnimatorDriver` share 50 but don't depend on each other |
| Edit mode vs play mode | The sandbox builder spawns fighters in edit mode. `BodyMeshes` creates `new Mesh` and `HumanoidBody` creates materials without the runtime suffix, so both get saved **inside the scene file**. That's valid, but the scene will be large. The runtime-suffixed materials are destroyed on disable. The builder still produces Arena / Player / Systems / Dummies / Enemies (`FireSandboxBuilder.cs:150-196`) |
| Statics, domain reload off | Everything that matters resets (`Combatant`, `MeleeHitQuery`, `FireVfx`, `TimeScaleController`, `MecanimPoseSource` warnings, the pose library default). The only exception is V-20 |
| AvatarBuilder | The runtime Avatar is built from rest-pose bones with unique names and a full skeleton. On any failure it logs one warning and stays procedural; the Avatar is destroyed with the component; root motion is off. Needs a real run to confirm Unity accepts the proportions |
| Old tuning assets | See V-06: new fields get defaults, but existing arrays and values stay old |
| F1 overlay | Matches the actual bindings row for row. It promises "hold LB + X in the air = plunge", which V-02 breaks after an air strike |
| VFX | No ParticleSystems. Pooled primitives, Trail/LineRenderers and lights are all capped; materials are owned and destroyed; no per-frame allocations seen in the update loops. The shader is found with `Shader.Find`: fine in the Editor, but a player build needs URP Unlit/Lit referenced by a material or in Always Included Shaders |

## 7. Files the verifier looked at (scratchpad, not in Git)

`/tmp/claude-0/-home-user-vaatus-revenge/08c08173-6fe0-59c1-a069-7a487d39a9a7/scratchpad/verify/`:
- `anim.json`, `abilities.md`, `fuzz.md`, `compile.log`, `tests.log`
- Contact sheets:
  - `sheet_chain_q.png` and `sheet_chain_side.png` (5-hit string, 3/4 and side)
  - `sheet_aerial.png`, `side_launch_axe.png`, `q_launch_axe.png` (launcher, air string, axe kick)
  - `sheet_abilities.png` (whip, wheel, blast, fa jin, zip, air dash)
  - `sheet_soldier.png` (thrust, overhead, double slash, quick slash, shove, stagger)
  - `sheet_gallery.png` (sprint kick, crossbow shot and burst, practice swing, block, heal, death)
  - `sheet_punish_whiff.png` (V-09 and V-04)
  - `sheet_pop_soldier.png` (V-05), `sheet_pop_axe.png` (V-11), `side_jump.png`
- No videos were rendered (sheets only).
- The harness (`harness/`, a copy of the CombatSim csproj plus `Play*.cs` scripts): `dotnet bin/Release/net8.0/Harness.dll <string|nobo|sparry|juggle|launch2|inf|zip|airdash|ranges|wheel|fajin|hover|parry|dodge|dodge2|flick|chord|parryafter>`
- Python metrics: `py/metrics2.py` (knees), `py/reach.py` (strike aim), `py/fluid.py` (pops and speeds), `py/feet.py` (contact and sliding), `py/pen.py` (interpenetration), `py/render_side.py` (renderer with a `CAM_OFFSET` side camera)
