using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityTools.Placement.Tests
{
    public class PlacementGraphTests
    {
        private readonly List<Object> _created = new();

        private sealed class RecordingOutput : IPlacementOutput
        {
            public readonly List<(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)> Spawned = new();

            public void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale) =>
                Spawned.Add((prefab, position, rotation, scale));
        }

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

        private PlacementGraphAsset CreateGraph(List<PlacementNode> nodes, List<PlacementEdge> edges)
        {
            var graph = Track(ScriptableObject.CreateInstance<PlacementGraphAsset>());
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].Name = $"{i}: {nodes[i].GetType().Name}";
                nodes[i].NodeSeed = (uint)(i + 1) * 7919u;
            }
            Assert.IsTrue(PlacementGraphAsset.TrySortTopologically(nodes, edges, out var sorted, out var sortedEdges));
            graph.SetContents(sorted, sortedEdges);
            return graph;
        }

        private List<PrefabEntry> CreatePalette(params GameObject[] prefabs)
        {
            var palette = new List<PrefabEntry>();
            foreach (GameObject prefab in prefabs) palette.Add(new PrefabEntry { Prefab = prefab, Weight = 1f });
            return palette;
        }

        private static PlacementContext Context(int seed = 1, IPlacementOutput output = null) => new PlacementContext
        {
            LocalBounds = new Bounds(Vector3.zero, new Vector3(10f, 4f, 10f)),
            Seed = seed,
            Output = output
        };

        [Test]
        public void Random_IsDeterministicAndInRange()
        {
            for (uint i = 0; i < 1000; i++)
            {
                float a = PlacementRandom.Value(PlacementRandom.Hash(i, 42u));
                Assert.AreEqual(a, PlacementRandom.Value(PlacementRandom.Hash(i, 42u)));
                Assert.That(a, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
            }
            Assert.AreNotEqual(PlacementRandom.Hash(1u, 2u), PlacementRandom.Hash(2u, 1u));
        }

        [Test]
        public void VolumeSampler_FillsGridInsideBounds()
        {
            var graph = CreateGraph(new List<PlacementNode> { new VolumeSamplerNode { Spacing = 1f, Jitter = 1f } }, new List<PlacementEdge>());
            PointData[][] outputs = PlacementExecutor.Run(graph, Context());
            Assert.AreEqual(100, outputs[0][0].Count);
            foreach (PlacementPoint point in outputs[0][0].Points)
            {
                Assert.That(point.Position.x, Is.InRange(-5f, 5f));
                Assert.That(point.Position.z, Is.InRange(-5f, 5f));
                Assert.AreEqual(2f, point.Position.y, 1e-4f);
            }
        }

        [Test]
        public void VolumeSampler_TooManyPoints_WarnsAndReturnsNothing()
        {
            var graph = CreateGraph(new List<PlacementNode> { new VolumeSamplerNode { Spacing = 0.01f } }, new List<PlacementEdge>());
            PlacementContext context = Context();
            PointData[][] outputs = PlacementExecutor.Run(graph, context);
            Assert.AreEqual(0, outputs[0][0].Count);
            Assert.AreEqual(1, context.Warnings.Count);
        }

        [Test]
        public void SameSeed_SameResult_DifferentSeed_DifferentResult()
        {
            var prefab = Track(new GameObject("Prefab"));
            List<PlacementNode> Nodes() => new List<PlacementNode>
            {
                new VolumeSamplerNode { Spacing = 0.5f, Jitter = 1f },
                new NoiseDensityNode { Scale = 3f },
                new DensityFilterNode { Mode = DensityFilterNode.FilterMode.Random },
                new TransformPointsNode { ScaleMin = 0.5f, ScaleMax = 2f },
                new SpawnPrefabsNode { Entries = CreatePalette(prefab) }
            };
            var edges = new List<PlacementEdge> { new(0, 0, 1, 0), new(1, 0, 2, 0), new(2, 0, 3, 0), new(3, 0, 4, 0) };

            var first = new RecordingOutput();
            var second = new RecordingOutput();
            var other = new RecordingOutput();
            PlacementExecutor.Run(CreateGraph(Nodes(), edges), Context(5, first));
            PlacementExecutor.Run(CreateGraph(Nodes(), edges), Context(5, second));
            PlacementExecutor.Run(CreateGraph(Nodes(), edges), Context(6, other));

            Assert.Greater(first.Spawned.Count, 0);
            Assert.AreEqual(first.Spawned.Count, second.Spawned.Count);
            for (int i = 0; i < first.Spawned.Count; i++)
            {
                Assert.AreEqual(first.Spawned[i].position, second.Spawned[i].position);
                Assert.AreEqual(first.Spawned[i].scale, second.Spawned[i].scale);
            }
            Assert.IsFalse(first.Spawned.Count == other.Spawned.Count &&
                           first.Spawned[0].position == other.Spawned[0].position,
                "A different seed should change the result.");
        }

        [Test]
        public void DensityFilter_Threshold_SplitsKeptAndRejected()
        {
            var node = new DensityFilterNode { Mode = DensityFilterNode.FilterMode.Threshold, Threshold = 0.5f };
            var input = new PointData();
            for (int i = 0; i < 10; i++)
            {
                PlacementPoint point = PlacementPoint.At(Vector3.zero, (uint)i);
                point.Density = i / 10f;
                input.Points.Add(point);
            }
            var outputs = new PointData[2];
            node.Execute(Context(), new[] { input }, outputs);
            Assert.AreEqual(5, outputs[0].Count);
            Assert.AreEqual(5, outputs[1].Count);
            Assert.AreEqual(10, input.Count, "Inputs must not be modified.");
        }

        [Test]
        public void SlopeFilter_KeepsPointsWithinAngle()
        {
            var node = new SlopeFilterNode { MinAngle = 0f, MaxAngle = 30f };
            PlacementPoint flat = PlacementPoint.At(Vector3.zero, 1);
            PlacementPoint steep = PlacementPoint.At(Vector3.zero, 2);
            steep.Normal = Quaternion.Euler(60f, 0f, 0f) * Vector3.up;
            var outputs = new PointData[2];
            node.Execute(Context(), new[] { new PointData(new List<PlacementPoint> { flat, steep }) }, outputs);
            Assert.AreEqual(1, outputs[0].Count);
            Assert.AreEqual(1, outputs[1].Count);
            Assert.AreEqual(1u, outputs[0].Points[0].Seed);
        }

        [Test]
        public void SurfaceSampler_ProjectsOntoColliders()
        {
            GameObject ground = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            Physics.SyncTransforms();

            var graph = CreateGraph(new List<PlacementNode> { new SurfaceSamplerNode { Spacing = 1f, Jitter = 0f } }, new List<PlacementEdge>());
            PointData[][] outputs = PlacementExecutor.Run(graph, Context());

            // Only the 4 x 4 cells over the cube hit something.
            Assert.AreEqual(16, outputs[0][0].Count);
            foreach (PlacementPoint point in outputs[0][0].Points)
            {
                Assert.AreEqual(0f, point.Position.y, 1e-3f);
                Assert.AreEqual(Vector3.up, point.Normal);
            }
        }

        [Test]
        public void SurfaceSampler_WithEmptyWiredInput_PlacesNothing()
        {
            GameObject ground = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            Physics.SyncTransforms();

            var nodes = new List<PlacementNode>
            {
                new VolumeSamplerNode { Spacing = 1f },
                new DensityFilterNode { Mode = DensityFilterNode.FilterMode.Threshold, Threshold = 1.1f },
                new SurfaceSamplerNode()
            };
            var graph = CreateGraph(nodes, new List<PlacementEdge> { new(0, 0, 1, 0), new(1, 0, 2, 0) });
            PointData[][] outputs = PlacementExecutor.Run(graph, Context());
            Assert.AreEqual(0, outputs[2][0].Count);
        }

        [Test]
        public void TopologicalSort_OrdersSourcesFirst_AndRejectsCycles()
        {
            var nodes = new List<PlacementNode> { new SpawnPrefabsNode(), new TransformPointsNode(), new VolumeSamplerNode() };
            var edges = new List<PlacementEdge> { new(2, 0, 1, 0), new(1, 0, 0, 0) };
            Assert.IsTrue(PlacementGraphAsset.TrySortTopologically(nodes, edges, out var sorted, out var sortedEdges));
            Assert.IsInstanceOf<VolumeSamplerNode>(sorted[0]);
            Assert.IsInstanceOf<TransformPointsNode>(sorted[1]);
            Assert.IsInstanceOf<SpawnPrefabsNode>(sorted[2]);
            Assert.AreEqual(new PlacementEdge(0, 0, 1, 0), sortedEdges[0]);

            var a = new TransformPointsNode();
            var b = new TransformPointsNode();
            Assert.IsFalse(PlacementGraphAsset.TrySortTopologically(new List<PlacementNode> { a, b },
                new List<PlacementEdge> { new(0, 0, 1, 0), new(1, 0, 0, 0) }, out _, out _));
        }

        [Test]
        public void Executor_MergesSeveralWiresIntoOneInput()
        {
            var nodes = new List<PlacementNode>
            {
                new VolumeSamplerNode { Spacing = 5f },
                new VolumeSamplerNode { Spacing = 2.5f },
                new TransformPointsNode()
            };
            var graph = CreateGraph(nodes, new List<PlacementEdge> { new(0, 0, 2, 0), new(1, 0, 2, 0) });
            PointData[][] outputs = PlacementExecutor.Run(graph, Context());
            Assert.AreEqual(4 + 16, outputs[2][0].Count);
        }

        [Test]
        public void Entries_PickByWeight()
        {
            var a = Track(new GameObject("A"));
            var b = Track(new GameObject("B"));
            var entries = new List<PrefabEntry>
            {
                new PrefabEntry { Prefab = a, Weight = 3f },
                new PrefabEntry { Prefab = b, Weight = 1f },
                new PrefabEntry { Prefab = null, Weight = 5f }
            };
            Assert.AreEqual(4f, PrefabEntry.TotalWeight(entries));
            Assert.AreEqual(0, PrefabEntry.PickIndex(entries, 0f));
            Assert.AreEqual(0, PrefabEntry.PickIndex(entries, 0.74f));
            Assert.AreEqual(1, PrefabEntry.PickIndex(entries, 0.76f));
            Assert.AreEqual(1, PrefabEntry.PickIndex(entries, 0.9999f));
            Assert.AreEqual(-1, PrefabEntry.PickIndex(new List<PrefabEntry>(), 0.5f));
        }

        [Test]
        public void Component_GenerateAndClear_ManageChildren()
        {
            GameObject ground = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ground.transform.localScale = new Vector3(10f, 1f, 10f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            Physics.SyncTransforms();

            var prefab = Track(new GameObject("Prop"));
            var graph = CreateGraph(new List<PlacementNode>
            {
                new SurfaceSamplerNode { Spacing = 2f, Jitter = 0f },
                new SpawnPrefabsNode { Entries = CreatePalette(prefab) }
            }, new List<PlacementEdge> { new(0, 0, 1, 0) });

            var go = Track(new GameObject("Placement Graph"));
            var component = go.AddComponent<PlacementGraph>();
            component.Size = new Vector3(10f, 4f, 10f);
            component.Graph = graph;
            component.Generate();
            Assert.AreEqual(25, go.transform.childCount);
            Assert.AreEqual(25, component.InstanceCount);
            Assert.AreEqual(25, component.LastNodeCounts[1]);

            component.Clear();
            Assert.AreEqual(0, go.transform.childCount);
        }
    }
}
