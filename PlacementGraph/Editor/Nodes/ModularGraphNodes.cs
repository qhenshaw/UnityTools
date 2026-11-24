using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace UnityTools.Placement.Editor
{
    // Graph Toolkit nodes for modular building. Same rules as PlacementGraphNodes.cs.

    /// <summary>The data type of the wire from a Module node to Subdivide's Modules input.</summary>
    public sealed class ModuleLink { }

    /// <summary>
    /// One building module for Subdivide: a prefab and the symbol the grammar names it by. Modules form a chain like
    /// Prefab Entry nodes: each one's Module output into the next one's Modules input, the last into Subdivide's Modules.
    /// </summary>
    [Node("Modular", null, "Module")]
    [Serializable]
    internal class ModuleNode : PlacementGraphNode
    {
        public const string ModulePort = "Module";

        internal override bool IsRuntimeNode => false;

        protected override PlacementNode CreateRuntimeInstance() => null;

        protected override object CreateSettingsTarget() => new ModuleDefinition();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddLinkInput<ModuleLink>(context, ModulesPort, "The previous Module in the chain, if any.");
            AddLinkOutput<ModuleLink>(context, ModulePort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<GameObject>(context, "Prefab");
            AddSetting<string>(context, "Symbol", "The name the grammar uses for this module, e.g. Wall.");
            AddSetting<float>(context, "Weight", "For a choice like [Wall,Window]: the chance is the weight over the others' total.");
            AddSetting<float>(context, "Size", "Length along the subdivision axis in metres; 0 measures it from the prefab's meshes.");
            AddSetting<Vector3>(context, "Position Offset", "In the slot's local space.");
            AddSetting<Vector3>(context, "Rotation Offset", "Euler degrees, after the slot's rotation.");
        }

        public override void Validate(List<string> problems)
        {
            IPort prefab = GetInputPortByName("Prefab");
            if (prefab != null && !prefab.IsConnected && (!prefab.TryGetValue(out GameObject value) || value == null))
                problems.Add("A Module has no Prefab.");
        }
    }

    [Node("Modular", null, "Subdivide")]
    [Serializable]
    internal class SubdivideNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.SubdivideNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddLinkInput<ModuleLink>(context, ModulesPort, "Module nodes (a chain): the modules the grammar can use.");
            AddPointOutput(context, OutPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<string>(context, "Grammar", "[A] = one module, [A]* = repeat to fill, [A]+ = at least one, [A,B] = a weighted choice. E.g. [Corner][Wall]*[Door][Wall]*[Corner].");
            AddSetting<Placement.Subdivide.SourceMode>(context, "Source", "The input points as a polyline, the volume's bottom edges, or the spline's knots.");
            AddSetting<bool>(context, "Closed", "Input Points: also join the last point to the first.");
            AddSetting<Placement.Subdivide.Axis>(context, "Module Axis", "Which local axis of the modules runs along the segment.");
            AddSetting<bool>(context, "Scale To Fit", "Scale modules along their length to fill each segment exactly; off leaves a gap at the end.");
            AddSetting<float>(context, "Corner Trim", "Shorten segments by this much at each corner, to leave room for corner modules.");
            AddSetting<float>(context, "Corner Angle", "Turns of at least this many degrees count as corners.");
        }

        public override void Validate(List<string> problems)
        {
            IPort grammar = GetInputPortByName("Grammar");
            if (grammar == null || grammar.IsConnected || !grammar.TryGetValue(out string text)) return;
            if (!ModuleGrammar.TryParse(text, out _, out string error)) problems.Add($"Subdivide grammar: {error}");
            IPort modules = GetInputPortByName(ModulesPort);
            if (modules != null && !modules.IsConnected) problems.Add("Subdivide has no modules. Chain Module nodes into Modules.");
        }
    }

    [Node("Modular", null, "Corner")]
    [Serializable]
    internal class CornerNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.CornerNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<Placement.Subdivide.SourceMode>(context, "Source", "The input points as a polyline, the volume's bottom edges, or the spline's knots.");
            AddSetting<bool>(context, "Closed", "Input Points: also join the last point to the first.");
            AddSetting<float>(context, "Min Angle", "Only turns of at least this many degrees are corners.");
        }
    }

    [Node("Modular", null, "Create Grid")]
    [Serializable]
    internal class CreateGridNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.CreateGridNode();

        protected override void DefinePointPorts(IPortDefinitionContext context) => AddPointOutput(context, OutPort);

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<Vector3>(context, "Cell Size", "Distance between points along each axis (Y in 3D only).");
            AddSetting<Vector3>(context, "Offset", "From the volume's minimum corner.");
            AddSetting<bool>(context, "Three Dimensional", "Fill the volume in Y too.");
        }
    }

    [Node("Modular", null, "Snap To Grid")]
    [Serializable]
    internal class SnapToGridNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.SnapToGridNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<Vector3>(context, "Cell Size");
            AddSetting<Vector3>(context, "Offset");
            AddSetting<bool>(context, "Snap Y");
            AddSetting<float>(context, "Rotation Step", "Also snap the rotation about Y to multiples of this many degrees (0 = off).");
        }
    }
}
