using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Water, Earth and Air, drawn with the same pool as Fire (ElementVfx calls these; Fire never comes here). Each shape
    // keeps Fire's rule that what you see is what can hit: a wave travels exactly to the move's range, a cone fans across
    // exactly its arc. What changes per element is the material, the motion and the pieces:
    //   Water  see-through blobs (splash), droplets that fall and stop on the floor, foam rings, mist
    //   Earth  lit rock cubes that tumble under heavy gravity, land, slide and stay a moment; dust clouds; dark cracks
    //   Air    additive streaks and rings, puffs, spinning; nothing falls, nothing lingers
    // Every picture slot (Art/VFX/<Element>/<slot>.png) replaces its stand-in when present: blobs and clouds become
    // camera-facing pictures, rings a flat picture on the floor, rocks a tumbling picture.
    public partial class FireVfxRunner
    {
        const float FloorRestOffset = 0.05f;   // a landed rock or droplet rests this far above the floor point

        // ---- Shapes ------------------------------------------------------------------------------------------------

        internal void ElementBurst(ElementId element, Vector3 position, Vector3 direction, float scale)
        {
            if (!(scale > 0f)) return;
            ElementVfxStyle s = ElementVfx.StyleOf(element);
            Vector3 dir = SafeDirection(direction, Vector3.up);
            float size = s.BurstSize * scale;
            float life = s.BurstLifetime;
            float floor = FloorBelow(position);
            if (ElementFxRules.StoneFromFloor(element))
            {
                // Earth (J3-07, spec 7): stone is drawn from the ground, never out of a fist or a chest. Dust bursts from
                // the strike; the rock rises out of the floor under it, thrown up and along the strike.
                ElementDust(element, position, dir, scale);
                FloorStone(s, position, dir, scale, floor, Mathf.Clamp(s.BurstPieces, 0, 12), life);
                return;
            }
            Cloud(s, position, dir * (s.BurstSpeed * scale * 0.3f), 8f, size, life * 0.8f);
            int count = Mathf.Clamp(s.BurstPieces, 0, 12);
            for (int i = 0; i < count; i++)
            {
                Vector3 velocity = (dir + Random.insideUnitSphere * 0.4f).normalized * (s.BurstSpeed * scale * Random.Range(0.6f, 1.1f));
                Bit(s, position + dir * (size * 0.2f), velocity, s.ChunkSize * Mathf.Sqrt(scale) * Random.Range(0.7f, 1.2f), life, floor);
            }
            // Water and Air push a ring out ahead of the strike: a splash crown, a ring of pressure.
            if (element != ElementId.Earth)
            {
                Shockwave(s, position + dir * (size * 0.4f), dir, size * 0.3f, size * 1.6f, life * 0.6f);
            }
        }

        // Earth's rock for an effect at 'position' (J3-07): pieces thrown up out of the floor under it (leaning along the
        // strike's direction when it has one along the floor), with a kick of dust there. Nothing when the point is too
        // high above the floor for the floor to be its source (a juggled foe): the dust at the point is all there is.
        void FloorStone(ElementVfxStyle s, Vector3 position, Vector3 direction, float scale, float floor, int count, float life)
        {
            if (count <= 0 || !ElementFxRules.FloorStoneUnder(s.Element, position.y - floor)) return;
            Vector3 along = new Vector3(direction.x, 0f, direction.z);
            along = along.sqrMagnitude > 0.01f ? along.normalized : Vector3.zero;
            Vector3 ground = new Vector3(position.x, floor + 0.1f, position.z) + along * 0.3f;
            Cloud(s, ground + Vector3.up * 0.1f, Vector3.up * 0.6f, 3f, s.BurstSize * Mathf.Max(0.5f, scale) * 0.8f, life);
            for (int i = 0; i < count; i++)
            {
                Vector3 velocity = (Vector3.up * 1.4f + along * 0.8f + Random.insideUnitSphere * 0.35f).normalized
                                   * (s.BurstSpeed * Mathf.Max(0.6f, scale) * Random.Range(0.8f, 1.2f));
                Bit(s, ground + Random.insideUnitSphere * 0.15f, velocity, s.ChunkSize * Mathf.Sqrt(Mathf.Max(0.3f, scale)) * Random.Range(0.7f, 1.2f), life,
                    floor);
            }
        }

        // Earth's strikes in the air: a cloud of dust and a puff of pressure, no rock pieces (an earthbender in the air has
        // no ground to draw stone from: canon, spec 8.1).
        internal void ElementDust(ElementId element, Vector3 position, Vector3 direction, float scale)
        {
            if (!(scale > 0f)) return;
            ElementVfxStyle s = ElementVfx.StyleOf(element);
            Vector3 dir = SafeDirection(direction, Vector3.up);
            float size = s.BurstSize * scale;
            float life = s.BurstLifetime;
            Cloud(s, position, dir * (s.BurstSpeed * scale * 0.3f), 8f, size, life * 0.8f);
            Cloud(s, position + dir * (size * 0.3f), dir * (s.BurstSpeed * scale * 0.15f), 6f, size * 0.7f, life * 0.6f);
            Shockwave(s, position + dir * (size * 0.4f), dir, size * 0.3f, size * 1.4f, life * 0.5f);
        }

        internal void ElementExplosion(ElementId element, Vector3 position, float radius)
        {
            ElementVfxStyle s = ElementVfx.StyleOf(element);
            radius = Mathf.Max(0.1f, radius);
            float life = s.ExplosionLifetime;
            float floor = FloorBelow(position);
            Cloud(s, position, Vector3.zero, 0f, radius * 2f, life);
            if (element == ElementId.Water) Mist(s, position, radius * 2.6f, life * 1.4f);
            Band(s, VfxSlot.Ring, new Vector3(position.x, Mathf.Max(floor, position.y - radius * 0.5f), position.z), Quaternion.identity,
                radius * 0.15f, radius * 1f, radius * 1.15f, s.RingHeight, life * 0.8f, s.CoreColor, s.FadeColor, 0.55f);
            int count = element == ElementId.Earth ? 10 : 8;
            float speed = s.ChunkSpeed * Mathf.Sqrt(radius);
            for (int i = 0; i < count; i++)
            {
                Vector3 d = Random.onUnitSphere;
                if (d.y < 0f) d.y *= -0.5f; // mostly up and out, not into the floor
                Bit(s, position, d * (speed * Random.Range(0.6f, 1.2f)), s.ChunkSize * Random.Range(0.8f, 1.4f), life, floor);
            }
            if (element == ElementId.Earth) Crack(s, new Vector3(position.x, floor, position.z), radius * 1.6f, life * 2f);
        }

        internal void ElementRing(ElementId element, Vector3 center, float radius)
        {
            ElementVfxStyle s = ElementVfx.StyleOf(element);
            radius = Mathf.Max(0.2f, radius);
            float life = s.RingLifetime;
            Vector3 ground = center + Vector3.up * GroundOffset;
            Color start = element == ElementId.Earth ? s.CloudColor : s.CoreColor;
            Band(s, VfxSlot.Ring, ground, Quaternion.identity, radius * 0.15f, radius * 1f, radius * 1.1f, s.RingHeight, life, start, s.FadeColor, 0.6f);
            // A ripple on the floor under it: foam for water, a pale flash for air; earth cracks instead.
            if (element == ElementId.Earth) Crack(s, ground, radius * 1.4f, life * 2f);
            else FlatRing(s, ground, Quaternion.identity, radius * 0.2f, radius * 0.9f, life * 0.8f, s.CoreColor, s.FadeColor, false);
            for (int i = 0; i < 6; i++)
            {
                float angle = (i + Random.value * 0.5f) * TwoPi / 6f;
                var d = new Vector3(Mathf.Sin(angle), 0.35f, Mathf.Cos(angle));
                Bit(s, ground + Vector3.up * 0.1f, d * (s.ChunkSpeed * Random.Range(0.6f, 1f)), s.ChunkSize, life, center.y);
            }
        }

        internal void ElementHitSpark(ElementId element, Vector3 position, Color color)
        {
            ElementHitSpark(element, position, color, false);
        }

        // dustOnly: Earth hitting from the air: the pop and a puff of dust, no gravel (canon, ElementFxRules.DustOnly).
        internal void ElementHitSpark(ElementId element, Vector3 position, Color color, bool dustOnly)
        {
            ElementVfxStyle s = ElementVfx.StyleOf(element);
            FireVfxStyle fire = FireVfx.Style;
            Color bright = color * fire.SparkIntensity;
            bright.a = 1f;
            Color faded = color;
            faded.a = 0f;
            float life = s.SparkLifetime;
            // The pop itself is light (additive) whatever the element, so a hit always reads; the element is in the bits.
            VfxSlot slot = VfxSlot.Spark;
            Texture2D picture = null;
            bool hasAlpha = false;
            bool pictured = ElementVfx.TryGetTexture(ElementId.None, slot, out picture, out hasAlpha);
            FireVfxPiece pop = SpawnPiece(pictured ? VfxShape.Quad : VfxShape.Sphere, pictured && hasAlpha ? VfxMaterialKind.AlphaBlend : VfxMaterialKind.Additive,
                picture, position, Vector3.zero, 0f, Uniform(s.SparkSize * 0.15f), Uniform(s.SparkSize), Vector3.zero, 0.3f, life, bright, faded);
            if (pop != null) pop.Billboard = pictured;
            if (dustOnly)
            {
                Cloud(s, position, Vector3.up * 0.3f, 4f, s.SparkSize * 0.8f, life * 1.5f);
                return;
            }
            float floor = FloorBelow(position);
            int count = Mathf.Clamp(s.SparkCount, 0, 12);
            if (ElementFxRules.StoneFromFloor(element))
            {
                // Earth (J3-07): grit never flies out of the body that was hit. Dust there; chips kick up from the floor
                // under it.
                Cloud(s, position, Vector3.up * 0.3f, 4f, s.SparkSize * 0.8f, life * 1.5f);
                if (!ElementFxRules.FloorStoneUnder(element, position.y - floor)) return;
                Vector3 ground = new Vector3(position.x, floor + 0.08f, position.z);
                for (int i = 0; i < count; i++)
                {
                    Vector3 d = Random.insideUnitSphere * 0.6f + Vector3.up;
                    Bit(s, ground + Random.insideUnitSphere * 0.2f, d.normalized * (s.ChunkSpeed * 0.5f * Random.Range(0.8f, 1.3f)), s.ChunkSize * 0.6f,
                        life * 2f, floor);
                }
                return;
            }
            for (int i = 0; i < count; i++)
            {
                Vector3 d = Random.onUnitSphere;
                if (d.y < 0f) d.y *= -0.3f;
                Bit(s, position, d * (s.ChunkSpeed * 0.6f * Random.Range(0.8f, 1.4f)), s.ChunkSize * 0.6f, life * 2f, floor);
            }
        }

        internal void ElementMuzzle(ElementId element, Vector3 position, Vector3 direction)
        {
            ElementVfxStyle s = ElementVfx.StyleOf(element);
            Vector3 dir = SafeDirection(direction, Vector3.forward);
            float size = s.BurstSize * 0.8f;
            Cloud(s, position + dir * (size * 0.3f), dir * 2f, 6f, size, s.BurstLifetime * 0.5f);
            Shockwave(s, position + dir * (size * 0.5f), dir, size * 0.2f, size * 1.4f, s.BurstLifetime * 0.5f);
        }

        // Pieces fan across the arc and travel exactly to range in the cone's lifetime.
        internal void ElementCone(ElementId element, Vector3 origin, Vector3 direction, float range, float arcDegrees)
        {
            ElementVfxStyle s = ElementVfx.StyleOf(element);
            Vector3 dir = SafeDirection(direction, Vector3.forward);
            float life = Mathf.Max(0.05f, s.ConeLifetime);
            float speed = Mathf.Max(0.1f, range) / life;
            int count = Mathf.Clamp(Mathf.RoundToInt(arcDegrees / 8f), 6, 16);
            float half = Mathf.Clamp(arcDegrees, 10f, 180f) * 0.5f;
            float floor = FloorBelow(origin);
            for (int i = 0; i < count; i++)
            {
                float yaw = Mathf.Lerp(-half, half, count > 1 ? i / (count - 1f) : 0.5f) + Random.Range(-3f, 3f);
                Vector3 d = Quaternion.AngleAxis(yaw, Vector3.up) * dir;
                float peak = s.BurstSize * Random.Range(0.8f, 1.2f) * (0.6f + range * 0.12f);
                if (element == ElementId.Air)
                {
                    // A gale: long streaks racing out to the edge of the reach.
                    Streak(s, origin, d * (speed * Random.Range(0.85f, 1f)), 0f, peak * 0.25f, 3.5f, life * Random.Range(0.9f, 1.05f), s.AccentColor);
                }
                else
                {
                    FireVfxPiece blob = Cloud(s, origin, d * (speed * Random.Range(0.85f, 1f)), 0f, peak, life * Random.Range(0.9f, 1.05f));
                    Stretch(blob, d, 1.6f);
                    if (i % 2 == 0) Bit(s, origin, d * (speed * 0.8f) + Vector3.up * 2f, s.ChunkSize, life * 1.5f, floor);
                }
            }
            Shockwave(s, origin + dir * 0.3f, dir, s.BurstSize * 0.3f, s.BurstSize * 1.8f, life * 0.6f);
        }

        // Under a launched enemy: a water spout, a pillar of rock thrust up from the floor, an updraft of spinning rings.
        internal void ElementPillar(ElementId element, Vector3 feet, float height)
        {
            ElementVfxStyle s = ElementVfx.StyleOf(element);
            height = Mathf.Max(0.5f, height);
            float life = Mathf.Max(0.05f, s.PillarLifetime);
            Vector3 ground = feet + Vector3.up * GroundOffset;
            switch (element)
            {
                case ElementId.Earth:
                {
                    // A column of rock rises out of the floor (from below, so it never appears in mid-air), holds, crumbles.
                    float rise = life * 0.3f;
                    const float drag = 12f;
                    float lift = height * drag / (1f - Mathf.Exp(-drag * rise));
                    Vector3 size = new Vector3(0.9f, height, 0.9f);
                    FireVfxPiece column = Chunk(s, VfxSlot.PillarTop, ground - Vector3.up * (height * 0.5f), Vector3.up * lift, drag,
                        size, size, new Vector3(0.5f, height * 0.6f, 0.5f), 0.7f, life * 1.4f);
                    if (column != null)
                    {
                        column.Rotation = Quaternion.AngleAxis(Random.Range(0f, 90f), Vector3.up);
                        column.Gravity = 0f;
                        column.HasFloor = false;
                        column.SpinSpeed = 0f;
                        Place(column);
                    }
                    ElementStomp(element, feet, 1.4f);
                    break;
                }
                case ElementId.Air:
                {
                    // Rings of wind climbing round the enemy, spinning as they go, with streaks rushing up.
                    for (int i = 0; i < 3; i++)
                    {
                        FireVfxPiece ring = Band(s, VfxSlot.Swirl, ground + Vector3.up * (i * 0.3f),
                            Quaternion.AngleAxis(Random.Range(-12f, 12f), Vector3.right), 0.6f, 1.6f - i * 0.25f, 1.2f, s.RingHeight, life, s.CoreColor, s.FadeColor, 0.35f);
                        if (ring == null) continue;
                        ring.Velocity = Vector3.up * (height / life * (0.6f + i * 0.2f));
                        ring.Age = -i * 0.06f;
                        ring.SpinAxis = Vector3.up;
                        ring.SpinSpeed = s.SpinSpeed;
                        Place(ring);
                    }
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 at = ground + new Vector3(Random.Range(-0.6f, 0.6f), 0.2f, Random.Range(-0.6f, 0.6f));
                        Streak(s, at, Vector3.up * (height / life * Random.Range(0.7f, 1.2f)), 0f, 0.07f, 4f, life * Random.Range(0.6f, 0.9f), s.AccentColor);
                    }
                    ElementRing(element, feet, 1.1f);
                    break;
                }
                default:
                {
                    // A spout of water: blobs climbing the column, droplets raining off it, a splash ring at the base.
                    int count = 10;
                    for (int i = 0; i < count; i++)
                    {
                        float up = height / life * Random.Range(0.45f, 1f);
                        var v = new Vector3(Random.Range(-0.4f, 0.4f), up, Random.Range(-0.4f, 0.4f));
                        FireVfxPiece blob = Cloud(s, ground, v, 0.5f, s.BurstSize * Random.Range(0.9f, 1.4f), life * Random.Range(0.8f, 1.1f));
                        Stretch(blob, Vector3.up, 1.6f);
                    }
                    for (int i = 0; i < 6; i++)
                    {
                        Vector3 d = new Vector3(Random.Range(-1f, 1f), 2.5f, Random.Range(-1f, 1f));
                        Bit(s, ground + Vector3.up * (height * 0.5f), d * Random.Range(1.2f, 2f), s.ChunkSize, life * 1.4f, feet.y);
                    }
                    ElementRing(element, feet, 1.1f);
                    break;
                }
            }
        }

        // A ring all round plus pieces racing out along the floor to exactly radius.
        internal void ElementWheel(ElementId element, Vector3 center, float radius)
        {
            ElementVfxStyle s = ElementVfx.StyleOf(element);
            radius = Mathf.Max(0.3f, radius);
            ElementRing(element, center, radius);
            float life = Mathf.Max(0.05f, s.RingLifetime);
            int count = 12;
            for (int i = 0; i < count; i++)
            {
                float a = (i + Random.value * 0.4f) * TwoPi / count;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 start = center + d * 0.4f + Vector3.up * 0.3f;
                Vector3 velocity = d * ((radius - 0.4f) / life);
                if (element == ElementId.Air) Streak(s, start, velocity, 0f, 0.08f, 3f, life, s.AccentColor);
                else Stretch(Cloud(s, start, velocity, 0f, s.BurstSize * 0.8f, life), d, 1.8f);
            }
        }

        // Same structure as Fire's slam: a burst down from the strike, a ring and a burst where it meets the floor.
        internal void ElementSlam(ElementId element, Vector3 position, float radius)
        {
            ElementBurst(element, position, Vector3.down, Mathf.Max(0.6f, radius * 0.4f));
            Vector3 ground = GroundBelow(position);
            ElementRing(element, ground, Mathf.Max(0.5f, radius));
            ElementExplosion(element, ground + Vector3.up * 0.3f, Mathf.Max(0.3f, radius * 0.3f));
        }

        // A surge travelling along the floor to exactly range across the arc.
        internal void ElementWave(ElementId element, Vector3 origin, Vector3 direction, float range, float arcDegrees)
        {
            ElementVfxStyle s = ElementVfx.StyleOf(element);
            Vector3 dir = Flat(direction);
            range = Mathf.Max(0.5f, range);
            float life = Mathf.Max(0.05f, s.WaveLifetime);
            float speed = range / life;
            float floor = FloorBelow(origin);
            Vector3 ground = new Vector3(origin.x, floor + GroundOffset, origin.z);
            float half = Mathf.Clamp(arcDegrees, 10f, 300f) * 0.5f;
            int across = Mathf.Clamp(Mathf.RoundToInt(arcDegrees / 14f), 3, 12);
            switch (element)
            {
                case ElementId.Earth:
                {
                    // The ground ripples outward: rocks pop up one row after another as the wave passes.
                    int rows = Mathf.Clamp(Mathf.RoundToInt(range / 0.7f), 3, 10);
                    for (int row = 1; row <= rows; row++)
                    {
                        float distance = range * row / rows;
                        int pieces = Mathf.Max(1, Mathf.RoundToInt(across * distance / range));
                        for (int k = 0; k < pieces; k++)
                        {
                            float yaw = pieces > 1 ? Mathf.Lerp(-half, half, k / (pieces - 1f)) : 0f;
                            Vector3 at = ground + Quaternion.AngleAxis(yaw, Vector3.up) * dir * distance;
                            float delay = distance / speed;
                            FireVfxPiece rock = Bit(s, at, Vector3.up * Random.Range(2.5f, 4f) + dir * 1.5f, s.ChunkSize * Random.Range(1f, 1.6f),
                                life, floor);
                            Delay(rock, delay);
                            if (k % 2 == 0) Delay(Cloud(s, at + Vector3.up * 0.2f, Vector3.up * 0.6f, 2f, s.BurstSize * 0.9f, life), delay);
                        }
                    }
                    break;
                }
                case ElementId.Air:
                {
                    // A crescent of wind flying out, with streaks across the arc.
                    Vector3 mid = ground + Vector3.up * 0.9f;
                    FireVfxPiece crescent = Shockwave(s, mid, dir, 0.4f, 2f, life);
                    if (crescent != null)
                    {
                        crescent.Velocity = dir * speed;
                        Place(crescent);
                    }
                    for (int k = 0; k < across; k++)
                    {
                        float yaw = Mathf.Lerp(-half, half, across > 1 ? k / (across - 1f) : 0.5f);
                        Vector3 d = Quaternion.AngleAxis(yaw, Vector3.up) * dir;
                        Streak(s, mid + Vector3.up * Random.Range(-0.4f, 0.4f), d * (speed * Random.Range(0.9f, 1f)), 0f, 0.07f, 4f, life, s.AccentColor);
                    }
                    break;
                }
                default:
                {
                    // A rolling crest of water: blobs across the arc riding forward, droplets thrown off the top.
                    for (int k = 0; k < across; k++)
                    {
                        float yaw = Mathf.Lerp(-half, half, across > 1 ? k / (across - 1f) : 0.5f);
                        Vector3 d = Quaternion.AngleAxis(yaw, Vector3.up) * dir;
                        Vector3 at = ground + Vector3.up * 0.45f + d * 0.4f;
                        FireVfxPiece crest = Cloud(s, VfxSlot.Wave, at, d * speed, 0f, s.BurstSize * 1.3f, life);
                        if (crest != null && !crest.Billboard)
                        {
                            Vector3 flat = new Vector3(1.4f, 0.7f, 0.6f) * (s.BurstSize * 1.3f);
                            crest.Rotation = Quaternion.LookRotation(d);
                            crest.StartScale = flat * 0.4f;
                            crest.PeakScale = flat;
                            crest.EndScale = flat * 0.5f;
                            Place(crest);
                        }
                        Bit(s, at + Vector3.up * 0.3f, d * (speed * 0.7f) + Vector3.up * 3f, s.ChunkSize, life * 1.5f, floor);
                    }
                    FlatRing(s, ground, Quaternion.identity, 0.3f, range * 0.8f, life, s.CoreColor, s.FadeColor, false);
                    break;
                }
            }
        }

        // A straight line along the floor to exactly range: Earth's spikes erupt one after another, Air's blade flies, Water
        // splashes along it.
        internal void ElementLine(ElementId element, Vector3 origin, Vector3 direction, float range)
        {
            ElementVfxStyle s = ElementVfx.StyleOf(element);
            Vector3 dir = Flat(direction);
            range = Mathf.Max(0.5f, range);
            float life = Mathf.Max(0.05f, s.WaveLifetime);
            float floor = FloorBelow(origin);
            Vector3 ground = new Vector3(origin.x, floor, origin.z);
            switch (element)
            {
                case ElementId.Earth:
                {
                    int count = Mathf.Clamp(Mathf.RoundToInt(range / 0.55f), 4, 16);
                    float travel = life * 0.6f;     // the line reaches its end in this long
                    for (int i = 0; i < count; i++)
                    {
                        float distance = range * (i + 1f) / count;
                        Vector3 at = ground + dir * distance + Quaternion.AngleAxis(90f, Vector3.up) * dir * Random.Range(-0.15f, 0.15f);
                        float tall = Random.Range(0.6f, 1.1f);
                        // A spike thrust up through the floor, leaning a little, then sinking back.
                        Vector3 size = new Vector3(0.22f, tall, 0.22f);
                        const float drag = 14f;
                        FireVfxPiece spike = Chunk(s, VfxSlot.Rock, at - Vector3.up * (tall * 0.5f), Vector3.up * (tall * drag), drag,
                            size, size, new Vector3(0.1f, tall * 0.3f, 0.1f), 0.75f, life * 1.6f);
                        if (spike != null)
                        {
                            spike.Rotation = Quaternion.AngleAxis(Random.Range(-15f, 15f), Vector3.right) * Quaternion.AngleAxis(Random.Range(0f, 90f), Vector3.up);
                            spike.Gravity = 0f;
                            spike.HasFloor = false;
                            spike.SpinSpeed = 0f;
                        }
                        Delay(spike, travel * distance / range);
                        if (i % 2 == 0) Delay(Cloud(s, at + Vector3.up * 0.15f, Vector3.up * 0.5f, 2f, s.BurstSize * 0.8f, life), travel * distance / range);
                    }
                    Crack(s, ground + dir * (range * 0.5f), range, life * 2.5f, dir);
                    break;
                }
                case ElementId.Air:
                {
                    // A thin blade of air flying flat to the end of its reach, a streak either side.
                    Vector3 at = origin;
                    float speed = range / life;
                    FireVfxPiece blade = SpawnPiece(VfxShape.Sphere, s.RingKind, null, at, dir * speed, 0f,
                        new Vector3(0.5f, 0.03f, 0.3f), new Vector3(1.2f, 0.05f, 0.5f), new Vector3(1.4f, 0.02f, 0.2f), 0.3f, life, s.AccentColor, s.FadeColor);
                    if (blade != null)
                    {
                        blade.Rotation = Quaternion.LookRotation(dir);
                        Place(blade);
                    }
                    Vector3 side = Quaternion.AngleAxis(90f, Vector3.up) * dir;
                    Streak(s, at + side * 0.3f, dir * speed, 0f, 0.05f, 5f, life, s.AccentColor);
                    Streak(s, at - side * 0.3f, dir * speed, 0f, 0.05f, 5f, life, s.AccentColor);
                    break;
                }
                default:
                {
                    int count = Mathf.Clamp(Mathf.RoundToInt(range / 0.8f), 3, 12);
                    for (int i = 0; i < count; i++)
                    {
                        float distance = range * (i + 1f) / count;
                        Vector3 at = ground + dir * distance + Vector3.up * 0.2f;
                        Delay(Cloud(s, at, Vector3.up * 1.5f, 3f, s.BurstSize, life), life * 0.5f * distance / range);
                    }
                    break;
                }
            }
        }

        // A shell round the body: stone slabs leaning in (Earth), a shield of wind (Air), a bubble of water.
        internal void ElementDome(ElementId element, Vector3 center, float radius)
        {
            ElementVfxStyle s = ElementVfx.StyleOf(element);
            radius = Mathf.Max(0.5f, radius);
            float life = Mathf.Max(0.05f, s.DomeLifetime);
            float floor = FloorBelow(center + Vector3.up * 0.5f);
            Vector3 ground = new Vector3(center.x, floor, center.z);
            switch (element)
            {
                case ElementId.Earth:
                {
                    // A tent of stone: slabs thrust up round the body, leaning in.
                    int slabs = 8;
                    float tall = radius * 1.1f;
                    // J3-08: a full-height slab between the camera and the player would hide the player for a second: the
                    // slab(s) facing the camera rise only to a low wall.
                    Vector3 toCamera = hasCameraPosition ? new Vector3(cameraPosition.x - center.x, 0f, cameraPosition.z - center.z) : Vector3.zero;
                    bool cameraKnown = toCamera.sqrMagnitude > 0.01f;
                    if (cameraKnown) toCamera.Normalize();
                    float lowWallCos = Mathf.Cos(s.TentCameraSideAngle * Mathf.Deg2Rad);
                    for (int i = 0; i < slabs; i++)
                    {
                        float a = i * TwoPi / slabs;
                        var outward = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                        Vector3 at = ground + outward * (radius * 0.7f);
                        float slabTall = cameraKnown && Vector3.Dot(outward, toCamera) > lowWallCos ? tall * s.TentCameraSideHeight : tall;
                        Vector3 size = new Vector3(radius * 0.75f, slabTall, 0.25f);
                        const float drag = 14f;
                        FireVfxPiece slab = Chunk(s, VfxSlot.Rock, at - Vector3.up * (slabTall * 0.5f), Vector3.up * (slabTall * drag), drag,
                            size, size, size * 0.4f, 0.75f, life * 1.6f);
                        if (slab == null) continue;
                        slab.Rotation = Quaternion.LookRotation(outward) * Quaternion.AngleAxis(-22f, Vector3.right);
                        slab.Gravity = 0f;
                        slab.HasFloor = false;
                        slab.SpinSpeed = 0f;
                        Place(slab);
                    }
                    ElementStomp(element, center, radius);
                    break;
                }
                default:
                {
                    // A shell of air (light) or water (see-through) round the body, with rings spinning round it. J3-08: the
                    // shell is a faint inner glow (DomeShellShare of the reach, DomeShellAlpha), never a sphere the size of
                    // the reach, which the camera 3-4 m away would sit inside or right against (it washed out the frame);
                    // the spinning rings show the real reach.
                    Vector3 middle = ground + Vector3.up * (radius * 0.45f);
                    float shell = radius * Mathf.Clamp01(s.DomeShellShare);
                    if (hasCameraPosition && Vector3.Distance(cameraPosition, middle) < shell * 1.5f) shell = 0f;   // never round the camera
                    if (shell > 0f)
                    {
                        SpawnPiece(VfxShape.Sphere, s.BlobKind, null, middle, Vector3.zero, 0f, Uniform(shell * 0.7f), Uniform(shell * 2f),
                            Uniform(shell * 2.1f), 0.3f, life, Faded(s.MainColor, Mathf.Clamp01(s.DomeShellAlpha)), s.FadeColor);
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        FireVfxPiece ring = Band(s, VfxSlot.Swirl, middle, Quaternion.AngleAxis(i == 0 ? 25f : -25f, Vector3.right),
                            radius * 0.4f, radius * 1f, radius * 1.05f, s.RingHeight * 0.5f, life, s.CoreColor, s.FadeColor, 0.3f);
                        if (ring == null) continue;
                        ring.SpinAxis = Vector3.up;
                        ring.SpinSpeed = i == 0 ? s.SpinSpeed : -s.SpinSpeed;
                        Place(ring);
                    }
                    if (element == ElementId.Water)
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            Vector3 d = Random.onUnitSphere;
                            d.y = Mathf.Abs(d.y);
                            Bit(s, middle + d * radius, d * s.ChunkSpeed * 0.6f, s.ChunkSize, life * 1.4f, floor);
                        }
                    }
                    break;
                }
            }
        }

        // A swirl round the body out to radius: pieces orbit, widening and climbing, with a spinning ring for Water and Air.
        // onFloor false (J3-05: the air string's spiral kick, high above the floor): the swirl turns round 'center' itself
        // (the chest), with no floor ring, instead of being dropped to the floor metres under the juggle.
        internal void ElementVortex(ElementId element, Vector3 center, float radius)
        {
            ElementVortex(element, center, radius, true);
        }

        internal void ElementVortex(ElementId element, Vector3 center, float radius, bool onFloor)
        {
            ElementVfxStyle s = ElementVfx.StyleOf(element);
            radius = Mathf.Max(0.5f, radius);
            float life = Mathf.Max(0.05f, s.VortexLifetime);
            float floor = onFloor ? FloorBelow(center + Vector3.up * 0.5f) : center.y - 0.7f;
            Vector3 middle = new Vector3(center.x, floor + 0.7f, center.z);
            int count = 12;
            for (int i = 0; i < count; i++)
            {
                FireVfxPiece piece = element == ElementId.Earth && onFloor
                    ? Bit(s, middle, Vector3.zero, s.ChunkSize * Random.Range(0.8f, 1.3f), life, floor)
                    : element == ElementId.Earth
                        ? Cloud(s, middle, Vector3.zero, 0f, s.BurstSize * 0.7f, life)   // off the floor: Earth swirls dust only
                    : element == ElementId.Air
                        ? Streak(s, middle, Vector3.zero, 0f, 0.08f, 3f, life, s.AccentColor)
                        : Cloud(s, middle, Vector3.zero, 0f, s.BurstSize * 0.7f, life);
                if (piece == null) continue;
                piece.Orbits = true;
                piece.OrbitCenter = middle + Vector3.up * Random.Range(-0.4f, 0.4f);
                piece.OrbitRadius = radius * 0.35f;
                piece.OrbitGrowth = radius * 0.65f / life;
                piece.OrbitAngle = i * TwoPi / count;
                piece.OrbitSpeed = 9f;
                piece.OrbitRise = 0.8f;
                piece.Gravity = 0f;
                piece.HasFloor = false;
                if (element == ElementId.Air && !piece.Billboard)
                {
                    // Streaks lie along the way they whirl (each starts on its tangent and turns with the swirl).
                    piece.Rotation = Quaternion.LookRotation(new Vector3(Mathf.Cos(piece.OrbitAngle), 0f, -Mathf.Sin(piece.OrbitAngle)));
                    piece.StartScale.z *= 3f;
                    piece.PeakScale.z *= 3f;
                    piece.EndScale.z *= 3f;
                    piece.SpinAxis = Vector3.up;
                    piece.SpinSpeed = piece.OrbitSpeed * Mathf.Rad2Deg;   // turning with the orbit keeps them tangent
                }
                Place(piece);
            }
            if (element == ElementId.Earth)
            {
                if (onFloor) ElementRing(element, new Vector3(center.x, floor, center.z), radius * 0.8f);
                return;
            }
            FireVfxPiece ring = Band(s, VfxSlot.Swirl, middle, Quaternion.identity, radius * 0.25f, radius * 1f, radius * 1.05f, s.RingHeight,
                life, s.CoreColor, s.FadeColor, 0.4f);
            if (ring != null)
            {
                ring.SpinAxis = Vector3.up;
                ring.SpinSpeed = s.SpinSpeed;
            }
        }

        // Small fast pieces flying along direction to exactly range: ice darts, stone chips, cutting air.
        internal void ElementShards(ElementId element, Vector3 origin, Vector3 direction, float range)
        {
            ElementVfxStyle s = ElementVfx.StyleOf(element);
            Vector3 dir = SafeDirection(direction, Vector3.forward);
            float life = Mathf.Max(0.05f, s.ConeLifetime);
            float speed = Mathf.Max(0.5f, range) / life;
            for (int i = 0; i < 5; i++)
            {
                Vector3 d = (dir + Random.insideUnitSphere * 0.08f).normalized;
                Vector3 velocity = d * (speed * Random.Range(0.9f, 1f));
                if (element == ElementId.Earth)
                {
                    FireVfxPiece chip = Chunk(s, VfxSlot.Debris, origin, velocity, 0f, Uniform(s.ChunkSize), Uniform(s.ChunkSize), Uniform(s.ChunkSize * 0.6f), 0.8f, life);
                    if (chip == null) continue;
                    chip.Gravity = 0f;
                    chip.HasFloor = false;
                    continue;
                }
                // Water: slivers of ice; Air: cutting streaks.
                VfxSlot slot = element == ElementId.Water ? VfxSlot.Shard : VfxSlot.Streak;
                FireVfxPiece shard = Picture(s, slot, origin, velocity, 0f, Uniform(0.04f), Uniform(0.09f), Uniform(0.05f), 0.2f, life,
                    s.AccentColor, Faded(s.AccentColor, 0f), s.ChunkKind);
                Stretch(shard, d, 6f);
            }
            ElementMuzzle(element, origin, dir);
        }

        // A ground impact round the feet: Earth cracks the floor, throws dust and rock; Water splashes; Air puffs out.
        internal void ElementStomp(ElementId element, Vector3 feet, float radius)
        {
            ElementVfxStyle s = ElementVfx.StyleOf(element);
            radius = Mathf.Max(0.5f, radius);
            float floor = FloorBelow(feet + Vector3.up * 0.3f);
            Vector3 ground = new Vector3(feet.x, floor, feet.z);
            float life = s.RingLifetime;
            ElementRing(element, ground, radius);
            int clouds = 6;
            for (int i = 0; i < clouds; i++)
            {
                float a = (i + Random.value * 0.5f) * TwoPi / clouds;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Cloud(s, ground + d * (radius * 0.4f) + Vector3.up * 0.25f, d * (radius / life * 0.5f), 3f, s.BurstSize * 1.1f, life * 1.3f);
            }
            if (element != ElementId.Earth) return;
            // Rock torn out of the floor round the impact.
            for (int i = 0; i < 6; i++)
            {
                float a = Random.value * TwoPi;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Bit(s, ground + d * Random.Range(0.3f, radius * 0.8f) + Vector3.up * 0.1f, Vector3.up * Random.Range(3f, 5.5f) + d * 1.5f,
                    s.ChunkSize * Random.Range(1f, 1.8f), life, floor);
            }
        }

        // ---- Build 05 presentation ---------------------------------------------------------------------------------

        // Changing element: the old element blows off the body, the new one washes up it from a ring at the feet.
        internal void SwitchFlourish(ElementId from, ElementId to, Vector3 chest, Vector3 feet)
        {
            SwitchFlourish(from, to, chest, feet, false);
        }

        // airborne: Earth washes up as dust only. On the ground Earth's rock rises from the floor ring at the feet, never
        // out of the chest (stone comes from the ground: spec 7 / 8.1).
        internal void SwitchFlourish(ElementId from, ElementId to, Vector3 chest, Vector3 feet, bool airborne)
        {
            ElementVfxStyle next = ElementVfx.StyleOf(to);
            VfxMaterialKind kind = to == ElementId.Earth ? VfxMaterialKind.AlphaBlend : next.RingKind;
            Color ring = to == ElementId.Earth ? next.CloudColor : next.CoreColor;
            float life = 0.35f;
            SpawnPiece(VfxShape.RingBand, kind, null, feet + Vector3.up * GroundOffset, Vector3.zero, 0f, new Vector3(0.4f, 0.3f, 0.4f),
                new Vector3(2.6f, 0.2f, 2.6f), new Vector3(3f, 0f, 3f), 0.5f, life, ring, next.FadeColor);
            // A band sweeping up the body from the feet to the head.
            SpawnPiece(VfxShape.RingBand, kind, null, feet, Vector3.up * 5f, 2f, new Vector3(1.1f, 0.15f, 1.1f),
                new Vector3(1.2f, 0.3f, 1.2f), new Vector3(0.6f, 0f, 0.6f), 0.4f, life, ring, next.FadeColor);
            if (from != ElementId.None && from != to)
            {
                ElementVfxStyle previous = ElementVfx.StyleOf(from);
                VfxMaterialKind oldKind = from == ElementId.Earth ? VfxMaterialKind.AlphaBlend : previous.BlobKind;
                SpawnPiece(VfxShape.Sphere, oldKind, null, chest, Vector3.zero, 0f, Uniform(0.5f), Uniform(1.3f), Uniform(1.6f), 0.25f, life * 0.8f,
                    Faded(from == ElementId.Earth ? previous.CloudColor : previous.MainColor, 0.35f), previous.FadeColor);
            }
            if (to != ElementId.Earth) ElementVfx.Burst(to, chest, Vector3.up, 0.6f);
            else if (airborne) ElementVfx.Dust(to, chest, Vector3.up, 0.6f);
            else ElementVfx.Burst(to, feet + Vector3.up * GroundOffset, Vector3.up, 0.6f);
        }

        // A thin ring flashing outwards round position, facing the camera.
        internal void BeatAccent(Vector3 position, float scale, Color color, float lifetime)
        {
            if (!(scale > 0f)) return;
            Color end = color;
            end.a = 0f;
            bool pictured = ElementVfx.TryGetTexture(ElementId.None, VfxSlot.BeatRing, out Texture2D picture, out bool hasAlpha);
            FireVfxPiece ring = SpawnPiece(pictured ? VfxShape.Quad : VfxShape.FlatRing,
                pictured && hasAlpha ? VfxMaterialKind.AlphaBlend : VfxMaterialKind.Additive, picture, position, Vector3.zero, 0f,
                Uniform(0.25f * scale), Uniform(0.85f * scale), Uniform(1f * scale), 0.45f, Mathf.Max(0.02f, lifetime), color, end);
            if (ring == null) return;
            ring.Billboard = true;
            ring.Rotation = pictured ? Quaternion.identity : Quaternion.Euler(90f, 0f, 0f);
            Place(ring);
        }

        // ---- Emitters and glows ------------------------------------------------------------------------------------

        void UpdateElementEmitter(FireVfxEmitter e, float dt)
        {
            ElementVfxStyle s = ElementVfx.StyleOf(e.Element);
            float rate;
            switch (e.Kind)
            {
                case FireVfxEmitterKind.FootJet: rate = s.JetRate; break;
                case FireVfxEmitterKind.Embers: rate = s.LaunchTrailRate; break;
                case FireVfxEmitterKind.Afterimage: rate = s.AfterimageRate; break;
                default: rate = s.LimbRate; break;
            }
            e.Accumulator += rate * dt;
            Vector3 at = e.Follow.position;
            while (e.Accumulator >= 1f)
            {
                e.Accumulator -= 1f;
                switch (e.Kind)
                {
                    case FireVfxEmitterKind.FootJet:
                    {
                        Vector3 d = SafeDirection(e.Direction, Vector3.down);
                        Vector3 v = (d + Random.insideUnitSphere * 0.3f).normalized * (s.JetSpeed * Random.Range(0.7f, 1.1f));
                        if (e.Element == ElementId.Air) Streak(s, at, v, 4f, s.JetSize * 0.4f, 3f, s.JetLifetime, s.AccentColor);
                        else Stretch(Cloud(s, at, v, 5f, s.JetSize, s.JetLifetime), d, 1.5f);
                        break;
                    }
                    case FireVfxEmitterKind.Embers:
                    {
                        // What trails a launched enemy: droplets raining off it, dust and grit, wisps of air.
                        Vector3 v = Random.insideUnitSphere * 0.8f + Vector3.up * 0.3f;
                        // Earth (J3-07): dust and grit only, never rock shed by a body in the air.
                        if (e.Element == ElementId.Air) Streak(s, at + Random.insideUnitSphere * 0.2f, v, 1f, 0.05f, 3f, s.LimbLifetime * 2f, s.AccentColor);
                        else if (ElementFxRules.StoneFromFloor(e.Element) || Random.value < 0.5f)
                            Cloud(s, at + Random.insideUnitSphere * 0.15f, v, 2f, s.LimbSize * 2.5f, s.LimbLifetime * 2f);
                        else Bit(s, at, v, s.ChunkSize * 0.6f, s.LimbLifetime * 3f, at.y - 3f);
                        break;
                    }
                    case FireVfxEmitterKind.Afterimage:
                        Afterimage(s, at);
                        break;
                    case FireVfxEmitterKind.LimbDust:
                        // Earth's limb in the air: dust only.
                        Cloud(s, at, Vector3.up * 0.4f + Random.insideUnitSphere * 0.4f, 3f, s.LimbSize, s.LimbLifetime);
                        break;
                    default:
                    {
                        // The element on the striking limb: water coiling off it, dust off an earthbender's fist (no rock on
                        // the body: stone is drawn from the ground, spec 7; FLAG for David), wisps of air.
                        Vector3 v = Vector3.up * 0.5f + Random.insideUnitSphere * 0.5f;
                        if (e.Element == ElementId.Air) Streak(s, at, v, 3f, s.LimbSize * 0.5f, 3f, s.LimbLifetime, s.AccentColor);
                        else if (e.Element == ElementId.Earth) Cloud(s, at, Vector3.up * 0.4f + Random.insideUnitSphere * 0.4f, 3f, s.LimbSize, s.LimbLifetime);
                        else Cloud(s, at, v, 3f, s.LimbSize, s.LimbLifetime);
                        break;
                    }
                }
            }
        }

        // A see-through copy of the body where it just was: a soft upright shape round the chest, fading fast.
        void Afterimage(ElementVfxStyle s, Vector3 chest)
        {
            VfxMaterialKind kind = s.Element == ElementId.Air || s.Element == ElementId.Fire ? VfxMaterialKind.Additive : VfxMaterialKind.AlphaBlend;
            Color color = s.Element == ElementId.Earth ? s.CloudColor : s.MainColor;
            color.a = s.AfterimageAlpha;
            var body = new Vector3(0.55f, 1.55f, 0.4f);
            SpawnPiece(VfxShape.Sphere, kind, null, chest - Vector3.up * 0.3f, Vector3.zero, 0f, body, body, body * 0.85f, 0.05f,
                s.AfterimageLifetime, color, Faded(color, 0f));
        }

        // The charge glow of Water (a ball of water that wobbles), Earth (a ball of dust gathering, churning: never a stone on
        // the fist, J3-07) and Air (a swirling ball).
        void UpdateElementGlow(FireVfxPiece glow, float dt)
        {
            ElementVfxStyle s = ElementVfx.StyleOf(glow.Element);
            FireVfxStyle fire = FireVfx.Style;
            float size = Mathf.Lerp(s.ChargeMinSize, s.ChargeMaxSize, glow.Level);
            if (glow.Stopping)
            {
                glow.StopAge += dt;
                float remaining = fire.ChargeStopTime > 0f ? 1f - glow.StopAge / fire.ChargeStopTime : 0f;
                if (remaining <= 0f)
                {
                    ReleasePiece(glow);
                    return;
                }
                size *= remaining;
            }
            glow.SpinAngle += glow.SpinSpeed * (0.3f + glow.Level) * dt;
            Vector3 scale;
            Color color;
            switch (glow.Element)
            {
                case ElementId.Earth:
                {
                    float churn = 0.08f * Mathf.Sin(time * 13f);
                    scale = new Vector3(size * (1f + churn), size * (0.9f - churn), size * (1f + churn));
                    color = Color.Lerp(s.CloudColor, s.CoreColor, glow.Level * 0.4f);
                    color.a = Mathf.Lerp(0.35f, 0.7f, glow.Level);
                    break;
                }
                case ElementId.Air:
                {
                    float flicker = 1f + 0.1f * Mathf.Sin(time * 31f);
                    scale = new Vector3(size * flicker, size * 0.8f, size * flicker);
                    color = Color.Lerp(s.MainColor, s.CoreColor, glow.Level);
                    break;
                }
                default:
                {
                    // Water wobbles: it is held, not burning.
                    float wobble = 0.12f * Mathf.Sin(time * 9f);
                    scale = new Vector3(size * (1f + wobble), size * (1f - wobble), size * (1f + wobble));
                    color = Color.Lerp(s.MainColor, s.CoreColor, glow.Level);
                    break;
                }
            }
            glow.Position = glow.Follow.position;
            glow.Transform.SetPositionAndRotation(glow.Position, Quaternion.AngleAxis(glow.SpinAngle, glow.SpinAxis));
            glow.Transform.localScale = scale;
            glow.SetColor(color);
        }

        // ---- Building blocks ---------------------------------------------------------------------------------------

        // The element's main mass, grown and faded: a splash of water, a dust cloud, a puff of air.
        FireVfxPiece Cloud(ElementVfxStyle s, Vector3 position, Vector3 velocity, float drag, float size, float life)
        {
            return Cloud(s, CloudSlot(s.Element), position, velocity, drag, size, life);
        }

        FireVfxPiece Cloud(ElementVfxStyle s, VfxSlot slot, Vector3 position, Vector3 velocity, float drag, float size, float life)
        {
            switch (s.Element)
            {
                case ElementId.Earth:
                    // Dust keeps spreading as it fades.
                    return Picture(s, slot, position, velocity, drag, Uniform(size * 0.4f), Uniform(size), Uniform(size * 1.35f), 0.4f, life,
                        s.CloudColor, s.FadeColor, s.BlobKind);
                case ElementId.Air:
                    return Picture(s, slot, position, velocity, drag, Uniform(size * 0.3f), Uniform(size), Uniform(size * 1.2f), 0.3f, life,
                        s.MainColor, s.FadeColor, s.BlobKind);
                default:
                    return Picture(s, slot, position, velocity, drag, Uniform(size * 0.35f), Uniform(size), Uniform(size * 0.6f), 0.3f, life,
                        s.CoreColor, s.FadeColor, s.BlobKind);
            }
        }

        // Water's spray hanging in the air after a big splash.
        void Mist(ElementVfxStyle s, Vector3 position, float size, float life)
        {
            Picture(s, VfxSlot.Mist, position, Vector3.up * 0.4f, 1f, Uniform(size * 0.5f), Uniform(size), Uniform(size * 1.2f), 0.5f, life,
                s.CloudColor, s.FadeColor, s.BlobKind);
        }

        // A thrown piece: a droplet that falls and stops on the floor, a rock that tumbles, bounces and settles, a streak of
        // air that slows and fades. floorY: where the floor is under it.
        FireVfxPiece Bit(ElementVfxStyle s, Vector3 position, Vector3 velocity, float size, float life, float floorY)
        {
            switch (s.Element)
            {
                case ElementId.Earth:
                {
                    VfxSlot slot = size < s.ChunkSize * 0.8f ? VfxSlot.Gravel : VfxSlot.Rock;
                    var shape = new Vector3(size * Random.Range(0.8f, 1.2f), size * Random.Range(0.7f, 1.1f), size * Random.Range(0.8f, 1.2f));
                    FireVfxPiece rock = Chunk(s, slot, position, velocity, 0f, shape, shape, Vector3.zero, 0.8f, Mathf.Max(life, s.DebrisLifetime));
                    if (rock != null)
                    {
                        rock.FloorY = floorY + size * 0.5f;
                        rock.Rotation = Random.rotationUniform;
                        Place(rock);
                    }
                    return rock;
                }
                case ElementId.Air:
                    return Streak(s, position, velocity, 5f, size, 3f, life, s.AccentColor);
                default:
                {
                    FireVfxPiece drop = Picture(s, VfxSlot.Droplet, position, velocity, 0.5f, Uniform(size), Uniform(size), Uniform(size * 0.5f), 0.6f,
                        Mathf.Max(life, s.DebrisLifetime), s.AccentColor, Faded(s.MainColor, 0f), s.ChunkKind);
                    if (drop == null) return null;
                    drop.Gravity = s.Gravity;
                    drop.HasFloor = true;
                    drop.FloorY = floorY + FloorRestOffset;
                    drop.Bounce = s.Bounce;
                    return drop;
                }
            }
        }

        // A solid piece (Earth): a lit cube, or the slot's picture tumbling, under gravity, landing on the floor.
        FireVfxPiece Chunk(ElementVfxStyle s, VfxSlot slot, Vector3 position, Vector3 velocity, float drag, Vector3 startScale, Vector3 peakScale,
            Vector3 endScale, float peakAt, float life)
        {
            bool pictured = ElementVfx.TryGetTexture(s.Element, slot, out Texture2D picture, out bool hasAlpha);
            Color color = Color.Lerp(s.MainColor, s.CoreColor, Random.Range(0f, 0.25f));
            color.a = 1f;
            FireVfxPiece piece = pictured
                ? SpawnPiece(VfxShape.Quad, hasAlpha ? VfxMaterialKind.AlphaBlend : VfxMaterialKind.Additive, picture, position, velocity, drag,
                    startScale, peakScale, endScale, peakAt, life, Color.white, Color.white)
                : SpawnPiece(VfxShape.Cube, s.ChunkKind, null, position, velocity, drag, startScale, peakScale, endScale, peakAt, life, color, color);
            if (piece == null) return null;
            piece.Billboard = pictured;
            piece.Gravity = s.Gravity;
            piece.HasFloor = true;
            piece.FloorY = position.y;
            piece.Bounce = s.Bounce;
            piece.SpinAxis = Random.onUnitSphere;
            piece.SpinSpeed = s.SpinSpeed * Random.Range(0.5f, 1f);
            return piece;
        }

        // A streak of light along velocity (Air; water's ice uses Picture).
        FireVfxPiece Streak(ElementVfxStyle s, Vector3 position, Vector3 velocity, float drag, float size, float stretch, float life, Color color)
        {
            FireVfxPiece streak = Picture(s, VfxSlot.Streak, position, velocity, drag, Uniform(size * 0.6f), Uniform(size), Uniform(size * 0.3f), 0.25f,
                life, color, Faded(color, 0f), s.RingKind);
            if (streak != null && !streak.Billboard && velocity.sqrMagnitude > 1e-4f) Stretch(streak, velocity, stretch);
            return streak;
        }

        // A sphere in the element's look, or the slot's picture facing the camera when David has added one.
        FireVfxPiece Picture(ElementVfxStyle s, VfxSlot slot, Vector3 position, Vector3 velocity, float drag, Vector3 startScale, Vector3 peakScale,
            Vector3 endScale, float peakAt, float life, Color startColor, Color endColor, VfxMaterialKind kind)
        {
            bool pictured = ElementVfx.TryGetTexture(s.Element, slot, out Texture2D picture, out bool hasAlpha);
            FireVfxPiece piece = SpawnPiece(pictured ? VfxShape.Quad : VfxShape.Sphere,
                pictured ? (hasAlpha ? VfxMaterialKind.AlphaBlend : VfxMaterialKind.Additive) : kind,
                picture, position, velocity, drag, startScale, peakScale, endScale, peakAt, life, startColor, endColor);
            if (piece == null) return null;
            piece.Billboard = pictured;
            if (pictured)
            {
                piece.Rotation = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.forward);   // pictures don't all line up
                Place(piece);
            }
            return piece;
        }

        // A ring: a band (a wall that spreads and sinks) or, with the slot's picture, that picture lying in the ring's plane.
        // rotation turns the ring's axis (up = lying on the floor). Radii as start, peak, end; height of the wall.
        // CONTRACT (J3-04): the radii are WORLD radii (the meshes have radius 0.5 and are scaled by radius x 2 here), so a
        // caller drawing a move's reach passes the reach itself, never reach x 2.
        FireVfxPiece Band(ElementVfxStyle s, VfxSlot slot, Vector3 center, Quaternion rotation, float startRadius, float peakRadius, float endRadius,
            float height, float life, Color startColor, Color endColor, float peakAt)
        {
            bool pictured = ElementVfx.TryGetTexture(s.Element, slot, out Texture2D picture, out bool hasAlpha);
            FireVfxPiece band = pictured
                ? SpawnPiece(VfxShape.Quad, hasAlpha ? VfxMaterialKind.AlphaBlend : VfxMaterialKind.Additive, picture, center, Vector3.zero, 0f,
                    new Vector3(startRadius * 2f, startRadius * 2f, 1f), new Vector3(peakRadius * 2f, peakRadius * 2f, 1f),
                    new Vector3(endRadius * 2f, endRadius * 2f, 1f), peakAt, life, startColor, endColor)
                : SpawnPiece(VfxShape.RingBand, s.RingKind, null, center, Vector3.zero, 0f, new Vector3(startRadius * 2f, height, startRadius * 2f),
                    new Vector3(peakRadius * 2f, height * 0.8f, peakRadius * 2f), new Vector3(endRadius * 2f, 0f, endRadius * 2f), peakAt, life,
                    startColor, endColor);
            if (band == null) return null;
            // The flat quad faces +Z; turn it to lie in the ring's plane.
            band.Rotation = pictured ? rotation * Quaternion.Euler(90f, 0f, 0f) : rotation;
            Place(band);
            return band;
        }

        // A ring spreading out across the strike's path, facing along it (a splash crown, a ring of pressure ahead of a
        // palm). Flat, so the camera behind the player sees it whole; a band seen along its axis would vanish edge-on.
        FireVfxPiece Shockwave(ElementVfxStyle s, Vector3 center, Vector3 direction, float startRadius, float endRadius, float life)
        {
            Quaternion facing = Quaternion.FromToRotation(Vector3.up, SafeDirection(direction, Vector3.forward));
            if (ElementVfx.TryGetTexture(s.Element, VfxSlot.Ring, out _, out _))
                return Band(s, VfxSlot.Ring, center, facing, startRadius, endRadius * 0.9f, endRadius, 0f, life, s.CoreColor, s.FadeColor, 0.4f);
            return FlatRing(s, center, facing, startRadius, endRadius, life, s.CoreColor, s.FadeColor, false);
        }

        // A thin outline ring in a plane (a ripple on the floor; billboard = facing the camera).
        FireVfxPiece FlatRing(ElementVfxStyle s, Vector3 center, Quaternion rotation, float startRadius, float endRadius, float life, Color startColor,
            Color endColor, bool billboard)
        {
            FireVfxPiece ring = SpawnPiece(VfxShape.FlatRing, s.RingKind, null, center, Vector3.zero, 0f, Uniform(startRadius * 2f),
                Uniform(endRadius * 1.8f), Uniform(endRadius * 2f), 0.6f, life, startColor, endColor);
            if (ring == null) return null;
            ring.Billboard = billboard;
            ring.Rotation = rotation;
            Place(ring);
            return ring;
        }

        // Cracks in the floor (Earth): the 'crack' picture laid flat, or dark splinters radiating from the centre.
        // along: a crack that follows a line instead (spikes), zero = a star.
        void Crack(ElementVfxStyle s, Vector3 ground, float size, float life, Vector3 along = default)
        {
            Vector3 at = ground + Vector3.up * GroundOffset;
            Color dark = s.AccentColor;
            Color gone = Faded(dark, 0f);
            if (ElementVfx.TryGetTexture(ElementId.Earth, VfxSlot.Crack, out Texture2D picture, out bool hasAlpha))
            {
                bool line = along.sqrMagnitude > 1e-4f;
                Vector3 extent = line ? new Vector3(size * 0.35f, size, 1f) : new Vector3(size, size, 1f);
                FireVfxPiece decal = SpawnPiece(VfxShape.Quad, hasAlpha ? VfxMaterialKind.AlphaBlend : VfxMaterialKind.Additive, picture, at, Vector3.zero,
                    0f, extent * 0.6f, extent, extent, 0.15f, life, Color.white, new Color(1f, 1f, 1f, 0f));
                if (decal == null) return;
                Quaternion turn = line ? Quaternion.LookRotation(along) : Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up);
                decal.Rotation = turn * Quaternion.Euler(90f, 0f, 0f);
                Place(decal);
                return;
            }
            if (along.sqrMagnitude > 1e-4f)
            {
                Vector3 length = new Vector3(0.06f, 0.02f, size);
                FireVfxPiece seam = SpawnPiece(VfxShape.Cube, VfxMaterialKind.AlphaBlend, null, at, Vector3.zero, 0f, new Vector3(0.06f, 0.02f, size * 0.3f),
                    length, length, 0.2f, life, dark, gone);
                if (seam == null) return;
                seam.Rotation = Quaternion.LookRotation(along);
                Place(seam);
                return;
            }
            int splinters = 7;
            for (int i = 0; i < splinters; i++)
            {
                float yaw = (i + Random.Range(-0.3f, 0.3f)) * 360f / splinters;
                Vector3 d = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.forward;
                float reach = size * 0.5f * Random.Range(0.6f, 1f);
                var full = new Vector3(Random.Range(0.04f, 0.08f), 0.02f, reach);
                FireVfxPiece splinter = SpawnPiece(VfxShape.Cube, VfxMaterialKind.AlphaBlend, null, at + d * (reach * 0.5f), Vector3.zero, 0f,
                    new Vector3(full.x, full.y, reach * 0.2f), full, full, 0.15f, life, dark, gone);
                if (splinter == null) continue;
                splinter.Rotation = Quaternion.LookRotation(d);
                Place(splinter);
            }
        }

        static VfxSlot CloudSlot(ElementId element)
        {
            switch (element)
            {
                case ElementId.Earth: return VfxSlot.Dust;
                case ElementId.Air: return VfxSlot.Puff;
                default: return VfxSlot.Splash;
            }
        }

        // Holds a piece back for seconds before it appears (a line of spikes erupting in turn).
        void Delay(FireVfxPiece piece, float seconds)
        {
            if (piece == null || !(seconds > 0f)) return;
            piece.Age = -seconds;
            Place(piece);
        }

        // The floor height under a point (or the point's own height when nothing is found within 15 m).
        static float FloorBelow(Vector3 position)
        {
            return GroundBelow(position).y;
        }

        static Vector3 Flat(Vector3 direction)
        {
            return SafeDirection(new Vector3(direction.x, 0f, direction.z), Vector3.forward);
        }

        static Vector3 Uniform(float size)
        {
            return new Vector3(size, size, size);
        }

        static Color Faded(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
