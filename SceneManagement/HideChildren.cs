using UnityEngine;

namespace SceneManagement
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class HideChildren : MonoBehaviour
    {
        [field: SerializeField] public bool IsHidden { get; set; } = true;
        [field: SerializeField] public bool HideComponents { get; set; } = false;
        [field: SerializeField] public bool ShowInPrefabMode { get; set; } = true;

        private static HideFlags _hiddenFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
        private static HideFlags _visibleFlags = HideFlags.None;

        private void OnValidate()
        {
            SetChildrenHideFlags(gameObject, IsHidden, HideComponents);
        }

        private void Awake()
        {
            SetChildrenHideFlags(gameObject, IsHidden, HideComponents);
        }

        public void SetHidden(bool hidden)
        {
            IsHidden = hidden;
            SetChildrenHideFlags(gameObject, hidden, HideComponents);
        }

        private void OnDestroy()
        {
            if(!Application.isPlaying)
            {
                SetChildrenHideFlags(gameObject, false, false);
            }
        }

        public static void SetChildrenHideFlags(GameObject gameObject, bool hidden, bool hideComponents)
        {
            HideFlags transformFlags = hidden ? _hiddenFlags : _visibleFlags;
            Transform[] tranforms = gameObject.GetComponentsInChildren<Transform>();
            for (int i = 0; i < tranforms.Length; i++)
            {
                if (tranforms[i].gameObject == gameObject) continue;
                tranforms[i].gameObject.hideFlags = transformFlags;
            }

            HideFlags componentFlags = (hidden && hideComponents) ? _hiddenFlags : _visibleFlags;
            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is Transform) continue;
                if (components[i] is HideChildren) continue;
                components[i].hideFlags = componentFlags;
            }
        }
    }
}