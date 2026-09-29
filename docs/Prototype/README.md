# Fire Combat Prototype

A grey-box sandbox for judging whether firebending combat is fun. Capsules and cubes, real timing.

## Play it

1. In `C:\Users\david\Avatar Game - The Missing Bison`: `git fetch origin` then `git switch claude/wizardly-hamilton-b7ncq5`.
2. Open `game/` in Unity 6000.6.3f1 and let it compile. **Red errors in the Console?** Copy them to Claude before doing anything else.
3. Menu **Vaatu's Revenge ▸ Build Fire Combat Sandbox**, then press **Play**. Click the Game view to capture the mouse.
4. Once it works, commit the generated `Assets/_Project/Scenes`, `Tuning` and `Materials` files so Jeremy gets the same tuning.

**F1** shows the controls in-game. Gamepad is the intended way to play.

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
- Mashing light still beats a single soldier in Fluid (slower and costlier now). Fixing it needs a new enemy rule, e.g. an armoured counter after repeated hits. Jeremy's call.
- The camera can still jump ~2 m when a lock-on swing sweeps a pillar into it, about every 20-25 s in pillar fights.
- Fire Blast doesn't lead moving targets (by design for now: close the distance).
