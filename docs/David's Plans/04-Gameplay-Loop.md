# 04 · Gameplay Loop

## My notes

What makes the Elden Ring loop work, and what we want:

- **Nothing really gets explained.**
- You don't know why you're doing anything, but you roughly know where it's heading.
- It never explains how to do it.
- **You can do it any way you like.**
- **You grind to get stronger.**
- **Find and wield higher-tier loot:**
  - Weapons and armour
  - Practical
  - Combo loot with status effects
- **Almost nothing is meaningless.**
- **The game rewards you well for exploring**, e.g. Avatar artifacts left behind by past lives.
- **Boss system:** corrupted spirits are the bosses and the enemies.
- **Campfires** make enemy aggro disappear, replenish you, and let you level up with runes.
- **Attention to detail** in the map and the fighting mechanics.

## Things to decide

- [ ] **The loop in one line.** Draft: *explore → find a campfire → fight → die → recover what you lost → level up → go further.*
- [ ] **"Nothing gets explained" vs. new players.** Elden Ring still teaches through level design (a safe first enemy, a hint message, a visible path). How do we teach without explaining?
- [ ] **Open world or connected zones?** Elden Ring is open-world with dungeons. Dark Souls is one interconnected map. Which suits us, especially while we're building the first region?
- [ ] **Travel.** On foot, by sky bison, air glider, fast-travel between campfires?
- [ ] **What rewards exploration?** List reward types: artifacts, loot, masters, lore, secret bosses, shortcuts. (Decided: masters must be found, and lore is scattered across the map. See [02 Storyline](02-Storyline.md).)

## Claude's suggestions

**The loop, with the Avatar version of each Elden Ring piece:**

```
Explore region ──► Find campfire (rest, level up, respawn point)
     ▲                     │
     │                     ▼
Stronger + better loot ◄── Fight corrupted spirits ──► Die ──► Lose runes where you fell
                                                         │
                                                         └─► Go back and pick them up (or lose them for good)
```

**Build order tip:** this loop is the first thing to prototype, with grey boxes: one campfire, three enemies, dying, and recovering runes. If that loop is fun with capsules, the game will be fun.

## Connects to

[06 Leveling System](06-Leveling-System.md) · [07 Weapons and Armour](07-Weapons-and-Armour.md) · [08 Artifacts and Loot](08-Artifacts-and-Loot.md) · [09 Boss System](09-Boss-System.md)
