using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>Receives what spawner nodes place. The scene component instantiates prefabs; tests can record.</summary>
    public interface IPlacementOutput
    {
        void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale);
    }

    /// <summary>Everything a node can read about the run: where, which seed, and where spawned objects go.</summary>
    public sealed class PlacementContext
    {
        /// <summary>Local-to-world matrix of the generating component. Bounds are in this space.</summary>
        public Matrix4x4 LocalToWorld = Matrix4x4.identity;

        /// <summary>The generation volume, in the component's local space.</summary>
        public Bounds LocalBounds = new Bounds(Vector3.zero, Vector3.one * 10f);

        /// <summary>The component's seed. Nodes mix it with their own seed and each point's seed.</summary>
        public int Seed;

        /// <summary>Where spawner nodes send objects. Null skips spawning (points are still computed).</summary>
        public IPlacementOutput Output;

        /// <summary>Marker transforms for the Child Markers node: the children of the component's Markers transform.</summary>
        public readonly List<Transform> Markers = new List<Transform>();

#if UNITYTOOLS_SPLINES
        /// <summary>The spline for the Spline Sampler node: the component's Spline field.</summary>
        public UnityEngine.Splines.SplineContainer Spline;
#endif

        /// <summary>Points each node produced on its first output, by node index. Filled by the executor.</summary>
        public readonly List<int> NodeOutputCounts = new List<int>();

        /// <summary>When set, the executor keeps every node's outputs here, by node index, for the debug view.</summary>
        public List<PointData[]> NodeOutputs;

        /// <summary>Warnings raised while running (a node with nothing to spawn, and so on).</summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>For the node that is running: whether each of its inputs has at least one wire.</summary>
        public IReadOnlyList<bool> InputConnected { get; internal set; } = Array.Empty<bool>();

        /// <summary>The seed as an unsigned value, for hashing.</summary>
        public uint SeedHash => PlacementRandom.Hash(unchecked((uint)Seed));

        /// <summary>Converts a point in the component's local space to world space.</summary>
        public Vector3 LocalToWorldPoint(Vector3 local) => LocalToWorld.MultiplyPoint3x4(local);

        public void Warn(PlacementNode node, string message) => Warnings.Add($"{node?.Name ?? "Graph"}: {message}");
    }
}
