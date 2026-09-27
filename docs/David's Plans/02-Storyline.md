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
- **Three or four masters per element to choose from**, so the choice is broad.
- **Several endings, based on the player's choices.**
- **There is direct storytelling**, but players are **rewarded for exploring and learning from the environment**.
- **Information and lore are scattered around the map.**

**From the Quest Board (27 Sep):**
- **Opening:** near the start, **a close friend you grew up with** explains the chaos the world has fallen into and sets you on your mission.
- **Finding masters:** all four ways: **rumours from people, clues in the world, hidden locations, and a trial to prove yourself.**
- **Hidden masters:** the **seismic sense** master, the **combustion** master, the **flight** master and **one of the water masters** are hidden. Players have to find them or stumble on them.
- **Masters:** you train with **one master per element first, and can train with others later** in the game.
- **Master questlines affect which ending you get.**
- **5–6 endings**, like Elden Ring.
- **5 Avatar State stages:** one for each element learned (in any order), plus **final mastery at the Tree of Time**.
- **Difficulty:** a **mix**. Regions have fixed difficulty, and each master's trial scales to you.
  - *My note:* make sure the tuning values are **easy to set and change**.
- **Vaatu before the end:** **visions and taunts**, speaking through corrupted spirits and dreams.
- **Scattered lore:** **carvings and murals**, **past-Avatar statues** that trigger visions, and **item descriptions**.

## Things to decide

- [x] **Opening scene.** A childhood friend explains the chaos and gives you your mission. *(Still open: where you wake up and what you first see.)*
- [ ] **Who is the childhood friend?** Name, nation, and what happens to them over the story.
- [x] **How do you find a master?** Rumours, world clues, hidden locations and trials.
- [x] **How many masters per element?** Three or four.
- [x] **Which masters are hidden?** Seismic sense, combustion, flight, and one water master.
- [ ] **Which water master is hidden?** Healer, ice warrior, swamp hermit or spirit calmer?
- [ ] **Who are the masters?** Names, personalities, where they live, what their trial is. (Draft roster in Claude's suggestions.)
- [x] **Can you learn from more than one master per element?** One first, more later.
- [x] **Does your choice of master affect the ending?** Yes, through their questlines.
- [ ] **What are the endings?** What choices lead to each one? (Lore check: every ending must leave Vaatu sealed; see below.) **Target: 5–6 endings.**
- [x] **How many Avatar State stages are there?** Five: one per element learned, plus final mastery at the Tree of Time.
- [x] **How do we balance difficulty when players can go anywhere?** A mix: fixed regions, scaling master trials.
- [x] **Does Vaatu appear or taunt you before the final fight?** Yes, through visions and taunts.
- [x] **Types of scattered lore.** Carvings and murals, past-Avatar statues, item descriptions.

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

**Avatar State stages unlock by *how many* elements you've learned, not *which* ones.** Decided: stage 1 at your 1st element, stages 2–4 at your 2nd, 3rd and 4th, and stage 5 as final mastery at the Tree of Time. Because the order is free, every route gets the same power curve.

### Is the difficulty mix harder to balance?

Your question from the Quest Board. A little: you balance fixed regions *and* scaling trials, instead of just one. It stays manageable if **every number lives in data, not code**: enemy health, damage, trial scaling and XP costs all go in one set of tuning files (in Unity these are called ScriptableObjects). Changing difficulty is then editing values in the editor, with no programming. This is now a project rule in `CLAUDE.md`.

### Balancing an open order

Elden Ring solves this by making some areas simply harder than others and letting players find that out by dying. Options for us:
- Each region has an **easy outer area and a hard core**. Finding the master means going deep.
- **Enemies scale** based on how many elements you have. Easier to balance, but it can make levelling up feel pointless, which works against the grind you want.
- **A mix:** regions are fixed difficulty, but the master's trial scales.

### Masters as a meaningful choice

Each element has 3–4 masters, and **each master teaches a different skill sub-tree**. That makes the choice matter to your build, not just the story. Draft roster (all lore-checked for our ancient era):

**🔥 Fire**
| Master | Teaches | Plays like | Lore anchor |
|---|---|---|---|
| Dragon lineage (Sun Warriors) | The **Dancing Dragon**: fire as life and breath | Balanced, sustained pressure, breath-powered | Wan learned it from a spirit dragon; the Sun Warriors are an ancient civilisation |
| Lightning master | **Lightning generation** | Precise, slow charge, huge single hits | Origin never given, so safe. ⚠️ *Redirecting* lightning was invented by Iroh much later, so it's off-limits |
| Combustion master | **Combustion** (explosive blasts) | Long-range artillery, big wind-ups | Origin never given, so safe |
| Fire sage / monk | **Fire jets and heat control** | Mobility: dashes, jet-boosted leaps | Fire propulsion appears in ATLA with no stated inventor |

**🌊 Water**
| Master | Teaches | Plays like | Lore anchor |
|---|---|---|---|
| Northern healer | **Healing** | Support: heal mid-fight, strengthen spirit water | The Water Tribes' healing tradition; the Spirit Oasis |
| Ice warrior | **Ice combat** | Tai Chi counters, ice spikes, freezing enemies | Classic waterbending |
| Swamp hermit | **Plantbending** | Terrain control, roots, traps | The Foggy Swamp style, origin never given |
| Spirit calmer | **Spirit purification** | Calm or purify corrupted spirits instead of killing them | Canon waterbenders calm spirits. It also answers the "purify vs kill" question for bosses (see [09](09-Boss-System.md)) |

**🪨 Earth**
| Master | Teaches | Plays like | Lore anchor |
|---|---|---|---|
| Rooted master | **Hung Gar** classic earthbending | Tanky, heavy blocks, big strikes | Classic earthbending |
| Badgermole hermit | **Seismic sense** (Southern Praying Mantis) | Close-range counters, sensing hidden enemies | The first earthbenders learned from badgermoles. *Also a path towards discovering Avatar-only metalbending* |
| Desert nomad | **Sandbending** | Area denial, sandstorms, blinding | The Si Wong desert tribes |
| Volcano master | **Lavabending** | Slow, devastating, terrain-melting | Canon calls it extremely rare, with no origin given |

**🌪️ Air**
| Master | Teaches | Plays like | Lore anchor |
|---|---|---|---|
| Temple monk | **Baguazhang** classic airbending | Evasion, circling, redirection | Classic airbending |
| Bison keeper | **The original bison forms** | Wind currents, knockback, a sky bison companion or mount? | Airbending was learned from the sky bison |
| Spirit monk | **Spiritual airbending** (spirit projection) | Scouting, spirit sight, entering the Spirit World | Air Nomads' deep spiritual tradition |
| Wandering guru | **Flight** | Late-game freedom of movement | Guru Laghima's teaching of detaching from earthly ties (his exact date is unknown) |

**Hidden masters (optional):** a 5th secret master per element could teach one of our **brand-new lost styles**, e.g. mist or spirit-light bending (see [05](05-Stats-and-Skill-Trees.md)). Only the most thorough explorers would find them.

**Scope note:** 16 masters means 16 characters, each with a location, a trial and a skill sub-tree, which is a lot of content. For the first playable slice, build **one element with two masters**. Once that works, add the rest.

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
