using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// Source: reads authored objects from the scene the Placement Graph is in: Placement Zones by label, or GameObjects
    /// by tag. Outputs one point per object (its position, rotation and scale) or, for zones, a grid of points inside
    /// each zone. Points carry the attributes Object (the GameObject) and, for zones, Zone, which Difference uses to
    /// carve the zone's shape out of other points. Objects are ordered by their place in the hierarchy, so results
    /// don't depend on the order Unity finds them in.
    /// </summary>
    [Serializable]
    public class SceneInputNode : PlacementNode
    {
        public enum SourceMode
        {
            /// <summary>Placement Zone components whose Label matches.</summary>
            ZonesByLabel,
            /// <summary>GameObjects with the Unity tag.</summary>
            TaggedObjects
        }

        public enum OutputMode
        {
            /// <summary>One point per object, at its transform.</summary>
            OnePointPerObject,
            /// <summary>A grid of points (Spacing) inside each zone. Zones only.</summary>
            PointsInsideZones
        }

        /// <summary>The attribute holding the zone a point came from (Object).</summary>
        public const string ZoneAttribute = "Zone";
        /// <summary>The attribute holding the GameObject a point came from (Object).</summary>
        public const string ObjectAttribute = "Object";

        public SourceMode Source = SourceMode.ZonesByLabel;
        [Tooltip("Zones by Label: the label to match.")]
        public string Label = "";
        [Tooltip("Tagged Objects: the Unity tag to match.")]
        public string Tag = "";
        public OutputMode Output = OutputMode.OnePointPerObject;
        [Tooltip("Points Inside Zones: grid spacing in metres.")]
        [Min(0.01f)] public float Spacing = 1f;
        [Tooltip("Only objects whose bounds overlap the generation volume.")]
        public bool OnlyOverlapping = true;
        [Tooltip("Points Inside Zones: metres inward from the zone's sides (seen from above) over which the Edge Falloff attribute rises to 1. 0 turns it off.")]
        [Min(0f)] public float EdgeFalloffDistance = 0f;
        [Tooltip("Points Inside Zones: how the Edge Falloff attribute rises from the edge.")]
        public FalloffCurve EdgeFalloffCurve = FalloffCurve.Linear;
        [Tooltip("Points Inside Zones: the Edge Falloff value at the zone's edge. Above 0 keeps a minimum, e.g. for scale.")]
        [Range(0f, 1f)] public float EdgeFalloffMin = 0f;
        [Tooltip("Points Inside Zones: the Float attribute the edge falloff is written to. Other points get 1.")]
        public string EdgeFalloffAttribute = EdgeFalloff.DefaultAttribute;

        public override int InputCount => 0;
        public override int OutputCount => 1;

        /// <summary>A matched object, with the key that orders matches stably.</summary>
        public readonly struct Match
        {
            public readonly GameObject GameObject;
            public readonly PlacementZone Zone;
            public readonly string Key;

            public Match(GameObject gameObject, PlacementZone zone, string key)
            {
                GameObject = gameObject;
                Zone = zone;
                Key = key;
            }
        }

        /// <summary>
        /// The objects this node matches for <paramref name="context"/>: in the context's scene, optionally overlapping
        /// its volume, ordered by hierarchy path.
        /// </summary>
        public List<Match> FindMatches(PlacementContext context)
        {
            var matches = new List<Match>();
            if (Source == SourceMode.ZonesByLabel)
            {
                foreach (PlacementZone zone in PlacementZone.All)
                {
                    if (zone == null || (zone.Label ?? "") != (Label ?? "")) continue;
                    if (!InScene(context, zone.gameObject)) continue;
                    if (PlacementGraph.IsGenerated(zone.transform)) continue;
                    if (OnlyOverlapping && !Overlaps(context, zone.WorldBounds)) continue;
                    matches.Add(new Match(zone.gameObject, zone, KeyOf(zone.transform)));
                }
            }
            else
            {
                if (string.IsNullOrEmpty(Tag) || Tag == "Untagged")
                {
                    context.Warn(this, "Tagged Objects: set a Tag to match (Untagged objects can't be searched for).");
                    return matches;
                }
                GameObject[] tagged;
                try
                {
                        tagged = GameObject.FindGameObjectsWithTag(Tag);
                    }
                    catch (UnityException)
                    {
                        context.Warn(this, $"There's no tag called '{Tag}'.");
                        return matches;
                    }
                    foreach (GameObject gameObject in tagged)
                    {
                        if (!InScene(context, gameObject)) continue;
                        // A graph's own output (or another graph's) is never input: reading it back would feed on itself.
                        if (PlacementGraph.IsGenerated(gameObject.transform)) continue;
                        if (OnlyOverlapping && !Overlaps(context, new Bounds(gameObject.transform.position, Vector3.zero))) continue;
                        matches.Add(new Match(gameObject, gameObject.GetComponent<PlacementZone>(), KeyOf(gameObject.transform)));
                    }
                }
                matches.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                return matches;
            }

            private static bool InScene(PlacementContext context, GameObject gameObject) =>
                !context.Scene.IsValid() || gameObject.scene == context.Scene;

            /// <summary>Whether world <paramref name="bounds"/> overlap the context's volume (compared in its local space).</summary>
            private static bool Overlaps(PlacementContext context, Bounds bounds)
            {
                Matrix4x4 worldToLocal = context.LocalToWorld.inverse;
                Vector3 min = bounds.min, max = bounds.max;
                Bounds local = new Bounds(worldToLocal.MultiplyPoint3x4(min), Vector3.zero);
                for (int corner = 1; corner < 8; corner++)
                {
                    var point = new Vector3((corner & 1) == 0 ? min.x : max.x, (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z);
                    local.Encapsulate(worldToLocal.MultiplyPoint3x4(point));
                }
                Bounds volume = context.LocalBounds;
                return local.min.x <= volume.max.x && local.max.x >= volume.min.x &&
                       local.min.y <= volume.max.y && local.max.y >= volume.min.y &&
                       local.min.z <= volume.max.z && local.max.z >= volume.min.z;
            }

            /// <summary>The object's hierarchy path with sibling indices, e.g. "0:Rocks/2:Cluster": stable between runs.</summary>
            public static string KeyOf(Transform transform)
            {
                var builder = new StringBuilder();
                var chain = new List<Transform>();
                for (Transform t = transform; t != null; t = t.parent) chain.Add(t);
                for (int i = chain.Count - 1; i >= 0; i--)
                {
                    if (builder.Length > 0) builder.Append('/');
                    builder.Append(chain[i].GetSiblingIndex().ToString("D5")).Append(':').Append(chain[i].name);
                }
                return builder.ToString();
            }

            /// <summary>FNV-1a of a string, stable across runs.</summary>
            internal static uint StableHash(string text)
            {
                unchecked
                {
                    uint hash = 2166136261;
                    foreach (char c in text)
                    {
                        hash ^= c;
                        hash *= 16777619;
                    }
                    return hash;
                }
            }

            public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
            {
                var result = new PointData();
                outputs[0] = result;
                List<Match> matches = FindMatches(context);
                PointAttribute<UnityEngine.Object> objects = result.GetOrAddAttribute<UnityEngine.Object>(ObjectAttribute);
                PointAttribute<UnityEngine.Object> zones = result.GetOrAddAttribute<UnityEngine.Object>(ZoneAttribute);
                uint nodeSeed = PlacementRandom.Hash(context.SeedHash, NodeSeed);
                float spacing = Mathf.Max(0.01f, Spacing);
                bool falloff = Output == OutputMode.PointsInsideZones && EdgeFalloffDistance > 0f && !string.IsNullOrEmpty(EdgeFalloffAttribute);
                PointAttribute<float> edge = falloff ? result.GetOrAddAttribute<float>(EdgeFalloffAttribute) : null;

                foreach (Match match in matches)
                {
                    uint objectSeed = PlacementRandom.Hash(nodeSeed, StableHash(match.Key));
                    if (Output == OutputMode.OnePointPerObject || match.Zone == null)
                    {
                        Transform t = match.GameObject.transform;
                        PlacementPoint point = PlacementPoint.At(t.position, objectSeed);
                        point.Rotation = t.rotation;
                        point.Scale = t.lossyScale;
                        point.Normal = t.up;
                        edge?.Set(result.Count, 1f);
                        Add(result, objects, zones, point, match);
                        continue;
                    }

                    // Points inside the zone, on a world-anchored grid so they don't move when the zone does.
                    match.Zone.BeginQueries();
                    try
                    {
                    Bounds bounds = match.Zone.WorldBounds;
                    int x0 = Mathf.FloorToInt(bounds.min.x / spacing), x1 = Mathf.CeilToInt(bounds.max.x / spacing);
                    int z0 = Mathf.FloorToInt(bounds.min.z / spacing), z1 = Mathf.CeilToInt(bounds.max.z / spacing);
                    if ((long)(x1 - x0) * (z1 - z0) > GridSampling.MaxPoints)
                    {
                        context.Warn(this, $"Spacing {spacing} would make too many points in {match.GameObject.name}. Increase Spacing.");
                        continue;
                    }
                    for (int z = z0; z < z1; z++)
                    for (int x = x0; x < x1; x++)
                    {
                        var position = new Vector3((x + 0.5f) * spacing, bounds.center.y, (z + 0.5f) * spacing);
                        if (!match.Zone.Contains(position)) continue;
                        PlacementPoint point = PlacementPoint.At(position, PlacementRandom.Hash(objectSeed, unchecked((uint)x), unchecked((uint)z)));
                        if (falloff)
                        {
                            float inside = match.Zone.InsideDistanceFromAbove(position);
                            edge.Set(result.Count, EdgeFalloff.Evaluate(inside, EdgeFalloffDistance, EdgeFalloffCurve, EdgeFalloffMin));
                        }
                        Add(result, objects, zones, point, match);
                    }
                }
                finally
                {
                    match.Zone.EndQueries();
                }
            }
        }

        private static void Add(PointData result, PointAttribute<UnityEngine.Object> objects, PointAttribute<UnityEngine.Object> zones, PlacementPoint point, Match match)
        {
            int index = result.Count;
            result.Points.Add(point);
            objects.Set(index, match.GameObject);
            if (match.Zone != null) zones.Set(index, match.Zone);
        }
    }
}
