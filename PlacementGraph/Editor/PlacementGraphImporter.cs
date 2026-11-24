using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace UnityTools.Placement.Editor
{
    /// <summary>
    /// Imports a .placegraph file: loads the Graph Toolkit graph and compiles it into a <see cref="PlacementGraphAsset"/>,
    /// the main object, which is what a Placement Graph component references.
    /// </summary>
    [ScriptedImporter(1, PlacementGraphDefinition.AssetExtension)]
    internal class PlacementGraphImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = ScriptableObject.CreateInstance<PlacementGraphAsset>();
            PlacementGraphDefinition graph = GraphDatabase.LoadGraphForImporter<PlacementGraphDefinition>(ctx.assetPath);
            if (graph == null)
            {
                ctx.LogImportError($"Couldn't load the placement graph at {ctx.assetPath}.");
            }
            else
            {
                var errors = new List<string>();
                var warnings = new List<string>();
                PlacementGraphCompiler.TryCompile(graph, asset, errors, warnings);
                foreach (string error in errors) ctx.LogImportError(error);
                foreach (string warning in warnings) ctx.LogImportWarning(warning);
            }
            ctx.AddObjectToAsset("PlacementGraph", asset);
            ctx.SetMainObject(asset);
        }
    }

    /// <summary>Regenerates Placement Graph components when their graph is reimported or a Prefab Palette changes.</summary>
    internal class PlacementGraphPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported)
            {
                if (path.EndsWith("." + PlacementGraphDefinition.AssetExtension))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<PlacementGraphAsset>(path);
                    if (asset != null) PlacementGraph.MarkUsersDirty(asset);
                }
                else if (AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(PrefabPalette))
                {
                    // A palette can be used by any graph; rerunning them all is cheap enough for edit time.
                    PlacementGraph.MarkUsersDirty(null);
                }
            }
        }
    }
}
