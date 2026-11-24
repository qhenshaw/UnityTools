using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityTools.Placement
{
    /// <summary>Runs a compiled placement graph: each node in order, passing point data along the wires.</summary>
    public static class PlacementExecutor
    {
        /// <summary>
        /// Runs <paramref name="graph"/> with <paramref name="context"/>. Spawner nodes send objects to
        /// <see cref="PlacementContext.Output"/>. Returns the outputs of every node, by node index.
        /// </summary>
        public static PointData[][] Run(PlacementGraphAsset graph, PlacementContext context)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Overrides are written into the asset's nodes for the run and put back afterwards, so the asset never
            // keeps an instance's values.
            List<(PlacementNode node, FieldInfo field, object previous)> applied = ApplyOverrides(graph, context);
            try
            {
                return RunNodes(graph, context);
            }
            finally
            {
                for (int i = applied.Count - 1; i >= 0; i--) applied[i].field.SetValue(applied[i].node, applied[i].previous);
            }
        }

        private static readonly Dictionary<(Type, string), FieldInfo> FieldCache = new Dictionary<(Type, string), FieldInfo>();

        /// <summary>The public instance field a binding names, or null.</summary>
        internal static FieldInfo FindField(Type type, string name)
        {
            if (FieldCache.TryGetValue((type, name), out FieldInfo field)) return field;
            field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            FieldCache[(type, name)] = field;
            return field;
        }

        private static List<(PlacementNode, FieldInfo, object)> ApplyOverrides(PlacementGraphAsset graph, PlacementContext context)
        {
            var applied = new List<(PlacementNode, FieldInfo, object)>();
            if (context.ParameterOverrides.Count == 0 || graph.Bindings.Count == 0) return applied;

            foreach (PlacementParameterBinding binding in graph.Bindings)
            {
                if (binding.Node < 0 || binding.Node >= graph.Nodes.Count || graph.Nodes[binding.Node] == null) continue;
                PlacementParameter declared = graph.FindParameter(binding.Parameter);
                if (declared == null) continue;
                PlacementParameter value = null;
                foreach (PlacementParameter candidate in context.ParameterOverrides)
                {
                    if (candidate != null && candidate.Name == binding.Parameter && candidate.Type == declared.Type) value = candidate;
                }
                if (value == null) continue;

                PlacementNode node = graph.Nodes[binding.Node];
                FieldInfo field = FindField(node.GetType(), binding.Field);
                Type valueType = PlacementParameter.SystemTypeOf(value.Type);
                if (field == null || !field.FieldType.IsAssignableFrom(valueType)) continue;
                applied.Add((node, field, field.GetValue(node)));
                field.SetValue(node, value.Value);
            }
            return applied;
        }

        private static PointData[][] RunNodes(PlacementGraphAsset graph, PlacementContext context)
        {
            IReadOnlyList<PlacementNode> nodes = graph.Nodes;
            IReadOnlyList<PlacementEdge> edges = graph.Edges;
            var outputs = new PointData[nodes.Count][];

            // Wires into each node, in authored order, so merged inputs are deterministic.
            var incoming = new List<PlacementEdge>[nodes.Count];
            for (int i = 0; i < edges.Count; i++)
            {
                PlacementEdge edge = edges[i];
                if (edge.ToNode < 0 || edge.ToNode >= nodes.Count) continue;
                (incoming[edge.ToNode] ??= new List<PlacementEdge>()).Add(edge);
            }

            context.NodeOutputCounts.Clear();
            var gathered = new List<PointData>();
            for (int n = 0; n < nodes.Count; n++)
            {
                PlacementNode node = nodes[n];
                if (node == null)
                {
                    outputs[n] = Array.Empty<PointData>();
                    context.NodeOutputCounts.Add(0);
                    continue;
                }

                var inputs = new PointData[node.InputCount];
                var connected = new bool[node.InputCount];
                for (int port = 0; port < inputs.Length; port++)
                {
                    gathered.Clear();
                    if (incoming[n] != null)
                    {
                        foreach (PlacementEdge edge in incoming[n])
                        {
                            if (edge.ToPort != port || edge.FromNode >= n) continue;
                            connected[port] = true;
                            PointData[] source = outputs[edge.FromNode];
                            if (source != null && edge.FromPort >= 0 && edge.FromPort < source.Length)
                                gathered.Add(source[edge.FromPort]);
                        }
                    }
                    inputs[port] = PointData.Merge(gathered);
                }

                var nodeOutputs = new PointData[node.OutputCount];
                context.InputConnected = connected;
                node.Execute(context, inputs, nodeOutputs);
                for (int port = 0; port < nodeOutputs.Length; port++) nodeOutputs[port] ??= PointData.Empty;

                outputs[n] = nodeOutputs;
                context.NodeOutputCounts.Add(nodeOutputs.Length > 0 ? nodeOutputs[0].Count : 0);
            }

            context.InputConnected = Array.Empty<bool>();
            if (context.NodeOutputs != null)
            {
                context.NodeOutputs.Clear();
                context.NodeOutputs.AddRange(outputs);
            }
            return outputs;
        }
    }
}
