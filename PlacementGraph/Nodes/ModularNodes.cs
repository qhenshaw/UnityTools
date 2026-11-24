using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>Shared polyline helpers for the modular nodes: segments, turn angles and corner orientation.</summary>
    internal static class Polylines
    {
        /// <summary>The polylines a modular node works on, as lists of world positions with whether each is closed.</summary>
        public static List<(List<Vector3> points, bool closed)> Gather(PlacementContext context, PlacementNode node,
            Subdivide.SourceMode source, PointData input, bool closed)
        {
            var lines = new List<(List<Vector3>, bool)>();
            switch (source)
            {
                case Subdivide.SourceMode.VolumeEdges:
                {
                    Bounds b = context.LocalBounds;
                    var corners = new List<Vector3>
                    {
                        context.LocalToWorldPoint(new Vector3(b.min.x, b.min.y, b.min.z)),
                        context.LocalToWorldPoint(new Vector3(b.min.x, b.min.y, b.max.z)),
                        context.LocalToWorldPoint(new Vector3(b.max.x, b.min.y, b.max.z)),
                        context.LocalToWorldPoint(new Vector3(b.max.x, b.min.y, b.min.z))
                    };
                    lines.Add((corners, true));
                    break;
                }
#if UNITYTOOLS_SPLINES
                case Subdivide.SourceMode.SplineKnots:
                {
                    UnityEngine.Splines.SplineContainer container = context.Spline;
                    if (container == null)
                    {
                        context.Warn(node, "No spline. Set Spline on the Placement Graph component.");
                        break;
                    }
                    foreach (UnityEngine.Splines.Spline spline in container.Splines)
                    {
                        var points = new List<Vector3>();
                        foreach (UnityEngine.Splines.BezierKnot knot in spline.Knots)
                            points.Add(container.transform.TransformPoint(knot.Position));
                        if (points.Count >= 2) lines.Add((points, spline.Closed));
                    }
                    break;
                }
#endif
                default:
                {
                    var points = new List<Vector3>(input.Count);
                    foreach (PlacementPoint point in input.Points) points.Add(point.Position);
                    if (points.Count >= 2) lines.Add((points, closed));
                    break;
                }
            }
            return lines;
        }

        /// <summary>
        /// The signed turn at vertex <paramref name="i"/> in degrees, seen from above: positive turns left
        /// (counter-clockwise), negative right. 0 at the open ends of an open polyline.
        /// </summary>
        public static float TurnAngle(List<Vector3> points, bool closed, int i)
        {
            int count = points.Count;
            if (!closed && (i == 0 || i == count - 1)) return 0f;
            Vector3 previous = points[(i - 1 + count) % count];
            Vector3 next = points[(i + 1) % count];
            var incoming = new Vector2(points[i].x - previous.x, points[i].z - previous.z);
            var outgoing = new Vector2(next.x - points[i].x, next.z - points[i].z);
            if (incoming.sqrMagnitude < 1e-10f || outgoing.sqrMagnitude < 1e-10f) return 0f;
            return Vector2.SignedAngle(incoming, outgoing);
        }

        /// <summary>The signed area of a closed polyline seen from above: positive when it winds counter-clockwise.</summary>
        public static float SignedArea(List<Vector3> points)
        {
            float area = 0f;
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
                area += points[j].x * points[i].z - points[i].x * points[j].z;
            return area * 0.5f;
        }

        /// <summary>A rotation whose local <paramref name="axis"/> (0 = X, 2 = Z) points along <paramref name="direction"/>, staying upright.</summary>
        public static Quaternion AlongAxis(Vector3 direction, int axis)
        {
            Vector3 flat = direction.sqrMagnitude > 1e-10f ? direction.normalized : Vector3.forward;
            Quaternion look = Quaternion.LookRotation(flat, Mathf.Abs(Vector3.Dot(flat, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up);
            return axis == 0 ? look * Quaternion.Euler(0f, -90f, 0f) : look;
        }
    }

    /// <summary>Holds the enums several modular nodes share.</summary>
    public static class Subdivide
    {
        public enum SourceMode
        {
            /// <summary>The input points, in order, as one polyline.</summary>
            InputPoints,
            /// <summary>The four bottom edges of the generation volume, as a closed loop.</summary>
            VolumeEdges,
#if UNITYTOOLS_SPLINES
            /// <summary>The knots of each spline in the component's Spline, joined by straight segments.</summary>
            SplineKnots
#endif
        }

        public enum Axis
        {
            /// <summary>The module's length runs along its local X axis.</summary>
            X = 0,
            /// <summary>The module's length runs along its local Z axis.</summary>
            Z = 2
        }
    }

    /// <summary>
    /// Splits polyline segments into modules following a grammar such as <c>[Corner][Wall]*[Door][Wall]*[Corner]</c>
    /// (see <see cref="ModuleGrammar"/>). Each segment is filled exactly: repeated modules are scaled slightly along
    /// their length to close the gap (or left at their size with Scale To Fit off). Output points carry the Prefab
    /// attribute (so Spawn Prefabs and Spawn Instanced place the modules), ModuleIndex, and BoundsMin and BoundsMax
    /// (the module's measured bounds, scaled).
    /// </summary>
    [Serializable]
    public class SubdivideNode : PlacementNode
    {
        public const string ModuleIndexAttribute = "ModuleIndex";
        public const string BoundsMinAttribute = "BoundsMin";
        public const string BoundsMaxAttribute = "BoundsMax";

        [Tooltip("The modules the grammar can use. In a graph: the Module nodes chained into Modules.")]
        public List<ModuleDefinition> Modules = new List<ModuleDefinition>();
        [Tooltip("For example [Corner][Wall]*[Door][Wall]*[Corner]. [A] = one module, [A]* = repeat to fill, [A]+ = at least one, [A,B] = a weighted choice.")]
        public string Grammar = "[Wall]*";
        public Subdivide.SourceMode Source = Subdivide.SourceMode.InputPoints;
        [Tooltip("Input Points: also join the last point back to the first.")]
        public bool Closed;
        [Tooltip("Which local axis of the modules runs along the segment.")]
        public Subdivide.Axis ModuleAxis = Subdivide.Axis.X;
        [Tooltip("Scale modules along their length so each segment is filled exactly. Off: modules keep their size and the gap is left at the end.")]
        public bool ScaleToFit = true;
        [Tooltip("Shorten segments by this many metres at each corner, to leave room for corner modules.")]
        [Min(0f)] public float CornerTrim;
        [Tooltip("Turns of at least this many degrees count as corners for Corner Trim.")]
        [Range(0f, 180f)] public float CornerAngle = 10f;

        public override int InputCount => 1;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            var result = new PointData();
            outputs[0] = result;
            if (!ModuleGrammar.TryParse(Grammar, out ModuleGrammar grammar, out string error))
            {
                context.Warn(this, error);
                return;
            }

            PointAttribute<UnityEngine.Object> prefabs = result.GetOrAddAttribute<UnityEngine.Object>(PointAttributeNames.Prefab);
            PointAttribute<int> indices = result.GetOrAddAttribute(ModuleIndexAttribute, -1);
            PointAttribute<Vector3> boundsMin = result.GetOrAddAttribute<Vector3>(BoundsMinAttribute);
            PointAttribute<Vector3> boundsMax = result.GetOrAddAttribute<Vector3>(BoundsMaxAttribute);
            var measured = new Bounds[Modules.Count];
            for (int m = 0; m < Modules.Count; m++) measured[m] = ModuleDefinition.MeasureBounds(Modules[m]?.Prefab);

            uint seed = PlacementRandom.Hash(context.SeedHash, NodeSeed);
            int axis = (int)ModuleAxis;
            var slots = new List<ModuleGrammar.Slot>();
            int segmentNumber = 0;
            foreach ((List<Vector3> points, bool closed) in Polylines.Gather(context, this, Source, inputs[0], Closed))
            {
                int segments = closed ? points.Count : points.Count - 1;
                for (int s = 0; s < segments; s++, segmentNumber++)
                {
                    Vector3 a = points[s], b = points[(s + 1) % points.Count];
                    Vector3 direction = b - a;
                    float length = direction.magnitude;
                    if (length < 1e-4f) continue;
                    direction /= length;
                    float trimStart = Mathf.Abs(Polylines.TurnAngle(points, closed, s)) >= CornerAngle && (closed || s > 0) ? CornerTrim : 0f;
                    int endIndex = (s + 1) % points.Count;
                    float trimEnd = Mathf.Abs(Polylines.TurnAngle(points, closed, endIndex)) >= CornerAngle && (closed || endIndex < points.Count - 1) ? CornerTrim : 0f;
                    float usable = length - trimStart - trimEnd;
                    if (usable <= 1e-4f) continue;

                    uint segmentSeed = PlacementRandom.Hash(seed, unchecked((uint)segmentNumber));
                    if (!grammar.TryFit(usable, Modules, axis, ScaleToFit,
                            pick => PlacementRandom.Value(PlacementRandom.Hash(segmentSeed, unchecked((uint)pick), 77u)), slots, out error))
                    {
                        context.Warn(this, error);
                        return;
                    }

                    Quaternion rotation = Polylines.AlongAxis(direction, axis);
                    for (int k = 0; k < slots.Count; k++)
                    {
                        ModuleGrammar.Slot slot = slots[k];
                        ModuleDefinition module = Modules[slot.Module];
                        Vector3 centre = a + direction * (trimStart + slot.Start + slot.Length * 0.5f);
                        PlacementPoint point = PlacementPoint.At(centre, PlacementRandom.Hash(segmentSeed, unchecked((uint)k)));
                        // The module's own centre along its axis sits at the slot centre.
                        Vector3 scale = Vector3.one;
                        scale[axis] = slot.Scale;
                        Vector3 pivotShift = Vector3.zero;
                        pivotShift[axis] = -measured[slot.Module].center[axis] * slot.Scale;
                        point.Rotation = rotation * Quaternion.Euler(module.RotationOffset);
                        point.Position = centre + rotation * (pivotShift + module.PositionOffset);
                        point.Scale = scale;
                        point.Normal = Vector3.up;
                        int index = result.Count;
                        result.Points.Add(point);
                        prefabs.Set(index, module.Prefab);
                        indices.Set(index, slot.Module);
                        boundsMin.Set(index, Vector3.Scale(measured[slot.Module].min, scale));
                        boundsMax.Set(index, Vector3.Scale(measured[slot.Module].max, scale));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Finds the corners of polylines: one point per vertex that turns at least Min Angle, rotated to the bisector, with
    /// the attributes CornerAngle (the signed turn in degrees, positive turning left seen from above) and Inside (1 for a
    /// concave corner of a closed loop, or a right turn of an open one; 0 otherwise).
    /// </summary>
    [Serializable]
    public class CornerNode : PlacementNode
    {
        public const string AngleAttribute = "CornerAngle";
        public const string InsideAttribute = "Inside";

        public Subdivide.SourceMode Source = Subdivide.SourceMode.InputPoints;
        [Tooltip("Input Points: also join the last point back to the first.")]
        public bool Closed;
        [Tooltip("Only turns of at least this many degrees are corners.")]
        [Range(0f, 180f)] public float MinAngle = 10f;

        public override int InputCount => 1;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            var result = new PointData();
            outputs[0] = result;
            PointAttribute<float> angles = result.GetOrAddAttribute<float>(AngleAttribute);
            PointAttribute<int> inside = result.GetOrAddAttribute<int>(InsideAttribute);
            uint seed = PlacementRandom.Hash(context.SeedHash, NodeSeed);
            int line = 0;
            foreach ((List<Vector3> points, bool closed) in Polylines.Gather(context, this, Source, inputs[0], Closed))
            {
                float winding = closed ? Mathf.Sign(Polylines.SignedArea(points)) : 1f;
                for (int i = 0; i < points.Count; i++)
                {
                    float turn = Polylines.TurnAngle(points, closed, i);
                    if (Mathf.Abs(turn) < MinAngle || Mathf.Abs(turn) < 1e-3f) continue;
                    Vector3 previous = points[(i - 1 + points.Count) % points.Count];
                    Vector3 next = points[(i + 1) % points.Count];
                    Vector3 bisector = (points[i] - previous).normalized + (next - points[i]).normalized;
                    PlacementPoint point = PlacementPoint.At(points[i], PlacementRandom.Hash(seed, unchecked((uint)line), unchecked((uint)i)));
                    point.Rotation = Polylines.AlongAxis(bisector, 2);
                    int index = result.Count;
                    result.Points.Add(point);
                    angles.Set(index, turn);
                    // A turn against the loop's winding is concave (inside); on open lines, right turns count as inside.
                    inside.Set(index, Mathf.Sign(turn) != winding ? 1 : 0);
                }
                line++;
            }
        }
    }

    /// <summary>
    /// Source: points on a regular grid in the volume: on its bottom face (2D) or filling it (3D). Cells are Cell Size
    /// apart, starting at the volume's corner plus Offset; each point's seed comes from its cell index.
    /// </summary>
    [Serializable]
    public class CreateGridNode : PlacementNode
    {
        [Tooltip("Distance between grid points along each axis, in metres (Y is used in 3D only).")]
        public Vector3 CellSize = Vector3.one;
        [Tooltip("Shifts the grid from the volume's minimum corner, in metres.")]
        public Vector3 Offset = Vector3.zero;
        [Tooltip("Fill the volume in Y too, instead of just the bottom face.")]
        public bool ThreeDimensional;

        public override int InputCount => 0;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            var result = new PointData();
            outputs[0] = result;
            Vector3 size = Vector3.Max(CellSize, Vector3.one * 0.01f);
            Bounds b = context.LocalBounds;
            int countX = Mathf.FloorToInt((b.size.x - Offset.x) / size.x) + 1;
            int countZ = Mathf.FloorToInt((b.size.z - Offset.z) / size.z) + 1;
            int countY = ThreeDimensional ? Mathf.FloorToInt((b.size.y - Offset.y) / size.y) + 1 : 1;
            long total = (long)Mathf.Max(0, countX) * Mathf.Max(0, countZ) * Mathf.Max(0, countY);
            if (total > GridSampling.MaxPoints)
            {
                context.Warn(this, $"The grid would have {total} points; the limit is {GridSampling.MaxPoints}. Increase Cell Size.");
                return;
            }
            uint seed = PlacementRandom.Hash(context.SeedHash, NodeSeed);
            Quaternion rotation = context.LocalToWorld.rotation;
            for (int y = 0; y < countY; y++)
            for (int z = 0; z < countZ; z++)
            for (int x = 0; x < countX; x++)
            {
                var local = new Vector3(b.min.x + Offset.x + x * size.x, b.min.y + Offset.y + y * size.y, b.min.z + Offset.z + z * size.z);
                if (local.x > b.max.x + 1e-4f || local.y > b.max.y + 1e-4f || local.z > b.max.z + 1e-4f) continue;
                PlacementPoint point = PlacementPoint.At(context.LocalToWorldPoint(local),
                    PlacementRandom.Hash(seed, unchecked((uint)x), unchecked((uint)y), unchecked((uint)z)));
                point.Rotation = rotation;
                result.Points.Add(point);
            }
        }
    }

    /// <summary>Moves each point to the nearest point of a grid (Cell Size apart, shifted by Offset), in world space. Y snaps only with Snap Y on.</summary>
    [Serializable]
    public class SnapToGridNode : PlacementNode
    {
        public Vector3 CellSize = Vector3.one;
        public Vector3 Offset = Vector3.zero;
        public bool SnapY;
        [Tooltip("Also snap the rotation about Y to multiples of this many degrees (0 = off).")]
        [Min(0f)] public float RotationStep;

        public override int InputCount => 1;
        public override int OutputCount => 1;

        public static float Snap(float value, float step, float offset) =>
            step <= 0f ? value : Mathf.Round((value - offset) / step) * step + offset;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            PointData input = inputs[0];
            var result = PointData.Derive(input, input.Count);
            for (int i = 0; i < input.Count; i++)
            {
                PlacementPoint point = input.Points[i];
                Vector3 p = point.Position;
                point.Position = new Vector3(Snap(p.x, CellSize.x, Offset.x), SnapY ? Snap(p.y, CellSize.y, Offset.y) : p.y, Snap(p.z, CellSize.z, Offset.z));
                if (RotationStep > 0f)
                {
                    Vector3 euler = point.Rotation.eulerAngles;
                    point.Rotation = Quaternion.Euler(euler.x, Snap(euler.y, RotationStep, 0f), euler.z);
                }
                result.AddFrom(input, i, point);
            }
            outputs[0] = result;
        }
    }
}
