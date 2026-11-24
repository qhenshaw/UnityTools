using System;
using Unity.GraphToolkit.Editor;

namespace UnityTools.Placement.Editor
{
    // Graph Toolkit nodes for the second set of runtime nodes. Same rules as PlacementGraphNodes.cs: point ports in
    // the runtime node's order, option defaults from a fresh runtime node.

    [Node("Filters", null, "Height Filter")]
    [Serializable]
    internal class HeightFilterNode : PlacementGraphNode
    {
        private static readonly Placement.HeightFilterNode Defaults = new Placement.HeightFilterNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
            AddPointOutput(context, RejectedPort);
        }

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Min").WithDefaultValue(Defaults.Min).Delayed();
            context.AddOption<float>("Max").WithDefaultValue(Defaults.Max).Delayed();
        }

        public override PlacementNode CreateRuntimeNode() => new Placement.HeightFilterNode
        {
            Min = GetOption("Min", Defaults.Min),
            Max = GetOption("Max", Defaults.Max)
        };
    }

    [Node("Filters", null, "Self Pruning")]
    [Serializable]
    internal class SelfPruningNode : PlacementGraphNode
    {
        private static readonly Placement.SelfPruningNode Defaults = new Placement.SelfPruningNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
            AddPointOutput(context, RejectedPort);
        }

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Radius").WithDefaultValue(Defaults.Radius).Delayed();
        }

        public override PlacementNode CreateRuntimeNode() => new Placement.SelfPruningNode
        {
            Radius = GetOption("Radius", Defaults.Radius)
        };
    }

    [Node("Filters", null, "Difference")]
    [Serializable]
    internal class DifferenceNode : PlacementGraphNode
    {
        public const string ExcludePort = "Exclude";
        private static readonly Placement.DifferenceNode Defaults = new Placement.DifferenceNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointInput(context, ExcludePort);
            AddPointOutput(context, OutPort);
            AddPointOutput(context, RejectedPort);
        }

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Radius").WithDefaultValue(Defaults.Radius).Delayed();
        }

        public override PlacementNode CreateRuntimeNode() => new Placement.DifferenceNode
        {
            Radius = GetOption("Radius", Defaults.Radius)
        };
    }

    [Node("Points", null, "Merge")]
    [Serializable]
    internal class MergeNode : PlacementGraphNode
    {
        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, "A");
            AddPointInput(context, "B");
            AddPointOutput(context, OutPort);
        }

        public override PlacementNode CreateRuntimeNode() => new Placement.MergeNode();
    }

    [Node("Points", null, "Copy Points")]
    [Serializable]
    internal class CopyPointsNode : PlacementGraphNode
    {
        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, "Source");
            AddPointInput(context, "Pattern");
            AddPointOutput(context, OutPort);
        }

        public override PlacementNode CreateRuntimeNode() => new Placement.CopyPointsNode();
    }

    [Node("Density", null, "Distance Density")]
    [Serializable]
    internal class DistanceDensityNode : PlacementGraphNode
    {
        private static readonly Placement.DistanceDensityNode Defaults = new Placement.DistanceDensityNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointInput(context, "Target");
            AddPointOutput(context, OutPort);
        }

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Inner Radius").WithDefaultValue(Defaults.InnerRadius).Delayed();
            context.AddOption<float>("Outer Radius").WithDefaultValue(Defaults.OuterRadius).Delayed();
            context.AddOption<bool>("Invert").WithDefaultValue(Defaults.Invert);
        }

        public override PlacementNode CreateRuntimeNode() => new Placement.DistanceDensityNode
        {
            InnerRadius = GetOption("Inner Radius", Defaults.InnerRadius),
            OuterRadius = GetOption("Outer Radius", Defaults.OuterRadius),
            Invert = GetOption("Invert", Defaults.Invert)
        };
    }

    [Node("Sources", null, "Child Markers")]
    [Serializable]
    internal class ChildMarkersNode : PlacementGraphNode
    {
        protected override void DefinePointPorts(IPortDefinitionContext context) => AddPointOutput(context, OutPort);

        public override PlacementNode CreateRuntimeNode() => new Placement.ChildMarkersNode();
    }

    [Node("Sources", null, "Terrain Sampler")]
    [Serializable]
    internal class TerrainSamplerNode : PlacementGraphNode
    {
        private static readonly Placement.TerrainSamplerNode Defaults = new Placement.TerrainSamplerNode();

        protected override void DefinePointPorts(IPortDefinitionContext context) => AddPointOutput(context, OutPort);

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Spacing").WithDefaultValue(Defaults.Spacing).Delayed();
            context.AddOption<float>("Jitter").WithDefaultValue(Defaults.Jitter).Delayed();
            context.AddOption<int>("Layer Index").WithDefaultValue(Defaults.LayerIndex).Delayed()
                .WithTooltip("Terrain layer to filter by, or -1 to keep every point on the terrain.");
            context.AddOption<float>("Min Weight").WithDefaultValue(Defaults.MinWeight).Delayed();
        }

        public override PlacementNode CreateRuntimeNode() => new Placement.TerrainSamplerNode
        {
            Spacing = GetOption("Spacing", Defaults.Spacing),
            Jitter = GetOption("Jitter", Defaults.Jitter),
            LayerIndex = GetOption("Layer Index", Defaults.LayerIndex),
            MinWeight = GetOption("Min Weight", Defaults.MinWeight)
        };
    }

#if UNITYTOOLS_SPLINES
    [Node("Sources", null, "Spline Sampler")]
    [Serializable]
    internal class SplineSamplerNode : PlacementGraphNode
    {
        private static readonly Placement.SplineSamplerNode Defaults = new Placement.SplineSamplerNode();

        protected override void DefinePointPorts(IPortDefinitionContext context) => AddPointOutput(context, OutPort);

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<Placement.SplineSamplerNode.SampleMode>("Mode").WithDefaultValue(Defaults.Mode);
            context.AddOption<float>("Spacing").WithDefaultValue(Defaults.Spacing).Delayed();
            context.AddOption<float>("Jitter").WithDefaultValue(Defaults.Jitter).Delayed();
        }

        public override PlacementNode CreateRuntimeNode() => new Placement.SplineSamplerNode
        {
            Mode = GetOption("Mode", Defaults.Mode),
            Spacing = GetOption("Spacing", Defaults.Spacing),
            Jitter = GetOption("Jitter", Defaults.Jitter)
        };
    }
#endif
}
