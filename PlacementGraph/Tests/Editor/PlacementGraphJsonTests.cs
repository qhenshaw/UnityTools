using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityTools.Placement.Editor;
using Object = UnityEngine.Object;

namespace UnityTools.Placement.Tests
{
    /// <summary>The handlers behind the `unity command placement*` CLI commands, on a temporary graph.</summary>
    [SceneIsolation]
    public class PlacementGraphJsonTests
    {
        private const string Folder = "Assets/__PlacementGraphJsonTests";
        private const string GraphPath = Folder + "/JsonGraph.placegraph";
        private readonly List<Object> _created = new();
        private GameObject _propA;
        private GameObject _propB;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__PlacementGraphJsonTests");
            _propA = SavedPrefab("JsonPropA");
            _propB = SavedPrefab("JsonPropB");

            // Create Grid > Spawn Prefabs with one entry: a known number of points in any volume.
            PlacementGraphHandle graph = PlacementGraphAuthoring.Create(GraphPath);
            PlacementNodeHandle grid = graph.AddNode("Create Grid").Set("Cell Size", new Vector3(1f, 1f, 1f));
            PlacementNodeHandle spawn = graph.AddNode("Spawn Prefabs");
            graph.Connect(grid, "Out", spawn, "In");
            graph.AddEntry(spawn, _propA);
            graph.Save();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
            AssetDatabase.DeleteAsset(Folder);
        }

        private static GameObject SavedPrefab(string name)
        {
            var source = new GameObject(name);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, $"{Folder}/{name}.prefab");
            Object.DestroyImmediate(source);
            return prefab;
        }

        private static void AssertOk(string json) => Assert.IsFalse(json.StartsWith("{\"error\""), json);

        private static string ErrorOf(string json)
        {
            StringAssert.StartsWith("{\"error\":", json);
            return json;
        }

        private static int Count(string json, string key)
        {
            Match match = Regex.Match(json, $"\"{Regex.Escape(key)}\":(\\d+)");
            Assert.IsTrue(match.Success, $"No \"{key}\" count in {json}");
            return int.Parse(match.Groups[1].Value);
        }

        private static PlacementNodeHandle Spawn(PlacementGraphHandle graph) => graph.FindNode("Spawn Prefabs");

        [Test]
        public void Inspect_ListsNodesWiresAndEntries()
        {
            string json = PlacementGraphJson.Inspect("JsonGraph");
            AssertOk(json);
            StringAssert.Contains("\"type\":\"Create Grid\"", json);
            StringAssert.Contains("\"type\":\"Spawn Prefabs\"", json);
            StringAssert.Contains("\"entries\":[{\"index\":0", json);
            StringAssert.Contains($"\"Prefab\":\"{Folder}/JsonPropA.prefab\"", json);
            StringAssert.Contains(".Out -> ", json);
            StringAssert.Contains("\"errors\":[]", json);

            string node = PlacementGraphJson.Inspect(GraphPath, "Create Grid");
            AssertOk(node);
            StringAssert.Contains("\"name\":\"Cell Size\",\"type\":\"Vector3\"", node);
        }

        [Test]
        public void Inspect_UnknownNode_SuggestsTheClosest()
        {
            string json = ErrorOf(PlacementGraphJson.Inspect("JsonGraph", "Create Gird"));
            StringAssert.Contains("Did you mean 'Create Grid'?", json);
            ErrorOf(PlacementGraphJson.Inspect("NoSuchGraph"));
        }

        [Test]
        public void NodeSet_SetsNumbersEnumsAndVectors_AndReportsUnknownSettings()
        {
            string added = PlacementGraphJson.NodeAdd("JsonGraph", "Density Filter", "Mode=Random");
            AssertOk(added);
            string id = Regex.Match(added, "\"id\":\"([^\"]+)\"").Groups[1].Value;

            // Setting an enum a second time works too (the first may store it as a constant node).
            AssertOk(PlacementGraphJson.NodeSet("JsonGraph", id, "Mode=Threshold"));
            AssertOk(PlacementGraphJson.NodeSet("JsonGraph", "Create Grid", "Cell Size=2,1,3;Three Dimensional=true"));

            PlacementGraphHandle graph = PlacementGraphAuthoring.Open(GraphPath);
            Assert.AreEqual(new Vector3(2f, 1f, 3f), graph.FindNode("Create Grid").Get("Cell Size"));
            Assert.AreEqual(true, graph.FindNode("Create Grid").Get("Three Dimensional"));
            StringAssert.Contains("\"Mode\":\"Threshold\"", PlacementGraphJson.Inspect("JsonGraph"));

            string error = ErrorOf(PlacementGraphJson.NodeSet("JsonGraph", "Create Grid", "Cell Sise=1"));
            StringAssert.Contains("Did you mean 'Cell Size'?", error);
            ErrorOf(PlacementGraphJson.NodeSet("JsonGraph", "Create Grid", "Cell Size=abc"));
        }

        [Test]
        public void EntriesSet_ReplacesTheChain_AndAddRemoveRewireIt()
        {
            AssertOk(PlacementGraphJson.EntriesSet("JsonGraph", "Spawn Prefabs", "JsonPropB", "Random Scale=false;Weight=2"));
            PlacementGraphHandle graph = PlacementGraphAuthoring.Open(GraphPath);
            List<PlacementNodeHandle> entries = graph.EntriesOf(Spawn(graph));
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(_propB, entries[0].Get("Prefab"));
            Assert.AreEqual(2f, entries[0].Get("Weight"));
            Assert.AreEqual(false, entries[0].Get("Random Scale"));
            Assert.AreEqual(1, graph.Nodes.Count(n => n.Title == "Prefab Entry"), "the old entry is removed");

            AssertOk(PlacementGraphJson.EntriesAdd("JsonGraph", "Spawn Prefabs", $"{Folder}/JsonPropA.prefab", index: 0));
            entries = graph.EntriesOf(Spawn(graph));
            CollectionAssert.AreEqual(new Object[] { _propA, _propB }, entries.Select(e => e.Get("Prefab")).ToArray());

            AssertOk(PlacementGraphJson.EntriesEdit("JsonGraph", "Spawn Prefabs", 1, "Weight=5"));
            Assert.AreEqual(5f, graph.EntriesOf(Spawn(graph))[1].Get("Weight"));

            AssertOk(PlacementGraphJson.EntriesRemove("JsonGraph", "Spawn Prefabs", prefab: "JsonPropA"));
            entries = graph.EntriesOf(Spawn(graph));
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(_propB, entries[0].Get("Prefab"));

            ErrorOf(PlacementGraphJson.EntriesSet("JsonGraph", "Create Grid", "JsonPropA"));
            ErrorOf(PlacementGraphJson.EntriesSet("JsonGraph", "Spawn Prefabs", "NoSuchPrefab"));
        }

        [Test]
        public void EntriesSet_OnSubdivide_SetsModules()
        {
            AssertOk(PlacementGraphJson.NodeAdd("JsonGraph", "Subdivide", "Grammar=[JsonPropA]*"));
            AssertOk(PlacementGraphJson.EntriesSet("JsonGraph", "Subdivide", "JsonPropA;JsonPropB", "Size=2"));
            string list = PlacementGraphJson.EntriesList("JsonGraph", "Subdivide");
            StringAssert.Contains("\"modules\":[", list);
            StringAssert.Contains("\"Symbol\":\"JsonPropA\"", list);
            StringAssert.Contains("\"Symbol\":\"JsonPropB\"", list);
            StringAssert.Contains("\"Size\":2", list);
        }

        [Test]
        public void Wires_ConnectAndDisconnect()
        {
            AssertOk(PlacementGraphJson.NodeAdd("JsonGraph", "Merge"));
            AssertOk(PlacementGraphJson.Connect("JsonGraph", "Create Grid", "Merge", toPort: "A"));
            PlacementGraphHandle graph = PlacementGraphAuthoring.Open(GraphPath);
            Assert.IsTrue(graph.FindNode("Merge").Node.GetInputPortByName("A").IsConnected);

            AssertOk(PlacementGraphJson.Disconnect("JsonGraph", "Merge", "A"));
            Assert.IsFalse(graph.FindNode("Merge").Node.GetInputPortByName("A").IsConnected);

            StringAssert.Contains("no input port", ErrorOf(PlacementGraphJson.Connect("JsonGraph", "Create Grid", "Merge", toPort: "Q")));
            ErrorOf(PlacementGraphJson.Disconnect("JsonGraph", "Merge", "A"));
        }

        [Test]
        public void Evaluate_CountsPointsAndSpawns_WithoutTouchingTheScene()
        {
            int roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().rootCount;
            string json = PlacementGraphJson.Evaluate("JsonGraph", size: "4,2,4");
            AssertOk(json);
            int spawned = Count(json, "JsonPropA");
            Assert.Greater(spawned, 0);
            StringAssert.Contains($"\"points\":{spawned}", json);
            Assert.AreEqual(roots, UnityEngine.SceneManagement.SceneManager.GetActiveScene().rootCount);

            // Unsaved edits are evaluated too.
            AssertOk(PlacementGraphJson.EntriesSet("JsonGraph", "Spawn Prefabs", "JsonPropB", save: false));
            StringAssert.Contains("\"JsonPropB\":", PlacementGraphJson.Evaluate("JsonGraph", size: "4,2,4"));
        }

        [Test]
        public void Generate_SpawnsByPrefab_StatsAndClear()
        {
            string created = PlacementGraphJson.ComponentCreate("JsonGraph", "JsonGraphObject", "0,0,0", "4,2,4", generate: false);
            AssertOk(created);
            var go = GameObject.Find("JsonGraphObject");
            _created.Add(go);

            string generated = PlacementGraphJson.Generate("JsonGraphObject");
            AssertOk(generated);
            int spawned = Count(generated, "JsonPropA");
            Assert.Greater(spawned, 0);
            Assert.AreEqual(spawned, go.transform.childCount);
            Assert.AreEqual(spawned, Count(PlacementGraphJson.Stats("JsonGraphObject"), "objects"));

            AssertOk(PlacementGraphJson.ComponentSet("JsonGraphObject", "Seed=7;Size=6,2,6;Auto Generate=false"));
            var component = go.GetComponent<PlacementGraph>();
            Assert.AreEqual(7, component.Seed);
            Assert.AreEqual(new Vector3(6f, 2f, 6f), component.Size);
            StringAssert.Contains("Did you mean 'Seed'?", ErrorOf(PlacementGraphJson.ComponentSet("JsonGraphObject", "Sead=1")));

            AssertOk(PlacementGraphJson.Clear("JsonGraphObject"));
            Assert.AreEqual(0, go.transform.childCount);
        }

        [Test]
        public void Params_AddBindSetAndOverride()
        {
            AssertOk(PlacementGraphJson.ParamsAdd("JsonGraph", "Spacing", "Vector3", "2,1,2"));
            AssertOk(PlacementGraphJson.ParamsBind("JsonGraph", "Spacing", "Create Grid", "Cell Size"));
            string list = PlacementGraphJson.ParamsList("JsonGraph");
            StringAssert.Contains("\"name\":\"Spacing\",\"type\":\"Vector3\",\"default\":[2,1,2]", list);
            StringAssert.Contains(".Cell Size", list);

            AssertOk(PlacementGraphJson.ParamsSet("Spacing", "3,1,3", "JsonGraph"));
            StringAssert.Contains("\"default\":[3,1,3]", PlacementGraphJson.ParamsList("JsonGraph"));

            AssertOk(PlacementGraphJson.ComponentCreate("JsonGraph", "JsonParamsObject", generate: false));
            _created.Add(GameObject.Find("JsonParamsObject"));
            AssertOk(PlacementGraphJson.ParamsSet("Spacing", "4", obj: "JsonParamsObject"));
            StringAssert.Contains("\"override\":[4,4,4]", PlacementGraphJson.ParamsList(obj: "JsonParamsObject"));
            AssertOk(PlacementGraphJson.ParamsClear("JsonParamsObject", "Spacing"));
            StringAssert.Contains("\"override\":null", PlacementGraphJson.ParamsList(obj: "JsonParamsObject"));

            ErrorOf(PlacementGraphJson.ParamsSet("Spaceing", "1", "JsonGraph"));
        }

        [Test]
        public void ParsePairs_KeepsEscapedSeparators()
        {
            List<(string key, string value)> pairs = PlacementGraphJson.ParsePairs("Grammar=[A]\\;[B];Weight = 2");
            Assert.AreEqual(2, pairs.Count);
            Assert.AreEqual(("Grammar", "[A];[B]"), pairs[0]);
            Assert.AreEqual(("Weight", "2"), pairs[1]);
        }
    }
}
