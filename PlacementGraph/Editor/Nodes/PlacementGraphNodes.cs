using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace UnityTools.Placement.Editor
{
    // The Graph Toolkit nodes. Each one mirrors a runtime node: same point ports in the same order, and one option per
    // runtime setting. Option defaults come from a fresh runtime node so the two can't drift apart.

    [Serializable]
    internal class VolumeSamplerNode : PlacementGraphNode
    {
        private static readonly Placement.VolumeSamplerNode Defaults = new Placement.VolumeSamplerNode();

        protected override void DefinePointPorts(IPortDefinitionContext context) => AddPointOutput(context, OutPort);

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Spacing").WithDefaultValue(Defaults.Spacing).Delayed();
            context.AddOption<float>("Jitter").WithDefaultValue(Defaults.Jitter).Delayed();
        }

        public override PlacementNode CreateRuntimeNode() => new Placement.VolumeSamplerNode
        {
            Spacing = GetOption("Spacing", Defaults.Spacing),
            Jitter = GetOption("Jitter", Defaults.Jitter)
        };
    }

    [Serializable]
    internal class SurfaceSamplerNode : PlacementGraphNode
    {
        private static readonly Placement.SurfaceSamplerNode Defaults = new Placement.SurfaceSamplerNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
        }

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Spacing").WithDefaultValue(Defaults.Spacing).Delayed();
            context.AddOption<float>("Jitter").WithDefaultValue(Defaults.Jitter).Delayed();
            context.AddOption<LayerMask>("Layer Mask").WithDefaultValue(Defaults.LayerMask);
        }

        public override PlacementNode CreateRuntimeNode() => new Placement.SurfaceSamplerNode
        {
            Spacing = GetOption("Spacing", Defaults.Spacing),
            Jitter = GetOption("Jitter", Defaults.Jitter),
            LayerMask = GetOption("Layer Mask", Defaults.LayerMask)
        };
    }

    [Serializable]
    internal class NoiseDensityNode : PlacementGraphNode
    {
        private static readonly Placement.NoiseDensityNode Defaults = new Placement.NoiseDensityNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
        }

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Scale").WithDefaultValue(Defaults.Scale).Delayed();
            context.AddOption<float>("Contrast").WithDefaultValue(Defaults.Contrast).Delayed();
            context.AddOption<bool>("Invert").WithDefaultValue(Defaults.Invert);
        }

        public override PlacementNode CreateRuntimeNode() => new Placement.NoiseDensityNode
        {
            Scale = GetOption("Scale", Defaults.Scale),
            Contrast = GetOption("Contrast", Defaults.Contrast),
            Invert = GetOption("Invert", Defaults.Invert)
        };
    }

    [Serializable]
    internal class DensityFilterNode : PlacementGraphNode
    {
        private static readonly Placement.DensityFilterNode Defaults = new Placement.DensityFilterNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
            AddPointOutput(context, RejectedPort);
        }

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<Placement.DensityFilterNode.FilterMode>("Mode").WithDefaultValue(Defaults.Mode);
            context.AddOption<float>("Threshold").WithDefaultValue(Defaults.Threshold).Delayed();
        }

        public override PlacementNode CreateRuntimeNode() => new Placement.DensityFilterNode
        {
            Mode = GetOption("Mode", Defaults.Mode),
            Threshold = GetOption("Threshold", Defaults.Threshold)
        };
    }

    [Serializable]
    internal class SlopeFilterNode : PlacementGraphNode
    {
        private static readonly Placement.SlopeFilterNode Defaults = new Placement.SlopeFilterNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
            AddPointOutput(context, RejectedPort);
        }

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Min Angle").WithDefaultValue(Defaults.MinAngle).Delayed();
            context.AddOption<float>("Max Angle").WithDefaultValue(Defaults.MaxAngle).Delayed();
        }

        public override PlacementNode CreateRuntimeNode() => new Placement.SlopeFilterNode
        {
            MinAngle = GetOption("Min Angle", Defaults.MinAngle),
            MaxAngle = GetOption("Max Angle", Defaults.MaxAngle)
        };
    }

    [Serializable]
    internal class TransformPointsNode : PlacementGraphNode
    {
        private static readonly Placement.TransformPointsNode Defaults = new Placement.TransformPointsNode();

        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
        }

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<Vector3>("Offset Min").WithDefaultValue(Defaults.OffsetMin).Delayed();
            context.AddOption<Vector3>("Offset Max").WithDefaultValue(Defaults.OffsetMax).Delayed();
            context.AddOption<Vector3>("Rotation Min").WithDefaultValue(Defaults.RotationMin).Delayed();
            context.AddOption<Vector3>("Rotation Max").WithDefaultValue(Defaults.RotationMax).Delayed();
            context.AddOption<float>("Scale Min").WithDefaultValue(Defaults.ScaleMin).Delayed();
            context.AddOption<float>("Scale Max").WithDefaultValue(Defaults.ScaleMax).Delayed();
            context.AddOption<bool>("Align To Normal").WithDefaultValue(Defaults.AlignToNormal);
        }

        public override PlacementNode CreateRuntimeNode() => new Placement.TransformPointsNode
        {
            OffsetMin = GetOption("Offset Min", Defaults.OffsetMin),
            OffsetMax = GetOption("Offset Max", Defaults.OffsetMax),
            RotationMin = GetOption("Rotation Min", Defaults.RotationMin),
            RotationMax = GetOption("Rotation Max", Defaults.RotationMax),
            ScaleMin = GetOption("Scale Min", Defaults.ScaleMin),
            ScaleMax = GetOption("Scale Max", Defaults.ScaleMax),
            AlignToNormal = GetOption("Align To Normal", Defaults.AlignToNormal)
        };
    }

    [Serializable]
    internal class SpawnPrefabsNode : PlacementGraphNode
    {
        protected override void DefinePointPorts(IPortDefinitionContext context)
        {
            AddPointInput(context, InPort);
            AddPointOutput(context, OutPort);
        }

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<PrefabPalette>("Palette");
        }

        public override PlacementNode CreateRuntimeNode() => new Placement.SpawnPrefabsNode
        {
            Palette = GetOption<PrefabPalette>("Palette", null)
        };

        public override void Validate(List<string> problems)
        {
            if (GetOption<PrefabPalette>("Palette", null) == null)
                problems.Add("Spawn Prefabs has no Palette. Create one with Assets > Create > UnityTools > Prefab Palette.");
        }
    }
}
