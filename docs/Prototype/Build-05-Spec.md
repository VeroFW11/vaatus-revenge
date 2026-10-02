# Build 05 spec: the four elements, combat rhythm, Spider-Man 2 dodge, danger sense, tutorial

Status: **architect spec, single source of truth for the Build 05 builders** (1 Oct 2026). Branch base: `claude/dazzling-clarke-njm3m5`.
Merges three scout reports (combat feel, elements + lore, presentation + partition) and the VFX art result. Where scouts disagreed, this
document decides; the decision log is in §8. Builders do not re-open decisions: they raise a question to the architect instead.

Contents: 1 Player summary and controls · 2 Core rules and data · 3 Move tables · 4 Contracts · 5 Work packages · 6 Acceptance
criteria · 7 Canon check · 8 Decision log and open questions for David and Jeremy.

---

## 1. What it feels like (for David)

**Dodging.** Tap B and you dodge, but you never turn your back on the fight. Push the stick *toward* an enemy and you slip in close,
ready to hit. Push *away* and you hop back out of reach. Push *sideways* and you side-step round them. Leave the stick alone when
something is about to hit you and the game picks a safe sideways dodge for you. In Fluid, dodging costs no stamina, and you can chain
a few dodges before a short breather (Earth two, Fire and Water three, Air four; two in every element on Punishing). **Dodging does not break your combo.** If you were on hit 3 of your string and you dodge, your next
X is hit 4. Press X late in a dodge and you get a **dodge strike**: a counter that dashes back in and counts as the next hit.

**Rhythm combos.** Every element has a five-hit string on X. You can mash and it still works, but slowly and at a stamina cost. Press X
**so it lands with each hit**: a gold ring closes on a circle and touches it on the beat, so press as it touches (the flash and chime
confirm a hit on the beat; reacting to them is too late). On the beat the next hit comes out faster and harder. Keep the beat for the
whole string and the finisher hits harder still and (Fluid) knocks the enemy up. Want a different ending? Hit **X, X, then wait until your
hands are back in guard and the circle glows blue, then X X**: that's the **pause finisher**, a different two-hit ending per element
(Fire's is a sweeping kick into a rising kick that launches). *(Build 05 verify: the pause chain is two moves, so it is X X (wait) X X.)*

**Element switching.** Hold RB, then press a face button to change element (modifier first, like Spider-Man 2's L1 gadgets; §8.6). **The button's colour is the element: B (red) Fire, X (blue)
Water, A (green) Earth, Y (yellow) Air** (§8.1 item 3). Do it **in the middle of a string** and the string carries on in the new element
with its next hit: X, X, hold RB then X, X, X is two Fire hits then three Water hits ending on Water's finisher. The switching hit lands a little
harder (on the beat or not). Use more elements in one combo and every hit gets stronger: **two elements hits harder, three knocks them up on the finisher,
four also tops up Fire's Momentum (FinisherMeterRefill) (and breaks the guard of a foe too heavy to launch).** The HUD calls this **MIX**.

Each element plays like its martial art:
- **Fire (Northern Shaolin):** fast, steady, relentless; on-beat hits build Momentum.
- **Water (Tai Chi):** slower, forgiving timing, long reach, pulls enemies in, heals you on finishers, widest parry.
- **Earth (Hung Gar):** slow, heavy and rooted; hits through enemy attacks (armour); holds a real block; strict timing.
- **Air (Baguazhang):** fastest, many little hits, circles round enemies, longest dodge, biggest knockback and zip range.

**Danger sense.** When an enemy is about to hit you, a mark flashes above your head with an arrow pointing at the attacker.
**Gold** means you can parry it. **Red** means you must dodge it. In Fluid it turns **white** at the exact moment to press.

**Tutorial.** In the sandbox, press **View** (the small left button) to start an 11-step tutorial against a sparring partner who can't die.
Each step shows the buttons as pictures, counts your progress, and chimes when you pass. Tap View to skip a step, hold it to quit.

### Xbox controller (Spider-Man 2 layout, unchanged except where marked NEW)

| Button | Action |
|---|---|
| Left stick / Right stick | Move / camera. Flick the right stick to change target while locked on |
| **X** | Attack. Tap in rhythm for the 5-hit string. **X X (wait) X X** = pause finisher (NEW). In the air: air string |
| Hold **X** | Launcher (throws the enemy up, you follow); keep holding any string press before the finisher and that hit becomes the launcher |
| **Y** | Zip strike to a far enemy (keeps your combo going) |
| **B** | Tap: dodge (stick decides slip-in, evade-out or side-step; NEW). Hold: sprint. **X late in a dodge = dodge strike** (NEW) |
| **A** | Jump |
| Tap **LB** | Parry. Earth only: hold LB to block |
| Tap **RB** | Ranged skill of the current element |
| Hold **LB** + X / Y / B | Charged fa jin heavy (in the air: plunge) / mid-range ability / close all-round ability |
| Hold **RB** + B / X / A / Y | Switch to Fire / Water / Earth / Air (colour-matched). Mid-string it is a **switch strike** (NEW) |
| D-pad down | Heal |
| R3 / L3 | Lock-on (optional) / swap camera shoulder |
| **View** | Sandbox: start / skip (tap) / quit (hold 1 s) the tutorial (NEW) |

Keyboard: F7 starts or quits the tutorial, F8 skips a step, 1-4 switch element (existing).

---

## 2. Core rules and data (pure C#, `Scripts/Core`)

Every number below lives in a tuning class (`PlayerTuning`, `ElementMoveSet` and their children). Nothing is a literal in rule code.
Fluid is the default; Punishing is the Elden Ring preset. No new statics in Core; any Unity-side static is reset in a
`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` method.

### 2.1 Current code this build changes (verified on the branch)

| # | Where | Today | Build 05 |
|---|---|---|---|
| C1 | `PlayerCombatModel.Actions.cs` `ExitAction` (~706) | every exit wipes `chainIndex` and chain grace | call `RememberString(reason)` first; `chainGrace*` replaced by string memory (§2.2) |
| C2 | `Actions.cs` `TryLight` (~123) | next index from grace or window | `ResolveNextStringMove(out MoveData, out int index, out ComboBranch)` (§2.2 order) |
| C3 | `PlayerCombatModel.Defense.cs` (~108) | dodge without lock faces the dash (turns your back) | focus-target facing (§2.3) |
| C4 | `Defense.cs` (~72, ~110) | dodge needs and spends stamina | `DodgeProfile.RequiresStamina`, Fluid cost 0 |
| C5 | `Actions.cs` `CanStartAttack` | one `AttackCancelAt` | `EvadeAttackCancelAt` / `SlipInAttackCancelAt` |
| C6 | `PlayerCombatModel.Locomotion.cs` (~225) | turns to target during dodge only when locked | turn to focus target at `FocusTurnRate` |
| C7 | `Defense.cs` / `Locomotion.cs` | dodge zeroes velocity, so you re-accelerate from 0 | exit carries `RunSpeed × ExitSpeedCarry` |
| C8 | `Actions.cs` `action.Advance(dt)` | moves play at 1× | `dt × attackPlaybackRate` while Attacking (§2.4) |
| C9 | `Actions.cs` `UpdateAttack` buffer lock | early judged presses can expire (Phoenix Palm) | lock at judgement; `InputBuffer.Lock(double runnableAt)` ages from `max(PressTime, runnableAt)` |
| C10 | `Scripts/Player/PlayerController.cs` `UpdateElementSelect` (~686) | Unity-only, "not learned" message | element select handled in the core (§2.6) |
| C11 | `MomentumMeter.Tick` | zeroes when settings disabled | every element's meter ticks with its own settings; never zeroed on switch |
| C12 | `Dodge`, `Charge`, `Plunge`, `Aerial`, `Guard` properties read `moveSet` live | mid-action switch changes a running action | `actionSet` snapshot at each action start |
| C13 | Fire Fluid chain `DodgeCancelAt` 0.22-0.38 | can't dodge the start of a hit | Fluid chain hits 0-3 `DodgeCancelAt = 0`, finisher 0.30; Punishing unchanged |

Existing tests this invalidates (A updates them, never deletes them): `MovementTests` dodge facing (~160-175),
`CombatPlaytestFixTests` (~320, dodge at `LightChain[0].DodgeCancelAt`), `CombatDefenseTests.ChainedDodgesNeverGiveContinuousInvulnerability`
(kept, extended with the chain cap).

### 2.2 String memory (the combo survives dodges, zips, abilities and switches)

New private state: `stringNext` (-1 = none), `stringBranch` (`Main`/`Pause`), `stringKind` (`Light`/`Air`), `stringMemoryUntil`.
`RememberString` runs in `ExitAction` while a string move (`Light`, `Air`, `DodgeStrike`, pause chain) is running:
- interrupted **before** `ActiveStart` → `stringNext = chainIndex` (retry the same hit);
- at/after `ActiveStart` or finished → `stringNext = chainIndex + 1` (ground string loops modulo length; air string doesn't).

| Event | String memory | Combo counter (§2.5) |
|---|---|---|
| Dodge / air dash (any kind) | held while dodging + `StringMemoryAfterAction` | kept |
| Zip strike (Y) | held; the zip is a connector and does **not** use a slot | kept, counts its own hit |
| Ability (LB+face), skill (RB tap), heavy | held + `StringMemoryAfterAction` | kept |
| Element switch | held | kept |
| Jump | ground memory held; air string starts at 0 | kept |
| Launcher (hold X) | ground string ends; air string starts at 0 | kept |
| Air finisher slam, landing | ground memory reset to 0 | kept |
| Clean hit taken, Staggered, Parried, Died, Respawned, `ApplyTuning`/`ApplyLoadout` | cleared | ended |
| Move finished, no press | pause band rules (§2.4.3), else old grace | kept until timeout |

A perfect dodge extends memory to at least the end of the counter window.

**Light press resolution order** (`ResolveNextStringMove`):
1. Pressed during a dodge (fires at the kind's attack-cancel point) or within `DodgeStrikeGrace` after it, with a lock / soft / focus target
   within `SoftLockRange` → **dodge strike** (§2.3.4).
2. Inside the running move's combo window → `chainIndex + 1`.
3. In the pause band of an eligible move → pause chain (§2.4.3).
4. String memory live → `stringNext` in the **active** element's chain (clamped, §2.6).
5. Otherwise → index 0.

| `PlayerTuning` field | Fluid | Punishing |
|---|---|---|
| `StringMemoryAfterAction` | 0.45 s | 0.35 s |
| `DodgeStrikeGrace` | 0.20 s | 0.10 s |

### 2.3 Dodge (Spider-Man 2 style)

#### 2.3.1 Focus target and facing
Focus target, first match wins: lock target → attacker of the most imminent danger-sense threat whose impact is within
`DangerSense.AutoEvadeLookahead` → soft-lock candidate → nearest enemy within `DodgeProfile.FocusRadius`
(`PlayerWorldState.HasNearestEnemy` / `NearestEnemyPosition`).
- Focus target at dodge start → snap `facingYaw` to it, keep turning toward it at `FocusTurnRate` while dodging.
- No focus target → face the dash (`DodgeKind.Traverse`). Backstep keeps the current facing.
- This is a bug fix and applies to **both** presets.

#### 2.3.2 Dodge kinds
`angle = Angle(cameraRelativeStick, directionToFocus)`:

| Kind | When | Motion |
|---|---|---|
| `SlipIn` | angle ≤ `SlipInAngle` | toward the target, stops `SlipInStopGap` from its body (reuse `GapTo`/`LimitApproach`), at most `SlipInMaxDistance`. Perfect slip-in earns `PerfectTowardBonus` |
| `EvadeOut` | angle ≥ `EvadeOutAngle` | `EvadeOutDistance` away, still facing the target |
| `SideSlip` | between | `SideSlipDistance` sideways, facing kept (circle-strafe) |
| `AutoEvade` | neutral stick, `AutoEvadeOnNeutral`, a threat within `AutoEvadeLookahead` | `SideSlipDistance` perpendicular to the attacker's `StrikeForward`, on the side away from the centroid of other enemies within `FocusRadius`; tie → camera-right (deterministic) |
| `Backstep` | neutral stick otherwise | `BackstepDistance` (existing) |
| `Traverse` | no focus target, stick pushed | `EvadeOutDistance` along the stick, facing the dash |
| `AirDash` | airborne | `AerialSettings.AirDashDistance`; facing follows the focus rule |

#### 2.3.3 Data (`DodgeProfile`; Fire values; other elements in §3.5)

| Field | Fluid | Punishing | Note |
|---|---|---|---|
| `Duration` | 0.24 (was 0.30) | 0.36 | |
| `DashEaseOut` | 0.70 | 0.60 | |
| `EvadeOutDistance` (replaces `Distance`; old assets are reset by the `DataVersion` check, and a read-only `Distance` alias keeps old code compiling) | 4.0 | 4.2 | |
| `SideSlipDistance` | 3.0 | 3.2 | |
| `SlipInMaxDistance` / `SlipInStopGap` | 3.4 / 0.6 | 3.0 / 0.6 | Fire favours slipping in |
| `SlipInAngle` / `EvadeOutAngle` | 50° / 120° | same | |
| `BackstepDistance` | 2.2 | 2.2 | |
| `FocusRadius` / `FocusTurnRate` | 7.0 m / 900°/s | 7.0 m / 600°/s | |
| `AutoEvadeOnNeutral` | true | false | Punishing neutral = backstep |
| `IFrameStart` / `IFrameEnd` | 0.00 / 0.18 | 0.04 / 0.30 | |
| `ChainIFrameGap` | 0.06 | 0.05 | existing formula in `StartDodge` |
| `EvadeAttackCancelAt` (renames `AttackCancelAt`) | 0.08 | 0.48 | attack/jump/guard out of every kind except slip-in |
| `SlipInAttackCancelAt` | 0.05 | 0.48 | |
| `NextDodgeAt` / `EndRecovery` | 0.18 / 0 | 0.48 / 0.12 | |
| `ChainMax` / `ChainLink` / `ChainCooldown` | 3 / 0.12 / 0.30 | 2 / 0.12 / 0.45 | a dodge starting within `ChainLink` of the previous ending extends the chain; after `ChainMax`, `DodgeChainLimited` and no dodge for `ChainCooldown` |
| `StaminaCost` / `RequiresStamina` | 0 / false | 16 / true | Fluid: empty bar still dodges, can't attack |
| `ExitSpeedCarry` | 1.0 | 0.5 | stick held at exit → `moveVelocity = stick × RunSpeed × carry` |
| Perfect dodge (window 0.12, slow-mo 0.35×, +25, counter 0.8 s ×1.5) | unchanged | disabled | |

Invulnerability cap: under maximum spam the invulnerable share over 3 s must stay **≤ 60 %** for every element (Fluid Fire ≈ 41 %).
`DodgeStarted` gains `DodgeKind` and `LocalDirection` (dash in facing space) so the animator can pick a directional clip.

#### 2.3.4 Dodge strike
- Move: `ElementMoveSet.DodgeStrike` (`MoveData`), kind `PlayerAttackKind.DodgeStrike`, branch `ComboBranch.DodgeStrike`.
- Takes the next slot: runs at `stringNext`; afterwards the string continues at `stringNext + 1`. If `stringNext` is the finisher index,
  the finisher plays instead (a dodge never skips your finisher).
- Lunge uses free-flow gap close (`PlanLunge` treats `DodgeStrike` like `Light`), so evade-out then X closes back in.
- Inside the perfect-dodge counter window it is the counter (`ConsumeCounterWindow`, ×`CounterDamageMultiplier`) and graded `Auto`.
  Outside it is not beat-judged (the press after it is). No target → normal string continue.
- Poise rule: for every element, the light string with the dodge strike substituted into **any** slot, plus hit 1 again, stays below 52.

#### 2.3.5 Air dash
Fluid `AirDashesPerJump` 1 → 2 (Punishing stays 0). An air dash keeps the **air** string memory; air dashes count toward `ChainMax`
and use the same i-frame gap. No separate air dodge strike: the next air chain hit is the counter.

### 2.4 Rhythm combos

#### 2.4.1 Timing model
For each string move (main chain, pause chain, air chain, dodge strike): `BeatTime = attackStartClock + ActiveStart / rate`.
Beat window = `[BeatTime − BeatEarly, BeatTime + BeatLate]` in **game clock** (hitstop freezes it, slow motion stretches it: both
intended). `BeatInputOffset` (calibration, default 0) is added to every press time. Per-move tracking reset in `StartAttack`:
`followUpPressClock`, `followUpCount`, `earlyPressSeen`.

| Follow-up Light (or switch strike) press | Grade | Next move |
|---|---|---|
| in `(attackStartClock, BeatTime − BeatEarly)` | `Early` (HUD: mash) | chains at the cancel point; `rate = OffBeatPlaybackRate`; `+MashStaminaSurcharge`; no bonus |
| in the window, no second press before the next move starts | `OnBeat` | `rate = OnBeatPlaybackRate`; damage × `OnBeatDamageMultiplier`; element perk (§2.4.4) |
| in the window, then another press before the next move starts | `Mashed` (re-emitted downgrade) | as Early |
| after the window, up to `ComboWindowEnd` | `Late` | chains; rate 1.0; no bonus; streak resets |
| none by `ComboWindowEnd`, press in the pause band | `Pause` | pause chain, rate 1.0 |
| counter-window attack; switch strike in its widened window | `Auto` / `OnBeat` | as OnBeat |

The triggering press of a move is always before `attackStartClock`, so it can never grade its own move Early.
Playback rate: `attackPlaybackRate` set in `StartAttack` from the triggering grade (1.0 default; never applied to Charging, Plunging,
Dodging); `AdvanceAction` advances by `dt × rate` while Attacking, so every `MoveData` time (cancels, windows, lunge) scales and no frame
data changes. Clamped to `[MinPlaybackRate, MaxPlaybackRate]`. **Poise is never scaled by rhythm.**

#### 2.4.2 Data (`PlayerTuning.Rhythm`, new `[Serializable] RhythmTuning`)

| Field | Fluid | Punishing |
|---|---|---|
| `Enabled` | true | true |
| `BeatEarly` / `BeatLate` | 0.06 / 0.10 | 0.04 / 0.07 |
| `OnBeatPlaybackRate` / `OffBeatPlaybackRate` | 1.15 / 0.85 | 1.12 / 0.80 |
| `MinPlaybackRate` / `MaxPlaybackRate` | 0.6 / 1.4 | 0.6 / 1.4 |
| `OnBeatDamageMultiplier` | 1.10 | 1.10 |
| `MashStaminaSurcharge` | 4 | 3 |
| `PauseGrace` | 0.35 | 0.25 |
| `PerfectStringDamageMultiplier` | 1.20 | 1.15 |
| `PerfectStringLaunchSpeed` | 9 | 0 |
| `BeatInputOffset` | 0 | 0 |

`Enabled = false` restores today's chain timing exactly (sandbox A/B and a regression test).

#### 2.4.3 Pause chain (X X, wait, X X)
- Eligible only after main-chain index `ElementRhythm.PauseAfterIndex` (default **1**, i.e. after the 2nd hit), with no follow-up and no
  early press. One rule to teach: "two hits, wait, two hits".
- Pause band (move-local, scaled by rate): `(ComboWindowEnd, TotalDuration + PauseGrace]`. A press in it → `PauseChain[0]`, branch `Pause`,
  graded `Pause`. Presses inside the pause chain are beat-judged normally and walk `PauseChain[1..]`; after its last move the string
  loops to main index 0. After the band: restart at 0. A dodge / zip / ability in between turns the press into a string-memory continue,
  never a pause. After a non-eligible hit, a press after `ComboWindowEnd` behaves as today (grace if any, else restart).
- Perfect string: if every follow-up of this string was OnBeat or Auto, the finisher (main or pause) gets
  `PerfectStringDamageMultiplier` and, on a launchable foe, `PerfectStringLaunchSpeed`; `PerfectString` is raised at its start.

**Beat data validation** (test, both presets, every element, every chain / pause / air / dodge-strike move), with
`rOn` = the element's effective on-beat rate and `late` = `BeatLate + element BeatLateDelta + SwitchStrikeBeatLateBonus`:
`ActiveStart / rOn − BeatEarly ≥ 0` · `ActiveStart + late ≤ ComboWindowEnd` (moves that chain) · `ComboWindowEnd < TotalDuration + PauseGrace`
· `ChainCancelAt ≥ ActiveEnd` · `ComboWindowStart ≤ ChainCancelAt`.

#### 2.4.4 Element rhythm (`ElementMoveSet.Rhythm`, new `[Serializable] ElementRhythm`, deltas on top of the preset)

| Field | Fire | Water | Earth | Air |
|---|---|---|---|---|
| `BeatEarlyDelta` / `BeatLateDelta` | 0 / 0 | 0 / +0.04 | −0.02 / −0.03 | 0 / 0 |
| `OnBeatPlaybackRate` (0 = preset) | 0 | 1.10 | 1.10 | 1.20 |
| `OnBeatDamageBonus` (added to multiplier) | 0 | 0 | +0.10 | 0 |
| `OnBeatMomentumBonus` | 3 | 0 | 0 | 0 |
| `OnBeatStaminaRefund` | 0 | 3 | 0 | 0 |
| `OnBeatHyperArmor` (next hit armoured 0 → `ActiveEnd`) | false | false | true | false |
| `DodgeKeepsBeat` (a dodge between hits keeps the streak; dodge strike always `Auto`) | false | false | false | true |
| `PauseAfterIndex` | 1 | 1 | 1 | 1 |

Martial meaning: Fire's steady relentless beat feeds Momentum; Tai Chi is legato and forgiving; Hung Gar is strict and rooted; in
Bagua the step *is* the beat.

### 2.5 Combo counter (hit counter) and MIX

**ComboCount** (SM2's meter): +1 per clean hit per `AttackId` (multi-hit moves count every sub-hit; projectiles count when they land),
hooked in `OnAttackLanded`. Refreshed without +1 by a perfect dodge or a deflect. Untouched by dodges, switches, jumps, whiffs, blocks.
Ends (`ComboEnded{Count, EndReason}`) on: `TookHit` (any `Damaged`, including through hyper armour, and `GuardBroken`), `Timeout`,
`Staggered`, `Died`, `Respawned`, `PresetChanged`. Ending clears MIX and string memory (a `Timeout` while string memory is still live
only ends the counter).

| `PlayerTuning.Combo` | Fluid | Punishing |
|---|---|---|
| `ComboTimeout` | 3.0 s | 2.0 s |

**MIX** (multi-element combo; named MIX, not "Flow", to avoid clashing with a future Water meter): bitmask of distinct elements that
**landed a clean hit** in the current combo; `MixLevel` = popcount 1-4. Switching alone earns nothing; Fire/Water/Fire/Water caps at 2.

| `PlayerTuning.Mix` | Fluid | Punishing |
|---|---|---|
| `DamageByLevel[1..4]` (all player damage while the combo lives) | 1.00 / 1.10 / 1.20 / 1.30 | 1.00 / 1.08 / 1.15 / 1.22 |
| `FinisherDamage` (level ≥ 2, main or pause finisher) | 1.20 | 1.15 |
| `FinisherLaunchSpeed` (level ≥ 3, launchable foes) | 10 | 8 |
| `FinisherPoiseBreak` (level 4; respects stagger immunity and break-out armour) | true | true |
| `FinisherMeterRefill` (level 4: each element's identity meter, fraction of max) | 0.5 | 0.3 |

Effects don't stack past the highest tier; the perfect-string launch and MIX launch use the larger speed. Damage multipliers multiply
(Momentum × on-beat × switch strike × MIX × counter). **Poise is never multiplied by MIX.**

### 2.6 Element switching

**Loadout.** New `ElementLoadout` holds four `ElementMoveSet`s and learned flags (all four learned in this build; sandbox can toggle).
The old `(PlayerTuning, ElementMoveSet)` constructor wraps a one-set loadout.

**Rule in one line: "hold RB, then an element's button = your next hit in that element (on the beat: faster too)".**
- **A. String live** (Attacking a string move; or dodging / free with string memory or pause band live; or airborne in an air string):
  the chord is `PlayerCommand.SwitchStrike` (element in `pendingSwitchElement`), buffered and beat-judged **exactly like a Light press**
  with `SwitchStrikeBeatLateBonus` extra late window. When it runs (normal cancel point) the element switches at that instant and the
  next string move comes from the new element: index carries over (`chainIndex + 1` or `stringNext`); if that index is past the new
  chain's length and the old move wasn't the finisher → the new finisher; after a finisher → 0; in the pause chain → new `PauseChain`
  at the same index (clamped); in the air → new `AirChain`, with `airAttacksUsed` shared across elements (switching can't extend a
  juggle; cap = the active element's `AirAttacksPerJump`). The switch strike gets `SwitchStrikeDamageMultiplier` and
  `SwitchStrikePoiseMultiplier` on that one hit.
- **B. String not live:** plain switch. Instant when free, airborne, attacking a non-string move, dodging, or guarding with no deflect
  armed. **Busy** while Charging, Plunging, Healing, Staggered, or a deflect window is open: waits in a one-slot `pendingSwitch` for
  `SwitchBufferWindow` (a stagger clears it).
- **C. On cooldown:** a switch strike resolves as a normal Light press in the current element (the string never drops) plus
  `ElementSwitchDenied{Cooldown}`; a plain switch is just denied.
- **D. Other denials:** `NotLearned` (HUD message); `SameElement` (event, no HUD message).
- **Snapshot (C12):** `actionSet` is taken at `StartAttack/StartCharge/StartPlunge/StartDodge/EnterGuard/TryHeal`; a running action always
  finishes with the element it started with.
- **Guard:** switching into a `ParryOnly` element while holding a `BlockAndParry` block drops the guard (`GuardEnded`); switching into
  `BlockAndParry` with LB held raises it on the next free frame.
- **Meters (C11):** each element's identity meter lives with its loadout slot and ticks every frame with its own settings. A meter's
  damage multiplier applies only to hits whose `Element` matches; `BuildDamage(evt)` uses the event's element (a Fire Blast in flight
  after a switch keeps Fire's Momentum). Only Fire has a meter in this build (Water/Earth/Air: `Momentum.Enabled = false`).
- Cost: 0 stamina in both presets; the cooldown is the limiter.

| `PlayerTuning.ElementSwitch` | Fluid | Punishing |
|---|---|---|
| `Cooldown` | 0.20 s (was 0.30, §8.2) | 0.45 s (was 0.60, §8.2) |
| `SwitchBufferWindow` | 0.25 s | 0.20 s |
| `SwitchStrikeBeatLateBonus` | 0.06 | 0.04 |
| `SwitchStrikeDamageMultiplier` | 1.20 | 1.15 |
| `SwitchStrikePoiseMultiplier` | 1.5 | 1.3 |

Poise note: the "one string + a jab never staggers a fresh soldier" rule (`EnemyAiPlaytestFixTests`, soldier `MaxPoise` 52) applies to
**single-element** strings. A mixed string with a switch strike may stagger on the 6th hit; that is the reward for mixing.

### 2.7 Danger sense

The enemy brain stays ignorant of the player's tuning. A core relay schedules strikes; the player model raises warnings at its own
preset's lead times.
- `DangerSenseRelay.OnEnemyEvent(...)` (core, stateless) is called by Unity `EnemyStrikes.cs` and by CombatSim `SimEnemy`:
  `TelegraphStarted` → for each hit `h < HitCount`, impact = now + `Move.Startup + h × HitInterval` (+ distance / projectile speed for
  ranged); `ProjectileLaunched` → refresh that hit from the real origin and speed; `AttackEnded` before all impacts, `Died`, `Reset` →
  cancel. Must-dodge = `!Move.Parryable || Move.Unblockable`. `EnemyAttackData.HideDangerSense` (default false, bosses only, none in this
  build) and `DangerLeadScale` (default 1).
- Model tick (after `UpdateTimers`), per pending strike (fixed array of `MaxTracked`, no allocation, upsert by attacker + hit index):
  `clock ≥ Impact − WarningLead` → `DangerWarning` once; `clock ≥ Impact − NowLead` (if > 0) → `DangerNow` once;
  `clock > Impact + ClearAfterImpact` or cancelled → drop, `DangerCleared` if warned. A strike registered with less time left than a
  lead fires that event immediately.
- Warnings track the **strike**, not the wind-up: the Dao Soldier's Delayed Thrust warns late, so reacting to the sense beats it while
  panicking on the glow is still punished.
- Danger events never touch the string; dodging on Now keeps string memory and the counter window gives an `Auto` follow-up.

| `PlayerTuning.DangerSense` | Fluid | Punishing |
|---|---|---|
| `Enabled` | true | true |
| `DisplayName` | "Danger Sense" | same |
| `WarningLead` | 0.60 s | 0.45 s |
| `NowLead` | 0.30 s (reaction 0.25 lands in the perfect / deflect window) | 0 (off) |
| `ClearAfterImpact` | 0.10 s | 0.10 s |
| `AutoEvadeLookahead` | 0.45 s | 0.45 s |
| `MaxTracked` | 8 | 8 |

### 2.8 New per-move mechanics (A implements; B fills data)

| `MoveData` field | Default | Rule |
|---|---|---|
| `HitCount`, `HitInterval` | 1, 0 | sub-hits spread across Active: sub-hit `k` goes live at `ActiveStart + k × HitInterval`. Each sub-hit gets its own `AttackId` (existing dedupe unchanged) and shares `MoveInstanceId` (= the first sub-hit's AttackId). Damage, Poise, GuardStaminaDamage are per sub-hit. `Active ≥ (HitCount−1) × HitInterval` (validated) |
| `PullDistance` | 0 | a clean hit pulls the target toward the attacker by up to this, stopping 1.5 m short (`DamageInfo.PullDistance`, applied by `EnemyBrain` like knockback) |
| `OrbitDegrees` | 0 | the lunge curves round the target, ending this many degrees round it; side from the stick (deterministic: camera-right if neutral) |
| `HealOnHit`, `HealPerMoveMax` | 0, 0 | player heals per clean hit, capped per move instance (0 cap = `HealOnHit`) |

`EnemyBreakOutRule` counts hits **per `MoveInstanceId`**, so Air's multi-hit moves don't arm the break-out in three presses.

New `ElementMoveSet` fields: `PauseChain` (`MoveData[]`), `DodgeStrike` (`MoveData`), `Rhythm` (`ElementRhythm`), `AnimationStyle`
(string: `""` Fire, `"water"`, `"earth"`, `"air"`), `DataVersion` (initialiser **0**; factories set `CurrentDataVersion = 5`).
`PlayerTuning.DataVersion` likewise. A stale asset reads 0 because Unity keeps initialisers for fields missing from old YAML.

**Deferred to Build 06** (do not implement; B's data must not need them): self-guard windows during moves, stream tracking,
launch-on-last-hit-only, status effects (chilled, soaked, off-balance), multi-projectile fans and pierce, armour while charging,
move-speed while charging, guard counter-moves and heal-on-deflect, the Air `EvadeParry` defence style, identity meters for Water /
Earth / Air, element dodge twists (perfect heal, armour after dodge, curved dash).

---

## 3. Move tables

Key: **S/A/R** startup / active / recovery (s). **CW** combo window. **Ch / Dg** `ChainCancelAt` / `DodgeCancelAt` (Fluid; Punishing
`Dg` = `ActiveEnd + Recovery × 0.6` via `ApplyPunishing`). **Po** poise. **GSD** guard stamina damage (= Dmg unless stated).
**Lng** lunge. **Air:** L LaunchSpeed, AL AirLift, Sl SlamSpeed, SL SelfLift. **St** Fluid stamina. **Gain** meter gain (Fire only uses
it). **×N** = per sub-hit, N sub-hits. Fluid chain hits 0-3 use `Dg 0` and finishers `Dg 0.30` (C13); the Dg column below is what
`ApplyPunishing` overwrites anyway, so it lists the Fluid value. Names live only in `DisplayName`.

`ApplyPunishing(ElementMoveSet set)` (A, shared): exactly today's `CreateFirePunishing` rules generalised to every move list, including
`PauseChain` (13 each) and `DodgeStrike` (11). `CreateFirePunishing()` must equal `ApplyPunishing(CreateFireFluid())` and keep today's
values for every pre-existing field (test).

### 3.1 Fire (Northern Shaolin): existing set plus pause chain and dodge strike

Unchanged: Light chain Flame Jab, Flame Cross, Rising Snap Kick, Dragon Tail Kick, Phoenix Palm (poise 8+9+7+8+11 = 43); Rising Dragon
Kick launcher; air chain Sky Jab, Crescent Flame Kick, Tornado Slam Kick; Falling Axe Kick plunge; Fa Jin Palm; Fire Whip; Flame Wheel;
Fire Blast; Flame Step Strike zip; Flying Fire Kick sprint. Fluid changes only: chain `DodgeCancelAt` (C13), dodge (§2.3.3),
`AirDashesPerJump` 2.

| Slot | Name | AnimationKey / EffectKey | S/A/R | CW | Ch/Dg | Dmg | Po | Rng/Arc | Lng | Air | St | Gain |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| PauseChain[0] | Sweeping Flame Kick (low spinning sweep) | `sweep_kick` / `trail` | .16/.12/.34 | .20-.48 | .30/0 | 11 | 10 | 3.0/160, OriginFwd 0 | .6 | — | 10 | 8 |
| PauseChain[1] (finisher) | Rising Phoenix Kick | `rising_phoenix_kick` / `pillar` | .20/.12/.46 | .52-.80 | .50/.30 | 14 | 12 | 2.8/100 | .4 | L 10, SL 0 | 12 | 12 |
| DodgeStrike | Turning Heel Counter (spinning back kick) | `spin_back_kick` / `burst` | .10/.10/.28 | .12-.38 | .22/0 | 11 | 7 | 2.0 (was 2.8, §8.2)/140 | .6 | — | 0 | 8 |

Pause path poise 8+9+10+12 = 39. Fire rhythm: on-beat +3 Momentum.

### 3.2 Water (Tai Chi): yield, return force, circle; pulls, heals, widest parry

| Slot | Name | AnimationKey / EffectKey | S/A/R | CW | Ch/Dg | Dmg | Po | Rng/Arc | Lng | Air | St | Extra |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Light 1 | Ward Off | `palm_ward` / `burst` | .14/.14/.30 | .18-.52 | .30/.20 | 7 | 6 | 3.0/110 | .35 | — | 8 | KB .3 |
| Light 2 | Roll Back | `palm_rollback` / `whip` | .14/.14/.32 | .18-.54 | .30/.20 | 7 | 6 | 3.6/90 | 0 | — | 8 | Pull .8, KB 0 |
| Light 3 | Press | `forearm_press` / `wave` | .16/.12/.34 | .20-.56 | .32/.22 | 9 | 8 | 3.4/80 | .45 | — | 9 | KB .5, hitstop .045 |
| Light 4 | Push | `two_palm_push` / `wave` | .18/.14/.38 | .30-.66 | .40/.26 | 11 | 9 | 3.8/100 | .4 | — | 10 | KB .8, GSD 12 |
| Light 5 | Single Whip | `single_whip` / `whip` | .22/.18/.46 | .50-.84 | .62/.36 | 17 | 10 | 5.5/150, OriginH 1.2, VR 1.4 | .2 | — | 13 | HealOnHit 2, KB 1.2, hitstop .065 |
| Pause[0] | Cloud Hands | `cloud_hands` / `vortex` | .16/.48/.30 | .56-.90 | .66/.24 | 3 ×4 | 2 ×4 | 3.6/360, OriginFwd 0 | 0 | — | 12 | HitInterval .12, Pull .3 |
| Pause[1] | Part the Wild Horse's Mane | `split_mane` / `wave` | .20/.14/.44 | .52-.80 | .60/.30 | 15 | 14 | 4.5/180 | .3 | — | 12 | KB 2.0, HealOnHit 2 |
| DodgeStrike | Return the Tide | `return_tide` / `wave` | .10/.12/.30 | .12-.40 | .24/0 | 12 | 6 | 2.0 (was 3.2, §8.2)/120 | .6 | — | 0 | HealOnHit 2 |
| Launcher | White Crane Spreads Wings | `crane_rise` / `pillar` | .18/.12/.36 | — | .30/.30 | 8 | 12 | 3.2/90, VR 1.6 | .3 | L 11, SL 9.5 | 12 | |
| Air 1 | Brush Knee Palm | `air_brush_palm` / `trail` | .09/.10/.22 | .12-.38 | .19/.16 | 6 | 5 | 2.8/100, VR 1.8 | .3 | AL 3.2, SL 3.2 | 6 | |
| Air 2 | Fair Lady Works the Shuttles | `air_shuttle` / `vortex` | .12/.14/.26 | .16-.44 | .26/.20 | 7 | 7 | 3.0/160, VR 1.8 | .3 | AL 3.4, SL 3.4 | 7 | |
| Air 3 | Needle at Sea Bottom | `air_needle` / `slam` | .16/.12/.34 | — | .40/.30 | 11 | 18 | 2.8/120, VR 2.2 | .2 | Sl 16, SL 1.5 | 9 | HealOnHit 2 |
| Plunge | Snake Creeps Down | `snake_drop` / `wave` | R .50 | — | .50/.30 | 15 | 22 | ring 2.8 | — | Hang .10, Fall 16 | 16 | KB 1.4 |
| LB+X heavy | Ocean Palm | `tide_palm` / `wave` | .30/.16/.50 | — | .90/.38 | 18 | 26 | 4.5/100 | .3 | — | 20 | GSD 22, KB 2.2, HealOnHit 4. Charge: Max 1.4, sweet .70-1.10, FaJin ×1.6 / poise ×2.0 |
| LB+Y | Water Whip | `water_lash` / `whip` | .22/.14/.40 | — | .42/.34 | 9 | 10 | 7.5/40, OriginH 1.2 | 0 | — | 14 | **Pull 5.0**, Tracking 900 |
| LB+B | Tide Ring | `tide_ring` / `vortex` | .20/.24/.42 | — | .60/.36 | 7 ×2 | 10 ×2 | 3.6/360 | 0 | — | 20 | HitInterval .12, HealOnHit 4, HealPerMoveMax 8, KB 1.4 |
| RB skill | Ice Dart | `dart_flick` / `shards` | .18/0/.28 | — | .34/.26 | 12 | 9 | projectile speed 34, radius .25, range 22 | 0 | — | 16 | single dart this build (fan deferred) |
| Y zip | Wave Ride Strike | `wave_ride` / `wave` | .34/.12/.36 | — | .46/.38 | 11 | 16 | 2.6/100, VR 1.6 | dash | AL 3.2 | 13 | Zip Range 16, Angle 50, MaxHeight 3 |
| Sprint | Tidal Rush Palm | `rush_palm` / `wave` | .20/.14/.40 | — | .50/.32 | 14 | 18 | 3.0/100 | 3.4 | — | 14 | |

String poise 6+6+8+9+10 = 39 (+6 = 45). Guard: `ParryOnly`, DeflectWindow **.20**, lockout .30, arc 360. Aerial: 6 attacks / 2 dashes /
gravity .28 / dash 3.6.

### 3.3 Earth (Hung Gar): rooted horse stance, iron bridge, crushing guards

| Slot | Name | AnimationKey / EffectKey | S/A/R | CW | Ch/Dg | Dmg | Po | Rng/Arc | Lng | Air | St | Extra |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Light 1 | Horse Stance Punch (was "Stone Fist": the name implied stone on the fist, §7) | `horse_punch` / `burst` | .18/.10/.36 | .22-.54 | .30/.24 | 11 | 7 | 2.6/70 | .25 | — | 11 | GSD 16, hitstop .05 |
| Light 2 | Tiger Claw Rake | `tiger_claw` / `line` | .18/.12/.38 | .24-.58 | .32/.26 | 12 | 8 | 2.8/100 | .25 | — | 11 | GSD 18 |
| Light 3 | Rooted Stomp | `stomp_line` / `line` | .22/.12/.40 | .28-.62 | .36/.28 | 13 | 9 | 4.5/25 | 0 | — | 12 | HyperArmor from .10, GSD 18 |
| Light 4 | Butterfly Palms | `butterfly_palms` / `burst` | .24/.12/.44 | .36-.72 | .46/.32 | 15 | 9 | 3.4/70 | .3 | — | 13 | HyperArmor from .08, GSD 22, KB 1.0 |
| Light 5 | Mountain Quake | `quake_slam` / `stomp` | .30/.14/.56 | .58-.94 | .72/.44 | 21 | 11 | 3.6/360, OriginFwd 0, VR 1.2 | 0 | — | 16 | HyperArmor 0, GSD 30, KB 1.6, hitstop .08 |
| Pause[0] | Raise the Boulder | `boulder_raise` / `pillar` | .24/.12/.30 | .36-.66 | .40/.28 | 10 | 10 | 2.6/120, VR 1.6 | 0 | L 6 | 12 | HyperArmor from .10 |
| Pause[1] | Boulder Hurl | `boulder_hurl` / `burst` | .26/0/.48 | .52-.80 | .56/.36 | 20 | 14 | projectile speed 24, gravity 6, radius .6, explosion 2.5, range 18 | 0 | — | 14 | HyperArmor 0, GSD 26, KB 1.8 |
| DodgeStrike | Pivot Elbow | `pivot_elbow` / `burst` | .12/.10/.34 | .14-.44 | .26/0 | 14 | 7 | 2.0 (was 2.6, §8.2)/120 | .5 | — | 0 | HyperArmor 0, GSD 18 |
| Launcher | Rising Pillar | `pillar_uppercut` / `pillar` | .20/.12/.40 | — | .32/.32 | 10 | 14 | 2.8/90, VR 1.6 | .2 | L 11, SL 9.5 | 13 | HyperArmor from .08 |
| Air 1 | Hammer Fist | `air_hammer` / `burst` | .10/.10/.26 | .14-.40 | .20/.18 | 8 | 7 | 2.4/80, VR 1.8 | .3 | AL 3.0, SL 3.0 | 7 | martial strike, dust VFX only (§7) |
| Air 2 | Tiger Tail Kick | `air_back_kick` / `trail` | .14/.10/.30 | .18-.46 | .26/.22 | 10 | 9 | 2.6/120, VR 1.8 | .3 | AL 3.0, SL 3.0 | 8 | dust only |
| Air 3 | Meteor Drop | `air_meteor` / `slam` | .18/.12/.38 | — | .44/.32 | 15 | 22 | 2.8/160, VR 2.2 | .2 | Sl 20, SL 1.0 | 10 | spikes appear on landing only |
| Plunge | Earthquake Drop | `quake_drop` / `stomp` | R .60 | — | .60/.40 | 22 | 30 | ring 2.6 | — | Hang .06, Fall 22 | 18 | HyperArmor, KB 1.2 |
| LB+X heavy | Mountain Fa Jin | `root_fajin` / `line` | .30/.16/.60 | — | 1.0/.44 | 24 | 34 | 6.0/30 | .2 | — | 24 | HyperArmor, GSD 40, KB 1.8. Charge: Max 1.5, sweet .90-1.15, FaJin ×2.0 / poise ×2.0 |
| LB+Y | Stone Spike Line | `spike_line` / `line` | .26/.20/.44 | — | .62/.40 | 14 | 16 | 8.0/18 | 0 | L 7 | 18 | GSD 18 |
| LB+B | Stone Tent | `stone_tent` / `dome` | .30/.12/.36 | — | .50/.20 | 14 | 20 | 3.0/360, OriginFwd 0 | 0 | — | 20 | HyperArmor 0 (self-guard deferred), KB 2.0 |
| RB skill | Boulder Toss | `boulder_toss` / `burst` | .30/0/.36 | — | .46/.34 | 16 | 18 | projectile speed 26, gravity 4, radius .5, explosion 1.8, range 22 | 0 | — | 22 | GSD 20 |
| Y zip | Earth Surf Charge | `earth_surf` / `wave` | .32/.12/.40 | — | .48/.40 | 14 | 22 | 2.4/100 | dash | AL 0 | 15 | HyperArmor 0; Zip Range 12, Angle 45, **MaxHeight 1.0** |
| Sprint | Avalanche Shoulder | `shoulder_charge` / `burst` | .20/.14/.44 | — | .54/.34 | 18 | 24 | 2.8/90 | 3.0 | — | 16 | HyperArmor |

String poise 7+8+9+9+11 = 44 (+7 = 51). Guard: `BlockAndParry`, arc 180, MoveSpeed .35, GuardBreakStagger 1.2, DeflectWindow **.12**,
lockout .40. Aerial: 4 attacks / 1 dash / gravity .45 / dash 2.6.

### 3.4 Air (Baguazhang): circle walking, palm changes, never standing still

| Slot | Name | AnimationKey / EffectKey | S/A/R | CW | Ch/Dg | Dmg | Po | Rng/Arc | Lng | Air | St | Extra |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Light 1 | Piercing Palm | `piercing_palm` / `burst` | .09/.10/.22 | .10-.36 | .19/.12 | 3 ×2 | 2 ×2 | 3.0/60 | .6 | — | 7 | HitInterval .06, Orbit 20 |
| Light 2 | Turning Palm | `turning_palm` / `trail` | .09/.12/.22 | .12-.38 | .21/.12 | 3 ×2 | 2 ×2 | 3.0/120 | .6 | — | 7 | HitInterval .08, Orbit 35 |
| Light 3 | Swimming Body Sweep | `swim_sweep` / `trail` | .10/.14/.26 | .14-.44 | .24/.14 | 3 ×3 | 2 ×3 | 3.4/160 | .4 | — | 8 | HitInterval .05, Orbit 30 |
| Light 4 | Double Palm Change | `double_palm_change` / `vortex` | .12/.18/.28 | .22-.52 | .30/.16 | 3 ×3 | 3 ×3 | 3.0/200 | .8 | — | 9 | HitInterval .07, **Orbit 150** |
| Light 5 | Gale Palm | `gale_palm` / `cone` | .16/.12/.40 | .40-.68 | .52/.24 | 14 | 12 | 5.0/70 | .3 | — | 11 | **KB 4.0**, hitstop .07 |
| Pause[0] | Circle Walk Flurry | `circle_walk` / `trail` | .09/.40/.24 | .44-.72 | .49/.14 | 3 ×4 | 2 ×4 | 3.0/120 | .4 | — | 10 | HitInterval .10, Orbit 180 |
| Pause[1] | Whirlwind | `whirlwind` / `vortex` | .18/.30/.40 | .52-.80 | .70/.30 | 3 ×3 | 3 ×3 | 3.8/360, VR 1.6 | 0 | L 9, AL 4 | 14 | HitInterval .10 |
| DodgeStrike | Circle Step Palm | `circle_step_palm` / `trail` | .09/.14/.24 | .12-.38 | .24/0 | 3 ×3 | 2 ×3 | 2.0 (was 3.0, §8.2)/140 | .7 | — | 0 | HitInterval .05, Orbit 90 |
| Launcher | Updraft Palm | `updraft_palm` / `pillar` | .12/.10/.32 | — | .24/.24 | 6 | 8 | 3.0/100, VR 1.8 | .3 | L 12, SL 10.5 | 10 | |
| Air 1 | Air Swipe | `air_swipe` / `trail` | .09/.10/.20 | .10-.34 | .19/.12 | 3 ×2 | 2 ×2 | 3.0/110, VR 1.8 | .3 | AL 3.2, SL 3.2 | 5 | HitInterval .05 |
| Air 2 | Spiral Kick | `air_spiral_kick` / `vortex` | .10/.14/.22 | .14-.40 | .24/.14 | 3 ×3 | 2 ×3 | 3.0/200, VR 1.8 | .3 | AL 3.4, SL 3.4 | 6 | HitInterval .05 |
| Air 3 | Downburst Palm | `air_downburst` / `slam` | .14/.10/.32 | — | .36/.24 | 10 | 16 | 3.2/140, VR 2.4 | .2 | Sl 18, SL 2.0 | 8 | |
| Plunge | Air Burst Landing | `air_landing` / `wave` | R .30 | — | .30/.18 | 10 | 14 | ring 3.6 | — | Hang .12, Fall 14 | 12 | KB 2.6 |
| LB+X heavy | Hurricane Palm | `hurricane_palm` / `cone` | .20/.14/.42 | — | .70/.30 | 16 | 22 | 7.0/30 | .2 | — | 20 | KB 5.0. Charge: Max .9, sweet .40-.60, FaJin ×1.6 / poise ×1.6 |
| LB+Y | Air Blade | `air_blade` / `line` | .16/.12/.34 | — | .40/.28 | 12 | 8 | 10/10, VR 2.0 | 0 | — | 16 | Tracking 900 |
| LB+B | Air Shield | `air_shield` / `dome` | .04/.50/.30 | — | .56/.20 | 4 ×2 | 5 ×2 | 2.8/360 | 0 | — | 16 | HitInterval .25, HyperArmor 0 (reflect deferred), KB 2.4 |
| RB skill | Air Blast | `air_blast` / `burst` | .14/0/.22 | — | .26/.20 | 8 | 10 | projectile speed 40, radius .45, range 28 | 0 | — | 14 | KB 2.5 |
| Y zip | Wind Leap Strike | `wind_leap` / `trail` | .26/.10/.30 | — | .38/.30 | 9 | 12 | 2.6/110, VR 2.0 | dash | AL 3.4 | 10 | Zip **Range 18, Angle 60, MaxHeight 6** |
| Sprint | Wind Runner Kick | `wind_runner_kick` / `trail` | .14/.12/.34 | — | .40/.26 | 12 | 14 | 2.8/100 | 4.0 | — | 12 | |

Air startups are ≥ .09 so the beat window opens after the move starts at the 1.20 on-beat rate (§2.4.3 validation).
String poise 4+4+6+9+12 = 35 (+4 = 39). Guard: `ParryOnly`, DeflectWindow .18, lockout .30, arc 360 (`EvadeParry` deferred).
Aerial: 9 attacks / 2 dashes / gravity .18 / dash 4.2.

### 3.5 Dodge profiles per element (Fluid; Punishing = §2.3.3 Punishing column for all four via `ApplyPunishing`)

| Field | Fire: Flame Step | Water: Flowing Step | Earth: Stone Slide | Air: Circle Step |
|---|---|---|---|---|
| `EvadeOutDistance` / `SideSlipDistance` / `SlipInMaxDistance` / `BackstepDistance` | 4.0 / 3.0 / 3.4 / 2.2 | 4.6 / 3.6 / 3.0 / 2.4 | 3.0 / 2.2 / 2.4 / 1.8 | 4.8 / 3.6 / 3.4 / 2.6 |
| `Duration` / `DashEaseOut` | .24 / .70 | .28 / .65 (was .30, §8.2) | .22 / .80 | .28 / .65 (was .40, §8.2) |
| `IFrameStart`-`IFrameEnd` | 0-.18 | 0-.20 | 0-.14 | 0-.20 |
| `EvadeAttackCancelAt` / `SlipInAttackCancelAt` / `NextDodgeAt` | .08 / .05 / .18 | .10 / .06 / .22 | .06 / .05 / .20 | .08 / .05 / .16 |
| `ChainMax` | 3 | 3 | 2 | 4 |
| `PerfectWindow` / `CounterWindow` / `CounterDamageMultiplier` | .12 / .8 / 1.5 | .16 / 1.0 / 1.4 | .10 / .8 / 1.8 | .12 / .8 / 1.3 |

All other dodge fields as Fire. Stamina 0 Fluid, 16 Punishing for every element.

### 3.6 Element summary

| | Fire | Water | Earth | Air |
|---|---|---|---|---|
| String damage / time to finisher end at 1.0× | 58 / 2.03 s (28.6 dps) | 51 / 2.18 s (23.4) | 72 / 2.44 s (29.5) | 44 / 1.62 s (27.2) |
| Role | single-target pressure | mid range, pull, heal, best parry | slow, armour, block, guard damage | fast multi-hit, circling, knockback, best air game |
| Defence | parry | parry (widest) | block + parry (tightest) | parry |
| Stance (`AnimationStyle` idle override) | bow stance (existing) | `water:idle` upright, weight back, soft round arms | `earth:idle` horse stance, fists chambered | `air:idle` torso turned in, lead palm at eye height |

---

## 4. Contracts between core and Unity (A writes all of these first; frozen afterwards)

### 4.1 Enums (`Core/Combat/CombatEnums.cs`, append only)

```csharp
public enum PlayerCommand { None, Light, Heavy, Dodge, Jump, Skill, Heal, Guard, ZipStrike, AbilityNorth, AbilityEast, SwitchStrike }
public enum PlayerAttackKind { None, Light, Heavy, Sprint, Skill, Plunge, ZipStrike, Launcher, Air, Ability, DodgeStrike }
public enum BeatGrade { None, OnBeat, Late, Early, Mashed, Pause, Auto }
public enum ComboBranch { Main, Pause, DodgeStrike, Air, Launcher, Other }
public enum DodgeKind { Traverse, EvadeOut, SlipIn, SideSlip, Backstep, AutoEvade, AirDash }
public enum ComboEndReason { Timeout, TookHit, Staggered, Died, Respawned, PresetChanged }
public enum SwitchDeniedReason { Cooldown, NotLearned, Busy, SameElement }
```

### 4.2 Events (`Core/Combat/PlayerEvent.cs`; append after `ChargeReadyCue`, this order)

| # | `PlayerEventType` | Fields filled |
|---|---|---|
| 1 | `ComboBeatOpened` | Move, AttackId, ChainIndex, Branch, Element, Duration (window length), Amount (time from open to BeatTime) |
| 2 | `BeatJudged` | Grade, Amount (signed press offset from BeatTime), Count (`OnBeatStreak`), Element. Re-emitted as `Mashed` on downgrade |
| 3 | `ComboHit` | Move, AttackId, MoveInstanceId, AttackKind, Branch, ChainIndex, IsFinisher, Element, Grade (of the move), Count (ComboCount), Amount (combo timeout remaining), Origin (contact), InAir (target airborne) |
| 4 | `PerfectString` | Move (finisher), Element |
| 5 | `ComboEnded` | Count (final), EndReason |
| 6 | `ElementSwitched` | Element, PreviousElement, IsSwitchStrike, Branch, Count (MixLevel), InAir |
| 7 | `ElementSwitchDenied` | Element (requested), DenyReason, Duration (cooldown remaining) |
| 8 | `MixChanged` | Count (MixLevel), Amount (damage multiplier), Element (just added) |
| 9 | `MixFinisher` | Count (level), Move, AttackId, Element |
| 10 | `DangerWarning` | AttackerId, Count (hit index), Origin (attacker feet), Direction (attacker → player, flat, normalised), Duration (time to impact), MustDodge, IsRanged |
| 11 | `DangerNow` | same as DangerWarning |
| 12 | `DangerCleared` | AttackerId, Count (hit index) |
| 13 | `DodgeChainLimited` | Duration (cooldown remaining) |

Existing events gain fields: `AttackStarted` + ChainIndex, Branch, IsFinisher, PlaybackRate, Element, Grade (that triggered it),
MoveInstanceId · `AttackActiveStart`/`AttackActiveEnd`/`ProjectileLaunched`/`AttackEnded` + Element, MoveInstanceId (one Active pair per
sub-hit) · `DodgeStarted` + DodgeKind, LocalDirection, Element · `PerfectDodge` / `Deflected` + Element.

New `PlayerEvent` fields (appended in this order):
```csharp
public BeatGrade Grade;
public int Count;
public int ChainIndex;
public ComboBranch Branch;
public bool IsFinisher;
public float PlaybackRate;
public ElementId Element;
public ElementId PreviousElement;
public DodgeKind DodgeKind;
public Vector3 LocalDirection;
public int AttackerId;
public int MoveInstanceId;
public bool MustDodge;
public bool IsRanged;
public bool IsSwitchStrike;
public ComboEndReason EndReason;
public SwitchDeniedReason DenyReason;
```
`DamageInfo` (`Core/Contracts/CombatContracts.cs`) appends `public ElementId Element; public int MoveInstanceId; public float PullDistance;`.
No new `EnemyEventType` values.

### 4.3 Model API (`PlayerCombatModel`; read-only views in new partial `PlayerCombatModel.Views.cs`)

```csharp
public PlayerCombatModel(PlayerTuning tuning, ElementLoadout loadout, int ownerId = 0, float facingYaw = 0f);
public PlayerCombatModel(PlayerTuning tuning, ElementMoveSet moveSet, int ownerId = 0, float facingYaw = 0f); // kept: one-set loadout
public void ApplyTuning(PlayerTuning newTuning, ElementMoveSet newMoveSet);   // kept
public void ApplyTuning(PlayerTuning newTuning, ElementLoadout newLoadout);   // ends combo (PresetChanged), keeps ActiveElement if learned
public void OnAttackLanded(in HitResult result, int attackId, bool targetAirborne); // new overload; old ones forward with false

public ElementLoadout Loadout { get; }
public ElementMoveSet MoveSet { get; }            // existing: the ACTIVE element's set
public ElementId ActiveElement { get; }
public bool IsLearned(ElementId element);
public float SwitchCooldownRemaining { get; }
public float SwitchCooldown01 { get; }           // 1 = just switched, 0 = ready
public int ComboCount { get; }
public float ComboTimeRemaining { get; }
public float ComboTimeRemaining01 { get; }
public RhythmView Rhythm { get; }
public int StringNextIndex { get; }              // -1 = none
public ComboBranch StringBranch { get; }
public int MixLevel { get; }                     // 0 when no combo
public int MixElementsMask { get; }              // bit (1 << (int)ElementId)
public float MeterFraction(ElementId element);   // identity meter 0..1 (Fire Momentum; others 0)
public int PendingThreatCount { get; }
public bool TryGetThreat(int index, out IncomingStrike strike);
public bool TryGetMostImminentThreat(out IncomingStrike strike);
public void NotifyIncomingStrike(in IncomingStrike strike);
public void CancelIncomingStrikes(int attackerId);
public void NotifyEnemyStrike(Vector3 origin, Vector3 forward, MoveData move, Vector3 attackerFeet); // existing, unchanged
```

```csharp
public struct RhythmView      // Core/Combat/RhythmView.cs
{
    public bool Active;          // a string move is running and its beat hasn't been judged yet
    public float TimeToBeat;     // game seconds; > 0 before the beat, < 0 after
    public float EarlyWindow;    // effective BeatEarly (preset + element)
    public float LateWindow;     // effective BeatLate (+ switch bonus if a switch strike is pending)
    public int Streak;           // OnBeatStreak
    public BeatGrade LastGrade;
    public float PlaybackRate;   // of the running move
}

public struct IncomingStrike  // Core/Combat/IncomingStrike.cs
{
    public int AttackerId; public int AttackKey; public int HitIndex;
    public double ImpactClock;
    public Vector3 AttackerFeet; public Vector3 StrikeForward;
    public bool Parryable; public bool Unblockable; public bool Ranged; public bool Hidden;
    public bool Warned; public bool NowFired;   // set by the model
    public bool MustDodge => !Parryable || Unblockable;
}

public static class DangerSenseRelay   // Core/AI/DangerSenseRelay.cs
{
    public static void OnEnemyEvent(in EnemyEvent e, EnemyBrain brain, int attackerId,
                                    Vector3 attackerFeet, Vector3 playerFeet, PlayerCombatModel player);
}
```

### 4.4 Tuning types (`Core/Tuning`)

```csharp
[Serializable] public class ElementLoadout
{
    public ElementMoveSet Fire, Water, Earth, Air;
    public ElementId Starting = ElementId.Fire;
    public bool LearnedFire = true, LearnedWater = true, LearnedEarth = true, LearnedAir = true;
    public ElementMoveSet Get(ElementId element);       // null for None
    public bool IsLearned(ElementId element);           // false when the set is null
    public static ElementLoadout CreateFluid();         // the four Create<Element>Fluid() factories
    public static ElementLoadout CreatePunishing();
    public static ElementLoadout FromSingle(ElementMoveSet set);
}
// ElementMoveSet: + MoveData[] PauseChain; MoveData DodgeStrike; ElementRhythm Rhythm; string AnimationStyle;
//   int DataVersion = 0; public const int CurrentDataVersion = 5;
//   static CreateWaterFluid/Punishing, CreateEarthFluid/Punishing, CreateAirFluid/Punishing; static void ApplyPunishing(ElementMoveSet)
// PlayerTuning: + RhythmTuning Rhythm; ComboTuning Combo; MixTuning Mix; ElementSwitchTuning ElementSwitch;
//   DangerSenseSettings DangerSense; float StringMemoryAfterAction; float DodgeStrikeGrace;
//   int DataVersion = 0; public const int CurrentDataVersion = 5;
// MoveData: + int HitCount = 1; float HitInterval; float PullDistance; float OrbitDegrees; float HealOnHit; float HealPerMoveMax;
// DodgeProfile: fields of §2.3.3 (Distance/AttackCancelAt kept as read-only property aliases for old code)
// EnemyAttackData: + bool HideDangerSense; float DangerLeadScale = 1f;
// EnemyTuning: + static EnemyTuning CreateTutorialPartner();  // Dao Soldier copy: Unkillable, Launchable, DisplayName "Sparring Partner"
// EnemyBrain: + bool Passive { get; set; }   // true = never starts an attack (tutorial)
```

### 4.5 Animation keys and effect keys (`Core/Animation/AnimationKeys.cs`)

New `AnimationKeys` constants (65; total 111): Fire `sweep_kick`, `rising_phoenix_kick`, `spin_back_kick`; dodge `dodge_slip`,
`dodge_side_l`, `dodge_side_r`, `dodge_evade`; `element_switch`; plus every Water, Earth and Air key in §3.2-3.4 (19 each).
Each gets a placeholder clip in `PoseLibraryDefaults.Placeholders.cs` (alias of the nearest Fire clip) so `AnimationTests` stays green
from day one. `PoseLibraryDefaults` becomes `static partial class`. Dodge clip choice: SlipIn → `dodge_slip`; SideSlip / AutoEvade →
`dodge_side_l` / `_r` from `LocalDirection.X`; EvadeOut → `dodge_evade`; Backstep → `backstep`; Traverse → `dodge`; AirDash → `air_dash`.
Charges per element use style overrides of `charge` (`water:charge` etc.), not new keys.

`EffectKeys` is a closed set of shapes (13): existing `burst cone pillar whip wheel slam trail` plus `wave line dome vortex shards stomp`.
Move data may only use these. The element supplies the look.

### 4.6 Unity-side contract (A creates thin versions; then ownership moves as §5 says)

```csharp
// Scripts/Player/PlayerController.cs
public delegate void PlayerEventHandler(in PlayerEvent e);
public event PlayerEventHandler GameplayEvent;      // every model event, after the controller's own handling
public void Spawn(..., PlayerTuningAsset tuning, ElementLoadoutAsset loadout);  // overload next to today's
public void ApplyTuning(PlayerTuning tuning, ElementLoadout loadout);
// UpdateElementSelect removed: the model reads PlayerInputFrame.ElementSelect; the "not learned" toast is driven by ElementSwitchDenied.

// Scripts/Player/ElementLoadoutAsset.cs : ScriptableObject
public MoveSetAsset Fire, Water, Earth, Air;
public bool LearnedFire = true, LearnedWater = true, LearnedEarth = true, LearnedAir = true;
public ElementLoadout ToLoadout();

// Scripts/Sandbox/SandboxDirector.cs
public void Configure(PlayerTuningAsset fluidTuning, ElementLoadoutAsset fluidLoadout,
                      PlayerTuningAsset punishingTuning, ElementLoadoutAsset punishingLoadout, Transform spawn);
public void SetEnemiesActive(bool active);          // A: stub that logs; D implements

// Scripts/Input/PlayerInputReader.cs
public IReadOnlyList<ElementId> ElementSlots { get; }   // order Y, B, A, X; default Fire, Water, Earth, Air
public bool ElementModifierHeld { get; }                // RB held (existing rbHeld)
public bool UsingGamepad { get; }                       // if not already public

// Scripts/Enemies/EnemyStrikes.cs: calls DangerSenseRelay.OnEnemyEvent next to NotifyPlayerOfStrike.

// Scripts/Bending/ElementVfxLibraryAsset.cs : ScriptableObject (shell; C fills behaviour)
[Serializable] public class SlotTexture { public string Slot; public Texture2D Texture; public bool HasAlpha; }
[Serializable] public class ElementVfxEntry { public ElementId Element; public Color HudColor; public SlotTexture[] Textures; }
public ElementVfxEntry[] Elements;
public Material AdditiveMaterial, AlphaBlendMaterial;   // optional; builder creates them so player builds keep the shaders
// Scripts/Bending/ElementVfxBootstrap.cs : MonoBehaviour (shell)
public ElementVfxLibraryAsset Library;

// Scripts/UI/HudGlyphs.cs (complete, small): enum HudGlyph { A, B, X, Y, LB, RB, LT, RT, LS, RS, View, Menu, DPadDown };
// Xbox colours A (0.42,0.78,0.20) B (0.86,0.20,0.17) X (0.15,0.45,0.85) Y (0.95,0.75,0.10), LB/RB grey pill;
// keyboard labels from the same table; HudGlyphText.Parse("{RB}+{B} mid-combo") caches segments; Draw(HudPainter, Rect, bool gamepad).
```

New folders, each with `.meta`, created **only by A**: `Art/`, `Art/VFX/`, `Art/VFX/{Common,Fire,Water,Earth,Air}/` (with `.keep`),
`Scripts/Core/Tutorial/`, `Editor/Vfx/`. A also adds a `.png` template to `tools/gen-unity-meta.py`
(`fileFormatVersion: 2\nguid: {guid}\n`) and writes `Art/VFX/README.md` (the texture contract of §5 C).

---

## 5. Work packages

### Rules for every builder
1. **Only the owner edits a file.** Everyone may read anything. Ownership below is disjoint between B, C and D.
2. **A lands first, alone.** A pushes a checkpoint commit `A-contract` (all §4 signatures with stub behaviour, compile 5/5, all tests
   green) before the rules. B, C and D may branch from `A-contract` to start early but **must rebase onto merged A** before their PR.
3. Signatures in §4 are frozen after A. A needed change goes to the architect.
4. Files A creates for another package (marked "→ B/C/D") change owner when A merges; A never edits them again.
5. Each builder runs `python3 tools/gen-unity-meta.py <its new files>` with explicit paths, never without arguments, and never creates
   folders (A made them all).
6. Parallel builds: `export VR_BUILD_DIR=$TMPDIR/vr-build-<letter>` and a separate `TMPDIR` per worktree before any tool (the shared
   lock and output dir otherwise serialise and clobber builds).
7. Merge order after A: **B → C → D** (D last: its builder seeds assets from B's factories and wires C's components). Each rebases first.
8. `.png` goes through Git LFS. Remind David to `git lfs lock` before editing any `.blend`/`.fbx`/`.psd`.
9. Do not commit. The judge reviews each package's worktree.

### Package A: core contracts + combat-feel rules (lands first)
**Contract that must exist first:** none.
**Owns:**
- `Scripts/Core/Combat/**` (incl. new `PlayerCombatModel.Views.cs`, `PlayerCombatModel.Rhythm.cs`, `PlayerCombatModel.Switch.cs`,
  `PlayerCombatModel.Danger.cs`, `RhythmView.cs`, `IncomingStrike.cs`), `Scripts/Core/Contracts/**`, `Scripts/Core/Movement/**`,
  `Scripts/Core/Math/**`, `Scripts/Core/AI/**` (`DangerSenseRelay.cs`, `EnemyBrain.Passive`, pull, break-out per `MoveInstanceId`).
- `Scripts/Core/Tuning/**` except the three files handed to B (new: `RhythmTuning.cs`, `ComboTuning.cs`, `MixTuning.cs`,
  `ElementSwitchTuning.cs`, `DangerSenseSettings.cs`, `ElementRhythm.cs`, `ElementLoadout.cs`). Fire's `PauseChain`, `DodgeStrike` and
  rhythm data (§3.1) and Fire dodge (§2.3.3) are A's.
- Creates → B: `Core/Tuning/ElementMoveSet.Water.cs`, `.Earth.cs`, `.Air.cs` (stubs: clone Fire with Element, DisplayName, AnimationStyle,
  `Momentum.Enabled = false`), `Core/Animation/PoseLibraryDefaults.Placeholders.cs`. Edits then → B: `Core/Animation/AnimationKeys.cs`
  (append keys, EffectKeys), `Core/Animation/PoseLibraryDefaults.cs` (`partial` only).
- Creates/edits → C: `Scripts/Player/PlayerController.cs` (§4.6 only), `Scripts/Enemies/EnemyStrikes.cs` (relay call),
  `Scripts/Bending/ElementVfxLibraryAsset.cs`, `Scripts/Bending/ElementVfxBootstrap.cs` (shells).
- Creates/edits → D: `Scripts/Sandbox/SandboxDirector.cs` (§4.6 overload + stub), `Scripts/Input/PlayerInputReader.cs` (accessors).
- Creates, then frozen (no owner): `Scripts/Player/ElementLoadoutAsset.cs`, `Scripts/UI/HudGlyphs.cs`, all new folders + `.meta`,
  `Art/VFX/README.md`, `tools/gen-unity-meta.py` (.png template).
- Tests: every existing `Tests/EditMode/*.cs` except `AnimationTests.cs`; new `CombatStringMemoryTests.cs`, `CombatRhythmTests.cs`,
  `CombatPauseBranchTests.cs`, `CombatDodgeFlowTests.cs`, `ElementSwitchTests.cs`, `ElementMixTests.cs`, `ComboCounterTests.cs`,
  `DangerSenseTests.cs`, `EnemyDangerRelayTests.cs`, `MultiHitMoveTests.cs` (test lists in §6.1).
- `tools/CombatSim/**` except `Scenarios/AnimationScenario.cs`; `tools/CombatSim/README.md`; `tools/README.md`.
**Reads:** everything. **Must not** touch any other `Scripts/` file or any `Editor/` file.
**Work:** §2 in full; §4 contracts; CombatSim bots `rhythm`, `sloppy`, `slowtap`, `reactpress`, `switcher`, `pauser`, `sense`
(schedule from move data at `AttackStarted`, as `anticipate` does); scenarios `rhythm`, `dodgeflow`, `switch`, `danger`, `elements`
(per-element string dps, poise budgets, beat validation, for B to run); `duels` gains `rhythm`, `switcher`, `sense`; `fuzz` gains random
`ElementSelect` chords and dodge-kind sticks plus the §6.1 invariants; `framedata` prints On/OffBeat scaled frame data per element;
`TuningOverrides` targets `rhythm`, `combo`, `mix`, `switch`, `danger`, `loadout.<element>.*`; the `anim` trace carries Branch, Grade,
Element, DodgeKind for `render_fight.py`. After A merges, the sandbox plays exactly as today plus the new rules (Water/Earth/Air are
Fire clones until B lands).

### Package B: element move-set data + poses
**Contract that must exist first:** `ElementMoveSet.cs` (§4.4 fields), `MoveData.cs` (§2.8 fields), `ElementRhythm.cs`,
`AnimationKeys.cs` (all §4.5 keys), `PoseLibraryDefaults.Placeholders.cs`.
**Owns:**
- `Scripts/Core/Tuning/ElementMoveSet.Water.cs`, `.Earth.cs`, `.Air.cs` (§3.2-3.5 data; Punishing via `ApplyPunishing`).
- `Scripts/Core/Animation/**` (incl. `AnimationKeys.cs` append-only with EffectKeys frozen; `PoseLibraryDefaults*.cs`; new
  `PoseLibraryDefaults.Water.cs`, `.Earth.cs`, `.Air.cs`, `.Dodge.cs`, emptying `.Placeholders.cs`; `FighterAnimator.cs` `SetStyle`;
  `PlayerAnimationFeed.cs` style from `Loadout.Get(ActiveElement).AnimationStyle`, dodge-kind clips, `element_switch` flourish only when
  free (Locomotion/idle; never mid-combo), playback rate honoured; `EnemyAnimationFeed.cs` if needed).
- `Scripts/Body/**`, `Editor/Animation/**` (pack-clip rules: slip/weave/duck, stomp, palm).
- Tests: `Tests/EditMode/AnimationTests.cs` (iterate all four loadout sets), new `ElementMoveSetDataTests.cs`, `ElementPoseTests.cs`.
- `tools/CombatSim/Scenarios/AnimationScenario.cs` (element per effect, four-element gallery), `tools/render/**` (colour by element).
**Reads:** all Core, `tools/CombatSim/**`, this spec.
**Work:** fill the three sets exactly from §3; stances (§3.6); a real `PoseClip` for every new key with full extension (Reach 1) on
Active 0 with `PoseEase.Snap`; dodge clips facing the fight; the multi-hit moves animate each sub-hit (`ClipTiming.HitCount` exists).

### Package C: Unity VFX, HUD, danger sense and element-switch presentation
**Contract that must exist first:** `PlayerEvent.cs`, `CombatEnums.cs`, `PlayerCombatModel.Views.cs`, `IncomingStrike.cs`,
`AnimationKeys.cs` (EffectKeys), `ElementVfxLibraryAsset.cs`, `ElementVfxBootstrap.cs`, `HudGlyphs.cs`, `PlayerInputReader` accessors.
**Owns:**
- `Scripts/Bending/**` (new `ElementVfx.cs` facade with Fire forwarding byte-for-byte to today's paths, `ElementVfxStyle.cs` palettes,
  `ElementMoveEffects.cs` dispatch moved out of `PlayerFeedback.ActiveStarted`; fills the two shells; edits `FireVfxRunner.cs`,
  `FireVfxPieces.cs`: piece shapes Sphere/RingBand/Quad/Cube, texture, billboard, gravity, spin, three pooled material kinds).
- `Scripts/Player/**` except `ElementLoadoutAsset.cs`, `MoveSetAsset.cs`, `PlayerTuningAsset.cs`: `PlayerController.cs` (projectile visual by
  element, toasts), `PlayerFeedback.cs`, `PlayerFeedbackSettings.cs` (`[Header("Rhythm feedback")]` volumes, `StompShake`),
  `PlayerRumble.cs` (danger tick 0.15 low / 0.25 high / 0.04 s; on-beat tick), new `RhythmAudio.cs` (created at runtime by
  `PlayerFeedback`; `AudioClip.Create` chime 880 Hz 60 ms, hit tick 440 Hz 30 ms, finisher 3-tone 180 ms, switch whoosh 150 ms).
- `Scripts/Combat/**` (`ProjectileVisual` + `WaterOrb, Rock, AirBall`; `FireProjectile` impact → `ElementVfx.Explosion(element, …)`).
- `Scripts/Enemies/**` (`EnemyRigPresenter` launch/knockdown effect in the player's element).
- `Scripts/UI/**` except `HudControlsOverlay.cs`, `HudTutorialPanel.cs`, `HudGlyphs.cs`: `CombatHud.cs` (calls the widgets),
  `HudPainter.cs` (`Ring`, `Diamond`), new `HudComboCounter.cs`, `HudBeatPulse.cs`, `HudElementWheel.cs`, `HudDangerSense.cs`.
- `Scripts/Greybox/GreyboxShapes.cs` (`CreateAlphaBlend`), `Editor/Vfx/**` (`VfxTextureImporter.cs`), PNGs under `Art/VFX/**`.
**Reads:** all Core, `Scripts/Sandbox/**`, `Scripts/Input/**`, this spec.
**Work:**
- **Per-element looks for all 13 EffectKeys** (Water/Earth/Air table from the presentation scout: Water blue alpha blobs + ribbons +
  droplets with gravity; Earth opaque Lit rock chunks/cubes with 18 m/s² gravity, dust, cracks, no point lights; Air additive streaks,
  ring bands, swirls, no lights). Fire keeps today's look; Fire falls back to `burst` for keys it never used.
- Non-move effects: `Switch(from, to, chest, feet)` ring + 0.4 s limb aura; `BeatAccent` (white-gold ring 0.12 s on `ComboHit` with
  OnBeat/Auto); MIX accent (first hit after a mid-combo switch at 1.4× scale); dodge slip afterimages; per-element evade trails;
  dodge-strike burst ×1.2.
- **HUD** (allocation-free, 1080p reference units, `HudPainter.U()`): combo counter right side (≥ 2 hits; element-tinted; pop 1.3→1.0 over
  0.12 s; "ON BEAT" + up to 5 streak pips; drain bar from `ComboTimeRemaining01`; "MIX n" (elements landed) with element icons; fade 0.5 s on
  `ComboEnded`); beat pulse under it (approach ring 2.2r → r driven only by `Rhythm.TimeToBeat`, gold inner disc inside the window,
  burst on OnBeat, grey tick on Early/Mashed, amber LATE on Late; optional copy at the player's feet, on during the tutorial); element wheel bottom-right
  (four diamonds in face-button layout from `ElementSlots`, active lit, others 55 %, grows ×1.25 with "RB +" while
  `ElementModifierHeld`, white flash + radial cooldown wipe on switch, colours from the library asset: Fire (1,.38,.12) Water
  (.25,.62,1) Earth (.45,.75,.25) Air (.95,.88,.6)); danger sense above the head (chevrons or `Common/danger.png`; one direction
  triangle per threat up to 3, most urgent larger; gold parryable, red must-dodge, white after `DangerNow`; pulse 6→14 Hz with urgency;
  off-screen attacker shows at the circle edge; hidden when behind the camera). Screen angle is computed here from
  `TryGetThreat` and the camera, not in the core.
- **Textures.** Contract: `Art/VFX/<Element>/<slot>.png`, lower-case. Slots: Fire (optional) `flame ember ribbon smoke burst ring whip orb`;
  Water `splash mist droplet ribbon ring shard orb whip wave`; Earth `dust rock crack debris gravel pillar_top swirl ribbon`; Air
  `swirl streak ring ribbon puff`; Common `beat_ring spark danger`. Importer: under `Art/VFX/`, sRGB, mipmaps, max 512,
  `alphaIsTransparency`, Repeat for `ribbon`/`streak` else Clamp; a texture with no alpha (black background) draws additive, with alpha
  draws alpha-blended. Every slot has a primitive fallback (blobs → sphere, rings/cracks/swirls → ring band, streaks/shards → stretched
  sphere, rocks/debris → cube, ribbon → plain trail), so a missing texture or `Library == null` still renders; one log message at bootstrap
  listing every missing slot (a line each), nothing per frame.
- **VFX art status (blocked).** 24 images were generated but none reached the repo (egress policy refused the CDN). `Art/VFX` holds no
  PNGs; C ships on fallbacks and must not depend on them. When David downloads them by hand, rename into slots:

  | Generated file | Slot |
  |---|---|
  | Fire `flame_tongue`, `ember_sparks_cluster`, `flame_trail_streak`, `heat_smoke_wisp`, `fire_burst_puff`, `fire_swirl_ring`, `fire_whip_arc`, `fireball_core` | `flame`, `ember`, `ribbon`, `smoke`, `burst`, `ring`, `whip`, `orb` |
  | Water `water_splash_crown`, `spray_mist`, `water_droplet_cluster`, `water_trail_streak`, `foam_ring_ripple`, `water_orb`, `water_ribbon_whip`, `water_wave_crest` | `splash`, `mist`, `droplet`, `ribbon`, `ring`, `orb`, `whip`, `wave` |
  | Earth `dust_puff_cloud` (job unfinished), `rock_chunk`, `ground_crack_decal`, `rock_shards_cluster`, `pebble_debris_spray`, `earth_pillar_top`, `sand_dust_swirl` | `dust`, `rock`, `crack`, `debris`, `gravel`, `pillar_top`, `swirl` (`stone_fist` unused) |
  | Air: none generated; Water `shard` and all Common slots: none | fallbacks |

### Package D: tutorial + sandbox builder + docs / F1 overlay
**Contract that must exist first:** `Scripts/Core/Tutorial/` folder, `PlayerController.GameplayEvent`, `SandboxDirector` overloads,
`ElementLoadoutAsset.cs`, `ElementVfxBootstrap.cs` / `ElementVfxLibraryAsset.cs` shells, `EnemyTuning.CreateTutorialPartner`,
`EnemyBrain.Passive`, `PlayerTuning`/`ElementMoveSet` `DataVersion`, `HudGlyphs.cs`, the element factories.
**Owns:**
- `Scripts/Core/Tutorial/**` (pure C#: `TutorialGoal`, `TutorialStepData`, `TutorialTracker`, `TutorialSnapshot`, `TutorialScript.CreateDefault()`).
- `Scripts/Sandbox/**` (`SandboxDirector.cs` loadouts, F5/F6 swap whole loadouts, `SetEnemiesActive`; new `TutorialDirector.cs`,
  `TutorialScriptAsset.cs`).
- `Scripts/Input/PlayerInputReader.cs` (View / F7 / F8 reading; no binding changes), `Scripts/Greybox/ArenaLayout.cs`
  (`TutorialPartner`, `TutorialStart` spawns, 6 m in front of Dummy1).
- `Scripts/UI/HudControlsOverlay.cs` (same `Draw` signature), new `Scripts/UI/HudTutorialPanel.cs` (drawn from `TutorialDirector.OnGUI`).
- `Editor/Sandbox/**` (`FireSandboxBuilder.cs`, `SandboxTuningAssets.cs`, `ArenaBuilder.cs`, `ArenaGeometry.cs`, `GreyboxMaterials.cs`).
- Tests: new `Tests/EditMode/TutorialTrackerTests.cs`.
- Docs: `docs/Prototype/README.md`, `docs/Prototype/Fire-Combat-Prototype-Spec.md` (controls + Build 05 pointer),
  `docs/David's Plans/10-Combat-and-Martial-Arts.md` (decision log). This spec stays architect-owned.
**Reads:** everything.
**Work:**
- **Builder.** Keep class `FireSandboxBuilder` and scene path (no .meta churn); relabel menus: `Vaatu's Revenge/Build Combat Sandbox` (1),
  `Open Combat Sandbox` (2), **new** `Play Combat Tutorial` (3: build if missing, open, `PlayerPrefs.SetInt("VR.TutorialAutoStart", 1)`,
  enter Play; `TutorialDirector.Start` reads and clears it), `Reset Sandbox Tuning To Defaults` (20), `Refresh VFX Textures` (21).
  Assets: existing `Player_*`, `FireMoves_*` + new `WaterMoves_*`, `EarthMoves_*`, `AirMoves_*`, `Loadout_Fluid/Punishing`,
  `Enemy_TutorialPartner`, `Tutorial`, `Art/VFX/ElementVfxLibrary.asset` (re-link textures by filename on every build, keep edited
  colours), `Materials/VfxAdditive.mat`, `VfxAlphaBlend.mat` (URP Particles/Unlit). Re-link null loadout slots to their default assets
  (references, never values). Adds `ElementVfxBootstrap` and `TutorialDirector` to Systems; partner spawned inactive, registered with
  the director, not the encounter.
- **Stale detection** keeps today's checks plus: any `MoveSetAsset` with `DataVersion < ElementMoveSet.CurrentDataVersion`;
  `PlayerTuningAsset` with `DataVersion < PlayerTuning.CurrentDataVersion`; a move set whose `Element` doesn't match its file name.
  Missing assets are created silently. **"Update (recommended)" resets only the stale assets** (new
  `ResetAssetsToDefaults(IReadOnlyList<string> names, …)`) and lists them in the dialog; `BuildWithoutPrompts` logs the same.
- **Tutorial** (forces Fluid while running and restores the preset after; hides soldiers and crossbowmen; respawns the player at
  `TutorialStart`; enables the passive partner; turns on the feet beat ring; first-Play toast "Press View for the combat tutorial").
  Panel top-centre: "3 / 11", title, glyph prompt, progress pips, footer "View: skip · hold View: quit"; on success green flash, chime,
  1.0 s pause. Steps (Xbox prompts, all text in `TutorialScript.CreateDefault()` data):

  | # | Title: prompt | Success (from `GameplayEvent` + model snapshot) | Count |
  |---|---|---|---|
  | 1 | Move & look: "{LS} move, {RS} camera" | Timed 4 s | — |
  | 2 | On the beat: "{X} ×5, press as the ring touches the circle" | `ComboHit` with `IsFinisher`, Branch Main, in a string with ≥ 3 `BeatJudged` OnBeat | 1 |
  | 3 | Pause finisher: "{X} {X} … wait … {X} {X}" (the circle glows blue while the pause band is open) | `ComboHit` Branch Pause, IsFinisher | 2 |
  | 4 | Dodge keeps the combo: "mid-combo, {B} when the mark flashes, keep pressing {X}" (dummy swings) | `DodgeStarted` while ComboCount ≥ 2, then `ComboHit` with higher Count, same combo | 2 |
  | 5 | Slip in: "from range, stick toward the target + {B}, then {X}" | `DodgeStarted` SlipIn starting ≥ 4 m away, `ComboHit` within 0.6 s | 2 |
  | 6 | Dodge strike: "{X} late in a dodge" | `ComboHit` Branch DodgeStrike | 2 |
  | 7 | Launch & juggle: "hold {X}, then {X}{X}{X} in the air" (partner) | Launcher `ComboHit`, then air-chain last index `ComboHit` with InAir | 1 |
  | 8 / 8b / 8c | Switch mid-combo: "{X}{X}, {RB}+{X} (Water), keep going"; then {RB}+{A} Earth; {RB}+{Y} Air | `ElementSwitched` IsSwitchStrike to that element, then a `ComboHit` in it, same combo | 1 each |
  | 9 | MIX finisher: "finish a string after mixing two elements" | `MixFinisher` with Count ≥ 2 | 1 |
  | 10 | Danger sense: "gold = {LB} parry, red = {B} dodge, press when it turns white" (partner attacks) | `Deflected` or `PerfectDodge` within 0.35 s after a `DangerNow` | 2 |
  | 11 | Graduation: "20-hit combo using 3 elements" | ComboCount ≥ 20 with MixLevel ≥ 3 | 1 |

- **F1 overlay**: F1 cycles closed → page 1 "Rhythm & Mixing" (glyph strings: `X·X·X·X·X` press as the ring touches; `X·X·(wait)·X·X`;
  hold X → launcher → `X·X·X` in the air; B toward = slip in, B away = evade, combo keeps going; X late in a dodge = dodge strike;
  RB + B/X/A/Y mid-combo = switch strike, MIX bonus; one line per element: finisher name from data + martial-art hint; danger mark gold =
  parry, red = dodge) → page 2 (controls table, gameplay rows from `PlayerInputReader` incl. switch strike and tutorial; sandbox rows +
  F7/View tutorial, F8 skip) → closed. Rows built once.
- **Docs**: README "Play it" uses the new menu names and the Update flow; controls table matches §1; decision log entry for Build 05.

---

## 6. Acceptance criteria (what the judge checks)

### 6.0 Every package
- `tools/compile-check.sh` **5/5 PASS** (baseline 5/5). Core has no `UnityEngine` and compiles as C# 9.
- `tools/run-core-tests.sh` **all green**, count ≥ 279 + the package's new tests (baseline 279/279). No existing test deleted;
  the three tests in §2.1 are updated with a comment saying why.
- Only owned files changed (`git diff --name-only` against the base ⊆ ownership list). New files have `.meta` from `gen-unity-meta.py`.
- `dotnet run --project tools/CombatSim -- anim --out $TMPDIR/anim.json` prints **"Animation keys shown: 111 / 111"**.
- No new static without a `SubsystemRegistration` reset. No literal gameplay numbers in rule code (grep review of new rule files).

### 6.1 Package A
Tests (names are requirements; each asserts the behaviour in its name):
- `CombatStringMemoryTests`: DodgeMidStringKeepsChainIndex (X X B X → 3rd move `ChainIndex` 2, Branch DodgeStrike when targeted, Main
  otherwise), DodgeDuringStartupRetriesSameHit, ZipStrikeKeepsStringAndDoesNotConsumeSlot, AbilityAndSkillKeepString,
  StringMemoryExpiresAfterStringMemoryAfterAction, CleanHitTakenClearsString, StaggerClearsString, ApplyTuningClearsString.
- `CombatRhythmTests`: OnBeatPressRaisesNextPlaybackRateAndDamage, PoiseNotScaledByOnBeat, EarlyPressGradesEarlyAndSlowsNext,
  SecondPressBeforeNextMoveDowngradesToMashed, LatePressNeutral, MashSurchargeChargesExtraStamina, PressDuringHitstopCountsAtFrozenClock,
  BeatWindowScalesWithPlaybackRate, JudgedPressNotExpiredBeforeScaledCancel (Phoenix Palm), OneJudgementPerMoveAndEventOrder
  (ComboBeatOpened → BeatJudged → AttackStarted), PunishingWindowsNarrower, PerfectStringFinisherBonusAndLaunch,
  SingleElementOnBeatStringNeverStaggersFreshSoldier (all four elements, incl. dodge-strike substitution), AllStringMovesPassBeatDataValidation
  (both presets, all four elements), RhythmDisabledRestoresOldChainTiming, ElementRhythmDeltasApply (Earth armour, Water refund, Air
  DodgeKeepsBeat, Fire Momentum).
- `CombatPauseBranchTests`: XXPauseXEntersPauseChain, PauseOnlyAfterPauseAfterIndex, NoPauseAfterFinisher, PressAfterPauseBandRestarts,
  DodgeBetweenTurnsPauseIntoContinue, MashedPressesNeverTriggerPause.
- `CombatDodgeFlowTests`: DodgeKeepsFacingEngagedEnemy, DodgeWithNoEnemyFacesDash, FocusTrackedDuringDodge, SlipInStopsAtSlipInStopGap,
  EvadeOutFullDistance, SideSlipDistance, NeutralWithThreatAutoEvadesPerpendicularAwayFromGroup, NeutralWithoutThreatBacksteps,
  PunishingNeutralAlwaysBacksteps, DodgeStrikeTakesNextSlotAndStringContinues, DodgeStrikeBeforeFinisherPlaysFinisher,
  DodgeStrikeInsideCounterWindowIsCounterAndAutoBeat, DodgeStrikeNeedsTarget, DodgeChainMaxThenCooldown, FluidDodgeFreeAndAllowedAtZeroStamina,
  PunishingDodgeCostsAndNeedsStamina, DodgeExitCarriesRunSpeed, AirDashKeepsAirString, TwoAirDashesFluidZeroPunishing,
  FluidChainHitsDodgeCancelableFromStart, SnapshotDodgeProfileSurvivesMidDodgeSwitch, and the extended
  ChainedDodgesNeverGiveContinuousInvulnerability (≤ 60 % over 3 s at 30, 60, 144 fps, every element).
- `ElementSwitchTests`: PlainSwitchWhenFreeIsInstant, SwitchStrikeSwitchesAtCancelPointNotAtPress, ChainIndexCarriesToNewElement,
  ShorterChainClampsToFinisher, AfterFinisherSwitchStartsAtZero, PauseBranchSwitchContinuesPauseChain, AirSwitchSharesAirAttackCap,
  CooldownDeniedStrikeStillContinuesStringInOldElement, NotLearnedDeniedWithEvent, SameElementDeniedWithoutToast,
  BusyWhileChargingBufferedThenApplied, BufferedSwitchClearedByStagger, GuardDroppedWhenSwitchingToParryOnly,
  RunningActionFinishesWithStartElement, FireMomentumSurvivesSwitchAwayAndDecaysNormally, InFlightProjectileKeepsElementMultiplier,
  OldSingleSetConstructorStillWorks.
- `ElementMixTests`: MixCountsOnlyElementsThatLanded, MixCapsAtDistinctCount (F/W/F/W = 2), MixMultiplierApplied, MixFinisherTiers
  (2 damage, 3 launch, 4 poise break respecting stagger immunity), MixClearedOnComboEnd, SwitchStrikeBonusesApplyToOneHitOnly, MixNeverScalesPoise.
- `ComboCounterTests`: CountsCleanHitsOncePerAttackId, MultiTargetHitCountsOnce, MultiHitMoveCountsEachSubHit, ProjectileCountsWhenItLands,
  ResetsOnDamagedWithReason, ResetsOnGuardBroken, NotOnEvadeDeflectBlock, TimesOut, PerfectDodgeAndDeflectRefreshWithoutIncrement,
  RespawnAndPresetSwapEndCombo.
- `DangerSenseTests`: WarningFiresWarningLeadBeforeImpact (±1 frame at 30/60/144 fps), NowFiresNowLeadBeforeImpact, ShortTelegraphWarnsImmediately,
  InterruptedAttackEmitsCleared, MultiHitAttackWarnsEachHit, BoltImpactRefreshedOnLaunch, MustDodgeForUnparryableOrUnblockable,
  HiddenAttackNoWarning, PunishingHasNoNowCue, DelayedThrustWarningTracksStrikeNotWindup, EveryWarningResolvedByImpactOrCleared (soak),
  MostImminentThreatOrdering. `EnemyDangerRelayTests`: Dao Soldier, Crossbowman, break-out shove.
- `MultiHitMoveTests`: SubHitsGoLiveAtInterval, SubHitsShareMoveInstanceId, BreakOutCountsPerMoveInstance, PullStopsShort, OrbitEndsAtDegrees,
  HealOnHitCappedPerMove. `PunishingFactoryUnchangedForExistingFields` (Fire).

CombatSim (40 seeds unless stated; `-c Release`):

| Scenario / measure | Fluid | Punishing |
|---|---|---|
| `rhythm`: `rhythm` bot on-beat | ≥ 85 % | ≥ 75 % |
| `rhythm`: `sloppy` on-beat | 50-80 % | ≥ 40 % |
| `rhythm`: `masher` on-beat / accidental pauses per minute | ≤ 15 % / ≤ 0.5 | ≤ 15 % / ≤ 0.5 |
| `rhythm`: `reactpress` graded Mashed | ≤ 10 % | ≤ 10 % |
| `rhythm`: string DPS `rhythm` ÷ `masher` (dummy) | ≥ 1.30 | ≥ 1.25 |
| `duels` vs 1 soldier: `rhythm` win rate | ≥ 95 % | ≥ 85 % |
| `duels` vs 1 soldier: `masher` win rate (baseline 98 % / 95 %) | ≤ 80 % | ≤ 65 % |
| `duels` vs 1 soldier: `rhythm` − `masher` | ≥ 15 points | ≥ 20 points |
| `duels`: `react`, `anticipate`, `guard`, `fajin` (all groups) | not below report 04 minus 5 points | same |
| `dodgeflow`: facing error to target during dodge, P95 | ≤ 30° | ≤ 30° |
| `dodgeflow`: slip-in end gap | radii + [0.5, 0.7] m | same |
| `dodgeflow`: invulnerable share at max spam, every element | ≤ 60 % | ≤ 60 % |
| `dodgeflow`: time to run speed after a dodge with stick held | ≤ 0.05 s | — |
| `dodgeflow`: string continues across dodge, zip, ability; dodge strike lands after a 4 m evade-out | 100 % | 100 % |
| `switch`: cooldown denials that drop the string / `airAttacksUsed` over cap | 0 / 0 | 0 / 0 |
| `switch`: `switcher` reaches MIX 4 finisher in ≥ | 80 % of seeds | 50 % |
| `danger`: \|warning − (impact − lead)\| | ≤ 1 frame | ≤ 1 frame |
| `danger`: warnings resolved by impact or cleared | 100 % | 100 % |
| `danger`: `sense` perfect-dodge rate vs `react` | ≥ react + 20 points, damage taken ≤ react's | — |
| `danger`: `sense` panic-dodges on Delayed Thrust | ≤ 10 % | — |
| `fuzz --quick` violations | 0 | 0 |

`fuzz` invariants added: `ActiveElement` learned; `actionSet` non-null in any action; `ComboCount ≥ 0` and 0 after `ComboEnded`;
playback rate in `[Min, Max]`; every `DangerWarning` followed by impact or `DangerCleared`; no i-frames inside `ChainIFrameGap`;
`stringNext` < active chain length. If the masher target fails, tune in this order and record it in the PR: `OffBeatPlaybackRate`
0.85 → 0.80, `MashStaminaSurcharge` 4 → 6, `BeatEarly` 0.06 → 0.05 (via `--set player.Rhythm.X=` first, then the factory).

### 6.2 Package B
- `ElementMoveSetDataTests`: for Water, Earth, Air in both presets: light string poise + hit 1 < 52; pause path poise + hit 1 < 52;
  dodge-strike substitution rule; `ChainCancelAt ≥ ActiveEnd`; `ComboWindowStart ≤ ChainCancelAt`; `Active ≥ (HitCount−1)×HitInterval`;
  only §4.5 EffectKeys used; every `AnimationKey` non-empty and a defined const; `Element`, `AnimationStyle` correct;
  `DataVersion == CurrentDataVersion`; Punishing = `ApplyPunishing(Fluid)`.
- `CombatSim elements`: every element's string dps at 1.0× in **22-32** (Fire reference 28.6); beat validation passes; values match §3
  (spot-check of 10 random rows by the judge).
- `AnimationTests` green over all four sets; `ElementPoseTests`: each style's idle and every new move reach full extension on Active 0,
  no limb stretch beyond the skeleton tolerance, and a style switch mid-blend never pops (max joint delta per frame under the existing
  blend test's limit). `.Placeholders.cs` holds no entries.
- `render_fight.py … --scene chain --count 12` contact sheet for each element shows distinct stances and strikes (judge views it).
- Feel (playtesters): Water feels slower and floatier than Fire; Earth feels planted and trades hits; Air keeps moving round the target;
  each element's idle is recognisable without the HUD.

### 6.3 Package C
- Compile 5/5; no edits outside ownership; `ElementVfx` Fire path produces the same pieces as before (diff of `FireVfx` calls in a
  scripted Fire string: identical).
- In Unity with **no PNGs present**: all 13 EffectKeys render for all four elements with no errors and exactly one log message at bootstrap
  listing the missing slots. With a test PNG dropped into a slot, Refresh VFX Textures picks it up and it renders (black-background PNG reads
  additive).
- Profiler, 30 s fight with switching: **0 B GC alloc per frame** in `CombatHud`, `PlayerFeedback` and `ElementVfx` after warm-up.
- Beat ring reaches its inner radius on the frame of `BeatTime` (± 1 frame, checked in slow motion F2) and freezes during hitstop.
- Danger mark appears `WarningLead` before impact, gold vs red matches `MustDodge`, turns white on `DangerNow`; off-screen attackers show
  an edge arrow.
- Element switch: wheel flash, switch VFX and stance change happen on the `ElementSwitched` frame; no flourish mid-combo.
- Projectiles use the element's visual; launch / knockdown effects use the player's element.

### 6.4 Package D
- `TutorialTrackerTests`: synthetic events per goal (pass, fail, combo reset, skip) **and** one integration test per step (1-11 incl.
  8b/8c) that drives a real `PlayerCombatModel` with scripted `PlayerInputFrame`s on default Fluid tuning and completes it.
- In Unity on David's PC: **Build Combat Sandbox ▸ Update (recommended) ▸ Play** works from a pre-Build-05 sandbox; the dialog lists only
  the stale asset names; new Water/Earth/Air assets are created silently; an edited, current asset is not reset.
- **Play Combat Tutorial** goes from menu to step 1 in one click; View / F7 start, tap View / F8 skip, hold View 1 s quits and restores
  enemies and preset.
- F1 shows both pages with glyphs; rows match §1 exactly.
- README, Fire spec controls and the decision log updated; no stale menu names left (`grep "Build Fire Combat Sandbox" docs` → only
  history entries).

### 6.5 Feel checklist for David and Jeremy (Fluid, gamepad)
1. X X, B away, X: the next hit is hit 3 (the hit counter reads 3 as the dodge strike lands), and the dodge strike dashes back in.
2. Dodge in the middle of a hit's wind-up, then X: the same hit comes out again (not skipped).
3. X X RB+X X X: two Fire hits, three Water hits ending on Water's finisher; the combo count never resets.
4. Pressing X as the gold ring touches the circle makes the string visibly faster than mashing; mashing still finishes the string.
5. X X (wait for the blue circle) X X gives the pause finisher every time, in every element, and never by accident while mashing.
6. Dodging next to an enemy never turns your back to it; with no enemy nearby the dodge goes where you push.
7. Three quick dodges then a short lockout; you never feel stuck after a dodge (run speed carries on).
8. The danger mark's white flash is the right moment: dodging on it gives a perfect dodge most of the time.
9. A three-element combo finisher launches the enemy; a four-element one launches it and tops up Fire's Momentum (a foe too heavy to launch
   staggers instead).
10. A first-time player finishes the tutorial in under 15 minutes without reading anything outside the game.

---

## 7. Canon check (Wan → before Szeto / Yangchen)

| Item | Verdict |
|---|---|
| Water: whips, waves, wave riding, spouts, ice darts, healing | OK, basic waterbending; ice and healing are long-established. Heal-on-hit is a game abstraction of canon healing: describe it as "Water restores you", never as draining enemies |
| Water counter named "Return the Tide"; Tai Chi yielding | OK. Never use the word "redirect" in UI, names or code identifiers (lightning redirection is Iroh's, much later) |
| Earth: columns, spikes, earth wave, surfing, boulders, tent / dome | OK, basic earthbending. **Decision (verify round 4):** a thrown boulder (Boulder Toss, Boulder Hurl) is drawn up out of the ground under its launch point over the throw's wind-up and thrown from there; it is never conjured in the air or at chest height from nothing, and in the air Earth throws nothing but dust (§8.4 item 5) |
| Water's source | **Open for David:** Water bends water and ice from nothing (even mid-air) and the Avatar carries no water skin. Record one: a water skin on the sash, water in every arena, or an accepted game abstraction |
| Earth air string | Canon earthbenders can't bend airborne without rock in hand: Earth's air hits are **pure Hung Gar strikes with dust only**; Meteor Drop spikes appear on landing. FLAG for David (alternative: chunks torn off by Rising Pillar) |
| Earth hyper armour | OK as stance / rooting, **not** earth armour (no rock on the body). Earth gloves (Dai Li), lavabending, sandbending, seismic sense: not used |
| Air: blasts, swipes, shield, vortex, gust leaps, soft landing | OK, ancient (taught by sky bison) |
| Air Blade | Low risk (seen in the original series, not a Zaheer invention); FLAG for David |
| Air: no scooter, no flight, no suffocation | Respected |
| Fire: pause chain kicks, dodge strike | OK, martial strikes with fire; no lightning, no combustion, no jet flight (Flame Step stays a dash) |
| Danger Sense | A **UI convention**, not a canon Avatar power and not seismic sense. Display name in data ("Danger Sense"); "Spirit Sense" offered to David as an alternative |
| Element switching mid-fight, MIX | OK: canon Avatars mix elements in combat. MIX is not the Avatar State (that stays a future staged unlock) |
| Metalbending, bloodbending | Not in this build (player-only lost techniques, later) |
| Past lives, Vaatu, technology | Untouched. Tutorial partner is a generic "Sparring Partner"; no electrified or gas gear in effects |
| Martial-art move names (Tai Chi, Hung Gar, Bagua postures) | Real-world arts, not IP; kept in `DisplayName` data only |

---

## 8. Decision log and open questions

Conflicts resolved:
| Topic | Options from scouts | Decision and why |
|---|---|---|
| Rhythm window | narrow per element 0.02/0.04 around Active vs preset 0.06/0.10 at ActiveStart | preset 0.06/0.10 + small element deltas: simulated (rhythm 98 %, masher 11 % on-beat), forgiving for a first-time player |
| "Faster" mechanism | startup multiplier vs whole-move playback rate | playback rate: one number, animation and VFX follow it, no frame data edits |
| Pause eligibility | after any non-finisher vs after hit 2 | after hit 2 (`PauseAfterIndex` data): one rule to teach |
| Fire pause chain | Butterfly Kick + Dragon's Breath (stream, new mechanics) vs sweep + Rising Phoenix Kick | sweep + Rising Phoenix Kick: knock-up David likes, no deferred mechanics |
| Multi-element bonus name | "Flow" vs "MIX" | MIX: Flow is reserved for a future Water meter |
| Danger sense home | Unity-side `DangerSense` class vs core relay + model | core: testable headless and in CombatSim; screen angle stays Unity-side |
| Danger lead times | 0.45/0.25 vs 0.60/0.30 | 0.60/0.30 (Now lands in the perfect window at human reaction time) |
| Event set | `ComboHit`/`BeatWindowOpened` vs `BeatHit`+`ComboCountChanged` | one `ComboHit` carrying grade, element and count; `ComboBeatOpened` kept |
| Element-specific mechanics | 14 new mechanics | 4 now (multi-hit, pull, orbit, heal on hit), the rest deferred (§2.8) |
| Masher target | "≤ 70 % / ≤ 40 %" was based on a misread baseline | real baseline 98 % / 95 % (report 04): targets ≤ 80 % / ≤ 65 % plus a rhythm-vs-masher gap |
| HUD ownership | D vs C | C (presentation); D keeps the F1 overlay and the tutorial panel; shared glyphs frozen in A |

Open questions (flag, don't block):
1. **David:** Danger Sense name ("Danger Sense" or "Spirit Sense"); Earth's air string presentation (§7); colour-matched element layout
   (Y Air yellow, B Fire red, A Earth green, X Water blue) instead of today's Y Fire, B Water, A Earth, X Air; launchers on the pause
   finisher and the MIX 3 finisher.
2. **Jeremy:** free Fluid dodges (stamina pressure now comes from attacks and mashing); MIX 4 guard break vs future bosses; masher
   win-rate targets.
3. **Art:** allow `d8j0ntlcm91z4.cloudfront.net` in the environment's network policy, or download the 24 images by hand and rename
   per §5 C; then generate the Air set (62 credits left of the 110 cap).

### 8.1 Open questions resolved (1 Oct; David delegated technical and design calls to Claude)
These override anything above that conflicts.
1. **Danger Sense** keeps the name "Danger Sense" (display name lives in data; can be renamed later with no code change).
2. **Earth's air string**: pure Hung Gar strikes with dust only, spikes on landing (the canon-safe option in §7).
3. **Element layout: colour-matched to the Xbox pad.** Hold RB + **B (red) = Fire**, **X (blue) = Water**, **A (green) = Earth**,
   **Y (yellow) = Air**. Reason: David asked for combos that are easier to understand; the button colour now tells you the element.
   Keyboard 1-4 stays Fire, Water, Earth, Air. Update §1's control table, the F1 overlay, the tutorial prompts and the HUD to match.
4. **Launchers** stay on the pause finisher and the MIX 3 finisher.
5. **Jeremy's items** (free Fluid dodges, MIX 4 guard break, masher targets) ship as specified and are listed for his review in the
   playtest report.
6. **Art**: the effect images are delivered to David as one download to unzip into the project; the code must work with or without
   them (procedural stand-ins), exactly as §5 C says.

### 8.2 Build 05 verify round 1 (1 Oct): fixes to the feel and the teaching
The judge played the build headless against David's brief and found the Spider-Man 2 feel breaking in exactly the moments the
brief is about. These calls were made to fix it (Jeremy and David: please try them and say if any should go back):
1. **Pause finisher is X X (wait) X X.** Every pause chain has two moves, so the old "X X (wait) X" could never finish it. Taught
   with a cue instead of a time: the HUD's beat circle **glows blue while the pause band is open** (`RhythmView.PauseReady`), since
   the band opens 0.47-0.72 s after the 2nd press depending on the element. The tutorial, F1, How-To-Play and §1 say so.
2. **The beat is taught as anticipation.** "Press as the gold ring touches the circle, so X lands *with* the hit"; the flash and
   chime only confirm it (a press made in reaction to them grades Late 93 % of the time). The closing ring is now gold, and the
   ring at the player's feet is on by default (CombatHud). **Open for Jeremy:** Fluid `BeatLate` 0.10 → 0.15 would forgive
   reaction presses; not changed.
3. **Element switch cooldown 0.30 → 0.20 s (Fluid), 0.60 → 0.45 s (Punishing).** "One X between two switches" now holds in every
   element order on Fluid (Air's quick hit in the middle used to be refused); Punishing needs two X. A refused switch, and an RB pick
   of the element already in hand, **shake the element wheel** (cooldown: its dots flash amber); a same-element pick mid-string now
   carries the string on (it used to be swallowed).
4. **Arriving lunges.** A stretched (gap-closing) lunge, including the dodge strike dashing back in after an evade-out, and any own
   lunge longer than 1.5 m (sprint attacks) now **arrive as the strike goes active**, like the zip strike, so the hit lands with the
   limb on the target instead of from 2-2.7 m away followed by a glide in. The dash is capped at 20 m/s by stretching the startup
   (up to +0.15 s; `PlayerTuning.ArriveLungeMaxSpeed`, `ArriveLungeMaxExtraStartup`, `ArriveLungeMinDistance`); the beat and every
   later mark move with it. Dodge strikes' Range trimmed to 2.0 m (string reach).
5. **Dodges never stop dead.** A dodge's end speed carries on and locomotion brakes it (or it flows into the run with the stick
   held); an air dash flows into the jump's drift. Water and Air `DashEaseOut` 0.30/0.40 → 0.65 so their exits are ~6 m/s
   instead of 11-12 m/s. The animator's leap lift eases out over the hand-back, chained dodges restart their clip, and the legs
   stop within 0.1 s when the body does.
6. **Side-slip circles the enemy** at the distance you started (never closer than a slip-in stops), at most
   `DodgeProfile.SideSlipMaxDegrees` (100°) round per slip, instead of a straight tangent that left you 3-4 m away.
7. **Earth in the air throws no rock** (§8.1 item 2 enforced in the VFX): air strikes draw dust only, the limb sheds dust, not
   gravel. "Stone Fist" renamed **Horse Stance Punch** (§7).
8. **MIX 4** wording matches the rules: the finisher launches and tops up Fire's Momentum; the guard break shows on a foe too heavy to
   launch (Jeremy's call whether MIX 4 should break guard instead of launching, open question 2).


### 8.3 Build 05 verify round 2 (1 Oct): the controller, the teaching text and the canon
1. *(The grace and `RetractPress` below were removed in §8.6 item 1; the same-frame chord and the skill guard stay.)* **RB + a face button never fires the ranged skill.** A human "simultaneous" chord often reaches the game in one frame; the
   reader used to start a new RB hold *after* swallowing the face button, so letting go of RB fired the Fire Blast and replaced
   the queued switch strike. The chord logic now lives in pure C# (`Core/Contracts/PadChordReader.cs`, tested frame by frame) and a
   face button pressed up to `elementChordGrace` (0.05 s, 3 frames) **before** RB is upgraded into the element pick: its own action
   is taken back if it's still waiting (`PlayerInputFrame.RetractPress`; a queued X becomes the switch strike in place, keeping its
   beat grade). An action that had already started can't be taken back. A queued switch strike can no longer be replaced by any
   other attack press (a dodge still can).
2. **Lunges keep the rear foot planted.** The lunge anchor pulls a planted foot back by at most one stride (0.45 m), fades out while
   the leap lifts the body, and a foot it was holding plants where it was drawn and steps in. The `anim` scenario now fails if a
   grounded foot trails behind at hip height for more than 3 frames or drops more than 0.3 m in one frame.
3. **Earth off the ground is dust on every path** (`Core/Combat/ElementFxRules.cs`): hit sparks from the air string, the air dash,
   the zip dash, a perfect dodge and a switch to Earth in the air. **Lore call for David:** on the ground Earth's limb aura is now
   dust too (no gravel falling from a stone fist) and the switch flourish's rock rises from the floor ring, not out of the chest
   (§7: stone comes from the ground). Say if you want the gravel back.
4. **Tutorial hints wrap** inside the panel (and the F1 rows were shortened); the step 2 hint is shorter. Every default hint must
   stay under `TutorialScript.HintBudget` (140 characters). `TutorialScript.CurrentDataVersion` is 3: the sandbox builder offers to
   update an older saved tutorial.
5. **A Late beat press shows an amber "LATE"** under the beat circle (Early and Mashed stay grey).
6. **How-To-Play:** the Punishing three-element recipe is spread over two strings (a Punishing string only fits two elements before
   its finisher); "the hit turns into the launcher" now reads "that hit is followed by the launcher"; mashing is "a little slower and
   weaker, and burns stamina"; MIX 4 "tops up Fire's Momentum"; the HUD reads "MIX 2/3/4" (elements landed, not a multiplier).
7. Smaller: arms ease back to guard over ~0.15 s when a run stops (legs still stop at once); Air's circling dodge strike now
   counts its arc when capping the dash speed (25 → 21 m/s; the last bit is the 0.15 s startup-stretch cap); the Momentum bar shows only for an element
   with Momentum; the element's name shows once (under the wheel); gamepad players see pad hints for the overlay, and an overlay
   opened with Y while paused closes on resume.

Still open:
- **David:** "Air Blade" is a player-facing name (LB + Y in Air); §7 flagged it as low risk but it was never confirmed. Keep, or
  rename (DisplayName only, no code change)? Also: the grey-box FaceBand reads as a blindfold on the painted Avatar.
- **David and Jeremy:** gold means both "press X now" (the beat ring) and "parry with LB" (the danger mark). Give the beat its own
  colour, or the parry mark another one?
- **Jeremy:** the Punishing pause band is about one reaction long (Air 0.25 s, Water 0.28 s); raise Punishing `PauseGrace` to
  ~0.35 s? Fluid `BeatLate` 0.10 → 0.15 is still open from round 1. And on Punishing an X pressed in the first ~0.3 s of a dodge
  expires (0.2 s buffer, the dodge can't be cut before 0.48 s; `CombatTimingTests` pins this as Punishing's rule): keep it, or let
  an attack press wait for the dodge's cancel point? How-To-Play now tells Punishing players to press X near the dodge's end.

### 8.4 Build 05 verify round 3 (1 Oct): a cue you can anticipate, Earth's stone from the ground, motion that reads
The judge re-derived every animation and effect claim from its own trace and found eight Majors. Calls made (Jeremy and David:
please try them and say if any should go back):
1. **The beat ring is predictive** (`RhythmView.Cue*` / `NextCue*`, `HudBeatPulse`). While a hit winds up, the ring for the hit
   after it already closes, fainter and further out, assuming you keep the beat; once you press, it is the next hit's predicted beat
   (the running move's cancel point plus the next move's startup at the rate your press earned) and keeps closing across the move
   change. Every follow-up's ring is up 0.28-0.57 s before its beat in every element and preset (`VerifyRound3Tests` asserts
   >= 0.25 s); only the first beat after the opening X is as short as the opener's wind-up (0.11-0.20 s), because nothing can show
   it before you press. The rules (Active / TimeToBeat, the windows) are unchanged. The tutorial puts you in Fire as it starts
   (`TutorialScript.StartElement`, `PlayerCombatModel.SetElementAtRest`). At the feet the ring is drawn as outlines only, so the
   gold and blue fills no longer cover your legs.
2. **Spins carry on.** A blend out of a fast spin keeps turning the way it was going when the short way round would reverse it by
   more than 90° (`AnimatorSettings.SpinCarryMinSpeed` / `SpinCarryMinReverse`). Air's Spiral Kick no longer snaps on its active
   frame and turns at an even ~35° a frame; three quarters of the turn is done by its chain cancel.
3. **Lunges run.** A strike rushing along the ground at 2.5-12 m/s (gap-closing openers, Air's circle walk) runs there in real
   steps (the walk/run cycle at its speed, starting with the rear foot pushing off) instead of hovering both feet in one frozen pose
   (`AnimatorSettings.LungeStrides`, `StrideMinSpeed`, `StrideMaxSpeed`); only faster rushes leap. Surf clips (`PoseClip.Glides`)
   keep their glide. The `anim` scenario now fails on a joint jumping > 0.8 m in a frame, a spin turning back > 60° at a clip change,
   or both feet gliding for 3+ frames.
4. **Effects are drawn at the reach that can hit.** Water/Earth/Air rings, explosions, dome rings and vortices drew at twice the hit
   radius; `Band` now takes world radii (its contract comment says so). The air string's spin vortex (Spiral Kick, Fair Lady) swirls
   round the chest up in the air, not on the floor under the juggle.
5. **Earth's stone comes from the ground, everywhere** (`ElementFxRules.StoneFromFloor`, `FloorStoneUnder`): a burst from a fist,
   a hit spark on a foe, the chest on a perfect dodge or a parry is dust at that point, and its rock rises out of the floor under it
   (only dust when the point is more than 1.6 m up, e.g. a juggled foe). The charge glow is a churning ball of dust, a launched foe
   trails dust, and an airborne switch strike into Earth accents with dust (spec 8.1 item 2). **Lore call for David:** say if you
   prefer stone visibly gathered to the fist; it would be recorded in §7 as a canon decision.
6. **Air Shield and Water's bubble** are a faint inner shell (60 % of the reach, alpha 0.25, never drawn round the camera) with the
   spinning rings showing the reach; Stone Tent's slab on the camera's side rises only to a low wall (`ElementVfxStyle`
   `DomeShell*`, `TentCamera*`).
7. Should-fix polish: RB held with the same element mid-string no longer shakes the wheel; X a frame before RB from neutral is one
   attack plus a plain switch (never a second, automatic switch strike); a switch strike that outlives its buffer (Punishing, early
   in a dodge) still switches; a slip-in from contact range draws no push or trails; slam finishers no longer lift you and you fall
   at full gravity once they strike (`AerialSettings.FinisherGravityScale`); the jump pose starts at take-off; per-element switch
   flash colours of equal brightness (`ElementVfxStyle.SwitchFlashColor`); Tiger Claw Rake is a swipe (Trail) rather than a straight
   spike line; the Avatar has eyes instead of the grey-box face band; the tutorial's slip-in step (now 3 m) shows a distance bar.
   `ElementMoveSet.CurrentDataVersion` 7 and `TutorialScript.CurrentDataVersion` 4: the sandbox builder offers to update older saved
   assets.

Still open:
- **Jeremy:** let only face presses within ~0.4 s of RB going down be element picks (later presses with RB still held would act
  normally: B dodges)? For now How-To-Play says to let go of RB after switching. A slip-in from contact range is still a sway in place
  (now without the backwards jet): turn it into a short circle round the foe? That would cost the "dash through the attack" perfect
  dodge bonus at contact range, so it's your call. Per-element dodge poses (one shared pose today) and Fluid `BeatLate` are open too.

### 8.5 Build 05 verify round 4 (1 Oct): the chord on a real pad, the dodge strike's dash, Earth's boulders
1. *(Superseded by §8.6 item 1: the hold-back and the grace are gone, chords are modifier first.)* **Thumb-first chords only switch** (J4-01, `PadChordReader.FaceChordLatency`, `PlayerInputReader.faceChordLatency`). On the
   gamepad, with RB up, a Y / B / A press is held back 0.08 s (5 frames at 60 fps, 2 at 30) in case RB follows; RB in that time makes
   it only the element pick (no jump, dodge or zip ever starts). Otherwise it is reported then, carrying its real age
   (`ButtonState.PressDelay`) so the buffer and the dodge's tap/hold timer count from the real press. A quick tap let go sooner is
   reported at once. X is never held back (its beat grade must not move): X up to 5 frames before RB is upgraded to the pick
   (`ElementChordGrace` 0.08 s), and an X that already ran stays that one hit, never a second attack, also into the element in hand
   (BH-02). A face held up to 0.15 s when RB goes down, too late for a pick, no longer lets RB's release fire the ranged skill
   (`ChordSkillGuard`, BH-03); held longer (sprinting on B), an RB tap is still the skill. The keyboard is never held back.
   **Jeremy:** the cost is 0.08 s on a pad jump / dodge / zip pressed on its own (a Punishing dodge, on release, is unaffected by a
   quick tap). Set `faceChordLatency` to 0 in the input reader to trade the chord tolerance back for zero delay. CombatSim's fuzz runs
   (30 / 144 fps, spikes) and the switch scenario's "Real pad" table now press RB and the face up to 80 ms apart through the real
   chord reader.
2. **The dodge strike's dash eases** (J4-02, `PlayerTuning.ArriveLungeRampIn` / `RampOut` / `EndSpeed` / `EntryCarry` /
   `MaxAccel`; `ArriveLungeMaxExtraStartup` 0.22). An arriving lunge starts from the way you were moving (half your speed backwards
   out of an evade, so the body plants and pushes off instead of flipping from -17.6 to +20 m/s in one frame), stays under 20 m/s,
   and slows to about 3 m/s as the strike lands (no dead stop from full speed); the startup stretch is solved for the eased profile
   so it still lands on contact. Sideways motion carries on and dies away (`ArrivalCarry`); Air's circling strike eases its turn in.
   Its legs run on strides up to 21 m/s (`AnimatorSettings.DashStrideMaxSpeed`, only for the dodge strike: `FighterAnimInput.DashIn`),
   and once the dash has stopped the feet land fast (`LeapLiftLandRate`). The element trails from the feet on the way in
   (`PlayerFeedback.DodgeStrikeDashStarted`). The `anim` scenario now fails on a statue slide (over 12 m/s for 6+ frames with the
   legs frozen) or a one-frame velocity change over 20 m/s in a strike. `PlayerTuning.CurrentDataVersion` 7.
3. **Earth's boulders come out of the ground** (J4-03): over the wind-up a block the boulder's size rises out of the floor under the
   launch point (`ElementVfx.RaiseStone`), the throw kicks grit out of the floor under it (Earth muzzle), and the boulder is drawn
   inside its hit size (`VisualScale` 0.9: 0.92 x 0.81 x 0.97 m for the Hurl, 0.77 x 0.68 x 0.81 m for the Toss), turned only about
   the vertical, tumbling end over end in flight. `ElementMoveSet.CurrentDataVersion` 8. The `anim` scenario fails an Earth throw
   that didn't rise from the floor or a boulder drawn bigger than its hit.
4. Should-fix polish: a refused fourth dodge in a row flashes the stamina bar's outline with a dull flash and a light thud
   (`DodgeChainLimited`); another element's Momentum (Fire's, topped up by a MIX 4 finisher) shows as a thin labelled bar while
   you're in Water, Earth or Air; How-To-Play's Punishing notes fixed (the pause route fits three elements in one string; the launcher
   hold is on any hit before the finisher; when to press B on Punishing; three dodges then a breath).

### 8.6 Build 05 verify round 5 (2 Oct): modifier-first chords, no added pad latency, landings on the floor
1. **Element switching is modifier first** (lead design decision 1-2 Oct on David's delegation; overrides J4-01 / S-09 and §8.5 item 1).
   Hold RB, then press the face button, exactly like Spider-Man 2's L1 + face for gadgets. RB and the face on the same frame is still
   the chord. A face button pressed **before** RB simply does its own job (X attacks, B dodges, A jumps, Y zips): accepted, documented
   behaviour. `PadChordReader` no longer holds Y / B / A back (`FaceChordLatency` and `PlayerInputReader.faceChordLatency` deleted),
   has no grace for a face pressed before RB (`ElementChordGrace`, `PlayerInputFrame.RetractPress` and `ButtonState.PressDelay` deleted),
   so every face press reaches the rules on the frame it is pressed and in the order it was made. Kept: a face held at most
   `ChordSkillGuard` (0.15 s) when RB goes down still stops RB's release firing the ranged skill, and a held RB with nothing picked
   fires nothing after `SkillTapMaxTime`. **Measured cost for Jeremy (J5-01): 0 frames.** The round-4 hold-back started every pad
   dodge up to 5 frames late; the sense bot's perfect-dodge rate on the pad fell to 19 / 21 / 13 % (one soldier / two / the full ring).
   Now `danger` runs sense vs react on the keyboard and on the pad path and both read 50 / 42 / 24 % (sense +50 / +39 / +22 points
   over react, target +20); `framedata` shows input to dodge / jump / attack / pick and the dodge's i-frames at 0 frames added on the
   pad; `dodgeflow` repeats facing, slip-in, spam and the string on the pad path (same numbers); `duels` compares pad and keyboard for
   five bots. J5-02 (a brisk B then X losing the dodge strike, A then X the air attack, B then LB becoming Flame Wheel) is gone with
   the hold-back: presses come out in order (`PadChordReaderTests`, `PadChordModelTests.BriskSequencesComeOutInOrder`).
   Tutorial prompts, hints, the F1 overlay and How-To-Play say "hold RB, then press"; `TutorialScript.CurrentDataVersion` 5 (the
   sandbox builder offers the update).
2. **A switch pressed Late in the string is still the switch strike** (J5-03). With RB first, a mid-string pick is buffered and
   beat-judged like any X: past the switch strike's widened window (0.16 s Fluid, 0.11 s Punishing) it is graded Late (no on-beat
   speed-up) but still switches with the strike's damage and poise multipliers, at the next slot; later still, the pause band takes it.
   The tutorial's switch steps also pass for a plain switch made while the combo is still going, followed by a hit in that element.
   A queued switch strike that another press replaces (a dodge or parry pressed right after the chord, a jump, a heal) still
   switches, as a plain switch, like one that outlives its buffer (J3-S03): a pick is never dropped without a trace.
3. **Air finishers and plunges land on the floor** (J5-04, `AnimatorSettings.LandingFit*`). For 0.6 s after a touchdown, whenever the
   solved feet hang more than 4 cm above the floor (an air-string finisher or plunge still in its air pose: `FinisherGravityScale`
   gets the body down before the clip expects), the legs take the grounded locomotion pose (the knee-bending landing) at once and the
   pelvis sits down onto them; the fit hands back over 0.12 s once the clip's own legs reach the floor. Clips that leave the ground on
   purpose are marked `PoseClip.Leaps` (zip kick, sprint kick, wind leap, wind runner kick) and keep their legs. The `anim` scenario
   fails on a grounded fighter with both feet over 0.2 m up for 3+ frames (Meteor Drop, Earthquake Drop, Air Burst Landing, Tornado
   Slam, Needle Drop all used to float 5-14 frames); only the touchdown frame itself remains (the model reports grounded a tick later).
4. Should-fix polish: per-element charge glow on the fists (`ElementVfxStyle.ChargeFistColor`: Fire orange, Water pale blue, Air faint
   white, Earth none; the sweet spot's white-gold is shared); the danger mark keeps its gold / red rim and arrows when its core turns
   white at "now"; the air dash eases into and out of its pose (worst limb move per frame 0.60 m to 0.36 m); the pelvis height eases
   between stance and gait (`GaitSettings.HipsHeightSmoothing`: Earth's stop no longer drops the hips 9.5 cm in a frame; `anim` fails a
   hip step over 5 cm in locomotion); a boulder at the end of its range breaks into chunks; the other element's meter is labelled from
   the move data; the F1 overlay has a pause row and a shorter MIX row; dodge counts per element and "that hit becomes the launcher" in
   How-To-Play, David's plan and §1; stale "X X, wait, X" comments fixed.

