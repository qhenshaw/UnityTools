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

    /// <summary>
    /// The data that flows along the wires of a placement graph: a set of points and, optionally, named attributes
    /// with a value per point (see <see cref="PointAttribute"/>).
    /// </summary>
    public sealed class PointData
    {
        /// <summary>The points. Nodes build new sets rather than editing their inputs.</summary>
        public readonly List<PlacementPoint> Points;

        /// <summary>The attribute columns, each parallel to <see cref="Points"/>.</summary>
        public readonly List<PointAttribute> Attributes = new List<PointAttribute>();

        // The last source copied from with AddFrom, and where each of this set's columns is in it.
        private PointData _mapSource;
        private int[] _map;

        public PointData() => Points = new List<PlacementPoint>();
        public PointData(int capacity) => Points = new List<PlacementPoint>(capacity);
        public PointData(List<PlacementPoint> points) => Points = points ?? new List<PlacementPoint>();

        public int Count => Points.Count;

        /// <summary>An empty set, used for unconnected inputs.</summary>
        public static PointData Empty => new PointData();

        /// <summary>
        /// An empty set with the same attribute columns as <paramref name="source"/>, for a node that keeps, drops or
        /// reorders its points. Fill it with <see cref="AddFrom(PointData, int)"/>.
        /// </summary>
        public static PointData Derive(PointData source, int capacity = 0)
        {
            var result = new PointData(capacity);
            if (source == null) return result;
            foreach (PointAttribute attribute in source.Attributes) result.Attributes.Add(attribute.CreateEmpty(capacity));
            return result;
        }

        /// <summary>A copy of <paramref name="source"/> with its own point list and attribute columns, for a node that adds attributes.</summary>
        public static PointData Copy(PointData source)
        {
            var result = new PointData(new List<PlacementPoint>(source.Points));
            foreach (PointAttribute attribute in source.Attributes) result.Attributes.Add(attribute.Clone());
            return result;
        }

        /// <summary>Adds point <paramref name="index"/> of <paramref name="source"/> with its attribute values.</summary>
        public void AddFrom(PointData source, int index) => AddFrom(source, index, source.Points[index]);

        /// <summary>Adds <paramref name="point"/> with the attribute values of point <paramref name="index"/> of <paramref name="source"/>.</summary>
        public void AddFrom(PointData source, int index, PlacementPoint point)
        {
            int at = Points.Count;
            Points.Add(point);
            if (Attributes.Count == 0) return;
            if (!ReferenceEquals(source, _mapSource) || _map == null || _map.Length != Attributes.Count)
            {
                _mapSource = source;
                _map = new int[Attributes.Count];
                for (int i = 0; i < Attributes.Count; i++) _map[i] = source.IndexOfAttribute(Attributes[i].Name, Attributes[i].Type);
            }
            for (int i = 0; i < Attributes.Count; i++)
            {
                int column = _map[i];
                if (column >= 0) Attributes[i].CopyValue(source.Attributes[column], index, at);
            }
        }

        private int IndexOfAttribute(string name, PointAttributeType type)
        {
            for (int i = 0; i < Attributes.Count; i++)
            {
                if (Attributes[i].Name == name && Attributes[i].Type == type) return i;
            }
            return -1;
        }

        /// <summary>The attribute called <paramref name="name"/>, of any type, or null.</summary>
        public PointAttribute GetAttribute(string name)
        {
            foreach (PointAttribute attribute in Attributes)
            {
                if (attribute.Name == name) return attribute;
            }
            return null;
        }

        /// <summary>The attribute called <paramref name="name"/> if it has type <typeparamref name="T"/>, or null.</summary>
        public PointAttribute<T> GetAttribute<T>(string name) => GetAttribute(name) as PointAttribute<T>;

        /// <summary>
        /// The attribute called <paramref name="name"/>, added with <paramref name="defaultValue"/> if it doesn't exist.
        /// An existing attribute of another type is replaced. Only call this on a set the node created.
        /// </summary>
        public PointAttribute<T> GetOrAddAttribute<T>(string name, T defaultValue = default)
        {
            for (int i = 0; i < Attributes.Count; i++)
            {
                if (Attributes[i].Name != name) continue;
                if (Attributes[i] is PointAttribute<T> existing) return existing;
                var replacement = new PointAttribute<T>(name, defaultValue, Points.Count);
                Attributes[i] = replacement;
                _map = null;
                return replacement;
            }
            var added = new PointAttribute<T>(name, defaultValue, Points.Count);
            Attributes.Add(added);
            _map = null;
            return added;
        }

        /// <summary>
        /// Concatenates sets, in order. Used when several wires feed one input. The result has every attribute any set
        /// has; points from a set without one get its default.
        /// </summary>
        public static PointData Merge(IReadOnlyList<PointData> sets)
        {
            if (sets == null || sets.Count == 0) return Empty;
            if (sets.Count == 1) return sets[0] ?? Empty;
            int total = 0;
            for (int i = 0; i < sets.Count; i++) total += sets[i]?.Count ?? 0;
            var merged = new PointData(total);
            foreach (PointData set in sets)
            {
                if (set == null) continue;
                foreach (PointAttribute attribute in set.Attributes)
                {
                    if (merged.IndexOfAttribute(attribute.Name, attribute.Type) < 0) merged.Attributes.Add(attribute.CreateEmpty(total));
                }
            }
            foreach (PointData set in sets)
            {
                if (set == null) continue;
                if (merged.Attributes.Count == 0) { merged.Points.AddRange(set.Points); continue; }
                for (int i = 0; i < set.Count; i++) merged.AddFrom(set, i);
            }
            return merged;
        }
    }
}
