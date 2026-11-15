using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

#if HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace LightingTools.Editor
{
    public static class LightingSettingsMenuItems
    {
#if HDRP
        private const string AssignVolumeMenu = "GameObject/Light/Assign Volume to Open Scenes";
        internal const string StaticSkyObjectName = "Runtime-StaticLightingSky";

        [MenuItem(AssignVolumeMenu, true)]
        private static bool ValidateAssignSelectedVolume()
        {
            return Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<Volume>() != null;
        }

        [MenuItem(AssignVolumeMenu, false, priority = -10)]
        private static void AssignSelectedVolume()
        {
            Volume volume = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Volume>() : null;
            if (volume == null || volume.sharedProfile == null)
            {
                Debug.LogError("Assign Volume: the selected GameObject needs a Volume component with a profile assigned.");
                return;
            }

            AssignVolumeToLoadedScenes(volume);
        }

        /// <summary>
        /// Uses the volume's profile as the Static Lighting Sky of every loaded scene, and copies the volume
        /// scene's LightingSettings asset to every loaded scene. Logs what it changed and what it skipped.
        /// </summary>
        internal static void AssignVolumeToLoadedScenes(Volume volume)
        {
            VolumeProfile profile = volume.sharedProfile;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Assign Volume to Open Scenes");

            foreach (Scene scene in GetLoadedScenes())
            {
                AssignEnvironmentProfile(scene, profile);
            }
            Debug.Log($"Updated HDRP Environment Profile to: {profile.name}", profile);

            LightingSettings activeLightingSettings = Lightmapping.GetLightingSettingsForScene(volume.gameObject.scene);
            if (activeLightingSettings != null)
            {
                foreach (Scene scene in GetLoadedScenes())
                {
                    Lightmapping.SetLightingSettingsForScene(scene, activeLightingSettings);
                }
                Debug.Log($"Updated LightingSettings to: {activeLightingSettings.name}", activeLightingSettings);
            }
            else
            {
                Debug.LogWarning($"Assign Volume: scene '{volume.gameObject.scene.name}' has no Lighting Settings asset, " +
                                 "so Lighting Settings were not copied to the other scenes. Assign one in Window > Rendering > Lighting.");
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkAllScenesDirty();
        }

        internal static IEnumerable<Scene> GetLoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded) yield return scene;
            }
        }

        /// <summary>All StaticLightingSky components in a loaded scene, including hidden and inactive ones.</summary>
        internal static List<StaticLightingSky> FindStaticLightingSkies(Scene scene)
        {
            var result = new List<StaticLightingSky>();
            if (!scene.IsValid() || !scene.isLoaded) return result;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                result.AddRange(root.GetComponentsInChildren<StaticLightingSky>(true));
            }
            return result;
        }

        internal static void AssignEnvironmentProfile(Scene scene, VolumeProfile profile)
        {
            List<StaticLightingSky> skies = FindStaticLightingSkies(scene);
            StaticLightingSky staticSky = skies.Count > 0 ? skies[0] : null;
            if (skies.Count > 1)
            {
                Debug.LogWarning($"Scene '{scene.name}' has {skies.Count} Static Lighting Sky components; using the one on " +
                                 $"'{staticSky.gameObject.name}'. Use the APV Bake Checklist to remove the duplicates.", staticSky);
            }

            if (staticSky == null)
            {
                GameObject go = new GameObject(StaticSkyObjectName) { hideFlags = HideFlags.HideInHierarchy };
                SceneManager.MoveGameObjectToScene(go, scene);
                Undo.RegisterCreatedObjectUndo(go, "Create Static Lighting Sky");
                staticSky = go.AddComponent<StaticLightingSky>();
            }

            Undo.RecordObject(staticSky, "Change HDRP Environment Profile");
            staticSky.profile = profile;
            staticSky.staticLightingSkyUniqueID = ResolveSkyUniqueID(profile, staticSky.staticLightingSkyUniqueID);
            EditorUtility.SetDirty(staticSky);
        }

        /// <summary>
        /// The sky type the profile's Visual Environment selects. Without a Visual Environment, uses the first
        /// sky in the profile, and otherwise keeps the current ID, logging a warning instead of guessing.
        /// </summary>
        private static int ResolveSkyUniqueID(VolumeProfile profile, int currentID)
        {
            if (profile.TryGet(out VisualEnvironment ve)) return ve.skyType.value;

            foreach (VolumeComponent component in profile.components)
            {
                if (component is SkySettings sky)
                {
                    Debug.LogWarning($"Profile '{profile.name}' has no Visual Environment override; using its {sky.GetType().Name} as the static lighting sky.", profile);
                    return SkySettings.GetUniqueID(sky.GetType());
                }
            }

            Debug.LogWarning($"Profile '{profile.name}' has no Visual Environment or sky override, so the static lighting sky type was left unchanged.", profile);
            return currentID;
        }
#endif
    }
}
