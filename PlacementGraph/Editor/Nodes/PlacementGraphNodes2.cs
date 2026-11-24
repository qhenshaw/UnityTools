using System;
using Unity.GraphToolkit.Editor;

namespace UnityTools.Placement.Editor
{
    // Graph Toolkit nodes for the terrain, marker and spline sources and the set and density nodes. Same rules as
    // PlacementGraphNodes.cs.

    [Node("Filters", null, "Height Filter")]
    [Serializable]
    internal class HeightFilterNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.HeightFilterNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
            AddPointOutput(context, RejectedPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<float>(context, "Min", "World height in metres.");
            AddSetting<float>(context, "Max", "World height in metres.");
        }
    }

    [Node("Filters", null, "Self Pruning")]
    [Serializable]
    internal class SelfPruningNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.SelfPruningNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
            AddPointOutput(context, RejectedPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<float>(context, "Radius", "Minimum distance between kept points, in metres.");
        }
    }

    [Node("Filters", null, "Difference")]
    [Serializable]
    internal class DifferenceNode : PlacementGraphNode
    {
        public const string ExcludePort = "Exclude";

        protected override PlacementNode CreateRuntimeInstance() => new Placement.DifferenceNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointInput(context, ExcludePort);
            AddPointOutput(context, OutPort);
            AddPointOutput(context, RejectedPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<float>(context, "Radius", "In points closer than this to an Exclude point are dropped, in metres.");
        }
    }

    [Node("Points", null, "Merge")]
    [Serializable]
    internal class MergeNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.MergeNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, "A");
            AddPointInput(context, "B");
            AddPointOutput(context, OutPort);
        }
    }

    [Node("Points", null, "Copy Points")]
    [Serializable]
    internal class CopyPointsNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.CopyPointsNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, "Source");
            AddPointInput(context, "Pattern");
            AddPointOutput(context, OutPort);
        }
    }

    [Node("Density", null, "Distance Density")]
    [Serializable]
    internal class DistanceDensityNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.DistanceDensityNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointInput(context, "Target");
            AddPointOutput(context, OutPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<float>(context, "Inner Radius", "Density is unchanged within this distance of a Target point.");
            AddSetting<float>(context, "Outer Radius", "Density reaches 0 at this distance.");
            AddSetting<bool>(context, "Invert");
        }
    }

    [Node("Sources", null, "Child Markers")]
    [Serializable]
    internal class ChildMarkersNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.ChildMarkersNode();

        protected override void DefinePointPorts(IPortDefinitionContext context) => AddPointOutput(context, OutPort);
    }

    [Node("Sources", null, "Terrain Sampler")]
    [Serializable]
    internal class TerrainSamplerNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.TerrainSamplerNode();

        protected override void DefinePointPorts(IPortDefinitionContext context) => AddPointOutput(context, OutPort);

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<float>(context, "Spacing", "Distance between grid points, in metres.");
            AddSetting<float>(context, "Jitter");
            AddSetting<int>(context, "Layer Index", "Terrain layer to filter by, or -1 to keep every point on the terrain.");
            AddSetting<float>(context, "Min Weight", "Points are kept where the layer is painted at least this much.");
        }
    }

#if UNITYTOOLS_SPLINES
    [Node("Sources", null, "Spline Sampler")]
    [Serializable]
    internal class SplineSamplerNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.SplineSamplerNode();

        protected override void DefinePointPorts(IPortDefinitionContext context) => AddPointOutput(context, OutPort);

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<float>(context, "Spacing", "Distance between points along the spline, or grid spacing for Inside.");
            AddSetting<float>(context, "Jitter", "Inside only.");
        }

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<Placement.SplineSamplerNode.SampleMode>("Mode")
                .WithDefaultValue(((Placement.SplineSamplerNode)Defaults).Mode);
        }

        protected override void ApplyOptions(PlacementNode runtime)
        {
            var node = (Placement.SplineSamplerNode)runtime;
            node.Mode = GetOption("Mode", node.Mode);
        }
    }
#endif
}
