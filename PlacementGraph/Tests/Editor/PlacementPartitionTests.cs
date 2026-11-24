using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityTools.Placement.Tests
{
    /// <summary>Partitioning: cells that run separately and regenerate only when their inputs change.</summary>
    [SceneIsolation]
    public class PlacementPartitionTests
    {
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

        private PlacementGraph Component(List<PlacementNode> nodes, List<PlacementEdge> edges, Vector3 size, float cellSize, float overlap = 2f)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].Name = i.ToString();
                nodes[i].NodeSeed = (uint)(i + 1) * 7907u;
            }
            var graph = Track(ScriptableObject.CreateInstance<PlacementGraphAsset>());
            graph.SetContents(nodes, edges);
            var component = Track(new GameObject("Placement Graph")).AddComponent<PlacementGraph>();
            component.Size = size;
            component.CellSize = cellSize;
            component.CellOverlap = overlap;
            component.Graph = graph;
            return component;
        }

        private List<PrefabEntry> Entries() => new List<PrefabEntry> { new PrefabEntry { Prefab = Track(new GameObject("Prop")) } };

        private static List<Vector3> Positions(PlacementGraph component) =>
            component.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Prop")).Select(t => t.position).ToList();

        private static void AssertSamePositions(List<Vector3> a, List<Vector3> b)
        {
            Assert.AreEqual(a.Count, b.Count);
            var sortedA = a.OrderBy(p => p.x).ThenBy(p => p.z).ToList();
            var sortedB = b.OrderBy(p => p.x).ThenBy(p => p.z).ToList();
            for (int i = 0; i < sortedA.Count; i++) Assert.That(Vector3.Distance(sortedA[i], sortedB[i]), Is.LessThan(1e-4f));
        }

        private List<PlacementNode> SamplerAndSpawn(float spacing = 1f) => new List<PlacementNode>
        {
            new VolumeSamplerNode { Spacing = spacing, Jitter = 1f },
            new TransformPointsNode { ScaleMin = 0.5f, ScaleMax = 2f },
            new SpawnPrefabsNode { Entries = Entries() }
        };

        private static List<PlacementEdge> Chain(int count) => Enumerable.Range(0, count - 1).Select(i => new PlacementEdge(i, 0, i + 1, 0)).ToList();

        [Test]
        public void Cells_OwnTheirObjects_AndTheResultDoesNotDependOnCellSize()
        {
            PlacementGraph small = Component(SamplerAndSpawn(), Chain(3), new Vector3(20f, 4f, 20f), cellSize: 5f);
            PlacementGraph large = Component(SamplerAndSpawn(), Chain(3), new Vector3(20f, 4f, 20f), cellSize: 7f);
            PlacementGraph single = Component(SamplerAndSpawn(), Chain(3), new Vector3(20f, 4f, 20f), cellSize: 100f);
            small.Generate();
            large.Generate();
            single.Generate();

            Assert.AreEqual(16, small.CellIndices.Count());
            Assert.AreEqual(400, small.InstanceCount);
            Assert.IsTrue(small.CellIndices.All(c => small.CellContainer(c) != null && small.CellContainer(c).transform.parent == small.transform));
            List<Vector3> reference = Positions(single);
            AssertSamePositions(reference, Positions(small));
            AssertSamePositions(reference, Positions(large));
        }

        [Test]
        public void MovingAMarker_RegeneratesOnlyTheCellsItTouches()
        {
            var nodes = new List<PlacementNode> { new ChildMarkersNode(), new SpawnPrefabsNode { Entries = Entries() } };
            PlacementGraph component = Component(nodes, Chain(2), new Vector3(20f, 4f, 20f), cellSize: 5f, overlap: 1f);
            var markers = new GameObject("Markers").transform;
            markers.SetParent(component.transform, false);
            var marker = new GameObject("Marker").transform;
            marker.SetParent(markers, false);
            marker.position = new Vector3(-7.5f, 0f, -7.5f);
            component.Markers = markers;
            component.Generate();
            Assert.AreEqual(16, component.LastRegeneratedCells.Count);
            Assert.AreEqual(1, component.InstanceCount);
            GameObject untouched = component.CellContainer(new Vector2Int(1, 1));

            marker.position = new Vector3(-7.4f, 0f, -7.5f);
            component.RegenerateChanged();
            CollectionAssert.AreEquivalent(new[] { new Vector2Int(-2, -2) }, component.LastRegeneratedCells, "Only the marker's cell.");
            Assert.AreEqual(untouched, component.CellContainer(new Vector2Int(1, 1)), "Other cells keep their objects.");
            Assert.AreEqual(1, component.InstanceCount);
            Assert.IsNotNull(markers.parent, "The Markers object is kept.");

            component.RegenerateChanged();
            Assert.AreEqual(0, component.LastRegeneratedCells.Count, "Nothing changed.");
        }

        [Test]
        public void GrowingTheVolume_RegeneratesOnlyTheCellsItChanges()
        {
            PlacementGraph component = Component(SamplerAndSpawn(), Chain(3), new Vector3(20f, 4f, 20f), cellSize: 5f, overlap: 0f);
            component.Generate();
            GameObject inner = component.CellContainer(new Vector2Int(0, 0));

            // Wider by 10 m (5 m on each side): only the new outer columns are new or changed.
            component.Size = new Vector3(30f, 4f, 20f);
            component.RegenerateChanged();
            Assert.AreEqual(24, component.CellIndices.Count());
            Assert.AreEqual(8, component.LastRegeneratedCells.Count, string.Join(" ", component.LastRegeneratedCells));
            Assert.IsTrue(component.LastRegeneratedCells.All(c => c.x == -3 || c.x == 2));
            Assert.AreEqual(inner, component.CellContainer(new Vector2Int(0, 0)));

            // Shrinking removes the cells and what they made.
            component.Size = new Vector3(10f, 4f, 10f);
            component.RegenerateChanged();
            Assert.AreEqual(4, component.CellIndices.Count());
            Assert.AreEqual(4, component.transform.childCount);
        }

        [Test]
        public void SelfPruning_AcrossBorders_KeepsItsDistanceWithEnoughOverlap()
        {
            var nodes = new List<PlacementNode>
            {
                new VolumeSamplerNode { Spacing = 0.5f, Jitter = 1f },
                new SelfPruningNode { Radius = 1f },
                new SpawnPrefabsNode { Entries = Entries() }
            };
            PlacementGraph component = Component(nodes, Chain(3), new Vector3(16f, 4f, 16f), cellSize: 4f, overlap: 3f);
            component.Generate();
            List<Vector3> positions = Positions(component);
            Assert.Greater(positions.Count, 50);
            int tooClose = 0;
            for (int i = 0; i < positions.Count; i++)
            for (int j = i + 1; j < positions.Count; j++)
                if ((positions[i] - positions[j]).sqrMagnitude < 0.999f) tooClose++;
            Assert.AreEqual(0, tooClose, "No pair closer than the radius, even across cell borders.");
        }

        [Test]
        public void SpawnInstanced_KeepsBatchesPerCell()
        {
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Track(prefab);
            prefab.GetComponent<MeshRenderer>().sharedMaterial = Track(new Material(Shader.Find("Hidden/Internal-Colored")) { enableInstancing = true });
            var nodes = new List<PlacementNode>
            {
                new VolumeSamplerNode { Spacing = 1f },
                new SpawnInstancedNode { Entries = new List<PrefabEntry> { new PrefabEntry { Prefab = prefab } } }
            };
            PlacementGraph component = Component(nodes, Chain(2), new Vector3(10f, 4f, 10f), cellSize: 5f, overlap: 0f);
            component.Generate();
            Assert.AreEqual(4, component.Instances.Batches.Count, "One batch per cell.");
            Assert.AreEqual(100, component.Instances.InstanceCount);
            Assert.AreEqual(100, component.InstanceCount);

            component.CellSize = 0f;
            component.Generate();
            Assert.AreEqual(1, component.Instances.Batches.Count);
            Assert.AreEqual(0, component.transform.childCount, "Cell containers are gone.");
        }
    }
}
