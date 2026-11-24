using System;
using System.Collections.Generic;
using System.Linq;
using Unity.GraphToolkit.Editor;

namespace UnityTools.Placement.Editor
{
    /// <summary>
    /// Turns a Graph Toolkit placement graph into the runtime node list, wires, parameters and bindings. Subgraph
    /// nodes are inlined: their nodes are compiled as if they were in the main graph, with the subgraph's Input and
    /// Output variables standing for the subgraph node's ports.
    /// </summary>
    internal static class PlacementGraphCompiler
    {
        /// <summary>Subgraphs nested deeper than this are an error, which also catches a subgraph that contains itself.</summary>
        public const int MaxSubgraphDepth = 16;

        /// <summary>
        /// A graph being compiled: the main graph, or one use of a subgraph (each subgraph node is its own scope, so
        /// two nodes using the same subgraph get separate nodes and random streams).
        /// </summary>
        private sealed class Scope
        {
            public Graph Graph;
            public ISubgraphNode Node;
            public Scope Outer;
            public string Path = "";
            public string Title = "";
            public int Depth;
            public readonly Dictionary<INode, int> Units = new Dictionary<INode, int>();
            public readonly Dictionary<INode, Scope> Children = new Dictionary<INode, Scope>();
        }

        private struct Unit
        {
            public PlacementGraphNode Node;
            public Scope Scope;
        }

        /// <summary>
        /// Compiles <paramref name="graph"/> into <paramref name="asset"/>. Returns false and fills
        /// <paramref name="errors"/> when it can't (a cycle, a subgraph that contains itself);
        /// <paramref name="warnings"/> lists things that still compile. <paramref name="dependencies"/> receives the
        /// asset paths of the subgraphs used, so the importer can reimport when they change.
        /// </summary>
        public static bool TryCompile(Graph graph, PlacementGraphAsset asset, List<string> errors, List<string> warnings,
            List<string> dependencies = null)
        {
            List<PlacementParameter> parameters = CollectParameters(graph, warnings);

            var units = new List<Unit>();
            var root = new Scope { Graph = graph };
            if (!Collect(root, units, errors, warnings, dependencies))
            {
                asset.SetContents(new List<PlacementNode>(), new List<PlacementEdge>(), parameters);
                return false;
            }

            var runtimeNodes = new List<PlacementNode>(units.Count);
            var nodeBindings = new List<(PlacementNode node, string field, string parameter)>();
            var bindings = new List<(string field, string parameter)>();
            var usedSeeds = new HashSet<string>();
            for (int i = 0; i < units.Count; i++)
            {
                Unit unit = units[i];
                bindings.Clear();
                SettingResolver resolver = unit.Scope.Node == null ? SettingResolver.Default : new SubgraphSettingResolver(unit.Scope);
                PlacementNode runtime = unit.Node.CreateRuntimeNode(resolver, bindings);
                string id = string.IsNullOrEmpty(unit.Node.Id) ? i.ToString() : unit.Node.Id;
                runtime.Name = $"{i}: {unit.Scope.Title}{unit.Node.DisplayName}";
                // Nodes in the main graph hash their id alone, so adding subgraph support didn't reshuffle old graphs.
                // A copy that still has its original's id (a graph not edited since the paste) gets its own seed too.
                string key = unit.Scope.Path + id;
                if (!usedSeeds.Add(key))
                {
                    int copy = 1;
                    while (!usedSeeds.Add($"{key}#{copy}")) copy++;
                    key = $"{key}#{copy}";
                }
                runtime.NodeSeed = StableHash(key);
                runtimeNodes.Add(runtime);
                foreach ((string field, string parameter) in bindings) nodeBindings.Add((runtime, field, parameter));
            }

            var edges = new List<PlacementEdge>();
            for (int to = 0; to < units.Count; to++)
            {
                List<IPort> inputs = PointInputs(units[to].Node);
                for (int toPort = 0; toPort < inputs.Count; toPort++)
                {
                    foreach ((int from, int fromPort) in Sources(inputs[toPort], units[to].Scope, 0))
                        edges.Add(new PlacementEdge(from, fromPort, to, toPort));
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

        /// <summary>Finds every placement node in <paramref name="scope"/> and, recursively, in its subgraphs.</summary>
        private static bool Collect(Scope scope, List<Unit> units, List<string> errors, List<string> warnings, List<string> dependencies)
        {
            int subgraphOrdinal = 0;
            foreach (INode node in scope.Graph.GetNodes())
            {
                switch (node)
                {
                    case PlacementGraphNode placementNode:
                        scope.Units[node] = units.Count;
                        units.Add(new Unit { Node = placementNode, Scope = scope });
                        break;
                    case ISubgraphNode subgraphNode:
                    {
                        int ordinal = subgraphOrdinal++;
                        Graph subgraph = subgraphNode.GetSubgraph();
                        if (subgraph == null)
                        {
                            warnings?.Add($"Subgraph node '{subgraphNode.Title}' has no graph and is ignored.");
                            break;
                        }
                        if (subgraph is not PlacementGraphDefinition)
                        {
                            warnings?.Add($"Subgraph '{subgraphNode.Title}' isn't a placement graph and is ignored.");
                            break;
                        }
                        if (scope.Depth + 1 > MaxSubgraphDepth || ContainsGraph(scope, subgraph))
                        {
                            errors?.Add($"Subgraph '{subgraphNode.Title}' contains itself, or subgraphs are nested more than {MaxSubgraphDepth} deep.");
                            return false;
                        }
                        string path = GraphDatabase.GetGraphAssetPath(subgraph);
                        string rootPath = GraphDatabase.GetGraphAssetPath(Root(scope).Graph);
                        if (!string.IsNullOrEmpty(path) && path != rootPath && dependencies != null && !dependencies.Contains(path))
                            dependencies.Add(path);

                        var child = new Scope
                        {
                            Graph = subgraph,
                            Node = subgraphNode,
                            Outer = scope,
                            Path = $"{scope.Path}{ordinal}:{subgraph.Name}/",
                            Title = $"{scope.Title}{subgraphNode.Title} / ",
                            Depth = scope.Depth + 1
                        };
                        scope.Children[node] = child;
                        if (!Collect(child, units, errors, warnings, dependencies)) return false;
                        break;
                    }
                    case IVariableNode:
                    case IConstantNode:
                        break;
                    default:
                        warnings?.Add($"{node.GetType().Name} isn't a placement node and is ignored.");
                        break;
                }
            }
            return true;
        }

        private static bool ContainsGraph(Scope scope, Graph graph)
        {
            for (Scope s = scope; s != null; s = s.Outer)
            {
                if (s.Graph == graph) return true;
            }
            return false;
        }

        private static Scope Root(Scope scope)
        {
            while (scope.Outer != null) scope = scope.Outer;
            return scope;
        }

        /// <summary>
        /// The compiled node outputs that feed a point input: placement nodes directly, through subgraph nodes into
        /// their Output variables, and out of a subgraph through its Input variables to the subgraph node's port.
        /// </summary>
        private static List<(int node, int port)> Sources(IPort input, Scope scope, int depth)
        {
            var result = new List<(int, int)>();
            if (depth > MaxSubgraphDepth * 4) return result;
            var connected = new List<IPort>();
            input.GetConnectedPorts(connected);
            foreach (IPort output in connected)
            {
                INode node = output.GetNode();
                switch (node)
                {
                    case PlacementGraphNode when scope.Units.TryGetValue(node, out int index):
                    {
                        int port = PointOutputs(node).IndexOf(output);
                        if (port >= 0) result.Add((index, port));
                        break;
                    }
                    case ISubgraphNode when scope.Children.TryGetValue(node, out Scope child):
                    {
                        foreach (IVariableNode outputNode in VariableNodes(child.Graph, output.DisplayName, VariableKind.Output))
                        {
                            foreach (IPort inner in outputNode.GetInputPorts())
                                result.AddRange(Sources(inner, child, depth + 1));
                        }
                        break;
                    }
                    case IVariableNode variableNode when variableNode.Variable.VariableKind == VariableKind.Input && scope.Node != null:
                    {
                        IPort outerPort = SubgraphPort(scope.Node.GetInputPorts(), variableNode.Variable.Name);
                        if (outerPort != null) result.AddRange(Sources(outerPort, scope.Outer, depth + 1));
                        break;
                    }
                }
            }
            return result;
        }

        private static IEnumerable<IVariableNode> VariableNodes(Graph graph, string name, VariableKind kind)
        {
            foreach (INode node in graph.GetNodes())
            {
                if (node is IVariableNode variableNode && variableNode.Variable.VariableKind == kind && variableNode.Variable.Name == name)
                    yield return variableNode;
            }
        }

        /// <summary>A subgraph node's port for a variable. Ports are labelled with the variable's name.</summary>
        internal static IPort SubgraphPort(IEnumerable<IPort> ports, string variableName) =>
            ports.FirstOrDefault(port => port.DisplayName == variableName);

        /// <summary>
        /// Resolves settings inside a subgraph: an Input variable reads what the subgraph node's matching port gets
        /// (its inline field, or what's wired into it, which can be a parameter of the main graph).
        /// </summary>
        private sealed class SubgraphSettingResolver : SettingResolver
        {
            private readonly Scope _scope;

            public SubgraphSettingResolver(Scope scope) => _scope = scope;

            protected override bool TryResolveVariable(IVariable variable, Type type, out object value, out string parameter)
            {
                if (variable.VariableKind == VariableKind.Input)
                {
                    IPort outerPort = SubgraphPort(_scope.Node.GetInputPorts(), variable.Name);
                    if (outerPort != null && outerPort.DataType == type)
                    {
                        SettingResolver outer = _scope.Outer.Node == null ? Default : new SubgraphSettingResolver(_scope.Outer);
                        if (outer.TryResolve(outerPort, out value, out parameter)) return true;
                    }
                }
                // Not a parameter: only the main graph's Input variables are.
                bool found = base.TryResolveVariable(variable, type, out value, out _);
                parameter = null;
                return found;
            }
        }

        /// <summary>
        /// The graph's Blackboard variables of a parameter type, with their defaults, in Blackboard order. Local and Input
        /// variables both count (Graph Toolkit makes new variables Local); Output variables and point variables are for
        /// subgraphs.
        /// </summary>
        public static List<PlacementParameter> CollectParameters(Graph graph, List<string> warnings)
        {
            var parameters = new List<PlacementParameter>();
            foreach (IVariable variable in graph.GetVariables(SortMethod.Display))
            {
                if (variable.VariableKind == VariableKind.Output || variable.DataType == typeof(PointData)) continue;
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

        /// <summary>
        /// Problems worth showing while the graph is edited. A graph with Output variables is treated as a subgraph,
        /// which doesn't need its own Spawn Prefabs node.
        /// </summary>
        public static void Validate(Graph graph, List<string> problems)
        {
            foreach (INode node in graph.GetNodes())
            {
                if (node is PlacementGraphNode placementNode) placementNode.Validate(problems);
            }
            bool isSubgraph = graph.GetVariables().Any(v => v.VariableKind == VariableKind.Output);
            if (!isSubgraph && !HasSpawner(graph, 0))
                problems.Add("The graph has no Spawn Prefabs node, so it won't place anything.");
        }

        private static bool HasSpawner(Graph graph, int depth)
        {
            if (graph == null || depth > MaxSubgraphDepth) return false;
            foreach (INode node in graph.GetNodes())
            {
                if (node is SpawnPrefabsNode) return true;
                if (node is ISubgraphNode subgraphNode && HasSpawner(subgraphNode.GetSubgraph(), depth + 1)) return true;
            }
            return false;
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
