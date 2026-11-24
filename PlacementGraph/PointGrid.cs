using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// A spatial hash of positions in cubic cells, for "is anything within this radius" and "how far is the nearest"
    /// queries without comparing every pair of points. Queries with a radius up to the cell size look at the 27
    /// cells around the query point.
    /// </summary>
    internal sealed class PointGrid
    {
        private readonly float _cellSize;
        private readonly float _inverseCellSize;
        private readonly Dictionary<Vector3Int, List<Vector3>> _cells = new Dictionary<Vector3Int, List<Vector3>>();

        /// <summary>Creates an empty grid. <paramref name="cellSize"/> should be the largest radius you'll query with.</summary>
        public PointGrid(float cellSize)
        {
            _cellSize = Mathf.Max(0.0001f, cellSize);
            _inverseCellSize = 1f / _cellSize;
        }

        /// <summary>Creates a grid holding the positions of <paramref name="points"/>.</summary>
        public PointGrid(float cellSize, PointData points) : this(cellSize)
        {
            foreach (PlacementPoint point in points.Points) Add(point.Position);
        }

        private Vector3Int CellOf(Vector3 position) => new Vector3Int(
            Mathf.FloorToInt(position.x * _inverseCellSize),
            Mathf.FloorToInt(position.y * _inverseCellSize),
            Mathf.FloorToInt(position.z * _inverseCellSize));

        public void Add(Vector3 position)
        {
            Vector3Int cell = CellOf(position);
            if (!_cells.TryGetValue(cell, out List<Vector3> list)) _cells[cell] = list = new List<Vector3>(4);
            list.Add(position);
        }

        /// <summary>True if any stored position is within <paramref name="radius"/> (at most the cell size) of <paramref name="position"/>.</summary>
        public bool AnyWithin(Vector3 position, float radius)
        {
            float radiusSq = radius * radius;
            Vector3Int centre = CellOf(position);
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                if (!_cells.TryGetValue(centre + new Vector3Int(x, y, z), out List<Vector3> list)) continue;
                foreach (Vector3 other in list)
                {
                    if ((other - position).sqrMagnitude < radiusSq) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Distance to the nearest stored position, if it is within <paramref name="maxDistance"/> (at most the cell
        /// size); otherwise <see cref="float.PositiveInfinity"/>.
        /// </summary>
        public float NearestDistance(Vector3 position, float maxDistance)
        {
            float bestSq = maxDistance * maxDistance;
            bool found = false;
            Vector3Int centre = CellOf(position);
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                if (!_cells.TryGetValue(centre + new Vector3Int(x, y, z), out List<Vector3> list)) continue;
                foreach (Vector3 other in list)
                {
                    float distanceSq = (other - position).sqrMagnitude;
                    if (distanceSq <= bestSq)
                    {
                        bestSq = distanceSq;
                        found = true;
                    }
                }
            }
            return found ? Mathf.Sqrt(bestSq) : float.PositiveInfinity;
        }
    }
}
