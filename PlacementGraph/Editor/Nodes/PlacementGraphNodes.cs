using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace UnityTools.Placement.Editor
{
    // The Graph Toolkit nodes for the samplers, noise, filters, Transform Points and Spawn Prefabs. Each one mirrors a
    // runtime node: the same point ports in the same order, then one setting port per runtime setting (named after
    // the field, defaulting to a fresh runtime node's value).

    [Node("Sources", null, "Volume Sampler")]
    [Serializable]
    internal class VolumeSamplerNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.VolumeSamplerNode();

        protected override void DefinePointPorts(IPortDefinitionContext context) => AddPointOutput(context, OutPort);

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<float>(context, "Spacing", "Distance between grid points, in metres.");
            AddSetting<float>(context, "Jitter", "0 is a regular grid; 1 moves each point anywhere in its cell.");
        }
    }

    [Node("Sources", null, "Surface Sampler")]
    [Serializable]
    internal class SurfaceSamplerNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.SurfaceSamplerNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<float>(context, "Spacing", "Grid spacing in metres, when nothing is wired into In.");
            AddSetting<float>(context, "Jitter");
            AddSetting<LayerMask>(context, "Layer Mask", "Colliders on these layers are hit.");
        }
    }

    [Node("Density", null, "Noise Density")]
    [Serializable]
    internal class NoiseDensityNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.NoiseDensityNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<float>(context, "Scale", "Size of one noise feature, in metres.");
            AddSetting<float>(context, "Contrast", "Above 1 pushes values towards 0 and 1, for sharper patches.");
            AddSetting<bool>(context, "Invert");
        }
    }

    [Node("Filters", null, "Density Filter")]
    [Serializable]
    internal class DensityFilterNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.DensityFilterNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
            AddPointOutput(context, RejectedPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<Placement.DensityFilterNode.FilterMode>(context, "Mode",
                "Threshold keeps points at or above Threshold; Random keeps each point with its density as the chance.");
            AddSetting<float>(context, "Threshold", "Threshold mode: points at or above this density are kept.");
        }
    }

    [Node("Filters", null, "Slope Filter")]
    [Serializable]
    internal class SlopeFilterNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.SlopeFilterNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
            AddPointOutput(context, RejectedPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<float>(context, "Min Angle", "Degrees from flat.");
            AddSetting<float>(context, "Max Angle", "Degrees from flat.");
        }
    }

    [Node("Points", null, "Transform Points")]
    [Serializable]
    internal class TransformPointsNode : PlacementGraphNode
    {
        protected override PlacementNode CreateRuntimeInstance() => new Placement.TransformPointsNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<Vector3>(context, "Offset Min", "World-space offset range.");
            AddSetting<Vector3>(context, "Offset Max");
            AddSetting<Vector3>(context, "Rotation Min", "Euler angle range in degrees, applied after alignment.");
            AddSetting<Vector3>(context, "Rotation Max");
            AddSetting<float>(context, "Scale Min", "Uniform scale range (Uniform Scale on), multiplied into or replacing the point's scale.");
            AddSetting<float>(context, "Scale Max");
            AddSetting<bool>(context, "Align To Normal", "Rotate each point so its up axis follows the surface normal, before the random rotation.");
            AddSetting<Placement.TransformPointsNode.RotationModes>(context, "Rotation Mode", "Add: on top of the point's rotation. Replace: instead of it.");
            AddSetting<bool>(context, "Uniform Scale", "On: Scale Min and Scale Max for all axes. Off: Axis Scale Min and Axis Scale Max per axis.");
            AddSetting<Vector3>(context, "Axis Scale Min", "Per-axis scale range, used with Uniform Scale off.");
            AddSetting<Vector3>(context, "Axis Scale Max");
            AddSetting<Placement.TransformPointsNode.ScaleModes>(context, "Scale Mode", "Multiply: times the point's scale. Replace: instead of it.");
        }
    }

    [Node("Spawners", null, "Spawn Prefabs")]
    [Serializable]
    internal class SpawnPrefabsNode : PlacementGraphNode
    {
        public const string PalettePort = "Palette";

        protected override PlacementNode CreateRuntimeInstance() => new Placement.SpawnPrefabsNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
        }

        protected override void DefineSettings(IPortDefinitionContext context)
        {
            AddSetting<PrefabPalette>(context, PalettePort, "The weighted prefabs to pick from.");
            AddSetting<bool>(context, "Keep Prefab Rotation", "Apply the prefab's own root rotation after the point's.");
            AddSetting<bool>(context, "Keep Prefab Scale", "Multiply the point's scale by the prefab's own root scale.");
        }

        public override void Validate(List<string> problems)
        {
            IPort port = GetInputPortByName(PalettePort);
            if (port == null || port.IsConnected) return;
            if (!port.TryGetValue(out PrefabPalette palette) || palette == null)
                problems.Add("Spawn Prefabs has no Palette. Create one with Assets > Create > UnityTools > Prefab Palette.");
        }
    }
}
