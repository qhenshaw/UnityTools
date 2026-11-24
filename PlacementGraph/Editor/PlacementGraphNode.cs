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
    /// <item>Options (shown in the node's inspector) are for choices that aren't parameters, such as a mode enum.</item>
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

        [NonSerialized] private PlacementNode _defaults;

        /// <summary>The node's stable id, created the first time its ports are defined.</summary>
        public string Id => _id;

        /// <summary>Name shown in logs and the component's node list: the title from <see cref="NodeAttribute"/>, as in the graph window.</summary>
        public virtual string DisplayName => TitleOf(GetType());

        /// <summary>A fresh runtime node, which holds every setting's default.</summary>
        protected PlacementNode Defaults => _defaults ??= CreateRuntimeInstance();

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

        /// <summary>Creates the runtime node with its default settings.</summary>
        protected abstract PlacementNode CreateRuntimeInstance();

        /// <summary>Copies options (not settings) to the runtime node, e.g. a mode enum.</summary>
        protected virtual void ApplyOptions(PlacementNode runtime) { }

        protected static void AddPointInput(IPortDefinitionContext context, string name) =>
            context.AddInputPort<PointData>(name).Build();

        protected static void AddPointOutput(IPortDefinitionContext context, string name) =>
            context.AddOutputPort<PointData>(name).Build();

        /// <summary>A setting port: a value input with an inline field, defaulting to the runtime node's field.</summary>
        protected void AddSetting<T>(IPortDefinitionContext context, string name, string tooltip = null)
        {
            FieldInfo field = PlacementExecutor.FindField(Defaults.GetType(), FieldNameOf(name));
            if (field == null || field.FieldType != typeof(T))
                throw new InvalidOperationException($"{GetType().Name}: setting '{name}' needs a public {typeof(T).Name} field {FieldNameOf(name)} on {Defaults.GetType().Name}.");
            IInputPortBuilder<T> builder = context.AddInputPort<T>(name).WithDefaultValue((T)field.GetValue(Defaults));
            if (typeof(T) == typeof(float) || typeof(T) == typeof(int) || typeof(T) == typeof(Vector3)) builder = builder.Delayed();
            if (!string.IsNullOrEmpty(tooltip)) builder = builder.WithTooltip(tooltip);
            builder.Build();
        }

        /// <summary>True for a port that carries points rather than a setting.</summary>
        public static bool IsPointPort(IPort port) => port.DataType == typeof(PointData);

        /// <summary>The setting ports, in order.</summary>
        public IEnumerable<IPort> GetSettingPorts() => GetInputPorts().Where(port => !IsPointPort(port));

        /// <summary>
        /// Builds the runtime node: defaults, then options, then every setting port's value, resolved by
        /// <paramref name="resolver"/>. Settings that read a graph parameter are added to <paramref name="bindings"/>
        /// as (field, parameter name).
        /// </summary>
        public PlacementNode CreateRuntimeNode(SettingResolver resolver = null, List<(string field, string parameter)> bindings = null)
        {
            resolver ??= SettingResolver.Default;
            PlacementNode runtime = CreateRuntimeInstance();
            ApplyOptions(runtime);
            foreach (IPort port in GetSettingPorts())
            {
                FieldInfo field = PlacementExecutor.FindField(runtime.GetType(), FieldNameOf(port.Name));
                if (field == null) continue;
                if (!resolver.TryResolve(port, out object value, out string parameter)) continue;
                if (value != null && !field.FieldType.IsInstanceOfType(value)) continue;
                if (value == null && field.FieldType.IsValueType) continue;
                field.SetValue(runtime, value);
                if (parameter != null) bindings?.Add((field.Name, parameter));
            }
            return runtime;
        }

        /// <summary>Reads an option's value, or <paramref name="fallback"/> when it's missing or a different type.</summary>
        protected T GetOption<T>(string name, T fallback)
        {
            INodeOption option = GetNodeOptionByName(name);
            return option != null && option.TryGetValue(out T value) ? value : fallback;
        }

        /// <summary>Problems that should be reported in the graph window, e.g. a spawner with no palette.</summary>
        public virtual void Validate(List<string> problems) { }
    }

    /// <summary>
    /// Works out a setting port's value: its inline field when unconnected, or what's wired into it (a constant, or a
    /// Blackboard variable's default). A variable of kind Input also names the graph parameter the setting reads.
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

        /// <summary>A variable's default; an Input variable also names the parameter.</summary>
        protected virtual bool TryResolveVariable(IVariable variable, Type type, out object value, out string parameter)
        {
            parameter = variable.VariableKind == VariableKind.Input ? variable.Name : null;
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
