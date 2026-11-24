using System.Collections.Generic;
using System.Linq;
using Unity.GraphToolkit.Editor;

namespace UnityTools.Placement.Editor
{
    /// <summary>Turns a Graph Toolkit placement graph into the runtime node list and wires.</summary>
    internal static class PlacementGraphCompiler
    {
        /// <summary>
        /// Compiles <paramref name="graph"/> into <paramref name="asset"/>. Returns false and fills
        /// <paramref name="errors"/> when it can't (a cycle); <paramref name="warnings"/> lists things that still compile.
        /// </summary>
        public static bool TryCompile(Graph graph, PlacementGraphAsset asset, List<string> errors, List<string> warnings)
        {
            var graphNodes = new List<PlacementGraphNode>();
            foreach (INode node in graph.GetNodes())
            {
                if (node is PlacementGraphNode placementNode) graphNodes.Add(placementNode);
                else warnings?.Add($"{node.GetType().Name} isn't a placement node and is ignored.");
            }

            var indexOf = new Dictionary<INode, int>();
            var runtimeNodes = new List<PlacementNode>(graphNodes.Count);
            for (int i = 0; i < graphNodes.Count; i++)
            {
                PlacementGraphNode graphNode = graphNodes[i];
                indexOf[graphNode] = i;
                PlacementNode runtime = graphNode.CreateRuntimeNode();
                runtime.Name = $"{i}: {graphNode.DisplayName}";
                runtime.NodeSeed = StableHash(string.IsNullOrEmpty(graphNode.Id) ? i.ToString() : graphNode.Id);
                runtimeNodes.Add(runtime);
            }

            var edges = new List<PlacementEdge>();
            var connected = new List<IPort>();
            for (int to = 0; to < graphNodes.Count; to++)
            {
                List<IPort> inputs = graphNodes[to].GetInputPorts().ToList();
                for (int toPort = 0; toPort < inputs.Count; toPort++)
                {
                    inputs[toPort].GetConnectedPorts(connected);
                    foreach (IPort source in connected)
                    {
                        INode sourceNode = source.GetNode();
                        if (sourceNode == null || !indexOf.TryGetValue(sourceNode, out int from)) continue;
                        int fromPort = sourceNode.GetOutputPorts().ToList().IndexOf(source);
                        if (fromPort < 0) continue;
                        edges.Add(new PlacementEdge(from, fromPort, to, toPort));
                    }
                }
            }

            if (!PlacementGraphAsset.TrySortTopologically(runtimeNodes, edges, out List<PlacementNode> sortedNodes, out List<PlacementEdge> sortedEdges))
            {
                errors?.Add("The graph has a cycle: a node's output leads back into its own input.");
                asset.SetContents(new List<PlacementNode>(), new List<PlacementEdge>());
                return false;
            }

            // Names carry the authored index; after sorting, number them by run order so they match the component's list.
            for (int i = 0; i < sortedNodes.Count; i++)
            {
                string name = sortedNodes[i].Name;
                int colon = name.IndexOf(':');
                sortedNodes[i].Name = $"{i}:{(colon >= 0 ? name.Substring(colon + 1) : " " + name)}";
            }
            asset.SetContents(sortedNodes, sortedEdges);
            return true;
        }

        /// <summary>Problems worth showing while the graph is edited.</summary>
        public static void Validate(Graph graph, List<string> problems)
        {
            bool hasSpawner = false;
            foreach (INode node in graph.GetNodes())
            {
                if (node is not PlacementGraphNode placementNode) continue;
                if (placementNode is SpawnPrefabsNode) hasSpawner = true;
                placementNode.Validate(problems);
            }
            if (!hasSpawner) problems.Add("The graph has no Spawn Prefabs node, so it won't place anything.");
        }

        /// <summary>FNV-1a, stable across runs and platforms (string.GetHashCode isn't guaranteed to be).</summary>
        public static uint StableHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in text)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return hash;
            }
        }
    }
}
