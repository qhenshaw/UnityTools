using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>Filter: keeps points whose world-space height (Y) is between Min and Max, inclusive.</summary>
    [Serializable]
    public class HeightFilterNode : PlacementNode
    {
        public float Min = 0f;
        public float Max = 10f;

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
                float y = input.Points[i].Position.y;
                (y >= Min && y <= Max ? kept : rejected).AddFrom(input, i);
            }
            outputs[0] = kept;
            outputs[1] = rejected;
        }
    }

    /// <summary>
    /// Filter: keeps points at least Radius apart. Points are considered from the highest density down (ties by
    /// seed), and a point is dropped when it is closer than Radius to one already kept. Kept points come out in their
    /// input order.
    /// </summary>
    [Serializable]
    public class SelfPruningNode : PlacementNode
    {
        [Tooltip("Minimum distance between kept points, in metres.")]
        [Min(0f)] public float Radius = 1f;
        [Tooltip("Points with BoundsMin and BoundsMax attributes (from Subdivide) also keep their bounds apart: the gap between them must be at least Radius.")]
        public bool UsePointBounds;

        public override int InputCount => 1;
        /// <summary>Output 0: kept. Output 1: rejected.</summary>
        public override int OutputCount => 2;

        /// <summary>A point's horizontal extent: the largest distance from its position to its bounds' edge in X or Z, scaled.</summary>
        internal static float HorizontalExtent(Vector3 min, Vector3 max, Vector3 scale) =>
            Mathf.Max(Mathf.Max(Mathf.Abs(min.x), Mathf.Abs(max.x)) * Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(min.z), Mathf.Abs(max.z)) * Mathf.Abs(scale.z));

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            List<PlacementPoint> points = inputs[0].Points;
            PointAttribute<Vector3> boundsMin = UsePointBounds ? inputs[0].GetAttribute<Vector3>(SubdivideNode.BoundsMinAttribute) : null;
            PointAttribute<Vector3> boundsMax = UsePointBounds ? inputs[0].GetAttribute<Vector3>(SubdivideNode.BoundsMaxAttribute) : null;
            float[] extents = null;
            float maxExtent = 0f;
            if (boundsMin != null && boundsMax != null)
            {
                extents = new float[points.Count];
                for (int i = 0; i < points.Count; i++)
                {
                    extents[i] = HorizontalExtent(boundsMin.Get(i), boundsMax.Get(i), points[i].Scale);
                    maxExtent = Mathf.Max(maxExtent, extents[i]);
                }
            }
            if (Radius <= 0f && maxExtent <= 0f)
            {
                outputs[0] = inputs[0];
                outputs[1] = PointData.Empty;
                return;
            }

            var order = new int[points.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int byDensity = points[b].Density.CompareTo(points[a].Density);
                if (byDensity != 0) return byDensity;
                int bySeed = points[a].Seed.CompareTo(points[b].Seed);
                return bySeed != 0 ? bySeed : a.CompareTo(b);
            });

            var keep = new bool[points.Count];
            if (extents == null)
            {
                var grid = new PointGrid(Radius);
                foreach (int index in order)
                {
                    Vector3 position = points[index].Position;
                    if (grid.AnyWithin(position, Radius)) continue;
                    grid.Add(position);
                    keep[index] = true;
                }
            }
            else
            {
                // With bounds, two points clash when their centres are closer than Radius plus both extents.
                float cell = Mathf.Max(0.0001f, Radius + 2f * maxExtent);
                var cells = new Dictionary<Vector3Int, List<int>>();
                Vector3Int CellOf(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
                foreach (int index in order)
                {
                    Vector3 position = points[index].Position;
                    Vector3Int centre = CellOf(position);
                    bool clash = false;
                    for (int z = -1; z <= 1 && !clash; z++)
                    for (int y = -1; y <= 1 && !clash; y++)
                    for (int x = -1; x <= 1 && !clash; x++)
                    {
                        if (!cells.TryGetValue(centre + new Vector3Int(x, y, z), out List<int> list)) continue;
                        foreach (int other in list)
                        {
                            float minimum = Radius + extents[index] + extents[other];
                            if ((points[other].Position - position).sqrMagnitude < minimum * minimum) { clash = true; break; }
                        }
                    }
                    if (clash) continue;
                    if (!cells.TryGetValue(centre, out List<int> own)) cells[centre] = own = new List<int>();
                    own.Add(index);
                    keep[index] = true;
                }
            }

            var kept = PointData.Derive(inputs[0]);
            var rejected = PointData.Derive(inputs[0]);
            for (int i = 0; i < points.Count; i++) (keep[i] ? kept : rejected).AddFrom(inputs[0], i);
            outputs[0] = kept;
            outputs[1] = rejected;
        }
    }

    /// <summary>
    /// Filter: drops the In points that are within Radius of any Exclude point, or inside (or within Radius of) any
    /// Placement Zone the Exclude points came from (Scene Input). With Falloff, kept points near the excluded area get
    /// their density scaled down.
    /// </summary>
    [Serializable]
    public class DifferenceNode : PlacementNode
    {
        [Tooltip("Points closer than this to an Exclude point, or to the surface of an Exclude zone, are dropped, in metres. Points inside a zone are always dropped.")]
        [Min(0f)] public float Radius = 1f;
        [Tooltip("Beyond Radius, kept points within this many metres more get their density scaled down towards the excluded area (0 = off).")]
        [Min(0f)] public float Falloff = 0f;

        /// <summary>Input 0: the points to filter. Input 1: the points (or zones, from Scene Input) to keep away from.</summary>
        public override int InputCount => 2;
        /// <summary>Output 0: kept. Output 1: rejected.</summary>
        public override int OutputCount => 2;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            PointData exclude = inputs[1];
            // Exclude points that came from a Placement Zone stand for the zone's whole shape.
            var zones = new List<PlacementZone>();
            var plain = new PointData();
            PointAttribute<UnityEngine.Object> zoneColumn = exclude.GetAttribute<UnityEngine.Object>(SceneInputNode.ZoneAttribute);
            for (int i = 0; i < exclude.Count; i++)
            {
                if (zoneColumn?.Get(i) is PlacementZone zone && zone != null)
                {
                    if (!zones.Contains(zone)) zones.Add(zone);
                }
                else plain.Points.Add(exclude.Points[i]);
            }

            float reach = Radius + Falloff;
            if (zones.Count == 0 && (reach <= 0f || plain.Count == 0))
            {
                outputs[0] = inputs[0];
                outputs[1] = PointData.Empty;
                return;
            }

            PointGrid grid = plain.Count > 0 && reach > 0f ? new PointGrid(reach, plain) : null;
            PointData input = inputs[0];
            var kept = PointData.Derive(input, input.Count);
            var rejected = PointData.Derive(input);
            for (int i = 0; i < input.Count; i++)
            {
                PlacementPoint point = input.Points[i];
                float pointDistance = grid?.NearestDistance(point.Position, reach) ?? float.PositiveInfinity;
                float zoneDistance = float.PositiveInfinity;
                foreach (PlacementZone zone in zones) zoneDistance = Mathf.Min(zoneDistance, zone.SignedDistance(point.Position));

                if (pointDistance < Radius || zoneDistance <= Radius)
                {
                    rejected.AddFrom(input, i);
                    continue;
                }
                float distance = Mathf.Min(pointDistance, zoneDistance);
                if (Falloff > 0f && distance < reach)
                {
                    point.Density *= Mathf.Clamp01((distance - Radius) / Falloff);
                    kept.AddFrom(input, i, point);
                }
                else kept.AddFrom(input, i);
            }
            outputs[0] = kept;
            outputs[1] = rejected;
        }
    }

    /// <summary>Combines two point sets: A's points, then B's.</summary>
    [Serializable]
    public class MergeNode : PlacementNode
    {
        public override int InputCount => 2;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            outputs[0] = PointData.Merge(inputs);
        }
    }

    /// <summary>
    /// Places a copy of the Pattern at every Source point. Pattern positions are taken relative to the pattern's
    /// centre (the average position), then scaled and rotated by the source point. Each copy gets its own seed, and
    /// its density is the product of the two.
    /// </summary>
    [Serializable]
    public class CopyPointsNode : PlacementNode
    {
        /// <summary>Above this many points the node stops and warns.</summary>
        public const int MaxPoints = GridSampling.MaxPoints;

        /// <summary>Input 0: where to put copies. Input 1: the pattern to copy.</summary>
        public override int InputCount => 2;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            PointData sources = inputs[0];
            PointData pattern = inputs[1];
            long total = (long)sources.Count * pattern.Count;
            if (total > MaxPoints)
            {
                context.Warn(this, $"Copying {pattern.Count} points to {sources.Count} points would make {total}; the limit is {MaxPoints}.");
                outputs[0] = PointData.Empty;
                return;
            }

            Vector3 centre = Vector3.zero;
            foreach (PlacementPoint point in pattern.Points) centre += point.Position;
            if (pattern.Count > 0) centre /= pattern.Count;

            // Copies keep the source point's attributes, plus any the pattern has that the sources don't.
            var result = PointData.Derive(sources, (int)total);
            var patternOnly = new List<(PointAttribute from, PointAttribute to)>();
            foreach (PointAttribute attribute in pattern.Attributes)
            {
                if (result.GetAttribute(attribute.Name) != null) continue;
                PointAttribute column = attribute.CreateEmpty((int)total);
                result.Attributes.Add(column);
                patternOnly.Add((attribute, column));
            }
            for (int s = 0; s < sources.Count; s++)
            {
                PlacementPoint source = sources.Points[s];
                for (int p = 0; p < pattern.Count; p++)
                {
                    PlacementPoint item = pattern.Points[p];
                    Vector3 offset = Vector3.Scale(source.Scale, item.Position - centre);
                    int at = result.Count;
                    result.AddFrom(sources, s, new PlacementPoint
                    {
                        Position = source.Position + source.Rotation * offset,
                        Rotation = source.Rotation * item.Rotation,
                        Scale = Vector3.Scale(source.Scale, item.Scale),
                        Normal = source.Rotation * item.Normal,
                        Density = source.Density * item.Density,
                        Seed = PlacementRandom.Hash(source.Seed, item.Seed)
                    });
                    foreach ((PointAttribute from, PointAttribute to) in patternOnly) to.CopyValue(from, p, at);
                }
            }
            outputs[0] = result;
        }
    }

    /// <summary>
    /// Density: scales each point's density by its distance to the nearest Target point: 1 within Inner Radius,
    /// falling linearly to 0 at Outer Radius. Invert flips it, to thin points out near the targets instead.
    /// </summary>
    [Serializable]
    public class DistanceDensityNode : PlacementNode
    {
        [Min(0f)] public float InnerRadius = 1f;
        [Min(0f)] public float OuterRadius = 5f;
        public bool Invert;

        /// <summary>Input 0: the points to change. Input 1: the points distance is measured to.</summary>
        public override int InputCount => 2;
        public override int OutputCount => 1;

        /// <summary>The falloff for a distance: 1 at or inside <paramref name="inner"/>, 0 at or beyond <paramref name="outer"/>.</summary>
        public static float Falloff(float distance, float inner, float outer)
        {
            if (distance <= inner) return 1f;
            if (distance >= outer) return 0f;
            return 1f - (distance - inner) / (outer - inner);
        }

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            float inner = Mathf.Max(0f, InnerRadius);
            float outer = Mathf.Max(inner, OuterRadius);
            PointGrid grid = inputs[1].Count > 0 ? new PointGrid(Mathf.Max(outer, 0.01f), inputs[1]) : null;

            PointData input = inputs[0];
            var result = PointData.Derive(input, input.Count);
            for (int i = 0; i < input.Count; i++)
            {
                PlacementPoint point = input.Points[i];
                float distance = grid?.NearestDistance(point.Position, Mathf.Max(outer, 0.01f)) ?? float.PositiveInfinity;
                float falloff = Falloff(distance, inner, outer);
                if (Invert) falloff = 1f - falloff;
                PlacementPoint changed = point;
                changed.Density *= falloff;
                result.AddFrom(input, i, changed);
            }
            outputs[0] = result;
        }
    }
}
