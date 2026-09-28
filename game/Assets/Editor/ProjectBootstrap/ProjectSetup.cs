using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProjectBootstrap
{
    // One-off project setup: names the game, creates the folder layout, imports and saves.
    // Run: Unity.exe -batchmode -quit -projectPath game -executeMethod ProjectBootstrap.ProjectSetup.Run
    public static class ProjectSetup
    {
        static readonly string[] Folders =
        {
            "Assets/_Project/Scenes",
            "Assets/_Project/Scripts/Player",
            "Assets/_Project/Scripts/Combat",
            "Assets/_Project/Scripts/Bending",
            "Assets/_Project/Scripts/Enemies",
            "Assets/_Project/Scripts/Camera",
            "Assets/_Project/Tuning",
            "Assets/_Project/Prefabs",
            "Assets/_Project/Animations",
            "Assets/_Project/Materials",
            "Assets/_Project/Models",
            "Assets/_Project/Input",
        };

        public static void Run()
        {
            PlayerSettings.productName = "Vaatu's Revenge";
            PlayerSettings.companyName = "Vaatu's Revenge Team";

            foreach (var folder in Folders)
            {
                Directory.CreateDirectory(folder);
                // Unity doesn't track empty folders in git; a .keep file holds each one in place.
                var keep = Path.Combine(folder, ".keep");
                if (!File.Exists(keep)) File.WriteAllText(keep, "");
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] Named the game, created folders, imported and saved.");
            EditorApplication.Exit(0);
        }
    }
}
