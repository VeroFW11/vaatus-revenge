# Fire Combat Prototype

A grey-box sandbox for judging whether firebending combat is fun: jointed martial-artist fighters animated in code, grey-box arena, real timing.

## Play it

1. In `C:\Users\david\Avatar Game - The Missing Bison`: `git fetch origin` then `git switch claude/dazzling-clarke-njm3m5` and `git pull` (the branch with the Spider-Man controls, aerial combat and animated fighters; it builds on PR #2's branch).
2. Open `game/` in Unity 6000.6.3f1 and let it compile. **Red errors in the Console?** Copy them to Claude before doing anything else.
3. Menu **Vaatu's Revenge ▸ Build Fire Combat Sandbox**, then press **Play**. Click the Game view to capture the mouse. If it says your tuning assets are from an older version, click **Update (recommended)**.
4. Once it works, commit the generated `Assets/_Project/Scenes`, `Tuning` and `Materials` files so Jeremy gets the same tuning.

**F1** shows the controls in-game. Gamepad is the intended way to play. The layout is **Spider-Man 2's** (29 Sep): X attack, Y zip strike, B dodge, A jump, tap LB parry, tap RB Fire Blast, hold LB + X fa jin, D-pad down heal. Full table in the [spec](Fire-Combat-Prototype-Spec.md#controls).

**Built the sandbox before (29 Sep or earlier)?** Your saved tuning assets hold the old moves and numbers (3-hit chain, 4 m auto-aim). The builder spots this and offers **Update (recommended)**; or run **Vaatu's Revenge ▸ Reset Sandbox Tuning To Defaults** yourself.

## Martial-arts animation packs (not in Git)

The three free Asset Store packs (SAINDEVELOPER *Martial Art Animations - Sample* and *Brawler Animations - Free*, MAGICPOT *Fighting Motions Vol.1*) can't be committed: the repo is public and the Asset Store licence doesn't allow sharing the files. Each of us imports them from our own Unity account:

1. Add the three packs to your Unity account on the Asset Store (they're free).
2. In Unity: **Window ▸ Package Manager ▸ My Assets**, then **Download** and **Import** each pack.
3. In the Project window, create `Assets/ThirdParty/` and drag the imported pack folders into it. Git ignores that folder.
4. **Vaatu's Revenge ▸ Animation ▸ Set ThirdParty Rigs To Humanoid**, then **Vaatu's Revenge ▸ Animation ▸ Write Animation Catalogue**.
5. **Vaatu's Revenge ▸ Animation ▸ Build Animation Set From ThirdParty**. The Console prints a table of which clip went to which move, with its impact time.
6. **Vaatu's Revenge ▸ Build Fire Combat Sandbox** again, so the fighters pick up the clips.
7. Commit `docs/Prototype/Animation-Catalogue.json` and the two sets in `Assets/_Project/Animations/` (`FireFighterAnimations.asset`, `EnemyFighterAnimations.asset`). They hold clip names, timings and references only, with no animation data.

What step 5 does: wherever a pack clip matches one of our moves by name (a jab, a front kick, a hit reaction, an idle...), the fighters play that real motion-capture clip instead of the procedural animation. Moves with no match stay procedural. Each clip is sped up or slowed down around its "fully extended" frame, so the fist or foot lands exactly on the frame the hit becomes live in our frame data, and hitstop and slow motion freeze and slow it like everything else.

The builder guesses the clip names and the impact frames. Check each strike in the Animation preview. If a guess is wrong, fix it in the set asset: pick another clip, drag the impact time, or tick Mirror to swap left and right. The builder never overwrites an entry you've edited. To start over, delete the asset and run step 5 again. Without the packs nothing changes: every move stays procedural and nothing errors.

| Key | Sandbox tool |
|---|---|
| F1 / F3 | Controls overlay / debug panel (state, frame data, i-frames) |
| F2 | Slow motion (0.25x) to study moves |
| F5 / F6 | Fluid / Punishing preset, live |
| F4 / T | Respawn player / reset enemies |
| Esc | Release the mouse and pause |

## Tuning while you play

Every number lives in `Assets/_Project/Tuning/` (ScriptableObject assets). Select one during Play and drag values: changes apply live. Unity keeps edits made to assets during Play. **Vaatu's Revenge ▸ Reset Sandbox Tuning To Defaults** puts everything back.

## What to test

The human checklist is in [Playtest Report 01, section 6](Playtest-Report-01.md), with updates in [Report 02](Playtest-Report-02.md). The big questions for Jeremy:

- Fluid vs Punishing (F5/F6): which dodge feel is right?
- Is fa jin (hold heavy, let go in the gold band) satisfying as the timing reward?
- Does the over-the-shoulder camera feel like Spider-Man? Shoulder offset 0 in `Camera.asset` gives the centred Elden Ring view.
- Decided by David (29 Sep): perfect dodge in any direction, crossbowmen share the 2 attack slots, buffered dodge beats a later attack press. Jeremy should still say if any feel wrong.

## Documents

- [Build spec](Fire-Combat-Prototype-Spec.md): controls, moves, numbers, architecture, who built what.
- [Playtest Report 01](Playtest-Report-01.md): the independent headless playtest and code review.
- [Playtest Report 02](Playtest-Report-02.md): re-check after fixes, plus round 2 and round 3 fixes.
- [Unity 6 compile-risk audit](Unity6-Compile-Risk-Audit.md): API uses the offline check (Unity 2021.3 DLLs) can't vouch for.
- [`tools/README.md`](../../tools/README.md): offline compile check, tests and the CombatSim bot harness.

## Known open items

- Nothing has run in the real Unity Editor yet: the first run is the real test.
- Dao Soldiers now answer mashing with a violet break-out counter (masher wins 65% vs one soldier in Fluid). It makes Punishing vs 2 soldiers harder; the damage is tunable in `Enemy_DaoSoldier` → `BreakOut`. Jeremy's call.
- Camera pops near pillars are reduced but not gone (worst ~1.7 m, rare).
- Fire Blast doesn't lead moving targets (by design for now: close the distance).
