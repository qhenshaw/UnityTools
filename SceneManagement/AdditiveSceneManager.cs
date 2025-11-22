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
    [DisallowMultipleComponent]
    public class AdditiveSceneManager : MonoBehaviour
    {
        [ShowInInspector, InlineButton("AddNewScene", "Add New")] private string _newSceneName;
        [field: SerializeField] public List<string> SceneList { get; private set; }

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
            string path = activeScene.path.Replace(activeScene.name + ".unity", "");
            Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            int index = GetLoadedSceneIndex(newScene);
            if (index == -1)
            {
                Debug.LogError("Scene failed to load correctly");
                return;
            }

            newScene.name = $"{activeScene.name}-{_newSceneName}";
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
    }
}