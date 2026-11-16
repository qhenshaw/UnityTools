using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace UnityTools.Scenes.Editor
{
    /// <summary>Checks whether scenes are in Build Settings (and so can be loaded at runtime), and adds them.</summary>
    internal static class SceneBuildCheck
    {
        private static EditorBuildSettingsScene[] s_BuildScenes;

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorBuildSettings.sceneListChanged += () => s_BuildScenes = null;
        }

        private static EditorBuildSettingsScene[] BuildScenes => s_BuildScenes ??= EditorBuildSettings.scenes;

        /// <summary>
        /// True when the stored value can be loaded at runtime: for a path, that scene is in Build Settings and
        /// enabled; for a name, any enabled scene with that name is.
        /// </summary>
        internal static bool IsInBuild(string stored, bool storeName) => IsInBuild(stored, storeName, BuildScenes);

        internal static bool IsInBuild(string stored, bool storeName, IEnumerable<EditorBuildSettingsScene> buildScenes)
        {
            if (string.IsNullOrEmpty(stored)) return true;
            return buildScenes.Any(scene => scene.enabled &&
                (storeName ? Path.GetFileNameWithoutExtension(scene.path) == stored : scene.path == stored));
        }

        /// <summary>Adds the scene to Build Settings, or enables it if it's there but disabled.</summary>
        internal static void AddToBuild(string scenePath)
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            int index = scenes.FindIndex(scene => scene.path == scenePath);
            if (index >= 0) scenes[index] = new EditorBuildSettingsScene(scenePath, true);
            else scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            s_BuildScenes = null;
        }
    }
}
