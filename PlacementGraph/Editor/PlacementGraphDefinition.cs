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

        // Variables that existed when the graph loaded or were already seen, so only new ones are changed.
        [NonSerialized] private HashSet<IVariable> _knownVariables;

        public override void OnEnable()
        {
            base.OnEnable();
            RememberVariables();
        }

        private void RememberVariables()
        {
            _knownVariables = new HashSet<IVariable>(GetVariables());
        }

        /// <summary>
        /// Graph Toolkit creates Blackboard variables as Local. In a placement graph a new variable is made Input
        /// instead, so it becomes a port when the graph is used as a subgraph (in the main graph both kinds are
        /// parameters). Variables that existed before, or that are switched back to Local, are left alone.
        /// </summary>
        internal void MakeNewVariablesInputs()
        {
            if (_knownVariables == null)
            {
                RememberVariables();
                return;
            }
            foreach (IVariable variable in GetVariables())
            {
                if (!_knownVariables.Add(variable)) continue;
                if (variable.VariableKind == VariableKind.Local) variable.VariableKind = VariableKind.Input;
            }
        }

        public override void OnGraphChanged(GraphLogger graphLogger)
        {
            MakeNewVariablesInputs();
            var problems = new List<string>();
            PlacementGraphCompiler.Validate(this, problems);
            foreach (string problem in problems) graphLogger.LogError(problem);
        }
    }
}
