using System;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>Shared grid sampling for the sampler nodes: a jittered grid over the volume's top face.</summary>
    internal static class GridSampling
    {
        /// <summary>Above this many points a sampler stops and warns, to keep a typo in Spacing from freezing the editor.</summary>
        public const int MaxPoints = 250000;

        public static PointData SampleTop(PlacementContext context, PlacementNode node, float spacing, float jitter)
        {
            spacing = Mathf.Max(0.01f, spacing);
            jitter = Mathf.Clamp01(jitter);
            Bounds bounds = context.LocalBounds;
            int countX = Mathf.Max(1, Mathf.FloorToInt(bounds.size.x / spacing));
            int countZ = Mathf.Max(1, Mathf.FloorToInt(bounds.size.z / spacing));
            if ((long)countX * countZ > MaxPoints)
            {
                context.Warn(node, $"Spacing {spacing} would make {(long)countX * countZ} points; the limit is {MaxPoints}. Increase Spacing.");
                return PointData.Empty;
            }

            // Cells are centred in the volume, so resizing it grows the grid evenly on both sides.
            float startX = bounds.center.x - countX * spacing * 0.5f;
            float startZ = bounds.center.z - countZ * spacing * 0.5f;
            uint seed = PlacementRandom.Hash(context.SeedHash, node.NodeSeed);
            Quaternion rotation = context.LocalToWorld.rotation;
            Vector3 up = context.LocalToWorld.MultiplyVector(Vector3.up).normalized;

            var result = new PointData(countX * countZ);
            for (int z = 0; z < countZ; z++)
            {
                for (int x = 0; x < countX; x++)
                {
                    // The point's seed comes from its cell, not its index, so resizing the volume keeps existing points.
                    uint pointSeed = PlacementRandom.Hash(seed, unchecked((uint)x), unchecked((uint)z));
                    float jx = (PlacementRandom.Value(PlacementRandom.Hash(pointSeed, 1u)) - 0.5f) * jitter;
                    float jz = (PlacementRandom.Value(PlacementRandom.Hash(pointSeed, 2u)) - 0.5f) * jitter;
                    var local = new Vector3(startX + (x + 0.5f + jx) * spacing, bounds.max.y, startZ + (z + 0.5f + jz) * spacing);
                    PlacementPoint point = PlacementPoint.At(context.LocalToWorldPoint(local), pointSeed);
                    point.Rotation = rotation;
                    point.Normal = up;
                    result.Points.Add(point);
                }
            }
            return result;
        }
    }

    /// <summary>Source: a jittered grid of points on the top face of the generation volume.</summary>
    [Serializable]
    public class VolumeSamplerNode : PlacementNode
    {
        [Min(0.01f)] public float Spacing = 1f;
        [Range(0f, 1f)] public float Jitter = 0.5f;

        public override int InputCount => 0;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            outputs[0] = GridSampling.SampleTop(context, this, Spacing, Jitter);
        }
    }

    /// <summary>
    /// Source: drops points straight down through the volume onto colliders and keeps the hits, with the surface
    /// normal. With nothing wired in it makes its own grid; with points wired in it projects those instead.
    /// </summary>
    [Serializable]
    public class SurfaceSamplerNode : PlacementNode
    {
        [Min(0.01f)] public float Spacing = 1f;
        [Range(0f, 1f)] public float Jitter = 0.5f;
        public LayerMask LayerMask = ~0;

        public override int InputCount => 1;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            bool wired = context.InputConnected.Count > 0 && context.InputConnected[0];
            PointData source = wired ? inputs[0] : GridSampling.SampleTop(context, this, Spacing, Jitter);
            Bounds bounds = context.LocalBounds;
            Matrix4x4 worldToLocal = context.LocalToWorld.inverse;
            Vector3 down = context.LocalToWorld.MultiplyVector(Vector3.down);
            float distance = down.magnitude * bounds.size.y;
            down.Normalize();

            var result = new PointData(source.Count);
            foreach (PlacementPoint point in source.Points)
            {
                // Start every ray at the top face, above the point, so wired-in points project the same way.
                Vector3 local = worldToLocal.MultiplyPoint3x4(point.Position);
                local.y = bounds.max.y;
                Vector3 origin = context.LocalToWorldPoint(local);
                if (!Physics.Raycast(origin, down, out RaycastHit hit, distance, LayerMask, QueryTriggerInteraction.Ignore)) continue;

                PlacementPoint projected = point;
                projected.Position = hit.point;
                projected.Normal = hit.normal;
                result.Points.Add(projected);
            }
            outputs[0] = result;
        }
    }
}
