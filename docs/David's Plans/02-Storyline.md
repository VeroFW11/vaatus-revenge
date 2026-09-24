# 02 · Storyline

## My notes

**The core story:**
- You wake up as the Avatar and the world is in chaos between the Spirit World and the real world.
- Your mission is to defeat Vaatu.
- At the start you **can't access the Avatar State**.
- As you level up, learn moves and progress through the story, you unlock more parts of the Avatar State and grow more powerful.
- You must **learn all four elements** and **master the Avatar State** before you can fight Vaatu. He's insanely strong.
- **Vaatu is the final boss.** The fight happens at the **Tree of Time** in the Spirit World while he tries to break out, and you **reseal** him (see [01](01-Lore-and-Universe.md)).

**How the story is structured:**
- **The player chooses the order of the elements.** They choose which region to go to, and learn that element from a master there.
- **Masters have to be found.** They aren't handed to you.
- **Several masters per element to choose from.**
- **Several endings, based on the player's choices.**
- **There is direct storytelling**, but players are **rewarded for exploring and learning from the environment**.
- **Information and lore are scattered around the map.**

## Things to decide

- [ ] **Opening scene.** Where do you wake up, and what's the first thing you see?
- [ ] **How do you find a master?** Rumours from NPCs, clues in the environment, hidden locations, a test to prove yourself?
- [ ] **How many masters per element?** Two or three? What makes each one different? (See Claude's suggestions.)
- [ ] **Does your choice of master affect the ending?**
- [ ] **What are the endings?** What choices lead to each one? (Lore check: every ending must leave Vaatu sealed; see below.)
- [ ] **How many Avatar State stages are there?**
- [ ] **How do we balance difficulty when players can go anywhere?** (See Claude's suggestions.)
- [ ] **Does Vaatu appear or taunt you before the final fight?** Through visions, corrupted spirits speaking for him?
- [ ] **Types of scattered lore.** Which ones do we use? (List below.)

## Claude's suggestions

### Structure: open order, clear spine

```
            Opening: wake up, learn starting element (chosen class)
                                   │
          ┌────────────────┬───────┴────────┬────────────────┐
          ▼                ▼                ▼                ▼
      Fire region      Air region      Water region     Earth region
    (find a master)  (find a master)  (find a master)  (find a master)
          └────────────────┴───────┬────────┴────────────────┘
                     any order; your starting element's
                     region is skipped or revisited for mastery
                                   │
                                   ▼
               All four elements learned → Spirit World
                                   │
                                   ▼
                  Tree of Time: Vaatu → one of several endings
```

**Tie Avatar State stages to *how many* elements you've learned, not *which* ones.** Because the order is free, stages should unlock at 2 elements, 3 elements and 4 elements (plus story moments). Every route then gets the same power curve.

### Balancing an open order

Elden Ring solves this by making some areas simply harder than others and letting players find that out by dying. Options for us:
- Each region has an **easy outer area and a hard core**. Finding the master means going deep.
- **Enemies scale** based on how many elements you have. Easier to balance, but it can make levelling up feel pointless, which works against the grind you want.
- **A mix:** regions are fixed difficulty, but the master's trial scales.

### Masters as a meaningful choice

Each element has 2–3 masters, and **each master teaches a different skill sub-tree**. That makes the choice matter to your build, not just the story. For example:

| Element | Master A teaches | Master B teaches |
|---|---|---|
| Fire | Lightning (precise, disciplined) | Combustion (raw power) |
| Water | Healing (support, spirit) | Plant/swamp bending (terrain control) |
| Earth | Hung Gar rooted style | Badgermole seismic style |
| Air | Evasion and flight | Spiritual airbending |

Masters could also have their own **questlines and allegiances** that feed into which ending you reach, like the NPC questlines in Elden Ring.

### Endings (lore-safe)

**Lore check:** Vaatu has to stay sealed until Korra's era, so **no ending can free him for good or destroy him forever**. Endings can still differ a lot in other ways:
- **The balance between the worlds:** spirits and humans kept apart, living together, or one side dominant.
- **Which masters' traditions survive**, depending on who you trained with and helped.
- **The Avatar's own fate:** your Avatar sacrifices themself to reseal Vaatu, and the cycle continues into canon's unknown future.
- **A secret ending** for players who found the most scattered lore, e.g. learning something about Wan that changes how you reseal Vaatu.

### Scattered lore: ways to tell story through the world

- **Carvings and murals** in ruins, which the show uses often (e.g. the Sun Warrior ruins, the Wan Shi Tong library)
- **Scrolls and journals** from masters, benders and ordinary people
- **Statues of past Avatars** that trigger a **vision** when you meditate at them, using the show's own vision mechanic
- **Item descriptions** that each add a line of history, as Elden Ring does
- **NPC dialogue** that changes as the world gets worse or better
- **Environmental clues:** a battlefield, a burnt village, a spirit-overgrown town that tells what happened without words

## Connects to

[01 Lore and Universe](01-Lore-and-Universe.md) · [03 Starting Classes](03-Starting-Classes.md) · [04 Gameplay Loop](04-Gameplay-Loop.md) · [05 Stats and Skill Trees](05-Stats-and-Skill-Trees.md) · [09 Boss System](09-Boss-System.md)
