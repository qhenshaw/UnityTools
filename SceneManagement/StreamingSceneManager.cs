using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace SceneManagement
{
    [DefaultExecutionOrder(-9000)]
    [DisallowMultipleComponent]
    public class StreamingSceneManager : MonoBehaviour
    {
        public static StreamingSceneManager Instance { get; private set; }

        [SerializeField] private bool _logFailures = false;
        [SerializeField] private string[] _persistent;
        [SerializeField] private string[] _initial;

        private void Awake()
        {
            if (Instance != null)
            {
                Debug.LogWarning("Multiple StreamingSceneManager singletons loaded.", gameObject);
                Destroy(gameObject);
                return;
            }

            Instance = this;

            LoadPersistent();
            LoadInitial();
        }

        private void LoadPersistent()
        {
            LoadSet(_persistent);
        }

        private void LoadInitial()
        {
            LoadSet(_initial);
        }

        private bool CheckIfSceneLoaded(string sceneName)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            return scene.IsValid();
        }

        public void Unload(string sceneName)
        {
            if (!CheckIfSceneLoaded(sceneName))
            {
                if (_logFailures) Debug.LogWarning($"Scene not loaded, cannot unload: {sceneName}");
                return;
            }

            if (_persistent.Contains(sceneName))
            {
                if (_logFailures) Debug.LogWarning($"Attempting to unload persistent scene: {sceneName}");
                return;
            }

            SceneManager.UnloadSceneAsync(sceneName);
        }

        public void LoadSet(string[] sceneNames)
        {
            List<string> unload = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                unload.Add(scene.name);
                foreach (string sceneName in sceneNames)
                {
                    if (scene.name.Equals(sceneName))
                    {
                        unload.Remove(sceneName);
                        break;
                    }
                }
            }

            foreach (string sceneName in sceneNames)
            {
                Load(sceneName);
            }

            foreach (string sceneName in unload)
            {
                Unload(sceneName);
            }
        }

        public void Load(string sceneName, LoadSceneMode loadSceneMode = LoadSceneMode.Additive)
        {
            if (CheckIfSceneLoaded(sceneName))
            {
                if (_logFailures) Debug.LogWarning($"Scene already loaded: {sceneName}");
                return;
            }

            StartCoroutine(LoadAsyncRoutine(sceneName, loadSceneMode));
        }

        private IEnumerator LoadAsyncRoutine(string sceneName, LoadSceneMode loadSceneMode = LoadSceneMode.Additive)
        {
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, loadSceneMode);
            while (!asyncLoad.isDone)
            {
                yield return null;
            }
        }

#if UNITY_EDITOR
        [MenuItem("GameObject/Scene Management/Streaming Scene Manager", false, 5)]
        static void CreateCustomGameObject(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("Streaming Scene Manager");
            go.AddComponent<StreamingSceneManager>();
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }

        [MenuItem("GameObject/Scene Management/Add Empty Scene", false, 5)]
        static void AddEmptyScene(MenuCommand menuCommand)
        {
            Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            newScene.name = "New Empty Scene";
        }
#endif
    }
}