using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityTools.Placement.Tests
{
    /// <summary>Scene Input and Placement Zones: reading authored objects and carving them out with Difference.</summary>
    [SceneIsolation]
    public class PlacementSceneInputTests
    {
        // Far from anything in the open scene, so only the test's objects overlap the volumes.
        private static readonly Vector3 Origin = new Vector3(20000f, 0f, 20000f);
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
        }

        private T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }

        private PlacementZone Zone(string label, PlacementZone.ZoneShape shape, Vector3 offset, float size = 4f, string name = "Zone")
        {
            var zone = Track(new GameObject(name)).AddComponent<PlacementZone>();
            zone.transform.position = Origin + offset;
            zone.Label = label;
            zone.Shape = shape;
            zone.Size = Vector3.one * size;
            zone.Radius = size * 0.5f;
            return zone;
        }

        private static PlacementContext Context(Vector3 size) => new PlacementContext
        {
            LocalToWorld = Matrix4x4.TRS(Origin, Quaternion.identity, Vector3.one),
            LocalBounds = new Bounds(Vector3.zero, size),
            Scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene()
        };

        private static PointData[] Run(PlacementNode node, PlacementContext context, params PointData[] inputs) =>
            PlacementAttributeTests.Run(node, context, inputs);

        [Test]
        public void ZonesByLabel_MatchOnlyThatLabel_InHierarchyOrder()
        {
            PlacementZone b = Zone("rocks", PlacementZone.ZoneShape.Box, new Vector3(3f, 0f, 0f), name: "B");
            PlacementZone a = Zone("rocks", PlacementZone.ZoneShape.Box, new Vector3(-3f, 0f, 0f), name: "A");
            Zone("water", PlacementZone.ZoneShape.Box, Vector3.zero, name: "W");
            Zone("rocks", PlacementZone.ZoneShape.Box, new Vector3(500f, 0f, 0f), name: "Far");

            var node = new SceneInputNode { Source = SceneInputNode.SourceMode.ZonesByLabel, Label = "rocks" };
            PointData points = Run(node, Context(new Vector3(20f, 10f, 20f)))[0];
            Assert.AreEqual(2, points.Count, "Only rocks zones overlapping the volume.");
            PointAttribute<Object> zones = points.GetAttribute<Object>(SceneInputNode.ZoneAttribute);
            // Ordered by hierarchy path, not by creation order: B was created first but comes first only if it's earlier in the hierarchy.
            List<Object> expected = new[] { a, b }.OrderBy(z => SceneInputNode.KeyOf(z.transform), System.StringComparer.Ordinal).Cast<Object>().ToList();
            CollectionAssert.AreEqual(expected, new[] { zones.Get(0), zones.Get(1) });
            Assert.AreEqual(b.transform.position, points.Points[expected.IndexOf(b)].Position);

            // Same objects, same seeds, whatever order they were found in.
            PointData again = Run(new SceneInputNode { Label = "rocks" }, Context(new Vector3(20f, 10f, 20f)))[0];
            CollectionAssert.AreEqual(points.Points.Select(p => p.Seed), again.Points.Select(p => p.Seed));
        }

        [Test]
        public void TaggedObjects_AreMatchedByTag()
        {
            GameObject tagged = Track(new GameObject("Spawn Point"));
            tagged.tag = "Respawn";
            tagged.transform.position = Origin + new Vector3(1f, 0f, 2f);
            Track(new GameObject("Untagged")).transform.position = Origin;

            var node = new SceneInputNode { Source = SceneInputNode.SourceMode.TaggedObjects, Tag = "Respawn" };
            PointData points = Run(node, Context(new Vector3(20f, 10f, 20f)))[0];
            Assert.AreEqual(1, points.Count);
            Assert.AreEqual(tagged.transform.position, points.Points[0].Position);
            Assert.AreEqual(tagged, points.GetAttribute<Object>(SceneInputNode.ObjectAttribute).Get(0));

            var missing = Context(new Vector3(20f, 10f, 20f));
            Run(new SceneInputNode { Source = SceneInputNode.SourceMode.TaggedObjects, Tag = "NoSuchTag12345" }, missing);
            Assert.AreEqual(1, missing.Warnings.Count);
        }

        [Test]
        public void GeneratedObjects_AreNeverMatched_SoAGraphDoesNotReadItsOwnOutput()
        {
            GameObject prop = Track(new GameObject("Tagged Prop"));
            prop.tag = "Respawn";
            var nodes = new List<PlacementNode>
            {
                new VolumeSamplerNode { Spacing = 2f, Jitter = 0f },
                new SceneInputNode { Source = SceneInputNode.SourceMode.TaggedObjects, Tag = "Respawn" },
                new DifferenceNode { Radius = 0.5f },
                new SpawnPrefabsNode { Entries = new List<PrefabEntry> { new PrefabEntry { Prefab = prop } } }
            };
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].Name = i.ToString();
                nodes[i].NodeSeed = (uint)(i + 7) * 13u;
            }
            var graph = Track(ScriptableObject.CreateInstance<PlacementGraphAsset>());
            Assert.IsTrue(PlacementGraphAsset.TrySortTopologically(nodes,
                new List<PlacementEdge> { new(0, 0, 2, 0), new(1, 0, 2, 1), new(2, 0, 3, 0) }, out var sorted, out var edges));
            graph.SetContents(sorted, edges);
            var component = Track(new GameObject("Placement Graph")).AddComponent<PlacementGraph>();
            component.transform.position = Origin + new Vector3(0f, 0f, 100f);
            component.Size = new Vector3(10f, 4f, 10f);
            component.Graph = graph;

            component.RegenerateChanged();
            int first = component.InstanceCount;
            Assert.AreEqual(25, first);
            // The spawned copies carry the tag too; reading them back would carve the next run's points away.
            component.RegenerateChanged();
            Assert.AreEqual(first, component.InstanceCount, "Regenerating gives the same result.");
            Assert.IsTrue(PlacementGraph.IsGenerated(component.transform.GetChild(0)));
            Assert.IsFalse(PlacementGraph.IsGenerated(prop.transform));
        }

        [Test]
        public void EmptyTag_MatchesNothing_AndWarns()
        {
            var context = Context(new Vector3(20f, 10f, 20f));
            PointData points = Run(new SceneInputNode { Source = SceneInputNode.SourceMode.TaggedObjects }, context)[0];
            Assert.AreEqual(0, points.Count);
            StringAssert.Contains("set a Tag", context.Warnings.Single());
        }

#if UNITYTOOLS_SPLINES
        [Test]
        public void SplineZone_PointsInside_AreFast_AndFollowTheZoneWhenItMoves()
        {
            var zone = Zone("field", PlacementZone.ZoneShape.Box, Vector3.zero);
            zone.Shape = PlacementZone.ZoneShape.Spline;
            var container = zone.gameObject.AddComponent<UnityEngine.Splines.SplineContainer>();
            UnityEngine.Splines.Spline spline = container.Spline;
            foreach (Vector3 knot in new[] { new Vector3(-8f, 0f, -6f), new Vector3(8f, 0f, -6f), new Vector3(8f, 0f, 6f), new Vector3(-8f, 0f, 6f) })
                spline.Add(new UnityEngine.Splines.BezierKnot(new Unity.Mathematics.float3(knot.x, knot.y, knot.z)));
            spline.Closed = true;

            var node = new SceneInputNode { Label = "field", Output = SceneInputNode.OutputMode.PointsInsideZones, Spacing = 0.1f };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            PointData points = Run(node, Context(new Vector3(40f, 10f, 40f)))[0];
            watch.Stop();
            // About 160 x 120 points; a few on the edge go either way (the test runs far from the world origin).
            Assert.That(points.Count, Is.InRange(160 * 120 - 200, 160 * 120), "A 16 x 12 m field at 0.1 m.");
            // Each point used to evaluate the spline 64 times (about 3 s for this zone).
            Assert.Less(watch.Elapsed.TotalMilliseconds, 500.0, "Inside tests use the cached outline.");

            // Moving the zone (in the same frame) moves the points: the outline is checked at the start of each run.
            zone.transform.position += new Vector3(4f, 0f, 0f);
            PointData moved = Run(node, Context(new Vector3(40f, 10f, 40f)))[0];
            Assert.That(moved.Count, Is.InRange(points.Count - 200, points.Count + 200));
            Assert.AreEqual(points.Points.Min(p => p.Position.x) + 4f, moved.Points.Min(p => p.Position.x), 1e-3f);
            Assert.IsTrue(zone.Contains(zone.transform.position));
            Assert.Less(zone.SignedDistance(zone.transform.position), 0f);
        }
#endif

        [Test]
        public void Difference_CarvesBoxAndSphereZones()
        {
            Zone("carve", PlacementZone.ZoneShape.Box, new Vector3(-5f, 0f, 0f), size: 4f);
            Zone("carve", PlacementZone.ZoneShape.Sphere, new Vector3(5f, 0f, 0f), size: 4f);
            PlacementContext context = Context(new Vector3(20f, 10f, 20f));
            PointData zones = Run(new SceneInputNode { Label = "carve" }, context)[0];

            var grid = new PointData();
            for (int x = -9; x <= 9; x++)
            for (int z = -9; z <= 9; z++)
                grid.Points.Add(PlacementPoint.At(Origin + new Vector3(x, 0f, z), (uint)((x + 10) * 100 + z + 10)));

            PointData[] split = Run(new DifferenceNode { Radius = 0f }, context, grid, zones);
            foreach (PlacementPoint point in split[1].Points)
            {
                Vector3 local = point.Position - Origin;
                bool inBox = Mathf.Abs(local.x + 5f) <= 2f && Mathf.Abs(local.z) <= 2f;
                bool inSphere = Vector3.Distance(local, new Vector3(5f, 0f, 0f)) <= 2f;
                Assert.IsTrue(inBox || inSphere, $"{local} was removed but isn't in a zone.");
            }
            Assert.AreEqual(25 + 13, split[1].Count, "5 x 5 grid points in the box and 13 in the sphere.");

            // With Falloff, kept points near a zone are thinned out, points far away aren't.
            PointData[] soft = Run(new DifferenceNode { Radius = 0f, Falloff = 3f }, context, grid, zones);
            Assert.AreEqual(split[0].Count, soft[0].Count);
            PlacementPoint near = soft[0].Points.First(p => Vector3.Distance(p.Position, Origin + new Vector3(-2f, 0f, 3f)) < 0.01f);
            PlacementPoint far = soft[0].Points.First(p => Vector3.Distance(p.Position, Origin + new Vector3(0f, 0f, 9f)) < 0.01f);
            Assert.Less(near.Density, 1f);
            Assert.AreEqual(1f, far.Density);
        }

        [Test]
        public void MovingAZone_RegeneratesOnlyTheCellsItTouches()
        {
            var prop = Track(new GameObject("Prop"));
            var nodes = new List<PlacementNode>
            {
                new VolumeSamplerNode { Spacing = 1f, Jitter = 0f },
                new SceneInputNode { Label = "clearing" },
                new DifferenceNode { Radius = 0f },
                new SpawnPrefabsNode { Entries = new List<PrefabEntry> { new PrefabEntry { Prefab = prop } } }
            };
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].Name = i.ToString();
                nodes[i].NodeSeed = (uint)(i + 3) * 31u;
            }
            var graph = Track(ScriptableObject.CreateInstance<PlacementGraphAsset>());
            Assert.IsTrue(PlacementGraphAsset.TrySortTopologically(nodes,
                new List<PlacementEdge> { new(0, 0, 2, 0), new(1, 0, 2, 1), new(2, 0, 3, 0) }, out var sorted, out var edges));
            graph.SetContents(sorted, edges);

            PlacementZone zone = Zone("clearing", PlacementZone.ZoneShape.Box, new Vector3(-7.5f, 0f, -7.5f), size: 2f);
            zone.Size = new Vector3(2.5f, 10f, 2.5f); // Tall enough to reach the sampled points at the top of the volume.
            var component = Track(new GameObject("Placement Graph")).AddComponent<PlacementGraph>();
            component.transform.position = Origin;
            component.Size = new Vector3(20f, 4f, 20f);
            component.CellSize = 5f;
            component.CellOverlap = 0f;
            component.Graph = graph;
            component.Generate();
            Assert.AreEqual(400 - 9, component.InstanceCount, "The 2.5 x 2.5 m zone removes 3 x 3 grid points.");

            zone.transform.position = Origin + new Vector3(-7f, 0f, -7.5f);
            component.RegenerateChanged();
            CollectionAssert.AreEquivalent(new[] { new Vector2Int(-2, -2) }, component.LastRegeneratedCells);

            // Moving it into another cell reruns both the cell it left and the one it entered.
            zone.transform.position = Origin + new Vector3(7.5f, 0f, 7.5f);
            component.RegenerateChanged();
            CollectionAssert.AreEquivalent(new[] { new Vector2Int(-2, -2), new Vector2Int(1, 1) }, component.LastRegeneratedCells);
            Assert.AreEqual(400 - 9, component.InstanceCount);
        }
    }
}
