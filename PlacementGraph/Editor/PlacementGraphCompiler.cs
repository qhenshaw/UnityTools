using System.Collections.Generic;
using System.Linq;
using Unity.GraphToolkit.Editor;

namespace UnityTools.Placement.Editor
{
    /// <summary>Turns a Graph Toolkit placement graph into the runtime node list, wires, parameters and bindings.</summary>
    internal static class PlacementGraphCompiler
    {
        /// <summary>
        /// Compiles <paramref name="graph"/> into <paramref name="asset"/>. Returns false and fills
        /// <paramref name="errors"/> when it can't (a cycle); <paramref name="warnings"/> lists things that still compile.
        /// </summary>
        public static bool TryCompile(Graph graph, PlacementGraphAsset asset, List<string> errors, List<string> warnings)
        {
            List<PlacementParameter> parameters = CollectParameters(graph, warnings);

            var graphNodes = new List<PlacementGraphNode>();
            foreach (INode node in graph.GetNodes())
            {
                if (node is PlacementGraphNode placementNode) graphNodes.Add(placementNode);
                else if (node is IVariableNode || node is IConstantNode) continue;
                else warnings?.Add($"{node.GetType().Name} isn't a placement node and is ignored.");
            }

            var indexOf = new Dictionary<INode, int>();
            var runtimeNodes = new List<PlacementNode>(graphNodes.Count);
            var nodeBindings = new List<(PlacementNode node, string field, string parameter)>();
            var bindings = new List<(string field, string parameter)>();
            for (int i = 0; i < graphNodes.Count; i++)
            {
                PlacementGraphNode graphNode = graphNodes[i];
                indexOf[graphNode] = i;
                bindings.Clear();
                PlacementNode runtime = graphNode.CreateRuntimeNode(SettingResolver.Default, bindings);
                runtime.Name = $"{i}: {graphNode.DisplayName}";
                runtime.NodeSeed = StableHash(string.IsNullOrEmpty(graphNode.Id) ? i.ToString() : graphNode.Id);
                runtimeNodes.Add(runtime);
                foreach ((string field, string parameter) in bindings) nodeBindings.Add((runtime, field, parameter));
            }

            var edges = new List<PlacementEdge>();
            var connected = new List<IPort>();
            for (int to = 0; to < graphNodes.Count; to++)
            {
                List<IPort> inputs = PointInputs(graphNodes[to]);
                for (int toPort = 0; toPort < inputs.Count; toPort++)
                {
                    connected.Clear();
                    inputs[toPort].GetConnectedPorts(connected);
                    foreach (IPort source in connected)
                    {
                        INode sourceNode = source.GetNode();
                        if (sourceNode == null || !indexOf.TryGetValue(sourceNode, out int from)) continue;
                        int fromPort = PointOutputs(sourceNode).IndexOf(source);
                        if (fromPort < 0) continue;
                        edges.Add(new PlacementEdge(from, fromPort, to, toPort));
                    }
                }
            }

            if (!PlacementGraphAsset.TrySortTopologically(runtimeNodes, edges, out List<PlacementNode> sortedNodes, out List<PlacementEdge> sortedEdges))
            {
                errors?.Add("The graph has a cycle: a node's output leads back into its own input.");
                asset.SetContents(new List<PlacementNode>(), new List<PlacementEdge>(), parameters);
                return false;
            }

            // Names carry the authored index; after sorting, number them by run order so they match the component's list.
            for (int i = 0; i < sortedNodes.Count; i++)
            {
                string name = sortedNodes[i].Name;
                int colon = name.IndexOf(':');
                sortedNodes[i].Name = $"{i}:{(colon >= 0 ? name.Substring(colon + 1) : " " + name)}";
            }

            var compiledBindings = new List<PlacementParameterBinding>();
            foreach ((PlacementNode node, string field, string parameter) in nodeBindings)
            {
                if (parameters.All(p => p.Name != parameter)) continue;
                compiledBindings.Add(new PlacementParameterBinding(sortedNodes.IndexOf(node), field, parameter));
            }

            asset.SetContents(sortedNodes, sortedEdges, parameters, compiledBindings);
            return true;
        }

        /// <summary>The graph's Input variables of a parameter type, with their defaults, in Blackboard order.</summary>
        public static List<PlacementParameter> CollectParameters(Graph graph, List<string> warnings)
        {
            var parameters = new List<PlacementParameter>();
            foreach (IVariable variable in graph.GetVariables(SortMethod.Display))
            {
                if (variable.VariableKind != VariableKind.Input || variable.DataType == typeof(PointData)) continue;
                if (!PlacementParameter.TryGetType(variable.DataType, out PlacementParameterType type))
                {
                    warnings?.Add($"Variable '{variable.Name}' is a {variable.DataType?.Name}, which can't be a parameter. Use float, int, bool, Vector3, Prefab Palette or LayerMask.");
                    continue;
                }
                if (parameters.Any(p => p.Name == variable.Name))
                {
                    warnings?.Add($"There are two parameters called '{variable.Name}'; only the first is exposed.");
                    continue;
                }
                var parameter = new PlacementParameter(variable.Name, type);
                if (TryGetDefault(variable, out object value) && value != null) parameter.Value = value;
                parameters.Add(parameter);
            }
            return parameters;
        }

        private static bool TryGetDefault(IVariable variable, out object value)
        {
            var method = typeof(IVariable).GetMethod(nameof(IVariable.TryGetDefaultValue)).MakeGenericMethod(variable.DataType);
            var arguments = new object[] { null };
            bool found = (bool)method.Invoke(variable, arguments);
            value = arguments[0];
            return found;
        }

        /// <summary>A node's point inputs, in order (the runtime node's input indices).</summary>
        public static List<IPort> PointInputs(INode node) => node.GetInputPorts().Where(PlacementGraphNode.IsPointPort).ToList();

        /// <summary>A node's point outputs, in order (the runtime node's output indices).</summary>
        public static List<IPort> PointOutputs(INode node) => node.GetOutputPorts().Where(PlacementGraphNode.IsPointPort).ToList();

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
