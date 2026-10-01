using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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

        // The picture importer that does the linking (it also sets the import rules for Art/VFX). Called by name, so the
        // builder works the same whether or not that importer is in this project yet; without it nothing is linked.
        const string ImporterTypeName = "VaatusRevenge.EditorTools.VfxTextureImporter";
        const string RelinkMethodName = "RelinkLibrary";

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

        // Points every slot of the library at its picture by file name. Returns how many slots have one, or -1 when the
        // picture importer isn't in the project (nothing linked).
        public static int RelinkTextures(ElementVfxLibraryAsset library)
        {
            if (library == null) return 0;
            MethodInfo relink = FindRelink();
            if (relink == null) return -1;
            object found = relink.Invoke(null, new object[] { library });
            return found is int count ? count : 0;
        }

        static MethodInfo FindRelink()
        {
            Type importer = typeof(SandboxVfxAssets).Assembly.GetType(ImporterTypeName);
            if (importer == null) return null;
            MethodInfo method = importer.GetMethod(RelinkMethodName, BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(ElementVfxLibraryAsset) }, null);
            return method != null && method.ReturnType == typeof(int) ? method : null;
        }
    }
}
