using UnityEditor;
using UnityEngine;

namespace UnityTools.DebugTools.Editor
{
    /// <summary>Stores the debug menu settings in ProjectSettings/ (not Assets/) and hands them to the runtime in the Editor.</summary>
    [FilePath(FilePathInProject, FilePathAttribute.Location.ProjectFolder)]
    internal class DebugMenuProjectSettings : ScriptableSingleton<DebugMenuProjectSettings>
    {
        internal const string FilePathInProject = "ProjectSettings/UnityToolsDebugMenu.asset";

        [SerializeField] private DebugMenuSettings _settings = DebugMenuSettings.Defaults();

        public DebugMenuSettings Settings => _settings ??= DebugMenuSettings.Defaults();

        [InitializeOnLoadMethod]
        private static void HookRuntime()
        {
            DebugMenuSettings.EditorSource = () => instance.Settings;
        }

        private void OnEnable()
        {
            // ScriptableSingleton instances are not editable by default; the settings page needs to edit it.
            hideFlags &= ~HideFlags.NotEditable;
        }

        internal void SaveSettings() => Save(true);
    }

    internal static class UnityToolsSettingsPages
    {
        internal const string RootPath = "Project/Unity Tools";

        [SettingsProvider]
        public static SettingsProvider CreateRoot()
        {
            return new SettingsProvider(RootPath, SettingsScope.Project)
            {
                label = "Unity Tools",
                guiHandler = _ =>
                {
                    EditorGUILayout.LabelField("Settings for the Unity Tools package. Pick a page on the left.", EditorStyles.wordWrappedLabel);
                },
                keywords = new[] { "Unity Tools", "UnityTools" }
            };
        }

        [SettingsProvider]
        public static SettingsProvider CreateDebugMenu()
        {
            return new SettingsProvider(RootPath + "/Debug Menu", SettingsScope.Project)
            {
                label = "Debug Menu",
                guiHandler = _ =>
                {
                    DebugMenuProjectSettings settings = DebugMenuProjectSettings.instance;
                    var so = new SerializedObject(settings);
                    SerializedProperty root = so.FindProperty("_settings");

                    EditorGUILayout.HelpBox("The debug menu is created automatically in the Editor and in Development builds " +
                                            "(and in release builds with the DEBUG_MENU scripting define). Features add their own " +
                                            "sections with DebugMenu.Register.", MessageType.Info);

                    EditorGUILayout.PropertyField(root.FindPropertyRelative(nameof(DebugMenuSettings.ToggleAction)), new GUIContent("Toggle Action"), true);
                    EditorGUILayout.PropertyField(root.FindPropertyRelative(nameof(DebugMenuSettings.StartOpen)));
                    EditorGUILayout.PropertyField(root.FindPropertyRelative(nameof(DebugMenuSettings.PauseWhileOpen)));
                    EditorGUILayout.PropertyField(root.FindPropertyRelative(nameof(DebugMenuSettings.BlockGameplayInput)));
                    EditorGUILayout.PropertyField(root.FindPropertyRelative(nameof(DebugMenuSettings.ShowGameSection)));
                    EditorGUILayout.PropertyField(root.FindPropertyRelative(nameof(DebugMenuSettings.RefreshInterval)));

                    if (so.ApplyModifiedProperties()) settings.SaveSettings();

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Toggle Action with no bindings uses F5 on keyboard and Select + Start on gamepad. " +
                                               "Changes take effect the next time the menu is created (next Play).", EditorStyles.wordWrappedMiniLabel);
                },
                keywords = new[] { "Debug", "Menu", "Cheats", "Toggle", "Pause", "Input", "Time Scale" }
            };
        }
    }
}
