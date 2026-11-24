using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace UnityTools.Placement.Editor
{
    /// <summary>
    /// Builds and edits Placement Graphs from code: for editor scripts, tests and AI agents. The result is an ordinary
    /// .placegraph asset that opens in the graph window; this only drives Graph Toolkit's own editing API.
    /// <code>
    /// PlacementGraphHandle graph = PlacementGraphAuthoring.Create("Assets/Forest.placegraph");
    /// PlacementNodeHandle sampler = graph.AddNode("Surface Sampler").Set("Spacing", 2f);
    /// PlacementNodeHandle spawn = graph.AddNode("Spawn Prefabs").Set("Palette", palette);
    /// graph.Connect(sampler, "Out", spawn, "In");
    /// graph.Save();
    /// </code>
    /// Node names are the titles in the graph window, and port and setting names are as shown on the nodes.
    /// <see cref="ListNodeTypes"/> and <see cref="DescribeNodeTypes"/> list them. Mistakes throw
    /// <see cref="ArgumentException"/> or <see cref="InvalidOperationException"/> with what was expected.
    /// </summary>
    public static class PlacementGraphAuthoring
    {
        /// <summary>The file extension of placement graphs, without the dot.</summary>
        public const string Extension = PlacementGraphDefinition.AssetExtension;

        private static List<PlacementNodeTypeInfo> _nodeTypes;
        private static Dictionary<string, Type> _typesByKey;

        /// <summary>
        /// Creates a new, empty graph asset at <paramref name="path"/> (under Assets/, ending in .placegraph). Throws if a
        /// file is already there, unless <paramref name="overwrite"/> is true.
        /// </summary>
        public static PlacementGraphHandle Create(string path, bool overwrite = false)
        {
            CheckPath(path);
            if (File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                if (!overwrite) throw new InvalidOperationException($"There's already a file at {path}. Pass overwrite: true to replace it.");
                AssetDatabase.DeleteAsset(path);
            }
            string folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
                throw new ArgumentException($"The folder {folder} doesn't exist.", nameof(path));
            var graph = GraphDatabase.CreateGraph<PlacementGraphDefinition>(path);
            if (graph == null) throw new InvalidOperationException($"Graph Toolkit couldn't create a graph at {path}.");
            return new PlacementGraphHandle(graph, path);
        }

        /// <summary>Opens an existing graph asset for editing.</summary>
        public static PlacementGraphHandle Open(string path)
        {
            CheckPath(path);
            var graph = GraphDatabase.LoadGraph<PlacementGraphDefinition>(path);
            if (graph == null) throw new FileNotFoundException($"There's no placement graph at {path}.", path);
            return new PlacementGraphHandle(graph, path);
        }

        private static void CheckPath(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("A path is needed.", nameof(path));
            if (!path.EndsWith("." + Extension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"A placement graph path must end in .{Extension}: {path}", nameof(path));
            if (!path.StartsWith("Assets/") && !path.StartsWith("Packages/"))
                throw new ArgumentException($"Use a project path starting with Assets/ (or Packages/): {path}", nameof(path));
        }

        /// <summary>Every node type: its title, category, ports and settings with their types and defaults.</summary>
        public static IReadOnlyList<PlacementNodeTypeInfo> ListNodeTypes()
        {
            if (_nodeTypes != null) return _nodeTypes;
            _nodeTypes = new List<PlacementNodeTypeInfo>();
            _typesByKey = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<Type> types = typeof(PlacementGraphNode).Assembly.GetTypes()
                .Where(t => typeof(PlacementGraphNode).IsAssignableFrom(t) && !t.IsAbstract);
            foreach (Type type in types)
            {
                var node = (PlacementGraphNode)Activator.CreateInstance(type);
                var attribute = (NodeAttribute)Attribute.GetCustomAttribute(type, typeof(NodeAttribute));
                List<PlacementGraphNode.PortDescription> ports = node.Describe();
                var info = new PlacementNodeTypeInfo(
                    PlacementGraphNode.TitleOf(type),
                    attribute?.CategoryPath ?? "",
                    node.RuntimeType,
                    ports.Where(p => p.IsInput && p.DataType == typeof(PointData)).Select(p => p.Name).ToArray(),
                    ports.Where(p => !p.IsInput).Select(p => p.Name).ToArray(),
                    ports.Where(p => p.IsInput && p.DataType != typeof(PointData))
                        .Select(p => new PlacementSettingInfo(p.Name, p.DataType, p.DefaultValue, p.Tooltip)).ToArray());
                _nodeTypes.Add(info);
                foreach (string key in new[] { info.Title, Key(info.Title), type.Name, info.RuntimeType.Name, info.RuntimeType.FullName })
                    _typesByKey[Key(key)] = type;
            }
            _nodeTypes.Sort((a, b) => string.CompareOrdinal(a.Category + "/" + a.Title, b.Category + "/" + b.Title));
            return _nodeTypes;
        }

        /// <summary>The node types as readable text, one block per node, for an agent or a log.</summary>
        public static string DescribeNodeTypes()
        {
            var text = new StringBuilder();
            foreach (PlacementNodeTypeInfo info in ListNodeTypes())
            {
                text.AppendLine($"{info.Title} ({info.Category})");
                text.AppendLine($"  point inputs: {(info.PointInputs.Length > 0 ? string.Join(", ", info.PointInputs) : "none")}");
                text.AppendLine($"  point outputs: {(info.PointOutputs.Length > 0 ? string.Join(", ", info.PointOutputs) : "none")}");
                foreach (PlacementSettingInfo setting in info.Settings)
                {
                    string choices = setting.Type.IsEnum ? $" [{string.Join(", ", Enum.GetNames(setting.Type))}]" : "";
                    string tooltip = string.IsNullOrEmpty(setting.Tooltip) ? "" : $" - {setting.Tooltip}";
                    text.AppendLine($"  setting {setting.Name}: {setting.Type.Name}{choices} = {Format(setting.DefaultValue)}{tooltip}");
                }
            }
            text.AppendLine("Parameter types: float, int, bool, Vector3, PrefabPalette, LayerMask.");
            return text.ToString();
        }

        private static string Format(object value) => value switch
        {
            null => "none",
            LayerMask mask => mask.value == ~0 ? "Everything" : mask.value.ToString(),
            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

        internal static string Key(string name) => (name ?? "").Replace(" ", "");

        internal static Type FindNodeType(string name)
        {
            ListNodeTypes();
            if (_typesByKey.TryGetValue(Key(name), out Type type)) return type;
            throw new ArgumentException($"There's no node type called '{name}'. Node types: {string.Join(", ", _nodeTypes.Select(t => t.Title))}.", nameof(name));
        }

        internal static Type FindNodeType(Type runtimeType)
        {
            ListNodeTypes();
            PlacementNodeTypeInfo info = _nodeTypes.FirstOrDefault(t => t.RuntimeType == runtimeType)
                ?? throw new ArgumentException($"{runtimeType.Name} has no graph node.", nameof(runtimeType));
            return _typesByKey[Key(info.Title)];
        }

        /// <summary>
        /// Converts <paramref name="value"/> to <paramref name="type"/> where the meaning is clear: numbers between
        /// numeric types, enum names or numbers to enums, an int to a LayerMask, three numbers to a Vector3, and an asset
        /// path to a Prefab Palette. Throws otherwise.
        /// </summary>
        internal static object Convert(object value, Type type, string what)
        {
            if (value == null)
            {
                if (!type.IsValueType) return null;
                throw new ArgumentException($"{what} is a {type.Name} and can't be null.");
            }
            if (type.IsInstanceOfType(value)) return value;
            try
            {
                if (type.IsEnum)
                {
                    if (value is string name) return Enum.Parse(type, name, true);
                    return Enum.ToObject(type, System.Convert.ToInt32(value));
                }
                if (type == typeof(float) || type == typeof(int) || type == typeof(bool))
                {
                    if (value is string || value is Vector3) throw new InvalidCastException();
                    return System.Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture);
                }
                if (type == typeof(LayerMask) && (value is int || value is long)) return (LayerMask)System.Convert.ToInt32(value);
                if (type == typeof(Vector3) && value is System.Collections.IList list && list.Count == 3)
                    return new Vector3(System.Convert.ToSingle(list[0]), System.Convert.ToSingle(list[1]), System.Convert.ToSingle(list[2]));
                if (typeof(UnityEngine.Object).IsAssignableFrom(type) && value is string path)
                {
                    UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(path, type);
                    if (asset != null) return asset;
                    throw new ArgumentException($"{what}: there's no {type.Name} at {path}.");
                }
            }
            catch (Exception e) when (e is FormatException || e is InvalidCastException || e is OverflowException)
            {
                // Falls through to the error below.
            }
            string choices = type.IsEnum ? $" ({string.Join(", ", Enum.GetNames(type))})" : "";
            throw new ArgumentException($"{what} is a {type.Name}{choices}; a {value.GetType().Name} ({value}) can't be used.");
        }
    }

    /// <summary>A node type that <see cref="PlacementGraphAuthoring"/> can add.</summary>
    public sealed class PlacementNodeTypeInfo
    {
        /// <summary>The node's title in the graph window, which <see cref="PlacementGraphHandle.AddNode(string, Vector2?)"/> takes.</summary>
        public string Title { get; }
        /// <summary>Its group in the graph window's add-node menu.</summary>
        public string Category { get; }
        /// <summary>The runtime node it compiles to.</summary>
        public Type RuntimeType { get; }
        /// <summary>Point input ports, in order.</summary>
        public string[] PointInputs { get; }
        /// <summary>Point output ports, in order.</summary>
        public string[] PointOutputs { get; }
        /// <summary>Setting ports, in order.</summary>
        public PlacementSettingInfo[] Settings { get; }

        public PlacementNodeTypeInfo(string title, string category, Type runtimeType, string[] pointInputs, string[] pointOutputs, PlacementSettingInfo[] settings)
        {
            Title = title;
            Category = category;
            RuntimeType = runtimeType;
            PointInputs = pointInputs;
            PointOutputs = pointOutputs;
            Settings = settings;
        }

        public override string ToString() => Title;
    }

    /// <summary>A node setting: a value input port that can also have a parameter wired in.</summary>
    public sealed class PlacementSettingInfo
    {
        public string Name { get; }
        public Type Type { get; }
        public object DefaultValue { get; }
        public string Tooltip { get; }

        public PlacementSettingInfo(string name, Type type, object defaultValue, string tooltip)
        {
            Name = name;
            Type = type;
            DefaultValue = defaultValue;
            Tooltip = tooltip;
        }

        public override string ToString() => $"{Name}: {Type.Name}";
    }

    /// <summary>A placement graph being edited. Changes are in memory until <see cref="Save"/>.</summary>
    public sealed class PlacementGraphHandle
    {
        private const float ColumnWidth = 260f;

        /// <summary>The project path of the .placegraph asset.</summary>
        public string Path { get; }

        /// <summary>The Graph Toolkit graph, for anything this API doesn't cover.</summary>
        public Graph Graph { get; }

        internal PlacementGraphHandle(Graph graph, string path)
        {
            Graph = graph;
            Path = path;
        }

        /// <summary>Every node in the graph, including variable and subgraph nodes, in the order they were added.</summary>
        public IReadOnlyList<PlacementNodeHandle> Nodes => Graph.GetNodes().Select(n => new PlacementNodeHandle(this, n)).ToList();

        /// <summary>The graph's parameters (Input variables), as (name, type).</summary>
        public IReadOnlyList<(string name, Type type)> Parameters =>
            Graph.GetVariables().Where(v => v.VariableKind == VariableKind.Input).Select(v => (v.Name, v.DataType)).ToList();

        private Vector2 NextPosition() => new Vector2(Graph.NodeCount * ColumnWidth, 0f);

        /// <summary>Adds a node by its title ("Surface Sampler"); the class name or runtime class name works too.</summary>
        public PlacementNodeHandle AddNode(string nodeType, Vector2? position = null) =>
            AddNode(PlacementGraphAuthoring.FindNodeType(nodeType), position);

        /// <summary>Adds the graph node for the runtime node type <typeparamref name="T"/>, e.g. <c>AddNode&lt;SurfaceSamplerNode&gt;()</c>.</summary>
        public PlacementNodeHandle AddNode<T>(Vector2? position = null) where T : PlacementNode =>
            AddNode(PlacementGraphAuthoring.FindNodeType(typeof(T)), position);

        private PlacementNodeHandle AddNode(Type editorType, Vector2? position)
        {
            var node = (PlacementGraphNode)Activator.CreateInstance(editorType);
            Vector2 at = position ?? NextPosition();
            Graph.AddNode(node);
            INode added = Graph.GetNodes().Last();
            SetPosition(added, at);
            return new PlacementNodeHandle(this, added);
        }

        private static void SetPosition(INode node, Vector2 position)
        {
            if (node is Node concrete) concrete.Position = position;
        }

        /// <summary>The first node with this title (or the variable's name, or the subgraph's name). Throws if none.</summary>
        public PlacementNodeHandle FindNode(string title)
        {
            PlacementNodeHandle found = Nodes.FirstOrDefault(n => PlacementGraphAuthoring.Key(n.Title) == PlacementGraphAuthoring.Key(title));
            return found ?? throw new ArgumentException($"There's no node called '{title}'. Nodes: {string.Join(", ", Nodes.Select(n => n.Title))}.", nameof(title));
        }

        /// <summary>Wires an output port of one node into an input port of another.</summary>
        public void Connect(PlacementNodeHandle from, string outputPort, PlacementNodeHandle to, string inputPort)
        {
            Check(from, nameof(from));
            Check(to, nameof(to));
            IPort output = from.FindPort(outputPort, PortDirection.Output);
            IPort input = to.FindPort(inputPort, PortDirection.Input);
            if (output.DataType != input.DataType)
                throw new InvalidOperationException($"Can't connect {from.Title}.{Label(output)} ({output.DataType.Name}) to {to.Title}.{Label(input)} ({input.DataType.Name}): the types differ.");
            if (!Graph.Connect(output, input))
                throw new InvalidOperationException($"Graph Toolkit refused to connect {from.Title}.{Label(output)} to {to.Title}.{Label(input)}.");
        }

        /// <summary>Removes the wire between two ports, if there is one.</summary>
        public void Disconnect(PlacementNodeHandle from, string outputPort, PlacementNodeHandle to, string inputPort)
        {
            Graph.Disconnect(from.FindPort(outputPort, PortDirection.Output), to.FindPort(inputPort, PortDirection.Input));
        }

        /// <summary>Removes a node and its wires.</summary>
        public void RemoveNode(PlacementNodeHandle node)
        {
            Check(node, nameof(node));
            Graph.RemoveNode(node.Node);
        }

        /// <summary>
        /// Adds a parameter: an Input variable of type float, int, bool, Vector3, PrefabPalette or LayerMask with a
        /// default. Wire it into settings with <see cref="BindParameter"/>. In a graph used as a subgraph, parameters
        /// are the subgraph node's value inputs.
        /// </summary>
        public void AddParameter<T>(string name, T defaultValue)
        {
            if (!PlacementParameter.TryGetType(typeof(T), out _))
                throw new ArgumentException($"A parameter can't be a {typeof(T).Name}. Use float, int, bool, Vector3, PrefabPalette or LayerMask.", nameof(defaultValue));
            CreateVariable(name, typeof(T), defaultValue, VariableKind.Input);
        }

        /// <summary>Wires the parameter <paramref name="parameter"/> into the setting <paramref name="setting"/> of <paramref name="node"/>.</summary>
        public PlacementNodeHandle BindParameter(string parameter, PlacementNodeHandle node, string setting)
        {
            Check(node, nameof(node));
            IVariable variable = Graph.GetVariables().FirstOrDefault(v => v.Name == parameter && v.VariableKind == VariableKind.Input)
                ?? throw new ArgumentException($"There's no parameter called '{parameter}'. Add it with AddParameter first.", nameof(parameter));
            IPort port = node.FindPort(setting, PortDirection.Input);
            if (port.DataType == typeof(PointData))
                throw new ArgumentException($"{node.Title}.{setting} is a point input; parameters go into settings.", nameof(setting));
            if (port.DataType != variable.DataType)
                throw new InvalidOperationException($"Parameter '{parameter}' is a {variable.DataType.Name} but {node.Title}.{setting} is a {port.DataType.Name}.");
            IVariableNode variableNode = Graph.AddVariableNode(variable, NodePosition(node) + new Vector2(-ColumnWidth * 0.6f, 120f));
            Graph.Connect(variableNode.GetOutputPort(0), port);
            return new PlacementNodeHandle(this, variableNode);
        }

        /// <summary>
        /// For a graph used as a subgraph: an Input variable of points, which becomes a point input on the subgraph
        /// node. Returns its variable node; connect from it with any port name ("Out" reads well).
        /// </summary>
        public PlacementNodeHandle AddPointInput(string name) => AddPointVariable(name, VariableKind.Input);

        /// <summary>
        /// For a graph used as a subgraph: an Output variable of points, which becomes a point output on the subgraph
        /// node. Returns its variable node; connect into it with any port name ("In" reads well).
        /// </summary>
        public PlacementNodeHandle AddPointOutput(string name) => AddPointVariable(name, VariableKind.Output);

        private PlacementNodeHandle AddPointVariable(string name, VariableKind kind)
        {
            IVariable variable = CreateVariable(name, typeof(PointData), null, kind);
            return new PlacementNodeHandle(this, Graph.AddVariableNode(variable, NextPosition()));
        }

        private IVariable CreateVariable(string name, Type type, object defaultValue, VariableKind kind)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A variable needs a name.", nameof(name));
            if (Graph.GetVariables().Any(v => v.Name == name))
                throw new ArgumentException($"There's already a variable called '{name}'.", nameof(name));
            return Graph.CreateVariable(name, type, defaultValue, kind);
        }

        /// <summary>Adds a subgraph node using the graph asset at <paramref name="subgraphPath"/>. Its ports are named after the subgraph's variables.</summary>
        public PlacementNodeHandle AddSubgraph(string subgraphPath, Vector2? position = null)
        {
            var subgraph = GraphDatabase.LoadGraph<PlacementGraphDefinition>(subgraphPath)
                ?? throw new FileNotFoundException($"There's no placement graph at {subgraphPath}.", subgraphPath);
            if (subgraphPath == Path) throw new InvalidOperationException("A graph can't contain itself.");
            ISubgraphNode node = Graph.AddSubgraphNode(subgraph, position ?? NextPosition());
            return new PlacementNodeHandle(this, node ?? throw new InvalidOperationException($"Graph Toolkit couldn't add {subgraphPath} as a subgraph."));
        }

        /// <summary>Adds a subgraph node using another graph being edited (save that one first so its ports are current).</summary>
        public PlacementNodeHandle AddSubgraph(PlacementGraphHandle subgraph, Vector2? position = null) => AddSubgraph(subgraph.Path, position);

        /// <summary>Problems the graph window would show, such as a Spawn Prefabs node without a palette.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            PlacementGraphCompiler.Validate(Graph, problems);
            return problems;
        }

        /// <summary>Compiles the graph in memory without saving, to check it. Errors and warnings are returned.</summary>
        public PlacementGraphAsset Compile(List<string> errors = null, List<string> warnings = null)
        {
            var asset = ScriptableObject.CreateInstance<PlacementGraphAsset>();
            asset.name = System.IO.Path.GetFileNameWithoutExtension(Path);
            PlacementGraphCompiler.TryCompile(Graph, asset, errors, warnings);
            return asset;
        }

        /// <summary>Writes the graph to its asset and reimports it. Returns the compiled graph a Placement Graph component uses.</summary>
        public PlacementGraphAsset Save()
        {
            GraphDatabase.SaveGraph(Graph);
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);
            return Compiled;
        }

        /// <summary>The compiled graph as last imported (call <see cref="Save"/> first for current changes).</summary>
        public PlacementGraphAsset Compiled => AssetDatabase.LoadAssetAtPath<PlacementGraphAsset>(Path);

        private Vector2 NodePosition(PlacementNodeHandle node) => node.Node.Position;

        private void Check(PlacementNodeHandle node, string name)
        {
            if (node == null) throw new ArgumentNullException(name);
            if (node.Graph != this && node.Graph.Graph != Graph) throw new ArgumentException($"{node.Title} belongs to another graph.", name);
        }

        internal static string Label(IPort port) => string.IsNullOrEmpty(port.DisplayName) ? port.Name : port.DisplayName;
    }

    /// <summary>A node in a graph being edited: a placement node, a variable node or a subgraph node.</summary>
    public sealed class PlacementNodeHandle
    {
        internal PlacementGraphHandle Graph { get; }

        /// <summary>The Graph Toolkit node, for anything this API doesn't cover.</summary>
        public INode Node { get; }

        internal PlacementNodeHandle(PlacementGraphHandle graph, INode node)
        {
            Graph = graph;
            Node = node;
        }

        /// <summary>The title shown in the graph window: the node type, the variable's name, or the subgraph's name.</summary>
        public string Title => Node switch
        {
            PlacementGraphNode placementNode => placementNode.DisplayName,
            IVariableNode variableNode => variableNode.Variable.Name,
            _ => string.IsNullOrEmpty(Node.Title) ? Node.GetType().Name : Node.Title
        };

        /// <summary>Input port names (point inputs and settings), in order.</summary>
        public IReadOnlyList<string> InputPorts => Node.GetInputPorts().Select(PlacementGraphHandle.Label).ToList();

        /// <summary>Output port names, in order.</summary>
        public IReadOnlyList<string> OutputPorts => Node.GetOutputPorts().Select(PlacementGraphHandle.Label).ToList();

        /// <summary>
        /// Sets a setting's inline value (a setting port that nothing is wired into). Numbers convert between numeric
        /// types, enums take their names, and a Prefab Palette can be given as an asset path. Returns this node.
        /// </summary>
        public PlacementNodeHandle Set(string setting, object value)
        {
            IPort port = FindPort(setting, PortDirection.Input);
            if (port.DataType == typeof(PointData)) throw new ArgumentException($"{Title}.{setting} is a point input, not a setting.", nameof(setting));
            if (port.IsConnected) throw new InvalidOperationException($"{Title}.{setting} has something wired into it, so its own value isn't used. Disconnect it first.");
            object converted = PlacementGraphAuthoring.Convert(value, port.DataType, $"{Title}.{setting}");
            if (Invoke(nameof(SetValue), port.DataType, port, converted)) return this;
            if (port.DataType.IsEnum && TrySetEnum(port, (Enum)converted)) return this;
            throw new InvalidOperationException($"Graph Toolkit refused the value for {Title}.{setting}.");
        }

        /// <summary>
        /// Graph Toolkit's TrySetValue doesn't take enums on ports: it stores them as its internal EnumValueReference.
        /// That type is used when it's there; otherwise the value goes in as a constant node wired into the port,
        /// which the compiler reads the same way.
        /// </summary>
        private bool TrySetEnum(IPort port, Enum value)
        {
            Type reference = typeof(IPort).Assembly.GetType("Unity.GraphToolkit.EnumValueReference");
            ConstructorInfo constructor = reference?.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(Enum) }, null);
            if (constructor != null)
            {
                object boxed = constructor.Invoke(new object[] { value });
                var setter = typeof(IPort).GetMethod(nameof(IPort.TrySetValue)).MakeGenericMethod(reference);
                if ((bool)setter.Invoke(port, new[] { boxed })) return true;
            }
            IConstantNode constant = Graph.Graph.CreateConstantNode(Node.Position + new Vector2(-150f, 60f), port.DataType, value);
            return constant != null && Graph.Graph.Connect(constant.GetOutputPort(0), port);
        }

        /// <summary>A setting's inline value.</summary>
        public object Get(string setting)
        {
            IPort port = FindPort(setting, PortDirection.Input);
            var arguments = new object[] { port, null };
            typeof(PlacementNodeHandle).GetMethod(nameof(GetValue), BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(port.DataType).Invoke(null, arguments);
            return arguments[1];
        }

        private static bool Invoke(string method, Type type, IPort port, object value) =>
            (bool)typeof(PlacementNodeHandle).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(type).Invoke(null, new[] { port, value });

        private static bool SetValue<T>(IPort port, object value) => port.TrySetValue((T)value);

        private static void GetValue<T>(IPort port, out object value)
        {
            value = port.TryGetValue(out T typed) ? typed : null;
        }

        /// <summary>
        /// A port by name, as shown on the node (matching ignores case and spaces). A variable node has one port, so
        /// any name finds it.
        /// </summary>
        internal IPort FindPort(string name, PortDirection direction)
        {
            List<IPort> ports = (direction == PortDirection.Input ? Node.GetInputPorts() : Node.GetOutputPorts()).ToList();
            if (Node is IVariableNode && ports.Count == 1) return ports[0];
            string key = PlacementGraphAuthoring.Key(name);
            IPort port = ports.FirstOrDefault(p => PlacementGraphHandle.Label(p) == name || p.Name == name)
                ?? ports.FirstOrDefault(p => string.Equals(PlacementGraphAuthoring.Key(PlacementGraphHandle.Label(p)), key, StringComparison.OrdinalIgnoreCase));
            if (port != null) return port;
            string kind = direction == PortDirection.Input ? "input" : "output";
            string available = ports.Count > 0 ? string.Join(", ", ports.Select(PlacementGraphHandle.Label)) : "none";
            throw new ArgumentException($"{Title} has no {kind} port called '{name}'. Its {kind} ports: {available}.", nameof(name));
        }

        public override string ToString() => Title;
    }
}
