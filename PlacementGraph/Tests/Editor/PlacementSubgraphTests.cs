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
    /// <summary>Subgraphs: inlined at compile time, with Input and Output variables standing for the subgraph node's ports.</summary>
    public class PlacementSubgraphTests
    {
        private const string SubPath = "Assets/__PlacementSubgraphTests_Sub.placegraph";
        private const string MainPath = "Assets/__PlacementSubgraphTests_Main.placegraph";
        private readonly List<Object> _created = new();

        private sealed class RecordingOutput : IPlacementOutput
        {
            public readonly List<Vector3> Scales = new();
            public void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale) => Scales.Add(scale);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
            AssetDatabase.DeleteAsset(MainPath);
            AssetDatabase.DeleteAsset(SubPath);
        }

        private T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }

        private static T Add<T>(Graph graph) where T : PlacementGraphNode, new()
        {
            graph.AddNode(new T());
            return (T)graph.GetNodes().Last();
        }

        /// <summary>A subgraph: Points (in) > Transform Points with uniform scale Scale (default 3) > Result (out).</summary>
        private static Graph CreateScaleSubgraph()
        {
            var sub = GraphDatabase.CreateGraph<PlacementGraphDefinition>(SubPath);
            IVariable points = sub.CreateVariable<PointData>("Points", null, VariableKind.Input);
            IVariable result = sub.CreateVariable<PointData>("Result", null, VariableKind.Output);
            IVariable scale = sub.CreateVariable("Scale", 3f, VariableKind.Input);

            var transform = Add<Editor.TransformPointsNode>(sub);
            IVariableNode pointsNode = sub.AddVariableNode(points, Vector2.zero);
            IVariableNode resultNode = sub.AddVariableNode(result, Vector2.zero);
            IVariableNode scaleNode = sub.AddVariableNode(scale, Vector2.zero);
            Assert.IsTrue(sub.Connect(pointsNode.GetOutputPort(0), transform.GetInputPortByName("In")));
            Assert.IsTrue(sub.Connect(transform.GetOutputPortByName("Out"), resultNode.GetInputPort(0)));
            Assert.IsTrue(sub.Connect(scaleNode.GetOutputPort(0), transform.GetInputPortByName("Scale Min")));
            Assert.IsTrue(sub.Connect(scaleNode.GetOutputPort(0), transform.GetInputPortByName("Scale Max")));
            GraphDatabase.SaveGraph(sub);
            return sub;
        }

        private PrefabPalette Palette()
        {
            var palette = Track(ScriptableObject.CreateInstance<PrefabPalette>());
            palette.Entries.Add(new PrefabPalette.Entry { Prefab = Track(new GameObject("Prop")), Weight = 1f });
            return palette;
        }

        /// <summary>Main graph: Volume Sampler (4 points) > subgraph > Spawn Prefabs.</summary>
        private (Graph main, ISubgraphNode subgraphNode) CreateMain(Graph sub)
        {
            var main = GraphDatabase.CreateGraph<PlacementGraphDefinition>(MainPath);
            var sampler = Add<Editor.VolumeSamplerNode>(main);
            Assert.IsTrue(sampler.GetInputPortByName("Spacing").TrySetValue(5f));
            ISubgraphNode subgraphNode = main.AddSubgraphNode(sub, Vector2.zero);
            var spawn = Add<Editor.SpawnPrefabsNode>(main);
            Assert.IsTrue(spawn.GetInputPortByName("Palette").TrySetValue(Palette()));
            Assert.IsTrue(main.Connect(sampler.GetOutputPortByName("Out"), PlacementGraphCompiler.SubgraphPort(subgraphNode.GetInputPorts(), "Points")));
            Assert.IsTrue(main.Connect(PlacementGraphCompiler.SubgraphPort(subgraphNode.GetOutputPorts(), "Result"), spawn.GetInputPortByName("In")));
            return (main, subgraphNode);
        }

        private PlacementGraphAsset Compile(Graph graph, List<string> dependencies = null)
        {
            var asset = Track(ScriptableObject.CreateInstance<PlacementGraphAsset>());
            var errors = new List<string>();
            Assert.IsTrue(PlacementGraphCompiler.TryCompile(graph, asset, errors, null, dependencies), string.Join("\n", errors));
            return asset;
        }

        private static List<Vector3> Run(PlacementGraphAsset asset, params PlacementParameter[] overrides)
        {
            var output = new RecordingOutput();
            var context = new PlacementContext { LocalBounds = new Bounds(Vector3.zero, new Vector3(10f, 4f, 10f)), Output = output };
            context.ParameterOverrides.AddRange(overrides);
            PlacementExecutor.Run(asset, context);
            return output.Scales;
        }

        [Test]
        public void Subgraph_IsInlined_WithItsVariableDefault()
        {
            (Graph main, _) = CreateMain(CreateScaleSubgraph());
            var dependencies = new List<string>();
            PlacementGraphAsset asset = Compile(main, dependencies);

            Assert.AreEqual(3, asset.Nodes.Count);
            Assert.IsInstanceOf<Placement.VolumeSamplerNode>(asset.Nodes[0]);
            Assert.IsInstanceOf<Placement.TransformPointsNode>(asset.Nodes[1]);
            Assert.IsInstanceOf<Placement.SpawnPrefabsNode>(asset.Nodes[2]);
            StringAssert.Contains("/ Transform Points", asset.Nodes[1].Name);
            CollectionAssert.AreEquivalent(new[] { new PlacementEdge(0, 0, 1, 0), new PlacementEdge(1, 0, 2, 0) }, asset.Edges);
            CollectionAssert.AreEqual(new[] { SubPath }, dependencies);
            Assert.AreEqual(0, asset.Parameters.Count, "A subgraph's Input variables aren't parameters of the main graph.");

            List<Vector3> scales = Run(asset);
            Assert.AreEqual(4, scales.Count);
            Assert.IsTrue(scales.All(s => s == Vector3.one * 3f));
        }

        [Test]
        public void SubgraphNodePort_SetsTheSubgraphInput()
        {
            (Graph main, ISubgraphNode subgraphNode) = CreateMain(CreateScaleSubgraph());
            Assert.IsTrue(PlacementGraphCompiler.SubgraphPort(subgraphNode.GetInputPorts(), "Scale").TrySetValue(2f));
            Assert.IsTrue(Run(Compile(main)).All(s => s == Vector3.one * 2f));
        }

        [Test]
        public void MainGraphParameter_WiredIntoSubgraph_CanBeOverridden()
        {
            (Graph main, ISubgraphNode subgraphNode) = CreateMain(CreateScaleSubgraph());
            IVariable size = main.CreateVariable("Size", 1.5f, VariableKind.Input);
            IVariableNode sizeNode = main.AddVariableNode(size, Vector2.zero);
            Assert.IsTrue(main.Connect(sizeNode.GetOutputPort(0), PlacementGraphCompiler.SubgraphPort(subgraphNode.GetInputPorts(), "Scale")));

            PlacementGraphAsset asset = Compile(main);
            Assert.AreEqual(1, asset.Parameters.Count);
            Assert.AreEqual(2, asset.Bindings.Count, "Scale Min and Scale Max inside the subgraph.");
            Assert.IsTrue(Run(asset).All(s => s == Vector3.one * 1.5f));
            Assert.IsTrue(Run(asset, new PlacementParameter("Size", PlacementParameterType.Float, 4f)).All(s => s == Vector3.one * 4f));
        }

        [Test]
        public void TwoUsesOfOneSubgraph_GetSeparateNodesAndSeeds()
        {
            Graph sub = CreateScaleSubgraph();
            var main = GraphDatabase.CreateGraph<PlacementGraphDefinition>(MainPath);
            main.AddSubgraphNode(sub, Vector2.zero);
            main.AddSubgraphNode(sub, Vector2.one * 100f);
            PlacementGraphAsset asset = Compile(main);
            Assert.AreEqual(2, asset.Nodes.Count);
            Assert.AreNotEqual(asset.Nodes[0].NodeSeed, asset.Nodes[1].NodeSeed);
        }

        [Test]
        public void SubgraphThatContainsItself_FailsToCompile()
        {
            Graph sub = CreateScaleSubgraph();
            ISubgraphNode self = null;
            try { self = sub.AddSubgraphNode(sub, Vector2.zero); }
            catch (System.Exception) { /* Graph Toolkit may refuse this itself, which is fine too. */ }
            if (self == null) Assert.Pass("Graph Toolkit doesn't allow a graph to contain itself.");

            var asset = Track(ScriptableObject.CreateInstance<PlacementGraphAsset>());
            var errors = new List<string>();
            Assert.IsFalse(PlacementGraphCompiler.TryCompile(sub, asset, errors, null));
            StringAssert.Contains("contains itself", errors[0]);
        }

        [Test]
        public void Importer_CompilesSubgraphs()
        {
            (Graph main, _) = CreateMain(CreateScaleSubgraph());
            GraphDatabase.SaveGraph(main);
            AssetDatabase.ImportAsset(MainPath, ImportAssetOptions.ForceUpdate);
            var asset = AssetDatabase.LoadAssetAtPath<PlacementGraphAsset>(MainPath);
            Assert.IsNotNull(asset);
            Assert.AreEqual(3, asset.Nodes.Count);
            Assert.AreEqual(2, asset.Edges.Count);
            Assert.IsTrue(Run(asset).All(s => s == Vector3.one * 3f));
        }
    }
}
