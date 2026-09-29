using UnityEngine;

namespace VaatusRevenge
{
    // The pooled building blocks FireVfxRunner animates. Pooling means effects reuse a fixed set of
    // objects instead of creating and destroying GameObjects on every punch, which would cause hitches
    // from garbage collection in the middle of a fight.

    // A sphere or ring band that grows, cools down (changes colour) and shrinks away.
    internal sealed class FireVfxPiece
    {
        public GameObject GameObject;
        public Transform Transform;
        public MeshFilter Filter;
        public Material Material;
        public bool Active;
        public int Generation;

        public bool Persistent;          // charge glow: lives until stopped
        public float Age;
        public float Lifetime;
        public float PeakAt;             // 0..1: when it reaches its biggest size
        public Vector3 Position;
        public Vector3 Velocity;
        public float Drag;               // per second: how quickly flying blobs slow down
        public Quaternion Rotation;
        public Vector3 StartScale;
        public Vector3 PeakScale;
        public Vector3 EndScale;
        public Color StartColor;
        public Color EndColor;

        public Transform Follow;         // charge glow anchor
        public float Level;
        public bool Stopping;
        public float StopAge;

        Color lastColor;
        bool colorWritten;

        public bool IsAlive => Active && GameObject != null;

        public void SetColor(Color color)
        {
            if (colorWritten && color == lastColor) return;
            GreyboxShapes.SetBaseColor(Material, color);
            lastColor = color;
            colorWritten = true;
        }

        public void ResetColorCache()
        {
            colorWritten = false;
        }
    }

    // A TrailRenderer that follows a transform for a while, then stops emitting and fades.
    internal sealed class FireVfxTrail
    {
        public GameObject GameObject;
        public Transform Transform;
        public TrailRenderer Renderer;
        public bool Active;
        public int Generation;
        public Transform Follow;
        public bool Emitting;
        public bool Timed;               // false: emits until stopped
        public float EmitRemaining;
        public float FadeRemaining;

        public bool IsAlive => Active && GameObject != null;
    }

    // A short-lived point light for big effects. Only a handful exist, reused oldest-first.
    internal sealed class FireVfxLight
    {
        public GameObject GameObject;
        public Light Light;
        public bool Active;
        public float Age;
        public float Lifetime;
        public float PeakIntensity;
    }

    public enum FireVfxEmitterKind { LimbFlame, FootJet, Embers }

    // Spawns small flames at a moving point for a while (fire on a fist, jets from the feet, embers).
    internal sealed class FireVfxEmitter
    {
        public bool Active;
        public int Generation;
        public Transform Follow;
        public FireVfxEmitterKind Kind;
        public Vector3 Direction;
        public bool Timed;
        public float Remaining;
        public float Accumulator;
    }

    // A lash of flame drawn as a line that sweeps across an arc (Fire Whip).
    internal sealed class FireVfxWhip
    {
        public const int Points = 20;

        public GameObject GameObject;
        public LineRenderer Line;
        public bool Active;
        public int Generation;
        public Transform Hand;
        public Vector3 Origin;
        public float Yaw;
        public float Range;
        public float Arc;
        public float Duration;
        public float Age;
    }
}
