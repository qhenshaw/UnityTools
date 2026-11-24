using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace UnityTools.Placement.Editor
{
    /// <summary>The data type of the wire from a Prefab Entry node to an Entries input. It carries no data at run time.</summary>
    public sealed class PrefabEntryLink { }

    /// <summary>The data type of the wire from an Entry Attribute node to a Prefab Entry's Attributes input.</summary>
    public sealed class EntryAttributeLink { }

    /// <summary>
    /// One prefab for Select Prefab or Spawn Prefabs. Entries form a chain: wire one entry's Entry output into the next
    /// entry's Entries input, and the last entry into the Select Prefab or Spawn Prefabs node's Entries input (Graph
    /// Toolkit inputs take one wire each). Every field is a setting, so parameters can drive them. It doesn't run: the
    /// node the chain ends in reads it when the graph is compiled.
    /// </summary>
    [Node("Spawners", null, "Prefab Entry")]
    [Serializable]
    internal class PrefabEntryNode : PlacementGraphNode
    {
        public const string EntryPort = "Entry";
        public const string AttributesPort = "Attributes";

        internal override bool IsRuntimeNode => false;

        protected override PlacementNode CreateRuntimeInstance() => null;

        protected override object CreateSettingsTarget() => new PrefabEntry();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddLinkInput<PrefabEntryLink>(context, EntriesPort, "The previous Prefab Entry in the chain, if any.");
            AddLinkInput<EntryAttributeLink>(context, AttributesPort, "Entry Attribute nodes (a chain): values written onto the points this entry is picked for.");
            AddLinkOutput<PrefabEntryLink>(context, EntryPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<GameObject>(context, "Prefab");
            AddSetting<float>(context, "Weight", "The chance of this entry is its weight divided by the total of all entries.");
            AddSetting<Vector3>(context, "Position Offset", "Moves the prefab from the point, in the point's local space.");
            AddSetting<Vector3>(context, "Rotation Offset", "Euler degrees, after the point's rotation.");
            AddSetting<Vector3>(context, "Rotation Random Min", "A random extra rotation per copy, in Euler degrees.");
            AddSetting<Vector3>(context, "Rotation Random Max");
            AddSetting<bool>(context, "Random Scale", "A random uniform scale between Scale Min and Scale Max per copy.");
            AddSetting<float>(context, "Scale Min");
            AddSetting<float>(context, "Scale Max");
        }

        /// <summary>The entry this node describes, with its settings and the attributes wired into it.</summary>
        internal PrefabEntry BuildEntry(SettingResolver resolver, List<(string field, string parameter)> bindings, string prefix)
        {
            var entry = new PrefabEntry();
            ApplySettings(entry, resolver, bindings, prefix);
            foreach (EntryAttributeNode attributeNode in Chain<EntryAttributeNode>(GetInputPortByName(AttributesPort), AttributesPort))
            {
                var attribute = new EntryAttribute();
                attributeNode.ApplySettings(attribute, resolver, bindings, $"{prefix}{nameof(PrefabEntry.Attributes)}[{entry.Attributes.Count}].");
                entry.Attributes.Add(attribute);
            }
            return entry;
        }

        /// <summary>
        /// The value nodes of type <typeparamref name="T"/> chained into <paramref name="input"/>, first to last: the
        /// node wired into the input is the last, and each node's <paramref name="chainPort"/> leads to the one before.
        /// </summary>
        internal static List<T> Chain<T>(IPort input, string chainPort) where T : PlacementGraphNode
        {
            var chain = new List<T>();
            var visited = new HashSet<INode>();
            IPort current = input;
            while (current != null && current.IsConnected)
            {
                if (current.FirstConnectedPort?.GetNode() is not T node || !visited.Add(node)) break;
                chain.Add(node);
                current = node.GetInputPortByName(chainPort);
            }
            chain.Reverse();
            return chain;
        }

        public override void Validate(List<string> problems)
        {
            IPort prefab = GetInputPortByName("Prefab");
            if (prefab != null && !prefab.IsConnected && (!prefab.TryGetValue(out GameObject value) || value == null))
                problems.Add("A Prefab Entry has no Prefab.");
            IPort output = GetOutputPortByName(EntryPort);
            if (output != null && !output.IsConnected)
                problems.Add("A Prefab Entry isn't wired into a Select Prefab or Spawn Prefabs node's Entries.");
        }
    }

    /// <summary>
    /// A named value a Prefab Entry writes onto the points it is picked for. Wire it into the entry's Attributes input;
    /// for more than one, chain them through each other's Attributes input.
    /// </summary>
    [Node("Spawners", null, "Entry Attribute")]
    [Serializable]
    internal class EntryAttributeNode : PlacementGraphNode
    {
        internal override bool IsRuntimeNode => false;

        protected override PlacementNode CreateRuntimeInstance() => null;

        protected override object CreateSettingsTarget() => new EntryAttribute();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddLinkInput<EntryAttributeLink>(context, PrefabEntryNode.AttributesPort, "The previous Entry Attribute in the chain, if any.");
            AddLinkOutput<EntryAttributeLink>(context, "Attribute");
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<string>(context, "Name", "The attribute's name on the points.");
            AddSetting<PointAttributeType>(context, "Type", "Which value is written.");
            AddSetting<float>(context, "Float Value");
            AddSetting<int>(context, "Int Value");
            AddSetting<Vector3>(context, "Vector3 Value");
            AddSetting<UnityEngine.Object>(context, "Object Value");
        }
    }
}
