using System;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // One effect slot's texture (Art/VFX/<Element>/<slot>.png, see Art/VFX/README.md).
    [Serializable]
    public class SlotTexture
    {
        public string Slot;          // lower-case slot name, e.g. "splash"
        public Texture2D Texture;
        public bool HasAlpha;        // false: black background, drawn additive; true: drawn alpha-blended
    }

    // One element's look: its HUD colour and its effect textures.
    [Serializable]
    public class ElementVfxEntry
    {
        public ElementId Element;
        public Color HudColor = Color.white;
        public SlotTexture[] Textures = new SlotTexture[0];
    }

    // Per-element looks for the move effects: the HUD colour and the textures for each effect slot. Every slot has a
    // primitive fallback, so an empty library still renders.
    // SHELL (Build 05 package A): the fields are the contract; package C fills in the behaviour.
    [CreateAssetMenu(menuName = "Vaatu's Revenge/VFX/Element VFX Library", fileName = "ElementVfxLibrary")]
    public class ElementVfxLibraryAsset : ScriptableObject
    {
        public ElementVfxEntry[] Elements = new ElementVfxEntry[0];

        [Tooltip("Optional. Created by the sandbox builder so player builds keep the shaders the effects need.")]
        public Material AdditiveMaterial, AlphaBlendMaterial;
    }
}
