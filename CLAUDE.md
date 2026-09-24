# Vaatu's Revenge — project context for Claude

Two-person hobby team building an Avatar: The Last Airbender–inspired souls-like action RPG. Both are **first-time game developers**, so explain the why behind game-dev practices, not just the what.

- **David**: big Avatar fan, so the natural lead on lore, story, world and characters.
- **Jeremy**: big Elden Ring fan, so the natural lead on combat, the gameplay loop, difficulty and boss design.

**IP stance (undecided):** treat this as a private fan project for now. Keep code and systems IP-agnostic (e.g. generic element/stance systems, names kept in data rather than hard-coded) so the game could be reskinned into an original world later if the goal changes.

## Game in one paragraph

The player wakes as the Avatar while the Spirit World and physical world collide. They start with no Avatar State access and unlock it in stages by levelling up, learning all four elements and progressing the story. Final boss: Vaatu. Gameplay loop modelled on Elden Ring: explore → rest at checkpoint → fight → die → recover lost currency → level up.

## Core pillar: real martial arts

Combat is built on the real martial arts behind each bending style: Tai Chi (water), Hung Gar (earth), Southern Praying Mantis (Toph), Northern Shaolin (fire), Baguazhang (air), fa jin (bursts). Each style's principle should change how the element *plays*, not just how it looks. See `docs/David's Plans/10-Combat-and-Martial-Arts.md`.

## Tools

- **Engine:** Unity 6 + URP (recommended, not yet locked in). The Unity project lives in `game/`.
- **3D:** Blender. Source files go in `art/`, exported FBX files go into `game/Assets/`.
- **Budget:** free tools preferred.

## Conventions

- Design docs live in `docs/` as Markdown. Update the relevant doc when a design decision is made.
- Reference material goes in `docs/knowledge/` with a short summary and the link.
- Binary assets use Git LFS. Remind the user to `git lfs lock` before editing `.blend`/`.fbx`/`.psd`.
- Work on branches and merge to `main` via pull requests (see CONTRIBUTING.md).
- Build order: grey-box prototype of core combat first, then a vertical slice, and only then broader content.
