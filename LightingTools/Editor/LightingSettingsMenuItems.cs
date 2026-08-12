using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace LightingTools.Editor
{
    public class LightingSettingsMenuItems : EditorWindow
    {
        [MenuItem("GameObject/Light/Assign Volume to Open Scenes", false, priority = -10)]
        private static void AssignSelectedVolume()
        {
            Volume volume = Selection.activeGameObject.GetComponent<Volume>();
            if (volume == null || volume.sharedProfile == null)
            {
                Debug.LogError("Selected GameObject does not have a Volume component with a valid profile.");
                return;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                AssignEnvironmentProfile(SceneManager.GetSceneAt(i), volume.sharedProfile);
            }

            Debug.Log($"Updated HDRP Environment Profile to: {volume.sharedProfile.name}");

            LightingSettings activeLightingSettings = Lightmapping.GetLightingSettingsForScene(volume.gameObject.scene);
            if (activeLightingSettings != null)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    AssignLightingSettings(SceneManager.GetSceneAt(i), activeLightingSettings);
                }

                Debug.Log($"Updated LightingSettings to: {activeLightingSettings.name}");
            }

            EditorSceneManager.MarkAllScenesDirty();
        }

        private static void AssignEnvironmentProfile(Scene scene, VolumeProfile profile)
        {
            GameObject[] rootGameObjects = scene.GetRootGameObjects();
            StaticLightingSky staticSky = null;
            for (int i = 0; i < rootGameObjects.Length; i++)
            {
                staticSky = rootGameObjects[i].GetComponentInChildren<StaticLightingSky>();
            }

            if (staticSky == null)
            {
                GameObject go = new GameObject("Runtime-StaticLightingSky") { hideFlags = HideFlags.HideInHierarchy };
                staticSky = go.AddComponent<StaticLightingSky>();
                EditorSceneManager.MoveGameObjectToScene(go, scene);
            }

            Undo.RecordObject(staticSky, "Change HDRP Environment Profile");
            staticSky.profile = profile;
            int skyID = SkySettings.GetUniqueID<HDRISky>();
            if (profile.TryGet(out VisualEnvironment ve))
            {
                skyID = ve.skyType.value;
            }
            staticSky.staticLightingSkyUniqueID = skyID;
            EditorUtility.SetDirty(staticSky);
        }

        private static void AssignLightingSettings(Scene scene, LightingSettings settings)
        {
            Lightmapping.SetLightingSettingsForScene(scene, settings);
        }
    }
}