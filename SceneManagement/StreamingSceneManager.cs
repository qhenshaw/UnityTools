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

        [SerializeField] private bool _unloadUnusedAssets = true;
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

            string[] initialScenes = _persistent.Concat(_initial).ToArray();
            LoadSet(initialScenes);
        }

        public void LoadSet(string[] sceneNames)
        {
            StartCoroutine(LoadSetRoutine(sceneNames));
        }

        private IEnumerator LoadSetRoutine(string[] sceneNames)
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
                yield return StartCoroutine(LoadAsyncRoutine(sceneName));
            }

            foreach (string sceneName in unload)
            {
                yield return StartCoroutine(UnloadAsyncRoutine(sceneName));
            }
        }

        private bool CheckIfSceneLoaded(string sceneName)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            return scene.IsValid();
        }

        private IEnumerator UnloadAsyncRoutine(string sceneName)
        {
            if (!CheckIfSceneLoaded(sceneName)) yield break;
            if (_persistent.Contains(sceneName)) yield break;

            AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(sceneName);
            while(!asyncUnload.isDone)
            {
                yield return null;
            }

            if (_unloadUnusedAssets) Resources.UnloadUnusedAssets();
        }

        private IEnumerator LoadAsyncRoutine(string sceneName, LoadSceneMode loadSceneMode = LoadSceneMode.Additive)
        {
            if (CheckIfSceneLoaded(sceneName))  yield break;

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