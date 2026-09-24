# The Missing Bison — project context for Claude

Two-person hobby team building an Avatar: The Last Airbender–inspired souls-like action RPG. Both are **first-time game developers**, so explain the why behind game-dev practices, not just the what.

## Game in one paragraph

The player wakes as the Avatar while the Spirit World and physical world collide. They start with no Avatar State access and unlock it in stages by levelling up, learning all four elements and progressing the story. Final boss: Vaatu. Gameplay loop modelled on Elden Ring: explore → rest at checkpoint → fight → die → recover lost currency → level up.

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
