using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SceneManagement
{
    [DisallowMultipleComponent]
    public class StreamingSceneTransition : MonoBehaviour
    {
        [SerializeField] private string _tagFilter = "Player";

        [Header("Forced Set")]
        [SerializeField] private string[] _overrideOnEnter;

        [Header("On Enter")]
        [SerializeField] private string[] _unloadOnEnter;
        [SerializeField] private string[] _loadOnEnter;

        [Header("On Exit")]
        [SerializeField] private string[] _unloadOnExit;
        [SerializeField] private string[] _loadOnExit;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(_tagFilter)) return;
            if (StreamingSceneManager.Instance == null)
            {
                Debug.LogError("Streaming Scene Transition requires Streaming Scene Manager in persistent scene.");
                return;
            }

            foreach (string sceneName in _unloadOnEnter)
            {
                StreamingSceneManager.Instance.Unload(sceneName);
            }

            foreach (string sceneName in _loadOnEnter)
            {
                StreamingSceneManager.Instance.Load(sceneName);
            }

            if (_overrideOnEnter != null && _overrideOnEnter.Length > 0)
            {
                StreamingSceneManager.Instance.LoadSet(_overrideOnEnter);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(_tagFilter)) return;
            if (StreamingSceneManager.Instance == null)
            {
                Debug.LogError("Streaming Scene Transition requires Streaming Scene Manager in persistent scene.");
                return;
            }

            foreach (string sceneName in _unloadOnExit)
            {
                StreamingSceneManager.Instance.Unload(sceneName);
            }

            foreach (string sceneName in _loadOnExit)
            {
                StreamingSceneManager.Instance.Load(sceneName);
            }
        }

#if UNITY_EDITOR
        [MenuItem("GameObject/Scene Management/Streaming Scene Transition", false, 5)]
        static void CreateCustomGameObject(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("SCT");
            go.AddComponent<StreamingSceneTransition>();
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }
#endif
    }
}