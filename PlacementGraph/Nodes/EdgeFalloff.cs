using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// How an edge falloff rises from the edge of an area to its full value: Edge Falloff on Spline Sampler (Inside) and
    /// Scene Input (Points Inside Zones).
    /// </summary>
    public enum FalloffCurve
    {
        /// <summary>Straight from the edge value to 1.</summary>
        Linear,
        /// <summary>Smoothstep: flat at both ends.</summary>
        Smooth,
        /// <summary>Slow near the edge, fast near the full value (t squared).</summary>
        EaseIn,
        /// <summary>Fast near the edge, slow near the full value.</summary>
        EaseOut
    }

    /// <summary>The edge falloff value of a point from its distance inside an area's edge.</summary>
    public static class EdgeFalloff
    {
        /// <summary>The attribute an edge falloff is written to unless another is named.</summary>
        public const string DefaultAttribute = "EdgeFalloff";

        /// <summary>
        /// <paramref name="edgeValue"/> at the edge, rising to 1 at <paramref name="distance"/> metres inside along
        /// <paramref name="curve"/>. 1 everywhere when the distance is 0 or less.
        /// </summary>
        public static float Evaluate(float inside, float distance, FalloffCurve curve, float edgeValue)
        {
            if (distance <= 0f) return 1f;
            float t = Mathf.Clamp01(inside / distance);
            switch (curve)
            {
                case FalloffCurve.Smooth: t = t * t * (3f - 2f * t); break;
                case FalloffCurve.EaseIn: t *= t; break;
                case FalloffCurve.EaseOut: t = 1f - (1f - t) * (1f - t); break;
            }
            return Mathf.Lerp(edgeValue, 1f, t);
        }

        /// <summary>Distance from <paramref name="point"/> to the closest edge of a closed outline.</summary>
        public static float DistanceToOutline(Vector2[] outline, Vector2 point)
        {
            // Plain floats rather than Vector2 operators: this runs for every point of an area, often tens of thousands.
            float px = point.x, py = point.y;
            float best = float.PositiveInfinity;
            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
            {
                float ax = outline[j].x, ay = outline[j].y;
                float abx = outline[i].x - ax, aby = outline[i].y - ay;
                float apx = px - ax, apy = py - ay;
                float lengthSq = abx * abx + aby * aby;
                float t = lengthSq > 0f ? (apx * abx + apy * aby) / lengthSq : 0f;
                if (t < 0f) t = 0f;
                else if (t > 1f) t = 1f;
                float dx = apx - abx * t, dy = apy - aby * t;
                float d = dx * dx + dy * dy;
                if (d < best) best = d;
            }
            return Mathf.Sqrt(best);
        }
    }
}
