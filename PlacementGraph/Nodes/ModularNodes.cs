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

        /// <summary>What Subdivide puts in an opening.</summary>
        public enum OpeningMode
        {
            /// <summary>Nothing: the wall is left open.</summary>
            Gap,
            /// <summary>The module with the Opening Symbol, e.g. a door.</summary>
            Module
        }
    }

    /// <summary>
    /// Splits polyline segments into modules following a grammar such as <c>[Corner][Wall]*[Door][Wall]*[Corner]</c>
    /// (see <see cref="ModuleGrammar"/>). Each segment is filled exactly: repeated modules are scaled slightly along
    /// their length to close the gap (or left at their size with Scale To Fit off). Output points carry the Prefab
    /// attribute (so Spawn Prefabs and Spawn Instanced place the modules), ModuleIndex, and BoundsMin and BoundsMax
    /// (the module's measured bounds, scaled).
    /// <para>
    /// Openings (optional, second input) cut doorways and hallways into the walls: Placement Zones from Scene Input
    /// open the part of each segment their shape crosses, and plain points open Opening Width around where they
    /// project onto their nearest segment. Each segment is split at its openings and every remaining run is filled with
    /// the grammar; the opening is left empty (Gap) or gets the Opening Symbol's module (Module), whose points carry
    /// Opening = 1. Every opening also comes out of the Gaps output as one point at its centre, along the wall, with
    /// Opening = 1, Width and bounds, for door frames or hallway pieces.
    /// </para>
    /// </summary>
    [Serializable]
    public class SubdivideNode : PlacementNode
    {
        public const string ModuleIndexAttribute = "ModuleIndex";
        public const string BoundsMinAttribute = "BoundsMin";
        public const string BoundsMaxAttribute = "BoundsMax";
        public const string OpeningAttribute = "Opening";
        public const string WidthAttribute = "Width";

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
        [Tooltip("Openings: leave them empty (Gap), or place the Opening Symbol's module in them (Module).")]
        public Subdivide.OpeningMode OpeningMode = Subdivide.OpeningMode.Gap;
        [Tooltip("Openings, Module mode: the symbol of the module placed in each opening, e.g. Door.")]
        public string OpeningSymbol = "Door";
        [Tooltip("Openings: how wide an opening from a plain point is, in metres (zones use the part of the wall they cross).")]
        [Min(0f)] public float OpeningWidth = 2f;
        [Tooltip("Openings: make each opening exactly as wide as the Opening Symbol's module, centred where the zone or point is.")]
        public bool FitToModule;
        [Tooltip("Openings: how far a zone or point can be from a wall, in metres, and still open it.")]
        [Min(0f)] public float MaxDistance = 1f;

        public override int InputCount => 2;
        public override int OutputCount => 2;

        /// <summary>An opening on one segment: from <see cref="Start"/> to <see cref="End"/> metres along it.</summary>
        private struct Span
        {
            public float Start;
            public float End;
            public Span(float start, float end) { Start = start; End = end; }
        }

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            var result = new PointData();
            var gaps = new PointData();
            outputs[0] = result;
            outputs[1] = gaps;
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
            List<(List<Vector3> points, bool closed)> lines = Polylines.Gather(context, this, Source, inputs[0], Closed);

            // Openings: zones stand for their shape; plain points open a width around their projection.
            PointData openings = inputs.Length > 1 && inputs[1] != null ? inputs[1] : PointData.Empty;
            var zones = new List<PlacementZone>();
            var plain = new List<Vector3>();
            PointAttribute<UnityEngine.Object> zoneColumn = openings.GetAttribute<UnityEngine.Object>(SceneInputNode.ZoneAttribute);
            for (int i = 0; i < openings.Count; i++)
            {
                if (zoneColumn?.Get(i) is PlacementZone zone && zone != null)
                {
                    if (!zones.Contains(zone)) zones.Add(zone);
                }
                else plain.Add(openings.Points[i].Position);
            }
            bool hasOpenings = zones.Count > 0 || plain.Count > 0;
            int openingModule = -1;
            float openingSize = OpeningWidth;
            if (hasOpenings && (OpeningMode == Subdivide.OpeningMode.Module || FitToModule))
            {
                openingModule = FindModule(OpeningSymbol);
                if (openingModule >= 0) openingSize = Modules[openingModule].LengthAlong(axis);
                else if (OpeningMode == Subdivide.OpeningMode.Module)
                    context.Warn(this, $"No module has the Opening Symbol '{OpeningSymbol}', so openings are left empty.");
            }
            Dictionary<(int line, int segment), List<Vector3>> pointsBySegment = hasOpenings ? AssignToSegments(lines, plain) : null;

            PointAttribute<int> openingFlags = hasOpenings ? result.GetOrAddAttribute<int>(OpeningAttribute) : null;
            PointAttribute<int> gapFlags = gaps.GetOrAddAttribute<int>(OpeningAttribute);
            PointAttribute<float> gapWidths = gaps.GetOrAddAttribute<float>(WidthAttribute);
            PointAttribute<Vector3> gapMin = gaps.GetOrAddAttribute<Vector3>(BoundsMinAttribute);
            PointAttribute<Vector3> gapMax = gaps.GetOrAddAttribute<Vector3>(BoundsMaxAttribute);

            foreach (PlacementZone zone in zones) zone.BeginQueries();
            try
            {
                int segmentNumber = 0;
                for (int l = 0; l < lines.Count; l++)
                {
                    (List<Vector3> points, bool closed) = lines[l];
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
                        Quaternion rotation = Polylines.AlongAxis(direction, axis);
                        float from = trimStart, to = length - trimEnd;

                        List<Span> spans = hasOpenings
                            ? Spans(context, a, direction, length, from, to, zones,
                                pointsBySegment.TryGetValue((l, s), out List<Vector3> near) ? near : null, openingSize)
                            : null;

                        // The runs between openings, each filled with the grammar. With no openings it's the whole
                        // segment with the segment's own seed, exactly as before Openings existed.
                        float cursor = from;
                        int runCount = (spans?.Count ?? 0) + 1;
                        for (int r = 0; r < runCount; r++)
                        {
                            float runEnd = spans != null && r < spans.Count ? spans[r].Start : to;
                            float runLength = runEnd - cursor;
                            if (runLength > 1e-3f)
                            {
                                uint runSeed = r == 0 ? segmentSeed : PlacementRandom.Hash(segmentSeed, unchecked((uint)r), 991u);
                                if (!grammar.TryFit(runLength, Modules, axis, ScaleToFit,
                                        pick => PlacementRandom.Value(PlacementRandom.Hash(runSeed, unchecked((uint)pick), 77u)), slots, out error))
                                {
                                    context.Warn(this, error);
                                    return;
                                }
                                for (int k = 0; k < slots.Count; k++)
                                {
                                    ModuleGrammar.Slot slot = slots[k];
                                    AddModule(result, prefabs, indices, boundsMin, boundsMax, measured, a, direction, rotation, axis,
                                        cursor + slot.Start, slot.Length, slot.Scale, slot.Module, PlacementRandom.Hash(runSeed, unchecked((uint)k)));
                                    openingFlags?.Set(result.Count - 1, 0);
                                }
                            }
                            if (spans == null || r >= spans.Count) break;

                            Span span = spans[r];
                            float width = span.End - span.Start;
                            uint openingSeed = PlacementRandom.Hash(segmentSeed, unchecked((uint)r), 503u);
                            if (OpeningMode == Subdivide.OpeningMode.Module && openingModule >= 0)
                            {
                                int module = PickModule(OpeningSymbol, PlacementRandom.Value(PlacementRandom.Hash(openingSeed, 77u)));
                                float natural = Modules[module].LengthAlong(axis);
                                float scale = ScaleToFit ? width / natural : 1f;
                                float slotLength = natural * scale;
                                AddModule(result, prefabs, indices, boundsMin, boundsMax, measured, a, direction, rotation, axis,
                                    span.Start + (width - slotLength) * 0.5f, slotLength, scale, module, openingSeed);
                                openingFlags.Set(result.Count - 1, 1);
                            }

                            PlacementPoint gap = PlacementPoint.At(a + direction * (span.Start + width * 0.5f), PlacementRandom.Hash(openingSeed, 1u));
                            gap.Rotation = rotation;
                            gap.Normal = Vector3.up;
                            int g = gaps.Count;
                            gaps.Points.Add(gap);
                            gapFlags.Set(g, 1);
                            gapWidths.Set(g, width);
                            Vector3 half = Vector3.zero;
                            half[axis] = width * 0.5f;
                            gapMin.Set(g, -half);
                            gapMax.Set(g, half);
                            cursor = span.End;
                        }
                    }
                }
            }
            finally
            {
                foreach (PlacementZone zone in zones) zone.EndQueries();
            }
        }

        private void AddModule(PointData result, PointAttribute<UnityEngine.Object> prefabs, PointAttribute<int> indices,
            PointAttribute<Vector3> boundsMin, PointAttribute<Vector3> boundsMax, Bounds[] measured, Vector3 a, Vector3 direction,
            Quaternion rotation, int axis, float start, float slotLength, float slotScale, int moduleIndex, uint pointSeed)
        {
            ModuleDefinition module = Modules[moduleIndex];
            Vector3 centre = a + direction * (start + slotLength * 0.5f);
            PlacementPoint point = PlacementPoint.At(centre, pointSeed);
            // The module's own centre along its axis sits at the slot centre.
            Vector3 scale = Vector3.one;
            scale[axis] = slotScale;
            Vector3 pivotShift = Vector3.zero;
            pivotShift[axis] = -measured[moduleIndex].center[axis] * slotScale;
            point.Rotation = rotation * Quaternion.Euler(module.RotationOffset);
            point.Position = centre + rotation * (pivotShift + module.PositionOffset);
            point.Scale = scale;
            point.Normal = Vector3.up;
            int index = result.Count;
            result.Points.Add(point);
            prefabs.Set(index, module.Prefab);
            indices.Set(index, moduleIndex);
            boundsMin.Set(index, Vector3.Scale(measured[moduleIndex].min, scale));
            boundsMax.Set(index, Vector3.Scale(measured[moduleIndex].max, scale));
        }

        private bool Usable(int m, string symbol) =>
            Modules[m] != null && Modules[m].Prefab != null && Modules[m].Symbol == symbol && Modules[m].Weight > 0f;

        private int FindModule(string symbol)
        {
            for (int m = 0; m < Modules.Count; m++) if (Usable(m, symbol)) return m;
            return -1;
        }

        /// <summary>A module with <paramref name="symbol"/>, chosen by weight with <paramref name="random"/> in [0, 1).</summary>
        private int PickModule(string symbol, float random)
        {
            float total = 0f;
            for (int m = 0; m < Modules.Count; m++) if (Usable(m, symbol)) total += Modules[m].Weight;
            float target = random * total;
            int last = -1;
            for (int m = 0; m < Modules.Count; m++)
            {
                if (!Usable(m, symbol)) continue;
                last = m;
                if (target < Modules[m].Weight) return m;
                target -= Modules[m].Weight;
            }
            return last;
        }

        /// <summary>Each plain opening point goes to the segment nearest it (seen from above), if within Max Distance.</summary>
        private Dictionary<(int, int), List<Vector3>> AssignToSegments(List<(List<Vector3> points, bool closed)> lines, List<Vector3> plain)
        {
            var bySegment = new Dictionary<(int, int), List<Vector3>>();
            foreach (Vector3 p in plain)
            {
                float best = float.PositiveInfinity;
                (int, int) bestKey = (-1, -1);
                for (int l = 0; l < lines.Count; l++)
                {
                    (List<Vector3> points, bool closed) = lines[l];
                    int segments = closed ? points.Count : points.Count - 1;
                    for (int s = 0; s < segments; s++)
                    {
                        float distance = FlatDistanceToSegment(p, points[s], points[(s + 1) % points.Count]);
                        // Ties (a point at a corner) go to the earlier segment, so the result doesn't depend on float noise.
                        if (distance < best - 1e-5f)
                        {
                            best = distance;
                            bestKey = (l, s);
                        }
                    }
                }
                if (bestKey.Item1 < 0 || best > MaxDistance) continue;
                if (!bySegment.TryGetValue(bestKey, out List<Vector3> list)) bySegment[bestKey] = list = new List<Vector3>();
                list.Add(p);
            }
            return bySegment;
        }

        private static float FlatDistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var flatA = new Vector2(a.x, a.z);
            var ab = new Vector2(b.x, b.z) - flatA;
            var ap = new Vector2(p.x, p.z) - flatA;
            float t = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector2.Dot(ap, ab) / ab.sqrMagnitude) : 0f;
            return (ap - ab * t).magnitude;
        }

        /// <summary>
        /// The openings on one segment, as merged spans inside [<paramref name="from"/>, <paramref name="to"/>] metres
        /// along it, sorted. Spans that reach past the usable part (a corner, or Corner Trim) are clamped, with a warning.
        /// </summary>
        private List<Span> Spans(PlacementContext context, Vector3 a, Vector3 direction, float length, float from, float to,
            List<PlacementZone> zones, List<Vector3> points, float openingSize)
        {
            var raw = new List<(Span span, float centre)>();
            foreach (PlacementZone zone in zones)
            {
                if (TryZoneSpan(zone, a, direction, length, out Span span)) raw.Add((span, (span.Start + span.End) * 0.5f));
            }
            if (points != null)
            {
                var flatDirection = new Vector2(direction.x, direction.z).normalized;
                foreach (Vector3 p in points)
                {
                    float t = Vector2.Dot(new Vector2(p.x - a.x, p.z - a.z), flatDirection);
                    raw.Add((new Span(t - OpeningWidth * 0.5f, t + OpeningWidth * 0.5f), t));
                }
            }
            if (FitToModule)
            {
                for (int i = 0; i < raw.Count; i++)
                    raw[i] = (new Span(raw[i].centre - openingSize * 0.5f, raw[i].centre + openingSize * 0.5f), raw[i].centre);
            }

            var spans = new List<Span>();
            bool clamped = false;
            foreach ((Span span, float _) in raw)
            {
                if (span.End <= from + 1e-4f || span.Start >= to - 1e-4f)
                {
                    // Entirely in the trimmed corner (or past the segment): nothing of this wall to open.
                    if (span.End > 0f && span.Start < length) clamped = true;
                    continue;
                }
                var kept = new Span(Mathf.Max(span.Start, from), Mathf.Min(span.End, to));
                if (kept.Start > span.Start + 1e-4f || kept.End < span.End - 1e-4f) clamped = true;
                if (kept.End - kept.Start > 1e-3f) spans.Add(kept);
            }
            if (clamped) context.Warn(this, "An opening runs past the end of its wall (a corner, or Corner Trim); it was cut back to that wall.");

            spans.Sort((x, y) => x.Start.CompareTo(y.Start));
            var merged = new List<Span>(spans.Count);
            foreach (Span span in spans)
            {
                if (merged.Count > 0 && span.Start <= merged[merged.Count - 1].End + 1e-4f)
                {
                    Span last = merged[merged.Count - 1];
                    merged[merged.Count - 1] = new Span(last.Start, Mathf.Max(last.End, span.End));
                }
                else merged.Add(span);
            }
            return merged;
        }

        /// <summary>
        /// Where <paramref name="zone"/>'s shape crosses the segment, seen from above: sampled along the segment and
        /// refined at the edges. A zone that doesn't cross it but comes within Max Distance opens the part its footprint
        /// covers. False when the zone is further away.
        /// </summary>
        private bool TryZoneSpan(PlacementZone zone, Vector3 a, Vector3 direction, float length, out Span span)
        {
            span = default;
            int samples = Mathf.Clamp(Mathf.CeilToInt(length / 0.05f), 8, 4000);
            float step = length / samples;
            float first = float.NaN, last = float.NaN, nearest = float.PositiveInfinity;
            bool previousInside = false;
            for (int i = 0; i <= samples; i++)
            {
                float t = i * step;
                float distance = zone.SignedDistanceFromAbove(a + direction * t);
                nearest = Mathf.Min(nearest, distance);
                bool inside = distance <= 0f;
                if (inside && float.IsNaN(first)) first = i == 0 ? -0.01f : Refine(zone, a, direction, t - step, t); // Inside at the start: past it.
                if (!inside && previousInside) last = Refine(zone, a, direction, t, t - step);
                previousInside = inside;
            }
            if (!float.IsNaN(first))
            {
                if (float.IsNaN(last)) last = length + 0.01f; // Still inside at the end: it runs past the wall's end, which Spans reports.
                span = new Span(first, last);
                return true;
            }
            if (nearest > MaxDistance) return false;
            // Beside the wall: the footprint's extent along it.
            Bounds bounds = zone.WorldBounds;
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            for (int corner = 0; corner < 4; corner++)
            {
                var p = new Vector3((corner & 1) == 0 ? bounds.min.x : bounds.max.x, a.y, (corner & 2) == 0 ? bounds.min.z : bounds.max.z);
                float t = Vector3.Dot(p - a, direction);
                min = Mathf.Min(min, t);
                max = Mathf.Max(max, t);
            }
            span = new Span(min, max);
            return max > 0f && min < length;
        }

        /// <summary>The distance along the segment between <paramref name="outside"/> and <paramref name="inside"/> where the zone's edge is.</summary>
        private static float Refine(PlacementZone zone, Vector3 a, Vector3 direction, float outside, float inside)
        {
            for (int i = 0; i < 12; i++)
            {
                float middle = (outside + inside) * 0.5f;
                if (zone.SignedDistanceFromAbove(a + direction * middle) <= 0f) inside = middle;
                else outside = middle;
            }
            return (outside + inside) * 0.5f;
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
