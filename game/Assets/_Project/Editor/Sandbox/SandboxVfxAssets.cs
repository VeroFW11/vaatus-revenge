using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VaatusRevenge.EditorTools
{
    // The element effects' assets for the sandbox: the Element VFX Library (Art/VFX/ElementVfxLibrary.asset: each element's
    // HUD colour and effect pictures) and the two see-through materials the effects draw with (Materials/VfxAdditive.mat,
    // VfxAlphaBlend.mat), which the library references so a player build keeps their shaders.
    //
    // Pictures are linked by file name (Art/VFX/<Element>/<slot>.png, see Art/VFX/README.md) on every build and from
    // Vaatu's Revenge > Refresh VFX Textures. Colours edited by hand in the library are always kept. No pictures at all is
    // fine: every effect has a primitive stand-in.
    public static class SandboxVfxAssets
    {
        public const string Folder = "Assets/_Project/Art/VFX";
        public const string LibraryPath = Folder + "/ElementVfxLibrary.asset";
        public const string AdditiveMaterialName = "VfxAdditive";
        public const string AlphaBlendMaterialName = "VfxAlphaBlend";

        // Loads the library (creating it if missing: created receives its path) with its two materials, and links the
        // pictures that are there.
        public static ElementVfxLibraryAsset LoadOrCreate(List<string> created)
        {
            GreyboxMaterials.EnsureFolder(Folder);
            var library = AssetDatabase.LoadAssetAtPath<ElementVfxLibraryAsset>(LibraryPath);
            if (library == null)
            {
                if (File.Exists(LibraryPath) || AssetDatabase.LoadMainAssetAtPath(LibraryPath) != null)
                {
                    throw new InvalidOperationException(LibraryPath + " exists but isn't an Element VFX Library. Rename or move that "
                                                        + "file (the builder never overwrites files it doesn't recognise), then try again.");
                }
                library = ScriptableObject.CreateInstance<ElementVfxLibraryAsset>();
                AssetDatabase.CreateAsset(library, LibraryPath);
                if (created != null) created.Add(LibraryPath);
            }

            bool changed = false;
            if (library.AdditiveMaterial == null)
            {
                library.AdditiveMaterial = GreyboxMaterials.GetOrCreateEffect(AdditiveMaterialName, true);
                changed = true;
            }
            if (library.AlphaBlendMaterial == null)
            {
                library.AlphaBlendMaterial = GreyboxMaterials.GetOrCreateEffect(AlphaBlendMaterialName, false);
                changed = true;
            }
            if (changed)
            {
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssets();
            }
            RelinkTextures(library);
            return library;
        }

        // Points every slot of the library at its picture by file name and returns how many slots have one. The linking is
        // the picture importer's (VfxTextureImporter, which also sets the import rules for Art/VFX).
        public static int RelinkTextures(ElementVfxLibraryAsset library)
        {
            return library != null ? VfxTextureImporter.RelinkLibrary(library) : 0;
        }
    }
}
