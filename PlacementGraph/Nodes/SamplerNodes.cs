using System;
using Unity.Collections;
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
            if (context.AnchorGridToOrigin) return SampleTopAnchored(context, node, spacing, jitter);
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

        /// <summary>
        /// The grid for partitioned graphs: cells are anchored at the component's origin rather than centred in the
        /// volume, and a point's seed comes from its cell's global index, so every partition cell produces exactly the
        /// points of the whole grid that fall inside it, whatever cells ran before.
        /// </summary>
        private static PointData SampleTopAnchored(PlacementContext context, PlacementNode node, float spacing, float jitter)
        {
            Bounds bounds = context.LocalBounds;
            int x0 = Mathf.FloorToInt(bounds.min.x / spacing), x1 = Mathf.CeilToInt(bounds.max.x / spacing);
            int z0 = Mathf.FloorToInt(bounds.min.z / spacing), z1 = Mathf.CeilToInt(bounds.max.z / spacing);
            long cells = (long)(x1 - x0) * (z1 - z0);
            if (cells > MaxPoints)
            {
                context.Warn(node, $"Spacing {spacing} would make {cells} points; the limit is {MaxPoints}. Increase Spacing.");
                return PointData.Empty;
            }
            uint seed = PlacementRandom.Hash(context.SeedHash, node.NodeSeed);
            Quaternion rotation = context.LocalToWorld.rotation;
            Vector3 up = context.LocalToWorld.MultiplyVector(Vector3.up).normalized;
            var result = new PointData((int)Mathf.Max(0, cells));
            for (int z = z0; z < z1; z++)
            {
                for (int x = x0; x < x1; x++)
                {
                    // Cell centres decide membership, so a grid cell belongs to exactly one partition cell.
                    float cx = (x + 0.5f) * spacing, cz = (z + 0.5f) * spacing;
                    if (cx < bounds.min.x || cx >= bounds.max.x || cz < bounds.min.z || cz >= bounds.max.z) continue;
                    uint pointSeed = PlacementRandom.Hash(seed, unchecked((uint)x), unchecked((uint)z));
                    float jx = (PlacementRandom.Value(PlacementRandom.Hash(pointSeed, 1u)) - 0.5f) * jitter;
                    float jz = (PlacementRandom.Value(PlacementRandom.Hash(pointSeed, 2u)) - 0.5f) * jitter;
                    var local = new Vector3(cx + jx * spacing, bounds.max.y, cz + jz * spacing);
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

            var result = PointData.Derive(source, source.Count);
            outputs[0] = result;
            if (source.Count == 0) return;

            // All rays go out as one batch (RaycastCommand), which runs them on worker threads. The hits are the
            // same as Physics.Raycast's.
            var commands = new NativeArray<RaycastCommand>(source.Count, Allocator.TempJob);
            var hits = new NativeArray<RaycastHit>(source.Count, Allocator.TempJob);
            try
            {
                var query = new QueryParameters(LayerMask, false, QueryTriggerInteraction.Ignore, Physics.queriesHitBackfaces);
                for (int i = 0; i < source.Count; i++)
                {
                    // Start every ray at the top face, above the point, so wired-in points project the same way.
                    Vector3 local = worldToLocal.MultiplyPoint3x4(source.Points[i].Position);
                    local.y = bounds.max.y;
                    commands[i] = new RaycastCommand(context.LocalToWorldPoint(local), down, query, distance);
                }
                RaycastCommand.ScheduleBatch(commands, hits, 64, 1).Complete();
                for (int i = 0; i < source.Count; i++)
                {
                    RaycastHit hit = hits[i];
                    if (hit.collider == null) continue;
                    PlacementPoint projected = source.Points[i];
                    projected.Position = hit.point;
                    projected.Normal = hit.normal;
                    result.AddFrom(source, i, projected);
                }
            }
            finally
            {
                commands.Dispose();
                hits.Dispose();
            }
        }
    }
}
