using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VaatusRevenge
{
    // Shows the procedural body's "look layer" (hit flashes, attack wind-up glow, the dodge i-frame shimmer, the fa jin
    // charge glow, darkening on stagger and death) on the imported model that replaced it.
    //
    // HumanoidBody keeps those states private and writes them to its own (now hidden) shapes every frame through
    // property blocks, so this reads them back from three of its shapes, one per glow group: the chest (body), a
    // forearm (limbs) and the right fist (fists). Nothing in HumanoidBody had to change, and anything that calls
    // Flash / SetTelegraph / SetDead on the body keeps working unchanged.
    //
    // Two ways onto the model, chosen to work whatever shader the importer gave it:
    //   - Glow: a second copy of each skinned mesh, drawn additively in one colour on top of the model and switched on
    //     only while something glows. (Emission on the model's own material would depend on the importer's shader
    //     having an emission input switched on; glTFast's shaders and texture defaults vary between versions.)
    //   - Darkening: the model's base colour multiplied down through a MaterialPropertyBlock (the materials themselves
    //     are never changed). Shaders without a known base colour property keep their colour; the glow still works.
    // A model is one mesh for the whole body, so the limb and fist glows can't stay on the limbs: they're shared
    // over the whole model by Settings.LimbShare and FistShare.
    public sealed class SkinnedAvatarTint
    {
        [Serializable]
        public class Settings
        {
            [Tooltip("How much of the limbs' glow (an attack winding up, which lights the hands and feet fully and the torso a little) shows on the whole model. 0 = the torso's, 1 = the limbs'.")]
            [Range(0f, 1f)] public float LimbShare = 0.5f;
            [Tooltip("How much of the fists-only glow (fa jin charge) shows on the whole model.")]
            [Range(0f, 1f)] public float FistShare = 0.3f;
            [Tooltip("Brightness of the glow overlay relative to the procedural body's emission.")]
            [Min(0f)] public float GlowStrength = 0.6f;
            [Tooltip("Glows dimmer than this (in any colour channel) aren't drawn at all.")]
            [Min(0f)] public float GlowThreshold = 0.01f;
            [Tooltip("Darken the model when the procedural body darkens (stagger, death).")]
            public bool Darken = true;
        }

        // The shapes HumanoidBody builds (CreateVisual names them part + "Mesh"), one per glow group.
        public const string BodySampleName = "ChestShapeMesh";
        public const string LimbSampleName = "LeftForearmMesh";
        public const string FistSampleName = "RightFistShapeMesh";
        public const string OverlaySuffix = " Glow";

        // Base colour properties, most likely first: glTFast's glTF shaders, URP Lit/Unlit, the built-in Standard.
        static readonly string[] BaseColorNames = { "baseColorFactor", "_BaseColor", "_Color" };

        readonly Settings settings;
        readonly Renderer bodySample, limbSample, fistSample;
        readonly Color bodyBase = Color.white;
        readonly MaterialPropertyBlock read = new MaterialPropertyBlock();
        readonly MaterialPropertyBlock write = new MaterialPropertyBlock();
        readonly List<Target> targets = new List<Target>();
        readonly List<GameObject> overlays = new List<GameObject>();
        readonly List<Renderer> overlayRenderers = new List<Renderer>();
        Material overlayMaterial;
        bool overlayOn;
        float lastDim = 1f;

        sealed class Target
        {
            public Renderer Renderer;
            public int MaterialIndex;
            public int BaseColorId;
            public Color BaseColor;
        }

        public bool HasSamples => bodySample != null;

        public SkinnedAvatarTint(Transform proceduralRoot, IList<SkinnedMeshRenderer> renderers, Settings settings)
        {
            this.settings = settings ?? new Settings();
            if (proceduralRoot != null)
            {
                foreach (Renderer r in proceduralRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name == BodySampleName) bodySample = r;
                    else if (r.name == LimbSampleName) limbSample = r;
                    else if (r.name == FistSampleName) fistSample = r;
                }
            }
            if (bodySample != null && bodySample.sharedMaterial != null) bodyBase = BaseColorOf(bodySample.sharedMaterial);

            foreach (SkinnedMeshRenderer smr in renderers)
            {
                if (smr == null) continue;
                Material[] materials = smr.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material m = materials[i];
                    if (m == null) continue;
                    foreach (string property in BaseColorNames)
                    {
                        if (!m.HasProperty(property)) continue;
                        int id = Shader.PropertyToID(property);
                        targets.Add(new Target { Renderer = smr, MaterialIndex = i, BaseColorId = id, BaseColor = m.GetColor(id) });
                        break;
                    }
                }
                CreateOverlay(smr);
            }
        }

        // Call once per frame after the body's look layer has run (HumanoidBody updates it in Update).
        public void Update()
        {
            Color body = Emission(bodySample, out float dim);
            Color limb = Emission(limbSample, out _);
            Color fist = Emission(fistSample, out _) - limb;   // the fist shape shows limb glow plus the fist-only glow
            Color glow = Color.Lerp(body, limb, settings.LimbShare) + Positive(fist) * settings.FistShare;
            glow *= settings.GlowStrength;
            glow.a = 1f;

            bool on = Max(glow) > settings.GlowThreshold && overlayMaterial != null;
            if (on) SetOverlayColour(glow);
            if (on != overlayOn)
            {
                overlayOn = on;
                for (int i = 0; i < overlayRenderers.Count; i++)
                    if (overlayRenderers[i] != null) overlayRenderers[i].enabled = on;
            }

            if (!settings.Darken) dim = 1f;
            if (Mathf.Abs(dim - lastDim) > 1e-4f) ApplyDim(dim);
        }

        // Back to the model's own colours (on disable and destroy).
        public void Clear()
        {
            if (lastDim < 0.9999f) ApplyDim(1f);
            overlayOn = false;
            for (int i = 0; i < overlayRenderers.Count; i++)
                if (overlayRenderers[i] != null) overlayRenderers[i].enabled = false;
        }

        public void Dispose()
        {
            Clear();
            for (int i = 0; i < overlays.Count; i++) GreyboxShapes.SafeDestroy(overlays[i]);
            overlays.Clear();
            overlayRenderers.Clear();
            GreyboxShapes.SafeDestroy(overlayMaterial);
            overlayMaterial = null;
        }

        // The emission a sample shape shows right now (HDR), and how much its base colour is darkened.
        Color Emission(Renderer sample, out float dim)
        {
            dim = 1f;
            if (sample == null || !sample.HasPropertyBlock()) return Color.black;
            sample.GetPropertyBlock(read);
            if (read.isEmpty) return Color.black;
            Color shown = read.GetColor(GreyboxShapes.BaseColorId);
            float reference = Max(bodyBase);
            if (reference > 1e-4f) dim = Mathf.Clamp01(Max(shown) / reference);
            Color e = read.GetColor(GreyboxShapes.EmissionColorId);
            float floor = GreyboxShapes.EmissionFloor;
            return new Color(Mathf.Max(0f, e.r - floor), Mathf.Max(0f, e.g - floor), Mathf.Max(0f, e.b - floor), 1f);
        }

        void ApplyDim(float dim)
        {
            lastDim = dim;
            for (int i = 0; i < targets.Count; i++)
            {
                Target t = targets[i];
                if (t.Renderer == null) continue;
                // An empty block means "no overrides": the material's own colour shows again.
                write.Clear();
                if (dim < 0.9999f)
                {
                    Color c = t.BaseColor * dim;
                    c.a = t.BaseColor.a;
                    write.SetColor(t.BaseColorId, c);
                }
                t.Renderer.SetPropertyBlock(write, t.MaterialIndex);
            }
        }

        // An additive copy of the skinned mesh: same mesh, same bones, so it deforms exactly like the model.
        void CreateOverlay(SkinnedMeshRenderer source)
        {
            if (source.sharedMesh == null) return;
            if (overlayMaterial == null) overlayMaterial = CreateOverlayMaterial();
            if (overlayMaterial == null) return;

            var go = new GameObject(source.name + OverlaySuffix);
            go.layer = source.gameObject.layer;
            go.transform.SetParent(source.transform, false);
            SkinnedMeshRenderer copy = go.AddComponent<SkinnedMeshRenderer>();
            copy.sharedMesh = source.sharedMesh;
            copy.bones = source.bones;
            copy.rootBone = source.rootBone;
            copy.localBounds = source.localBounds;
            copy.updateWhenOffscreen = source.updateWhenOffscreen;
            var materials = new Material[Mathf.Max(1, source.sharedMesh.subMeshCount)];
            for (int i = 0; i < materials.Length; i++) materials[i] = overlayMaterial;
            copy.sharedMaterials = materials;
            copy.shadowCastingMode = ShadowCastingMode.Off;
            copy.receiveShadows = false;
            copy.lightProbeUsage = LightProbeUsage.Off;
            copy.reflectionProbeUsage = ReflectionProbeUsage.Off;
            copy.enabled = false;
            overlays.Add(go);
            overlayRenderers.Add(copy);
        }

        // URP Unlit, transparent, additive, no depth write: it adds light on top of the model and nothing else.
        static Material CreateOverlayMaterial()
        {
            Shader shader = GreyboxShapes.FindShader(GreyboxShapes.UnlitShaderName, null);
            if (shader == null) return null;
            var material = new Material(shader) { name = "Avatar glow overlay (runtime)" };
            SetFloat(material, "_Surface", 1f);   // transparent
            SetFloat(material, "_Blend", 2f);     // additive
            SetFloat(material, "_SrcBlend", (float)BlendMode.One);
            SetFloat(material, "_DstBlend", (float)BlendMode.One);
            SetFloat(material, "_SrcBlendAlpha", (float)BlendMode.Zero);
            SetFloat(material, "_DstBlendAlpha", (float)BlendMode.One);
            SetFloat(material, "_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            GreyboxShapes.SetBaseColor(material, Color.black);
            return material;
        }

        void SetOverlayColour(Color glow)
        {
            GreyboxShapes.SetBaseColor(overlayMaterial, glow);
        }

        static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        static Color BaseColorOf(Material material)
        {
            if (material.HasProperty(GreyboxShapes.BaseColorId)) return material.GetColor(GreyboxShapes.BaseColorId);
            return material.HasProperty("_Color") ? material.color : Color.white;
        }

        static Color Positive(Color c)
        {
            return new Color(Mathf.Max(0f, c.r), Mathf.Max(0f, c.g), Mathf.Max(0f, c.b), 1f);
        }

        static float Max(Color c)
        {
            return Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        }
    }
}
