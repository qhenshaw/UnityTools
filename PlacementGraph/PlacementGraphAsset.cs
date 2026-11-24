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

        /// <summary>Nodes in execution order: every node comes after the nodes that feed it.</summary>
        public IReadOnlyList<PlacementNode> Nodes => _nodes;

        /// <summary>Wires, as indices into <see cref="Nodes"/>.</summary>
        public IReadOnlyList<PlacementEdge> Edges => _edges;

        /// <summary>Replaces the contents. Nodes must already be in execution order.</summary>
        public void SetContents(List<PlacementNode> nodes, List<PlacementEdge> edges)
        {
            _nodes = nodes ?? new List<PlacementNode>();
            _edges = edges ?? new List<PlacementEdge>();
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
