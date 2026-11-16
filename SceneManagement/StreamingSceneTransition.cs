using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SceneManagement
{
    /// <summary>
    /// A trigger that asks the <see cref="StreamingSceneManager"/> to load a set of scenes when an object with the
    /// right tag enters it. Entering again while that set is loaded or loading does nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public class StreamingSceneTransition : MonoBehaviour
    {
        internal const string TriggerLayerName = "Trigger";

        [SerializeField] private string _tagFilter = "Player";

        [Header("Forced Set")]
        [SerializeField, SceneDropdown(storeName: true)] private string[] _overrideOnEnter;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(_tagFilter)) return;
            if (StreamingSceneManager.Instance == null)
            {
                Debug.LogError("Streaming Scene Transition requires Streaming Scene Manager in persistent scene.", this);
                return;
            }

            if (_overrideOnEnter != null && _overrideOnEnter.Length > 0)
            {
                StreamingSceneManager.Instance.LoadSet(_overrideOnEnter);
            }
        }

        /// <summary>The "Trigger" layer if the project has one, otherwise Ignore Raycast.</summary>
        internal static int TriggerLayer()
        {
            int layer = LayerMask.NameToLayer(TriggerLayerName);
            return layer >= 0 ? layer : LayerMask.NameToLayer("Ignore Raycast");
        }

#if UNITY_EDITOR
        [MenuItem("GameObject/Scene Management/Streaming Scene Transition", false, 5)]
        static void CreateCustomGameObject(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("SST");
            go.AddComponent<StreamingSceneTransition>();
            BoxCollider collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(3f, 3f, 3f);
            collider.isTrigger = true;
            go.layer = TriggerLayer();
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }
#endif
    }
}
