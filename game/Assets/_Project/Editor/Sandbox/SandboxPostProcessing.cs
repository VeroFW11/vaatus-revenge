using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VaatusRevenge.EditorTools
{
    // Turns on URP post-processing in the sandbox scene: a global Volume using the project's sample profile
    // (bloom, tonemapping, vignette) and post-processing on the camera, which URP creates switched off. Bloom
    // gives the HDR fire effects and the enemies' wind-up glows their halo, which makes them much easier to read
    // mid-fight (playtest report BUILD-01).
    //
    // Why reflection: Volume and UniversalAdditionalCameraData live in URP's assemblies, which our assembly
    // definitions (and the offline compile check) don't reference. Looking the types up by name when the
    // builder runs keeps that dependency out of our code. If URP ever renames something, the build logs one
    // warning and carries on without bloom instead of failing.
    //
    // Note: the volume shares SampleSceneProfile with SampleScene, so editing the profile changes both scenes.
    public static class SandboxPostProcessing
    {
        public const string ProfilePath = "Assets/Settings/SampleSceneProfile.asset";
        const string VolumeTypeName = "UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime";
        const string CameraDataTypeName = "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime";
        const string VolumeObjectName = "Global Volume";
        const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

        // Adds the global volume (registered with Undo under undoName) and turns on the camera's post-processing.
        // Never throws. Returns true when everything was set up; otherwise it has logged one warning saying what's
        // missing and how to add it by hand.
        public static bool SetUp(Camera camera, string undoName)
        {
            var problems = new List<string>();
            try
            {
                AddGlobalVolume(undoName, problems);
                EnableCameraPostProcessing(camera, undoName, problems);
            }
            catch (Exception e)
            {
                problems.Add(e.GetType().Name + ": " + e.Message);
            }
            if (problems.Count == 0) return true;

            Debug.LogWarning("Fire Combat Sandbox: bloom isn't fully set up (" + string.Join("; ", problems) + "). Everything else "
                             + "works; the fire and wind-up glows just won't have a halo. To add it by hand: GameObject > Volume > "
                             + "Global Volume with the profile " + ProfilePath + ", then tick Rendering > Post Processing on the Main Camera.");
            return false;
        }

        static void AddGlobalVolume(string undoName, List<string> problems)
        {
            Type volumeType = FindComponentType(VolumeTypeName);
            if (volumeType == null)
            {
                problems.Add("URP's Volume component wasn't found");
                return;
            }
            Type profileType = MemberType(volumeType, "sharedProfile");
            if (profileType == null)
            {
                problems.Add("Volume.sharedProfile wasn't found");
                return;
            }
            UnityEngine.Object profile = AssetDatabase.LoadAssetAtPath(ProfilePath, profileType);
            if (profile == null)
            {
                problems.Add(ProfilePath + " is missing or isn't a volume profile");
                return;
            }

            var volumeObject = new GameObject(VolumeObjectName);
            Undo.RegisterCreatedObjectUndo(volumeObject, undoName);
            Component volume = volumeObject.AddComponent(volumeType);
            if (volume == null)
            {
                problems.Add("the Volume component couldn't be added");
                return;
            }
            // Global: applies everywhere in the scene, not just inside a trigger volume.
            SetMember(volume, "isGlobal", true, problems);
            SetMember(volume, "sharedProfile", profile, problems);
            EditorUtility.SetDirty(volume);
        }

        static void EnableCameraPostProcessing(Camera camera, string undoName, List<string> problems)
        {
            if (camera == null)
            {
                problems.Add("there's no camera to turn post-processing on for");
                return;
            }
            Type dataType = FindComponentType(CameraDataTypeName);
            if (dataType == null)
            {
                problems.Add("URP's camera settings component (UniversalAdditionalCameraData) wasn't found");
                return;
            }
            // URP normally adds this component the first time the camera renders; add it now so the setting saves.
            Component data = camera.GetComponent(dataType);
            if (data == null) data = Undo.AddComponent(camera.gameObject, dataType);
            if (data == null)
            {
                problems.Add("UniversalAdditionalCameraData couldn't be added to the camera");
                return;
            }
            SetMember(data, "renderPostProcessing", true, problems);
            EditorUtility.SetDirty(data);
        }

        // Finds a component type by its assembly-qualified name, falling back to searching the loaded assemblies
        // by full name (in case the assembly's name ever changes). Null when it isn't a Component or doesn't exist.
        static Type FindComponentType(string assemblyQualifiedName)
        {
            Type type = Type.GetType(assemblyQualifiedName, false);
            if (type == null)
            {
                string fullName = assemblyQualifiedName.Split(',')[0].Trim();
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length && type == null; i++) type = assemblies[i].GetType(fullName, false);
            }
            return type != null && typeof(Component).IsAssignableFrom(type) ? type : null;
        }

        // The type of a public property or field (URP has moved members between the two across versions).
        static Type MemberType(Type type, string memberName)
        {
            PropertyInfo property = type.GetProperty(memberName, PublicInstance);
            if (property != null) return property.PropertyType;
            FieldInfo field = type.GetField(memberName, PublicInstance);
            return field != null ? field.FieldType : null;
        }

        static void SetMember(object target, string memberName, object value, List<string> problems)
        {
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(memberName, PublicInstance);
            if (property != null && property.CanWrite && property.PropertyType.IsInstanceOfType(value))
            {
                property.SetValue(target, value, null);
                return;
            }
            FieldInfo field = type.GetField(memberName, PublicInstance);
            if (field != null && field.FieldType.IsInstanceOfType(value))
            {
                field.SetValue(target, value);
                return;
            }
            problems.Add(type.Name + "." + memberName + " couldn't be set");
        }
    }
}
