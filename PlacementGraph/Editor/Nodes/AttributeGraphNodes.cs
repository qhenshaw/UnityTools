using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace UnityTools.Placement.Editor
{
    // Graph Toolkit nodes for the attribute nodes. Same rules as PlacementGraphNodes.cs.

    [Node("Attributes", null, "Select Prefab")]
    [Serializable]
    internal class SelectPrefabNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.SelectPrefabNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddLinkInput<PrefabEntryLink>(context, EntriesPort, "Prefab Entry nodes: the prefabs to pick from, by weight.");
            AddPointOutput(context, OutPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<bool>(context, "Write Entry Values", "Also write the entry's offset, rotation, scale and custom attributes.");
        }

        public override void Validate(List<string> problems)
        {
            IPort entries = GetInputPortByName(EntriesPort);
            if (entries != null && !entries.IsConnected) problems.Add("Select Prefab has no entries. Wire Prefab Entry nodes into Entries.");
        }
    }

    [Node("Attributes", null, "Filter By Attribute")]
    [Serializable]
    internal class FilterByAttributeNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.FilterByAttributeNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
            AddPointOutput(context, RejectedPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<Placement.FilterByAttributeNode.Comparison>(context, "Compare", "Prefab Is keeps points whose Prefab attribute is Object.");
            AddSetting<string>(context, "Attribute", "The attribute to test (not used by Prefab Is).");
            AddSetting<float>(context, "Value", "The number to compare with; the minimum for In Range.");
            AddSetting<float>(context, "Max", "The maximum for In Range.");
            AddSetting<GameObject>(context, "Object", "The prefab Prefab Is, Equal and Not Equal compare with.");
        }
    }

    [Node("Attributes", null, "Set Attribute")]
    [Serializable]
    internal class SetAttributeNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.SetAttributeNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<string>(context, "Attribute", "The attribute to write.");
            AddSetting<Placement.SetAttributeNode.ValueType>(context, "Type", "Which of the Float, Int and Vector3 settings is used.");
            AddSetting<bool>(context, "Random", "A random value between Min and Max per point, instead of Min.");
            AddSetting<float>(context, "Float Min");
            AddSetting<float>(context, "Float Max");
            AddSetting<int>(context, "Int Min");
            AddSetting<int>(context, "Int Max", "Included.");
            AddSetting<Vector3>(context, "Vector3 Min");
            AddSetting<Vector3>(context, "Vector3 Max");
        }
    }
}
