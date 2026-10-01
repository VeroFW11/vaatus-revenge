using System.IO;
using UnityEditor;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge.EditorTools
{
    // Imports the element effect pictures (Assets/_Project/Art/VFX/<Element>/<slot>.png, see Art/VFX/README.md) the way
    // the effects need them, and keeps the Element VFX Library pointing at them.
    //
    // Import settings for anything under Art/VFX: sRGB colour, mipmaps (they are seen small and far as often as close),
    // at most 512 px, alpha is transparency, and Repeat for the pictures stretched along trails ('ribbon', 'streak'),
    // Clamp for the rest so a burst's edge never bleeds round to the other side.
    //
    // RelinkLibrary fills the library's slot rows by file name and works out, per picture, whether it really uses its
    // alpha (an RGBA file with a solid black background doesn't): pictures with alpha are drawn alpha-blended, the others
    // additive (black disappears, bright parts glow). Colours edited by hand are kept. The sandbox builder calls it on
    // every build and from Vaatu's Revenge > Refresh VFX Textures; it also runs by itself shortly after pictures are
    // added, changed or removed, when the library exists at LibraryPath.
    public sealed class VfxTextureImporter : AssetPostprocessor
    {
        public const string Folder = "Assets/_Project/Art/VFX";
        public const string LibraryPath = Folder + "/ElementVfxLibrary.asset";
        const int MaxSize = 512;
        const byte OpaqueAlpha = 250;        // a pixel at or above this counts as solid
        const int AlphaSampleStride = 7;     // look at every 7th pixel: plenty to tell a cut-out from a solid background

        static bool relinkQueued;

        void OnPreprocessTexture()
        {
            if (!IsVfxPicture(assetPath)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = MaxSize;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            importer.wrapMode = Repeats(Path.GetFileNameWithoutExtension(assetPath)) ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!Touches(imported) && !Touches(deleted) && !Touches(moved) && !Touches(movedFrom)) return;
            if (relinkQueued) return;
            relinkQueued = true;
            // Change assets after the import has finished, not in the middle of it.
            EditorApplication.delayCall += RelinkDefaultLibrary;
        }

        static void RelinkDefaultLibrary()
        {
            relinkQueued = false;
            var library = AssetDatabase.LoadAssetAtPath<ElementVfxLibraryAsset>(LibraryPath);
            if (library != null) RelinkLibrary(library);
        }

        // Points every slot of the library at Art/VFX/<Folder>/<slot>.png (none = empty slot, the stand-in draws) and saves
        // it when anything changed. Returns how many slots have a picture.
        public static int RelinkLibrary(ElementVfxLibraryAsset library)
        {
            if (library == null) return 0;
            bool changed = library.EnsureDefaults();
            int found = 0;
            for (int i = 0; i < library.Elements.Length; i++)
            {
                ElementVfxEntry entry = library.Elements[i];
                if (entry == null || entry.Textures == null) continue;
                string folder = ElementVfx.FolderName(entry.Element);
                for (int t = 0; t < entry.Textures.Length; t++)
                {
                    SlotTexture row = entry.Textures[t];
                    if (row == null || string.IsNullOrEmpty(row.Slot)) continue;
                    string path = Folder + "/" + folder + "/" + row.Slot.ToLowerInvariant() + ".png";
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    bool hasAlpha = texture != null && UsesAlpha(path);
                    if (texture != null) found++;
                    if (row.Texture == texture && row.HasAlpha == hasAlpha) continue;
                    row.Texture = texture;
                    row.HasAlpha = hasAlpha;
                    changed = true;
                }
            }
            if (changed)
            {
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssets();
            }
            return found;
        }

        // Does the picture really use its alpha channel? Reads the file itself (the imported texture isn't readable): a
        // PNG with no alpha channel, or one whose alpha is solid everywhere, is drawn additive.
        static bool UsesAlpha(string assetPath)
        {
            string file = Path.GetFullPath(assetPath);
            if (!File.Exists(file)) return false;
            var probe = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                if (!probe.LoadImage(File.ReadAllBytes(file), false)) return false;
                Color32[] pixels = probe.GetPixels32();
                for (int i = 0; i < pixels.Length; i += AlphaSampleStride)
                {
                    if (pixels[i].a < OpaqueAlpha) return true;
                }
                return false;
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        static bool Touches(string[] paths)
        {
            if (paths == null) return false;
            for (int i = 0; i < paths.Length; i++)
            {
                if (IsVfxPicture(paths[i])) return true;
            }
            return false;
        }

        static bool IsVfxPicture(string path)
        {
            return !string.IsNullOrEmpty(path) && path.StartsWith(Folder + "/", System.StringComparison.Ordinal)
                   && path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase);
        }

        // Pictures stretched along a trail tile; everything else stops at its edge.
        static bool Repeats(string slot)
        {
            return string.Equals(slot, ElementVfx.SlotName(VfxSlot.Ribbon), System.StringComparison.OrdinalIgnoreCase)
                   || string.Equals(slot, ElementVfx.SlotName(VfxSlot.Streak), System.StringComparison.OrdinalIgnoreCase);
        }

        // Domain reload is off, so the queued flag would survive into the next session: clear it when scripts load.
        [InitializeOnLoadMethod]
        static void ResetStatics()
        {
            relinkQueued = false;
        }
    }
}
