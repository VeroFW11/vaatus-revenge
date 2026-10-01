using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // One pooled projectile: its flight state plus the objects that draw it. FireProjectile owns and
    // simulates these; after an impact a slot keeps its trail fading for a moment before it's reused.
    internal sealed class FireProjectileSlot
    {
        public bool Flying;
        public bool Fading;
        public float FadeRemaining;
        public bool SkipThisFrame;       // launched during FireProjectile's own update: starts moving next frame
        public bool CheckStartOverlap;   // first step checks whether it was launched inside a wall
        public float LaunchTime;

        public Vector3 Position;
        public Vector3 Velocity;
        public float Radius;
        public float MaxRange;
        public float Gravity;
        public float ExplosionRadius;
        public float VisualScale;
        public float Travelled;
        public DamageInfo Damage;
        public ProjectileVisual Visual;
        public System.Action<HitReport> OnHit;
        public readonly List<int> PassedThrough = new List<int>(4); // fighters it flew through (dodged or ignored)

        // An enemy bolt keeps the player's danger sense told when it will land, every frame (the estimate made when it was
        // loosed goes stale as soon as the player moves).
        public bool TracksDanger;
        public bool DangerOffPath;      // the player is beside its path: its warning is called off for now
        public IncomingStrike DangerStrike;

        public GameObject Root;
        public Transform RootTransform;
        public GameObject FireBall;      // the ball for Fire, Water's ice and Air's blast (its material is swapped)
        public MeshRenderer BallRenderer;
        public GameObject Bolt;
        public GameObject Rock;
        public TrailRenderer Trail;

        public bool IsFree => !Flying && !Fading;

        public void CreateVisuals(Transform parent, Material fireMaterial, Material boltMaterial, Material rockMaterial, Material trailMaterial)
        {
            Root = new GameObject("Projectile");
            RootTransform = Root.transform;
            RootTransform.SetParent(parent, false);
            FireBall = GreyboxShapes.CreateVisual("FireBall", PrimitiveType.Sphere, RootTransform, fireMaterial, false);
            BallRenderer = FireBall.GetComponent<MeshRenderer>();
            Bolt = GreyboxShapes.CreateVisual("Bolt", PrimitiveType.Cube, RootTransform, boltMaterial, false);
            Rock = GreyboxShapes.CreateVisual("Rock", PrimitiveType.Cube, RootTransform, rockMaterial, true);
            Trail = Root.AddComponent<TrailRenderer>();
            Trail.sharedMaterial = trailMaterial;
            Trail.shadowCastingMode = ShadowCastingMode.Off;
            Trail.receiveShadows = false;
            Trail.autodestruct = false;
            Trail.emitting = false;
            Trail.minVertexDistance = 0.05f;
            Trail.numCapVertices = 2;
            Trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            Root.SetActive(false);
        }

        public bool HasVisuals => Root != null && Trail != null && FireBall != null && Bolt != null && Rock != null;

        // Shows the right model at the launch point with a fresh trail. ballMaterial: the ball's look for this visual
        // (Fire, Water or Air; ignored for a bolt or a rock).
        public void ShowAt(Vector3 position, Vector3 direction, Material ballMaterial, Material trailMaterial, Gradient trailColors,
            float trailTime, float trailWidth)
        {
            Root.SetActive(true);
            RootTransform.SetPositionAndRotation(position, LookRotation(direction));
            bool ball = Visual == ProjectileVisual.Fire || Visual == ProjectileVisual.WaterOrb || Visual == ProjectileVisual.AirBall;
            FireBall.SetActive(ball);
            Bolt.SetActive(Visual == ProjectileVisual.Bolt);
            Rock.SetActive(Visual == ProjectileVisual.Rock);
            if (ball && ballMaterial != null && BallRenderer.sharedMaterial != ballMaterial) BallRenderer.sharedMaterial = ballMaterial;
            float size = Mathf.Max(0.05f, Radius * 2f * VisualScale);
            // Water's ice dart is a sliver along its flight; the others are round.
            FireBall.transform.localScale = Visual == ProjectileVisual.WaterOrb ? new Vector3(size * 0.45f, size * 0.45f, size * 1.6f) : new Vector3(size, size, size);
            FireBall.transform.localRotation = Quaternion.identity;
            // The bolt is drawn much thinner than its hit radius on purpose: the radius is forgiving for gameplay.
            float length = 0.7f * VisualScale;
            Bolt.transform.localScale = new Vector3(0.04f * VisualScale, 0.04f * VisualScale, length);
            Bolt.transform.localPosition = new Vector3(0f, 0f, -length * 0.5f);
            // The boulder is a little smaller than its hit radius, so a near miss still looks like one.
            Rock.transform.localScale = new Vector3(size * 0.85f, size * 0.75f, size * 0.9f);
            Rock.transform.localRotation = Random.rotationUniform;
            if (trailMaterial != null && Trail.sharedMaterial != trailMaterial) Trail.sharedMaterial = trailMaterial;
            Trail.colorGradient = trailColors;
            Trail.time = trailTime;
            Trail.widthMultiplier = trailWidth;
            Trail.Clear(); // no streak from where this pooled projectile was last used
            Trail.emitting = true;
        }

        public void MoveVisual(float time, float dt)
        {
            RootTransform.SetPositionAndRotation(Position, LookRotation(Velocity));
            float size = Mathf.Max(0.05f, Radius * 2f * VisualScale);
            switch (Visual)
            {
                case ProjectileVisual.Fire:
                {
                    // A slight flicker so the fireball reads as flame rather than a ball.
                    float flicker = size * (1f + 0.1f * Mathf.Sin(time * 50f));
                    FireBall.transform.localScale = new Vector3(flicker, flicker, flicker);
                    break;
                }
                case ProjectileVisual.AirBall:
                {
                    // A ball of wind: it swirls and breathes.
                    float breathe = size * (1f + 0.15f * Mathf.Sin(time * 35f));
                    FireBall.transform.localScale = new Vector3(breathe, breathe * 0.85f, breathe);
                    FireBall.transform.localRotation *= Quaternion.AngleAxis(900f * dt, Vector3.forward);
                    break;
                }
                case ProjectileVisual.Rock:
                    // A thrown boulder tumbles.
                    Rock.transform.localRotation *= Quaternion.AngleAxis(420f * dt, Vector3.right);
                    break;
            }
        }

        // Stops flying: hides the model and lets the trail fade out before the slot is reused.
        public void BeginFade()
        {
            Flying = false;
            OnHit = null;
            TracksDanger = false;
            if (!HasVisuals)
            {
                Fading = false;
                return;
            }
            FireBall.SetActive(false);
            Bolt.SetActive(false);
            Rock.SetActive(false);
            Trail.emitting = false;
            Fading = true;
            FadeRemaining = Trail.time;
        }

        public void Hide()
        {
            Flying = false;
            Fading = false;
            OnHit = null;
            TracksDanger = false;
            if (Trail != null)
            {
                Trail.emitting = false;
                Trail.Clear();
            }
            if (Root != null) Root.SetActive(false);
        }

        static Quaternion LookRotation(Vector3 direction)
        {
            return direction.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(direction) : Quaternion.identity;
        }
    }
}
