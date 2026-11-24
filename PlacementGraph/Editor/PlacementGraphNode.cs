using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace UnityTools.Placement.Editor
{
    /// <summary>
    /// Base for the Graph Toolkit side of a placement node. A subclass declares the ports and options the graph
    /// window shows and builds the matching runtime <see cref="PlacementNode"/>. Its point ports must be declared in
    /// the same order as the runtime node's inputs and outputs.
    /// </summary>
    [Serializable]
    internal abstract class PlacementGraphNode : Node
    {
        public const string InPort = "In";
        public const string OutPort = "Out";
        public const string RejectedPort = "Rejected";

        // A stable id, so a node keeps its random stream when other nodes are added or removed.
        [SerializeField] private string _id;

        /// <summary>The node's stable id, created the first time its ports are defined.</summary>
        public string Id => _id;

        /// <summary>Name shown in logs and the component's node list.</summary>
        public virtual string DisplayName
        {
            get
            {
                string name = GetType().Name;
                return name.EndsWith("Node") ? name.Substring(0, name.Length - 4) : name;
            }
        }

        protected sealed override void OnDefinePorts(IPortDefinitionContext context)
        {
            if (string.IsNullOrEmpty(_id)) _id = Guid.NewGuid().ToString("N");
            DefinePointPorts(context);
        }

        /// <summary>Declare point inputs, then point outputs, in runtime order. Use <see cref="AddPointInput"/> and <see cref="AddPointOutput"/>.</summary>
        protected abstract void DefinePointPorts(IPortDefinitionContext context);

        protected static void AddPointInput(IPortDefinitionContext context, string name) =>
            context.AddInputPort<PointData>(name).Build();

        protected static void AddPointOutput(IPortDefinitionContext context, string name) =>
            context.AddOutputPort<PointData>(name).Build();

        /// <summary>Builds the runtime node from this node's options.</summary>
        public abstract PlacementNode CreateRuntimeNode();

        /// <summary>Reads an option's value, or <paramref name="fallback"/> when it's missing or a different type.</summary>
        protected T GetOption<T>(string name, T fallback)
        {
            INodeOption option = GetNodeOptionByName(name);
            return option != null && option.TryGetValue(out T value) ? value : fallback;
        }

        /// <summary>Problems that should be reported in the graph window, e.g. a spawner with no palette.</summary>
        public virtual void Validate(List<string> problems) { }
    }
}
