using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>One candidate placement: a world-space transform plus the values nodes filter and weight on.</summary>
    [Serializable]
    public struct PlacementPoint
    {
        /// <summary>World-space position.</summary>
        public Vector3 Position;
        /// <summary>World-space rotation.</summary>
        public Quaternion Rotation;
        /// <summary>Local scale applied to whatever is spawned here.</summary>
        public Vector3 Scale;
        /// <summary>Surface normal at the point (world up until a surface sampler writes one).</summary>
        public Vector3 Normal;
        /// <summary>0 to 1. Samplers write 1, density nodes scale it, density filters cut on it.</summary>
        public float Density;
        /// <summary>Per-point seed, so random choices made for this point don't depend on any other point.</summary>
        public uint Seed;

        /// <summary>A point at a position with identity rotation, unit scale, up normal and full density.</summary>
        public static PlacementPoint At(Vector3 position, uint seed) => new PlacementPoint
        {
            Position = position,
            Rotation = Quaternion.identity,
            Scale = Vector3.one,
            Normal = Vector3.up,
            Density = 1f,
            Seed = seed
        };

        /// <summary>Angle in degrees between the point's normal and world up.</summary>
        public float Steepness => Vector3.Angle(Normal, Vector3.up);
    }

    /// <summary>The data that flows along the wires of a placement graph: a set of points.</summary>
    public sealed class PointData
    {
        /// <summary>The points. Nodes build new lists rather than editing their inputs.</summary>
        public readonly List<PlacementPoint> Points;

        public PointData() => Points = new List<PlacementPoint>();
        public PointData(int capacity) => Points = new List<PlacementPoint>(capacity);
        public PointData(List<PlacementPoint> points) => Points = points ?? new List<PlacementPoint>();

        public int Count => Points.Count;

        /// <summary>An empty set, used for unconnected inputs.</summary>
        public static PointData Empty => new PointData();

        /// <summary>Concatenates sets, in order. Used when several wires feed one input.</summary>
        public static PointData Merge(IReadOnlyList<PointData> sets)
        {
            if (sets == null || sets.Count == 0) return Empty;
            if (sets.Count == 1) return sets[0] ?? Empty;
            int total = 0;
            for (int i = 0; i < sets.Count; i++) total += sets[i]?.Count ?? 0;
            var merged = new PointData(total);
            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i] != null) merged.Points.AddRange(sets[i].Points);
            }
            return merged;
        }
    }
}
