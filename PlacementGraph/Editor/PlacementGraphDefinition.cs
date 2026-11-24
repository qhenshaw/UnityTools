using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEditor;

namespace UnityTools.Placement.Editor
{
    /// <summary>
    /// The authoring graph, edited in Graph Toolkit's window and saved as a .placegraph file. On import it is
    /// compiled into a <see cref="PlacementGraphAsset"/> (see <see cref="PlacementGraphImporter"/>).
    /// </summary>
    [Graph(AssetExtension, GraphOptions.SupportsSubgraphs)]
    [Serializable]
    internal class PlacementGraphDefinition : Graph
    {
        public const string AssetExtension = "placegraph";

        [MenuItem("Assets/Create/UnityTools/Placement Graph", false)]
        private static void CreateAssetFile()
        {
            GraphDatabase.PromptInProjectBrowserToCreateNewAsset<PlacementGraphDefinition>();
        }

        public override void OnGraphChanged(GraphLogger graphLogger)
        {
            var problems = new List<string>();
            PlacementGraphCompiler.Validate(this, problems);
            foreach (string problem in problems) graphLogger.LogError(problem);
        }
    }
}
