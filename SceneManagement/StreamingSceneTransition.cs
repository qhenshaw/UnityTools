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

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(_tagFilter)) return;
            if (StreamingSceneManager.Instance == null)
            {
                Debug.LogError("Streaming Scene Transition requires Streaming Scene Manager in persistent scene.");
                return;
            }

            if (_overrideOnEnter != null && _overrideOnEnter.Length > 0)
            {
                StreamingSceneManager.Instance.LoadSet(_overrideOnEnter);
            }
        }

#if UNITY_EDITOR
        [MenuItem("GameObject/Scene Management/Streaming Scene Transition", false, 5)]
        static void CreateCustomGameObject(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("SCT");
            go.AddComponent<StreamingSceneTransition>();
            BoxCollider collier = go.AddComponent<BoxCollider>();
            collier.size = new Vector3(3f, 3f, 3f);
            collier.isTrigger = true;
            go.layer = LayerMask.NameToLayer("Trigger");
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }
#endif
    }
}