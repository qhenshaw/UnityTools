using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityTools.Placement.Editor
{
    /// <summary>
    /// Edits, inspects and runs Placement Graphs by name, with every answer as JSON. Used by the
    /// <c>unity command placement*</c> CLI commands (with the Pipeline package), and usable from tests and editor
    /// scripts: <c>PlacementGraphJson.NodeSet("Room", "Subdivide", "Corner Trim=1")</c>.
    /// <para>
    /// Graphs are given by path (Assets/.../Room.placegraph) or by file name (Room). Nodes are given by id (the first
    /// characters of the id <see cref="Inspect"/> lists), by index (#3), by title when only one node has it
    /// (Subdivide), or by title and the title of a node wired into it (Spawn Prefabs&lt;Merge). Values are text:
    /// numbers, true/false, enum names, x,y,z vectors, asset paths or names, and none. Several values go in one
    /// <c>set</c> string: <c>Corner Trim=1;Closed=true</c>.
    /// </para>
    /// Edits go through Graph Toolkit's own graph, so a graph open in the graph window shows them, and are saved (and
    /// the graph reimported, which regenerates components with Auto Generate) unless <c>save</c> is false. Scene
    /// changes record Undo. Mistakes come back as <c>{"error": ...}</c> with what was expected.
    /// </summary>
    public static class PlacementGraphJson
    {
        private const float ColumnWidth = 260f;

        // ---------------------------------------------------------------- Assets

        /// <summary>Every .placegraph in the project (or under <paramref name="folder"/>), with its compiled node and parameter counts and the components in open scenes that use it.</summary>
        public static string List(string folder = "") => Run(() =>
        {
            List<PlacementGraph> components = Components();
            var graphs = new List<object>();
            foreach (string path in GraphPaths())
            {
                if (!string.IsNullOrEmpty(folder) && !path.StartsWith(folder.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)) continue;
                var compiled = AssetDatabase.LoadAssetAtPath<PlacementGraphAsset>(path);
                graphs.Add(new J
                {
                    { "path", path },
                    { "nodes", compiled != null ? compiled.Nodes.Count : 0 },
                    { "parameters", compiled != null ? compiled.Parameters.Select(p => p.Name).ToList() : new List<string>() },
                    { "usedBy", components.Where(c => c.Graph != null && c.Graph == compiled).Select(HierarchyPath).ToList() }
                });
            }
            return new J { { "graphs", graphs } };
        });

        /// <summary>Creates a graph at <paramref name="path"/> from a template (Empty, ScatterOnSurface, FillSplineArea, ModularWall) and returns it as <see cref="Inspect"/> does.</summary>
        public static string Create(string path, string template = "Empty", bool overwrite = false) => Run(() =>
        {
            path = NormalizePath(path);
            if (!path.EndsWith("." + PlacementGraphAuthoring.Extension, StringComparison.OrdinalIgnoreCase)) path += "." + PlacementGraphAuthoring.Extension;
            if (!Enum.TryParse(PlacementGraphAuthoring.Key(template ?? "Empty"), true, out PlacementGraphTemplate kind))
                throw new ArgumentException($"There's no template called '{template}'. Templates: {string.Join(", ", Enum.GetNames(typeof(PlacementGraphTemplate)))}.");
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                if (!overwrite) throw new InvalidOperationException($"There's already a file at {path}. Pass overwrite true to replace it.");
                AssetDatabase.DeleteAsset(path);
            }
            PlacementGraphTemplates.Create(kind, path);
            return Describe(new Ctx(PlacementGraphAuthoring.Open(path)), false);
        });

        /// <summary>Copies the graph <paramref name="graph"/> to <paramref name="path"/>.</summary>
        public static string Duplicate(string graph, string path) => Run(() =>
        {
            string source = GraphPath(graph);
            path = NormalizePath(path);
            if (!path.EndsWith("." + PlacementGraphAuthoring.Extension, StringComparison.OrdinalIgnoreCase)) path += "." + PlacementGraphAuthoring.Extension;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) throw new InvalidOperationException($"There's already a file at {path}.");
            PlacementGraphTemplates.Duplicate(source, path);
            return new J { { "path", path }, { "from", source } };
        });

        /// <summary>Every node type (or the ones whose title contains <paramref name="name"/>) with its ports and settings, their types, defaults and enum choices.</summary>
        public static string Types(string name = "") => Run(() =>
        {
            IEnumerable<PlacementNodeTypeInfo> types = PlacementGraphAuthoring.ListNodeTypes();
            if (!string.IsNullOrEmpty(name))
            {
                string key = PlacementGraphAuthoring.Key(name);
                types = types.Where(t => PlacementGraphAuthoring.Key(t.Title).IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (!types.Any())
                    throw new ArgumentException($"No node type matches '{name}'.{Suggest(name, PlacementGraphAuthoring.ListNodeTypes().Select(t => t.Title))}");
            }
            return new J
            {
                {
                    "types", types.Select(t => new J
                    {
                        { "title", t.Title }, { "category", t.Category }, { "runs", t.Runs },
                        { "pointInputs", t.PointInputs }, { "pointOutputs", t.PointOutputs }, { "links", t.Links },
                        { "settings", t.Settings.Select(s => SettingType(s.Name, s.Type, s.DefaultValue, s.Tooltip)).ToList() }
                    }).ToList()
                }
            };
        });

        // ---------------------------------------------------------------- Inspect and save

        /// <summary>
        /// The graph's nodes (id, index, type, settings, wires in and out, and the entries or modules chained into
        /// spawners and Subdivide), its wires, parameters, the problems the graph window shows and the compile errors
        /// and warnings. With <paramref name="node"/>, only that node, in full (every setting's type and choices).
        /// </summary>
        public static string Inspect(string graph, string node = "") => Run(() =>
        {
            var ctx = Open(graph);
            if (!string.IsNullOrEmpty(node)) return NodeJson(ctx, ctx.Resolve(node), true);
            return Describe(ctx, true);
        });

        /// <summary>Writes the graph to its asset and reimports it (components with Auto Generate regenerate).</summary>
        public static string Save(string graph) => Run(() =>
        {
            var ctx = Open(graph);
            return Saved(ctx, new J { { "path", ctx.Path } }, true, force: true);
        });

        // ---------------------------------------------------------------- Nodes

        /// <summary>Adds a node of type <paramref name="type"/> (its title, e.g. "Density Filter"), optionally at <paramref name="position"/> (x,y) and with settings.</summary>
        public static string NodeAdd(string graph, string type, string set = "", string position = "", bool save = true) => Run(() =>
        {
            var ctx = Open(graph);
            Vector2? at = string.IsNullOrEmpty(position) ? null : (Vector2?)ParseVector2(position, "position");
            PlacementNodeHandle added;
            using (ctx.Edit("Add Node")) added = ctx.Graph.AddNode(type, at);
            ApplySettings(ctx, added, set);
            ctx.Refresh();
            return Saved(ctx, (J)NodeJson(ctx, added.Node, false), save);
        });

        /// <summary>Removes a node and its wires. A spawner's or Subdivide's entry or module chain, and a Prefab Entry's attributes, go with it unless <paramref name="keepChain"/>.</summary>
        public static string NodeRemove(string graph, string node, bool keepChain = false, bool save = true) => Run(() =>
        {
            var ctx = Open(graph);
            INode target = ctx.Resolve(node);
            var removed = new List<string> { ctx.Id(target) };
            // A Prefab Entry or Module in the middle of a chain: the ones either side are joined up again.
            INode owner = ChainOwner(target);
            Chain chain = owner != null ? Chain.Of(owner) : null;
            List<INode> rest = chain?.Nodes(owner);
            using (ctx.Edit("Remove Node"))
            {
                if (!keepChain)
                {
                    foreach (INode linked in ChainedValueNodes(target))
                    {
                        removed.Add(ctx.Id(linked));
                        ctx.Graph.Graph.RemoveNode(linked);
                    }
                }
                ctx.Graph.Graph.RemoveNode(target);
                if (rest != null && rest.Remove(target)) chain.Rewire(ctx.Graph.Graph, owner, rest);
            }
            ctx.Refresh();
            return Saved(ctx, new J { { "removed", removed } }, save);
        });

        /// <summary>
        /// Sets settings on a node: <paramref name="set"/> as "Name=Value;Name=Value", or one <paramref name="option"/>
        /// and <paramref name="value"/> (for values that contain ; or =). Returns the node.
        /// </summary>
        public static string NodeSet(string graph, string node, string set = "", string option = "", string value = "", bool save = true) => Run(() =>
        {
            var ctx = Open(graph);
            PlacementNodeHandle target = ctx.Handle(ctx.Resolve(node));
            if (string.IsNullOrEmpty(set) && string.IsNullOrEmpty(option))
                throw new ArgumentException("Pass set \"Name=Value;...\" or option and value.");
            ApplySettings(ctx, target, set);
            if (!string.IsNullOrEmpty(option)) ApplySetting(ctx, target, option, value);
            return Saved(ctx, (J)NodeJson(ctx, target.Node, false), save);
        });

        // ---------------------------------------------------------------- Wires

        /// <summary>Wires <paramref name="fromPort"/> of one node into <paramref name="toPort"/> of another. A point input takes several wires; a setting or link input takes one, so its old wire is replaced.</summary>
        public static string Connect(string graph, string from, string to, string fromPort = "Out", string toPort = "In", bool save = true) => Run(() =>
        {
            var ctx = Open(graph);
            PlacementNodeHandle source = ctx.Handle(ctx.Resolve(from));
            PlacementNodeHandle target = ctx.Handle(ctx.Resolve(to));
            IPort input = target.FindPort(toPort, PortDirection.Input);
            using (ctx.Edit("Connect"))
            {
                if (input.IsConnected && !PlacementGraphNode.IsPointPort(input))
                    foreach (IPort old in Connected(input)) ctx.Graph.Graph.Disconnect(old, input);
                ctx.Graph.Connect(source, fromPort, target, toPort);
            }
            return Saved(ctx, new J { { "wire", Wire(ctx, source.FindPort(fromPort, PortDirection.Output), input) }, { "wires", Wires(ctx) } }, save);
        });

        /// <summary>Removes the wire from <paramref name="from"/> into <paramref name="to"/>.<paramref name="toPort"/>; without <paramref name="from"/>, every wire into that port.</summary>
        public static string Disconnect(string graph, string to, string toPort = "In", string from = "", string fromPort = "", bool save = true) => Run(() =>
        {
            var ctx = Open(graph);
            PlacementNodeHandle target = ctx.Handle(ctx.Resolve(to));
            IPort input = target.FindPort(toPort, PortDirection.Input);
            INode source = string.IsNullOrEmpty(from) ? null : ctx.Resolve(from);
            var removed = new List<object>();
            using (ctx.Edit("Disconnect"))
            {
                foreach (IPort output in Connected(input))
                {
                    if (source != null && output.GetNode() != source) continue;
                    if (!string.IsNullOrEmpty(fromPort) && !SamePort(output, fromPort)) continue;
                    removed.Add(Wire(ctx, output, input));
                    ctx.Graph.Graph.Disconnect(output, input);
                }
            }
            if (removed.Count == 0)
                throw new InvalidOperationException($"Nothing{(source != null ? $" from {ctx.Label(source)}" : "")} is wired into {ctx.Label(target.Node)}.{toPort}.");
            return Saved(ctx, new J { { "removed", removed } }, save);
        });

        // ---------------------------------------------------------------- Entries and modules

        /// <summary>The Prefab Entry nodes chained into a spawner (or the Module nodes chained into Subdivide), first to last, with their settings and attributes.</summary>
        public static string EntriesList(string graph, string node) => Run(() =>
        {
            var ctx = Open(graph);
            INode target = ctx.Resolve(node);
            return new J { { "node", ctx.Id(target) }, { Chain.Of(target).Key, ChainJson(ctx, target) } };
        });

        /// <summary>
        /// Replaces the entries (or modules) of <paramref name="node"/> with one per prefab in <paramref name="prefabs"/>
        /// ("a.prefab;b.prefab", paths or names), each given the settings in <paramref name="set"/>. Or
        /// <paramref name="file"/>: a JSON file <c>[{"prefab": "...", "set": "Weight=2;Symbol=Wall"}, ...]</c> for
        /// different settings per entry. A Module's Symbol defaults to its prefab's name.
        /// </summary>
        public static string EntriesSet(string graph, string node, string prefabs = "", string set = "", string file = "", bool save = true) => Run(() =>
        {
            var ctx = Open(graph);
            INode target = ctx.Resolve(node);
            Chain chain = Chain.Of(target);
            List<(GameObject prefab, string set)> specs = Specs(prefabs, set, file);
            using (ctx.Edit("Set Entries"))
            {
                foreach (INode old in chain.Nodes(target))
                {
                    foreach (INode attribute in ChainedValueNodes(old)) ctx.Graph.Graph.RemoveNode(attribute);
                    ctx.Graph.Graph.RemoveNode(old);
                }
                var nodes = specs.Select((s, i) => AddChainNode(ctx, target, chain, s.prefab, s.set, i)).ToList();
                chain.Rewire(ctx.Graph.Graph, target, nodes);
            }
            ctx.Refresh();
            return Saved(ctx, new J { { "node", ctx.Id(target) }, { chain.Key, ChainJson(ctx, target) } }, save);
        });

        /// <summary>Adds entries (or modules) for <paramref name="prefabs"/> at <paramref name="index"/> (-1: the end), each with the settings in <paramref name="set"/>.</summary>
        public static string EntriesAdd(string graph, string node, string prefabs = "", string set = "", int index = -1, string file = "", bool save = true) => Run(() =>
        {
            var ctx = Open(graph);
            INode target = ctx.Resolve(node);
            Chain chain = Chain.Of(target);
            List<INode> nodes = chain.Nodes(target);
            if (index < 0 || index > nodes.Count) index = nodes.Count;
            List<(GameObject prefab, string set)> specs = Specs(prefabs, set, file);
            using (ctx.Edit("Add Entries"))
            {
                for (int i = 0; i < specs.Count; i++) nodes.Insert(index + i, AddChainNode(ctx, target, chain, specs[i].prefab, specs[i].set, nodes.Count));
                chain.Rewire(ctx.Graph.Graph, target, nodes);
            }
            ctx.Refresh();
            return Saved(ctx, new J { { "node", ctx.Id(target) }, { chain.Key, ChainJson(ctx, target) } }, save);
        });

        /// <summary>Removes the entry (or module) at <paramref name="index"/>, or every one whose prefab is <paramref name="prefab"/> (path or name).</summary>
        public static string EntriesRemove(string graph, string node, int index = -1, string prefab = "", bool save = true) => Run(() =>
        {
            var ctx = Open(graph);
            INode target = ctx.Resolve(node);
            Chain chain = Chain.Of(target);
            List<INode> nodes = chain.Nodes(target);
            var remove = new List<INode>();
            if (index >= 0)
            {
                if (index >= nodes.Count) throw new ArgumentException($"{ctx.Label(target)} has {nodes.Count} {chain.Key}; index {index} is past the end.");
                remove.Add(nodes[index]);
            }
            else if (!string.IsNullOrEmpty(prefab))
            {
                remove.AddRange(nodes.Where(n => PrefabMatches(ctx.Handle(n).Get("Prefab") as GameObject, prefab)));
                if (remove.Count == 0)
                    throw new ArgumentException($"No {chain.Single} of {ctx.Label(target)} uses '{prefab}'. Its prefabs: {string.Join(", ", nodes.Select(n => Name(ctx.Handle(n).Get("Prefab") as Object)))}.");
            }
            else throw new ArgumentException("Pass index (0 is the first) or prefab.");
            using (ctx.Edit("Remove Entries"))
            {
                foreach (INode n in remove)
                {
                    nodes.Remove(n);
                    foreach (INode attribute in ChainedValueNodes(n)) ctx.Graph.Graph.RemoveNode(attribute);
                    ctx.Graph.Graph.RemoveNode(n);
                }
                chain.Rewire(ctx.Graph.Graph, target, nodes);
            }
            ctx.Refresh();
            return Saved(ctx, new J { { "node", ctx.Id(target) }, { chain.Key, ChainJson(ctx, target) } }, save);
        });

        /// <summary>Sets settings on the entry (or module) at <paramref name="index"/>: "Weight=2;Random Scale=true".</summary>
        public static string EntriesEdit(string graph, string node, int index, string set, bool save = true) => Run(() =>
        {
            var ctx = Open(graph);
            INode target = ctx.Resolve(node);
            Chain chain = Chain.Of(target);
            List<INode> nodes = chain.Nodes(target);
            if (index < 0 || index >= nodes.Count) throw new ArgumentException($"{ctx.Label(target)} has {nodes.Count} {chain.Key}; pass index 0 to {nodes.Count - 1}.");
            ApplySettings(ctx, ctx.Handle(nodes[index]), set);
            return Saved(ctx, new J { { "node", ctx.Id(target) }, { chain.Key, ChainJson(ctx, target) } }, save);
        });

        /// <summary>Adds an Entry Attribute to the Prefab Entry at <paramref name="index"/>: points it's picked for get <paramref name="name"/> = <paramref name="value"/> (a number, x,y,z or an asset). <paramref name="type"/>: Float, Int, Vector3 or Object, from the value when empty.</summary>
        public static string EntriesAttribute(string graph, string node, int index, string name, string value, string type = "", bool save = true) => Run(() =>
        {
            var ctx = Open(graph);
            INode target = ctx.Resolve(node);
            Chain chain = Chain.Of(target);
            if (chain.Key != "entries") throw new ArgumentException($"{ctx.Label(target)} takes modules; attributes go on Prefab Entry nodes.");
            List<INode> nodes = chain.Nodes(target);
            if (index < 0 || index >= nodes.Count) throw new ArgumentException($"{ctx.Label(target)} has {nodes.Count} entries; pass index 0 to {nodes.Count - 1}.");
            PointAttributeType? kind = null;
            if (!string.IsNullOrEmpty(type))
            {
                if (!Enum.TryParse(type, true, out PointAttributeType parsed))
                    throw new ArgumentException($"Attribute type is one of {string.Join(", ", Enum.GetNames(typeof(PointAttributeType)))}.");
                kind = parsed;
            }
            object parsedValue = ParseAttributeValue(value, kind);
            using (ctx.Edit("Add Entry Attribute")) ctx.Graph.AddEntryAttribute(ctx.Handle(nodes[index]), name, parsedValue, kind);
            ctx.Refresh();
            return Saved(ctx, new J { { "node", ctx.Id(target) }, { chain.Key, ChainJson(ctx, target) } }, save);
        });

        // ---------------------------------------------------------------- Parameters

        /// <summary>The graph's parameters with their types, defaults and the settings they drive; with <paramref name="obj"/>, also that component's overrides.</summary>
        public static string ParamsList(string graph = "", string obj = "") => Run(() =>
        {
            PlacementGraph component = string.IsNullOrEmpty(obj) ? null : FindComponent(obj);
            if (string.IsNullOrEmpty(graph))
            {
                if (component == null) throw new ArgumentException("Pass graph, object or both.");
                graph = GraphOf(component);
            }
            var ctx = Open(graph);
            return ParamsJson(ctx, component);
        });

        /// <summary>Adds a parameter: <paramref name="type"/> is float, int, bool, Vector3, GameObject or LayerMask. Wire it into settings with <see cref="ParamsBind"/>.</summary>
        public static string ParamsAdd(string graph, string name, string type, string value = "", bool save = true) => Run(() =>
        {
            var ctx = Open(graph);
            Type systemType = ParameterType(type);
            object parsed = string.IsNullOrEmpty(value) ? DefaultOf(systemType) : ParseValue(value, systemType, name);
            using (ctx.Edit("Add Parameter"))
            {
                typeof(PlacementGraphHandle).GetMethod(nameof(PlacementGraphHandle.AddParameter)).MakeGenericMethod(systemType)
                    .Invoke(ctx.Graph, new[] { name, parsed });
            }
            return Saved(ctx, (J)ParamsJson(ctx, null), save);
        });

        /// <summary>Wires the parameter <paramref name="name"/> into <paramref name="option"/> of <paramref name="node"/>.</summary>
        public static string ParamsBind(string graph, string name, string node, string option, bool save = true) => Run(() =>
        {
            var ctx = Open(graph);
            PlacementNodeHandle target = ctx.Handle(ctx.Resolve(node));
            IPort port = target.FindPort(option, PortDirection.Input);
            using (ctx.Edit("Bind Parameter"))
            {
                if (port.IsConnected) foreach (IPort old in Connected(port)) ctx.Graph.Graph.Disconnect(old, port);
                ctx.Graph.BindParameter(name, target, option);
            }
            ctx.Refresh();
            return Saved(ctx, (J)ParamsJson(ctx, null), save);
        });

        /// <summary>
        /// Sets parameter <paramref name="name"/>: without <paramref name="obj"/>, the graph's default; with it, that
        /// component's override (recorded for Undo).
        /// </summary>
        public static string ParamsSet(string name, string value, string graph = "", string obj = "", bool save = true) => Run(() =>
        {
            if (!string.IsNullOrEmpty(obj))
            {
                PlacementGraph component = FindComponent(obj);
                component.SyncParameters();
                PlacementParameterOverride entry = component.ParameterOverrides.FirstOrDefault(p => p?.Value != null && p.Value.Name == name)
                    ?? throw new ArgumentException($"{component.name}'s graph has no parameter called '{name}'.{Suggest(name, component.ParameterOverrides.Where(p => p?.Value != null).Select(p => p.Value.Name))}");
                object parsed = ParseValue(value, PlacementParameter.SystemTypeOf(entry.Value.Type), name);
                Undo.RecordObject(component, "Set Placement Parameter");
                component.SetParameter(name, parsed);
                EditorUtility.SetDirty(component);
                return ParamsJson(Open(GraphOf(component)), component);
            }
            var ctx = Open(graph);
            IVariable variable = ctx.Parameter(name);
            object typed = ParseValue(value, variable.DataType, name);
            bool ok;
            using (ctx.Edit("Set Parameter Default"))
            {
                ok = (bool)typeof(IVariable).GetMethod(nameof(IVariable.TrySetDefaultValue)).MakeGenericMethod(variable.DataType)
                    .Invoke(variable, new[] { typed });
            }
            if (!ok) throw new InvalidOperationException($"Graph Toolkit refused {value} as the default of '{name}'.");
            return Saved(ctx, (J)ParamsJson(ctx, null), save);
        });

        /// <summary>Stops <paramref name="obj"/> overriding parameter <paramref name="name"/>, so the graph's default is used (recorded for Undo).</summary>
        public static string ParamsClear(string obj, string name) => Run(() =>
        {
            PlacementGraph component = FindComponent(obj);
            component.SyncParameters();
            if (component.ParameterOverrides.All(p => p?.Value == null || p.Value.Name != name))
                throw new ArgumentException($"{component.name}'s graph has no parameter called '{name}'.");
            Undo.RecordObject(component, "Clear Placement Parameter");
            component.ClearParameter(name);
            EditorUtility.SetDirty(component);
            return ParamsJson(Open(GraphOf(component)), component);
        });

        /// <summary>Removes the parameter <paramref name="name"/> and its nodes from the graph.</summary>
        public static string ParamsRemove(string graph, string name, bool save = true) => Run(() =>
        {
            var ctx = Open(graph);
            IVariable variable = ctx.Parameter(name);
            using (ctx.Edit("Remove Parameter")) ctx.Graph.Graph.RemoveVariable(variable, true);
            ctx.Refresh();
            return Saved(ctx, (J)ParamsJson(ctx, null), save);
        });

        // ---------------------------------------------------------------- Components

        /// <summary>The Placement Graph components in open scenes (or those using <paramref name="graph"/>) with their graph, volume, cells and runtime settings.</summary>
        public static string ComponentsList(string graph = "") => Run(() =>
        {
            IEnumerable<PlacementGraph> components = Components();
            if (!string.IsNullOrEmpty(graph))
            {
                string path = GraphPath(graph);
                components = components.Where(c => c.Graph != null && AssetDatabase.GetAssetPath(c.Graph) == path);
            }
            return new J { { "components", components.Select(ComponentJson).ToList() } };
        });

        /// <summary>
        /// Adds a GameObject with a Placement Graph component running <paramref name="graph"/>, at
        /// <paramref name="position"/> (x,y,z) with <paramref name="size"/>, under <paramref name="parent"/> if given. One Undo step.
        /// </summary>
        public static string ComponentCreate(string graph, string name = "", string position = "", string size = "", string parent = "", bool generate = true) => Run(() =>
        {
            string path = GraphPath(graph);
            var asset = AssetDatabase.LoadAssetAtPath<PlacementGraphAsset>(path) ?? throw new InvalidOperationException($"{path} hasn't been imported.");
            Transform parentTransform = string.IsNullOrEmpty(parent) ? null : FindTransform(parent);
            var go = new GameObject(string.IsNullOrEmpty(name) ? Path.GetFileNameWithoutExtension(path) : name);
            Undo.RegisterCreatedObjectUndo(go, "Create Placement Graph");
            if (parentTransform != null) go.transform.SetParent(parentTransform, false);
            if (!string.IsNullOrEmpty(position)) go.transform.position = ParseVector3(position, "position");
            var component = go.AddComponent<PlacementGraph>();
            var serialized = new SerializedObject(component);
            serialized.FindProperty("_graph").objectReferenceValue = asset;
            if (!string.IsNullOrEmpty(size)) serialized.FindProperty("_size").vector3Value = ParseVector3(size, "size");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            component.SyncParameters();
            if (generate) component.Generate();
            return ComponentJson(component);
        });

        /// <summary>
        /// Sets fields of a Placement Graph component, by their inspector names: "Seed=3;Size=20,4,20;Cell Size=0;
        /// Generate On=NearPlayer;Auto Generate=false;Graph=Room;Markers=Path/To/Markers". Recorded for Undo.
        /// </summary>
        public static string ComponentSet(string obj, string set) => Run(() =>
        {
            PlacementGraph component = FindComponent(obj);
            var serialized = new SerializedObject(component);
            foreach ((string key, string text) in ParsePairs(set))
            {
                string field = "_" + char.ToLowerInvariant(PlacementGraphAuthoring.Key(key)[0]) + PlacementGraphAuthoring.Key(key).Substring(1);
                SerializedProperty property = serialized.FindProperty(field);
                if (property == null || field == "_parameters" || field == "_cells")
                    throw new ArgumentException($"A Placement Graph has no field '{key}'.{Suggest(key, ComponentFields(serialized))} Fields: {string.Join(", ", ComponentFields(serialized))}.");
                SetProperty(property, text, key);
            }
            serialized.ApplyModifiedProperties();
            component.SyncParameters();
            return ComponentJson(component);
        });

        // ---------------------------------------------------------------- Run

        /// <summary>
        /// Regenerates the component <paramref name="obj"/>, or every component using <paramref name="graph"/> (or the
        /// only one in the open scenes), as one Undo step each. Returns what was spawned, by prefab, and the run's timings.
        /// </summary>
        public static string Generate(string obj = "", string graph = "") => Run(() =>
        {
            var results = new List<object>();
            foreach (PlacementGraph component in Targets(obj, graph))
            {
                var timer = Stopwatch.StartNew();
                component.Generate();
                timer.Stop();
                J stats = StatsJson(component);
                stats.Insert(1, new KeyValuePair<string, object>("ms", timer.Elapsed.TotalMilliseconds));
                results.Add(stats);
            }
            return new J { { "generated", results } };
        });

        /// <summary>Destroys what <paramref name="obj"/> (or every component using <paramref name="graph"/>) generated, as one Undo step each.</summary>
        public static string Clear(string obj = "", string graph = "") => Run(() =>
        {
            var cleared = new List<object>();
            foreach (PlacementGraph component in Targets(obj, graph))
            {
                component.Clear();
                cleared.Add(HierarchyPath(component));
            }
            return new J { { "cleared", cleared } };
        });

        /// <summary>
        /// A dry run of the graph as it is now, saved or not: every node's point count and time, the spawns by prefab
        /// and the warnings, without touching the scene. With <paramref name="obj"/> it runs in that component's volume,
        /// with its seed, scene inputs and overrides; otherwise in a box of <paramref name="size"/> (x,y,z) at the origin
        /// with <paramref name="seed"/>.
        /// </summary>
        public static string Evaluate(string graph = "", string obj = "", string size = "20,10,20", int seed = 0) => Run(() =>
        {
            PlacementGraph component = string.IsNullOrEmpty(obj) ? null : FindComponent(obj);
            if (component == null && string.IsNullOrEmpty(graph)) throw new ArgumentException("Pass graph, object or both.");
            var ctx = Open(string.IsNullOrEmpty(graph) ? GraphOf(component) : graph);
            var errors = new List<string>();
            var warnings = new List<string>();
            PlacementGraphAsset compiled = ctx.Graph.Compile(errors, warnings);
            try
            {
                if (errors.Count > 0) return new J { { "graph", ctx.Path }, { "errors", errors }, { "warnings", warnings } };
                var timer = Stopwatch.StartNew();
                IReadOnlyList<int> counts;
                IReadOnlyList<double> times;
                IReadOnlyList<string> runWarnings;
                var spawns = new CountingOutput();
                if (component != null)
                {
                    // The component's own run, with this graph swapped in for the call: its volume, seed, markers,
                    // spline and overrides. A null output spawns nothing.
                    FieldInfo field = typeof(PlacementGraph).GetField("_graph", BindingFlags.NonPublic | BindingFlags.Instance);
                    object previous = field.GetValue(component);
                    field.SetValue(component, compiled);
                    try { component.Evaluate(); }
                    finally { field.SetValue(component, previous); }
                    counts = component.LastNodeCounts;
                    times = component.LastNodeMilliseconds;
                    runWarnings = component.LastWarnings;
                }
                else
                {
                    var context = new PlacementContext
                    {
                        LocalBounds = new Bounds(Vector3.zero, ParseVector3(size, "size")),
                        Seed = seed,
                        Output = spawns,
                        Scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene()
                    };
                    PlacementExecutor.Run(compiled, context);
                    counts = context.NodeOutputCounts;
                    times = context.NodeMilliseconds;
                    runWarnings = context.Warnings;
                }
                timer.Stop();
                var result = new J
                {
                    { "graph", ctx.Path },
                    { "object", component != null ? HierarchyPath(component) : null },
                    { "ms", timer.Elapsed.TotalMilliseconds },
                    { "nodes", RunNodes(compiled, counts, times) },
                };
                if (component == null) result.Add("spawns", spawns.ByPrefab);
                result.Add("warnings", warnings.Concat(runWarnings).Distinct().ToList());
                return result;
            }
            finally
            {
                Object.DestroyImmediate(compiled);
            }
        });

        /// <summary>What <paramref name="obj"/> (or every component using <paramref name="graph"/>) has generated, by prefab, its cells, and the last run's per-node points, times and warnings.</summary>
        public static string Stats(string obj = "", string graph = "") => Run(() =>
            new J { { "components", Targets(obj, graph).Select(c => (object)StatsJson(c)).ToList() } });

        // ================================================================ Graph context

        /// <summary>A graph opened for one call: its handle, its nodes in order and their ids.</summary>
        private sealed class Ctx
        {
            public readonly PlacementGraphHandle Graph;
            public string Path => Graph.Path;
            public List<INode> Nodes;
            private Dictionary<INode, string> _ids;
            public bool Changed;

            public Ctx(PlacementGraphHandle graph)
            {
                Graph = graph;
                Refresh();
            }

            public void Refresh()
            {
                Nodes = Graph.Graph.GetNodes().ToList();
                _ids = new Dictionary<INode, string>();
                var used = new HashSet<string>();
                for (int i = 0; i < Nodes.Count; i++)
                {
                    string id = Nodes[i] is PlacementGraphNode p && !string.IsNullOrEmpty(p.Id) ? p.Id.Substring(0, Math.Min(8, p.Id.Length)) : null;
                    // Copies keep their original's id until the graph is saved: the second one goes by index.
                    if (id == null || !used.Add(id)) id = "#" + i;
                    _ids[Nodes[i]] = id;
                }
            }


            public string Id(INode node) => node != null && _ids.TryGetValue(node, out string id) ? id : "?";

            public PlacementNodeHandle Handle(INode node) => new PlacementNodeHandle(Graph, node);

            public string Title(INode node) => Handle(node).Title;

            public string Label(INode node) => $"{Title(node)} ({Id(node)})";

            /// <summary>Opens a Graph Toolkit undo group for the edit; the result is marked changed.</summary>
            public IDisposable Edit(string name)
            {
                Changed = true;
                return new UndoScope(Graph.Graph, name);
            }

            public IVariable Parameter(string name)
            {
                List<IVariable> inputs = Graph.Graph.GetVariables().Where(v => v.VariableKind == VariableKind.Input && v.DataType != typeof(PointData)).ToList();
                return inputs.FirstOrDefault(v => v.Name == name)
                       ?? inputs.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase))
                       ?? throw new ArgumentException($"{System.IO.Path.GetFileName(Path)} has no parameter called '{name}'.{Suggest(name, inputs.Select(v => v.Name))} Parameters: {(inputs.Count > 0 ? string.Join(", ", inputs.Select(v => v.Name)) : "none")}.");
            }

            /// <summary>
            /// A node by id (or its first characters), index (#3 or 3), unique title, or title and the title of a node
            /// wired into it (Spawn Prefabs&lt;Merge).
            /// </summary>
            public INode Resolve(string selector)
            {
                if (string.IsNullOrWhiteSpace(selector)) throw new ArgumentException("Pass a node: an id, #index or title (see inspect).");
                string text = selector.Trim();
                string digits = text.StartsWith("#") ? text.Substring(1) : text;
                if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && (text.StartsWith("#") || !IsHexId(text)))
                {
                    if (index < 0 || index >= Nodes.Count) throw new ArgumentException($"There's no node #{index}: the graph has {Nodes.Count} (#0 to #{Nodes.Count - 1}).");
                    return Nodes[index];
                }
                if (IsHexId(text))
                {
                    List<INode> byId = Nodes.Where(n => Id(n).StartsWith(text, StringComparison.OrdinalIgnoreCase) ||
                                                        (n is PlacementGraphNode p && string.Equals(p.Id, text, StringComparison.OrdinalIgnoreCase))).ToList();
                    if (byId.Count == 1) return byId[0];
                    if (byId.Count > 1) throw new ArgumentException($"'{text}' starts several ids: {string.Join(", ", byId.Select(Label))}. Use more characters.");
                }
                string title = text, upstream = null;
                int arrow = text.IndexOf('<');
                if (arrow > 0)
                {
                    title = text.Substring(0, arrow).Trim();
                    upstream = text.Substring(arrow + 1).Trim();
                }
                List<INode> matches = Nodes.Where(n => SameName(Title(n), title)).ToList();
                if (upstream != null)
                    matches = matches.Where(n => Sources(n).Any(s => SameName(Title(s), upstream) || Id(s).StartsWith(upstream, StringComparison.OrdinalIgnoreCase))).ToList();
                if (matches.Count == 1) return matches[0];
                if (matches.Count > 1)
                {
                    INode first = matches[0];
                    string feeder = Sources(first).Select(Title).FirstOrDefault();
                    throw new ArgumentException($"'{text}' matches {matches.Count} nodes: {string.Join("; ", matches.Select(Describe))}. Pass an id" +
                                                (feeder != null && upstream == null ? $", or the title of a node wired into it, such as '{title}<{feeder}'." : "."));
                }
                IEnumerable<string> titles = Nodes.Select(Title).Distinct();
                throw new ArgumentException($"There's no node '{text}' in {System.IO.Path.GetFileName(Path)}.{Suggest(upstream != null ? title : text, titles)} Nodes: {string.Join(", ", Nodes.Select(Label))}.");
            }

            private string Describe(INode node)
            {
                List<string> sources = Sources(node).Select(Title).ToList();
                return $"{Id(node)} ({Title(node)}{(sources.Count > 0 ? ", fed by " + string.Join(", ", sources) : "")})";
            }

            private static bool IsHexId(string text) => text.Length >= 4 && text.All(Uri.IsHexDigit);
        }

        /// <summary>Wraps an edit in Graph Toolkit's undo recording, so it's one step in the graph window.</summary>
        private sealed class UndoScope : IDisposable
        {
            private readonly Graph _graph;
            private readonly bool _recording;

            public UndoScope(Graph graph, string name)
            {
                _graph = graph;
                try
                {
                    graph.UndoBeginRecordGraph(name);
                    _recording = true;
                }
                catch (Exception)
                {
                    // Undo is a convenience: the edit goes ahead without it.
                }
            }

            public void Dispose()
            {
                if (!_recording) return;
                try { _graph.UndoEndRecordGraph(); }
                catch (Exception) { }
            }
        }

        private static Ctx Open(string graph) => new Ctx(PlacementGraphAuthoring.Open(GraphPath(graph)));

        /// <summary>Saves (when asked and something changed) and adds the save's outcome to <paramref name="result"/>.</summary>
        private static object Saved(Ctx ctx, J result, bool save, bool force = false)
        {
            if (save && (ctx.Changed || force))
            {
                ctx.Graph.Save();
                result.Add("saved", true);
            }
            else result.Add("saved", false);
            List<string> problems = ctx.Graph.Validate();
            if (problems.Count > 0) result.Add("problems", problems);
            return result;
        }

        // ================================================================ Description

        private static object Describe(Ctx ctx, bool compile)
        {
            var result = new J
            {
                { "graph", ctx.Path },
                { "nodes", ctx.Nodes.Select(n => NodeJson(ctx, n, false)).ToList() },
                { "wires", Wires(ctx) },
                { "parameters", ((J)ParamsJson(ctx, null))["parameters"] },
                { "problems", ctx.Graph.Validate() }
            };
            if (compile)
            {
                var errors = new List<string>();
                var warnings = new List<string>();
                PlacementGraphAsset compiled = ctx.Graph.Compile(errors, warnings);
                result.Add("runtimeNodes", compiled.Nodes.Count);
                result.Add("errors", errors);
                result.Add("warnings", warnings);
                Object.DestroyImmediate(compiled);
            }
            return result;
        }

        private static object NodeJson(Ctx ctx, INode node, bool full)
        {
            int index = ctx.Nodes.IndexOf(node);
            var json = new J { { "id", ctx.Id(node) }, { "index", index }, { "type", NodeType(node) } };
            string title = ctx.Title(node);
            if (title != NodeType(node)) json.Add("name", title);
            if (full) json.Add("position", new[] { node.Position.x, node.Position.y });
            if (node is IVariableNode variableNode)
            {
                json.Add("variable", variableNode.Variable.Name);
                json.Add("kind", variableNode.Variable.VariableKind.ToString());
                json.Add("dataType", TypeName(variableNode.Variable.DataType));
            }

            var settings = new J();
            var inputs = new J();
            foreach (IPort port in node.GetInputPorts())
            {
                string name = PlacementGraphHandle.Label(port);
                bool isSetting = node is PlacementGraphNode && !PlacementGraphNode.IsPointPort(port) && !PlacementGraphNode.IsLinkPort(port);
                if (isSetting)
                {
                    object value = SettingValue(ctx, port);
                    if (full)
                    {
                        J described = SettingType(name, port.DataType, null, port.Tooltip);
                        described.RemoveAll(p => p.Key == "default");
                        described.Add("value", value);
                        settings.Add(name, described);
                    }
                    else settings.Add(name, value);
                }
                else if (port.IsConnected || full) inputs.Add(name, Connected(port).Select(p => Ref(ctx, p)).ToList());
            }
            if (settings.Count > 0) json.Add("settings", settings);
            if (inputs.Count > 0) json.Add("in", inputs);

            var outputs = new J();
            foreach (IPort port in node.GetOutputPorts())
            {
                List<IPort> to = Connected(port);
                if (to.Count > 0 || full) outputs.Add(PlacementGraphHandle.Label(port), to.Select(p => Ref(ctx, p)).ToList());
            }
            if (outputs.Count > 0) json.Add("out", outputs);

            Chain chain = Chain.TryOf(node);
            if (chain != null) json.Add(chain.Key, ChainJson(ctx, node));
            return json;
        }

        private static string NodeType(INode node) => node switch
        {
            PlacementGraphNode p => PlacementGraphNode.TitleOf(p.GetType()),
            IVariableNode => "Variable",
            IConstantNode => "Constant",
            ISubgraphNode => "Subgraph",
            _ => node.GetType().Name
        };

        private static List<object> Wires(Ctx ctx)
        {
            var wires = new List<object>();
            foreach (INode node in ctx.Nodes)
            {
                foreach (IPort output in node.GetOutputPorts())
                {
                    foreach (IPort input in Connected(output))
                    {
                        // Inline enum constants are shown as the setting's value.
                        if (node is IConstantNode) continue;
                        wires.Add(Wire(ctx, output, input));
                    }
                }
            }
            return wires;
        }

        private static string Wire(Ctx ctx, IPort output, IPort input) => $"{Ref(ctx, output)} -> {Ref(ctx, input)}";

        private static string Ref(Ctx ctx, IPort port) => $"{ctx.Id(port.GetNode())}.{PlacementGraphHandle.Label(port)}";

        private static object SettingValue(Ctx ctx, IPort port)
        {
            if (!port.IsConnected) return ReadPort(port);
            IPort source = port.FirstConnectedPort;
            INode node = source.GetNode();
            if (node is IConstantNode constant)
            {
                var arguments = new object[] { null };
                MethodInfo get = typeof(IConstantNode).GetMethod(nameof(IConstantNode.TryGetValue)).MakeGenericMethod(constant.DataType);
                return (bool)get.Invoke(constant, arguments) ? arguments[0] : null;
            }
            if (node is IVariableNode variable) return new J { { "parameter", variable.Variable.Name } };
            return new J { { "from", Ref(ctx, source) } };
        }

        private static object ReadPort(IPort port)
        {
            var arguments = new object[] { null };
            MethodInfo get = typeof(IPort).GetMethod(nameof(IPort.TryGetValue)).MakeGenericMethod(port.DataType);
            return (bool)get.Invoke(port, arguments) ? arguments[0] : null;
        }

        private static J SettingType(string name, Type type, object defaultValue, string tooltip)
        {
            var json = new J { { "name", name }, { "type", TypeName(type) }, { "default", defaultValue } };
            if (type.IsEnum) json.Add("choices", Enum.GetNames(type));
            if (!string.IsNullOrEmpty(tooltip)) json.Add("tooltip", tooltip);
            return json;
        }

        private static string TypeName(Type type) => type == typeof(float) ? "float" : type == typeof(int) ? "int" : type == typeof(bool) ? "bool" :
            type == typeof(string) ? "string" : type == typeof(PointData) ? "Points" : type.Name;

        private static List<IPort> Connected(IPort port)
        {
            var ports = new List<IPort>();
            if (port.IsConnected) port.GetConnectedPorts(ports);
            return ports;
        }

        private static IEnumerable<INode> Sources(INode node) =>
            node.GetInputPorts().SelectMany(Connected).Select(p => p.GetNode()).Distinct();

        private static bool SameName(string a, string b) =>
            string.Equals(PlacementGraphAuthoring.Key(a), PlacementGraphAuthoring.Key(b), StringComparison.OrdinalIgnoreCase);

        private static bool SamePort(IPort port, string name) => port.Name == name || SameName(PlacementGraphHandle.Label(port), name);

        // ================================================================ Chains (entries and modules)

        /// <summary>A chain of value nodes wired into a node: Prefab Entries into Entries, or Modules into Modules.</summary>
        private sealed class Chain
        {
            public string Key;        // "entries" or "modules"
            public string Single;     // "entry" or "module"
            public string NodeType;   // the title of the chained nodes
            public string Port;       // the target's input, also each chained node's input to the one before
            public string Output;     // each chained node's output

            private static readonly Chain Entries = new Chain
            { Key = "entries", Single = "entry", NodeType = "Prefab Entry", Port = PlacementGraphNode.EntriesPort, Output = PrefabEntryNode.EntryPort };

            private static readonly Chain Modules = new Chain
            { Key = "modules", Single = "module", NodeType = "Module", Port = PlacementGraphNode.ModulesPort, Output = ModuleNode.ModulePort };

            public static Chain TryOf(INode node)
            {
                if (node is PrefabEntryNode || node is ModuleNode) return null;
                if (node.GetInputPortByName(PlacementGraphNode.EntriesPort) != null) return Entries;
                if (node.GetInputPortByName(PlacementGraphNode.ModulesPort) != null) return Modules;
                return null;
            }

            public static Chain Of(INode node) => TryOf(node) ?? throw new ArgumentException(
                $"{PlacementGraphNode.TitleOf(node.GetType())} has no Entries or Modules input. Entries are on Spawn Prefabs, Spawn Instanced and Select Prefab; modules on Subdivide.");

            public List<INode> Nodes(INode target) =>
                PrefabEntryNode.Chain<PlacementGraphNode>(target.GetInputPortByName(Port), Port).Cast<INode>().ToList();

            /// <summary>Wires <paramref name="nodes"/> into <paramref name="target"/> in order, first to last, replacing the old chain's wires.</summary>
            public void Rewire(Graph graph, INode target, List<INode> nodes)
            {
                IPort input = target.GetInputPortByName(Port);
                foreach (IPort old in Connected(input)) graph.Disconnect(old, input);
                foreach (INode node in nodes)
                {
                    IPort chainIn = node.GetInputPortByName(Port);
                    foreach (IPort old in Connected(chainIn)) graph.Disconnect(old, chainIn);
                    IPort output = node.GetOutputPortByName(Output);
                    foreach (IPort old in Connected(output)) graph.Disconnect(output, old);
                }
                for (int i = 0; i < nodes.Count; i++)
                {
                    IPort into = i + 1 < nodes.Count ? nodes[i + 1].GetInputPortByName(Port) : input;
                    if (!graph.Connect(nodes[i].GetOutputPortByName(Output), into))
                        throw new InvalidOperationException($"Couldn't chain {NodeType} {i} into {PlacementGraphNode.TitleOf(target.GetType())}.");
                }
            }
        }

        private static List<object> ChainJson(Ctx ctx, INode target)
        {
            Chain chain = Chain.Of(target);
            return chain.Nodes(target).Select((n, i) =>
            {
                var json = new J { { "index", i }, { "id", ctx.Id(n) } };
                foreach (IPort port in n.GetInputPorts())
                {
                    if (PlacementGraphNode.IsLinkPort(port)) continue;
                    json.Add(PlacementGraphHandle.Label(port), SettingValue(ctx, port));
                }
                if (n is PrefabEntryNode)
                {
                    List<INode> attributes = PrefabEntryNode.Chain<EntryAttributeNode>(n.GetInputPortByName(PrefabEntryNode.AttributesPort), PrefabEntryNode.AttributesPort).Cast<INode>().ToList();
                    if (attributes.Count > 0)
                    {
                        json.Add("attributes", attributes.Select(a => new J
                        {
                            { "id", ctx.Id(a) },
                            { "name", ctx.Handle(a).Get("Name") },
                            { "type", ctx.Handle(a).Get("Type") },
                            { "value", AttributeValue(ctx.Handle(a)) }
                        }).ToList());
                    }
                }
                return (object)json;
            }).ToList();
        }

        private static object AttributeValue(PlacementNodeHandle attribute)
        {
            object type = attribute.Get("Type");
            string setting = type is PointAttributeType kind ? kind switch
            {
                PointAttributeType.Float => "Float Value",
                PointAttributeType.Int => "Int Value",
                PointAttributeType.Vector3 => "Vector3 Value",
                _ => "Object Value"
            } : "Float Value";
            return attribute.Get(setting);
        }

        /// <summary>The value nodes that only exist for <paramref name="node"/>: its entry or module chain, or a Prefab Entry's attributes.</summary>
        private static List<INode> ChainedValueNodes(INode node)
        {
            var linked = new List<INode>();
            Chain chain = Chain.TryOf(node);
            List<INode> direct = chain != null ? chain.Nodes(node) : new List<INode>();
            if (node is PrefabEntryNode) direct = PrefabEntryNode.Chain<EntryAttributeNode>(node.GetInputPortByName(PrefabEntryNode.AttributesPort), PrefabEntryNode.AttributesPort).Cast<INode>().ToList();
            foreach (INode child in direct)
            {
                linked.Add(child);
                if (child is PrefabEntryNode) linked.AddRange(ChainedValueNodes(child));
            }
            return linked;
        }

        /// <summary>For a Prefab Entry or Module, the node its chain ends in (a spawner or Subdivide); otherwise null.</summary>
        private static INode ChainOwner(INode node)
        {
            var visited = new HashSet<INode>();
            INode current = node;
            while (current is PrefabEntryNode || current is ModuleNode)
            {
                if (!visited.Add(current)) return null;
                IPort output = current.GetOutputPortByName(current is PrefabEntryNode ? PrefabEntryNode.EntryPort : ModuleNode.ModulePort);
                if (output == null || !output.IsConnected) return null;
                current = output.FirstConnectedPort.GetNode();
            }
            return current == node ? null : current;
        }

        private static INode AddChainNode(Ctx ctx, INode target, Chain chain, GameObject prefab, string set, int position)
        {
            Vector2 at = target.Position + new Vector2(-ColumnWidth * (position + 1), 220f);
            PlacementNodeHandle node = ctx.Graph.AddNode(chain.NodeType, at);
            node.Set("Prefab", prefab);
            if (chain.Key == "modules" && prefab != null) node.Set("Symbol", prefab.name);
            ApplySettings(ctx, node, set);
            return node.Node;
        }

        [Serializable]
        private class EntryFile
        {
            public EntrySpec[] entries;
        }

        [Serializable]
        private class EntrySpec
        {
            public string prefab;
            public string set;
        }

        private static List<(GameObject prefab, string set)> Specs(string prefabs, string set, string file)
        {
            var specs = new List<(GameObject, string)>();
            if (!string.IsNullOrEmpty(file))
            {
                if (!File.Exists(file)) throw new FileNotFoundException($"There's no entries file at {file}.", file);
                string text = File.ReadAllText(file).Trim();
                if (text.StartsWith("[")) text = "{\"entries\":" + text + "}";
                EntryFile parsed = JsonUtility.FromJson<EntryFile>(text);
                if (parsed?.entries == null) throw new ArgumentException($"{file} should hold [{{\"prefab\": \"...\", \"set\": \"Weight=2\"}}, ...].");
                foreach (EntrySpec spec in parsed.entries)
                    specs.Add((ParseAsset<GameObject>(spec.prefab, "prefab"), JoinSets(set, spec.set)));
            }
            foreach (string item in Split(prefabs, ';'))
                specs.Add((ParseAsset<GameObject>(item, "prefab"), set));
            if (specs.Count == 0) throw new ArgumentException("Pass prefabs (\"a.prefab;b.prefab\", paths or names) or file.");
            return specs;
        }

        private static string JoinSets(string a, string b) =>
            string.IsNullOrEmpty(a) ? b ?? "" : string.IsNullOrEmpty(b) ? a : a + ";" + b;

        private static bool PrefabMatches(GameObject prefab, string text)
        {
            if (prefab == null) return string.Equals(text, "none", StringComparison.OrdinalIgnoreCase);
            return string.Equals(prefab.name, text, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Path.GetFileNameWithoutExtension(text), prefab.name, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(AssetDatabase.GetAssetPath(prefab), text, StringComparison.OrdinalIgnoreCase);
        }

        // ================================================================ Parameters

        private static object ParamsJson(Ctx ctx, PlacementGraph component)
        {
            List<IVariable> variables = ctx.Graph.Graph.GetVariables().Where(v => v.VariableKind == VariableKind.Input && v.DataType != typeof(PointData)).ToList();
            var parameters = variables.Select(v =>
            {
                var arguments = new object[] { null };
                bool has = (bool)typeof(IVariable).GetMethod(nameof(IVariable.TryGetDefaultValue)).MakeGenericMethod(v.DataType).Invoke(v, arguments);
                var drives = new List<string>();
                foreach (INode node in ctx.Nodes.Where(n => n is IVariableNode vn && vn.Variable == v))
                    foreach (IPort output in node.GetOutputPorts())
                        drives.AddRange(Connected(output).Select(p => Ref(ctx, p)));
                var json = new J { { "name", v.Name }, { "type", TypeName(v.DataType) }, { "default", has ? arguments[0] : null }, { "drives", drives } };
                if (component != null)
                {
                    PlacementParameterOverride entry = component.ParameterOverrides.FirstOrDefault(p => p?.Value != null && p.Value.Name == v.Name);
                    json.Add("override", entry != null && entry.Override ? entry.Value.Value : null);
                }
                return (object)json;
            }).ToList();
            var result = new J { { "graph", ctx.Path } };
            if (component != null) result.Add("object", HierarchyPath(component));
            result.Add("parameters", parameters);
            return result;
        }

        private static Type ParameterType(string type)
        {
            switch ((type ?? "").Trim().ToLowerInvariant())
            {
                case "float": case "single": return typeof(float);
                case "int": case "integer": case "int32": return typeof(int);
                case "bool": case "boolean": return typeof(bool);
                case "vector3": case "vec3": return typeof(Vector3);
                case "gameobject": case "prefab": return typeof(GameObject);
                case "layermask": case "layers": return typeof(LayerMask);
                default: throw new ArgumentException($"A parameter type is one of float, int, bool, Vector3, GameObject, LayerMask; not '{type}'.");
            }
        }

        private static object DefaultOf(Type type) => type == typeof(LayerMask) ? (object)(LayerMask)~0 : type.IsValueType ? Activator.CreateInstance(type) : null;

        // ================================================================ Components

        private static List<PlacementGraph> Components()
        {
            var components = new List<PlacementGraph>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    components.AddRange(root.GetComponentsInChildren<PlacementGraph>(true));
            }
            UnityEditor.SceneManagement.PrefabStage stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null) components.AddRange(stage.prefabContentsRoot.GetComponentsInChildren<PlacementGraph>(true));
            return components;
        }

        private static PlacementGraph FindComponent(string obj)
        {
            List<PlacementGraph> components = Components();
            if (string.IsNullOrWhiteSpace(obj)) throw new ArgumentException("Pass object: a hierarchy path or name of a Placement Graph object.");
            string text = obj.Trim();
            List<PlacementGraph> matches = components.Where(c => HierarchyPath(c) == text || HierarchyPath(c).EndsWith("/" + text)).ToList();
            if (matches.Count == 0) matches = components.Where(c => string.Equals(c.name, text, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1) throw new ArgumentException($"Several Placement Graphs match '{text}': {string.Join(", ", matches.Select(HierarchyPath))}. Pass a longer path.");
            throw new ArgumentException($"There's no Placement Graph '{text}' in the open scenes.{Suggest(text, components.Select(c => c.name))} Placement Graphs: {(components.Count > 0 ? string.Join(", ", components.Select(HierarchyPath)) : "none")}.");
        }

        private static List<PlacementGraph> Targets(string obj, string graph)
        {
            if (!string.IsNullOrEmpty(obj)) return new List<PlacementGraph> { FindComponent(obj) };
            List<PlacementGraph> components = Components();
            if (!string.IsNullOrEmpty(graph))
            {
                string path = GraphPath(graph);
                List<PlacementGraph> users = components.Where(c => c.Graph != null && AssetDatabase.GetAssetPath(c.Graph) == path).ToList();
                if (users.Count == 0) throw new InvalidOperationException($"No Placement Graph in the open scenes uses {path}. Add one with placement.component create.");
                return users;
            }
            if (components.Count == 1) return components;
            throw new ArgumentException(components.Count == 0
                ? "There's no Placement Graph in the open scenes."
                : $"Pass object or graph: the open scenes have {components.Count} Placement Graphs ({string.Join(", ", components.Select(HierarchyPath))}).");
        }

        private static Transform FindTransform(string path)
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                        if (HierarchyPath(t) == path || HierarchyPath(t).EndsWith("/" + path) || t.name == path) return t;
                }
            }
            throw new ArgumentException($"There's no object '{path}' in the open scenes.");
        }

        private static string GraphOf(PlacementGraph component) =>
            component.Graph != null ? AssetDatabase.GetAssetPath(component.Graph) : throw new InvalidOperationException($"{component.name} has no graph assigned.");

        private static string HierarchyPath(Component component) => HierarchyPath(component.transform);

        private static string HierarchyPath(Transform t)
        {
            var path = new StringBuilder(t.name);
            for (Transform parent = t.parent; parent != null; parent = parent.parent) path.Insert(0, parent.name + "/");
            return path.ToString();
        }

        private static object ComponentJson(PlacementGraph c)
        {
            var serialized = new SerializedObject(c);
            return new J
            {
                { "object", HierarchyPath(c) },
                { "scene", c.gameObject.scene.path },
                { "graph", c.Graph != null ? AssetDatabase.GetAssetPath(c.Graph) : null },
                { "seed", c.Seed },
                { "size", c.Size },
                { "position", c.transform.position },
                { "autoGenerate", serialized.FindProperty("_autoGenerate")?.boolValue },
                { "cellSize", c.CellSize },
                { "cellOverlap", c.CellOverlap },
                { "cells", c.CellIndices.Count() },
                { "generateOn", c.GenerateOn },
                { "saveGenerated", c.SaveGenerated },
                { "instances", c.InstanceCount },
                { "markers", c.Markers != null ? HierarchyPath(c.Markers) : null },
                { "overrides", c.ParameterOverrides.Where(p => p?.Value != null && p.Override).ToDictionary(p => p.Value.Name, p => p.Value.Value) }
            };
        }

        private static IEnumerable<string> ComponentFields(SerializedObject serialized)
        {
            SerializedProperty property = serialized.GetIterator();
            if (!property.NextVisible(true)) yield break;
            do
            {
                if (property.name == "m_Script" || property.name == "_parameters" || property.name == "_cells") continue;
                yield return ObjectNames.NicifyVariableName(property.name);
            } while (property.NextVisible(false));
        }

        private static void SetProperty(SerializedProperty property, string text, string what)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer: property.intValue = (int)ParseValue(text, typeof(int), what); break;
                case SerializedPropertyType.Float: property.floatValue = (float)ParseValue(text, typeof(float), what); break;
                case SerializedPropertyType.Boolean: property.boolValue = (bool)ParseValue(text, typeof(bool), what); break;
                case SerializedPropertyType.Vector3: property.vector3Value = ParseVector3(text, what); break;
                case SerializedPropertyType.String: property.stringValue = text; break;
                case SerializedPropertyType.Enum:
                {
                    string[] names = property.enumNames;
                    int index = Array.FindIndex(names, n => SameName(n, text));
                    if (index < 0 && int.TryParse(text, out int given) && given >= 0 && given < names.Length) index = given;
                    if (index < 0) throw new ArgumentException($"{what} is one of {string.Join(", ", names)}.");
                    property.enumValueIndex = index;
                    break;
                }
                case SerializedPropertyType.ObjectReference:
                {
                    if (IsNone(text)) { property.objectReferenceValue = null; break; }
                    if (property.name == "_graph")
                    {
                        property.objectReferenceValue = AssetDatabase.LoadAssetAtPath<PlacementGraphAsset>(GraphPath(text));
                        break;
                    }
                    Transform target = FindTransform(text);
                    Type type = ObjectReferenceType(property);
                    if (type == null || type == typeof(Transform))
                    {
                        property.objectReferenceValue = target;
                        break;
                    }
                    Component component = target.GetComponent(type);
                    if (component == null) throw new ArgumentException($"{text} has no {type.Name}.");
                    property.objectReferenceValue = component;
                    break;
                }
                default: throw new ArgumentException($"{what} can't be set from the command line.");
            }
        }

        private static Type ObjectReferenceType(SerializedProperty property)
        {
            FieldInfo field = typeof(PlacementGraph).GetField(property.name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            return field?.FieldType;
        }

        private static J StatsJson(PlacementGraph c)
        {
            var byPrefab = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var roots = new List<Transform>();
            List<Vector2Int> cells = c.CellIndices.ToList();
            if (cells.Count > 0) roots.AddRange(cells.Select(c.CellContainer).Where(g => g != null).Select(g => g.transform));
            else roots.Add(c.transform);
            int objects = 0;
            foreach (Transform root in roots)
            {
                foreach (Transform child in root)
                {
                    if (cells.Count == 0 && c.IsInput(child)) continue;
                    GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                    string name = source != null && PrefabUtility.IsOutermostPrefabInstanceRoot(child.gameObject) ? source.name : child.name.Replace("(Clone)", "").Trim();
                    byPrefab.TryGetValue(name, out int count);
                    byPrefab[name] = count + 1;
                    objects++;
                }
            }
            return new J
            {
                { "object", HierarchyPath(c) },
                { "graph", c.Graph != null ? AssetDatabase.GetAssetPath(c.Graph) : null },
                { "objects", objects },
                { "byPrefab", byPrefab },
                { "instances", c.InstanceCount },
                { "cells", cells.Count },
                { "lastRun", c.Graph != null ? RunNodes(c.Graph, c.LastNodeCounts, c.LastNodeMilliseconds) : new List<object>() },
                { "warnings", c.LastWarnings.ToList() }
            };
        }

        private static List<object> RunNodes(PlacementGraphAsset graph, IReadOnlyList<int> counts, IReadOnlyList<double> times)
        {
            var nodes = new List<object>();
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                nodes.Add(new J
                {
                    { "index", i },
                    { "name", graph.Nodes[i]?.Name },
                    { "points", i < counts.Count ? counts[i] : (object)null },
                    { "ms", i < times.Count ? times[i] : (object)null }
                });
            }
            return nodes;
        }

        private sealed class CountingOutput : IPlacementOutput
        {
            public readonly SortedDictionary<string, int> ByPrefab = new SortedDictionary<string, int>(StringComparer.Ordinal);

            public void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
            {
                string name = prefab != null ? prefab.name : "none";
                ByPrefab.TryGetValue(name, out int count);
                ByPrefab[name] = count + 1;
            }
        }

        // ================================================================ Values

        private static void ApplySettings(Ctx ctx, PlacementNodeHandle node, string set)
        {
            foreach ((string key, string text) in ParsePairs(set)) ApplySetting(ctx, node, key, text);
        }

        private static void ApplySetting(Ctx ctx, PlacementNodeHandle node, string option, string text)
        {
            IPort port;
            try { port = node.FindPort(option, PortDirection.Input); }
            catch (ArgumentException)
            {
                IEnumerable<string> names = node.Node.GetInputPorts().Where(p => !PlacementGraphNode.IsPointPort(p) && !PlacementGraphNode.IsLinkPort(p)).Select(PlacementGraphHandle.Label);
                throw new ArgumentException($"{ctx.Label(node.Node)} has no setting '{option}'.{Suggest(option, names)} Its settings: {string.Join(", ", names)}.");
            }
            if (PlacementGraphNode.IsPointPort(port) || PlacementGraphNode.IsLinkPort(port))
                throw new ArgumentException($"{ctx.Label(node.Node)}.{option} is a wire input, not a setting: use placement.wire connect.");
            object value = ParseValue(text, port.DataType, $"{node.Title}.{option}");
            using (ctx.Edit("Set " + option))
            {
                // An inline enum constant (the authoring API's fallback) is replaced, so a value can be set again.
                if (port.IsConnected && Connected(port).All(p => p.GetNode() is IConstantNode))
                {
                    foreach (IPort old in Connected(port))
                    {
                        INode constant = old.GetNode();
                        ctx.Graph.Graph.Disconnect(old, port);
                        if (!constant.IsConnected) ctx.Graph.Graph.RemoveNode(constant);
                    }
                }
                node.Set(option, value);
            }
        }

        /// <summary>Parses "A=1;B=two" into pairs. A backslash keeps a ; or = in a value.</summary>
        internal static List<(string key, string value)> ParsePairs(string text)
        {
            var pairs = new List<(string, string)>();
            foreach (string item in Split(text, ';'))
            {
                int equals = IndexOfUnescaped(item, '=');
                if (equals <= 0) throw new ArgumentException($"'{item}' should be Name=Value.");
                pairs.Add((Unescape(item.Substring(0, equals)).Trim(), Unescape(item.Substring(equals + 1)).Trim()));
            }
            return pairs;
        }

        private static IEnumerable<string> Split(string text, char separator)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            var current = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length) { current.Append(text[i]).Append(text[++i]); continue; }
                if (text[i] == separator)
                {
                    if (current.ToString().Trim().Length > 0) yield return current.ToString().Trim();
                    current.Clear();
                    continue;
                }
                current.Append(text[i]);
            }
            if (current.ToString().Trim().Length > 0) yield return current.ToString().Trim();
        }

        private static int IndexOfUnescaped(string text, char c)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\') { i++; continue; }
                if (text[i] == c) return i;
            }
            return -1;
        }

        private static string Unescape(string text) => text.Replace("\\;", ";").Replace("\\=", "=").Replace("\\\\", "\\");

        private static bool IsNone(string text) => string.IsNullOrWhiteSpace(text) || text.Trim().Equals("none", StringComparison.OrdinalIgnoreCase) || text.Trim().Equals("null", StringComparison.OrdinalIgnoreCase);

        /// <summary>Text to a value of <paramref name="type"/>: numbers, booleans, enums by name, x,y,z vectors, layer masks, asset paths or names.</summary>
        internal static object ParseValue(string text, Type type, string what)
        {
            text = (text ?? "").Trim();
            if (type == typeof(string)) return text;
            if (type == typeof(float))
            {
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return f;
                throw new ArgumentException($"{what} is a number; '{text}' isn't one.");
            }
            if (type == typeof(int))
            {
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return i;
                throw new ArgumentException($"{what} is a whole number; '{text}' isn't one.");
            }
            if (type == typeof(bool))
            {
                switch (text.ToLowerInvariant())
                {
                    case "true": case "1": case "on": case "yes": return true;
                    case "false": case "0": case "off": case "no": return false;
                }
                throw new ArgumentException($"{what} is true or false; not '{text}'.");
            }
            if (type.IsEnum)
            {
                string match = Enum.GetNames(type).FirstOrDefault(n => SameName(n, text));
                if (match != null) return Enum.Parse(type, match);
                if (int.TryParse(text, out int number) && Enum.IsDefined(type, number)) return Enum.ToObject(type, number);
                throw new ArgumentException($"{what} is one of {string.Join(", ", Enum.GetNames(type))}; not '{text}'.{Suggest(text, Enum.GetNames(type))}");
            }
            if (type == typeof(Vector3)) return ParseVector3(text, what);
            if (type == typeof(Vector2)) return ParseVector2(text, what);
            if (type == typeof(LayerMask))
            {
                if (int.TryParse(text, out int mask)) return (LayerMask)mask;
                if (SameName(text, "Everything")) return (LayerMask)~0;
                if (SameName(text, "Nothing")) return (LayerMask)0;
                int value = 0;
                foreach (string layer in Split(text, ','))
                {
                    int index = LayerMask.NameToLayer(layer);
                    if (index < 0) throw new ArgumentException($"{what}: there's no layer called '{layer}'.");
                    value |= 1 << index;
                }
                return (LayerMask)value;
            }
            if (typeof(Object).IsAssignableFrom(type)) return IsNone(text) ? null : ParseAsset(text, type, what);
            throw new ArgumentException($"{what} is a {type.Name}, which can't be set from text.");
        }

        private static object ParseAttributeValue(string text, PointAttributeType? kind)
        {
            switch (kind)
            {
                case PointAttributeType.Float: return ParseValue(text, typeof(float), "value");
                case PointAttributeType.Int: return ParseValue(text, typeof(int), "value");
                case PointAttributeType.Vector3: return ParseVector3(text, "value");
                case PointAttributeType.Object: return IsNone(text) ? null : ParseAsset(text, typeof(Object), "value");
            }
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return i;
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return f;
            if (text.Contains(",")) return ParseVector3(text, "value");
            return IsNone(text) ? null : ParseAsset(text, typeof(Object), "value");
        }

        private static T ParseAsset<T>(string text, string what) where T : Object => (T)ParseAsset(text, typeof(T), what);

        /// <summary>An asset by path, or by name when only one asset of the type has it.</summary>
        private static Object ParseAsset(string text, Type type, string what)
        {
            text = (text ?? "").Trim().Replace('\\', '/');
            if (string.IsNullOrEmpty(text)) throw new ArgumentException($"{what}: pass an asset path or name.");
            if (text.StartsWith("Assets/") || text.StartsWith("Packages/"))
            {
                Object asset = AssetDatabase.LoadAssetAtPath(text, type);
                if (asset != null) return asset;
                throw new ArgumentException($"{what}: there's no {type.Name} at {text}.");
            }
            string name = Path.GetFileNameWithoutExtension(text);
            string filter = type == typeof(GameObject) ? "t:Prefab" : type == typeof(Object) ? "" : "t:" + type.Name;
            List<string> paths = AssetDatabase.FindAssets($"{name} {filter}".Trim())
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), name, StringComparison.OrdinalIgnoreCase))
                .Distinct().ToList();
            // A prefab and the model it's made from often share a name: the prefab is meant.
            if (paths.Count > 1 && type == typeof(GameObject))
            {
                List<string> prefabs = paths.Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)).ToList();
                if (prefabs.Count > 0) paths = prefabs;
            }
            if (paths.Count == 1) return AssetDatabase.LoadAssetAtPath(paths[0], type);
            if (paths.Count > 1) throw new ArgumentException($"{what}: several assets are called '{name}': {string.Join(", ", paths)}. Pass the path.");
            throw new ArgumentException($"{what}: there's no {(type == typeof(GameObject) ? "prefab" : type.Name)} called '{name}'. Pass an asset path.");
        }

        private static Vector3 ParseVector3(string text, string what)
        {
            float[] parts = Numbers(text, what);
            if (parts.Length == 1) return new Vector3(parts[0], parts[0], parts[0]);
            if (parts.Length == 3) return new Vector3(parts[0], parts[1], parts[2]);
            throw new ArgumentException($"{what} is a vector: x,y,z (or one number for all three); not '{text}'.");
        }

        private static Vector2 ParseVector2(string text, string what)
        {
            float[] parts = Numbers(text, what);
            if (parts.Length == 2) return new Vector2(parts[0], parts[1]);
            throw new ArgumentException($"{what} is x,y; not '{text}'.");
        }

        private static float[] Numbers(string text, string what)
        {
            string[] items = (text ?? "").Trim().Trim('(', ')', '[', ']').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var numbers = new float[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                if (!float.TryParse(items[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
                    throw new ArgumentException($"{what}: '{items[i]}' isn't a number.");
            }
            return numbers;
        }

        // ================================================================ Paths and names

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Pass a path under Assets/.");
            path = path.Trim().Replace('\\', '/');
            if (!path.StartsWith("Assets/") && !path.StartsWith("Packages/")) path = "Assets/" + path.TrimStart('/');
            return path;
        }

        private static List<string> GraphPaths() =>
            AssetDatabase.FindAssets("glob:\"**/*." + PlacementGraphAuthoring.Extension + "\"")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith("." + PlacementGraphAuthoring.Extension, StringComparison.OrdinalIgnoreCase))
                .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();

        /// <summary>A graph's project path from its path or file name.</summary>
        private static string GraphPath(string graph)
        {
            if (string.IsNullOrWhiteSpace(graph)) throw new ArgumentException("Pass graph: a .placegraph path or name.");
            string text = graph.Trim().Replace('\\', '/');
            string extension = "." + PlacementGraphAuthoring.Extension;
            if ((text.StartsWith("Assets/") || text.StartsWith("Packages/")) && text.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                if (AssetDatabase.LoadMainAssetAtPath(text) == null && !File.Exists(text)) throw new FileNotFoundException($"There's no placement graph at {text}.", text);
                return text;
            }
            string name = Path.GetFileNameWithoutExtension(text);
            List<string> all = GraphPaths();
            List<string> matches = all.Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1) throw new ArgumentException($"Several graphs are called '{name}': {string.Join(", ", matches)}. Pass the path.");
            throw new ArgumentException($"There's no placement graph called '{graph}'.{Suggest(name, all.Select(Path.GetFileNameWithoutExtension))}");
        }

        private static string Name(Object obj) => obj != null ? obj.name : "none";

        /// <summary>" Did you mean 'X'?" for the closest candidates, or nothing.</summary>
        internal static string Suggest(string input, IEnumerable<string> candidates)
        {
            string key = PlacementGraphAuthoring.Key(input ?? "").ToLowerInvariant();
            if (key.Length == 0) return "";
            List<string> close = candidates.Where(c => !string.IsNullOrEmpty(c)).Distinct()
                .Select(c => (c, d: Distance(key, PlacementGraphAuthoring.Key(c).ToLowerInvariant())))
                .Where(x => x.d <= Math.Max(2, key.Length / 3) || PlacementGraphAuthoring.Key(x.c).ToLowerInvariant().Contains(key))
                .OrderBy(x => x.d).Take(3).Select(x => x.c).ToList();
            return close.Count == 0 ? "" : $" Did you mean {string.Join(" or ", close.Select(c => $"'{c}'"))}?";
        }

        private static int Distance(string a, string b)
        {
            var row = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) row[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int previous = row[0];
                row[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int temp = row[j];
                    row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + (a[i - 1] == b[j - 1] ? 0 : 1));
                    previous = temp;
                }
            }
            return row[b.Length];
        }

        // ================================================================ JSON

        /// <summary>Runs a command body; mistakes become <c>{"error": ...}</c>.</summary>
        private static string Run(Func<object> body)
        {
            try
            {
                return Write(body());
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                return Error(e.InnerException);
            }
            catch (Exception e)
            {
                return Error(e);
            }
        }

        private static string Error(Exception e)
        {
            bool expected = e is ArgumentException || e is InvalidOperationException || e is FileNotFoundException;
            string message = expected ? e.Message : $"{e.GetType().Name}: {e.Message}";
            // ArgumentException appends "(Parameter 'x')", which means nothing on the command line.
            int parameter = message.IndexOf(" (Parameter '", StringComparison.Ordinal);
            if (parameter > 0) message = message.Substring(0, parameter);
            return Write(new J { { "error", message } });
        }

        /// <summary>An ordered JSON object.</summary>
        private sealed class J : List<KeyValuePair<string, object>>
        {
            public void Add(string key, object value) => Add(new KeyValuePair<string, object>(key, value));

            public object this[string key] => this.FirstOrDefault(p => p.Key == key).Value;
        }

        private static string Write(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null: sb.Append("null"); return;
                case string s: Quote(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case int or long or short or byte or uint: sb.Append(System.Convert.ToString(value, CultureInfo.InvariantCulture)); return;
                case float f: Number(sb, f); return;
                case double d: Number(sb, d); return;
                case Enum e: Quote(sb, e.ToString()); return;
                case Vector3 v: sb.Append('['); Number(sb, v.x); sb.Append(','); Number(sb, v.y); sb.Append(','); Number(sb, v.z); sb.Append(']'); return;
                case Vector2 v2: sb.Append('['); Number(sb, v2.x); sb.Append(','); Number(sb, v2.y); sb.Append(']'); return;
                case Vector2Int vi: sb.Append('[').Append(vi.x).Append(',').Append(vi.y).Append(']'); return;
                case LayerMask mask: sb.Append(mask.value); return;
                case Object obj:
                    if (obj == null) sb.Append("null");
                    else
                    {
                        string path = AssetDatabase.GetAssetPath(obj);
                        Quote(sb, string.IsNullOrEmpty(path) ? obj.name : path);
                    }
                    return;
                case J j:
                    sb.Append('{');
                    for (int i = 0; i < j.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        Quote(sb, j[i].Key);
                        sb.Append(':');
                        Write(sb, j[i].Value);
                    }
                    sb.Append('}');
                    return;
                case IDictionary dictionary:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Quote(sb, System.Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                        sb.Append(':');
                        Write(sb, entry.Value);
                    }
                    sb.Append('}');
                    return;
                }
                case IEnumerable items:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (object item in items)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    return;
                }
                default: Quote(sb, value.ToString()); return;
            }
        }

        private static void Number(StringBuilder sb, double d) =>
            sb.Append(double.IsFinite(d) ? Math.Round(d, 4).ToString(CultureInfo.InvariantCulture) : "null");

        private static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            sb.Append('"');
        }
    }
}
