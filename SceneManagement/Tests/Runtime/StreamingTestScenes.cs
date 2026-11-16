using UnityEngine.TestTools;

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#endif

namespace SceneManagement.PlayModeTests
{
    /// <summary>
    /// Creates small test scenes in Assets/UnityToolsStreamingTests before the PlayMode run and adds them to
    /// Build Settings (except NotInBuild); removes both afterwards. Each scene has an active root named
    /// "&lt;scene&gt;_Root" and an inactive one named "&lt;scene&gt;_Inactive".
    /// </summary>
    public class StreamingTestScenes : IPrebuildSetup, IPostBuildCleanup
    {
        public const string A = "UTStreamA";
        public const string B = "UTStreamB";
        public const string C = "UTStreamC";
        public const string Persist = "UTStreamPersist";
        public const string NotInBuild = "UTStreamNotInBuild";

        public static readonly string[] All = { A, B, C, Persist, NotInBuild };

#if UNITY_EDITOR
        private const string Folder = "Assets/UnityToolsStreamingTests";
        private const string BackupKey = "UnityTools.StreamingTestScenes.BuildBackup";

        public void Setup()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "UnityToolsStreamingTests");

            foreach (string name in All)
            {
                string path = $"{Folder}/{name}.unity";
                if (System.IO.File.Exists(path)) continue;

                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var root = new GameObject(name + "_Root");
                var inactive = new GameObject(name + "_Inactive");
                inactive.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, scene);
                SceneManager.MoveGameObjectToScene(inactive, scene);
                EditorSceneManager.SaveScene(scene, path);
                EditorSceneManager.CloseScene(scene, true);
            }

            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            if (!SessionState.GetBool(BackupKey + ".Saved", false))
            {
                SessionState.SetString(BackupKey, string.Join("\n", scenes.Select(s => (s.enabled ? "1" : "0") + s.path)));
                SessionState.SetBool(BackupKey + ".Saved", true);
            }
            foreach (string name in All)
            {
                if (name == NotInBuild) continue;
                string path = $"{Folder}/{name}.unity";
                if (scenes.All(s => s.path != path)) scenes.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        public void Cleanup()
        {
            if (SessionState.GetBool(BackupKey + ".Saved", false))
            {
                string backup = SessionState.GetString(BackupKey, "");
                EditorBuildSettings.scenes = backup.Split('\n')
                    .Where(line => line.Length > 1)
                    .Select(line => new EditorBuildSettingsScene(line.Substring(1), line[0] == '1'))
                    .ToArray();
                SessionState.EraseBool(BackupKey + ".Saved");
                SessionState.EraseString(BackupKey);
            }
            else
            {
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Where(s => !s.path.StartsWith(Folder + "/")).ToArray();
            }
            AssetDatabase.DeleteAsset(Folder);
        }
#else
        public void Setup() { }
        public void Cleanup() { }
#endif
    }
}
