# Combat Prototype

A grey-box sandbox for judging whether bending combat is fun: all four elements (Fire, Water, Earth, Air) on Marvel's Spider-Man 2 controls, rhythm combos, switching element mid-combo, jointed martial-artist fighters animated in code, a grey-box arena and real timing. It started as the Fire prototype; Build 05 ([spec](Build-05-Spec.md)) added the other three elements, the rhythm and MIX systems, the Spider-Man 2 dodge, danger sense and an in-game tutorial.

**New to it? Read [How to play](How-To-Play.md)** (one page, plain words) or just run the tutorial.

## Play it

1. In `C:\Users\david\Avatar Game - The Missing Bison`: `git fetch origin` then `git switch claude/dazzling-clarke-njm3m5` and `git pull`.
2. Open `game/` in Unity 6000.6.3f1 and let it compile. **Red errors in the Console?** Copy them to Claude before doing anything else.
3. Menu **Vaatu's Revenge ▸ Build Combat Sandbox**. If you built the sandbox before, it asks to replace the scene (yes), then lists the tuning assets an older version saved: click **Update (recommended)**. Only the listed ones are reset; new ones (Water, Earth and Air moves, the loadouts, the tutorial) are created without asking, and anything you tuned with this version is kept.
4. Press **Play**. Click the Game view to capture the mouse. Press **View** on the gamepad (or **F7**) to start the tutorial.
5. Once it works, commit the generated `Assets/_Project/Scenes`, `Tuning`, `Materials` and `Art/VFX` files so Jeremy gets the same tuning.

**Straight into the tutorial:** **Vaatu's Revenge ▸ Play Combat Tutorial** builds the sandbox if needed, opens it and starts the tutorial on the first frame of Play. Eleven short steps against a sparring partner who can't be hurt: the beat, the pause finisher, dodging without dropping the combo, slip-in, dodge strike, launcher and juggle, switching element mid-string (Water, Earth, Air), a MIX finisher, danger sense, and a 20-hit three-element graduation. Tap View (F8) to skip a step, hold View (F7) to quit.

**F1** shows the combos (page 1, "Rhythm & Mixing") and every control (page 2) in-game; press it again to turn the page, a third time to close. Gamepad is the intended way to play. The layout is **Spider-Man 2's**: X attack, Y zip strike, B dodge, A jump, tap LB parry, tap RB ranged skill, hold LB + X / Y / B abilities, D-pad down heal. **Hold RB + a face button switches element, and the button's colour is the element: B (red) Fire, X (blue) Water, A (green) Earth, Y (yellow) Air** (1-4 on the keyboard). Full table in the [spec](Fire-Combat-Prototype-Spec.md#controls).

**Effect pictures:** every effect has a built-in stand-in, so the sandbox looks complete without any. When you unzip the effect pictures into `game/Assets/_Project/Art/VFX/` (see its README for the folder and file names), run **Vaatu's Revenge ▸ Refresh VFX Textures** (the build also does it).

## Martial-arts animation packs (not in Git)

The three free Asset Store packs (SAINDEVELOPER *Martial Art Animations - Sample* and *Brawler Animations - Free*, MAGICPOT *Fighting Motions Vol.1*) can't be committed: the repo is public and the Asset Store licence doesn't allow sharing the files. Each of us imports them from our own Unity account:

1. Add the three packs to your Unity account on the Asset Store (they're free).
2. In Unity: **Window ▸ Package Manager ▸ My Assets**, then **Download** and **Import** each pack.
3. In the Project window, create `Assets/ThirdParty/` and drag the imported pack folders into it. Git ignores that folder.
4. **Vaatu's Revenge ▸ Animation ▸ Set ThirdParty Rigs To Humanoid**, then **Vaatu's Revenge ▸ Animation ▸ Write Animation Catalogue**.
5. **Vaatu's Revenge ▸ Animation ▸ Build Animation Set From ThirdParty**. The Console prints a table of which clip went to which move, with its impact time.
6. **Vaatu's Revenge ▸ Build Combat Sandbox** again, so the fighters pick up the clips.
7. Commit `docs/Prototype/Animation-Catalogue.json` and the two sets in `Assets/_Project/Animations/` (`FireFighterAnimations.asset`, `EnemyFighterAnimations.asset`). They hold clip names, timings and references only, with no animation data.

What step 5 does: wherever a pack clip matches one of our moves by name (a jab, a front kick, a hit reaction, an idle...), the fighters play that real motion-capture clip instead of the procedural animation. Moves with no match stay procedural. Each clip is sped up or slowed down around its "fully extended" frame, so the fist or foot lands exactly on the frame the hit becomes live in our frame data, and hitstop and slow motion freeze and slow it like everything else.

The builder guesses the clip names and the impact frames. Check each strike in the Animation preview. If a guess is wrong, fix it in the set asset: pick another clip, drag the impact time, or tick Mirror to swap left and right. The builder never overwrites an entry you've edited. To start over, delete the asset and run step 5 again. Without the packs nothing changes: every move stays procedural and nothing errors.

| Key | Sandbox tool |
|---|---|
| F1 / F3 | Combos and controls overlay (two pages) / debug panel (state, frame data, i-frames) |
| F2 | Slow motion (0.25x) to study moves |
| F5 / F6 | Fluid / Punishing preset, live (all four elements swap together) |
| F7 / F8 (View on the gamepad) | Tutorial: start or quit / skip a step (tap View to skip, hold it to quit) |
| F4 / T | Respawn player / reset enemies |
| Esc | Release the mouse and pause |

## Tuning while you play

Every number lives in `Assets/_Project/Tuning/` (ScriptableObject assets). Select one during Play and drag values: changes apply live. Unity keeps edits made to assets during Play. **Vaatu's Revenge ▸ Reset Sandbox Tuning To Defaults** puts everything back.

| Asset | Holds |
|---|---|
| `Player_Fluid`, `Player_Punishing` | Health, stamina, movement, the rhythm windows, combo timeout, MIX bonuses, element switching, danger sense |
| `FireMoves_*`, `WaterMoves_*`, `EarthMoves_*`, `AirMoves_*` | Each element's moves, dodge, guard and rhythm, per preset |
| `Loadout_Fluid`, `Loadout_Punishing` | Which move set each element uses and which are learned (untick one to lock it) |
| `Enemy_*` | The dao soldier, crossbowman, sparring dummy and the tutorial's sparring partner |
| `Tutorial` | Every tutorial step: its words, its button prompt and what counts as passing it |

## What to test

The human checklist is in [Playtest Report 01, section 6](Playtest-Report-01.md), with updates in [Report 02](Playtest-Report-02.md). The big questions for Jeremy:

- The [feel checklist in the Build 05 spec](Build-05-Spec.md#65-feel-checklist-for-david-and-jeremy-fluid-gamepad): ten things to try on the gamepad.
- Fluid vs Punishing (F5/F6): which dodge feel is right? (Fluid dodges are free now; stamina pressure comes from attacking and mashing.)
- Is fa jin (hold heavy, let go in the gold band) satisfying as the timing reward?
- Does the over-the-shoulder camera feel like Spider-Man? Shoulder offset 0 in `Camera.asset` gives the centred Elden Ring view.
- Decided by David (29 Sep): perfect dodge in any direction, crossbowmen share the 2 attack slots, buffered dodge beats a later attack press. Jeremy should still say if any feel wrong.

## Documents

- [How to play](How-To-Play.md): the combos in plain words, with combos to try.
- [Build 05 spec](Build-05-Spec.md): the four elements, rhythm, MIX, the Spider-Man 2 dodge, danger sense and the tutorial.
- [Build spec](Fire-Combat-Prototype-Spec.md): controls, moves, numbers, architecture, who built what (the Fire build).
- [Playtest Report 01](Playtest-Report-01.md): the independent headless playtest and code review.
- [Playtest Report 02](Playtest-Report-02.md): re-check after fixes, plus round 2 and round 3 fixes.
- [Unity 6 compile-risk audit](Unity6-Compile-Risk-Audit.md): API uses the offline check (Unity 2021.3 DLLs) can't vouch for.
- [`tools/README.md`](../../tools/README.md): offline compile check, tests and the CombatSim bot harness.

## Known open items

- Nothing has run in the real Unity Editor yet: the first run is the real test.
- Dao Soldiers answer mashing with a violet break-out counter: the 5-hit string lands whole, a 6th hit within 2 s arms the shove (soldier poise 52 so the string plus a jab doesn't stagger first; report 04, W-01). It makes Punishing vs 2 soldiers harder; the damage is tunable in `Enemy_DaoSoldier` → `BreakOut`. Jeremy's call.
- Camera pops near pillars are reduced but not gone (worst ~1.7 m, rare).
- Fire Blast doesn't lead moving targets (by design for now: close the distance).
