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

    // One element's look: its HUD colour and its effect textures. Element None holds the Common pictures (the beat ring,
    // the danger mark, hit sparks).
    [Serializable]
    public class ElementVfxEntry
    {
        public ElementId Element;
        public Color HudColor = Color.white;
        public SlotTexture[] Textures = new SlotTexture[0];
    }

    // Per-element looks for the move effects: the HUD colour and the textures for each effect slot. Every slot has a
    // primitive fallback, so an empty library still renders. ElementVfxBootstrap hands it to ElementVfx when play starts.
    // The sandbox builder keeps it at Art/VFX/ElementVfxLibrary.asset and re-links the pictures by file name on every
    // build (VfxTextureImporter.RelinkLibrary, which keeps colours edited by hand).
    [CreateAssetMenu(menuName = "Vaatu's Revenge/VFX/Element VFX Library", fileName = "ElementVfxLibrary")]
    public class ElementVfxLibraryAsset : ScriptableObject
    {
        public ElementVfxEntry[] Elements = new ElementVfxEntry[0];

        [Tooltip("Optional. Created by the sandbox builder so player builds keep the shaders the effects need.")]
        public Material AdditiveMaterial, AlphaBlendMaterial;

        // The HUD colours the spec gives each element (the element wheel, the combo counter, MIX icons).
        public static Color DefaultHudColor(ElementId element)
        {
            switch (element)
            {
                case ElementId.Fire: return new Color(1f, 0.38f, 0.12f);
                case ElementId.Water: return new Color(0.25f, 0.62f, 1f);
                case ElementId.Earth: return new Color(0.45f, 0.75f, 0.25f);
                case ElementId.Air: return new Color(0.95f, 0.88f, 0.6f);
                default: return Color.white;
            }
        }

        public bool TryGetEntry(ElementId element, out ElementVfxEntry entry)
        {
            if (Elements != null)
            {
                for (int i = 0; i < Elements.Length; i++)
                {
                    if (Elements[i] != null && Elements[i].Element == element)
                    {
                        entry = Elements[i];
                        return true;
                    }
                }
            }
            entry = null;
            return false;
        }

        // Makes sure there is one entry per element plus Common (None), each with a texture row for every slot it
        // expects. New entries get the default HUD colour; existing colours and textures are kept. Returns true when
        // anything was added (so the caller saves the asset).
        public bool EnsureDefaults()
        {
            bool changed = false;
            for (int folder = 0; folder <= (int)ElementId.Air; folder++)
            {
                var element = (ElementId)folder;
                if (!TryGetEntry(element, out ElementVfxEntry entry))
                {
                    entry = new ElementVfxEntry { Element = element, HudColor = DefaultHudColor(element) };
                    Array.Resize(ref Elements, (Elements != null ? Elements.Length : 0) + 1);
                    Elements[Elements.Length - 1] = entry;
                    changed = true;
                }
                if (entry.Textures == null)
                {
                    entry.Textures = new SlotTexture[0];
                    changed = true;
                }
                int slots = ElementVfx.SlotCount(element);
                for (int i = 0; i < slots; i++)
                {
                    string name = ElementVfx.SlotName(ElementVfx.SlotAt(element, i));
                    if (FindSlot(entry, name) != null) continue;
                    Array.Resize(ref entry.Textures, entry.Textures.Length + 1);
                    entry.Textures[entry.Textures.Length - 1] = new SlotTexture { Slot = name };
                    changed = true;
                }
            }
            return changed;
        }

        // The texture row for a slot name in an entry (null if it has none).
        public static SlotTexture FindSlot(ElementVfxEntry entry, string slot)
        {
            if (entry == null || entry.Textures == null) return null;
            for (int i = 0; i < entry.Textures.Length; i++)
            {
                SlotTexture row = entry.Textures[i];
                if (row != null && string.Equals(row.Slot, slot, StringComparison.OrdinalIgnoreCase)) return row;
            }
            return null;
        }
    }
}
