using UnityEditor;
using UnityEditor.SceneManagement;

namespace UnityTools.Scenes.Editor
{
    /// <summary>
    /// Shows the children of HideChildren components (with Show In Prefab Mode on) while their prefab is open in
    /// prefab mode, without changing their saved Is Hidden value.
    /// </summary>
    [InitializeOnLoad]
    public static class HideChildrenEditor
    {
        static HideChildrenEditor()
        {
            PrefabStage.prefabStageOpened += stage => SetShown(stage, true);
            PrefabStage.prefabStageClosing += stage => SetShown(stage, false);
        }

        internal static void SetShown(PrefabStage stage, bool shown)
        {
            if (stage == null || stage.prefabContentsRoot == null) return;
            foreach (HideChildren hideChildren in stage.prefabContentsRoot.GetComponentsInChildren<HideChildren>(true))
            {
                if (hideChildren.ShowInPrefabMode) hideChildren.ShowTemporarily(shown);
            }
        }
    }
}
