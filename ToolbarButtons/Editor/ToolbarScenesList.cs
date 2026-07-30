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

            EditorApplication.projectChanged += RefreshSceneList;
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
            string activeSceneName = GetActiveSceneName();
            if (activeSceneName == "Untitled")
            {
                Debug.LogWarning("Active scene is untitled. Please save the scene first.");
                return;
            }
            else
            {
                string scenePath = _paths.FirstOrDefault(path => Path.GetFileNameWithoutExtension(path) == activeSceneName);
                if (!string.IsNullOrEmpty(scenePath))
                {
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath));
                }
                else
                {
                    Debug.LogWarning($"Scene '{activeSceneName}' not found in project.");
                }
            }
        }

        public static void RestartScene()
        {
            string activeSceneName = GetActiveSceneName();
            if (activeSceneName == "Untitled")
            {
                Debug.LogWarning("Active scene is untitled. Please save the scene first.");
                return;
            }
            else
            {
                string scenePath = _paths.FirstOrDefault(path => Path.GetFileNameWithoutExtension(path) == activeSceneName);
                if (!string.IsNullOrEmpty(scenePath))
                {
                    SwitchScene(scenePath);
                }
                else
                {
                    Debug.LogWarning($"Scene '{activeSceneName}' not found in project.");
                }
            }
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
                menu.AddItem(new GUIContent(scenePath.Replace("Assets\\", "").Replace("\\", "/")), false, () =>
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
            _paths = Directory.GetFiles("Assets", "*.unity", SearchOption.AllDirectories);
            _buildPaths = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
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