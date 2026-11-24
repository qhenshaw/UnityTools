using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace UnityTools.Placement.Editor
{
    /// <summary>
    /// Base for the Graph Toolkit side of a placement node. A subclass declares the ports the graph window shows and
    /// which runtime <see cref="PlacementNode"/> it builds.
    /// <list type="bullet">
    /// <item>Point ports carry <see cref="PointData"/> and must be declared in the runtime node's input and output order.</item>
    /// <item>Setting ports are value inputs with an inline field, declared after the point ports with
    /// <see cref="AddSetting{T}"/>. A setting called "Min Angle" sets the runtime field MinAngle, and its default
    /// comes from a fresh runtime node. Wiring a Blackboard variable into it makes the setting a graph parameter.</item>
    /// <item>There are no node options: Graph Toolkit can't set those from code, and every setting being a port keeps
    /// graphs scriptable (see <see cref="PlacementGraphAuthoring"/>).</item>
    /// </list>
    /// </summary>
    [Serializable]
    internal abstract class PlacementGraphNode : Node
    {
        public const string InPort = "In";
        public const string OutPort = "Out";
        public const string RejectedPort = "Rejected";

        // A stable id, so a node keeps its random stream when other nodes are added or removed.
        [SerializeField] private string _id;

        [NonSerialized] private object _defaults;

        /// <summary>The node's stable id, created the first time its ports are defined.</summary>
        public string Id => _id;

        /// <summary>Gives the node a new id, e.g. when it turns out to be a copy of another node (copy and paste copies the id).</summary>
        internal void RenewId() => _id = Guid.NewGuid().ToString("N");

        /// <summary>Name shown in logs and the component's node list: the title from <see cref="NodeAttribute"/>, as in the graph window.</summary>
        public virtual string DisplayName => TitleOf(GetType());

        /// <summary>A fresh settings object (the runtime node, or a <see cref="PrefabEntry"/>), which holds every setting's default.</summary>
        protected object Defaults => _defaults ??= CreateSettingsTarget();

        /// <summary>
        /// False for nodes that don't run, such as Prefab Entry: they describe values that another node reads when the
        /// graph is compiled.
        /// </summary>
        internal virtual bool IsRuntimeNode => true;

        /// <summary>The object setting ports write to: the runtime node, or for value nodes their value object.</summary>
        protected virtual object CreateSettingsTarget() => CreateRuntimeInstance();

        /// <summary>The input that Prefab Entry nodes are wired into, on nodes that pick prefabs.</summary>
        public const string EntriesPort = "Entries";

        /// <summary>
        /// The title a node type shows in the graph window: its <see cref="NodeAttribute"/> title, or the class name
        /// without "Node", split into words.
        /// </summary>
        public static string TitleOf(Type type)
        {
            var attribute = (NodeAttribute)Attribute.GetCustomAttribute(type, typeof(NodeAttribute), false);
            if (attribute != null && !string.IsNullOrEmpty(attribute.Title)) return attribute.Title;
            string name = type.Name;
            if (name.EndsWith("Node")) name = name.Substring(0, name.Length - 4);
            return UnityEditor.ObjectNames.NicifyVariableName(name);
        }

        /// <summary>The runtime field a setting port sets: its name without spaces ("Min Angle" sets MinAngle).</summary>
        public static string FieldNameOf(string settingName) => settingName.Replace(" ", "");

        protected sealed override void OnDefinePorts(IPortDefinitionContext context)
        {
            if (string.IsNullOrEmpty(_id)) _id = Guid.NewGuid().ToString("N");
            DefinePointPorts(context);
            DefineSettings(context);
        }

        /// <summary>Declare point inputs, then point outputs, in runtime order. Use <see cref="AddPointInput"/> and <see cref="AddPointOutput"/>.</summary>
        protected abstract void DefinePointPorts(IPortDefinitionContext context);

        /// <summary>Declare setting ports with <see cref="AddSetting{T}"/>.</summary>
        protected virtual void DefineSettings(IPortDefinitionContext context) { }

        /// <summary>Creates the runtime node with its default settings (null for nodes that don't run).</summary>
        protected abstract PlacementNode CreateRuntimeInstance();

        /// <summary>A port as declared by a node type, for <see cref="Describe"/>.</summary>
        internal sealed class PortDescription
        {
            public string Name;
            public Type DataType;
            public bool IsInput;
            public object DefaultValue;
            public string Tooltip;
        }

        // Set while Describe runs: the Add methods record ports here instead of building them.
        [NonSerialized] private List<PortDescription> _describing;

        /// <summary>The ports this node type declares, in order, without adding it to a graph.</summary>
        internal List<PortDescription> Describe()
        {
            _describing = new List<PortDescription>();
            try
            {
                DefinePointPorts(null);
                DefineSettings(null);
                return _describing;
            }
            finally
            {
                _describing = null;
            }
        }

        /// <summary>The runtime node type this node builds (or the value type, for nodes that don't run).</summary>
        internal Type RuntimeType => Defaults.GetType();

        /// <summary>An input for links from value nodes (such as Prefab Entry), declared with the point ports.</summary>
        protected void AddLinkInput<T>(IPortDefinitionContext context, string name, string tooltip = null)
        {
            if (_describing != null) { _describing.Add(new PortDescription { Name = name, DataType = typeof(T), IsInput = true, Tooltip = tooltip }); return; }
            IInputPortBuilder<T> builder = context.AddInputPort<T>(name);
            if (!string.IsNullOrEmpty(tooltip)) builder = builder.WithTooltip(tooltip);
            builder.Build();
        }

        /// <summary>An output for a value node's link (such as a Prefab Entry's Entry).</summary>
        protected void AddLinkOutput<T>(IPortDefinitionContext context, string name)
        {
            if (_describing != null) { _describing.Add(new PortDescription { Name = name, DataType = typeof(T), IsInput = false }); return; }
            context.AddOutputPort<T>(name).Build();
        }

        protected void AddPointInput(IPortDefinitionContext context, string name)
        {
            if (_describing != null) _describing.Add(new PortDescription { Name = name, DataType = typeof(PointData), IsInput = true });
            else context.AddInputPort<PointData>(name).Build();
        }

        protected void AddPointOutput(IPortDefinitionContext context, string name)
        {
            if (_describing != null) _describing.Add(new PortDescription { Name = name, DataType = typeof(PointData), IsInput = false });
            else context.AddOutputPort<PointData>(name).Build();
        }

        /// <summary>A setting port: a value input with an inline field, defaulting to the runtime node's field.</summary>
        protected void AddSetting<T>(IPortDefinitionContext context, string name, string tooltip = null)
        {
            FieldInfo field = PlacementExecutor.FindField(Defaults.GetType(), FieldNameOf(name));
            if (field == null || field.FieldType != typeof(T))
                throw new InvalidOperationException($"{GetType().Name}: setting '{name}' needs a public {typeof(T).Name} field {FieldNameOf(name)} on {Defaults.GetType().Name}.");
            if (_describing != null)
            {
                _describing.Add(new PortDescription { Name = name, DataType = typeof(T), IsInput = true, DefaultValue = field.GetValue(Defaults), Tooltip = tooltip });
                return;
            }
            IInputPortBuilder<T> builder = context.AddInputPort<T>(name).WithDefaultValue((T)field.GetValue(Defaults));
            if (typeof(T) == typeof(float) || typeof(T) == typeof(int) || typeof(T) == typeof(Vector3)) builder = builder.Delayed();
            if (!string.IsNullOrEmpty(tooltip)) builder = builder.WithTooltip(tooltip);
            builder.Build();
        }

        /// <summary>True for a port that carries points rather than a setting.</summary>
        public static bool IsPointPort(IPort port) => port.DataType == typeof(PointData);

        /// <summary>True for a port that links value nodes (Prefab Entry, Entry Attribute) rather than holding a setting.</summary>
        public static bool IsLinkPort(IPort port) => port.DataType == typeof(PrefabEntryLink) || port.DataType == typeof(EntryAttributeLink);

        /// <summary>True for a type that is a link rather than a setting value.</summary>
        internal static bool IsLinkType(Type type) => type == typeof(PrefabEntryLink) || type == typeof(EntryAttributeLink);

        /// <summary>The setting ports, in order.</summary>
        public IEnumerable<IPort> GetSettingPorts() => GetInputPorts().Where(port => !IsPointPort(port) && !IsLinkPort(port));

        /// <summary>
        /// Builds the runtime node: defaults, then every setting port's value, resolved by
        /// <paramref name="resolver"/>. Settings that read a graph parameter are added to <paramref name="bindings"/>
        /// as (field, parameter name).
        /// </summary>
        public PlacementNode CreateRuntimeNode(SettingResolver resolver = null, List<(string field, string parameter)> bindings = null)
        {
            resolver ??= SettingResolver.Default;
            PlacementNode runtime = CreateRuntimeInstance();
            ApplySettings(runtime, resolver, bindings, "");
            FieldInfo entriesField = PlacementExecutor.FindField(runtime.GetType(), EntriesPort);
            IPort entriesPort = GetInputPortByName(EntriesPort);
            if (entriesField != null && entriesPort != null && entriesField.GetValue(runtime) is List<PrefabEntry> entries)
            {
                entries.Clear();
                foreach (PrefabEntryNode entryNode in PrefabEntryNode.Chain<PrefabEntryNode>(entriesPort, EntriesPort))
                    entries.Add(entryNode.BuildEntry(resolver, bindings, $"{EntriesPort}[{entries.Count}]."));
            }
            return runtime;
        }

        /// <summary>
        /// Writes every setting port's value into <paramref name="target"/>'s fields. Settings that read a graph
        /// parameter are added to <paramref name="bindings"/> with <paramref name="prefix"/> before the field name.
        /// </summary>
        internal void ApplySettings(object target, SettingResolver resolver, List<(string field, string parameter)> bindings, string prefix)
        {
            resolver ??= SettingResolver.Default;
            foreach (IPort port in GetSettingPorts())
            {
                FieldInfo field = PlacementExecutor.FindField(target.GetType(), FieldNameOf(port.Name));
                if (field == null) continue;
                if (!resolver.TryResolve(port, out object value, out string parameter)) continue;
                if (value != null && !field.FieldType.IsInstanceOfType(value)) continue;
                if (value == null && field.FieldType.IsValueType) continue;
                field.SetValue(target, value);
                if (parameter != null) bindings?.Add((prefix + field.Name, parameter));
            }
        }


        /// <summary>Problems that should be reported in the graph window, e.g. a spawner with no palette.</summary>
        public virtual void Validate(List<string> problems) { }
    }

    /// <summary>
    /// Works out a setting port's value: its inline field when unconnected, or what's wired into it (a constant, or a
    /// Blackboard variable's default). The variable also names the graph parameter the setting reads.
    /// </summary>
    internal class SettingResolver
    {
        public static readonly SettingResolver Default = new SettingResolver();

        private static readonly MethodInfo PortValue = typeof(SettingResolver).GetMethod(nameof(PortValueOf), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo VariableValue = typeof(SettingResolver).GetMethod(nameof(VariableValueOf), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo ConstantValue = typeof(SettingResolver).GetMethod(nameof(ConstantValueOf), BindingFlags.NonPublic | BindingFlags.Static);

        public virtual bool TryResolve(IPort port, out object value, out string parameter)
        {
            parameter = null;
            value = null;
            if (!port.IsConnected) return Invoke(PortValue, port.DataType, port, out value);

            IPort source = port.FirstConnectedPort;
            INode node = source?.GetNode();
            switch (node)
            {
                case IVariableNode variableNode:
                    return TryResolveVariable(variableNode.Variable, port.DataType, out value, out parameter);
                case IConstantNode constant:
                    return Invoke(ConstantValue, port.DataType, constant, out value);
                default:
                    return false;
            }
        }

        /// <summary>A variable's default; a Local or Input variable also names the parameter it is.</summary>
        protected virtual bool TryResolveVariable(IVariable variable, Type type, out object value, out string parameter)
        {
            parameter = variable.VariableKind != VariableKind.Output ? variable.Name : null;
            return Invoke(VariableValue, type, variable, out value);
        }

        protected static bool Invoke(MethodInfo method, Type type, object target, out object value)
        {
            var arguments = new object[] { target, null };
            bool found = (bool)method.MakeGenericMethod(type).Invoke(null, arguments);
            value = arguments[1];
            return found;
        }

        private static bool PortValueOf<T>(IPort port, out object value)
        {
            bool found = port.TryGetValue(out T typed);
            value = typed;
            return found;
        }

        private static bool VariableValueOf<T>(IVariable variable, out object value)
        {
            bool found = variable.TryGetDefaultValue(out T typed);
            value = typed;
            return found;
        }

        private static bool ConstantValueOf<T>(IConstantNode constant, out object value)
        {
            bool found = constant.TryGetValue(out T typed);
            value = typed;
            return found;
        }
    }
}
