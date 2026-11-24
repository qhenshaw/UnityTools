using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// An authored area a Placement Graph can read with a Scene Input node: a box, a sphere or (with the Splines package)
    /// a closed spline seen from above, with an optional label. Typical use: Difference carves the zone out of a
    /// scatter, so a hand-placed rock cluster isn't buried in grass.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("UnityTools/Placement Zone")]
    public class PlacementZone : MonoBehaviour
    {
        public enum ZoneShape
        {
            Box,
            Sphere,
#if UNITYTOOLS_SPLINES
            /// <summary>The area inside a closed spline on this object (a Spline Container), seen from above.</summary>
            Spline
#endif
        }

        [Tooltip("A name Scene Input nodes can look zones up by. Empty matches only a Scene Input with an empty label.")]
        [SerializeField] private string _label = "";
        [SerializeField] private ZoneShape _shape = ZoneShape.Box;
        [Tooltip("Box size in local space.")]
        [SerializeField] private Vector3 _size = Vector3.one * 4f;
        [Tooltip("Sphere radius, scaled by the largest axis of the object's scale.")]
        [SerializeField, Min(0f)] private float _radius = 2f;

        private static readonly List<PlacementZone> Enabled = new List<PlacementZone>();

        /// <summary>Every enabled zone, in any scene.</summary>
        public static IReadOnlyList<PlacementZone> All => Enabled;

        public string Label { get => _label; set => _label = value ?? ""; }
        public ZoneShape Shape { get => _shape; set => _shape = value; }
        public Vector3 Size { get => _size; set => _size = Vector3.Max(Vector3.zero, value); }
        public float Radius { get => _radius; set => _radius = Mathf.Max(0f, value); }

        private void OnEnable()
        {
            if (!Enabled.Contains(this)) Enabled.Add(this);
        }

        private void OnDisable() => Enabled.Remove(this);

        private float WorldRadius => _radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y), Mathf.Abs(transform.lossyScale.z));

        /// <summary>
        /// Distance from <paramref name="world"/> to the zone's surface: negative inside, positive outside. Boxes use the
        /// distance in the object's space (exact for unscaled or uniformly scaled boxes); splines are measured in XZ.
        /// </summary>
        public float SignedDistance(Vector3 world)
        {
            switch (_shape)
            {
                case ZoneShape.Sphere:
                    return Vector3.Distance(world, transform.position) - WorldRadius;
#if UNITYTOOLS_SPLINES
                case ZoneShape.Spline:
                    return SplineDistance(world);
#endif
                default:
                {
                    // Box: in local space, scaled back to metres by the object's scale.
                    Vector3 local = transform.InverseTransformPoint(world);
                    Vector3 half = _size * 0.5f;
                    Vector3 scale = transform.lossyScale;
                    var q = new Vector3(
                        (Mathf.Abs(local.x) - half.x) * Mathf.Abs(scale.x),
                        (Mathf.Abs(local.y) - half.y) * Mathf.Abs(scale.y),
                        (Mathf.Abs(local.z) - half.z) * Mathf.Abs(scale.z));
                    Vector3 outside = Vector3.Max(q, Vector3.zero);
                    return outside.magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
                }
            }
        }

        /// <summary>Whether <paramref name="world"/> is inside the zone.</summary>
        public bool Contains(Vector3 world) => SignedDistance(world) <= 0f;

        /// <summary>The zone's world-space bounds.</summary>
        public Bounds WorldBounds
        {
            get
            {
                switch (_shape)
                {
                    case ZoneShape.Sphere:
                        return new Bounds(transform.position, Vector3.one * (WorldRadius * 2f));
#if UNITYTOOLS_SPLINES
                    case ZoneShape.Spline:
                    {
                        var container = GetComponent<UnityEngine.Splines.SplineContainer>();
                        if (container == null || container.Splines.Count == 0) return new Bounds(transform.position, Vector3.zero);
                        Bounds bounds = UnityEngine.Splines.SplineUtility.GetBounds(container.Splines[0], transform.localToWorldMatrix);
                        for (int i = 1; i < container.Splines.Count; i++)
                            bounds.Encapsulate(UnityEngine.Splines.SplineUtility.GetBounds(container.Splines[i], transform.localToWorldMatrix));
                        return bounds;
                    }
#endif
                    default:
                    {
                        Bounds bounds = new Bounds(transform.TransformPoint(new Vector3(-_size.x, -_size.y, -_size.z) * 0.5f), Vector3.zero);
                        for (int corner = 1; corner < 8; corner++)
                        {
                            var local = new Vector3((corner & 1) == 0 ? -0.5f : 0.5f, (corner & 2) == 0 ? -0.5f : 0.5f, (corner & 4) == 0 ? -0.5f : 0.5f);
                            bounds.Encapsulate(transform.TransformPoint(Vector3.Scale(local, _size)));
                        }
                        return bounds;
                    }
                }
            }
        }

        /// <summary>A hash of the zone's shape and placement, to notice when it changes.</summary>
        public int StateHash
        {
            get
            {
                unchecked
                {
                    int hash = transform.localToWorldMatrix.GetHashCode();
                    hash = hash * 31 + _shape.GetHashCode();
                    hash = hash * 31 + _size.GetHashCode();
                    hash = hash * 31 + _radius.GetHashCode();
                    hash = hash * 31 + (_label?.GetHashCode() ?? 0);
                    hash = hash * 31 + (isActiveAndEnabled ? 1 : 0);
#if UNITYTOOLS_SPLINES
                    var container = GetComponent<UnityEngine.Splines.SplineContainer>();
                    if (_shape == ZoneShape.Spline && container != null)
                    {
                        foreach (UnityEngine.Splines.Spline spline in container.Splines)
                            foreach (UnityEngine.Splines.BezierKnot knot in spline.Knots) hash = hash * 31 + knot.GetHashCode();
                    }
#endif
                    return hash;
                }
            }
        }

#if UNITYTOOLS_SPLINES
        private float SplineDistance(Vector3 world)
        {
            var container = GetComponent<UnityEngine.Splines.SplineContainer>();
            if (container == null) return float.PositiveInfinity;
            float best = float.PositiveInfinity;
            var flat = new Vector2(world.x, world.z);
            const int segments = 64;
            for (int s = 0; s < container.Splines.Count; s++)
            {
                if (!container.Splines[s].Closed) continue;
                var outline = new Vector2[segments];
                for (int i = 0; i < segments; i++)
                {
                    Vector3 p = container.EvaluatePosition(s, i / (float)segments);
                    outline[i] = new Vector2(p.x, p.z);
                }
                float edge = float.PositiveInfinity;
                for (int i = 0, j = segments - 1; i < segments; j = i++)
                    edge = Mathf.Min(edge, DistanceToSegment(flat, outline[j], outline[i]));
                float signed = SplineSamplerNode.Contains(outline, flat) ? -edge : edge;
                best = Mathf.Min(best, signed);
            }
            return best;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }
#endif

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.6f);
            switch (_shape)
            {
                case ZoneShape.Sphere:
                    Gizmos.DrawWireSphere(transform.position, WorldRadius);
                    break;
#if UNITYTOOLS_SPLINES
                case ZoneShape.Spline:
                    Bounds bounds = WorldBounds;
                    Gizmos.DrawWireCube(bounds.center, bounds.size);
                    break;
#endif
                default:
                    Gizmos.matrix = transform.localToWorldMatrix;
                    Gizmos.DrawWireCube(Vector3.zero, _size);
                    Gizmos.matrix = Matrix4x4.identity;
                    break;
            }
        }
    }
}
