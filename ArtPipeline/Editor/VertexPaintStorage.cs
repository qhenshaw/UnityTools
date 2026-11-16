using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArtPipeline.Editor
{
    /// <summary>
    /// Manages the painted mesh assets used by VertexPainter.
    /// Painted meshes are saved as .asset files so they can be referenced from prefabs and scenes.
    /// Painting is copy-on-write: a painter only writes to a mesh asset that it owns exclusively.
    /// </summary>
    public static class VertexPaintStorage
    {
        /// <summary>Asset label put on every painted mesh asset, used to find them for cleanup.</summary>
        public const string AssetLabel = "VertexPaint";
        private const string FallbackFolder = "Assets/VertexPaint";

        /// <summary>Returns the mesh currently displayed by the painter (no assets are created).</summary>
        public static Mesh GetDisplayMesh(VertexPainter painter)
        {
            if (painter.MeshFilter == null) return null;
            return painter.MeshFilter.sharedMesh;
        }

        /// <summary>Returns a mesh asset that this painter can safely write to, creating a new copy if needed.</summary>
        public static Mesh GetWritableMesh(VertexPainter painter)
        {
            if (painter.MeshFilter == null) return null;

            if (painter.SourceMesh == null)
            {
                Undo.RecordObject(painter, "Set Vertex Paint Source");
                painter.SourceMesh = painter.MeshFilter.sharedMesh;
            }

            string copyReason = GetCopyReason(painter);
            if (copyReason != null) return CreateCopy(painter, copyReason);

            // keep the mesh filter in sync in case it was changed elsewhere
            if (painter.MeshFilter.sharedMesh != painter.PaintedMesh)
            {
                Undo.RecordObject(painter.MeshFilter, "Assign Painted Mesh");
                painter.MeshFilter.sharedMesh = painter.PaintedMesh;
            }
            return painter.PaintedMesh;
        }

        /// <summary>
        /// Forces the painter to use a new mesh asset copied from its current painted mesh (or the source mesh
        /// if it has none). Also used by Save to Asset for meshes stored in the scene.
        /// </summary>
        public static Mesh MakeUnique(VertexPainter painter)
        {
            if (painter.MeshFilter == null) return null;
            if (painter.SourceMesh == null)
            {
                Undo.RecordObject(painter, "Set Vertex Paint Source");
                painter.SourceMesh = painter.MeshFilter.sharedMesh;
            }
            return CreateCopy(painter, "Make Unique");
        }

        /// <summary>True when the painted mesh is a project asset rather than mesh data stored in the scene.</summary>
        public static bool IsSavedAsAsset(VertexPainter painter)
        {
            return painter.PaintedMesh != null && AssetDatabase.Contains(painter.PaintedMesh);
        }

        /// <summary>True when the source and painted meshes have different vertex counts, so rebuilding resets the colors.</summary>
        public static bool SourceTopologyChanged(VertexPainter painter)
        {
            if (painter.SourceMesh == null || painter.PaintedMesh == null) return false;
            return painter.SourceMesh.vertexCount != painter.PaintedMesh.vertexCount;
        }

        /// <summary>A short status line for the inspector and overlay: where the paint is saved, or that it isn't yet.</summary>
        public static string Describe(VertexPainter painter)
        {
            if (painter.PaintedMesh == null) return "Not painted yet. A mesh asset will be created on the first stroke.";
            if (!IsSavedAsAsset(painter)) return "Painted mesh is stored in the scene (legacy). It will be saved to an asset on the next stroke.";

            string path = AssetDatabase.GetAssetPath(painter.PaintedMesh);
            if (IsSharedWithPrefabSource(painter)) return $"Using prefab paint: {path}\nPainting this instance creates a unique copy.";
            return path;
        }

        /// <summary>
        /// Copies the current source mesh into the painted asset, keeping the painted colors when the vertex count
        /// is unchanged. Use after the source model has been reimported.
        /// </summary>
        public static void RebuildFromSource(VertexPainter painter)
        {
            if (painter.SourceMesh == null)
            {
                Debug.LogWarning("Vertex Painter has no source mesh to rebuild from.", painter);
                return;
            }

            Mesh painted = GetWritableMesh(painter);
            List<Color> colors = new List<Color>();
            painted.GetColors(colors);

            Undo.RegisterCompleteObjectUndo(painted, "Rebuild Painted Mesh");
            Mesh rebuilt = Object.Instantiate(painter.SourceMesh);
            rebuilt.name = painted.name;
            if (colors.Count == rebuilt.vertexCount)
            {
                rebuilt.SetColors(colors);
            }
            else
            {
                Debug.LogWarning($"Source mesh vertex count changed ({colors.Count} -> {rebuilt.vertexCount}), vertex colors were reset.", painter);
            }

            EditorUtility.CopySerialized(rebuilt, painted);
            Object.DestroyImmediate(rebuilt);
            EditorUtility.SetDirty(painted);
            AssetDatabase.SaveAssetIfDirty(painted);
            MeshGeometryChanged?.Invoke(painted);
        }

        // Raised when a painted mesh's vertices may have changed (not just its colors), so cached paint data can be rebuilt.
        internal static event System.Action<Mesh> MeshGeometryChanged;

        /// <summary>Restores the original mesh and removes the painter. The painted asset is left in the project.</summary>
        public static void RestoreSource(VertexPainter painter)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Remove Vertex Painter");
            if (painter.SourceMesh != null && painter.MeshFilter != null)
            {
                Undo.RecordObject(painter.MeshFilter, "Restore Source Mesh");
                painter.MeshFilter.sharedMesh = painter.SourceMesh;
            }
            Undo.DestroyObjectImmediate(painter);
        }

        // Returns why the painter needs a new mesh copy, or null if it can write to its current painted mesh.
        private static string GetCopyReason(VertexPainter painter)
        {
            Mesh painted = painter.PaintedMesh;
            if (painted == null) return "no painted mesh yet";
            if (!AssetDatabase.Contains(painted)) return $"painted mesh '{painted.name}' is not saved as an asset";
            if (IsSharedWithPrefabSource(painter)) return "mesh is inherited from the prefab source";
            VertexPainter other = FindOtherPainterSharingMesh(painter);
            if (other != null) return $"mesh is shared with '{other.name}' in scene '{other.gameObject.scene.name}'";
            return null;
        }

        // True when this painter is part of a prefab instance and still uses the prefab's painted mesh.
        private static bool IsSharedWithPrefabSource(VertexPainter painter)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(painter)) return false;
            VertexPainter source = PrefabUtility.GetCorrespondingObjectFromSource(painter);
            return source != null && source.PaintedMesh == painter.PaintedMesh;
        }

        /// <summary>
        /// Returns paths of all painted mesh assets in the project.
        /// Meshes are found by label, or by living in a paint folder (for meshes created before labels were added).
        /// </summary>
        public static List<string> FindAllPaintedMeshPaths()
        {
            HashSet<string> paths = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets($"t:Mesh l:{AssetLabel}", new[] { "Assets" }))
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".asset") && IsPaintFolder(Path.GetDirectoryName(path))) paths.Add(path);
            }
            return new List<string>(paths);
        }

        /// <summary>True for folders the painter saves into: named "VertexPaint" or ending in "_VertexPaint".</summary>
        public static bool IsPaintFolder(string folder)
        {
            string name = Path.GetFileName(folder);
            return name == "VertexPaint" || name.EndsWith("_VertexPaint");
        }

        /// <summary>All loaded scenes plus the open prefab stage.</summary>
        public static List<Scene> GetEditableScenes()
        {
            List<Scene> scenes = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) scenes.Add(scene);
            }
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null) scenes.Add(stage.scene);
            return scenes;
        }

        // Returns another painter in any loaded scene or the open prefab stage uses the same mesh (e.g. after duplicating).
        // Prefab instances that inherit the mesh from their prefab are ignored, the mesh belongs to the prefab, not the instance.
        // Without this, painting in Prefab Mode would copy on every stroke while instances of the prefab are open in a scene.
        private static VertexPainter FindOtherPainterSharingMesh(VertexPainter painter)
        {
            foreach (Scene scene in GetEditableScenes())
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (VertexPainter other in root.GetComponentsInChildren<VertexPainter>(true))
                    {
                        if (other == painter || other.PaintedMesh != painter.PaintedMesh) continue;
                        if (IsSharedWithPrefabSource(other)) continue;
                        return other;
                    }
                }
            }
            return null;
        }

        private static Mesh CreateCopy(VertexPainter painter, string reason)
        {
            Mesh template = painter.PaintedMesh != null ? painter.PaintedMesh : painter.SourceMesh;
            if (template == null) return null;

            Mesh copy = Object.Instantiate(template);
            string sourceName = painter.SourceMesh != null ? painter.SourceMesh.name : template.name;
            copy.name = $"{sourceName}_{GUID.Generate().ToString().Substring(0, 8)}";

            string folder = GetSaveFolder(painter);
            EnsureFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{copy.name}.asset");
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SetLabels(copy, new[] { AssetLabel });
            // setting labels can reimport the asset, reload so we don't hold a stale in-memory mesh
            copy = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            Debug.Log($"[Vertex Painter] Created {path} ({reason})", copy);

            Undo.RecordObject(painter, "Assign Painted Mesh");
            Undo.RecordObject(painter.MeshFilter, "Assign Painted Mesh");
            painter.PaintedMesh = copy;
            painter.MeshFilter.sharedMesh = copy;
            PrefabUtility.RecordPrefabInstancePropertyModifications(painter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(painter.MeshFilter);

            return copy;
        }

        // Prefab Mode: <PrefabDir>/<PrefabName>_VertexPaint
        // Saved scene: <SceneDir>/<SceneName>/VertexPaint (same convention as Unity's lighting data)
        // Otherwise:   Assets/VertexPaint
        private static string GetSaveFolder(VertexPainter painter)
        {
            PrefabStage stage = PrefabStageUtility.GetPrefabStage(painter.gameObject);
            if (stage != null)
            {
                string prefabPath = stage.assetPath;
                if (IsWritablePath(prefabPath))
                {
                    string dir = Path.GetDirectoryName(prefabPath).Replace('\\', '/');
                    return $"{dir}/{Path.GetFileNameWithoutExtension(prefabPath)}_VertexPaint";
                }
                return FallbackFolder;
            }

            string scenePath = painter.gameObject.scene.path;
            if (!string.IsNullOrEmpty(scenePath) && IsWritablePath(scenePath))
            {
                string dir = Path.GetDirectoryName(scenePath).Replace('\\', '/');
                return $"{dir}/{Path.GetFileNameWithoutExtension(scenePath)}/VertexPaint";
            }

            return FallbackFolder;
        }

        private static bool IsWritablePath(string path)
        {
            return path.StartsWith("Assets/");
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
