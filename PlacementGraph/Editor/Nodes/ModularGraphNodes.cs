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
            AddPointInput(context, OpeningsPort);
            AddLinkInput<ModuleLink>(context, ModulesPort, "Module nodes (a chain): the modules the grammar can use.");
            AddPointOutput(context, OutPort);
            AddPointOutput(context, GapsPort);
        }

        /// <summary>Optional: Placement Zones (from Scene Input) or points where the walls open.</summary>
        public const string OpeningsPort = "Openings";

        /// <summary>One point per opening, at its centre along the wall, with Opening, Width and bounds.</summary>
        public const string GapsPort = "Gaps";

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<string>(context, "Grammar", "[A] = one module, [A]* = repeat to fill, [A]+ = at least one, [A,B] = a weighted choice. E.g. [Corner][Wall]*[Door][Wall]*[Corner].");
            AddSetting<Placement.Subdivide.SourceMode>(context, "Source", "The input points as a polyline, the volume's bottom edges, or the spline's knots.");
            AddSetting<bool>(context, "Closed", "Input Points: also join the last point to the first.");
            AddSetting<Placement.Subdivide.Axis>(context, "Module Axis", "Which local axis of the modules runs along the segment.");
            AddSetting<bool>(context, "Scale To Fit", "Scale modules along their length to fill each segment exactly; off leaves a gap at the end.");
            AddSetting<float>(context, "Corner Trim", "Shorten segments by this much at each corner, to leave room for corner modules.");
            AddSetting<float>(context, "Corner Angle", "Turns of at least this many degrees count as corners.");
            AddSetting<Placement.Subdivide.OpeningMode>(context, "Opening Mode", "Openings: leave them empty (Gap), or place the Opening Symbol's module in them (Module).");
            AddSetting<string>(context, "Opening Symbol", "Openings, Module mode: the symbol of the module placed in each opening, e.g. Door.");
            AddSetting<float>(context, "Opening Width", "Openings: how wide an opening from a plain point is, in metres. Zones use the part of the wall they cross.");
            AddSetting<bool>(context, "Fit To Module", "Openings: make each opening exactly as wide as the Opening Symbol's module, centred on the zone or point.");
            AddSetting<float>(context, "Max Distance", "Openings: how far a zone or point can be from a wall, in metres, and still open it.");
        }

        public override void Validate(List<string> problems)
        {
            ValidateOpenings(problems);
            IPort grammar = GetInputPortByName("Grammar");
            if (grammar == null || grammar.IsConnected || !grammar.TryGetValue(out string text)) return;
            if (!ModuleGrammar.TryParse(text, out _, out string error)) problems.Add($"Subdivide grammar: {error}");
            IPort modules = GetInputPortByName(ModulesPort);
            if (modules != null && !modules.IsConnected) problems.Add("Subdivide has no modules. Chain Module nodes into Modules.");
        }

        /// <summary>With openings wired in and wanting a module (Module mode or Fit To Module), one with the Opening Symbol must be chained in.</summary>
        private void ValidateOpenings(List<string> problems)
        {
            IPort openings = GetInputPortByName(OpeningsPort);
            if (openings == null || !openings.IsConnected) return;
            IPort mode = GetInputPortByName("Opening Mode");
            IPort fit = GetInputPortByName("Fit To Module");
            bool wantsModule = (mode != null && !mode.IsConnected && mode.TryGetValue(out Placement.Subdivide.OpeningMode value) && value == Placement.Subdivide.OpeningMode.Module) ||
                               (fit != null && !fit.IsConnected && fit.TryGetValue(out bool fitValue) && fitValue);
            IPort symbolPort = GetInputPortByName("Opening Symbol");
            if (!wantsModule || symbolPort == null || symbolPort.IsConnected || !symbolPort.TryGetValue(out string symbol)) return;
            foreach (ModuleNode module in PrefabEntryNode.Chain<ModuleNode>(GetInputPortByName(ModulesPort), ModulesPort))
            {
                IPort moduleSymbol = module.GetInputPortByName("Symbol");
                if (moduleSymbol != null && moduleSymbol.TryGetValue(out string text) && text == symbol) return;
            }
            problems.Add($"Subdivide openings use the module '{symbol}' (Opening Symbol), but no Module in its chain has that symbol.");
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

    [Node("Modular", null, "Assembly")]
    [Serializable]
    internal class AssemblyNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.AssemblyNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<GameObject>(context, "Prefab", "A prefab whose direct children are the parts stamped at every point.");
            AddSetting<float>(context, "Drop Chance", "The chance each part is left out at a point.");
            AddSetting<float>(context, "Rotation Jitter", "Turns each part about its up axis by up to this many degrees either way.");
            AddSetting<float>(context, "Scale Jitter", "Scales each part by up to this fraction either way.");
        }
    }

    [Node("Spawners", null, "Spawn Spline Mesh")]
    [Serializable]
    internal class SpawnSplineMeshNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.SpawnSplineMeshNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<Mesh>(context, "Mesh", "Needs Read/Write enabled.");
            AddSetting<Material>(context, "Material");
            AddSetting<Placement.Subdivide.SourceMode>(context, "Source", "The input points as a polyline, the volume's bottom edges, or the spline's knots.");
            AddSetting<bool>(context, "Closed", "Input Points: also join the last point to the first.");
            AddSetting<Placement.Subdivide.Axis>(context, "Mesh Axis", "Which local axis of the mesh runs along the line.");
            AddSetting<bool>(context, "Stretch To Fit", "Stretch the copies to cover the line exactly.");
            AddSetting<UnityEngine.Rendering.ShadowCastingMode>(context, "Shadow Casting");
        }
    }
}
