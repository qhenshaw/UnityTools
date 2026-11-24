using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityTools.Placement.Tests
{
    /// <summary>Runtime generation, simulated in edit mode: Near Player cells, the frame budget, pooling and determinism.</summary>
    [SceneIsolation]
    public class PlacementRuntimeTests
    {
        private static readonly Vector3 Origin = new Vector3(-30000f, 0f, 0f);
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

        private PlacementGraph Component(PlacementGraph.GenerateMode mode, float cellSize = 10f)
        {
            var prop = Track(new GameObject("Prop"));
            var nodes = new List<PlacementNode>
            {
                new VolumeSamplerNode { Spacing = 2f, Jitter = 1f },
                new TransformPointsNode { ScaleMin = 0.5f, ScaleMax = 1.5f },
                new SpawnPrefabsNode { Entries = new List<PrefabEntry> { new PrefabEntry { Prefab = prop } } }
            };
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].Name = i.ToString();
                nodes[i].NodeSeed = (uint)(i + 1) * 4099u;
            }
            var graph = Track(ScriptableObject.CreateInstance<PlacementGraphAsset>());
            graph.SetContents(nodes, new List<PlacementEdge> { new(0, 0, 1, 0), new(1, 0, 2, 0) });
            var component = Track(new GameObject("Placement Graph")).AddComponent<PlacementGraph>();
            component.transform.position = Origin;
            component.Size = new Vector3(100f, 4f, 100f);
            component.CellSize = cellSize;
            component.CellOverlap = 0f;
            component.GenerateOn = mode;
            component.Graph = graph;
            component.SimulateRuntime(mode != PlacementGraph.GenerateMode.EditorOnly);
            return component;
        }

        private static List<(Vector3 position, Vector3 scale)> Spawned(PlacementGraph component) =>
            component.GetComponentsInChildren<Transform>(false).Where(t => t.name.StartsWith("Prop"))
                .Select(t => (t.position, t.localScale)).OrderBy(p => p.position.x).ThenBy(p => p.position.z).ToList();

        [Test]
        public void NearPlayer_LoadsCellsAroundTheSource_AndUnloadsFarOnes()
        {
            PlacementGraph component = Component(PlacementGraph.GenerateMode.NearPlayer);
            component.LoadRadius = 12f;
            component.UnloadRadius = 20f;
            var source = new[] { Origin + new Vector3(-45f, 0f, -45f) };
            while (component.RuntimeTick(1000f, source) > 0) { }
            Vector2Int[] loaded = component.CellIndices.ToArray();
            Assert.IsTrue(loaded.Length > 0 && loaded.Length <= 9, $"{loaded.Length} cells near the corner.");
            Assert.IsTrue(loaded.All(c => c.x <= -3 && c.y <= -3), string.Join(" ", loaded));

            // Moving to the far corner unloads those and loads new ones there.
            source[0] = Origin + new Vector3(45f, 0f, 45f);
            while (component.RuntimeTick(1000f, source) > 0) { }
            Vector2Int[] moved = component.CellIndices.ToArray();
            Assert.IsTrue(moved.All(c => c.x >= 2 && c.y >= 2), string.Join(" ", moved));
            Assert.IsFalse(moved.Intersect(loaded).Any());
        }

        [Test]
        public void FrameBudget_SpreadsCellsOverTicks_NearestFirst()
        {
            PlacementGraph component = Component(PlacementGraph.GenerateMode.NearPlayer);
            component.LoadRadius = 30f;
            var source = new[] { Origin };
            Assert.AreEqual(1, component.RuntimeTick(0f, source), "A zero budget still runs one cell.");
            Vector2Int first = component.LastRegeneratedCells.Single();
            Assert.That(new Vector2(first.x + 0.5f, first.y + 0.5f).magnitude, Is.LessThan(1f), "The cell at the source comes first.");
            int ticks = 1;
            while (component.RuntimeTick(0f, source) > 0) ticks++;
            Assert.AreEqual(component.CellIndices.Count(), ticks, "One cell per tick.");
            Assert.Greater(ticks, 10);
        }

        [Test]
        public void Pooling_ReusesObjectsOfUnloadedCells()
        {
            PlacementGraph component = Component(PlacementGraph.GenerateMode.NearPlayer);
            component.LoadRadius = 8f;
            component.UnloadRadius = 9f;
            var source = new[] { Origin + new Vector3(-30f, 0f, 0f) };
            while (component.RuntimeTick(1000f, source) > 0) { }
            var firstObjects = new HashSet<GameObject>(component.GetComponentsInChildren<Transform>(false).Where(t => t.name.StartsWith("Prop")).Select(t => t.gameObject));
            Assert.Greater(firstObjects.Count, 0);

            source[0] = Origin + new Vector3(30f, 0f, 0f);
            while (component.RuntimeTick(1000f, source) > 0) { }
            var secondObjects = new HashSet<GameObject>(component.GetComponentsInChildren<Transform>(false).Where(t => t.name.StartsWith("Prop")).Select(t => t.gameObject));
            Assert.Greater(secondObjects.Count, 0);
            Assert.Greater(secondObjects.Intersect(firstObjects).Count(), 0, "Objects from the unloaded cells are reused.");
            Assert.AreEqual(Mathf.Max(0, firstObjects.Count - secondObjects.Count), component.PooledCount);

            component.Pooling = false;
            source[0] = Origin + new Vector3(-30f, 0f, 0f);
            while (component.RuntimeTick(1000f, source) > 0) { }
            Assert.IsFalse(component.GetComponentsInChildren<Transform>(false).Where(t => t.name.StartsWith("Prop")).Any(t => secondObjects.Contains(t.gameObject)),
                "Without pooling, new objects are made.");
        }

        [Test]
        public void Runtime_MatchesTheEditorResult_ForTheSameSeed()
        {
            PlacementGraph editor = Component(PlacementGraph.GenerateMode.EditorOnly);
            editor.Generate();
            PlacementGraph runtime = Component(PlacementGraph.GenerateMode.NearPlayer);
            runtime.transform.position = Origin; // Same place as the editor one.
            runtime.LoadRadius = 1000f;
            runtime.UnloadRadius = 2000f;
            while (runtime.RuntimeTick(1000f, new[] { Origin }) > 0) { }

            List<(Vector3 position, Vector3 scale)> a = Spawned(editor);
            List<(Vector3 position, Vector3 scale)> b = Spawned(runtime);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(Vector3.Distance(a[i].position, b[i].position), Is.LessThan(1e-4f));
                Assert.That(Vector3.Distance(a[i].scale, b[i].scale), Is.LessThan(1e-4f));
            }
        }

        [Test]
        public void SaveGeneratedOff_MarksGeneratedObjectsAsNotSaved()
        {
            PlacementGraph component = Component(PlacementGraph.GenerateMode.EditorOnly, cellSize: 50f);
            component.Generate();
            Transform cell = component.transform.GetChild(0);
            Assert.AreEqual(HideFlags.None, cell.gameObject.hideFlags, "Saved by default.");

            component.SaveGenerated = false;
            component.Generate();
            foreach (Transform child in component.transform)
                Assert.IsTrue((child.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0, child.name);

            component.CellSize = 0f;
            component.Generate();
            Assert.Greater(component.transform.childCount, 0);
            foreach (Transform child in component.transform)
                Assert.IsTrue((child.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0, "Unpartitioned objects too.");
        }

        [Test]
        public void TrackSceneChanges_RegeneratesTheCellsAMovedMarkerTouches()
        {
            PlacementGraph component = Component(PlacementGraph.GenerateMode.NearPlayer);
            var prop = component.Graph.Nodes.OfType<SpawnPrefabsNode>().Single().Entries;
            var nodes = new List<PlacementNode> { new ChildMarkersNode { Name = "0" }, new SpawnPrefabsNode { Entries = prop, Name = "1" } };
            component.Graph.SetContents(nodes, new List<PlacementEdge> { new(0, 0, 1, 0) });
            var markers = new GameObject("Markers").transform;
            markers.SetParent(component.transform, false);
            var marker = new GameObject("Marker").transform;
            marker.SetParent(markers, false);
            marker.position = Origin + new Vector3(5f, 0f, 5f);
            component.Markers = markers;
            component.LoadRadius = 25f;
            var source = new[] { Origin };
            while (component.RuntimeTick(1000f, source) > 0) { }

            marker.position = Origin + new Vector3(6f, 0f, 5f);
            Assert.AreEqual(0, component.RuntimeTick(1000f, source), "Off by default: loaded cells don't notice.");

            component.TrackSceneChanges = true;
            component.RuntimeTick(1000f, source); // Takes its first look at the inputs.
            marker.position = Origin + new Vector3(7f, 0f, 5f);
            Assert.AreEqual(1, component.RuntimeTick(1000f, source), "Only the marker's cell reruns.");
            CollectionAssert.AreEqual(new[] { new Vector2Int(0, 0) }, component.LastRegeneratedCells);
        }

        [Test]
        public void MaxPoolSize_DestroysObjectsPastTheCap()
        {
            PlacementGraph component = Component(PlacementGraph.GenerateMode.NearPlayer);
            component.MaxPoolSize = 3;
            component.LoadRadius = 8f;
            component.UnloadRadius = 9f;
            var source = new[] { Origin + new Vector3(-30f, 0f, 0f) };
            while (component.RuntimeTick(1000f, source) > 0) { }
            source[0] = Origin + new Vector3(300f, 0f, 300f); // Outside the volume: everything unloads, nothing loads.
            component.RuntimeTick(1000f, source);
            Assert.AreEqual(0, component.CellIndices.Count());
            Assert.AreEqual(3, component.PooledCount);
        }

        [Test]
        public void Use3DDistance_CountsHeight()
        {
            PlacementGraph component = Component(PlacementGraph.GenerateMode.NearPlayer);
            component.LoadRadius = 12f;
            var above = new[] { Origin + new Vector3(0f, 100f, 0f) };
            Assert.Greater(component.RuntimeTick(1000f, above), 0, "Flat distance ignores the height.");
            component.Clear();
            component.Use3DDistance = true;
            Assert.AreEqual(0, component.RuntimeTick(1000f, above), "100 m up is out of range in 3D.");
        }

        [Test]
        public void SetParameter_AtRuntime_RegeneratesTheLoadedCells()
        {
            PlacementGraph component = Component(PlacementGraph.GenerateMode.NearPlayer);
            var nodes = component.Graph.Nodes.ToList();
            component.Graph.SetContents(nodes, component.Graph.Edges.ToList(),
                new List<PlacementParameter> { new("Spacing", PlacementParameterType.Float, 2f) },
                new List<PlacementParameterBinding> { new(0, "Spacing", "Spacing") });
            component.SyncParameters();
            component.LoadRadius = 6f;
            var source = new[] { Origin + new Vector3(5f, 0f, 5f) };
            while (component.RuntimeTick(1000f, source) > 0) { }
            int before = component.InstanceCount;
            Assert.Greater(before, 0);

            component.SetParameter("Spacing", 5f);
            int regenerated = 0;
            int ran;
            // A zero budget: one cell per tick, so the change has to carry over several ticks.
            while ((ran = component.RuntimeTick(0f, source)) > 0) regenerated += ran;
            Assert.AreEqual(component.CellIndices.Count(), regenerated, "Every loaded cell reran.");
            Assert.Less(component.InstanceCount, before, "Wider spacing places fewer.");
        }
    }
}
