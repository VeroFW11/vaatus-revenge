using UnityEngine;

namespace VaatusRevenge
{
    // The look of the grey-box fire effects, in one place. Purely visual: no gameplay numbers live here.
    // Colours are HDR (components above 1): the URP bloom pass only picks up pixels brighter than its
    // threshold (0.9 in this project), which is what makes the blobs read as glowing fire.
    [System.Serializable]
    public class FireVfxStyle
    {
        [Header("Colours (HDR)")]
        [Tooltip("White-hot centre of bursts and explosions.")]
        public Color CoreColor = new Color(4f, 3.1f, 1.5f);
        [Tooltip("Main flame colour.")]
        public Color FlameColor = new Color(4f, 1.45f, 0.25f);
        [Tooltip("Deep orange-red that flames cool down to before vanishing.")]
        public Color EmberColor = new Color(2.2f, 0.42f, 0.05f);
        public Color LightColor = new Color(1f, 0.55f, 0.2f);
        [Tooltip("HitSpark multiplies the colour it's given by this.")]
        public float SparkIntensity = 3f;

        [Header("Lifetimes (seconds)")]
        public float BurstLifetime = 0.28f;
        public float ExplosionLifetime = 0.45f;
        public float RingLifetime = 0.4f;
        public float SparkLifetime = 0.16f;
        public float MuzzleLifetime = 0.12f;

        [Header("Sizes and speeds (metres, metres per second)")]
        public float BurstSize = 0.45f;
        public float BurstSpeed = 5f;
        public int BurstBlobs = 3;
        public int ExplosionEmbers = 6;
        public float EmberSpeed = 6f;
        public float EmberSize = 0.14f;
        public float RingHeight = 0.35f;
        public float SparkSize = 0.35f;
        public int SparkCount = 4;
        public float SparkSpeed = 4f;
        public float MuzzleSize = 0.35f;

        [Header("Lights (URP lights each object with at most 4 extra lights, so keep MaxLights small)")]
        [Range(0, 8)] public int MaxLights = 4;
        public float LightIntensity = 6f;
        [Tooltip("Explosion and ring lights reach this many times the effect radius.")]
        public float LightRangePerRadius = 3f;
        public float BurstLightRange = 4f;
        public float LightLifetime = 0.2f;
        [Tooltip("Bursts at least this big also flash a light.")]
        public float BurstLightMinScale = 0.9f;

        [Header("Trails")]
        public float TrailWidth = 0.22f;
        [Tooltip("How long each trail segment lingers, in seconds.")]
        public float TrailTime = 0.18f;
        public Color TrailColor = new Color(3f, 1.3f, 0.3f);

        [Header("Charge glow")]
        public float ChargeMinSize = 0.12f;
        public float ChargeMaxSize = 0.5f;
        public float ChargeFlickerRate = 20f;
        public float ChargeStopTime = 0.1f;

        [Header("Move effects (sizes scale with each move's reach, so what you see is what can hit)")]
        public float ConeLifetime = 0.34f;
        public float PillarLifetime = 0.6f;
        public int PillarBlobs = 10;
        public float PillarRingRadius = 1.1f;
        public int WheelBlobs = 14;
        [Tooltip("Width of the Fire Whip's lash, in metres.")]
        public float WhipWidth = 0.32f;

        [Header("Emitters (fire on the fists and feet, jets, embers)")]
        public float LimbFlameRate = 45f;
        public float LimbFlameSize = 0.16f;
        public float LimbFlameLifetime = 0.16f;
        public float JetRate = 70f;
        public float JetSize = 0.24f;
        public float JetSpeed = 6f;
        public float JetLifetime = 0.18f;
        public float EmberRate = 30f;
        public float EmberTrailLifetime = 0.55f;

        [Header("Pools")]
        [Tooltip("Most effect primitives alive at once, for all four elements together (rock and droplets lie on the floor a "
                 + "moment); the oldest is recycled beyond this.")]
        public int MaxPieces = 256;
        public int MaxTrails = 24;
        public int MaxEmitters = 16;
        public int MaxWhips = 4;
    }
}
