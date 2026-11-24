using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>Receives what spawner nodes place. The scene component instantiates prefabs; tests can record.</summary>
    public interface IPlacementOutput
    {
        /// <summary>Places <paramref name="prefab"/> with this world position and rotation and this local scale (already final).</summary>
        void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale);
    }

    /// <summary>Everything a node can read about the run: where, which seed, and where spawned objects go.</summary>
    public sealed class PlacementContext
    {
        /// <summary>Local-to-world matrix of the generating component. Bounds are in this space.</summary>
        public Matrix4x4 LocalToWorld = Matrix4x4.identity;

        /// <summary>The generation volume, in the component's local space.</summary>
        public Bounds LocalBounds = new Bounds(Vector3.zero, Vector3.one * 10f);

        /// <summary>
        /// Partitioned runs: grids are anchored at the component's origin (not centred in the volume), so each cell
        /// samples the same global grid. Set by the component when Cell Size is used.
        /// </summary>
        public bool AnchorGridToOrigin;

        /// <summary>
        /// Partitioned runs: the part of the volume this run owns, in local space. Spawners only place points inside it,
        /// although <see cref="LocalBounds"/> (the cell plus its overlap) is sampled, so nodes see points across the
        /// border. Null when the graph isn't partitioned.
        /// </summary>
        public Bounds? OwnedBounds;

        private Matrix4x4 _ownedFrom = new Matrix4x4();
        private Matrix4x4 _worldToLocal;

        /// <summary>Whether a spawner should place a point at <paramref name="worldPosition"/> in this run.</summary>
        public bool IsOwned(Vector3 worldPosition)
        {
            if (OwnedBounds == null) return true;
            if (_ownedFrom != LocalToWorld)
            {
                _ownedFrom = LocalToWorld;
                _worldToLocal = LocalToWorld.inverse;
            }
            Vector3 local = _worldToLocal.MultiplyPoint3x4(worldPosition);
            Bounds owned = OwnedBounds.Value;
            return local.x >= owned.min.x && local.x < owned.max.x && local.z >= owned.min.z && local.z < owned.max.z;
        }

        /// <summary>The component's seed. Nodes mix it with their own seed and each point's seed.</summary>
        public int Seed;

        /// <summary>Where spawner nodes send objects. Null skips spawning (points are still computed).</summary>
        public IPlacementOutput Output;

        /// <summary>The scene the generating component is in; Scene Input only reads objects from it. Invalid means any scene.</summary>
        public UnityEngine.SceneManagement.Scene Scene;

        /// <summary>Where Spawn Instanced sends instances. Null skips them.</summary>
        public IInstanceOutput Instances;

        /// <summary>Where Spawn Spline Mesh sends generated meshes. Null skips them.</summary>
        public IMeshOutput Meshes;

        /// <summary>
        /// Values that replace the graph's parameter defaults for this run, matched by name and type. The component
        /// fills it from its overrides.
        /// </summary>
        public readonly List<PlacementParameter> ParameterOverrides = new List<PlacementParameter>();

        /// <summary>Marker transforms for the Child Markers node: the children of the component's Markers transform.</summary>
        public readonly List<Transform> Markers = new List<Transform>();

#if UNITYTOOLS_SPLINES
        /// <summary>The spline for the Spline Sampler node: the component's Spline field.</summary>
        public UnityEngine.Splines.SplineContainer Spline;
#endif

        /// <summary>Points each node produced on its first output, by node index. Filled by the executor.</summary>
        public readonly List<int> NodeOutputCounts = new List<int>();

        /// <summary>How long each node took to run, in milliseconds, by node index (spawning included). Filled by the executor.</summary>
        public readonly List<double> NodeMilliseconds = new List<double>();

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
