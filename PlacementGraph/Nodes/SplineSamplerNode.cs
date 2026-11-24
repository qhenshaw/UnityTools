#if UNITYTOOLS_SPLINES
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace UnityTools.Placement
{
    /// <summary>
    /// Source: points from the Spline Container set on the Placement Graph component
    /// (<see cref="PlacementContext.Spline"/>). Along places points every Spacing metres along each spline, facing
    /// along it. Inside fills the volume with a jittered grid and keeps the points inside each closed spline's outline
    /// seen from above. Only compiled when the Splines package (com.unity.splines) is installed.
    /// </summary>
    [Serializable]
    public class SplineSamplerNode : PlacementNode
    {
        public enum SampleMode
        {
            /// <summary>Points along the spline, every Spacing metres.</summary>
            Along,
            /// <summary>A grid of points inside the closed spline's outline, seen from above.</summary>
            Inside
        }

        public SampleMode Mode = SampleMode.Along;
        [Min(0.01f)] public float Spacing = 1f;
        [Tooltip("Inside only: how far grid points move from their cell centres.")]
        [Range(0f, 1f)] public float Jitter = 0.5f;
        [Tooltip("Inside only: metres inward from the outline over which the Edge Falloff attribute rises to 1. 0 turns it off.")]
        [Min(0f)] public float EdgeFalloffDistance = 0f;
        [Tooltip("Inside only: how the Edge Falloff attribute rises from the edge.")]
        public FalloffCurve EdgeFalloffCurve = FalloffCurve.Linear;
        [Tooltip("Inside only: the Edge Falloff value at the outline. Above 0 keeps a minimum, e.g. for scale.")]
        [Range(0f, 1f)] public float EdgeFalloffMin = 0f;
        [Tooltip("Inside only: the Float attribute the edge falloff is written to.")]
        public string EdgeFalloffAttribute = EdgeFalloff.DefaultAttribute;

        /// <summary>Segments per spline when its outline is traced for Inside.</summary>
        private const int OutlineSegments = 128;

        public override int InputCount => 0;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            SplineContainer container = context.Spline;
            if (container == null || container.Splines.Count == 0)
            {
                context.Warn(this, "No spline. Set Spline on the Placement Graph component to a Spline Container.");
                outputs[0] = PointData.Empty;
                return;
            }
            outputs[0] = Mode == SampleMode.Along ? SampleAlong(context, container) : SampleInside(context, container);
        }

        private PointData SampleAlong(PlacementContext context, SplineContainer container)
        {
            uint seed = PlacementRandom.Hash(context.SeedHash, NodeSeed);
            float spacing = Mathf.Max(0.01f, Spacing);
            var result = new PointData();
            for (int s = 0; s < container.Splines.Count; s++)
            {
                float length = container.CalculateLength(s);
                if (length <= 0f) continue;
                int count = Mathf.FloorToInt(length / spacing) + 1;
                if (result.Count + count > GridSampling.MaxPoints)
                {
                    context.Warn(this, $"Spacing {spacing} would make more than {GridSampling.MaxPoints} points. Increase Spacing.");
                    return PointData.Empty;
                }
                // A closed spline ends where it starts, so it doesn't get a second point there.
                if (container.Splines[s].Closed && count > 1 && Mathf.Approximately((count - 1) * spacing, length)) count--;
                float[] distances = DistanceTable(container, s, Mathf.Clamp(count * 8, 64, 65536));
                length = distances[distances.Length - 1];
                for (int i = 0; i < count; i++)
                {
                    float t = TAtDistance(distances, i * spacing);
                    if (!container.Evaluate(s, t, out float3 position, out float3 tangent, out float3 up)) continue;
                    Vector3 forward = ((Vector3)tangent).sqrMagnitude > 1e-8f ? ((Vector3)tangent).normalized : Direction(container, s, t);
                    Vector3 upVector = ((Vector3)up).sqrMagnitude > 1e-8f ? ((Vector3)up).normalized : Vector3.up;
                    PlacementPoint point = PlacementPoint.At(position, PlacementRandom.Hash(seed, unchecked((uint)s), unchecked((uint)i)));
                    point.Rotation = Quaternion.LookRotation(forward, upVector);
                    point.Normal = upVector;
                    result.Points.Add(point);
                }
            }
            return result;
        }

        /// <summary>World-space distance along the spline at <c>samples + 1</c> evenly spaced values of t.</summary>
        private static float[] DistanceTable(SplineContainer container, int spline, int samples)
        {
            var distances = new float[samples + 1];
            Vector3 previous = container.EvaluatePosition(spline, 0f);
            for (int i = 1; i <= samples; i++)
            {
                Vector3 position = container.EvaluatePosition(spline, i / (float)samples);
                distances[i] = distances[i - 1] + Vector3.Distance(previous, position);
                previous = position;
            }
            return distances;
        }

        /// <summary>The t at a distance along the spline, so points are evenly spaced however the knots are placed.</summary>
        private static float TAtDistance(float[] distances, float distance)
        {
            int samples = distances.Length - 1;
            if (distance <= 0f) return 0f;
            if (distance >= distances[samples]) return 1f;
            int index = Array.BinarySearch(distances, distance);
            if (index >= 0) return index / (float)samples;
            int upper = ~index;
            int lower = upper - 1;
            float span = distances[upper] - distances[lower];
            float fraction = span > 0f ? (distance - distances[lower]) / span : 0f;
            return (lower + fraction) / samples;
        }

        /// <summary>
        /// The direction of travel from nearby positions, for where the tangent is zero (the ends of a spline whose
        /// knots have no tangents).
        /// </summary>
        private static Vector3 Direction(SplineContainer container, int spline, float t)
        {
            const float step = 0.001f;
            Vector3 ahead = container.EvaluatePosition(spline, Mathf.Clamp01(t + step));
            Vector3 behind = container.EvaluatePosition(spline, Mathf.Clamp01(t - step));
            Vector3 direction = ahead - behind;
            return direction.sqrMagnitude > 1e-12f ? direction.normalized : Vector3.forward;
        }

        private PointData SampleInside(PlacementContext context, SplineContainer container)
        {
            var outlines = new List<Vector2[]>();
            for (int s = 0; s < container.Splines.Count; s++)
            {
                if (!container.Splines[s].Closed) continue;
                var outline = new Vector2[OutlineSegments];
                for (int i = 0; i < OutlineSegments; i++)
                {
                    float3 position = container.EvaluatePosition(s, i / (float)OutlineSegments);
                    outline[i] = new Vector2(position.x, position.z);
                }
                outlines.Add(outline);
            }
            if (outlines.Count == 0)
            {
                context.Warn(this, "Inside needs a closed spline. Turn on Closed for the spline.");
                return PointData.Empty;
            }

            PointData grid = GridSampling.SampleTop(context, this, Spacing, Jitter);
            var result = new PointData();
            bool falloff = EdgeFalloffDistance > 0f && !string.IsNullOrEmpty(EdgeFalloffAttribute);
            PointAttribute<float> edge = falloff ? result.GetOrAddAttribute<float>(EdgeFalloffAttribute) : null;
            foreach (PlacementPoint point in grid.Points)
            {
                var flat = new Vector2(point.Position.x, point.Position.z);
                foreach (Vector2[] outline in outlines)
                {
                    if (!Contains(outline, flat)) continue;
                    if (falloff)
                    {
                        float inside = EdgeFalloff.DistanceToOutline(outline, flat);
                        edge.Set(result.Count, EdgeFalloff.Evaluate(inside, EdgeFalloffDistance, EdgeFalloffCurve, EdgeFalloffMin));
                    }
                    result.Points.Add(point);
                    break;
                }
            }
            return result;
        }

        /// <summary>Even-odd test: whether <paramref name="point"/> is inside the polygon.</summary>
        internal static bool Contains(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
#endif
