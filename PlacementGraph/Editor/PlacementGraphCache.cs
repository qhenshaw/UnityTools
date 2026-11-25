using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityTools.Placement.Editor
{
    /// <summary>
    /// Frees Graph Toolkit's in-memory copy of a .placegraph once its file is deleted. Graph Toolkit keeps a hidden
    /// GraphObject for every graph it has loaded, even after the asset is gone, and enables it again on every domain
    /// reload; one whose subgraph node points to a deleted subgraph asset throws a NullReferenceException in
    /// SubgraphNodeModelImp.OnDefineNode each time. GraphObject is internal, so this goes through reflection and does
    /// nothing if that API changes.
    /// </summary>
    internal static class PlacementGraphCache
    {
        private static readonly Type GraphObjectType = typeof(Graph).Assembly.GetType("Unity.GraphToolkit.Editor.GraphObject");

        private static readonly PropertyInfo FilePathProperty =
            GraphObjectType?.GetProperty("FilePath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        /// <summary>Whether Graph Toolkit's GraphObject can be found (false: these helpers do nothing).</summary>
        public static bool Available => GraphObjectType != null && FilePathProperty != null;

        /// <summary>Graph Toolkit's loaded graph objects, or none if its API changed.</summary>
        public static List<Object> Loaded()
        {
            var loaded = new List<Object>();
            if (!Available) return loaded;
            try { loaded.AddRange(Resources.FindObjectsOfTypeAll(GraphObjectType)); }
            catch (Exception) { }
            return loaded;
        }

        /// <summary>The project path a graph object was loaded from, or null.</summary>
        public static string PathOf(Object graphObject)
        {
            if (!Available || graphObject == null) return null;
            try { return FilePathProperty.GetValue(graphObject) as string; }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Destroys the loaded graph objects whose .placegraph file no longer exists and for which
        /// <paramref name="match"/> is true. Graphs whose file is still there are never touched. Returns how many.
        /// </summary>
        public static int ReleaseDeleted(Func<string, bool> match) => ReleaseDeleted(Loaded(), match);

        /// <summary>As <see cref="ReleaseDeleted(Func{string, bool})"/>, but only among <paramref name="graphObjects"/>.</summary>
        public static int ReleaseDeleted(IEnumerable<Object> graphObjects, Func<string, bool> match)
        {
            int released = 0;
            foreach (Object graphObject in graphObjects)
            {
                if (graphObject == null) continue;
                string path = PathOf(graphObject);
                if (string.IsNullOrEmpty(path) || !path.EndsWith("." + PlacementGraphDefinition.AssetExtension, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(path) || !match(path)) continue;
                try
                {
                    Object.DestroyImmediate(graphObject);
                    released++;
                }
                catch (Exception)
                {
                    // Freeing is only a tidy-up; a graph object that can't be destroyed is left as it was.
                }
            }
            return released;
        }

        /// <summary>Frees the graph objects of deleted paths: files, or everything under a deleted folder.</summary>
        public static int ReleaseDeleted(IReadOnlyCollection<string> deletedPaths)
        {
            if (deletedPaths == null || deletedPaths.Count == 0) return 0;
            return ReleaseDeleted(path =>
            {
                foreach (string deleted in deletedPaths)
                {
                    if (string.IsNullOrEmpty(deleted)) continue;
                    if (string.Equals(path, deleted, StringComparison.OrdinalIgnoreCase)) return true;
                    if (path.StartsWith(deleted.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            });
        }
    }

    /// <summary>Frees Graph Toolkit's in-memory copy of each .placegraph deleted in the editor (see <see cref="PlacementGraphCache"/>).</summary>
    internal sealed class PlacementGraphDeletePostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (deleted == null || deleted.Length == 0) return;
            // Import workers have no graph objects of the editor's; only the editor itself frees them.
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            var paths = new List<string>();
            foreach (string path in deleted)
            {
                // A folder's contents are reported too, but a deleted folder on its own is matched by prefix.
                if (path.EndsWith("." + PlacementGraphDefinition.AssetExtension, StringComparison.OrdinalIgnoreCase) || !Path.HasExtension(path))
                    paths.Add(path);
            }
            if (paths.Count > 0) PlacementGraphCache.ReleaseDeleted(paths);
        }
    }
}
