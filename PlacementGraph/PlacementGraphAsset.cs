using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// A compiled placement graph: the nodes in the order they run and the wires between them. The importer writes
    /// this from a .placegraph file; it has no Graph Toolkit dependency, so it works at runtime as well.
    /// </summary>
    public class PlacementGraphAsset : ScriptableObject
    {
        [SerializeReference] private List<PlacementNode> _nodes = new List<PlacementNode>();
        [SerializeField] private List<PlacementEdge> _edges = new List<PlacementEdge>();
        [SerializeField] private List<PlacementParameter> _parameters = new List<PlacementParameter>();
        [SerializeField] private List<PlacementParameterBinding> _bindings = new List<PlacementParameterBinding>();

        /// <summary>Nodes in execution order: every node comes after the nodes that feed it.</summary>
        public IReadOnlyList<PlacementNode> Nodes => _nodes;

        /// <summary>Wires, as indices into <see cref="Nodes"/>.</summary>
        public IReadOnlyList<PlacementEdge> Edges => _edges;

        /// <summary>The exposed parameters with their default values, in Blackboard order.</summary>
        public IReadOnlyList<PlacementParameter> Parameters => _parameters;

        /// <summary>Which node settings read which parameter. Node fields already hold the defaults.</summary>
        public IReadOnlyList<PlacementParameterBinding> Bindings => _bindings;

        /// <summary>Replaces the contents. Nodes must already be in execution order.</summary>
        public void SetContents(List<PlacementNode> nodes, List<PlacementEdge> edges,
            List<PlacementParameter> parameters = null, List<PlacementParameterBinding> bindings = null)
        {
            _nodes = nodes ?? new List<PlacementNode>();
            _edges = edges ?? new List<PlacementEdge>();
            _parameters = parameters ?? new List<PlacementParameter>();
            _bindings = bindings ?? new List<PlacementParameterBinding>();
        }

        /// <summary>The parameter called <paramref name="name"/>, or null.</summary>
        public PlacementParameter FindParameter(string name)
        {
            foreach (PlacementParameter parameter in _parameters)
            {
                if (parameter != null && parameter.Name == name) return parameter;
            }
            return null;
        }

        /// <summary>
        /// Puts nodes in an order where each node follows everything wired into it (Kahn's algorithm), and remaps the
        /// edges to match. Returns false if the wires form a cycle.
        /// </summary>
        public static bool TrySortTopologically(List<PlacementNode> nodes, List<PlacementEdge> edges,
            out List<PlacementNode> sortedNodes, out List<PlacementEdge> sortedEdges)
        {
            int count = nodes.Count;
            var incoming = new int[count];
            var outgoing = new List<int>[count];
            for (int i = 0; i < count; i++) outgoing[i] = new List<int>();
            foreach (PlacementEdge edge in edges)
            {
                incoming[edge.ToNode]++;
                outgoing[edge.FromNode].Add(edge.ToNode);
            }

            // A sorted ready list keeps the order stable: unrelated nodes stay in the order they were authored.
            var ready = new SortedSet<int>();
            for (int i = 0; i < count; i++)
            {
                if (incoming[i] == 0) ready.Add(i);
            }

            var order = new List<int>(count);
            while (ready.Count > 0)
            {
                int next = ready.Min;
                ready.Remove(next);
                order.Add(next);
                foreach (int target in outgoing[next])
                {
                    if (--incoming[target] == 0) ready.Add(target);
                }
            }

            sortedNodes = null;
            sortedEdges = null;
            if (order.Count != count) return false;

            var newIndex = new int[count];
            sortedNodes = new List<PlacementNode>(count);
            for (int i = 0; i < count; i++)
            {
                newIndex[order[i]] = i;
                sortedNodes.Add(nodes[order[i]]);
            }
            sortedEdges = new List<PlacementEdge>(edges.Count);
            foreach (PlacementEdge edge in edges)
            {
                sortedEdges.Add(new PlacementEdge(newIndex[edge.FromNode], edge.FromPort, newIndex[edge.ToNode], edge.ToPort));
            }
            return true;
        }
    }
}
