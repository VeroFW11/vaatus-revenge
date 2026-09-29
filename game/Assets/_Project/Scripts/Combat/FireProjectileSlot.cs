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

        public GameObject Root;
        public Transform RootTransform;
        public GameObject FireBall;
        public GameObject Bolt;
        public TrailRenderer Trail;

        public bool IsFree => !Flying && !Fading;

        public void CreateVisuals(Transform parent, Material fireMaterial, Material boltMaterial, Material trailMaterial)
        {
            Root = new GameObject("Projectile");
            RootTransform = Root.transform;
            RootTransform.SetParent(parent, false);
            FireBall = GreyboxShapes.CreateVisual("FireBall", PrimitiveType.Sphere, RootTransform, fireMaterial, false);
            Bolt = GreyboxShapes.CreateVisual("Bolt", PrimitiveType.Cube, RootTransform, boltMaterial, false);
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

        public bool HasVisuals => Root != null && Trail != null && FireBall != null && Bolt != null;

        // Shows the right model at the launch point with a fresh trail.
        public void ShowAt(Vector3 position, Vector3 direction, Gradient trailColors, float trailTime, float trailWidth)
        {
            Root.SetActive(true);
            RootTransform.SetPositionAndRotation(position, LookRotation(direction));
            bool fire = Visual == ProjectileVisual.Fire;
            FireBall.SetActive(fire);
            Bolt.SetActive(!fire);
            float size = Mathf.Max(0.05f, Radius * 2f * VisualScale);
            FireBall.transform.localScale = new Vector3(size, size, size);
            // The bolt is drawn much thinner than its hit radius on purpose: the radius is forgiving for gameplay.
            float length = 0.7f * VisualScale;
            Bolt.transform.localScale = new Vector3(0.04f * VisualScale, 0.04f * VisualScale, length);
            Bolt.transform.localPosition = new Vector3(0f, 0f, -length * 0.5f);
            Trail.colorGradient = trailColors;
            Trail.time = trailTime;
            Trail.widthMultiplier = trailWidth;
            Trail.Clear(); // no streak from where this pooled projectile was last used
            Trail.emitting = true;
        }

        public void MoveVisual(float time)
        {
            RootTransform.SetPositionAndRotation(Position, LookRotation(Velocity));
            if (Visual == ProjectileVisual.Fire)
            {
                // A slight flicker so the fireball reads as flame rather than a ball.
                float size = Mathf.Max(0.05f, Radius * 2f * VisualScale) * (1f + 0.1f * Mathf.Sin(time * 50f));
                FireBall.transform.localScale = new Vector3(size, size, size);
            }
        }

        // Stops flying: hides the model and lets the trail fade out before the slot is reused.
        public void BeginFade()
        {
            Flying = false;
            OnHit = null;
            if (!HasVisuals)
            {
                Fading = false;
                return;
            }
            FireBall.SetActive(false);
            Bolt.SetActive(false);
            Trail.emitting = false;
            Fading = true;
            FadeRemaining = Trail.time;
        }

        public void Hide()
        {
            Flying = false;
            Fading = false;
            OnHit = null;
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
