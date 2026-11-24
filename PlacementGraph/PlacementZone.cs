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

        /// <summary>
        /// Like <see cref="SignedDistance"/>, but seen from above: the distance in XZ to the zone's sides, ignoring its
        /// top and bottom (a box's Y faces, a sphere's height). Edge falloffs use this, so a flat box falls off
        /// from its sides rather than from its thickness.
        /// </summary>
        public float SignedDistanceFromAbove(Vector3 world)
        {
            switch (_shape)
            {
                case ZoneShape.Sphere:
                {
                    Vector3 offset = world - transform.position;
                    return new Vector2(offset.x, offset.z).magnitude - WorldRadius;
                }
#if UNITYTOOLS_SPLINES
                case ZoneShape.Spline:
                {
                    // The outline the point is inside decides; the distance loop is the fast one edge falloffs use.
                    var flat = new Vector2(world.x, world.z);
                    List<Vector2[]> outlines = Outlines();
                    if (outlines.Count == 0) return float.PositiveInfinity;
                    float outside = float.PositiveInfinity;
                    foreach (Vector2[] outline in outlines)
                    {
                        float edge = EdgeFalloff.DistanceToOutline(outline, flat);
                        if (SplineSamplerNode.Contains(outline, flat)) return -edge;
                        outside = Mathf.Min(outside, edge);
                    }
                    return outside;
                }
#endif
                default:
                {
                    Vector3 local = transform.InverseTransformPoint(world);
                    Vector3 half = _size * 0.5f;
                    Vector3 scale = transform.lossyScale;
                    var q = new Vector2(
                        (Mathf.Abs(local.x) - half.x) * Mathf.Abs(scale.x),
                        (Mathf.Abs(local.z) - half.z) * Mathf.Abs(scale.z));
                    return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f);
                }
            }
        }

        /// <summary>
        /// For a point already known to be inside the zone: its distance to the zone's sides seen from above, as
        /// <see cref="SignedDistanceFromAbove"/> but cheaper, since a single spline outline needs no inside test.
        /// </summary>
        public float InsideDistanceFromAbove(Vector3 world)
        {
#if UNITYTOOLS_SPLINES
            if (_shape == ZoneShape.Spline)
            {
                List<Vector2[]> outlines = Outlines();
                if (outlines.Count == 1) return EdgeFalloff.DistanceToOutline(outlines[0], new Vector2(world.x, world.z));
            }
#endif
            return Mathf.Max(0f, -SignedDistanceFromAbove(world));
        }

        /// <summary>Whether <paramref name="world"/> is inside the zone.</summary>
        public bool Contains(Vector3 world)
        {
#if UNITYTOOLS_SPLINES
            // Inside-or-not needs no distance: a point-in-polygon test on the cached outline is much cheaper.
            if (_shape == ZoneShape.Spline)
            {
                var flat = new Vector2(world.x, world.z);
                foreach (Vector2[] outline in Outlines())
                {
                    if (SplineSamplerNode.Contains(outline, flat)) return true;
                }
                return false;
            }
#endif
            return SignedDistance(world) <= 0f;
        }

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

        // Set between BeginQueries and EndQueries: the cached shape is used without checking it.
        private bool _trustOutline;

        /// <summary>
        /// Starts a batch of many Contains and SignedDistance queries (as a node makes): the zone's shape is checked
        /// once here instead of on every query. End it with <see cref="EndQueries"/>.
        /// </summary>
        public void BeginQueries()
        {
            _trustOutline = false;
#if UNITYTOOLS_SPLINES
            if (_shape == ZoneShape.Spline) Outlines();
#endif
            _trustOutline = true;
        }

        /// <summary>Ends a batch started with <see cref="BeginQueries"/>.</summary>
        public void EndQueries() => _trustOutline = false;

#if UNITYTOOLS_SPLINES
        // The spline's outline seen from above, rebuilt only when the zone changes: evaluating the spline is far too slow
        // to do for every point a node tests.
        private readonly List<Vector2[]> _outlines = new List<Vector2[]>();
        private int _outlineHash;
        private bool _outlineValid;

        private const int OutlineSegments = 64;

        private List<Vector2[]> Outlines()
        {
            // Inside a node's batch of queries the zone can't change, so the outline is reused without checking.
            if (_outlineValid && _trustOutline) return _outlines;
            int hash = StateHash;
            if (_outlineValid && hash == _outlineHash) return _outlines;
            _outlines.Clear();
            var container = GetComponent<UnityEngine.Splines.SplineContainer>();
            if (container != null)
            {
                for (int s = 0; s < container.Splines.Count; s++)
                {
                    if (!container.Splines[s].Closed) continue;
                    var outline = new Vector2[OutlineSegments];
                    for (int i = 0; i < OutlineSegments; i++)
                    {
                        Vector3 p = container.EvaluatePosition(s, i / (float)OutlineSegments);
                        outline[i] = new Vector2(p.x, p.z);
                    }
                    _outlines.Add(outline);
                }
            }
            _outlineHash = hash;
            _outlineValid = true;
            return _outlines;
        }

        private float SplineDistance(Vector3 world)
        {
            List<Vector2[]> outlines = Outlines();
            if (outlines.Count == 0) return float.PositiveInfinity;
            float best = float.PositiveInfinity;
            var flat = new Vector2(world.x, world.z);
            foreach (Vector2[] outline in outlines)
            {
                float edge = EdgeFalloff.DistanceToOutline(outline, flat);
                float signed = SplineSamplerNode.Contains(outline, flat) ? -edge : edge;
                best = Mathf.Min(best, signed);
            }
            return best;
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
