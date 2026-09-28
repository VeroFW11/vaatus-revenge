using UnityEditor;
using UnityEngine;

namespace VaatusRevenge.EditorTools
{
    // Grey-box material assets, saved as real .mat files in Assets/_Project/Materials so scenes reference
    // them properly (a material made in code and never saved would be embedded in the scene file instead).
    public static class GreyboxMaterials
    {
        public const string Folder = "Assets/_Project/Materials";
        // Emissive materials glow at this fraction of their colour: visible in shadow, below the bloom threshold.
        const float EmissiveStrength = 0.3f;

        // Loads Assets/_Project/Materials/Greybox_<name>.mat, or creates it (URP Lit, this colour) if missing.
        // An existing material is returned untouched, so colours tweaked by hand survive a rebuild; delete
        // the asset to regenerate it from code. Returns null only if URP's shaders can't be found.
        public static Material GetOrCreate(string name, Color color, bool emissive)
        {
            string assetName = "Greybox_" + Sanitize(name);
            string path = Folder + "/" + assetName + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Material material = GreyboxShapes.CreateLit(assetName, color, emissive);
            if (material == null) return null;
            if (emissive) GreyboxShapes.SetEmission(material, color * EmissiveStrength);
            EnsureFolder(Folder);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // Creates every missing folder along an "Assets/..." path.
        public static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i])) continue;
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unnamed";
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') chars[i] = '_';
            }
            return new string(chars);
        }
    }
}
