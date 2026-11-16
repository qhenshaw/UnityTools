using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityTools.ToolbarButtons
{
    public class ToolbarScenesList
    {
#pragma warning disable UDR0001 // Domain Reload Analyzer
        const string _id = "Scene Selector";
        static string[] _paths = new string[0];
        static string[] _buildPaths = new string[0];

        static ToolbarScenesList()
        {
            RefreshSceneList();

            SceneManager.activeSceneChanged += SceneSwitched;
            EditorSceneManager.activeSceneChangedInEditMode += SceneSwitched;
        }
#pragma warning restore UDR0001 // Domain Reload Analyzer

        [MainToolbarElement(_id, defaultDockPosition = MainToolbarDockPosition.Middle)]
        public static IEnumerable<MainToolbarElement> Combined()
        {
            var selector = SceneSelectorDropdown();
            selector.populateContextMenu = (menu) =>
            {
                menu.AppendAction("Find Scene", (a) => FindScene());
                menu.AppendAction("Restart Scene", (a) => RestartScene());
            };
            yield return selector;
            yield return PlayFromDropdown();
        }

        public static void FindScene()
        {
            string scenePath = GetActiveScenePath();
            if (scenePath == null) return;
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath));
        }

        public static void RestartScene()
        {
            string scenePath = GetActiveScenePath();
            if (scenePath == null) return;
            SwitchScene(scenePath);
        }

        /// <summary>
        /// The active scene's asset path, or null (with a warning) when it's unsaved. Uses the path, not the name,
        /// so a scene with the same name in another folder is never picked by mistake.
        /// </summary>
        internal static string GetActiveScenePath()
        {
            string path = SceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("Active scene is untitled. Please save the scene first.");
                return null;
            }
            return path;
        }

        private static MainToolbarElement SceneSelectorDropdown()
        {
            string activeSceneName = GetActiveSceneName();
            var icon = EditorGUIUtility.IconContent("UnityLogo").image as Texture2D;
            var content = new MainToolbarContent(activeSceneName, icon, "Select active scene");
            return new MainToolbarDropdown(content, ShowDropdownMenu);
        }

        private static string GetActiveSceneName()
        {
            string activeSceneName;
            if (Application.isPlaying)
                activeSceneName = SceneManager.GetActiveScene().name;
            else
                activeSceneName = SceneManager.GetActiveScene().name;
            if (activeSceneName.Length == 0)
                activeSceneName = "Untitled";
            return activeSceneName;
        }

        private static void ShowDropdownMenu(Rect dropDownRect)
        {
            RefreshSceneList();

            var menu = new GenericMenu();
            if (_paths.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("No Scenes in Project"));
            }
            foreach (string scenePath in _paths)
            {
                string sceneName = Path.GetFileNameWithoutExtension(scenePath);
                menu.AddItem(new GUIContent(scenePath.StartsWith("Assets/") ? scenePath.Substring("Assets/".Length) : scenePath), false, () =>
                {
                    SwitchScene(scenePath);
                });
            }
            foreach (string scenePath in _buildPaths)
            {
                string sceneName = Path.GetFileNameWithoutExtension(scenePath);
                menu.AddItem(new GUIContent($"Build Settings/{scenePath.Replace("Assets/", "").Replace('/', '-').Replace('\\', '-').Replace(".unity", "")}"), false, () =>
                {
                    SwitchScene(scenePath);
                });
            }
            menu.DropDown(dropDownRect);
        }

        private static void SwitchScene(string scenePath)
        {
            if (Application.isPlaying)
            {
                string sceneName = Path.GetFileNameWithoutExtension(scenePath);
                if (Application.CanStreamedLevelBeLoaded(sceneName))
                {
                    Debug.Log($"Playing scene: {sceneName}");
                    SceneManager.LoadScene(sceneName);
                }
                else
                {
                    Debug.LogError($"Scene '{sceneName}' is not in the Build Settings.");
                }
            }
            else
            {
                if (File.Exists(scenePath))
                {
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        Debug.Log($"Switching to scene: {scenePath}");
                        EditorSceneManager.OpenScene(scenePath);
                    }
                }
                else
                {
                    Debug.LogError($"Scene at path '{scenePath}' does not exist.");
                }
            }
        }

        private static void RefreshSceneList()
        {
            _paths = FindScenePaths();
            _buildPaths = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        }

        /// <summary>Scene paths in Assets, from the AssetDatabase rather than a folder scan.</summary>
        internal static string[] FindScenePaths()
        {
            return AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".unity"))
                .OrderBy(path => path)
                .ToArray();
        }

        private static void SceneSwitched(Scene oldScene, Scene newScene)
        {
            MainToolbar.Refresh(_id);
        }

        private static MainToolbarElement PlayFromDropdown()
        {
            string sceneName = EditorSceneManager.playModeStartScene != null ? EditorSceneManager.playModeStartScene.name : "Current";
            var content = new MainToolbarContent(sceneName, null, "Select play from scene");
            return new MainToolbarDropdown(content, ShowPlayFromDropdown);
        }

        private static void ShowPlayFromDropdown(Rect dropDownRect)
        {
            RefreshSceneList();

            var menu = new GenericMenu();
            if (_buildPaths.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("No Scenes in Build List"));
            }

            menu.AddItem(new GUIContent("Current"), false, () =>
            {
                EditorSceneManager.playModeStartScene = null;
                Debug.Log($"Starting play mode from scene: {GetActiveSceneName()}");
                MainToolbar.Refresh(_id);
            });

            foreach (string scenePath in _buildPaths)
            {
                string sceneName = Path.GetFileNameWithoutExtension(scenePath);
                menu.AddItem(new GUIContent(scenePath.Replace("Assets/", "").Replace('/', '-').Replace('\\', '-').Replace(".unity", "")), false, () =>
                {
                    SetPlayFromScene(scenePath);
                    MainToolbar.Refresh(_id);
                });
            }
            menu.DropDown(dropDownRect);
        }

        private static void SetPlayFromScene(string path)
        {
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            if (sceneAsset != null)
            {
                EditorSceneManager.playModeStartScene = sceneAsset;
                Debug.Log($"Starting play mode from scene: {sceneAsset.name}");
            }
            else
            {
                Debug.LogError($"Scene at path '{path}' does not exist.");
            }
        }
    }
}