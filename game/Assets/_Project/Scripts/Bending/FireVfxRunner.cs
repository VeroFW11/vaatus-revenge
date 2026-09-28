using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VaatusRevenge
{
    // Owns and animates FireVfx's pooled objects. FireVfx creates it (hidden, DontDestroyOnLoad) on first use
    // in play mode; don't add it by hand. Runs in LateUpdate so trails and glows follow limbs after the
    // fighters and their rigs have moved, and before the camera (LateUpdate order 100) renders the frame.
    [DefaultExecutionOrder(60)]
    [AddComponentMenu("")]
    public class FireVfxRunner : MonoBehaviour
    {
        const float TwoPi = Mathf.PI * 2f;
        const float BurstSpread = 0.35f;   // how far burst blobs stray from the strike direction
        const float GroundOffset = 0.02f;  // lifts ground effects just above the floor so they don't flicker into it

        readonly List<FireVfxPiece> pieces = new List<FireVfxPiece>();
        readonly List<FireVfxTrail> trails = new List<FireVfxTrail>();
        readonly List<FireVfxLight> lights = new List<FireVfxLight>();
        readonly List<Material> ownedMaterials = new List<Material>();
        Shader pieceShader;
        Material trailMaterial;
        Gradient trailGradient;
        AnimationCurve trailWidth;
        int nextGeneration = 1;
        float time;

        public bool CanRender { get; private set; }

        internal static FireVfxRunner Create()
        {
            var go = new GameObject("FireVfx (pool)");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            FireVfxRunner runner = go.AddComponent<FireVfxRunner>();
            runner.pieceShader = GreyboxShapes.FindShader(GreyboxShapes.UnlitShaderName, GreyboxShapes.LitShaderName);
            runner.CanRender = runner.pieceShader != null;
            return runner;
        }

        // ---- Effects ---------------------------------------------------------------------------------

        internal void Burst(Vector3 position, Vector3 direction, float scale)
        {
            if (!(scale > 0f)) return;
            FireVfxStyle s = FireVfx.Style;
            Vector3 dir = SafeDirection(direction, Vector3.up);
            float size = s.BurstSize * scale;
            float life = s.BurstLifetime;
            // White-hot core where the flame leaves the limb...
            SpawnBlob(position, dir * (s.BurstSpeed * scale * 0.3f), 8f, size * 0.3f, size, 0f, 0.25f, life * 0.7f, s.CoreColor, s.FlameColor);
            // ...and tongues of flame thrown forward that slow down and cool.
            int count = Mathf.Clamp(s.BurstBlobs, 0, 12);
            for (int i = 0; i < count; i++)
            {
                Vector3 velocity = (dir + Random.insideUnitSphere * BurstSpread).normalized * (s.BurstSpeed * scale * Random.Range(0.6f, 1.1f));
                float blob = size * Random.Range(0.5f, 0.8f);
                SpawnBlob(position + dir * (size * 0.2f), velocity, 5f, blob * 0.4f, blob, 0f, 0.3f, life * Random.Range(0.8f, 1.2f), s.FlameColor, s.EmberColor);
            }
            if (scale >= s.BurstLightMinScale) SpawnLight(position, s.BurstLightRange * scale, s.LightIntensity * 0.6f, s.LightLifetime);
        }

        internal void Explosion(Vector3 position, float radius)
        {
            FireVfxStyle s = FireVfx.Style;
            radius = Mathf.Max(0.1f, radius);
            float life = s.ExplosionLifetime;
            SpawnBlob(position, Vector3.zero, 0f, radius * 0.5f, radius * 2f, 0f, 0.3f, life, s.CoreColor, s.EmberColor);
            int embers = Mathf.Clamp(s.ExplosionEmbers, 0, 24);
            float speed = s.EmberSpeed * Mathf.Sqrt(radius);
            for (int i = 0; i < embers; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                if (dir.y < 0f) dir.y *= -0.5f; // mostly up and out, not into the floor
                FireVfxPiece ember = SpawnBlob(position, dir * (speed * Random.Range(0.6f, 1.2f)), 3f, s.EmberSize, s.EmberSize * 1.3f, 0f,
                    0.2f, life * Random.Range(0.9f, 1.4f), s.FlameColor, s.EmberColor);
                Stretch(ember, dir, 2.5f);
            }
            SpawnLight(position, radius * s.LightRangePerRadius, s.LightIntensity, s.LightLifetime * 1.5f);
        }

        internal void Ring(Vector3 center, float radius)
        {
            FireVfxStyle s = FireVfx.Style;
            radius = Mathf.Max(0.2f, radius);
            float life = s.RingLifetime;
            Vector3 ground = center + Vector3.up * GroundOffset;
            // A low wall of flame racing outwards, then sinking into the floor (the mesh has radius 0.5, so scale = diameter).
            FireVfxPiece band = AcquirePiece(GreyboxShapes.GetRingBandMesh(), out _);
            if (band != null)
            {
                band.Position = ground;
                band.StartScale = new Vector3(radius * 0.3f, s.RingHeight, radius * 0.3f);
                band.PeakScale = new Vector3(radius * 2f, s.RingHeight * 0.8f, radius * 2f);
                band.EndScale = new Vector3(radius * 2.2f, 0f, radius * 2.2f);
                band.PeakAt = 0.6f;
                band.Lifetime = life;
                band.StartColor = s.CoreColor;
                band.EndColor = s.EmberColor;
                ApplyPiece(band, 0f);
            }
            // A flat flash of heat on the floor under it.
            FireVfxPiece disc = SpawnBlob(ground, Vector3.zero, 0f, radius * 0.4f, radius * 1.6f, 0f, 0.35f, life * 0.6f, s.FlameColor, s.EmberColor);
            if (disc != null)
            {
                disc.StartScale.y = disc.PeakScale.y = disc.EndScale.y = GroundOffset * 2f;
                ApplyPiece(disc, 0f);
            }
            for (int i = 0; i < 4; i++)
            {
                float angle = (i + Random.value * 0.5f) * TwoPi / 4f;
                var dir = new Vector3(Mathf.Sin(angle), 0.25f, Mathf.Cos(angle));
                FireVfxPiece ember = SpawnBlob(ground, dir * (s.EmberSpeed * Random.Range(0.8f, 1.2f)), 3f, s.EmberSize, s.EmberSize * 1.3f, 0f,
                    0.2f, life, s.FlameColor, s.EmberColor);
                Stretch(ember, dir, 2.5f);
            }
            SpawnLight(center + Vector3.up * 0.5f, radius * s.LightRangePerRadius, s.LightIntensity, s.LightLifetime * 1.5f);
        }

        internal void HitSpark(Vector3 position, Color color)
        {
            FireVfxStyle s = FireVfx.Style;
            Color bright = color * s.SparkIntensity;
            bright.a = 1f;
            Color faded = bright * 0.35f;
            faded.a = 1f;
            float life = s.SparkLifetime;
            SpawnBlob(position, Vector3.zero, 0f, s.SparkSize * 0.15f, s.SparkSize, 0f, 0.3f, life, bright, faded);
            int count = Mathf.Clamp(s.SparkCount, 0, 12);
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                FireVfxPiece spark = SpawnBlob(position, dir * (s.SparkSpeed * Random.Range(0.8f, 1.5f)), 8f, s.SparkSize * 0.12f, s.SparkSize * 0.18f, 0f,
                    0.2f, life * 1.5f, bright, faded);
                Stretch(spark, dir, 3f);
            }
        }

        internal void Muzzle(Vector3 position, Vector3 direction)
        {
            FireVfxStyle s = FireVfx.Style;
            Vector3 dir = SafeDirection(direction, Vector3.forward);
            FireVfxPiece flash = SpawnBlob(position + dir * (s.MuzzleSize * 0.3f), dir * 2f, 6f, s.MuzzleSize * 0.3f, s.MuzzleSize, 0f, 0.25f,
                s.MuzzleLifetime, s.CoreColor, s.FlameColor);
            Stretch(flash, dir, 2.2f);
            SpawnLight(position, s.BurstLightRange * 0.75f, s.LightIntensity * 0.5f, s.LightLifetime * 0.6f);
        }

        internal FireVfxHandle Trail(Transform follow, float duration)
        {
            if (follow == null) return FireVfxHandle.None;
            FireVfxTrail trail = AcquireTrail(out int index);
            if (trail == null) return FireVfxHandle.None;
            FireVfxStyle s = FireVfx.Style;
            trail.Active = true;
            trail.Generation = nextGeneration++;
            trail.Follow = follow;
            trail.Emitting = true;
            trail.Timed = duration > 0f;
            trail.EmitRemaining = duration;
            trail.FadeRemaining = 0f;
            trail.Transform.position = follow.position;
            trail.GameObject.SetActive(true);
            trail.Renderer.time = Mathf.Max(0.01f, s.TrailTime);
            trail.Renderer.widthMultiplier = s.TrailWidth;
            trail.Renderer.Clear(); // no streak from wherever this pooled trail was last used
            trail.Renderer.emitting = true;
            return new FireVfxHandle(FireVfxHandle.TrailKind, index, trail.Generation);
        }

        internal FireVfxHandle ChargeGlow(Transform anchor)
        {
            if (anchor == null) return FireVfxHandle.None;
            FireVfxPiece glow = AcquirePiece(GreyboxShapes.GetMesh(PrimitiveType.Sphere), out int index);
            if (glow == null) return FireVfxHandle.None;
            glow.Persistent = true;
            glow.Follow = anchor;
            UpdateGlow(glow, 0f);
            return new FireVfxHandle(FireVfxHandle.PieceKind, index, glow.Generation);
        }

        internal void StopAll()
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i].Active) ReleasePiece(pieces[i]);
            }
            for (int i = 0; i < trails.Count; i++)
            {
                if (trails[i].Active) ReleaseTrail(trails[i]);
            }
            for (int i = 0; i < lights.Count; i++) ReleaseLight(lights[i]);
        }

        // ---- Handles ---------------------------------------------------------------------------------

        internal void Stop(FireVfxHandle handle)
        {
            if (TryGetPiece(handle, out FireVfxPiece piece)) piece.Stopping = true;
            else if (TryGetTrail(handle, out FireVfxTrail trail) && trail.Emitting) StopTrail(trail);
        }

        internal void SetLevel(FireVfxHandle handle, float level)
        {
            if (TryGetPiece(handle, out FireVfxPiece piece)) piece.Level = Mathf.Clamp01(level);
            else if (TryGetTrail(handle, out FireVfxTrail trail)) trail.Renderer.widthMultiplier = FireVfx.Style.TrailWidth * Mathf.Max(0f, level);
        }

        internal bool IsAlive(FireVfxHandle handle)
        {
            return TryGetPiece(handle, out _) || TryGetTrail(handle, out _);
        }

        bool TryGetPiece(FireVfxHandle handle, out FireVfxPiece piece)
        {
            piece = null;
            if (handle.Kind != FireVfxHandle.PieceKind || handle.Index < 0 || handle.Index >= pieces.Count) return false;
            piece = pieces[handle.Index];
            return piece.IsAlive && piece.Generation == handle.Generation;
        }

        bool TryGetTrail(FireVfxHandle handle, out FireVfxTrail trail)
        {
            trail = null;
            if (handle.Kind != FireVfxHandle.TrailKind || handle.Index < 0 || handle.Index >= trails.Count) return false;
            trail = trails[handle.Index];
            return trail.IsAlive && trail.Generation == handle.Generation;
        }

        // ---- Frame update ----------------------------------------------------------------------------

        void LateUpdate()
        {
            float dt = EffectDeltaTime();
            float gameDt = Time.deltaTime; // trails fade in game time, like the TrailRenderer itself
            time += dt;
            for (int i = 0; i < pieces.Count; i++)
            {
                FireVfxPiece piece = pieces[i];
                if (!piece.Active) continue;
                if (piece.GameObject == null) piece.Active = false;
                else if (piece.Persistent) UpdateGlow(piece, dt);
                else UpdatePiece(piece, dt);
            }
            for (int i = 0; i < trails.Count; i++)
            {
                if (trails[i].Active) UpdateTrail(trails[i], gameDt);
            }
            for (int i = 0; i < lights.Count; i++)
            {
                if (lights[i].Active) UpdateLight(lights[i], dt);
            }
        }

        // Effects keep playing in real time during hitstop, so the impact burst blooms while the fighters are
        // frozen; otherwise they follow game time (slowing in slow motion, stopping when paused).
        static float EffectDeltaTime()
        {
            if (TimeScaleController.IsPaused || Time.timeScale <= 0f) return 0f;
            if (TimeScaleController.IsHitstopActive) return Time.unscaledDeltaTime;
            return Time.deltaTime;
        }

        void UpdatePiece(FireVfxPiece piece, float dt)
        {
            piece.Age += dt;
            float u = piece.Age / piece.Lifetime;
            if (u >= 1f)
            {
                ReleasePiece(piece);
                return;
            }
            if (piece.Drag > 0f) piece.Velocity *= Mathf.Exp(-piece.Drag * dt);
            piece.Position += piece.Velocity * dt;
            ApplyPiece(piece, u);
        }

        // Grows to its peak size fast, then shrinks away: "fading" by size keeps effects opaque and cheap.
        static void ApplyPiece(FireVfxPiece piece, float u)
        {
            Vector3 scale = u < piece.PeakAt
                ? Vector3.LerpUnclamped(piece.StartScale, piece.PeakScale, GreyboxLimbMotion.EaseOutCubic(u / piece.PeakAt))
                : Vector3.LerpUnclamped(piece.PeakScale, piece.EndScale, EaseIn((u - piece.PeakAt) / (1f - piece.PeakAt)));
            piece.Transform.SetPositionAndRotation(piece.Position, piece.Rotation);
            piece.Transform.localScale = scale;
            piece.SetColor(Color.Lerp(piece.StartColor, piece.EndColor, u));
        }

        void UpdateGlow(FireVfxPiece glow, float dt)
        {
            if (glow.Follow == null)
            {
                ReleasePiece(glow);
                return;
            }
            FireVfxStyle s = FireVfx.Style;
            float flicker = 1f + 0.12f * Mathf.Sin(time * s.ChargeFlickerRate * TwoPi) + 0.06f * Mathf.Sin(time * s.ChargeFlickerRate * 2.7f);
            float size = Mathf.Lerp(s.ChargeMinSize, s.ChargeMaxSize, glow.Level) * flicker;
            if (glow.Stopping)
            {
                glow.StopAge += dt;
                float remaining = s.ChargeStopTime > 0f ? 1f - glow.StopAge / s.ChargeStopTime : 0f;
                if (remaining <= 0f)
                {
                    ReleasePiece(glow);
                    return;
                }
                size *= remaining;
            }
            glow.Position = glow.Follow.position;
            glow.Transform.SetPositionAndRotation(glow.Position, Quaternion.identity);
            glow.Transform.localScale = new Vector3(size, size, size);
            glow.SetColor(Color.Lerp(s.EmberColor, s.CoreColor, glow.Level));
        }

        void UpdateTrail(FireVfxTrail trail, float dt)
        {
            if (trail.GameObject == null)
            {
                trail.Active = false;
                return;
            }
            if (trail.Emitting)
            {
                if (trail.Follow == null)
                {
                    StopTrail(trail);
                    return;
                }
                trail.Transform.position = trail.Follow.position;
                if (trail.Timed)
                {
                    trail.EmitRemaining -= dt;
                    if (trail.EmitRemaining <= 0f) StopTrail(trail);
                }
                return;
            }
            trail.FadeRemaining -= dt;
            if (trail.FadeRemaining <= 0f) ReleaseTrail(trail);
        }

        static void UpdateLight(FireVfxLight light, float dt)
        {
            if (light.Light == null)
            {
                light.Active = false;
                return;
            }
            light.Age += dt;
            float u = light.Age / light.Lifetime;
            if (u >= 1f)
            {
                ReleaseLight(light);
                return;
            }
            float k = 1f - u;
            light.Light.intensity = light.PeakIntensity * k * k;
        }

        // ---- Pools -----------------------------------------------------------------------------------

        FireVfxPiece SpawnBlob(Vector3 position, Vector3 velocity, float drag, float startSize, float peakSize, float endSize,
            float peakAt, float lifetime, Color startColor, Color endColor)
        {
            FireVfxPiece piece = AcquirePiece(GreyboxShapes.GetMesh(PrimitiveType.Sphere), out _);
            if (piece == null) return null;
            piece.Position = position;
            piece.Velocity = velocity;
            piece.Drag = drag;
            piece.StartScale = new Vector3(startSize, startSize, startSize);
            piece.PeakScale = new Vector3(peakSize, peakSize, peakSize);
            piece.EndScale = new Vector3(endSize, endSize, endSize);
            piece.PeakAt = Mathf.Clamp(peakAt, 0.01f, 0.99f);
            piece.Lifetime = Mathf.Max(0.01f, lifetime);
            piece.StartColor = startColor;
            piece.EndColor = endColor;
            ApplyPiece(piece, 0f); // placed now, so it never flashes up at a stale position
            return piece;
        }

        // Elongates a blob along a direction (streaks for sparks and embers).
        static void Stretch(FireVfxPiece piece, Vector3 direction, float stretch)
        {
            if (piece == null || direction.sqrMagnitude < 1e-6f) return;
            piece.Rotation = Quaternion.LookRotation(direction.normalized);
            piece.StartScale.z *= stretch;
            piece.PeakScale.z *= stretch;
            piece.EndScale.z *= stretch;
            ApplyPiece(piece, 0f);
        }

        FireVfxPiece AcquirePiece(Mesh mesh, out int index)
        {
            index = -1;
            if (mesh == null) return null;
            for (int i = 0; i < pieces.Count; i++)
            {
                if (!pieces[i].Active)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                if (pieces.Count < Mathf.Max(1, FireVfx.Style.MaxPieces))
                {
                    pieces.Add(new FireVfxPiece());
                    index = pieces.Count - 1;
                }
                else index = OldestPiece();
                if (index < 0) return null;
            }
            FireVfxPiece piece = pieces[index];
            if (piece.GameObject == null) CreatePieceObject(piece);
            if (piece.GameObject == null) return null;
            if (piece.Filter.sharedMesh != mesh) piece.Filter.sharedMesh = mesh;
            piece.Active = true;
            piece.Generation = nextGeneration++;
            piece.Persistent = false;
            piece.Follow = null;
            piece.Level = 0f;
            piece.Stopping = false;
            piece.StopAge = 0f;
            piece.Age = 0f;
            piece.Drag = 0f;
            piece.Velocity = Vector3.zero;
            piece.Rotation = Quaternion.identity;
            piece.GameObject.SetActive(true);
            return piece;
        }

        // Pool full: recycle the effect closest to finishing (charge glows are never stolen).
        int OldestPiece()
        {
            int oldest = -1;
            float mostProgress = -1f;
            for (int i = 0; i < pieces.Count; i++)
            {
                FireVfxPiece piece = pieces[i];
                if (piece.Persistent) continue;
                float progress = piece.Age / Mathf.Max(0.01f, piece.Lifetime);
                if (progress > mostProgress)
                {
                    mostProgress = progress;
                    oldest = i;
                }
            }
            return oldest;
        }

        void CreatePieceObject(FireVfxPiece piece)
        {
            Material material = NewMaterial("FireVfxPiece");
            if (material == null) return;
            GameObject go = GreyboxShapes.CreateVisual("FirePiece", PrimitiveType.Sphere, transform, material, false);
            go.SetActive(false);
            piece.GameObject = go;
            piece.Transform = go.transform;
            piece.Filter = go.GetComponent<MeshFilter>();
            piece.Material = material;
            piece.Active = false;
            piece.ResetColorCache();
        }

        static void ReleasePiece(FireVfxPiece piece)
        {
            piece.Active = false;
            piece.Follow = null;
            if (piece.GameObject != null) piece.GameObject.SetActive(false);
        }

        FireVfxTrail AcquireTrail(out int index)
        {
            index = -1;
            for (int i = 0; i < trails.Count; i++)
            {
                if (!trails[i].Active)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                if (trails.Count < Mathf.Max(1, FireVfx.Style.MaxTrails))
                {
                    trails.Add(new FireVfxTrail());
                    index = trails.Count - 1;
                }
                else
                {
                    // Pool full: reuse a fading trail if there is one, otherwise the first.
                    index = 0;
                    for (int i = 0; i < trails.Count; i++)
                    {
                        if (!trails[i].Emitting)
                        {
                            index = i;
                            break;
                        }
                    }
                    ReleaseTrail(trails[index]);
                }
            }
            FireVfxTrail trail = trails[index];
            if (trail.GameObject == null) CreateTrailObject(trail);
            return trail.GameObject != null ? trail : null;
        }

        void CreateTrailObject(FireVfxTrail trail)
        {
            Material material = TrailMaterial();
            if (material == null) return;
            var go = new GameObject("FireTrail");
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            TrailRenderer renderer = go.AddComponent<TrailRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.autodestruct = false;
            renderer.emitting = false;
            renderer.minVertexDistance = 0.04f;
            renderer.numCapVertices = 2;
            renderer.widthCurve = trailWidth;
            renderer.colorGradient = trailGradient;
            trail.GameObject = go;
            trail.Transform = go.transform;
            trail.Renderer = renderer;
            trail.Active = false;
        }

        static void StopTrail(FireVfxTrail trail)
        {
            trail.Emitting = false;
            trail.Follow = null;
            if (trail.Renderer == null) return;
            trail.Renderer.emitting = false;
            trail.FadeRemaining = trail.Renderer.time;
        }

        static void ReleaseTrail(FireVfxTrail trail)
        {
            trail.Active = false;
            trail.Emitting = false;
            trail.Follow = null;
            if (trail.Renderer != null)
            {
                trail.Renderer.emitting = false;
                trail.Renderer.Clear();
            }
            if (trail.GameObject != null) trail.GameObject.SetActive(false);
        }

        void SpawnLight(Vector3 position, float range, float intensity, float lifetime)
        {
            FireVfxStyle s = FireVfx.Style;
            int max = Mathf.Clamp(s.MaxLights, 0, 8);
            if (max == 0 || !(range > 0f)) return;
            int index = -1;
            int oldest = -1;
            float mostProgress = -1f;
            for (int i = 0; i < lights.Count && i < max; i++)
            {
                FireVfxLight candidate = lights[i];
                if (!candidate.Active)
                {
                    index = i;
                    break;
                }
                float progress = candidate.Age / Mathf.Max(0.01f, candidate.Lifetime);
                if (progress > mostProgress)
                {
                    mostProgress = progress;
                    oldest = i;
                }
            }
            if (index < 0 && lights.Count < max)
            {
                lights.Add(new FireVfxLight());
                index = lights.Count - 1;
            }
            if (index < 0) index = oldest;
            if (index < 0) return;

            FireVfxLight light = lights[index];
            if (light.Light == null) CreateLightObject(light);
            light.Active = true;
            light.Age = 0f;
            light.Lifetime = Mathf.Max(0.01f, lifetime);
            light.PeakIntensity = intensity;
            light.GameObject.transform.position = position;
            light.Light.range = range;
            light.Light.color = s.LightColor;
            light.Light.intensity = intensity;
            light.Light.enabled = true;
        }

        void CreateLightObject(FireVfxLight light)
        {
            var go = new GameObject("FireLight");
            go.transform.SetParent(transform, false);
            Light component = go.AddComponent<Light>();
            component.type = LightType.Point;
            component.shadows = LightShadows.None;
            component.enabled = false;
            light.GameObject = go;
            light.Light = component;
        }

        static void ReleaseLight(FireVfxLight light)
        {
            light.Active = false;
            if (light.Light != null) light.Light.enabled = false;
        }

        // ---- Materials -------------------------------------------------------------------------------

        Material NewMaterial(string materialName)
        {
            if (pieceShader == null) return null;
            var material = new Material(pieceShader) { name = materialName };
            ownedMaterials.Add(material);
            return material;
        }

        Material TrailMaterial()
        {
            if (trailMaterial != null) return trailMaterial;
            trailMaterial = GreyboxShapes.CreateAdditive("FireVfxTrail", FireVfx.Style.TrailColor);
            if (trailMaterial == null) return null;
            ownedMaterials.Add(trailMaterial);
            // Hot white-yellow at the limb, cooling to red and fading out along the tail.
            trailGradient = new Gradient();
            trailGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f),
                    new GradientColorKey(new Color(1f, 0.55f, 0.1f), 0.4f),
                    new GradientColorKey(new Color(0.8f, 0.15f, 0.02f), 1f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            trailWidth = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            return trailMaterial;
        }

        static Vector3 SafeDirection(Vector3 direction, Vector3 fallback)
        {
            return direction.sqrMagnitude > 1e-8f ? direction.normalized : fallback;
        }

        static float EaseIn(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x;
        }

        void OnDestroy()
        {
            // Materials made with 'new Material' aren't cleaned up with their GameObjects; release them here.
            for (int i = 0; i < ownedMaterials.Count; i++)
            {
                if (ownedMaterials[i] != null) Destroy(ownedMaterials[i]);
            }
            ownedMaterials.Clear();
        }
    }
}
