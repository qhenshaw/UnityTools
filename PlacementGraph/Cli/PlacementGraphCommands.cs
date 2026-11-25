using Unity.Pipeline.Commands;
using UnityTools.Placement.Editor;

namespace UnityTools.Placement.Cli
{
    /// <summary>
    /// Edits, inspects and runs Placement Graphs from the command line, through the Pipeline package:
    /// <c>unity command placement inspect --graph Room</c>,
    /// <c>unity command placement.node set --graph Room --node Subdivide --set "Corner Trim=1"</c>,
    /// <c>unity command placement generate --graph Room</c>. Every command returns JSON, and <c>{"error": ...}</c>
    /// when it can't do what was asked. See <see cref="PlacementGraphJson"/> for how graphs, nodes and values are given.
    /// </summary>
    public static class PlacementGraphCommands
    {
        private const string Graph = "The graph: a .placegraph path, or its file name (Room)";
        private const string Node = "A node: its id from inspect (or the first characters), #index, its title if only one node has it, or Title<Upstream (Spawn Prefabs<Merge)";
        private const string Set = "Settings as \"Name=Value;Name=Value\" (numbers, true/false, enum names, x,y,z, asset paths or names, none). \\; keeps a ; in a value";
        private const string Save = "Save the graph and reimport it afterwards (components with Auto Generate regenerate). false keeps the change in memory for a later placement save";
        private const string Object = "A Placement Graph object: its hierarchy path or name";

        // ---- Assets

        [CliCommand("placement", "list", "Every .placegraph in the project with its node count, parameters and the Placement Graph objects in open scenes that use it.", Effect = CommandEffect.ReadOnly)]
        public static string List([CliArg("folder", "Only graphs under this folder (optional)")] string folder = "") =>
            PlacementGraphJson.List(folder);

        [CliCommand("placement", "create", "Create a graph from a template (Empty, ScatterOnSurface, FillSplineArea, ModularWall). Returns it as inspect does.", Effect = CommandEffect.Additive)]
        public static string Create(
            [CliArg("path", "Where to save it, e.g. Assets/Graphs/Forest.placegraph", Required = true)] string path,
            [CliArg("template", "Empty, ScatterOnSurface, FillSplineArea or ModularWall")] string template = "Empty",
            [CliArg("overwrite", "Replace a file already at the path")] bool overwrite = false) =>
            PlacementGraphJson.Create(path, template, overwrite);

        [CliCommand("placement", "duplicate", "Copy a graph to a new path.", Effect = CommandEffect.Additive)]
        public static string Duplicate(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("path", "The copy's path", Required = true)] string path) =>
            PlacementGraphJson.Duplicate(graph, path);

        [CliCommand("placement", "types", "Every node type with its point ports, links and settings (types, defaults, enum choices, tooltips).", Effect = CommandEffect.ReadOnly)]
        public static string Types([CliArg("name", "Only types whose title contains this (optional)")] string name = "") =>
            PlacementGraphJson.Types(name);

        // ---- Inspect, save

        [CliCommand("placement", "inspect", "A graph's nodes (ids, types, settings, wires, entries and modules), wires, parameters, problems and compile errors and warnings. With --node, that node in full.", Effect = CommandEffect.ReadOnly)]
        public static string Inspect(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("node", Node + " (optional)")] string node = "") =>
            PlacementGraphJson.Inspect(graph, node);

        [CliCommand("placement", "save", "Save a graph and reimport it, after edits made with --save false. An open graph window shows the same graph.", Effect = CommandEffect.Idempotent)]
        public static string SaveGraph([CliArg("graph", Graph, Required = true)] string graph) =>
            PlacementGraphJson.Save(graph);

        // ---- Nodes

        [CliCommand("placement.node", "get", "One node in full: every setting with its type, choices and value, and every port with its wires.", Effect = CommandEffect.ReadOnly)]
        public static string NodeGet(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("node", Node, Required = true)] string node) =>
            PlacementGraphJson.Inspect(graph, node);

        [CliCommand("placement.node", "add", "Add a node by its title (see placement types), optionally at a position and with settings. Returns the node and its id.", Effect = CommandEffect.Additive)]
        public static string NodeAdd(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("type", "The node type's title, e.g. \"Density Filter\"", Required = true)] string type,
            [CliArg("set", Set)] string set = "",
            [CliArg("position", "x,y in the graph window (optional)")] string position = "",
            [CliArg("save", Save)] bool save = true) =>
            PlacementGraphJson.NodeAdd(graph, type, set, position, save);

        [CliCommand("placement.node", "remove", "Remove a node and its wires, with a spawner's entries, Subdivide's modules or a Prefab Entry's attributes. A removed Prefab Entry or Module's chain is joined up again.", Effect = CommandEffect.Destructive)]
        public static string NodeRemove(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("node", Node, Required = true)] string node,
            [CliArg("keep-chain", "Keep the entries, modules or attributes chained into it")] bool keepChain = false,
            [CliArg("save", Save)] bool save = true) =>
            PlacementGraphJson.NodeRemove(graph, node, keepChain, save);

        [CliCommand("placement.node", "set", "Set a node's settings: --set \"Corner Trim=1;Closed=true\", or --option and --value for one. Returns the node.", Effect = CommandEffect.Idempotent)]
        public static string NodeSet(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("node", Node, Required = true)] string node,
            [CliArg("set", Set)] string set = "",
            [CliArg("option", "One setting's name, with --value")] string option = "",
            [CliArg("value", "The value for --option, as text")] string value = "",
            [CliArg("save", Save)] bool save = true) =>
            PlacementGraphJson.NodeSet(graph, node, set, option, value, save);

        // ---- Wires

        [CliCommand("placement.wire", "connect", "Wire an output port of one node into an input port of another (ports as shown on the nodes; default Out and In). A setting or link input's old wire is replaced.", Effect = CommandEffect.Idempotent)]
        public static string Connect(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("from", "The node the wire comes from. " + Node, Required = true)] string from,
            [CliArg("to", "The node the wire goes into. " + Node, Required = true)] string to,
            [CliArg("from-port", "Output port (default Out)")] string fromPort = "Out",
            [CliArg("to-port", "Input port (default In)")] string toPort = "In",
            [CliArg("save", Save)] bool save = true) =>
            PlacementGraphJson.Connect(graph, from, to, fromPort, toPort, save);

        [CliCommand("placement.wire", "disconnect", "Remove the wires into a node's input port: all of them, or only those from --from (and --from-port).", Effect = CommandEffect.Destructive)]
        public static string Disconnect(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("to", "The node the wire goes into. " + Node, Required = true)] string to,
            [CliArg("to-port", "Input port (default In)")] string toPort = "In",
            [CliArg("from", "Only wires from this node (optional)")] string from = "",
            [CliArg("from-port", "Only wires from this output port (optional)")] string fromPort = "",
            [CliArg("save", Save)] bool save = true) =>
            PlacementGraphJson.Disconnect(graph, to, toPort, from, fromPort, save);

        // ---- Entries and modules

        [CliCommand("placement.entries", "list", "The Prefab Entries chained into a spawner (Spawn Prefabs, Spawn Instanced, Select Prefab), or the Modules chained into Subdivide, with their settings and attributes.", Effect = CommandEffect.ReadOnly)]
        public static string EntriesList(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("node", "The spawner or Subdivide. " + Node, Required = true)] string node) =>
            PlacementGraphJson.EntriesList(graph, node);

        [CliCommand("placement.entries", "set", "Replace a spawner's entries (or Subdivide's modules) with one per prefab, each with --set. Or --file, a JSON list of {\"prefab\", \"set\"}. A Module's Symbol defaults to its prefab's name.", Effect = CommandEffect.Idempotent)]
        public static string EntriesSet(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("node", "The spawner or Subdivide. " + Node, Required = true)] string node,
            [CliArg("prefabs", "Prefabs as \"a.prefab;b.prefab\" (paths, or names when unique)")] string prefabs = "",
            [CliArg("set", "Settings for every new entry, e.g. \"Weight=2;Random Scale=false\"")] string set = "",
            [CliArg("file", "A JSON file: [{\"prefab\": \"...\", \"set\": \"Weight=2\"}, ...] (optional)")] string file = "",
            [CliArg("save", Save)] bool save = true) =>
            PlacementGraphJson.EntriesSet(graph, node, prefabs, set, file, save);

        [CliCommand("placement.entries", "add", "Add entries (or modules) for prefabs, at --index or the end, each with --set.", Effect = CommandEffect.Additive)]
        public static string EntriesAdd(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("node", "The spawner or Subdivide. " + Node, Required = true)] string node,
            [CliArg("prefabs", "Prefabs as \"a.prefab;b.prefab\" (paths, or names when unique)")] string prefabs = "",
            [CliArg("set", "Settings for every new entry")] string set = "",
            [CliArg("index", "Insert before this entry (0 is the first); -1 adds at the end")] int index = -1,
            [CliArg("file", "A JSON file: [{\"prefab\": \"...\", \"set\": \"...\"}, ...] (optional)")] string file = "",
            [CliArg("save", Save)] bool save = true) =>
            PlacementGraphJson.EntriesAdd(graph, node, prefabs, set, index, file, save);

        [CliCommand("placement.entries", "remove", "Remove the entry (or module) at --index, or every one using --prefab. The chain is joined up again.", Effect = CommandEffect.Destructive)]
        public static string EntriesRemove(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("node", "The spawner or Subdivide. " + Node, Required = true)] string node,
            [CliArg("index", "The entry's index (0 is the first)")] int index = -1,
            [CliArg("prefab", "Remove every entry using this prefab (path or name)")] string prefab = "",
            [CliArg("save", Save)] bool save = true) =>
            PlacementGraphJson.EntriesRemove(graph, node, index, prefab, save);

        [CliCommand("placement.entries", "edit", "Set settings on one entry (or module) by index: --set \"Weight=2;Random Scale=true;Scale Max=1.5\".", Effect = CommandEffect.Idempotent)]
        public static string EntriesEdit(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("node", "The spawner or Subdivide. " + Node, Required = true)] string node,
            [CliArg("index", "The entry's index (0 is the first)", Required = true)] int index,
            [CliArg("set", Set, Required = true)] string set,
            [CliArg("save", Save)] bool save = true) =>
            PlacementGraphJson.EntriesEdit(graph, node, index, set, save);

        [CliCommand("placement.entries", "attribute", "Add an Entry Attribute to a Prefab Entry: the points it's picked for get name = value.", Effect = CommandEffect.Additive)]
        public static string EntriesAttribute(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("node", "The spawner. " + Node, Required = true)] string node,
            [CliArg("index", "The entry's index (0 is the first)", Required = true)] int index,
            [CliArg("name", "The attribute's name", Required = true)] string name,
            [CliArg("value", "A number, x,y,z or an asset path", Required = true)] string value,
            [CliArg("type", "Float, Int, Vector3 or Object; from the value when empty")] string type = "",
            [CliArg("save", Save)] bool save = true) =>
            PlacementGraphJson.EntriesAttribute(graph, node, index, name, value, type, save);

        // ---- Parameters

        [CliCommand("placement.params", "list", "A graph's parameters (types, defaults, the settings they drive); with --object, that component's overrides too.", Effect = CommandEffect.ReadOnly)]
        public static string ParamsList(
            [CliArg("graph", Graph + " (optional with --object)")] string graph = "",
            [CliArg("object", Object + " (optional)")] string obj = "") =>
            PlacementGraphJson.ParamsList(graph, obj);

        [CliCommand("placement.params", "add", "Add a parameter of type float, int, bool, Vector3, GameObject or LayerMask with a default. Wire it in with placement.params bind.", Effect = CommandEffect.Additive)]
        public static string ParamsAdd(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("name", "The parameter's name", Required = true)] string name,
            [CliArg("type", "float, int, bool, Vector3, GameObject or LayerMask", Required = true)] string type,
            [CliArg("value", "Its default, as text (optional)")] string value = "",
            [CliArg("save", Save)] bool save = true) =>
            PlacementGraphJson.ParamsAdd(graph, name, type, value, save);

        [CliCommand("placement.params", "bind", "Wire a parameter into a node's setting, so the setting takes the parameter's value.", Effect = CommandEffect.Idempotent)]
        public static string ParamsBind(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("name", "The parameter", Required = true)] string name,
            [CliArg("node", Node, Required = true)] string node,
            [CliArg("option", "The setting it drives", Required = true)] string option,
            [CliArg("save", Save)] bool save = true) =>
            PlacementGraphJson.ParamsBind(graph, name, node, option, save);

        [CliCommand("placement.params", "set", "Set a parameter: the graph's default, or with --object that component's override (Undo).", Effect = CommandEffect.Idempotent)]
        public static string ParamsSet(
            [CliArg("name", "The parameter", Required = true)] string name,
            [CliArg("value", "The value, as text", Required = true)] string value,
            [CliArg("graph", Graph + " (for the default)")] string graph = "",
            [CliArg("object", Object + " (for an override)")] string obj = "",
            [CliArg("save", Save + " (defaults only)")] bool save = true) =>
            PlacementGraphJson.ParamsSet(name, value, graph, obj, save);

        [CliCommand("placement.params", "clear", "Stop a component overriding a parameter, so it uses the graph's default (Undo).", Effect = CommandEffect.Idempotent)]
        public static string ParamsClear(
            [CliArg("object", Object, Required = true)] string obj,
            [CliArg("name", "The parameter", Required = true)] string name) =>
            PlacementGraphJson.ParamsClear(obj, name);

        [CliCommand("placement.params", "remove", "Remove a parameter and its nodes from a graph.", Effect = CommandEffect.Destructive)]
        public static string ParamsRemove(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("name", "The parameter", Required = true)] string name,
            [CliArg("save", Save)] bool save = true) =>
            PlacementGraphJson.ParamsRemove(graph, name, save);

        // ---- Components

        [CliCommand("placement.component", "list", "The Placement Graph objects in open scenes (or those using --graph): graph, seed, size, cells, Generate On, instances, overrides.", Effect = CommandEffect.ReadOnly)]
        public static string ComponentsList([CliArg("graph", "Only objects using this graph (optional)")] string graph = "") =>
            PlacementGraphJson.ComponentsList(graph);

        [CliCommand("placement.component", "create", "Add a Placement Graph object running a graph, in the active scene or under --parent, and generate it. One Undo step.", Effect = CommandEffect.Additive)]
        public static string ComponentCreate(
            [CliArg("graph", Graph, Required = true)] string graph,
            [CliArg("name", "The object's name (default: the graph's)")] string name = "",
            [CliArg("position", "World position x,y,z")] string position = "",
            [CliArg("size", "Volume size x,y,z")] string size = "",
            [CliArg("parent", "Parent object (hierarchy path or name)")] string parent = "",
            [CliArg("generate", "Generate it straight away")] bool generate = true) =>
            PlacementGraphJson.ComponentCreate(graph, name, position, size, parent, generate);

        [CliCommand("placement.component", "set", "Set a Placement Graph's fields by inspector name: \"Seed=3;Size=20,4,20;Cell Size=0;Generate On=NearPlayer;Auto Generate=false;Graph=Room\". Undo.", Effect = CommandEffect.Idempotent)]
        public static string ComponentSet(
            [CliArg("object", Object, Required = true)] string obj,
            [CliArg("set", "Fields as \"Name=Value;Name=Value\"", Required = true)] string set) =>
            PlacementGraphJson.ComponentSet(obj, set);

        // ---- Run

        [CliCommand("placement", "generate", "Regenerate an object, or every object using --graph (or the only one open), as one Undo step each. Returns what was spawned by prefab, timings and warnings.", Effect = CommandEffect.Idempotent)]
        public static string Generate(
            [CliArg("object", Object + " (optional)")] string obj = "",
            [CliArg("graph", "Every object using this graph (optional)")] string graph = "") =>
            PlacementGraphJson.Generate(obj, graph);

        [CliCommand("placement", "clear", "Destroy what an object (or every object using --graph) generated, as one Undo step each.", Effect = CommandEffect.Destructive)]
        public static string Clear(
            [CliArg("object", Object + " (optional)")] string obj = "",
            [CliArg("graph", "Every object using this graph (optional)")] string graph = "") =>
            PlacementGraphJson.Clear(obj, graph);

        [CliCommand("placement", "evaluate", "Dry run of the graph as it is now, saved or not: per-node point counts and times, spawns by prefab and warnings, without touching the scene. --object runs in that object's volume with its seed and inputs.", Effect = CommandEffect.ReadOnly)]
        public static string Evaluate(
            [CliArg("graph", Graph + " (optional with --object)")] string graph = "",
            [CliArg("object", Object + " (optional)")] string obj = "",
            [CliArg("size", "Without --object: the volume size x,y,z at the origin")] string size = "20,10,20",
            [CliArg("seed", "Without --object: the seed")] int seed = 0) =>
            PlacementGraphJson.Evaluate(graph, obj, size, seed);

        [CliCommand("placement", "stats", "What an object (or every object using --graph) has generated, by prefab, its cells, and the last run's per-node points, times and warnings.", Effect = CommandEffect.ReadOnly)]
        public static string Stats(
            [CliArg("object", Object + " (optional)")] string obj = "",
            [CliArg("graph", "Every object using this graph (optional)")] string graph = "") =>
            PlacementGraphJson.Stats(obj, graph);
    }
}
