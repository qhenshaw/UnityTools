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
    /// <summary>Graph parameters: Input variables compiled into the asset, and overrides on the component.</summary>
    public class PlacementParameterTests
    {
        private const string GraphPath = "Assets/__PlacementParameterTests.placegraph";
        private const string PrefabPath = "Assets/__PlacementParameterTests_Prop.prefab";
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
            AssetDatabase.DeleteAsset(GraphPath);
            AssetDatabase.DeleteAsset(PrefabPath);
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

        private GameObject Prop() => Track(new GameObject("Prop"));

        /// <summary>A Prefab Entry for <paramref name="prefab"/> wired into <paramref name="spawn"/>'s Entries.</summary>
        private static Editor.PrefabEntryNode AddEntry(Graph graph, PlacementGraphNode spawn, GameObject prefab)
        {
            var entry = Add<Editor.PrefabEntryNode>(graph);
            if (prefab != null) Assert.IsTrue(entry.GetInputPortByName("Prefab").TrySetValue(prefab));
            Assert.IsTrue(graph.Connect(entry.GetOutputPortByName("Entry"), spawn.GetInputPortByName("Entries")));
            return entry;
        }

        /// <summary>Volume Sampler > Spawn Prefabs, with the sampler's Spacing wired to a variable of <paramref name="kind"/>.</summary>
        private (Graph graph, PlacementGraphAsset asset) SpacingGraph(VariableKind kind, float spacing, GameObject prefab)
        {
            var graph = GraphDatabase.CreateGraph<PlacementGraphDefinition>(GraphPath);
            var sampler = Add<Editor.VolumeSamplerNode>(graph);
            var spawn = Add<Editor.SpawnPrefabsNode>(graph);
            Assert.IsTrue(graph.Connect(sampler.GetOutputPortByName("Out"), spawn.GetInputPortByName("In")));
            AddEntry(graph, spawn, prefab);

            IVariable variable = graph.CreateVariable("Spacing", spacing, kind);
            IVariableNode variableNode = graph.AddVariableNode(variable, Vector2.zero);
            Assert.IsTrue(graph.Connect(variableNode.GetOutputPort(0), sampler.GetInputPortByName("Spacing")));

            var asset = Track(ScriptableObject.CreateInstance<PlacementGraphAsset>());
            var errors = new List<string>();
            Assert.IsTrue(PlacementGraphCompiler.TryCompile(graph, asset, errors, null), string.Join("\n", errors));
            return (graph, asset);
        }

        [Test]
        public void InputVariable_CompilesToParameterAndBinding()
        {
            GameObject prefab = Prop();
            (_, PlacementGraphAsset asset) = SpacingGraph(VariableKind.Input, 2f, prefab);

            Assert.AreEqual(1, asset.Parameters.Count);
            Assert.AreEqual("Spacing", asset.Parameters[0].Name);
            Assert.AreEqual(PlacementParameterType.Float, asset.Parameters[0].Type);
            Assert.AreEqual(2f, asset.Parameters[0].FloatValue);

            Assert.AreEqual(1, asset.Bindings.Count);
            PlacementParameterBinding binding = asset.Bindings[0];
            Assert.IsInstanceOf<Placement.VolumeSamplerNode>(asset.Nodes[binding.Node]);
            Assert.AreEqual("Spacing", binding.Field);
            Assert.AreEqual(2f, ((Placement.VolumeSamplerNode)asset.Nodes[binding.Node]).Spacing, "The node holds the default.");
            Assert.AreEqual(prefab, ((Placement.SpawnPrefabsNode)asset.Nodes[1]).Entries.Single().Prefab, "Prefab Entry values compile.");
        }

        [Test]
        public void LocalVariable_SetsValueButIsNotAParameter()
        {
            // Graph Toolkit makes new Blackboard variables Local, so those are parameters too.
            (_, PlacementGraphAsset asset) = SpacingGraph(VariableKind.Local, 4f, Prop());
            Assert.AreEqual("Spacing", asset.Parameters.Single().Name);
            Assert.AreEqual(4f, asset.Parameters[0].FloatValue);
            Assert.AreEqual(1, asset.Bindings.Count);
            Assert.AreEqual(4f, ((Placement.VolumeSamplerNode)asset.Nodes[0]).Spacing);
        }

        [Test]
        public void NewVariables_DefaultToInput_ExistingOnesAreKept()
        {
            var graph = GraphDatabase.CreateGraph<PlacementGraphDefinition>(GraphPath);
            IVariable old = graph.CreateVariable("Old", 1f, VariableKind.Local);
            graph.MakeNewVariablesInputs();
            Assert.AreEqual(VariableKind.Input, old.VariableKind, "New variables become Input.");
            old.VariableKind = VariableKind.Local;
            GraphDatabase.SaveGraph(graph);

            // A graph loaded from disk keeps the kinds it had.
            var loaded = GraphDatabase.LoadGraph<PlacementGraphDefinition>(GraphPath);
            loaded.MakeNewVariablesInputs();
            Assert.AreEqual(VariableKind.Local, loaded.GetVariables().Single(v => v.Name == "Old").VariableKind);

            // A variable added afterwards (as the Blackboard's + button does) becomes Input on the next change.
            loaded.CreateVariable("New", 2f, VariableKind.Local);
            loaded.MakeNewVariablesInputs();
            IVariable added = loaded.GetVariables().Single(v => v.Name == "New");
            Assert.AreEqual(VariableKind.Input, added.VariableKind);
            Assert.AreEqual(VariableKind.Local, loaded.GetVariables().Single(v => v.Name == "Old").VariableKind);

            // Switching it back to Local sticks.
            added.VariableKind = VariableKind.Local;
            loaded.MakeNewVariablesInputs();
            Assert.AreEqual(VariableKind.Local, loaded.GetVariables().Single(v => v.Name == "New").VariableKind);
        }

        [Test]
        public void SavedGraphWithLocalVariables_ShowsParametersOnTheComponent()
        {
            // The usual way a graph is made in the window: variables are left Local, the graph is saved and imported.
            // The prefab is saved as an asset, since a saved graph can only reference assets.
            GameObject source = new GameObject("Prop");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, PrefabPath);
            Object.DestroyImmediate(source);
            var graph = GraphDatabase.CreateGraph<PlacementGraphDefinition>(GraphPath);
            var sampler = Add<Editor.SurfaceSamplerNode>(graph);
            var spawn = Add<Editor.SpawnPrefabsNode>(graph);
            Assert.IsTrue(graph.Connect(sampler.GetOutputPortByName("Out"), spawn.GetInputPortByName("In")));
            IVariable spacing = graph.CreateVariable("Spacing", 3f, VariableKind.Local);
            Assert.IsTrue(graph.Connect(graph.AddVariableNode(spacing, Vector2.zero).GetOutputPort(0), sampler.GetInputPortByName("Spacing")));
            var entry = AddEntry(graph, spawn, null);
            IVariable prefabVariable = graph.CreateVariable("Prop", prefab, VariableKind.Local);
            Assert.IsTrue(graph.Connect(graph.AddVariableNode(prefabVariable, Vector2.zero).GetOutputPort(0), entry.GetInputPortByName("Prefab")));
            GraphDatabase.SaveGraph(graph);
            AssetDatabase.ImportAsset(GraphPath, ImportAssetOptions.ForceUpdate);

            var asset = AssetDatabase.LoadAssetAtPath<PlacementGraphAsset>(GraphPath);
            CollectionAssert.AreEqual(new[] { "Spacing", "Prop" }, asset.Parameters.Select(p => p.Name).ToArray());
            var component = Track(new GameObject("Placement Graph")).AddComponent<PlacementGraph>();
            component.Graph = asset;
            component.SyncParameters();
            CollectionAssert.AreEqual(new[] { "Spacing", "Prop" }, component.ParameterOverrides.Select(p => p.Value.Name).ToArray());
            Assert.AreEqual(PlacementParameterType.Prefab, component.ParameterOverrides[1].Value.Type);
            Assert.AreEqual(prefab, component.ParameterOverrides[1].Value.PrefabValue, "Shows the graph's default.");
            Assert.AreEqual("Entries[0].Prefab", asset.Bindings.Single(b => b.Parameter == "Prop").Field, "Binds into the entry.");
        }

        [Test]
        public void UnconnectedSetting_UsesItsInlineValue()
        {
            var graph = GraphDatabase.CreateGraph<PlacementGraphDefinition>(GraphPath);
            var sampler = Add<Editor.VolumeSamplerNode>(graph);
            Assert.IsTrue(sampler.GetInputPortByName("Jitter").TrySetValue(0.25f));
            var runtime = (Placement.VolumeSamplerNode)sampler.CreateRuntimeNode();
            Assert.AreEqual(0.25f, runtime.Jitter);
            Assert.AreEqual(new Placement.VolumeSamplerNode().Spacing, runtime.Spacing);
        }

        [Test]
        public void Component_OverrideChangesResult_DefaultOtherwise()
        {
            (_, PlacementGraphAsset asset) = SpacingGraph(VariableKind.Input, 2f, Prop());
            var go = Track(new GameObject("Placement Graph"));
            var component = go.AddComponent<PlacementGraph>();
            component.Size = new Vector3(10f, 4f, 10f);
            component.Graph = asset;
            component.SyncParameters();
            Assert.AreEqual(1, component.ParameterOverrides.Count);
            Assert.IsFalse(component.ParameterOverrides[0].Override);

            component.Generate();
            Assert.AreEqual(25, component.InstanceCount, "Default spacing 2 over 10 m: 5 x 5.");

            component.SetParameter("Spacing", 5f);
            component.Generate();
            Assert.AreEqual(4, component.InstanceCount, "Overridden spacing 5: 2 x 2.");
            Assert.AreEqual(2f, ((Placement.VolumeSamplerNode)asset.Nodes[asset.Bindings[0].Node]).Spacing,
                "Running with an override doesn't change the asset.");

            component.ClearParameter("Spacing");
            component.Generate();
            Assert.AreEqual(25, component.InstanceCount);

            Assert.Throws<System.ArgumentException>(() => component.SetParameter("Missing", 1f));
        }

        [Test]
        public void Executor_IgnoresOverridesOfTheWrongType()
        {
            (_, PlacementGraphAsset asset) = SpacingGraph(VariableKind.Input, 2f, Prop());
            var context = new PlacementContext { LocalBounds = new Bounds(Vector3.zero, new Vector3(10f, 4f, 10f)) };
            context.ParameterOverrides.Add(new PlacementParameter("Spacing", PlacementParameterType.Int, 5));
            PointData[][] outputs = PlacementExecutor.Run(asset, context);
            Assert.AreEqual(25, outputs[0][0].Count);
        }

        [Test]
        public void SyncParameters_KeepsValuesByNameAndType()
        {
            var asset = Track(ScriptableObject.CreateInstance<PlacementGraphAsset>());
            asset.SetContents(new List<PlacementNode>(), new List<PlacementEdge>(), new List<PlacementParameter>
            {
                new("A", PlacementParameterType.Float, 1f),
                new("B", PlacementParameterType.Int, 2)
            });
            var component = Track(new GameObject("Graph")).AddComponent<PlacementGraph>();
            component.Graph = asset;
            component.SetParameter("B", 7);

            asset.SetContents(new List<PlacementNode>(), new List<PlacementEdge>(), new List<PlacementParameter>
            {
                new("B", PlacementParameterType.Int, 2),
                new("C", PlacementParameterType.Bool, true)
            });
            Assert.IsTrue(component.SyncParameters());
            Assert.AreEqual(new[] { "B", "C" }, component.ParameterOverrides.Select(p => p.Value.Name).ToArray());
            Assert.IsTrue(component.ParameterOverrides[0].Override);
            Assert.AreEqual(7, component.ParameterOverrides[0].Value.IntValue);
            Assert.IsFalse(component.ParameterOverrides[1].Override);
            Assert.IsTrue(component.ParameterOverrides[1].Value.BoolValue);
            Assert.IsFalse(component.SyncParameters());
        }
    }
}
