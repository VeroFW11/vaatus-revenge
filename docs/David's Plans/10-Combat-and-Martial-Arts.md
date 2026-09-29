# 10 · Combat and Martial Arts

## My notes

**Decision: the combat is built on the real martial arts styles, so it feels true to real life and true to the anime.**

- Attention to detail in the map and fighting mechanics.
- The anime used real martial arts styles, and so should we.

**Which style each element is based on** (source: [Martial arts in the World of Avatar](https://avatar.fandom.com/wiki/Martial_arts_in_the_World_of_Avatar), Avatar Wiki):

- **Tai Chi** is the basis of **waterbending**.
- **Hung Gar** is the basis of **earthbending**.
  - Toph Beifong's [seismic sense](https://avatar.fandom.com/wiki/Seismic_sense) and her unique earthbending are based on the **Chu Gar Southern Praying Mantis** style.
- **Northern Shaolin** is the basis of **firebending**.
- **[Baguazhang](https://martialarts.fandom.com/wiki/Baguazhang)** is the basis of **airbending**.
- When Aang unlocked [Sonam's staff](https://avatar.fandom.com/wiki/Sonam%27s_staff) on the [lion turtle islands](https://avatar.fandom.com/wiki/Lion_turtle_islands), his movement was based on a **fa jin** technique.
- **[Pro-bending](https://avatar.fandom.com/wiki/Pro-bending)** draws heavily on aerial acrobatic martial arts, alongside MMA/UFC-style fighting.

**From the planning session with Jeremy (27 Sep, [transcript](../Transcribed%20Notes/2026-09-27-Planning-Session-Bending-and-Elden-Ring.md)):**
- **Not Elden Ring's spell system.** Elden Ring casts spells through equipped seals and wands, which doesn't suit Avatar's martial-arts feel. Look at **Black Myth: Wukong** and the **Batman** games for inspiration instead.
- **Element switching on the D-pad:** each direction is an element, and the **same buttons and combos do that element's moves**.
- **Each element has its own dodge.** Airbending ties into movement and dodging.
- Combat should feel **grounded and martial, like a real fight (a UFC-like feel)**.
- **Concern:** modifier-heavy inputs (hold a bumper + press a button) and directional dodge combos could be near-impossible to perform while switching elements quickly.
- **Plan: prototype and playtest the controls.** Changing controls and movesets should be quick to try in the engine.

**From the Quest Board (28 Sep):**
- **Core move set:** Elden Ring's set (light, heavy, dodge, block/parry, lock-on) **plus jump attacks and sprint attacks**.
- **Feel:** bending and attack movesets should feel **fluid and fun**. The reference for dodging is **Marvel's Spider-Man 2**, which feels far more fluid than a souls-like roll.
- **Dodge input:** **one dodge button**, and the dodge changes with your current element.
- **First element to prototype: Fire.**
- **Animation:** I intended to use Blender, but I'm open to easier options if they give the same quality. I need the pros and cons first (see below).

**From the Fire combat prototype (28-29 Sep, [prototype docs](../Prototype/README.md)):**
- **Stagger immunity on normal enemies:** a full combo earns one stagger, then about 1.5 s where they can't be staggered again, so they always swing back. *(David, 28 Sep)*
- **Camera: third-person over the shoulder, like Spider-Man 2**, not the centred Elden Ring view. It pulls back in fights and swaps shoulders near walls. *(David, 28 Sep)*
- **Perfect dodge counts in any direction** (timing is what matters, like Spider-Man 2); dashing toward the attacker gives bonus Momentum. *(David, 29 Sep)*
- **Crossbowmen share the 2 attack slots** with melee enemies: never more than 2 attackers at once. *(David, 29 Sep)*
- **A buffered dodge or guard beats a later attack press**, so a panicked escape is never turned into an attack. *(David, 29 Sep)*

## Things to decide

- [x] **Is Toph's style its own path?** Yes: seismic sense is taught by its own earth master (the badgermole hermit, see [02](02-Storyline.md)), and it works as a see-through-walls toggle.
- [ ] **Where does fa jin fit?** A charged power strike, a perfect-timing bonus, or an Avatar State move? *The prototype tests it as a timing reward: hold heavy, let go in the gold band (0.65-0.95 s).*
- [x] ~~Does pro-bending appear?~~ **No.** Pro-bending is from Korra's era, thousands of years after ours (see [01](01-Lore-and-Universe.md)). Its acrobatic/MMA influence could still shape how some human fighters move, without the sport itself.
- [x] **Toph's style without Toph.** Solved: it's an ancient badgermole technique. The first earthbenders learned from badgermoles, who "see" through vibration.
- [ ] **How do we get authentic animation?** Pros and cons are in "Getting animation that matches the real styles" below.
- [x] **Combat basics.** Light, heavy, dodge, block/parry, lock-on, **plus jump and sprint attacks**.
- [x] **First element to prototype.** **Fire.**
- [x] **Souls-like or Spider-Man-fluid dodging?** **Spider-Man fluid** *(Quest Board, 28 Sep)*: dodges are nearly free and cancel into anything. Bending and attacks cost stamina, so the tension moves from dodging to how you spend stamina. *Jeremy (lead on combat feel) should try it in the prototype and confirm.*
- [x] **Switching elements mid-fight.** **D-pad:** one direction per element, and the same buttons do the chosen element's moves. *(To be confirmed by playtesting.)*
- [x] **Dodging.** **Each element has its own dodge.** Airbending blends into movement.
- [x] **Dodge inputs.** **One dodge button**; the dodge changes with your element.
- [ ] **Avatar State in combat.** A temporary power mode with a meter?

## Claude's suggestions

### Each style's principle becomes a game mechanic

The aim is that the martial art changes **how each element plays**, not just how it looks. If the animations were swapped out, you should still be able to tell which element is which from how it plays.

| Element | Style | Real principle | Gameplay translation |
|---|---|---|---|
| Water | Tai Chi | Yield, redirect the opponent's force, circular flow | **Parry-and-redirect** is the core: catching an attack sends it back. Moves chain smoothly into each other, and it's strongest on defence. |
| Earth | Hung Gar | Rooted horse stance, strong blocks, powerful direct strikes | **Rooted stance**: planting your feet gives poise (you're not staggered) and heavier hits, but you're slow to move. The best blocking in the game. |
| Earth (badgermole style, Toph's in the show) | Southern Praying Mantis | Close range, reactive, sensing through contact | **Seismic sense**: see enemies through walls or in darkness while standing on earth. Fast close-range counters. |
| Fire | Northern Shaolin | Long extended strikes, kicks, aggressive forward pressure | **Momentum**: consecutive hits build power, and backing off loses it. Kicks and long-reach strikes. In the show firebending comes from the breath, which could tie into stamina. |
| Air | Baguazhang | Circle walking, constant direction changes, evasion | **Circling**: moving in an arc around a locked-on enemy builds power, and dodges curve. Air is weak standing still and strong while it keeps moving. |
| Special | Fa jin | Explosive whole-body power released over a short distance | **Burst strike**: a short charge, then a huge release. Could be a perfect-timing reward or an Avatar State technique. |
| Human fighters (inspired by, not the sport) | Pro-bending's influences | Acrobatics plus MMA | Some human enemies use light, bouncing footwork and quick jab combos. |

### Fluid vs. punishing: the dodge tension

Spider-Man 2 and Elden Ring treat dodging very differently:

| | Elden Ring | Spider-Man 2 |
|---|---|---|
| Cost | Stamina; spamming it leaves you open | Nearly free |
| Commitment | Once you roll, you're locked in until it ends | Cancels straight into attacks, webs or movement |
| Reward | Survival | Flow: a well-timed dodge leads into a counter or combo |
| Feel | Weighty, deliberate, punishing | Fast, acrobatic, flashy |

**A hybrid that keeps both:** dodges still cost stamina/chi, but they can **cancel into attacks**, and a **perfectly timed dodge** gives a reward: a free counter, a short slow-down, or a momentum boost for fire. Each element's dodge expresses its martial art. Fire's is aggressive and dashes forward; Air's curves around the enemy (Baguazhang circling). This is a starting point for the prototype, and the numbers (stamina cost, cancel timing, perfect-dodge window) all go in tuning data so they're easy to adjust.

### Getting animation that matches the real styles

**Blender is in every option.** Whatever we pick, the character is modelled and rigged in Blender, and animations get cleaned up there. The question is only *where the movement comes from*.

| Option | How it works | Pros | Cons |
|---|---|---|---|
| **A. Mixamo** | Free Adobe library: upload your character, it auto-rigs it, then download ready-made animations | Free, instant, good quality. It has walks, runs, rolls, and plenty of punches and kicks (a few martial-arts moves too). Perfect for prototyping | Generic: it won't have real Tai Chi or Hung Gar forms. Other games use the same animations. Not unique to us |
| **B. Film yourselves + AI motion capture** | Film a move on a phone; a tool like Rokoko Vision or DeepMotion turns it into a 3D animation | **Real martial arts motion**, which fits the pillar. Fast once set up. Free tiers exist | Someone has to perform the moves well. Results need cleanup in Blender (foot sliding, jitter). Free tiers have limits. Using other people's videos needs their permission |
| **C. Hand-animate in Blender** | Pose the character frame by frame | Total control, including anime-style exaggeration that real bodies can't do. Free | By far the slowest, with a steep learning curve. Hard for beginners to make martial arts look real |
| **D. Hybrid** *(Claude's recommendation)* | A for movement, B for martial-arts moves, C for bending flourishes and cleanup | Each tool does what it's best at. Quality where it matters, speed everywhere else | More tools to learn (but you learn them gradually) |

**For the combat prototype:** Mixamo alone is enough. Grey-box testing is about timing and feel, not looks. Unity can mix animations from all these sources on the same character (its "Humanoid" rig system), so starting with Mixamo loses nothing later.

**Early task:** collect reference videos of each style (basic forms, stances, key strikes) in `docs/knowledge/`. It helps whoever animates, and it shows us which moves each element should have.

## Connects to

[03 Starting Classes](03-Starting-Classes.md) · [05 Stats and Skill Trees](05-Stats-and-Skill-Trees.md) · [07 Weapons and Armour](07-Weapons-and-Armour.md) · [09 Boss System](09-Boss-System.md)
