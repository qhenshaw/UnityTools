using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using InspectorAttributes;
using System;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace SceneManagement
{
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public class AdditiveSceneManager : MonoBehaviour
    {
#pragma warning disable CS0414
        [SerializeField, InlineButton(nameof(AddNewScene), "Add")] private string _newSceneName;
        [SerializeField, SceneDropdown, InlineButton(nameof(AddExistingScene), "Add")] private string _existingScene;
        [field: SerializeField, SceneDropdown] public List<string> SceneList { get; private set; }
        [SerializeField, Button("Load Scene List", true, Method = nameof(LoadSceneList))] private string _loadSceneListButton;
#pragma warning restore CS0414

        private void Awake()
        {
            LoadSceneList();
        }

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
            if(String.IsNullOrEmpty(_newSceneName))
            {
                Debug.LogError("New scene requires name.");
                return;
            }
            Scene activeScene = SceneManager.GetActiveScene();
            string folder = Path.GetDirectoryName(activeScene.path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder)) folder = "Assets";
            Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            int index = GetLoadedSceneIndex(newScene);
            if (index == -1)
            {
                Debug.LogError("Scene failed to load correctly");
                return;
            }

            newScene.name = $"{activeScene.name}-{_newSceneName}";
            EditorSceneManager.SaveScene(newScene, $"{folder}/{newScene.name}.unity");
            EditorSceneManager.SetActiveScene(activeScene);
            SceneList.Add(newScene.path);

            Debug.Log($"New scene added: {newScene.name}");
#endif
        }

        /// <summary>Adds the scene picked in the Existing Scene dropdown to the list and opens it additively.</summary>
        private void AddExistingScene()
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(_existingScene))
            {
                Debug.LogError("Pick a scene in Existing Scene first.", this);
                return;
            }
            if (_existingScene == gameObject.scene.path)
            {
                Debug.LogError("A scene can't load itself additively; pick a different scene.", this);
                return;
            }
            if (SceneList.Contains(_existingScene))
            {
                Debug.LogWarning($"{Path.GetFileNameWithoutExtension(_existingScene)} is already in the scene list.", this);
                return;
            }

            SceneList.Add(_existingScene);
            string sceneName = Path.GetFileNameWithoutExtension(_existingScene);
            if (!CheckIfSceneLoaded(sceneName))
            {
                Scene activeScene = SceneManager.GetActiveScene();
                EditorSceneManager.OpenScene(_existingScene, OpenSceneMode.Additive);
                EditorSceneManager.SetActiveScene(activeScene);
            }

            Debug.Log($"Existing scene added: {sceneName}", this);
            _existingScene = string.Empty;
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
    }
}