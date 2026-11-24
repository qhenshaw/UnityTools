using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// Source: one point per marker, at its position, rotation and scale. Markers are the active children of the
    /// Transform set as Markers on the Placement Graph component (<see cref="PlacementContext.Markers"/>).
    /// </summary>
    [Serializable]
    public class ChildMarkersNode : PlacementNode
    {
        public override int InputCount => 0;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            uint seed = PlacementRandom.Hash(context.SeedHash, NodeSeed);
            var result = new PointData(context.Markers.Count);
            for (int i = 0; i < context.Markers.Count; i++)
            {
                Transform marker = context.Markers[i];
                if (marker == null) continue;
                PlacementPoint point = PlacementPoint.At(marker.position, PlacementRandom.Hash(seed, unchecked((uint)i)));
                point.Rotation = marker.rotation;
                point.Scale = marker.lossyScale;
                point.Normal = marker.up;
                result.Points.Add(point);
            }
            if (result.Count == 0)
                context.Warn(this, "No markers. Set Markers on the Placement Graph component to a Transform and add children to it.");
            outputs[0] = result;
        }
    }

    /// <summary>
    /// Source: a jittered grid over the volume, dropped onto the active terrains below it, with the terrain's height
    /// and normal. Optionally keeps only points where one terrain layer is painted at least Min Weight.
    /// </summary>
    [Serializable]
    public class TerrainSamplerNode : PlacementNode
    {
        [Min(0.01f)] public float Spacing = 1f;
        [Range(0f, 1f)] public float Jitter = 0.5f;
        [Tooltip("Index of the terrain layer to filter by, or -1 to keep every point on the terrain.")]
        public int LayerIndex = -1;
        [Tooltip("Points are kept where the layer's painted weight is at least this.")]
        [Range(0f, 1f)] public float MinWeight = 0.5f;

        public override int InputCount => 0;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            PointData grid = GridSampling.SampleTop(context, this, Spacing, Jitter);
            Terrain[] terrains = Terrain.activeTerrains;
            if (terrains.Length == 0)
            {
                context.Warn(this, "There's no active terrain.");
                outputs[0] = PointData.Empty;
                return;
            }

            Bounds bounds = context.LocalBounds;
            Matrix4x4 worldToLocal = context.LocalToWorld.inverse;
            // Each terrain's splat maps are read once per run, and only when filtering by layer.
            var alphamaps = new Dictionary<Terrain, float[,,]>();
            var result = new PointData(grid.Count);
            foreach (PlacementPoint point in grid.Points)
            {
                Terrain terrain = FindTerrain(terrains, point.Position);
                if (terrain == null) continue;

                TerrainData data = terrain.terrainData;
                Vector3 origin = terrain.GetPosition();
                float nx = Mathf.Clamp01((point.Position.x - origin.x) / data.size.x);
                float nz = Mathf.Clamp01((point.Position.z - origin.z) / data.size.z);
                var position = new Vector3(point.Position.x, origin.y + terrain.SampleHeight(point.Position), point.Position.z);

                // Keep the result inside the volume, like the Surface Sampler.
                float localY = worldToLocal.MultiplyPoint3x4(position).y;
                if (localY < bounds.min.y - 1e-4f || localY > bounds.max.y + 1e-4f) continue;
                if (LayerIndex >= 0 && Weight(terrain, data, alphamaps, nx, nz) < MinWeight) continue;

                PlacementPoint sampled = point;
                sampled.Position = position;
                sampled.Normal = data.GetInterpolatedNormal(nx, nz);
                result.Points.Add(sampled);
            }
            outputs[0] = result;
        }

        private static Terrain FindTerrain(Terrain[] terrains, Vector3 position)
        {
            foreach (Terrain terrain in terrains)
            {
                if (terrain == null || terrain.terrainData == null) continue;
                Vector3 origin = terrain.GetPosition();
                Vector3 size = terrain.terrainData.size;
                if (position.x >= origin.x && position.x <= origin.x + size.x &&
                    position.z >= origin.z && position.z <= origin.z + size.z)
                    return terrain;
            }
            return null;
        }

        private float Weight(Terrain terrain, TerrainData data, Dictionary<Terrain, float[,,]> cache, float nx, float nz)
        {
            if (LayerIndex >= data.alphamapLayers) return 0f;
            if (!cache.TryGetValue(terrain, out float[,,] maps))
            {
                cache[terrain] = maps = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            }
            // Alphamaps are indexed [y, x, layer], where y runs along the terrain's z axis.
            int x = Mathf.Clamp(Mathf.RoundToInt(nx * (data.alphamapWidth - 1)), 0, data.alphamapWidth - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(nz * (data.alphamapHeight - 1)), 0, data.alphamapHeight - 1);
            return maps[y, x, LayerIndex];
        }
    }
}
