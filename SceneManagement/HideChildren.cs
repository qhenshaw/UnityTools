using UnityEngine;

namespace SceneManagement
{
    [DisallowMultipleComponent]
    public class HideChildren : MonoBehaviour
    {
        [field: SerializeField] public bool IsHidden { get; set; } = true;
        [field: SerializeField] public bool ShowInPrefabMode { get; set; } = true;

        private static HideFlags _hiddenFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
        private static HideFlags _visibleFlags = HideFlags.None;

        private void OnValidate()
        {
            SetChildrenHideFlags(gameObject, IsHidden);
        }

        private void Awake()
        {
            SetChildrenHideFlags(gameObject, IsHidden);
        }

        public void SetHidden(bool hidden)
        {
            IsHidden = hidden;
            SetChildrenHideFlags(gameObject, hidden);
        }

        public static void SetChildrenHideFlags(GameObject gameObject, bool hidden)
        {
            HideFlags flags = hidden ? _hiddenFlags : _visibleFlags;
            Transform[] tranforms = gameObject.GetComponentsInChildren<Transform>();
            for (int i = 0; i < tranforms.Length; i++)
            {
                if (tranforms[i].gameObject == gameObject) continue;
                tranforms[i].gameObject.hideFlags = flags;
            }

            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is Transform) continue;
                if (components[i] is HideChildren) continue;
                components[i].hideFlags = flags;
            }
        }
    }
}