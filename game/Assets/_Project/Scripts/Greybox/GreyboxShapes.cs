using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VaatusRevenge
{
    // Shared helpers for grey-box visuals: primitive meshes without colliders, URP materials with safe
    // fallbacks, and destroying objects correctly in both edit mode and play mode.
    //
    // Why no colliders: GameObject.CreatePrimitive adds one, and a collider on a fist or an effect makes
    // the camera pop forward and CharacterControllers bump into their own arms. So we borrow the built-in
    // meshes once and build visuals from MeshFilter + MeshRenderer only.
    public static class GreyboxShapes
    {
        public const string LitShaderName = "Universal Render Pipeline/Lit";
        public const string UnlitShaderName = "Universal Render Pipeline/Unlit";
        public const string ParticlesUnlitShaderName = "Universal Render Pipeline/Particles/Unlit";

        // URP turns a material's emission off when its emission colour is pure black (its material editor
        // "fixes" the keyword). Keeping a practically invisible amount keeps glows switchable at runtime.
        public const float EmissionFloor = 0.001f;

        public static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        public static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        static readonly Mesh[] primitiveMeshes = new Mesh[8];
        static readonly HashSet<string> reportedMissing = new HashSet<string>();
        static Mesh ringBandMesh;
        static Mesh flatRingMesh;

        // Domain reload is off, so statics survive between Play sessions: forget them at the start of each one (the
        // cached mesh may have been destroyed with the last scene, and a missing shader should be reported again).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            reportedMissing.Clear();
            ringBandMesh = null;
            flatRingMesh = null;
        }

        // Unity's built-in mesh for a primitive (the same one CreatePrimitive uses), cached.
        public static Mesh GetMesh(PrimitiveType type)
        {
            int index = (int)type;
            if (index < 0 || index >= primitiveMeshes.Length) return null;
            if (primitiveMeshes[index] == null)
            {
                // CreatePrimitive is the only public way to reach the built-in meshes: borrow the mesh from a
                // throwaway object, then destroy the object (and its collider) immediately.
                GameObject temp = GameObject.CreatePrimitive(type);
                temp.hideFlags = HideFlags.HideAndDontSave;
                Collider collider = temp.GetComponent<Collider>();
                if (collider != null) collider.enabled = false;
                MeshFilter filter = temp.GetComponent<MeshFilter>();
                primitiveMeshes[index] = filter != null ? filter.sharedMesh : null;
                Object.DestroyImmediate(temp);
            }
            return primitiveMeshes[index];
        }

        // An open, double-sided cylinder wall: radius 0.5, from y 0 to y 1. Scaled flat and wide it makes a
        // shockwave ring that can "sink into the floor" by shrinking its height (fade by scale, no transparency).
        public static Mesh GetRingBandMesh()
        {
            if (ringBandMesh != null) return ringBandMesh;
            const int segments = 48;
            var vertices = new Vector3[segments * 2];
            var normals = new Vector3[segments * 2];
            var triangles = new int[segments * 12];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                vertices[i * 2] = outward * 0.5f;
                vertices[i * 2 + 1] = outward * 0.5f + Vector3.up;
                normals[i * 2] = outward;
                normals[i * 2 + 1] = outward;
            }
            for (int i = 0; i < segments; i++)
            {
                int bottomA = i * 2, topA = i * 2 + 1;
                int bottomB = (i + 1) % segments * 2, topB = bottomB + 1;
                int t = i * 12;
                // Each quad twice, once per winding, so the band is visible from inside and outside.
                triangles[t] = bottomA; triangles[t + 1] = topA; triangles[t + 2] = topB;
                triangles[t + 3] = bottomA; triangles[t + 4] = topB; triangles[t + 5] = bottomB;
                triangles[t + 6] = bottomA; triangles[t + 7] = topB; triangles[t + 8] = topA;
                triangles[t + 9] = bottomA; triangles[t + 10] = bottomB; triangles[t + 11] = topB;
            }
            ringBandMesh = new Mesh { name = "GreyboxRingBand" };
            ringBandMesh.vertices = vertices;
            ringBandMesh.normals = normals;
            ringBandMesh.triangles = triangles;
            ringBandMesh.RecalculateBounds();
            return ringBandMesh;
        }

        // A flat, double-sided ring in the XZ plane: outer radius 0.5, inner radius 0.4. Turned to face the camera it draws
        // a circle outline of steady thickness (a ring band seen along its axis would vanish edge-on); laid on the floor
        // it is a ripple.
        public static Mesh GetFlatRingMesh()
        {
            if (flatRingMesh != null) return flatRingMesh;
            const int segments = 48;
            const float inner = 0.4f;
            var vertices = new Vector3[segments * 2];
            var normals = new Vector3[segments * 2];
            var uv = new Vector2[segments * 2];
            var triangles = new int[segments * 12];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                vertices[i * 2] = outward * inner;
                vertices[i * 2 + 1] = outward * 0.5f;
                normals[i * 2] = Vector3.up;
                normals[i * 2 + 1] = Vector3.up;
                uv[i * 2] = new Vector2(i / (float)segments, 0f);
                uv[i * 2 + 1] = new Vector2(i / (float)segments, 1f);
            }
            for (int i = 0; i < segments; i++)
            {
                int innerA = i * 2, outerA = i * 2 + 1;
                int innerB = (i + 1) % segments * 2, outerB = innerB + 1;
                int t = i * 12;
                triangles[t] = innerA; triangles[t + 1] = outerA; triangles[t + 2] = outerB;
                triangles[t + 3] = innerA; triangles[t + 4] = outerB; triangles[t + 5] = innerB;
                triangles[t + 6] = innerA; triangles[t + 7] = outerB; triangles[t + 8] = outerA;
                triangles[t + 9] = innerA; triangles[t + 10] = innerB; triangles[t + 11] = outerB;
            }
            flatRingMesh = new Mesh { name = "GreyboxFlatRing" };
            flatRingMesh.vertices = vertices;
            flatRingMesh.normals = normals;
            flatRingMesh.uv = uv;
            flatRingMesh.triangles = triangles;
            flatRingMesh.RecalculateBounds();
            return flatRingMesh;
        }

        // A renderable primitive with no collider, parented without changing its local transform.
        public static GameObject CreateVisual(string name, PrimitiveType type, Transform parent, Material material, bool castShadows)
        {
            return CreateVisual(name, GetMesh(type), parent, material, castShadows);
        }

        public static GameObject CreateVisual(string name, Mesh mesh, Transform parent, Material material, bool castShadows)
        {
            var go = new GameObject(name);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
                go.layer = parent.gameObject.layer;
            }
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = castShadows;
            return go;
        }

        // An empty pivot transform (anchors, bones).
        public static Transform CreatePivot(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            if (parent != null) go.layer = parent.gameObject.layer;
            return go.transform;
        }

        // Removes every collider under root. Never call it on a fighter's own GameObject: its
        // CharacterController is a Collider too. Colliders are disabled first because Destroy only takes
        // effect at the end of the frame; until then the camera and CharacterControllers would still hit them.
        public static int StripColliders(GameObject root)
        {
            if (root == null) return 0;
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            int removed = 0;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || collider is CharacterController) continue;
                collider.enabled = false;
                SafeDestroy(collider);
                removed++;
            }
            return removed;
        }

        // Destroy works only in play mode; editor code (the sandbox builder) must use DestroyImmediate.
        public static void SafeDestroy(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }

        // Finds a shader, trying a fallback, and logs a missing one only once (no console spam every frame).
        public static Shader FindShader(string preferred, string fallback)
        {
            Shader shader = Shader.Find(preferred);
            if (shader == null && !string.IsNullOrEmpty(fallback)) shader = Shader.Find(fallback);
            if (shader == null && reportedMissing.Add(preferred))
            {
                Debug.LogWarning("Greybox: shader '" + preferred + "' not found (is URP installed?). Grey-box visuals will be skipped.");
            }
            return shader;
        }

        // Opaque lit material for bodies and props. emissionReady turns emission on now so glows can be
        // driven later just by changing _EmissionColor.
        public static Material CreateLit(string name, Color color, bool emissionReady)
        {
            Shader shader = FindShader(LitShaderName, UnlitShaderName);
            if (shader == null) return null;
            var material = new Material(shader) { name = name };
            SetBaseColor(material, color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.2f);
            if (emissionReady) PrepareEmission(material);
            return material;
        }

        // Opaque unlit material. With an HDR colour (components above 1) bloom makes it glow like fire.
        public static Material CreateUnlit(string name, Color color)
        {
            Shader shader = FindShader(UnlitShaderName, LitShaderName);
            if (shader == null) return null;
            var material = new Material(shader) { name = name };
            SetBaseColor(material, color);
            return material;
        }

        // Additive material for trails: the trail adds light to what's behind it and fades out through its
        // vertex colours, with no sorting problems. Falls back to opaque unlit if the particle shader is missing.
        public static Material CreateAdditive(string name, Color color)
        {
            Shader shader = Shader.Find(ParticlesUnlitShaderName);
            if (shader == null) return CreateUnlit(name, color);
            var material = new Material(shader) { name = name };
            SetBaseColor(material, color);
            SetFloatIfPresent(material, "_Surface", 1f);   // transparent
            SetFloatIfPresent(material, "_Blend", 2f);     // additive
            SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.One);
            SetFloatIfPresent(material, "_SrcBlendAlpha", (float)BlendMode.One);
            SetFloatIfPresent(material, "_DstBlendAlpha", (float)BlendMode.One);
            SetFloatIfPresent(material, "_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        // Alpha-blended material for soft effects (water blobs, dust clouds, textures with an alpha channel): it fades
        // through the alpha of its colour and of its vertex colours, drawn after the opaque world and double-sided, so
        // a flat picture reads from either side. Falls back to opaque unlit if the particle shader is missing.
        public static Material CreateAlphaBlend(string name, Color color)
        {
            Shader shader = Shader.Find(ParticlesUnlitShaderName);
            if (shader == null) return CreateUnlit(name, color);
            var material = new Material(shader) { name = name };
            SetBaseColor(material, color);
            SetFloatIfPresent(material, "_Surface", 1f);   // transparent
            SetFloatIfPresent(material, "_Blend", 0f);     // alpha
            SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            SetFloatIfPresent(material, "_SrcBlendAlpha", (float)BlendMode.One);
            SetFloatIfPresent(material, "_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            SetFloatIfPresent(material, "_ZWrite", 0f);
            SetFloatIfPresent(material, "_Cull", (float)CullMode.Off);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        // See-through unlit material for effect meshes (spheres, rings, cubes, pictures on quads): additive (adds light)
        // or alpha-blended, fading through the alpha of its colour, double-sided. Unlike the particle shader it ignores
        // vertex colours, which primitive meshes don't have. Falls back to opaque unlit if URP's Unlit can't go transparent.
        public static Material CreateUnlitTransparent(string name, Color color, bool additive)
        {
            Shader shader = FindShader(UnlitShaderName, LitShaderName);
            if (shader == null) return null;
            var material = new Material(shader) { name = name };
            SetBaseColor(material, color);
            SetFloatIfPresent(material, "_Surface", 1f);                    // transparent
            SetFloatIfPresent(material, "_Blend", additive ? 2f : 0f);      // additive or alpha
            SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloatIfPresent(material, "_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            SetFloatIfPresent(material, "_SrcBlendAlpha", (float)BlendMode.One);
            SetFloatIfPresent(material, "_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            SetFloatIfPresent(material, "_ZWrite", 0f);
            SetFloatIfPresent(material, "_Cull", (float)CullMode.Off);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        // Makes a transparent effect material draw both faces (a flat picture or a ring seen from inside).
        public static void SetDoubleSided(Material material)
        {
            if (material != null) SetFloatIfPresent(material, "_Cull", (float)CullMode.Off);
        }

        // Turns emission on (keyword + a non-black colour so URP keeps it on).
        public static void PrepareEmission(Material material)
        {
            if (material == null || !material.HasProperty(EmissionColorId)) return;
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor(EmissionColorId, new Color(EmissionFloor, EmissionFloor, EmissionFloor));
        }

        public static void SetBaseColor(Material material, Color color)
        {
            if (material == null) return;
            if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, color);
            else material.color = color;
        }

        // Sets emission, never letting it reach pure black (see EmissionFloor).
        public static void SetEmission(Material material, Color emission)
        {
            if (material == null || !material.HasProperty(EmissionColorId)) return;
            emission.r = Mathf.Max(emission.r, EmissionFloor);
            emission.g = Mathf.Max(emission.g, EmissionFloor);
            emission.b = Mathf.Max(emission.b, EmissionFloor);
            emission.a = 1f;
            material.SetColor(EmissionColorId, emission);
        }

        static void SetFloatIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }
    }
}
