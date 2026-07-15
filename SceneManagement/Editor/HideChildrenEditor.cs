using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SceneManagement.Editor
{
    [InitializeOnLoad]
    public static class HideChildrenEditor
    {
        static HideChildrenEditor()
        {
            PrefabStage.prefabStageOpened += OnPrefabOpened;
            PrefabStage.prefabStageClosing += OnPrefabClosing;
        }

        private static void OnPrefabOpened(PrefabStage prefabStage)
        {
            GameObject prefabRoot = prefabStage.prefabContentsRoot;
            if (prefabRoot.TryGetComponent(out HideChildren hideChildren) && hideChildren.ShowInPrefabMode)
            {
                Debug.Log($"Showing prefab components: {prefabRoot.name}");
                hideChildren.SetHidden(false);
            }
        }

        private static void OnPrefabClosing(PrefabStage prefabStage)
        {
            GameObject prefabRoot = prefabStage.prefabContentsRoot;
            if (prefabRoot.TryGetComponent(out HideChildren hideChildren) && hideChildren.ShowInPrefabMode)
            {
                Debug.Log($"Hiding prefab components: {prefabRoot.name}");
                hideChildren.SetHidden(true);
            }
        }
    }
}
