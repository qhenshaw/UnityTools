using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArtPipeline.Editor
{
    // Finds painted mesh assets that are no longer referenced by any scene, prefab or asset and lets the user delete them.
    public class VertexPaintCleanupWindow : EditorWindow
    {
        private const int DependencyBatchSize = 200;

        private class Entry
        {
            public string Path;
            public Mesh Mesh;
            public long Size;
            public bool Selected = true;
        }

        private List<Entry> _unused = new List<Entry>();
        private int _totalCount;
        private bool _hasDirtyScenes;
        private Vector2 _scroll;

        [MenuItem("Tools/Vertex Painter/Clean Up Unused Paint Meshes")]
        public static void Open()
        {
            var window = GetWindow<VertexPaintCleanupWindow>("Vertex Paint Cleanup");
            window.minSize = new Vector2(450f, 250f);
            window.Scan();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Scan", EditorStyles.toolbarButton, GUILayout.Width(60f))) Scan();
                GUILayout.FlexibleSpace();
                long totalSize = _unused.Sum(e => e.Size);
                GUILayout.Label($"{_totalCount} painted meshes, {_unused.Count} unused ({EditorUtility.FormatBytes(totalSize)})");
            }

            if (_hasDirtyScenes)
            {
                EditorGUILayout.HelpBox("Open scenes have unsaved changes, their references are included.", MessageType.Info);
            }

            if (_unused.Count == 0)
            {
                EditorGUILayout.HelpBox("No unused painted meshes found.", MessageType.None);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (Entry entry in _unused)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(16f));
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ObjectField(entry.Mesh, typeof(Mesh), false, GUILayout.Width(180f));
                    }
                    if (GUILayout.Button(new GUIContent(entry.Path, "Ping asset"), EditorStyles.label))
                    {
                        EditorGUIUtility.PingObject(entry.Mesh);
                    }
                    GUILayout.Label(EditorUtility.FormatBytes(entry.Size), GUILayout.Width(70f));
                }
            }
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select All")) _unused.ForEach(e => e.Selected = true);
                if (GUILayout.Button("Select None")) _unused.ForEach(e => e.Selected = false);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!_unused.Any(e => e.Selected)))
                {
                    if (GUILayout.Button("Delete Selected", GUILayout.Width(120f))) DeleteSelected();
                }
            }
        }

        private void Scan()
        {
            _unused.Clear();
            List<string> paintedPaths = VertexPaintStorage.FindAllPaintedMeshPaths();
            _totalCount = paintedPaths.Count;
            if (paintedPaths.Count == 0) return;

            HashSet<string> painted = new HashSet<string>(paintedPaths);
            HashSet<string> used = new HashSet<string>();

            // saved references from scenes, prefabs and other assets
            string[] referencers = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/") && !painted.Contains(p)
                    && (p.EndsWith(".unity") || p.EndsWith(".prefab") || p.EndsWith(".asset")))
                .ToArray();

            try
            {
                for (int i = 0; i < referencers.Length; i += DependencyBatchSize)
                {
                    float progress = (float)i / referencers.Length;
                    if (EditorUtility.DisplayCancelableProgressBar("Vertex Paint Cleanup", "Scanning references...", progress))
                    {
                        _totalCount = 0;
                        return;
                    }

                    string[] batch = referencers.Skip(i).Take(DependencyBatchSize).ToArray();
                    foreach (string dependency in AssetDatabase.GetDependencies(batch, false))
                    {
                        if (painted.Contains(dependency)) used.Add(dependency);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // unsaved references from open scenes and the prefab stage
            _hasDirtyScenes = false;
            foreach (Scene scene in VertexPaintStorage.GetEditableScenes())
            {
                if (scene.isDirty) _hasDirtyScenes = true;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        AddUsed(filter.sharedMesh, painted, used);
                    }
                    foreach (VertexPainter painter in root.GetComponentsInChildren<VertexPainter>(true))
                    {
                        AddUsed(painter.PaintedMesh, painted, used);
                    }
                }
            }

            foreach (string path in paintedPaths.Where(p => !used.Contains(p)).OrderBy(p => p))
            {
                _unused.Add(new Entry
                {
                    Path = path,
                    Mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path),
                    Size = File.Exists(path) ? new FileInfo(path).Length : 0,
                });
            }
            Repaint();
        }

        private static void AddUsed(Mesh mesh, HashSet<string> painted, HashSet<string> used)
        {
            if (mesh == null) return;
            string path = AssetDatabase.GetAssetPath(mesh);
            if (painted.Contains(path)) used.Add(path);
        }

        private void DeleteSelected()
        {
            string[] paths = _unused.Where(e => e.Selected).Select(e => e.Path).ToArray();
            if (!EditorUtility.DisplayDialog("Delete Unused Paint Meshes",
                $"Move {paths.Length} painted mesh asset(s) to the trash?", "Delete", "Cancel"))
            {
                return;
            }

            List<string> failed = new List<string>();
            AssetDatabase.MoveAssetsToTrash(paths, failed);
            foreach (string path in failed) Debug.LogWarning($"Failed to delete painted mesh: {path}");

            // remove paint folders left empty
            foreach (string folder in paths.Select(p => Path.GetDirectoryName(p).Replace('\\', '/')).Distinct())
            {
                if (!VertexPaintStorage.IsPaintFolder(folder) || !AssetDatabase.IsValidFolder(folder)) continue;
                if (AssetDatabase.FindAssets("", new[] { folder }).Length == 0 && AssetDatabase.GetSubFolders(folder).Length == 0)
                {
                    AssetDatabase.DeleteAsset(folder);
                }
            }

            Scan();
        }
    }
}
