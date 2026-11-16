using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityTools.PlaytestFeedback.Editor
{
    /// <summary>Stores the Playtest Feedback settings in ProjectSettings/ and hands them to the runtime in the Editor.</summary>
    [FilePath(FilePathInProject, FilePathAttribute.Location.ProjectFolder)]
    internal class PlaytestFeedbackProjectSettings : ScriptableSingleton<PlaytestFeedbackProjectSettings>
    {
        internal const string FilePathInProject = "ProjectSettings/UnityToolsPlaytestFeedback.asset";

        [SerializeField] private FeedbackSettings _settings = new FeedbackSettings();

        public FeedbackSettings Settings => _settings ??= new FeedbackSettings();

        [InitializeOnLoadMethod]
        private static void HookRuntime()
        {
            FeedbackSettings.EditorSource = () => instance.Settings;
        }

        private void OnEnable()
        {
            // ScriptableSingleton instances are not editable by default; the settings page needs to edit it.
            hideFlags &= ~HideFlags.NotEditable;
        }

        internal void SaveSettings() => Save(true);

        internal void Replace(FeedbackSettings settings)
        {
            _settings = settings;
            SaveSettings();
        }

        // ------------------------------------------------------------------ Migration from the standalone package

#pragma warning disable CS0618
        // The standalone package kept its settings in Assets/Resources/PlaytestFeedbackSettings.asset. If one is
        // still there, copy it into Project Settings once and delete it. Only the main Editor process does this.
        [InitializeOnLoadMethod]
        private static void ScheduleMigration()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            EditorApplication.delayCall += MigrateLegacyAsset;
        }

        internal static void MigrateLegacyAsset()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(FeedbackSettingsAsset));
            if (guids.Length == 0) return;

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var legacy = AssetDatabase.LoadAssetAtPath<FeedbackSettingsAsset>(path);
            if (legacy == null) return;

            if (File.Exists(FilePathInProject))
            {
                Debug.LogWarning($"[PlaytestFeedback] {path} is from the standalone Playtest Feedback package and isn't used anymore. " +
                                 "It wasn't copied because Project Settings > Unity Tools > Playtest Feedback is already set up. Delete it when you're done with it.", legacy);
                return;
            }

            instance.Replace(ConvertLegacy(legacy, out bool droppedAssets));
            if (droppedAssets)
            {
                Debug.LogWarning("[PlaytestFeedback] Panel Settings Override and Extra Style Sheet aren't supported anymore and were not copied. " +
                                 "Edit the package's FeedbackPanel.uss to restyle the panel.");
            }

            if (AssetDatabase.DeleteAsset(path))
            {
                Debug.Log($"[PlaytestFeedback] Settings moved from {path} to Project Settings > Unity Tools > Playtest Feedback ({FilePathInProject}), and the old asset was deleted.");
            }
        }

        /// <summary>Copies every value of the old settings asset into the new settings, by field name.</summary>
        internal static FeedbackSettings ConvertLegacy(FeedbackSettingsAsset legacy, out bool droppedAssets)
        {
            droppedAssets = legacy.panelSettingsOverride != null || legacy.extraStyleSheet != null;
            FeedbackSettings settings = FeedbackSettings.FromJson(JsonUtility.ToJson(legacy));
            settings.toggleAction ??= new UnityEngine.InputSystem.InputAction("Toggle Feedback Panel", UnityEngine.InputSystem.InputActionType.Button);
            return settings;
        }
#pragma warning restore CS0618
    }
}
