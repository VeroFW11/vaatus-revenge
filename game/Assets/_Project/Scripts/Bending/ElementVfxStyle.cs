using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The look of one element's grey-box effects, in one place (Fire's original moves keep FireVfxStyle). Purely visual:
    // no gameplay numbers live here, and every move effect is still sized from the move's own reach.
    //
    // The four elements are told apart by more than colour, so they read at a glance in a busy fight:
    //   Fire   glowing orange-red tongues of flame that bloom (FireVfxStyle; this style only colours Build 05 extras)
    //   Water  see-through blue blobs and ribbons, droplets that fall and splash, white foam at the core
    //   Earth  solid lit rock chunks that tumble under heavy gravity and land, brown dust clouds, dark cracks, no glow
    //   Air    pale additive streaks, spinning rings and swirls, no gravity and no lights: it is felt more than seen
    // Colours of the see-through kinds (Additive, AlphaBlend) fade through their alpha; FadeColor is where they end.
    [System.Serializable]
    public class ElementVfxStyle
    {
        public ElementId Element;

        [Header("Colours (HDR allowed: above 1 blooms)")]
        [Tooltip("The brightest part: foam, the white of a gust, sunlit dust.")]
        public Color CoreColor = Color.white;
        [Tooltip("The body of the effect: water blue, pale air, rock brown.")]
        public Color MainColor = Color.white;
        [Tooltip("What it fades to before it vanishes.")]
        public Color FadeColor = new Color(1f, 1f, 1f, 0f);
        [Tooltip("Mist, dust or a puff of air.")]
        public Color CloudColor = new Color(1f, 1f, 1f, 0.5f);
        [Tooltip("Ice for water, dark cracks for earth, bright streaks for air.")]
        public Color AccentColor = Color.white;
        [Tooltip("The pop where this element's hits connect (LDR; brightened for bloom like Fire's hit sparks).")]
        public Color HitSparkColor = Color.white;
        [Tooltip("Limb trails: colour at the limb and at the tail (alpha fades along it).")]
        public Color TrailHeadColor = Color.white;
        public Color TrailTailColor = new Color(1f, 1f, 1f, 0f);

        [Header("Materials")]
        [Tooltip("Blobs and splashes.")]
        public VfxMaterialKind BlobKind = VfxMaterialKind.AlphaBlend;
        [Tooltip("Chunks, chips and shards (Lit for rock).")]
        public VfxMaterialKind ChunkKind = VfxMaterialKind.AlphaBlend;
        [Tooltip("Rings, streaks and swirls.")]
        public VfxMaterialKind RingKind = VfxMaterialKind.AlphaBlend;
        [Tooltip("Limb trails and lashes.")]
        public VfxMaterialKind TrailKind = VfxMaterialKind.AlphaBlend;

        [Header("Motion")]
        [Tooltip("Metres per second squared on droplets and rocks (0 = they float).")]
        public float Gravity;
        [Tooltip("Share of the falling speed a piece keeps when it lands (0 = it stops dead).")]
        public float Bounce = 0.25f;
        [Tooltip("Degrees per second chunks and swirls spin at.")]
        public float SpinSpeed = 360f;

        [Header("Lifetimes (seconds)")]
        public float BurstLifetime = 0.32f;
        public float ExplosionLifetime = 0.5f;
        public float RingLifetime = 0.42f;
        public float SparkLifetime = 0.2f;
        public float ConeLifetime = 0.34f;
        public float PillarLifetime = 0.65f;
        public float WaveLifetime = 0.4f;
        public float DomeLifetime = 0.45f;
        public float VortexLifetime = 0.5f;
        [Tooltip("How long a rock or a droplet may lie on the floor before it shrinks away.")]
        public float DebrisLifetime = 0.9f;

        [Header("Sizes and speeds (metres, metres per second)")]
        public float BurstSize = 0.45f;
        public float BurstSpeed = 5f;
        public int BurstPieces = 5;
        public float ChunkSize = 0.16f;
        public float ChunkSpeed = 6f;
        public float RingHeight = 0.3f;
        public float SparkSize = 0.32f;
        public int SparkCount = 5;

        [Header("Trails")]
        public float TrailWidth = 0.22f;
        [Tooltip("How long each trail segment lingers, in seconds.")]
        public float TrailTime = 0.2f;
        public float WhipWidth = 0.3f;

        [Header("Emitters (on the limbs, the feet, a launched enemy, afterimages)")]
        public float LimbRate = 30f;
        public float LimbSize = 0.12f;
        public float LimbLifetime = 0.2f;
        public float JetRate = 45f;
        public float JetSize = 0.2f;
        public float JetSpeed = 4f;
        public float JetLifetime = 0.25f;
        public float LaunchTrailRate = 24f;
        [Tooltip("Copies of the body left behind per second during a slip-in dodge.")]
        public float AfterimageRate = 28f;
        public float AfterimageLifetime = 0.22f;
        [Tooltip("Alpha of an afterimage when it appears.")]
        [Range(0f, 1f)] public float AfterimageAlpha = 0.35f;

        [Header("Charge glow (the heavy's wind-up)")]
        public float ChargeMinSize = 0.12f;
        public float ChargeMaxSize = 0.5f;

        // Water: Tai Chi's soft, round power. Blue and see-through, droplets that fall and splash, white foam.
        public static ElementVfxStyle CreateWater()
        {
            return new ElementVfxStyle
            {
                Element = ElementId.Water,
                CoreColor = new Color(0.85f, 0.97f, 1.2f, 0.95f),
                MainColor = new Color(0.22f, 0.6f, 1.15f, 0.8f),
                FadeColor = new Color(0.1f, 0.35f, 0.85f, 0f),
                CloudColor = new Color(0.75f, 0.9f, 1f, 0.45f),
                AccentColor = new Color(0.8f, 0.97f, 1.3f, 0.95f),
                HitSparkColor = new Color(0.55f, 0.85f, 1f),
                TrailHeadColor = new Color(0.8f, 0.95f, 1f, 0.95f),
                TrailTailColor = new Color(0.15f, 0.45f, 1f, 0f),
                BlobKind = VfxMaterialKind.AlphaBlend,
                ChunkKind = VfxMaterialKind.AlphaBlend,
                RingKind = VfxMaterialKind.AlphaBlend,
                TrailKind = VfxMaterialKind.AlphaBlend,
                Gravity = 9.8f, Bounce = 0f, SpinSpeed = 0f,
                BurstLifetime = 0.38f, ExplosionLifetime = 0.55f, RingLifetime = 0.5f, ConeLifetime = 0.4f, PillarLifetime = 0.7f,
                WaveLifetime = 0.45f, DomeLifetime = 0.5f, VortexLifetime = 0.55f, DebrisLifetime = 0.35f,
                BurstSize = 0.42f, BurstSpeed = 4.5f, BurstPieces = 6, ChunkSize = 0.09f, ChunkSpeed = 5.5f, RingHeight = 0.32f,
                TrailWidth = 0.26f, TrailTime = 0.24f, WhipWidth = 0.34f,
                LimbRate = 32f, LimbSize = 0.1f, LimbLifetime = 0.26f, JetRate = 40f, JetSize = 0.18f, JetSpeed = 3.5f, JetLifetime = 0.3f,
                LaunchTrailRate = 22f, AfterimageRate = 26f, AfterimageLifetime = 0.26f, AfterimageAlpha = 0.32f,
                ChargeMinSize = 0.14f, ChargeMaxSize = 0.55f
            };
        }

        // Earth: Hung Gar's rooted weight. Solid rock that tumbles, lands and stays a moment; brown dust; dark cracks. No
        // glow and no lights: earth is heavy, not bright.
        public static ElementVfxStyle CreateEarth()
        {
            return new ElementVfxStyle
            {
                Element = ElementId.Earth,
                CoreColor = new Color(0.78f, 0.68f, 0.52f, 0.7f),
                MainColor = new Color(0.46f, 0.36f, 0.25f, 1f),
                FadeColor = new Color(0.5f, 0.42f, 0.32f, 0f),
                CloudColor = new Color(0.62f, 0.52f, 0.4f, 0.55f),
                AccentColor = new Color(0.1f, 0.08f, 0.06f, 0.85f),
                HitSparkColor = new Color(0.9f, 0.72f, 0.45f),
                TrailHeadColor = new Color(0.7f, 0.6f, 0.45f, 0.7f),
                TrailTailColor = new Color(0.5f, 0.42f, 0.32f, 0f),
                BlobKind = VfxMaterialKind.AlphaBlend,
                ChunkKind = VfxMaterialKind.Lit,
                RingKind = VfxMaterialKind.AlphaBlend,
                TrailKind = VfxMaterialKind.AlphaBlend,
                Gravity = 18f, Bounce = 0.25f, SpinSpeed = 420f,
                BurstLifetime = 0.45f, ExplosionLifetime = 0.6f, RingLifetime = 0.5f, ConeLifetime = 0.42f, PillarLifetime = 0.75f,
                WaveLifetime = 0.5f, DomeLifetime = 0.6f, VortexLifetime = 0.55f, DebrisLifetime = 0.9f,
                BurstSize = 0.55f, BurstSpeed = 3.5f, BurstPieces = 4, ChunkSize = 0.17f, ChunkSpeed = 6f, RingHeight = 0.28f,
                TrailWidth = 0.18f, TrailTime = 0.16f, WhipWidth = 0.28f,
                LimbRate = 18f, LimbSize = 0.07f, LimbLifetime = 0.35f, JetRate = 26f, JetSize = 0.28f, JetSpeed = 2f, JetLifetime = 0.45f,
                LaunchTrailRate = 18f, AfterimageRate = 22f, AfterimageLifetime = 0.2f, AfterimageAlpha = 0.3f,
                ChargeMinSize = 0.12f, ChargeMaxSize = 0.42f
            };
        }

        // Air: Baguazhang's turning. Pale streaks and spinning rings that add light, no gravity, no lights; quick to come
        // and go, so many small hits stay readable.
        public static ElementVfxStyle CreateAir()
        {
            return new ElementVfxStyle
            {
                Element = ElementId.Air,
                CoreColor = new Color(1.3f, 1.35f, 1.4f, 0.7f),
                MainColor = new Color(0.9f, 0.95f, 1.05f, 0.45f),
                FadeColor = new Color(0.8f, 0.88f, 1f, 0f),
                CloudColor = new Color(0.92f, 0.95f, 1f, 0.3f),
                AccentColor = new Color(1.4f, 1.45f, 1.5f, 0.85f),
                HitSparkColor = new Color(0.92f, 0.96f, 1f),
                TrailHeadColor = new Color(1f, 1f, 1f, 0.75f),
                TrailTailColor = new Color(0.85f, 0.92f, 1f, 0f),
                BlobKind = VfxMaterialKind.Additive,
                ChunkKind = VfxMaterialKind.Additive,
                RingKind = VfxMaterialKind.Additive,
                TrailKind = VfxMaterialKind.Additive,
                Gravity = 0f, Bounce = 0f, SpinSpeed = 540f,
                BurstLifetime = 0.26f, ExplosionLifetime = 0.42f, RingLifetime = 0.36f, ConeLifetime = 0.3f, PillarLifetime = 0.6f,
                WaveLifetime = 0.34f, DomeLifetime = 0.5f, VortexLifetime = 0.45f, DebrisLifetime = 0.3f,
                BurstSize = 0.4f, BurstSpeed = 7f, BurstPieces = 4, ChunkSize = 0.06f, ChunkSpeed = 9f, RingHeight = 0.18f,
                TrailWidth = 0.2f, TrailTime = 0.2f, WhipWidth = 0.22f,
                LimbRate = 26f, LimbSize = 0.09f, LimbLifetime = 0.2f, JetRate = 40f, JetSize = 0.16f, JetSpeed = 6f, JetLifetime = 0.2f,
                LaunchTrailRate = 26f, AfterimageRate = 32f, AfterimageLifetime = 0.2f, AfterimageAlpha = 0.28f,
                ChargeMinSize = 0.12f, ChargeMaxSize = 0.5f
            };
        }

        // Fire's colours for the Build 05 extras only (switch ring, afterimages, accents): Fire's moves keep FireVfxStyle.
        public static ElementVfxStyle CreateFire()
        {
            return new ElementVfxStyle
            {
                Element = ElementId.Fire,
                CoreColor = new Color(4f, 3.1f, 1.5f, 1f),
                MainColor = new Color(4f, 1.45f, 0.25f, 0.9f),
                FadeColor = new Color(2.2f, 0.42f, 0.05f, 0f),
                CloudColor = new Color(0.25f, 0.2f, 0.18f, 0.4f),
                AccentColor = new Color(4f, 2.2f, 0.6f, 1f),
                HitSparkColor = new Color(1f, 0.55f, 0.15f),
                TrailHeadColor = new Color(1f, 0.95f, 0.7f, 1f),
                TrailTailColor = new Color(0.8f, 0.15f, 0.02f, 0f),
                BlobKind = VfxMaterialKind.Additive,
                ChunkKind = VfxMaterialKind.Additive,
                RingKind = VfxMaterialKind.Additive,
                TrailKind = VfxMaterialKind.Additive,
                Gravity = 0f, Bounce = 0f, SpinSpeed = 0f,
                AfterimageRate = 26f, AfterimageLifetime = 0.2f, AfterimageAlpha = 0.3f
            };
        }

        public static ElementVfxStyle CreateFor(ElementId element)
        {
            switch (element)
            {
                case ElementId.Water: return CreateWater();
                case ElementId.Earth: return CreateEarth();
                case ElementId.Air: return CreateAir();
                default: return CreateFire();
            }
        }
    }
}
