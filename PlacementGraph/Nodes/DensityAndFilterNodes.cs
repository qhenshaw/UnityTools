using System;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>Density: multiplies each point's density by world-space Perlin noise.</summary>
    [Serializable]
    public class NoiseDensityNode : PlacementNode
    {
        [Tooltip("Size of one noise feature, in metres.")]
        [Min(0.01f)] public float Scale = 10f;
        [Tooltip("Above 1 pushes values towards 0 and 1, for sharper patches.")]
        [Min(0f)] public float Contrast = 1f;
        public bool Invert;

        public override int InputCount => 1;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            // The offset comes from the seeds, so changing the graph seed moves the noise pattern.
            uint seed = PlacementRandom.Hash(context.SeedHash, NodeSeed);
            float offsetX = PlacementRandom.Value(PlacementRandom.Hash(seed, 1u)) * 10000f;
            float offsetZ = PlacementRandom.Value(PlacementRandom.Hash(seed, 2u)) * 10000f;
            float scale = Mathf.Max(0.01f, Scale);

            PointData input = inputs[0];
            var result = PointData.Derive(input, input.Count);
            for (int i = 0; i < input.Count; i++)
            {
                PlacementPoint point = input.Points[i];
                float noise = Mathf.Clamp01(Mathf.PerlinNoise(point.Position.x / scale + offsetX, point.Position.z / scale + offsetZ));
                noise = Mathf.Clamp01((noise - 0.5f) * Contrast + 0.5f);
                if (Invert) noise = 1f - noise;
                PlacementPoint changed = point;
                changed.Density *= noise;
                result.AddFrom(input, i, changed);
            }
            outputs[0] = result;
        }
    }

    /// <summary>Filter: keeps points by density, either above a threshold or at random with density as the chance.</summary>
    [Serializable]
    public class DensityFilterNode : PlacementNode
    {
        public enum FilterMode
        {
            /// <summary>Keep points whose density is at or above <see cref="Threshold"/>.</summary>
            Threshold,
            /// <summary>Keep each point with a chance equal to its density.</summary>
            Random
        }

        public FilterMode Mode = FilterMode.Random;
        [Range(0f, 1f)] public float Threshold = 0.5f;

        public override int InputCount => 1;
        /// <summary>Output 0: kept. Output 1: rejected.</summary>
        public override int OutputCount => 2;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            uint seed = PlacementRandom.Hash(context.SeedHash, NodeSeed);
            PointData input = inputs[0];
            var kept = PointData.Derive(input, input.Count);
            var rejected = PointData.Derive(input);
            for (int i = 0; i < input.Count; i++)
            {
                PlacementPoint point = input.Points[i];
                bool keep = Mode == FilterMode.Threshold
                    ? point.Density >= Threshold
                    : PlacementRandom.Value(PlacementRandom.Hash(point.Seed, seed)) < point.Density;
                (keep ? kept : rejected).AddFrom(input, i);
            }
            outputs[0] = kept;
            outputs[1] = rejected;
        }
    }

    /// <summary>Filter: keeps points whose surface steepness (angle from world up) is within a range.</summary>
    [Serializable]
    public class SlopeFilterNode : PlacementNode
    {
        [Range(0f, 180f)] public float MinAngle = 0f;
        [Range(0f, 180f)] public float MaxAngle = 30f;

        public override int InputCount => 1;
        /// <summary>Output 0: kept. Output 1: rejected.</summary>
        public override int OutputCount => 2;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            PointData input = inputs[0];
            var kept = PointData.Derive(input, input.Count);
            var rejected = PointData.Derive(input);
            for (int i = 0; i < input.Count; i++)
            {
                float angle = input.Points[i].Steepness;
                bool keep = angle >= MinAngle && angle <= MaxAngle;
                (keep ? kept : rejected).AddFrom(input, i);
            }
            outputs[0] = kept;
            outputs[1] = rejected;
        }
    }
}
