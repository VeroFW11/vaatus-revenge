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

## Things to decide

- [ ] **Is Toph's style its own path?** E.g. an earthbending sub-tree or a separate "blind bandit" stance with seismic sense.
- [ ] **Where does fa jin fit?** A charged power strike, a perfect-timing bonus, or an Avatar State move?
- [ ] **Does pro-bending appear?** It's from the Korra era, so it depends on when the game is set (see [01](01-Lore-and-Universe.md)). It could be an arena side activity or how human enemies fight.
- [ ] **How do we get authentic animation?** See the pipeline options below.
- [ ] **Combat basics.** Light attack, heavy attack, dodge, block/parry, lock-on, like Elden Ring?
- [ ] **Switching elements mid-fight.** A stance button, element wheel, or combos that mix elements?
- [ ] **Avatar State in combat.** A temporary power mode with a meter?

## Claude's suggestions

### Each style's principle becomes a game mechanic

The aim is that the martial art changes **how each element plays**, not just how it looks. If the animations were swapped out, you should still be able to tell which element is which from how it plays.

| Element | Style | Real principle | Gameplay translation |
|---|---|---|---|
| Water | Tai Chi | Yield, redirect the opponent's force, circular flow | **Parry-and-redirect** is the core: catching an attack sends it back. Moves chain smoothly into each other, and it's strongest on defence. |
| Earth | Hung Gar | Rooted horse stance, strong blocks, powerful direct strikes | **Rooted stance**: planting your feet gives poise (you're not staggered) and heavier hits, but you're slow to move. The best blocking in the game. |
| Earth (Toph) | Southern Praying Mantis | Close range, reactive, sensing through contact | **Seismic sense**: see enemies through walls or in darkness while standing on earth. Fast close-range counters. |
| Fire | Northern Shaolin | Long extended strikes, kicks, aggressive forward pressure | **Momentum**: consecutive hits build power, and backing off loses it. Kicks and long-reach strikes. In the show firebending comes from the breath, which could tie into stamina. |
| Air | Baguazhang | Circle walking, constant direction changes, evasion | **Circling**: moving in an arc around a locked-on enemy builds power, and dodges curve. Air is weak standing still and strong while it keeps moving. |
| Special | Fa jin | Explosive whole-body power released over a short distance | **Burst strike**: a short charge, then a huge release. Could be a perfect-timing reward or an Avatar State technique. |
| Korra era | Pro-bending | Acrobatics plus MMA | Light, bouncing footwork, quick jabs and combos, rings and knockback. |

### Getting animation that matches the real styles

Mixamo's free animation library won't have real Tai Chi or Hung Gar forms, so we'll need our own. Free or cheap options, from easiest to hardest:

1. **Reference video → hand animation in Blender.** Record yourselves or use practitioner videos as reference. It's slow, but gives the most control.
2. **Video → motion capture with AI tools.** Some tools turn a single video into a 3D animation (e.g. Rokoko Vision, DeepMotion, both with free tiers). You or Jeremy could film yourselves doing basic forms.
3. **Mixamo basics + custom attacks.** Use Mixamo for walking, running and rolling, and custom animations only for bending moves. This is the realistic starting point.

**Early task:** collect reference videos of each style (basic forms, stances, key strikes) in `docs/knowledge/`. It helps whoever animates, and it shows us which moves each element should have.

## Connects to

[03 Starting Classes](03-Starting-Classes.md) · [05 Stats and Skill Trees](05-Stats-and-Skill-Trees.md) · [07 Weapons and Armour](07-Weapons-and-Armour.md) · [09 Boss System](09-Boss-System.md)
