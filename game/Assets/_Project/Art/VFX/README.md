# Element effect textures

Optional pictures for the move effects. **The game works without any of them**: every slot has a primitive
stand-in (blobs become spheres, rings, cracks and swirls a ring band, streaks and shards a stretched sphere, rocks and
debris a cube, a ribbon a plain trail). Drop a picture in and it is used the next time you run
*Vaatu's Revenge > Refresh VFX Textures* (or rebuild the sandbox).

## Where they go

`Art/VFX/<Element>/<slot>.png`, file names in lower case. One folder per element plus `Common`.

| Folder | Slots |
|---|---|
| `Fire` (optional: Fire has its own look already) | `flame` `ember` `ribbon` `smoke` `burst` `ring` `whip` `orb` |
| `Water` | `splash` `mist` `droplet` `ribbon` `ring` `shard` `orb` `whip` `wave` |
| `Earth` | `dust` `rock` `crack` `debris` `gravel` `pillar_top` `swirl` `ribbon` |
| `Air` | `swirl` `streak` `ring` `ribbon` `puff` |
| `Common` | `beat_ring` `spark` `danger` |

## How they are imported

Anything under `Art/VFX/` is imported as sRGB, with mipmaps, at most 512 px, alpha is transparency. `ribbon` and
`streak` repeat (they are stretched along trails); everything else is clamped.

- A picture **with an alpha channel** is drawn alpha-blended (water blobs, dust, rock).
- A picture **without alpha** (a black background) is drawn additive: black disappears, bright parts glow (fire, sparks).

A missing slot is reported once, in one log line when play starts, never every frame.

## How to get them

Who: **David** (the art owner). The game never waits on them: every slot has a stand-in.

- **The generated set:** allow the image host in the Claude environment's network policy (Build 05 spec, §8 open
  question 3) and ask Claude to download and rename them, or download them by hand from the generation page and rename
  them as in the table below.
- **Or make your own:** any 256-512 px `.png` works (with alpha for blended effects, on black for glowing ones), named
  after its slot.

## Generated art waiting to be added

The 24 images generated for Build 05 could not be downloaded into the repository (nor could the player model, see
`Art/Characters/README.md`). When you have them they arrive with their generated names: rename each one to its slot below
and put it in its element's folder, then commit (Git LFS stores `.png`):

| Generated file | Slot |
|---|---|
| Fire `flame_tongue`, `ember_sparks_cluster`, `flame_trail_streak`, `heat_smoke_wisp`, `fire_burst_puff`, `fire_swirl_ring`, `fire_whip_arc`, `fireball_core` | `flame`, `ember`, `ribbon`, `smoke`, `burst`, `ring`, `whip`, `orb` |
| Water `water_splash_crown`, `spray_mist`, `water_droplet_cluster`, `water_trail_streak`, `foam_ring_ripple`, `water_orb`, `water_ribbon_whip`, `water_wave_crest` | `splash`, `mist`, `droplet`, `ribbon`, `ring`, `orb`, `whip`, `wave` |
| Earth `dust_puff_cloud`, `rock_chunk`, `ground_crack_decal`, `rock_shards_cluster`, `pebble_debris_spray`, `earth_pillar_top`, `sand_dust_swirl` | `dust`, `rock`, `crack`, `debris`, `gravel`, `pillar_top`, `swirl` (`stone_fist` is not used) |
| Air: none yet. Water `shard` and every `Common` slot: none yet | stand-ins |

`.png` files go through Git LFS. Remember to `git lfs lock` before editing `.blend`, `.fbx` or `.psd` sources.
