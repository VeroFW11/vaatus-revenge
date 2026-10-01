using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The named pictures an element effect can use (Art/VFX/<Element>/<slot>.png, see Art/VFX/README.md). Every slot has
    // a primitive stand-in, so none of them is required.
    public enum VfxSlot
    {
        None,
        Flame, Ember, Ribbon, Smoke, Burst, Ring, Whip, Orb,            // Fire (and the shared names Ribbon, Ring, Whip, Orb)
        Splash, Mist, Droplet, Shard, Wave,                             // Water
        Dust, Rock, Crack, Debris, Gravel, PillarTop, Swirl,            // Earth (and Air's Swirl)
        Streak, Puff,                                                   // Air
        BeatRing, Spark, Danger                                         // Common (the HUD and hit sparks)
    }

    // Every element's effects, one call each: ElementVfx.Burst(ElementId.Water, ...) splashes, Earth throws rock and
    // dust, Air a gust. Fire forwards straight to FireVfx, exactly as before Build 05, so Fire looks and behaves the
    // same; None is treated as Fire. All elements share FireVfx's pool, so nothing here allocates while playing.
    //
    // The look of Water, Earth and Air is in ElementVfxStyle (StyleOf); the pictures and the HUD colours come from the
    // ElementVfxLibraryAsset that ElementVfxBootstrap hands over when play starts (none = primitive stand-ins and the
    // default colours). Calls in edit mode or while play is stopping do nothing, like FireVfx.
    public static class ElementVfx
    {
        const int Folders = 5;                       // ElementId None (= Common), Fire, Water, Earth, Air
        const int SlotTotal = (int)VfxSlot.Danger + 1;

        static ElementVfxLibraryAsset library;
        static Texture2D[] pictures;                  // [folder * SlotTotal + slot]
        static bool[] pictureHasAlpha;
        static ElementVfxStyle[] styles;

        // ---------------------------------------------------------------- library, pictures, colours, styles

        // The library in use (null = stand-ins and default colours). Set by ElementVfxBootstrap.
        public static ElementVfxLibraryAsset Library => library;

        public static void SetLibrary(ElementVfxLibraryAsset newLibrary)
        {
            library = newLibrary;
            EnsureTables();
            System.Array.Clear(pictures, 0, pictures.Length);
            System.Array.Clear(pictureHasAlpha, 0, pictureHasAlpha.Length);
            if (library != null && library.Elements != null)
            {
                for (int i = 0; i < library.Elements.Length; i++)
                {
                    ElementVfxEntry entry = library.Elements[i];
                    if (entry == null || entry.Textures == null) continue;
                    int folder = FolderIndex(entry.Element);
                    for (int t = 0; t < entry.Textures.Length; t++)
                    {
                        SlotTexture slot = entry.Textures[t];
                        if (slot == null || slot.Texture == null || !TryParseSlot(slot.Slot, out VfxSlot parsed)) continue;
                        pictures[folder * SlotTotal + (int)parsed] = slot.Texture;
                        pictureHasAlpha[folder * SlotTotal + (int)parsed] = slot.HasAlpha;
                    }
                }
            }
            FireVfxRunner runner = FireVfx.ExistingRunner;
            if (runner != null) runner.RefreshPictures();
        }

        // The picture for an element's slot (folder None = Common). hasAlpha: draw it alpha-blended, else additive.
        public static bool TryGetTexture(ElementId folder, VfxSlot slot, out Texture2D texture, out bool hasAlpha)
        {
            texture = null;
            hasAlpha = false;
            if (pictures == null || slot == VfxSlot.None) return false;
            int index = FolderIndex(folder) * SlotTotal + (int)slot;
            texture = pictures[index];
            hasAlpha = pictureHasAlpha[index];
            return texture != null;
        }

        // Which folder a slot's picture lives in: the shared HUD and spark pictures are in Common.
        public static ElementId FolderOf(VfxSlot slot, ElementId element)
        {
            return slot == VfxSlot.BeatRing || slot == VfxSlot.Spark || slot == VfxSlot.Danger ? ElementId.None : element;
        }

        // The element's colour on the HUD (element wheel, combo counter, MIX icons): the library's, else the default.
        public static Color HudColor(ElementId element)
        {
            if (library != null && library.TryGetEntry(element, out ElementVfxEntry entry)) return entry.HudColor;
            return ElementVfxLibraryAsset.DefaultHudColor(element);
        }

        // The element's colour in the world (its effects' main colour), for things drawn on or round the body: the switch
        // flash, the MIX accents. The HUD keeps HudColor (matched to the pad's buttons). Opaque.
        public static Color WorldColor(ElementId element)
        {
            Color color = StyleOf(element).MainColor;
            color.a = 1f;
            return color;
        }

        // The look of an element's effects. Replace or edit it to restyle; never null. (Fire's moves use FireVfx.Style;
        // Fire's entry here only colours the Build 05 extras: switch ring, afterimages, accents.)
        public static ElementVfxStyle StyleOf(ElementId element)
        {
            if (styles == null) styles = new ElementVfxStyle[Folders];
            int index = FolderIndex(element == ElementId.None ? ElementId.Fire : element);
            return styles[index] ?? (styles[index] = ElementVfxStyle.CreateFor(element));
        }

        public static void SetStyle(ElementId element, ElementVfxStyle style)
        {
            if (styles == null) styles = new ElementVfxStyle[Folders];
            styles[FolderIndex(element == ElementId.None ? ElementId.Fire : element)] = style;
        }

        // ---------------------------------------------------------------- the move shapes (Core EffectKeys)

        // A strike's punch of the element travelling along direction. scale 1 = a light attack.
        public static void Burst(ElementId element, Vector3 position, Vector3 direction, float scale)
        {
            if (IsFire(element)) { FireVfx.Burst(position, direction, scale); return; }
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.ElementBurst(element, position, direction, scale);
        }

        // Earth in the air (canon, spec 8.1): a strike's push of dust only, no rock. Other elements burst as usual.
        public static void Dust(ElementId element, Vector3 position, Vector3 direction, float scale)
        {
            if (element != ElementId.Earth)
            {
                Burst(element, position, direction, scale);
                return;
            }
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.ElementDust(element, position, direction, scale);
        }

        // A projectile's impact: bursting out to radius.
        public static void Explosion(ElementId element, Vector3 position, float radius)
        {
            if (IsFire(element)) { FireVfx.Explosion(position, radius); return; }
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.ElementExplosion(element, position, radius);
        }

        // A shockwave spreading along the ground to radius (center = at the feet).
        public static void Ring(ElementId element, Vector3 center, float radius)
        {
            if (IsFire(element)) { FireVfx.Ring(center, radius); return; }
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.ElementRing(element, center, radius);
        }

        // A ribbon following a limb for duration seconds of game time (<= 0: until stopped).
        public static FireVfxHandle Trail(ElementId element, Transform follow, float duration)
        {
            if (IsFire(element)) return FireVfx.Trail(follow, duration);
            FireVfxRunner r = FireVfx.Runner;
            return r != null ? r.Trail(follow, duration, element) : FireVfxHandle.None;
        }

        // The heavy's wind-up at an anchor; drive it with handle.SetLevel(0..1), end it with handle.Stop().
        public static FireVfxHandle ChargeGlow(ElementId element, Transform anchor)
        {
            if (IsFire(element)) return FireVfx.ChargeGlow(anchor);
            FireVfxRunner r = FireVfx.Runner;
            return r != null ? r.ChargeGlow(anchor, element) : FireVfxHandle.None;
        }

        // A small pop where a hit connects, in the given colour, with a little of the element thrown off.
        public static void HitSpark(ElementId element, Vector3 position, Color color)
        {
            HitSpark(element, position, color, false);
        }

        // airborne: the attacker is off the ground. Earth then throws dust, never rock (ElementFxRules.DustOnly).
        public static void HitSpark(ElementId element, Vector3 position, Color color, bool airborne)
        {
            if (IsFire(element)) { FireVfx.HitSpark(position, color); return; }
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.ElementHitSpark(element, position, color, ElementFxRules.DustOnly(element, airborne));
        }

        // A burst, or Earth's dust when it's made off the ground (ElementFxRules.DustOnly): a dash, a switch, a dodge.
        public static void BurstOrDust(ElementId element, Vector3 position, Vector3 direction, float scale, bool airborne)
        {
            if (ElementFxRules.DustOnly(element, airborne)) Dust(element, position, direction, scale);
            else Burst(element, position, direction, scale);
        }

        // A launch flash pointing along direction (a projectile leaving the hand).
        public static void Muzzle(ElementId element, Vector3 position, Vector3 direction)
        {
            if (IsFire(element)) { FireVfx.Muzzle(position, direction); return; }
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.ElementMuzzle(element, position, direction);
        }

        // A wide fan from origin along direction, reaching range metres across arcDegrees.
        public static void Cone(ElementId element, Vector3 origin, Vector3 direction, float range, float arcDegrees)
        {
            if (IsFire(element)) { FireVfx.Cone(origin, direction, range, arcDegrees); return; }
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.ElementCone(element, origin, direction, range, arcDegrees);
        }

        // A column rising height metres from feet (under a launched enemy).
        public static void Pillar(ElementId element, Vector3 feet, float height)
        {
            if (IsFire(element)) { FireVfx.Pillar(feet, height); return; }
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.ElementPillar(element, feet, height);
        }

        // A ring racing out along the ground to radius (spinning sweeps).
        public static void Wheel(ElementId element, Vector3 center, float radius)
        {
            if (IsFire(element)) { FireVfx.Wheel(center, radius); return; }
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.ElementWheel(element, center, radius);
        }

        // A downward burst from position and a ring on the floor below it (a slam, a landing enemy).
        public static void Slam(ElementId element, Vector3 position, float radius)
        {
            if (IsFire(element)) { FireVfx.Slam(position, radius); return; }
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.ElementSlam(element, position, radius);
        }

        // A lash from the hand sweeping right to left across the arc at range, for duration seconds.
        public static FireVfxHandle Whip(ElementId element, Transform hand, Vector3 origin, Vector3 direction, float range,
            float arcDegrees, float duration)
        {
            if (IsFire(element)) return FireVfx.Whip(hand, origin, direction, range, arcDegrees, duration);
            FireVfxRunner r = FireVfx.Runner;
            return r != null ? r.Whip(hand, origin, direction, range, arcDegrees, duration, element) : FireVfxHandle.None;
        }

        // A travelling surge along direction to range across the arc (Water's waves, Earth's ground ripple, Air's crescent).
        // Fire has no wave of its own: a burst.
        public static void Wave(ElementId element, Vector3 origin, Vector3 direction, float range, float arcDegrees, float burstScale)
        {
            if (IsFire(element)) { FireVfx.Burst(origin, direction, burstScale); return; }
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.ElementWave(element, origin, direction, range, arcDegrees);
        }

        // A straight line along the ground to range (Earth's spikes and cracks, Air's blade). Fire: a burst.
        public static void Line(ElementId element, Vector3 origin, Vector3 direction, float range, float burstScale)
        {
            if (IsFire(element)) { FireVfx.Burst(origin, direction, burstScale); return; }
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.ElementLine(element, origin, direction, range);
        }

        // A shell round the body of radius (Earth's stone tent, Air's shield, Water's bubble). Fire: a burst.
        public static void Dome(ElementId element, Vector3 center, float radius, Vector3 direction, float burstScale)
        {
            if (IsFire(element)) { FireVfx.Burst(center, direction, burstScale); return; }
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.ElementDome(element, center, radius);
        }

        // A spinning swirl round the body out to radius. Fire: a burst.
        public static void Vortex(ElementId element, Vector3 center, float radius, Vector3 direction, float burstScale)
        {
            if (IsFire(element)) { FireVfx.Burst(center, direction, burstScale); return; }
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.ElementVortex(element, center, radius);
        }

        // Small fast pieces flying along direction to range (ice darts, stone chips, cutting air). Fire: a burst.
        public static void Shards(ElementId element, Vector3 origin, Vector3 direction, float range, float burstScale)
        {
            if (IsFire(element)) { FireVfx.Burst(origin, direction, burstScale); return; }
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.ElementShards(element, origin, direction, range);
        }

        // A ground impact round the feet out to radius (Earth's quake: cracks, dust, popping rock). Fire: a burst.
        public static void Stomp(ElementId element, Vector3 feet, float radius, Vector3 direction, float burstScale)
        {
            if (IsFire(element)) { FireVfx.Burst(feet, direction, burstScale); return; }
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.ElementStomp(element, feet, radius);
        }

        // ---------------------------------------------------------------- emitters

        // The element on a striking fist or foot for duration seconds (Fire: flames licking off it).
        public static FireVfxHandle LimbAura(ElementId element, Transform limb, float duration)
        {
            if (IsFire(element)) return FireVfx.LimbFlame(limb, duration);
            FireVfxRunner r = FireVfx.Runner;
            return r != null ? r.Emit(limb, FireVfxEmitterKind.LimbFlame, Vector3.up, duration, element) : FireVfxHandle.None;
        }

        // Earth's limb in the air: dust coming off it, never grit (see Dust). Other elements: their usual aura.
        public static FireVfxHandle LimbDust(ElementId element, Transform limb, float duration)
        {
            if (element != ElementId.Earth) return LimbAura(element, limb, duration);
            FireVfxRunner r = FireVfx.Runner;
            return r != null ? r.Emit(limb, FireVfxEmitterKind.LimbDust, Vector3.up, duration, element) : FireVfxHandle.None;
        }

        // A dash's push from a foot along direction (world) for duration seconds: air dashes and zip strikes.
        public static FireVfxHandle FootJet(ElementId element, Transform foot, Vector3 direction, float duration)
        {
            if (IsFire(element)) return FireVfx.FootJet(foot, direction, duration);
            FireVfxRunner r = FireVfx.Runner;
            return r != null ? r.Emit(foot, FireVfxEmitterKind.FootJet, direction, duration, element) : FireVfxHandle.None;
        }

        // What trails a launched enemy (Fire: embers and a smoky ribbon). duration <= 0 = until stopped.
        public static FireVfxHandle LaunchTrail(ElementId element, Transform follow, float duration)
        {
            if (IsFire(element)) return FireVfx.EmberTrail(follow, duration);
            FireVfxRunner r = FireVfx.Runner;
            if (r == null) return FireVfxHandle.None;
            r.Trail(follow, duration > 0f ? duration : LaunchTrailFallbackSeconds, element);
            return r.Emit(follow, FireVfxEmitterKind.Embers, Vector3.up, duration, element);
        }

        const float LaunchTrailFallbackSeconds = 1.5f;   // an open-ended launch trail's ribbon stops by itself after this

        // Fading see-through copies of the body left behind while it moves (a slip-in dodge). follow: the chest.
        public static FireVfxHandle Afterimage(ElementId element, Transform follow, float duration)
        {
            FireVfxRunner r = FireVfx.Runner;
            return r != null ? r.Emit(follow, FireVfxEmitterKind.Afterimage, Vector3.up, duration, element) : FireVfxHandle.None;
        }

        // ---------------------------------------------------------------- Build 05 presentation (rhythm, switching)

        // Switching element: a ring of the new element at the feet, its burst at the chest, the old one blown away.
        public static void Switch(ElementId from, ElementId to, Vector3 chest, Vector3 feet)
        {
            Switch(from, to, chest, feet, false);
        }

        // airborne: switching mid air string. Earth then washes up as dust, with no rock (ElementFxRules.DustOnly).
        public static void Switch(ElementId from, ElementId to, Vector3 chest, Vector3 feet, bool airborne)
        {
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.SwitchFlourish(from, to, chest, feet, airborne);
        }

        // A thin ring flashing outwards round position, facing the camera (an on-beat hit; a MIX hit in the element's
        // colour). lifetime in seconds.
        public static void BeatAccent(Vector3 position, float scale, Color color, float lifetime)
        {
            FireVfxRunner r = FireVfx.Runner;
            if (r != null) r.BeatAccent(position, scale, color, lifetime);
        }

        // Ends every running effect at once (a sandbox reset).
        public static void StopAll()
        {
            FireVfx.StopAll();
        }

        // ---------------------------------------------------------------- helpers

        static bool IsFire(ElementId element)
        {
            return element == ElementId.Fire || element == ElementId.None;
        }

        static int FolderIndex(ElementId element)
        {
            int index = (int)element;
            return index >= 0 && index < Folders ? index : 0;
        }

        static void EnsureTables()
        {
            if (pictures == null) pictures = new Texture2D[Folders * SlotTotal];
            if (pictureHasAlpha == null) pictureHasAlpha = new bool[Folders * SlotTotal];
        }

        // ---- The slot contract (Art/VFX/README.md): file names per folder ----

        // How many slots a folder expects, and the slot at an index (folder None = Common).
        public static int SlotCount(ElementId folder)
        {
            switch (folder)
            {
                case ElementId.Fire: return 8;
                case ElementId.Water: return 9;
                case ElementId.Earth: return 8;
                case ElementId.Air: return 5;
                default: return 3;
            }
        }

        public static VfxSlot SlotAt(ElementId folder, int index)
        {
            switch (folder)
            {
                case ElementId.Fire:
                    switch (index)
                    {
                        case 0: return VfxSlot.Flame;
                        case 1: return VfxSlot.Ember;
                        case 2: return VfxSlot.Ribbon;
                        case 3: return VfxSlot.Smoke;
                        case 4: return VfxSlot.Burst;
                        case 5: return VfxSlot.Ring;
                        case 6: return VfxSlot.Whip;
                        case 7: return VfxSlot.Orb;
                    }
                    break;
                case ElementId.Water:
                    switch (index)
                    {
                        case 0: return VfxSlot.Splash;
                        case 1: return VfxSlot.Mist;
                        case 2: return VfxSlot.Droplet;
                        case 3: return VfxSlot.Ribbon;
                        case 4: return VfxSlot.Ring;
                        case 5: return VfxSlot.Shard;
                        case 6: return VfxSlot.Orb;
                        case 7: return VfxSlot.Whip;
                        case 8: return VfxSlot.Wave;
                    }
                    break;
                case ElementId.Earth:
                    switch (index)
                    {
                        case 0: return VfxSlot.Dust;
                        case 1: return VfxSlot.Rock;
                        case 2: return VfxSlot.Crack;
                        case 3: return VfxSlot.Debris;
                        case 4: return VfxSlot.Gravel;
                        case 5: return VfxSlot.PillarTop;
                        case 6: return VfxSlot.Swirl;
                        case 7: return VfxSlot.Ribbon;
                    }
                    break;
                case ElementId.Air:
                    switch (index)
                    {
                        case 0: return VfxSlot.Swirl;
                        case 1: return VfxSlot.Streak;
                        case 2: return VfxSlot.Ring;
                        case 3: return VfxSlot.Ribbon;
                        case 4: return VfxSlot.Puff;
                    }
                    break;
                default:
                    switch (index)
                    {
                        case 0: return VfxSlot.BeatRing;
                        case 1: return VfxSlot.Spark;
                        case 2: return VfxSlot.Danger;
                    }
                    break;
            }
            return VfxSlot.None;
        }

        // The folder's name under Art/VFX/.
        public static string FolderName(ElementId folder)
        {
            switch (folder)
            {
                case ElementId.Fire: return "Fire";
                case ElementId.Water: return "Water";
                case ElementId.Earth: return "Earth";
                case ElementId.Air: return "Air";
                default: return "Common";
            }
        }

        // The slot's file name (without .png), lower case.
        public static string SlotName(VfxSlot slot)
        {
            switch (slot)
            {
                case VfxSlot.Flame: return "flame";
                case VfxSlot.Ember: return "ember";
                case VfxSlot.Ribbon: return "ribbon";
                case VfxSlot.Smoke: return "smoke";
                case VfxSlot.Burst: return "burst";
                case VfxSlot.Ring: return "ring";
                case VfxSlot.Whip: return "whip";
                case VfxSlot.Orb: return "orb";
                case VfxSlot.Splash: return "splash";
                case VfxSlot.Mist: return "mist";
                case VfxSlot.Droplet: return "droplet";
                case VfxSlot.Shard: return "shard";
                case VfxSlot.Wave: return "wave";
                case VfxSlot.Dust: return "dust";
                case VfxSlot.Rock: return "rock";
                case VfxSlot.Crack: return "crack";
                case VfxSlot.Debris: return "debris";
                case VfxSlot.Gravel: return "gravel";
                case VfxSlot.PillarTop: return "pillar_top";
                case VfxSlot.Swirl: return "swirl";
                case VfxSlot.Streak: return "streak";
                case VfxSlot.Puff: return "puff";
                case VfxSlot.BeatRing: return "beat_ring";
                case VfxSlot.Spark: return "spark";
                case VfxSlot.Danger: return "danger";
                default: return "";
            }
        }

        public static bool TryParseSlot(string name, out VfxSlot slot)
        {
            for (int i = 1; i < SlotTotal; i++)
            {
                if (string.Equals(SlotName((VfxSlot)i), name, System.StringComparison.OrdinalIgnoreCase))
                {
                    slot = (VfxSlot)i;
                    return true;
                }
            }
            slot = VfxSlot.None;
            return false;
        }

        // Domain reload is off in this project, so statics survive between play sessions: forget the last session's
        // library (its textures may be unloaded) and styles when play starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            library = null;
            pictures = null;
            pictureHasAlpha = null;
            styles = null;
        }
    }
}
