using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace SceneManagement
{
    [DefaultExecutionOrder(-10000)]
    public class AdditiveSceneManager : MonoBehaviour
    {
        [field: SerializeField, ValueDropdown("GetAllScenes"), InlineButton("AddNewScene", "New")] public List<string> SceneList { get; private set; }

        private void Awake()
        {
            LoadSceneList();
        }

        [Button]
        public void LoadSceneList()
        {
            List<string> loaded = new List<string>();

            if (Application.isPlaying)
            {
                foreach (string scenePath in SceneList)
                {
                    string sceneName = Path.GetFileNameWithoutExtension(scenePath);
                    if (!CheckIfSceneLoaded(sceneName))
                    {
                        SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
                        loaded.Add(sceneName);
                    }
                }
                return;
            }

#if UNITY_EDITOR
            foreach (string scenePath in SceneList)
            {
                string sceneName = Path.GetFileNameWithoutExtension(scenePath);
                if (!CheckIfSceneLoaded(sceneName))
                {
                    Scene loadedScene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
                    SetExpanded(loadedScene, false);
                    loaded.Add(sceneName);
                }
            }
#endif

            Debug.Log($"AdditiveSceneManager loading:");
            foreach (string sceneName in loaded)
            {
                Debug.Log($"    {sceneName}");
            }
        }

        private bool CheckIfSceneLoaded(string sceneName)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.name.Equals(sceneName)) return true;
            }

            return false;
        }

        private void AddNewScene()
        {
#if UNITY_EDITOR
            Scene activeScene = SceneManager.GetActiveScene();
            string path = activeScene.path.Replace(activeScene.name + ".unity", "");
            Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            int index = GetLoadedSceneIndex(newScene);
            if (index == -1)
            {
                Debug.LogError("Scene failed to load correctly");
                return;
            }

            newScene.name = $"{activeScene.name}-{index}";
            EditorSceneManager.SaveScene(newScene, path + "/" + newScene.name + ".unity");
            EditorSceneManager.SetActiveScene(activeScene);
            SceneList.Add(newScene.path);

            Debug.Log($"New scene added: {newScene.name}");
#endif
        }

        private int GetLoadedSceneIndex(Scene scene)
        {
            for(int i = 0; i < SceneManager.loadedSceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i) == scene) return i;
            }

            return -1;
        }

#if UNITY_EDITOR
        private static IEnumerable GetAllScenes()
        {
            return UnityEditor.AssetDatabase.FindAssets("t:Scene")
                .Select(x => UnityEditor.AssetDatabase.GUIDToAssetPath(x))
                .Select(x => new ValueDropdownItem(x, x));
        }

        private string GetScenePath(string sceneName)
        {
            return UnityEditor.AssetDatabase.FindAssets("t:Scene")
                .Select(x => UnityEditor.AssetDatabase.GUIDToAssetPath(x)).FirstOrDefault();
        }

        private void SetExpanded(Scene scene, bool expand)
        {

            foreach (var window in Resources.FindObjectsOfTypeAll<SearchableEditorWindow>())
            {
                if (window.GetType().Name != "SceneHierarchyWindow")
                    continue;

                var method = window.GetType().GetMethod("SetExpandedRecursive",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance, null,
                    new[] { typeof(int), typeof(bool) }, null);

                if (method == null)
                {
                    Debug.LogError(
                        "Could not find method 'UnityEditor.SceneHierarchyWindow.SetExpandedRecursive(int, bool)'.");
                    return;
                }

                var field = scene.GetType().GetField("m_Handle",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (field == null)
                {
                    Debug.LogError("Could not find field 'int UnityEngine.SceneManagement.Scene.m_Handle'.");
                    return;
                }

                var sceneHandle = field.GetValue(scene);
                method.Invoke(window, new[] { sceneHandle, expand });
            }

        }
#endif
    }
}