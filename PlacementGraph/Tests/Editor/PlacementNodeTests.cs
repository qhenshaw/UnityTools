using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
#if UNITYTOOLS_SPLINES
using Unity.Mathematics;
using UnityEngine.Splines;
#endif

namespace UnityTools.Placement.Tests
{
    /// <summary>Tests for the second set of nodes (filters, set operations, marker, terrain and spline sources).</summary>
    public class PlacementNodeTests
    {
        private readonly List<Object> _created = new();
        private readonly List<string> _assets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
            foreach (string path in _assets) AssetDatabase.DeleteAsset(path);
            _assets.Clear();
        }

        private T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }

        private static PlacementContext Context(int seed = 1) => new PlacementContext
        {
            LocalBounds = new Bounds(Vector3.zero, new Vector3(10f, 4f, 10f)),
            Seed = seed
        };

        private static PointData Points(params Vector3[] positions)
        {
            var data = new PointData();
            for (int i = 0; i < positions.Length; i++) data.Points.Add(PlacementPoint.At(positions[i], (uint)(i + 1)));
            return data;
        }

        private static PointData[] Run(PlacementNode node, PlacementContext context, params PointData[] inputs)
        {
            node.Name = node.GetType().Name;
            node.NodeSeed = 12345u;
            context.InputConnected = inputs.Select(_ => true).ToArray();
            var outputs = new PointData[node.OutputCount];
            node.Execute(context, inputs, outputs);
            return outputs;
        }

        [Test]
        public void HeightFilter_KeepsPointsInRange()
        {
            PointData[] outputs = Run(new HeightFilterNode { Min = 0f, Max = 2f }, Context(),
                Points(new Vector3(0, -1, 0), new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 2, 0), new Vector3(0, 3, 0)));
            CollectionAssert.AreEqual(new[] { 0f, 1f, 2f }, outputs[0].Points.Select(p => p.Position.y));
            CollectionAssert.AreEqual(new[] { -1f, 3f }, outputs[1].Points.Select(p => p.Position.y));
        }

        [Test]
        public void SelfPruning_KeepsDensestFirst()
        {
            PointData input = Points(new Vector3(0, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(3, 0, 0));
            PlacementPoint dense = input.Points[1];
            dense.Density = 1f;
            input.Points[1] = dense;
            PlacementPoint sparse = input.Points[0];
            sparse.Density = 0.2f;
            input.Points[0] = sparse;

            PointData[] outputs = Run(new SelfPruningNode { Radius = 1f }, Context(), input);
            CollectionAssert.AreEqual(new[] { 0.5f, 3f }, outputs[0].Points.Select(p => p.Position.x));
            Assert.AreEqual(1, outputs[1].Count);
        }

        [Test]
        public void SelfPruning_MatchesBruteForceAndIsDeterministic()
        {
            var input = new PointData();
            for (uint i = 0; i < 400; i++)
            {
                var position = new Vector3(
                    PlacementRandom.Range(i, 1u, 1u, -5f, 5f), 0f, PlacementRandom.Range(i, 1u, 2u, -5f, 5f));
                PlacementPoint point = PlacementPoint.At(position, i);
                point.Density = PlacementRandom.Range(i, 1u, 3u, 0f, 1f);
                input.Points.Add(point);
            }

            const float radius = 0.7f;
            PointData[] outputs = Run(new SelfPruningNode { Radius = radius }, Context(), input);
            PointData[] again = Run(new SelfPruningNode { Radius = radius }, Context(), input);
            CollectionAssert.AreEqual(outputs[0].Points.Select(p => p.Seed), again[0].Points.Select(p => p.Seed));
            Assert.AreEqual(input.Count, outputs[0].Count + outputs[1].Count);

            // The same greedy pass without a grid.
            var expected = new List<PlacementPoint>();
            foreach (PlacementPoint point in input.Points.OrderByDescending(p => p.Density).ThenBy(p => p.Seed))
            {
                if (expected.All(k => (k.Position - point.Position).sqrMagnitude >= radius * radius)) expected.Add(point);
            }
            CollectionAssert.AreEquivalent(expected.Select(p => p.Seed), outputs[0].Points.Select(p => p.Seed));
        }

        [Test]
        public void Difference_DropsPointsNearExclude()
        {
            PointData input = Points(new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(4, 0, 0));
            PointData exclude = Points(new Vector3(2.5f, 0, 0));
            PointData[] outputs = Run(new DifferenceNode { Radius = 1f }, Context(), input, exclude);
            CollectionAssert.AreEqual(new[] { 0f, 4f }, outputs[0].Points.Select(p => p.Position.x));
            CollectionAssert.AreEqual(new[] { 2f }, outputs[1].Points.Select(p => p.Position.x));

            PointData[] nothingToExclude = Run(new DifferenceNode { Radius = 1f }, Context(), input, PointData.Empty);
            Assert.AreEqual(3, nothingToExclude[0].Count);
        }

        [Test]
        public void Merge_ConcatenatesAThenB()
        {
            PointData[] outputs = Run(new MergeNode(), Context(), Points(Vector3.zero, Vector3.one), Points(Vector3.right * 5f));
            CollectionAssert.AreEqual(new[] { 0f, 1f, 5f }, outputs[0].Points.Select(p => p.Position.x));
        }

        [Test]
        public void CopyPoints_TransformsPatternBySource()
        {
            PlacementPoint source = PlacementPoint.At(new Vector3(10, 0, 0), 7u);
            source.Rotation = Quaternion.Euler(0f, 90f, 0f);
            source.Scale = Vector3.one * 2f;
            source.Density = 0.5f;
            PointData pattern = Points(new Vector3(4, 0, 0), new Vector3(6, 0, 0));
            PlacementPoint half = pattern.Points[1];
            half.Density = 0.5f;
            pattern.Points[1] = half;

            PointData[] outputs = Run(new CopyPointsNode(), Context(), new PointData(new List<PlacementPoint> { source }), pattern);
            Assert.AreEqual(2, outputs[0].Count);
            // Pattern offsets are (-1, 0, 0) and (1, 0, 0) from the centre; scaled by 2 and turned 90 degrees about Y.
            Assert.That(Vector3.Distance(new Vector3(10, 0, 2), outputs[0].Points[0].Position), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(new Vector3(10, 0, -2), outputs[0].Points[1].Position), Is.LessThan(1e-4f));
            Assert.AreEqual(Vector3.one * 2f, outputs[0].Points[0].Scale);
            Assert.AreEqual(0.5f, outputs[0].Points[0].Density, 1e-6f);
            Assert.AreEqual(0.25f, outputs[0].Points[1].Density, 1e-6f);
            Assert.AreEqual(PlacementRandom.Hash(7u, 1u), outputs[0].Points[0].Seed);
            Assert.AreNotEqual(outputs[0].Points[0].Seed, outputs[0].Points[1].Seed);
        }

        [Test]
        public void DistanceDensity_FallsOffBetweenRadii()
        {
            PointData input = Points(new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(3, 0, 0), new Vector3(5, 0, 0), new Vector3(8, 0, 0));
            PointData target = Points(Vector3.zero);
            PointData[] outputs = Run(new DistanceDensityNode { InnerRadius = 1f, OuterRadius = 5f }, Context(), input, target);
            CollectionAssert.AreEqual(new[] { 1f, 1f, 0.5f, 0f, 0f }, outputs[0].Points.Select(p => p.Density));

            PointData[] inverted = Run(new DistanceDensityNode { InnerRadius = 1f, OuterRadius = 5f, Invert = true }, Context(), input, target);
            CollectionAssert.AreEqual(new[] { 0f, 0f, 0.5f, 1f, 1f }, inverted[0].Points.Select(p => p.Density));

            PointData[] noTargets = Run(new DistanceDensityNode(), Context(), input, PointData.Empty);
            Assert.IsTrue(noTargets[0].Points.All(p => p.Density == 0f));
        }

        [Test]
        public void ChildMarkers_OnePointPerMarker()
        {
            var a = Track(new GameObject("A")).transform;
            var b = Track(new GameObject("B")).transform;
            a.SetPositionAndRotation(new Vector3(1, 2, 3), Quaternion.Euler(0f, 45f, 0f));
            b.position = new Vector3(-1, 0, 0);
            b.localScale = Vector3.one * 3f;
            PlacementContext context = Context();
            context.Markers.Add(a);
            context.Markers.Add(b);

            PointData[] outputs = Run(new ChildMarkersNode(), context);
            Assert.AreEqual(2, outputs[0].Count);
            Assert.AreEqual(a.position, outputs[0].Points[0].Position);
            Assert.AreEqual(a.rotation, outputs[0].Points[0].Rotation);
            Assert.AreEqual(Vector3.one * 3f, outputs[0].Points[1].Scale);
            Assert.AreNotEqual(outputs[0].Points[0].Seed, outputs[0].Points[1].Seed);

            PlacementContext empty = Context();
            Assert.AreEqual(0, Run(new ChildMarkersNode(), empty)[0].Count);
            Assert.AreEqual(1, empty.Warnings.Count);
        }

        [Test]
        public void Component_KeepsMarkersChild_AndSpawnsAtMarkers()
        {
            var prefab = Track(new GameObject("Prop"));
            var palette = Track(ScriptableObject.CreateInstance<PrefabPalette>());
            palette.Entries.Add(new PrefabPalette.Entry { Prefab = prefab, Weight = 1f });
            var nodes = new List<PlacementNode> { new ChildMarkersNode(), new SpawnPrefabsNode { Palette = palette } };
            for (int i = 0; i < nodes.Count; i++) nodes[i].Name = i.ToString();
            var graph = Track(ScriptableObject.CreateInstance<PlacementGraphAsset>());
            graph.SetContents(nodes, new List<PlacementEdge> { new(0, 0, 1, 0) });

            var go = Track(new GameObject("Placement Graph"));
            var markers = new GameObject("Markers").transform;
            markers.SetParent(go.transform);
            for (int i = 0; i < 3; i++) new GameObject("Marker " + i).transform.SetParent(markers);
            markers.GetChild(2).gameObject.SetActive(false);

            var component = go.AddComponent<PlacementGraph>();
            component.Markers = markers;
            component.Graph = graph;
            component.Generate();
            component.Generate();

            Assert.AreEqual(2, component.InstanceCount, "Inactive markers are skipped.");
            Assert.AreEqual(3, go.transform.childCount, "The Markers child plus two spawned objects.");
            Assert.AreEqual(markers, go.transform.Cast<Transform>().First(t => t.name == "Markers"));
            component.Clear();
            Assert.AreEqual(1, go.transform.childCount);
        }

        private Terrain CreateTerrain()
        {
            var data = new TerrainData { heightmapResolution = 33, alphamapResolution = 16 };
            data.size = new Vector3(20f, 10f, 20f);
            data.terrainLayers = new[] { new TerrainLayer(), new TerrainLayer() };
            // Layer 1 on the half with x < 0, layer 0 on the other half.
            var maps = new float[16, 16, 2];
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                maps[y, x, 1] = x < 8 ? 1f : 0f;
                maps[y, x, 0] = 1f - maps[y, x, 1];
            }
            data.SetAlphamaps(0, 0, maps);
            _created.Add(data);
            foreach (TerrainLayer layer in data.terrainLayers) _created.Add(layer);
            GameObject go = Track(Terrain.CreateTerrainGameObject(data));
            go.transform.position = new Vector3(-10f, -1f, -10f);
            return go.GetComponent<Terrain>();
        }

        [Test]
        public void TerrainSampler_SamplesHeightAndFiltersByLayer()
        {
            CreateTerrain();
            PointData[] all = Run(new TerrainSamplerNode { Spacing = 1f, Jitter = 0f }, Context());
            Assert.AreEqual(100, all[0].Count);
            foreach (PlacementPoint point in all[0].Points)
            {
                Assert.AreEqual(-1f, point.Position.y, 1e-3f);
                Assert.That(Vector3.Angle(Vector3.up, point.Normal), Is.LessThan(0.01f));
            }

            PointData[] layer1 = Run(new TerrainSamplerNode { Spacing = 1f, Jitter = 0f, LayerIndex = 1, MinWeight = 0.5f }, Context());
            Assert.AreEqual(50, layer1[0].Count);
            Assert.IsTrue(layer1[0].Points.All(p => p.Position.x < 0f));

            PointData[] missingLayer = Run(new TerrainSamplerNode { Spacing = 1f, Jitter = 0f, LayerIndex = 5 }, Context());
            Assert.AreEqual(0, missingLayer[0].Count);
        }

#if UNITYTOOLS_SPLINES
        private SplineContainer CreateSpline(bool closed, params Vector3[] knots)
        {
            var container = Track(new GameObject("Spline")).AddComponent<SplineContainer>();
            Spline spline = container.Spline;
            foreach (Vector3 knot in knots) spline.Add(new BezierKnot(new float3(knot.x, knot.y, knot.z)));
            spline.Closed = closed;
            return container;
        }

        [Test]
        public void SplineSampler_Along_PlacesPointsBySpacing()
        {
            PlacementContext context = Context();
            context.Spline = CreateSpline(false, new Vector3(-5, 0, 0), new Vector3(5, 0, 0));
            PointData[] outputs = Run(new SplineSamplerNode { Mode = SplineSamplerNode.SampleMode.Along, Spacing = 1f }, context);
            Assert.AreEqual(11, outputs[0].Count);
            for (int i = 0; i < outputs[0].Count; i++)
            {
                Assert.That(Vector3.Distance(new Vector3(-5 + i, 0, 0), outputs[0].Points[i].Position), Is.LessThan(1e-3f));
                Assert.That(Vector3.Angle(Vector3.right, outputs[0].Points[i].Rotation * Vector3.forward), Is.LessThan(0.5f));
            }
        }

        [Test]
        public void SplineSampler_Inside_KeepsGridPointsInsideOutline()
        {
            PlacementContext context = Context();
            context.Spline = CreateSpline(true, new Vector3(-3, 0, -3), new Vector3(3, 0, -3), new Vector3(3, 0, 3), new Vector3(-3, 0, 3));
            PointData[] outputs = Run(new SplineSamplerNode { Mode = SplineSamplerNode.SampleMode.Inside, Spacing = 1f, Jitter = 0f }, context);
            Assert.AreEqual(36, outputs[0].Count);
            Assert.IsTrue(outputs[0].Points.All(p => Mathf.Abs(p.Position.x) < 3f && Mathf.Abs(p.Position.z) < 3f));

            PlacementContext open = Context();
            open.Spline = CreateSpline(false, new Vector3(-3, 0, -3), new Vector3(3, 0, 3));
            Assert.AreEqual(0, Run(new SplineSamplerNode { Mode = SplineSamplerNode.SampleMode.Inside }, open)[0].Count);
            Assert.AreEqual(1, open.Warnings.Count);
        }
#endif

        [Test]
        public void EditorNodes_HaveReadableTitles()
        {
            const string path = "Assets/__PlacementNodeTitles.placegraph";
            _assets.Add(path);
            var graph = GraphDatabase.CreateGraph<Editor.PlacementGraphDefinition>(path);
            graph.AddNode(new Editor.VolumeSamplerNode());
            graph.AddNode(new Editor.SpawnPrefabsNode());
            List<INode> nodes = graph.GetNodes().ToList();
            Assert.AreEqual("Volume Sampler", nodes[0].Title);
            Assert.AreEqual("Spawn Prefabs", nodes[1].Title);
            Assert.AreEqual("Self Pruning", Editor.PlacementGraphNode.TitleOf(typeof(Editor.SelfPruningNode)));

            foreach (System.Type type in EditorNodeTypes())
            {
                var attribute = (NodeAttribute)System.Attribute.GetCustomAttribute(type, typeof(NodeAttribute));
                Assert.IsNotNull(attribute, $"{type.Name} has no [Node] attribute.");
                Assert.IsFalse(string.IsNullOrEmpty(attribute.Title), $"{type.Name} has no title.");
                Assert.IsFalse(string.IsNullOrEmpty(attribute.CategoryPath), $"{type.Name} has no category.");
            }
        }

        private static List<System.Type> EditorNodeTypes() => typeof(Editor.PlacementGraphNode).Assembly.GetTypes()
            .Where(t => typeof(Editor.PlacementGraphNode).IsAssignableFrom(t) && !t.IsAbstract).ToList();

        [Test]
        public void EditorNodes_PortsMatchRuntimeNodes()
        {
            const string path = "Assets/__PlacementNodeTests.placegraph";
            _assets.Add(path);
            var graph = GraphDatabase.CreateGraph<Editor.PlacementGraphDefinition>(path);
            var types = EditorNodeTypes();
            Assert.Greater(types.Count, 10);
            foreach (System.Type type in types)
            {
                var node = (Editor.PlacementGraphNode)System.Activator.CreateInstance(type);
                graph.AddNode(node);
                INode added = graph.GetNodes().Last();
                PlacementNode runtime = ((Editor.PlacementGraphNode)added).CreateRuntimeNode();
                Assert.AreEqual(runtime.InputCount, added.InputPortCount, $"{type.Name} inputs");
                Assert.AreEqual(runtime.OutputCount, added.OutputPortCount, $"{type.Name} outputs");
            }
        }
    }
}
