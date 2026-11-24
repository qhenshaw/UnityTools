using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using UnityTools.Placement.Editor;
using Object = UnityEngine.Object;

namespace UnityTools.Placement.Tests
{
    /// <summary>The authoring API: graphs built from code are ordinary .placegraph assets.</summary>
    public class PlacementAuthoringTests
    {
        private const string Folder = "Assets/__PlacementAuthoringTests";
        private const string GraphPath = Folder + "/Graph.placegraph";
        private const string SubPath = Folder + "/Sub.placegraph";
        private const string PrefabPath = Folder + "/Prop.prefab";
        private readonly List<Object> _created = new();

        private sealed class RecordingOutput : IPlacementOutput
        {
            public readonly List<(GameObject prefab, Vector3 scale)> Spawned = new();
            public void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale) => Spawned.Add((prefab, scale));
        }

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__PlacementAuthoringTests");
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

        /// <summary>A prefab asset (a saved graph can only reference assets).</summary>
        private GameObject SavedPrefab()
        {
            var source = new GameObject("Prop");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, PrefabPath);
            Object.DestroyImmediate(source);
            return prefab;
        }

        private static List<(GameObject prefab, Vector3 scale)> Run(PlacementGraphAsset asset, params PlacementParameter[] overrides)
        {
            var output = new RecordingOutput();
            var context = new PlacementContext { LocalBounds = new Bounds(Vector3.zero, new Vector3(10f, 4f, 10f)), Output = output };
            context.ParameterOverrides.AddRange(overrides);
            PlacementExecutor.Run(asset, context);
            return output.Spawned;
        }

        [Test]
        public void ListNodeTypes_DescribesPortsAndSettings()
        {
            IReadOnlyList<PlacementNodeTypeInfo> types = PlacementGraphAuthoring.ListNodeTypes();
            PlacementNodeTypeInfo filter = types.Single(t => t.Title == "Density Filter");
            Assert.AreEqual("Filters", filter.Category);
            Assert.AreEqual(typeof(DensityFilterNode), filter.RuntimeType);
            CollectionAssert.AreEqual(new[] { "In" }, filter.PointInputs);
            CollectionAssert.AreEqual(new[] { "Out", "Rejected" }, filter.PointOutputs);
            PlacementSettingInfo threshold = filter.Settings.Single(s => s.Name == "Threshold");
            Assert.AreEqual(typeof(float), threshold.Type);
            Assert.AreEqual(0.5f, threshold.DefaultValue);
            Assert.AreEqual(typeof(DensityFilterNode.FilterMode), filter.Settings.Single(s => s.Name == "Mode").Type);

            string text = PlacementGraphAuthoring.DescribeNodeTypes();
            StringAssert.Contains("Surface Sampler (Sources)", text);
            StringAssert.Contains("setting Mode: FilterMode [Threshold, Random] = Random", text);
        }

        [Test]
        public void BuildSaveReimportAndRun()
        {
            GameObject prefab = SavedPrefab();
            PlacementGraphHandle graph = PlacementGraphAuthoring.Create(GraphPath);
            PlacementNodeHandle sampler = graph.AddNode("Volume Sampler").Set("Spacing", 2).Set("Jitter", 0f);
            PlacementNodeHandle filter = graph.AddNode<DensityFilterNode>().Set("Mode", "Threshold").Set("Threshold", 0.5f);
            PlacementNodeHandle transform = graph.AddNode("transform points");
            PlacementNodeHandle spawn = graph.AddNode("Spawn Prefabs");
            graph.AddEntry(spawn, PrefabPath);
            graph.Connect(sampler, "Out", filter, "In");
            graph.Connect(filter, "Out", transform, "In");
            graph.Connect(transform, "Out", spawn, "In");
            graph.AddParameter("Size", 2f);
            graph.BindParameter("Size", transform, "Scale Min");
            graph.BindParameter("Size", transform, "Scale Max");
            CollectionAssert.IsEmpty(graph.Validate());

            PlacementGraphAsset asset = graph.Save();
            Assert.IsNotNull(asset);
            Assert.AreEqual(4, asset.Nodes.Count);
            Assert.IsInstanceOf<VolumeSamplerNode>(asset.Nodes[0]);
            Assert.AreEqual(2f, ((VolumeSamplerNode)asset.Nodes[0]).Spacing);
            Assert.AreEqual(DensityFilterNode.FilterMode.Threshold, ((DensityFilterNode)asset.Nodes[1]).Mode);
            Assert.AreEqual(prefab, ((SpawnPrefabsNode)asset.Nodes[3]).Entries.Single().Prefab, "The Prefab Entry compiles into the node's entries.");
            CollectionAssert.AreEqual(new[] { new PlacementEdge(0, 0, 1, 0), new PlacementEdge(1, 0, 2, 0), new PlacementEdge(2, 0, 3, 0) },
                asset.Edges.OrderBy(e => e.ToNode));
            Assert.AreEqual("Size", asset.Parameters.Single().Name);

            List<(GameObject prefab, Vector3 scale)> spawned = Run(asset);
            Assert.AreEqual(25, spawned.Count, "Every point has density 1, so the threshold keeps all 5 x 5.");
            Assert.IsTrue(spawned.All(s => s.prefab == prefab && s.scale == Vector3.one * 2f));
            Assert.IsTrue(Run(asset, new PlacementParameter("Size", PlacementParameterType.Float, 3f)).All(s => s.scale == Vector3.one * 3f));

            // The file is a normal graph: Graph Toolkit loads it with its nodes and wires.
            var loaded = GraphDatabase.LoadGraph<PlacementGraphDefinition>(GraphPath);
            Assert.AreEqual(5, loaded.GetNodes().OfType<PlacementGraphNode>().Count(), "Four nodes and the Prefab Entry.");
            PlacementGraphHandle reopened = PlacementGraphAuthoring.Open(GraphPath);
            Assert.AreEqual(2f, reopened.FindNode("Volume Sampler").Get("Spacing"));
            Assert.IsTrue(reopened.FindNode("Spawn Prefabs").Node.GetInputPortByName("In").IsConnected);
        }

        [Test]
        public void Subgraph_BuiltThroughTheApi()
        {
            GameObject prefab = SavedPrefab();
            PlacementGraphHandle sub = PlacementGraphAuthoring.Create(SubPath);
            PlacementNodeHandle points = sub.AddPointInput("Points");
            PlacementNodeHandle result = sub.AddPointOutput("Result");
            PlacementNodeHandle prune = sub.AddNode("Self Pruning");
            sub.Connect(points, "Out", prune, "In");
            sub.Connect(prune, "Out", result, "In");
            sub.AddParameter("Radius", 3f);
            sub.BindParameter("Radius", prune, "Radius");
            sub.Save();

            PlacementGraphHandle graph = PlacementGraphAuthoring.Create(GraphPath);
            PlacementNodeHandle sampler = graph.AddNode("Volume Sampler").Set("Jitter", 0f);
            PlacementNodeHandle subgraph = graph.AddSubgraph(sub).Set("Radius", 2.5f);
            PlacementNodeHandle spawn = graph.AddNode("Spawn Prefabs");
            graph.AddEntry(spawn, prefab);
            CollectionAssert.AreEqual(new[] { "Points", "Radius" }, subgraph.InputPorts);
            graph.Connect(sampler, "Out", subgraph, "Points");
            graph.Connect(subgraph, "Result", spawn, "In");
            PlacementGraphAsset asset = graph.Save();

            Assert.AreEqual(3, asset.Nodes.Count);
            Assert.AreEqual(2.5f, asset.Nodes.OfType<SelfPruningNode>().Single().Radius);
            List<(GameObject prefab, Vector3 scale)> spawned = Run(asset);
            Assert.That(spawned.Count, Is.InRange(9, 25), "A 10 x 10 grid pruned to 2.5 m apart.");
            Assert.AreEqual(spawned.Count, Run(asset).Count, "Deterministic.");
        }

        [Test]
        public void CopiedNodes_GetTheirOwnRandomNumbers()
        {
            PlacementGraphHandle graph = PlacementGraphAuthoring.Create(GraphPath);
            PlacementNodeHandle first = graph.AddNode("Transform Points");
            PlacementNodeHandle second = graph.AddNode("Transform Points");
            // What copy and paste does: the copy keeps the original's id.
            typeof(PlacementGraphNode).GetField("_id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(second.Node, ((PlacementGraphNode)first.Node).Id);

            PlacementGraphAsset compiled = graph.Compile();
            _created.Add(compiled);
            Assert.AreNotEqual(compiled.Nodes[0].NodeSeed, compiled.Nodes[1].NodeSeed, "Compiling a graph with a copied id still separates the seeds.");

            Assert.AreEqual(1, ((PlacementGraphDefinition)graph.Graph).RenewDuplicateIds());
            Assert.AreNotEqual(((PlacementGraphNode)first.Node).Id, ((PlacementGraphNode)second.Node).Id);
            Assert.AreEqual(0, ((PlacementGraphDefinition)graph.Graph).RenewDuplicateIds());
        }

        [Test]
        public void ApplyToPrefab_WithoutSelectPrefab_IsReported()
        {
            PlacementGraphHandle graph = PlacementGraphAuthoring.Create(GraphPath);
            PlacementNodeHandle transform = graph.AddNode("Transform Points").Set("Apply To", "OnlyPrefab");
            Assert.IsTrue(graph.Validate().Any(p => p.Contains("no Select Prefab")));
            graph.AddNode("Select Prefab");
            Assert.IsFalse(graph.Validate().Any(p => p.Contains("no Select Prefab")));
        }

        [Test]
        public void Mistakes_ThrowClearErrors()
        {
            PlacementGraphHandle graph = PlacementGraphAuthoring.Create(GraphPath);
            PlacementNodeHandle sampler = graph.AddNode("Volume Sampler");
            PlacementNodeHandle spawn = graph.AddNode("Spawn Prefabs");

            var unknownNode = Assert.Throws<System.ArgumentException>(() => graph.AddNode("Tree Maker"));
            StringAssert.Contains("Volume Sampler", unknownNode.Message);
            var unknownPort = Assert.Throws<System.ArgumentException>(() => graph.Connect(sampler, "Points", spawn, "In"));
            StringAssert.Contains("Out", unknownPort.Message);
            Assert.Throws<System.InvalidOperationException>(() => graph.Connect(sampler, "Out", spawn, "Entries"));
            var badValue = Assert.Throws<System.ArgumentException>(() => sampler.Set("Spacing", "wide"));
            StringAssert.Contains("Single", badValue.Message);
            Assert.Throws<System.ArgumentException>(() => sampler.Set("Height", 1f));
            Assert.Throws<System.ArgumentException>(() => graph.AddNode<DensityFilterNode>().Set("Mode", "Sometimes"));
            Assert.Throws<System.ArgumentException>(() => graph.AddParameter("Name", "text"));
            graph.AddParameter("Count", 3);
            Assert.Throws<System.InvalidOperationException>(() => graph.BindParameter("Count", sampler, "Spacing"));
            Assert.Throws<System.ArgumentException>(() => graph.BindParameter("Missing", sampler, "Spacing"));
            Assert.Throws<System.InvalidOperationException>(() => PlacementGraphAuthoring.Create(GraphPath));
            Assert.Throws<System.ArgumentException>(() => PlacementGraphAuthoring.Create("Assets/NoExtension.asset"));
            Assert.Throws<System.IO.FileNotFoundException>(() => PlacementGraphAuthoring.Open(Folder + "/Missing.placegraph"));
        }
    }
}
