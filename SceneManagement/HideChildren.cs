using System;
using UnityEngine;

namespace SceneManagement
{
    /// <summary>
    /// Hides this object's children (active and inactive) from the Hierarchy and Inspector, and optionally its own
    /// components. Only its own hide flags are added or removed; any other flags a child has are kept.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class HideChildren : MonoBehaviour
    {
        [field: SerializeField] public bool IsHidden { get; set; } = true;
        [field: SerializeField] public bool HideComponents { get; set; } = false;
        [field: SerializeField] public bool ShowInPrefabMode { get; set; } = true;

        internal const HideFlags HiddenFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;

        // Set while the prefab is open in prefab mode with Show In Prefab Mode on. Not saved, so saving the
        // prefab can't change Is Hidden.
        [NonSerialized] private bool _shownTemporarily;

        /// <summary>True when the children are currently hidden: Is Hidden, unless prefab mode is showing them.</summary>
        public bool IsEffectivelyHidden => IsHidden && !_shownTemporarily && !ShownByPrefabMode();

        // Also covers a domain reload while the prefab is open, which clears _shownTemporarily.
        private bool ShownByPrefabMode()
        {
#if UNITY_EDITOR
            return ShowInPrefabMode && UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null;
#else
            return false;
#endif
        }

        private void OnValidate() => Apply();

        private void Awake() => Apply();

        public void SetHidden(bool hidden)
        {
            IsHidden = hidden;
            Apply();
        }

        /// <summary>Shows the children without changing Is Hidden (used by prefab mode), or ends that.</summary>
        public void ShowTemporarily(bool show)
        {
            _shownTemporarily = show;
            Apply();
        }

        private void Apply() => SetChildrenHideFlags(gameObject, IsEffectivelyHidden, HideComponents);

        private void OnDestroy()
        {
            if (!Application.isPlaying)
            {
                SetChildrenHideFlags(gameObject, false, false);
            }
        }

        public static void SetChildrenHideFlags(GameObject gameObject, bool hidden, bool hideComponents)
        {
            Transform[] transforms = gameObject.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                GameObject child = transforms[i].gameObject;
                if (child == gameObject) continue;
                child.hideFlags = WithHidden(child.hideFlags, hidden);
            }

            bool hideOwn = hidden && hideComponents;
            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null) continue; // missing script
                if (components[i] is Transform) continue;
                if (components[i] is HideChildren) continue;
                components[i].hideFlags = WithHidden(components[i].hideFlags, hideOwn);
            }
        }

        internal static HideFlags WithHidden(HideFlags flags, bool hidden) =>
            hidden ? flags | HiddenFlags : flags & ~HiddenFlags;
    }
}
