# How we work together

## The daily loop

1. **Pull first:** `git pull` before you start, so you have your partner's latest work.
2. **Work on a branch**, not directly on `main`:
   `git switch -c feature/airbending-blast`
3. **Commit small and often**, with a message saying what changed:
   `git commit -m "Add airbending blast prototype"`
4. **Push your branch** and open a **Pull Request** on GitHub.
5. The other person looks it over (or plays it), then it gets merged into `main`.

`main` should always open and run in Unity without errors.

## Branch names

- `feature/...` — new mechanic or content (`feature/lock-on-camera`)
- `fix/...` — bug fix (`fix/dodge-roll-clipping`)
- `docs/...` — design doc changes (`docs/fire-nation-region`)
- `art/...` — new or updated art (`art/avatar-base-mesh`)

## Big files and locking

Binary files (`.blend`, `.fbx`, `.psd`, textures, audio) are stored with **Git LFS** and **can't be merged**. If two people edit the same one, one person's work is lost.

Before editing a `.blend`, `.fbx` or `.psd`, **lock it**:

```
git lfs lock art/characters/avatar.blend
```

When you've pushed your changes, **unlock it**:

```
git lfs unlock art/characters/avatar.blend
```

See what's locked: `git lfs locks`

The same idea applies to **Unity scenes**. Agree on who is working in which scene, or split levels into multiple scenes.

## Who owns what

_To be agreed. For example, one person leads code/gameplay and the other leads art/world. Both review each other's work._
