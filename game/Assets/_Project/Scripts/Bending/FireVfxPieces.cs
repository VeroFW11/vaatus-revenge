using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The pooled building blocks FireVfxRunner animates. Pooling means effects reuse a fixed set of
    // objects instead of creating and destroying GameObjects on every punch, which would cause hitches
    // from garbage collection in the middle of a fight.

    // The mesh a piece is drawn with. Sphere: blobs, droplets, streaks (stretched). RingBand: shockwaves and swirls.
    // Quad: a flat picture (an effect texture, a crack on the floor). Cube: rocks, chips, stone slabs. FlatRing: a circle
    // outline (an accent ring facing the camera, a ripple on the floor).
    public enum VfxShape { Sphere, RingBand, Quad, Cube, FlatRing }

    // How a piece is shaded. Each pooled piece keeps one material of each kind it has needed, so switching costs nothing.
    //   Glow        opaque unlit, HDR colour: bloom makes it read as fire (the original fire look)
    //   Lit         opaque, lit by the scene: rock and stone
    //   Additive    adds light and fades by alpha: air, sparks, pictures on a black background
    //   AlphaBlend  see-through and fades by alpha: water, dust, mist, pictures with an alpha channel
    public enum VfxMaterialKind { Glow, Lit, Additive, AlphaBlend }

    // A sphere or ring band that grows, cools down (changes colour) and shrinks away. Since Build 05 a piece can also be
    // a flat picture turned to face the camera (billboard), fall under gravity and settle on the floor, spin, orbit a
    // point (a vortex) and wait before it appears (a line of stone spikes erupting one after another).
    internal sealed class FireVfxPiece
    {
        public const int MaterialKinds = 4;

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        public GameObject GameObject;
        public Transform Transform;
        public MeshFilter Filter;
        public MeshRenderer Renderer;
        public Material Material;        // the material in use (one of Materials)
        public readonly Material[] Materials = new Material[MaterialKinds];
        public readonly Texture[] Textures = new Texture[MaterialKinds];   // the texture each of them holds now
        public bool Active;
        public int Generation;

        public bool Persistent;          // charge glow: lives until stopped
        public float Age;                // below 0 while it waits to appear (Delay)
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

        // Build 05: the other elements' looks.
        public VfxMaterialKind Kind;
        public bool Billboard;           // turned to face the camera every frame (Rotation is then the base turn)
        public float Gravity;            // metres per second squared, pulls Velocity down
        public bool HasFloor;            // settles at FloorY instead of falling through the floor
        public float FloorY;
        public float Bounce;             // share of the downward speed kept when it hits the floor
        public Vector3 SpinAxis;         // local axis it spins about...
        public float SpinSpeed;          // ...at this many degrees per second
        public float SpinAngle;
        public bool Orbits;              // moves round OrbitCenter instead of along Velocity
        public Vector3 OrbitCenter;
        public float OrbitRadius;
        public float OrbitAngle;         // radians, clockwise from +Z seen from above
        public float OrbitSpeed;         // radians per second
        public float OrbitGrowth;        // metres per second added to the orbit's radius
        public float OrbitRise;          // metres per second the orbit climbs
        public ElementId Element;        // a charge glow's element (Fire = the original glow)

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

        // Draws with this kind's material (which must already exist) holding this texture (null = plain colour).
        public void UseMaterial(VfxMaterialKind kind, Texture texture)
        {
            int index = (int)kind;
            Material material = Materials[index];
            if (material != Material)
            {
                Material = material;
                if (Renderer != null) Renderer.sharedMaterial = material;
                colorWritten = false;
            }
            Kind = kind;
            if (material == null || Textures[index] == texture) return;
            Textures[index] = texture;
            if (material.HasProperty(BaseMapId)) material.SetTexture(BaseMapId, texture);
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
        public ElementId Element;        // whose material and colours the renderer holds now (Fire = the original)

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

    // LimbFlame: the element on a striking fist or foot. FootJet: a dash's push from the feet. Embers: what trails a
    // launched enemy. Afterimage (Build 05): fading copies of the body left behind along a slip-in dodge.
    public enum FireVfxEmitterKind { LimbFlame, FootJet, Embers, Afterimage, LimbDust }   // LimbDust: Earth in the air, dust only

    // Spawns small flames at a moving point for a while (fire on a fist, jets from the feet, embers). Since Build 05 it
    // spawns the element's own look instead (droplets, dust and gravel, wisps of air) when Element isn't Fire.
    internal sealed class FireVfxEmitter
    {
        public bool Active;
        public int Generation;
        public Transform Follow;
        public FireVfxEmitterKind Kind;
        public ElementId Element;
        public Vector3 Direction;
        public bool Timed;
        public float Remaining;
        public float Accumulator;
    }

    // A lash of flame drawn as a line that sweeps across an arc (Fire Whip; Water's whips since Build 05).
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
        public ElementId Element;        // whose material and colours the line holds now (Fire = the original)
    }
}
