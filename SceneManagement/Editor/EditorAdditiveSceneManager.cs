using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace UnityTools.Scenes.Editor
{
    /// <summary>
    /// Opens an Additive Scene Manager's Scene List when its scene is opened in the editor, and adds the
    /// GameObject > Scene Management > Additive Scene Manager menu item.
    /// </summary>
    [InitializeOnLoad]
    public class EditorAdditiveSceneManager
    {
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorSceneManager.sceneOpened += OnEditorSceneOpened;
        }

        private static void OnEditorSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (scene != SceneManager.GetActiveScene()) return;
            AdditiveSceneManager sceneManager = Object.FindAnyObjectByType<AdditiveSceneManager>();
            sceneManager?.LoadSceneList();
        }

        [MenuItem("GameObject/Scene Management/Additive Scene Manager", false, 5)]
        static void CreateCustomGameObject(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("Additive Scene Manager");
            go.AddComponent<AdditiveSceneManager>();
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }
    }
}