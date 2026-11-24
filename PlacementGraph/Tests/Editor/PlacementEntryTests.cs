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
    /// <summary>Prefab Entry nodes, parameters bound into entries, the authoring helpers and the palette converter.</summary>
    public class PlacementEntryTests
    {
        private const string Folder = "Assets/__PlacementEntryTests";
        private const string GraphPath = Folder + "/Graph.placegraph";

        private sealed class RecordingOutput : IPlacementOutput
        {
            public readonly List<(GameObject prefab, Vector3 position, Vector3 scale)> Spawned = new();
            public void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale) => Spawned.Add((prefab, position, scale));
        }

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__PlacementEntryTests");
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Folder);

        private static GameObject SavedPrefab(string name)
        {
            var source = new GameObject(name);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, $"{Folder}/{name}.prefab");
            Object.DestroyImmediate(source);
            return prefab;
        }

        private static List<(GameObject prefab, Vector3 position, Vector3 scale)> Run(PlacementGraphAsset asset, params PlacementParameter[] overrides)
        {
            var output = new RecordingOutput();
            var context = new PlacementContext { LocalBounds = new Bounds(Vector3.zero, new Vector3(10f, 2f, 10f)), Output = output };
            context.ParameterOverrides.AddRange(overrides);
            PlacementExecutor.Run(asset, context);
            return output.Spawned;
        }

        [Test]
        public void EntryNodes_CompileIntoTheEntryList_WithAttributesAndParameters()
        {
            GameObject rock = SavedPrefab("Rock");
            GameObject tree = SavedPrefab("Tree");
            PlacementGraphHandle graph = PlacementGraphAuthoring.Create(GraphPath);
            PlacementNodeHandle sampler = graph.AddNode("Volume Sampler").Set("Spacing", 2f);
            PlacementNodeHandle select = graph.AddNode("Select Prefab");
            PlacementNodeHandle spawn = graph.AddNode("Spawn Prefabs");
            graph.Connect(sampler, "Out", select, "In");
            graph.Connect(select, "Out", spawn, "In");
            PlacementNodeHandle rockEntry = graph.AddEntry(select, rock, 1f).Set("Position Offset", new Vector3(0f, 1f, 0f));
            PlacementNodeHandle treeEntry = graph.AddEntry(select, tree, 0f).Set("Random Scale", true).Set("Scale Min", 2f).Set("Scale Max", 2f);
            graph.AddEntryAttribute(rockEntry, "Hardness", 5f);
            graph.AddParameter("Tree Weight", 0f);
            graph.BindParameter("Tree Weight", treeEntry, "Weight");
            CollectionAssert.AreEqual(new[] { rockEntry.Node, treeEntry.Node }, graph.EntriesOf(select).Select(e => e.Node));
            CollectionAssert.IsEmpty(graph.Validate());

            PlacementGraphAsset asset = graph.Save();
            Assert.AreEqual(3, asset.Nodes.Count, "Prefab Entry and Entry Attribute nodes don't run.");
            List<PrefabEntry> entries = ((SelectPrefabNode)asset.Nodes[1]).Entries;
            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual(rock, entries[0].Prefab);
            Assert.AreEqual(new Vector3(0f, 1f, 0f), entries[0].PositionOffset);
            Assert.AreEqual("Hardness", entries[0].Attributes.Single().Name);
            Assert.AreEqual(5f, entries[0].Attributes.Single().FloatValue);
            Assert.IsTrue(entries[1].RandomScale);
            Assert.AreEqual("Entries[1].Weight", asset.Bindings.Single().Field);

            // Tree's weight is 0 by default: only rocks, raised by their offset.
            List<(GameObject prefab, Vector3 position, Vector3 scale)> spawned = Run(asset);
            Assert.AreEqual(25, spawned.Count);
            Assert.IsTrue(spawned.All(s => s.prefab == rock && Mathf.Approximately(s.position.y, 2f)), "Volume top at 1, plus the offset.");

            // Overriding the tree's weight brings trees in, scaled by their entry; the asset keeps its default.
            List<(GameObject prefab, Vector3 position, Vector3 scale)> mixed = Run(asset, new PlacementParameter("Tree Weight", PlacementParameterType.Float, 1f));
            Assert.Greater(mixed.Count(s => s.prefab == tree), 0);
            Assert.IsTrue(mixed.Where(s => s.prefab == tree).All(s => s.scale == Vector3.one * 2f));
            Assert.AreEqual(0f, ((SelectPrefabNode)asset.Nodes[1]).Entries[1].Weight);
        }

        [Test]
        public void SetEntries_ReplacesTheEntryNodes()
        {
            GameObject rock = SavedPrefab("Rock");
            PlacementGraphHandle graph = PlacementGraphAuthoring.Create(GraphPath);
            PlacementNodeHandle spawn = graph.AddNode("Spawn Prefabs");
            graph.AddEntry(spawn, rock);
            graph.AddEntry(spawn, rock);
            var entry = new PrefabEntry { Prefab = rock, Weight = 3f, RotationOffset = new Vector3(0f, 45f, 0f) };
            entry.Attributes.Add(new EntryAttribute { Name = "Kind", Type = PointAttributeType.Int, IntValue = 2 });
            graph.SetEntries(spawn, new[] { entry });

            Assert.AreEqual(1, graph.EntriesOf(spawn).Count);
            Assert.AreEqual(1, graph.Nodes.Count(n => n.Node is PrefabEntryNode));
            PlacementGraphAsset compiled = graph.Compile();
            PrefabEntry built = ((SpawnPrefabsNode)compiled.Nodes.Single()).Entries.Single();
            Assert.AreEqual(3f, built.Weight);
            Assert.AreEqual(new Vector3(0f, 45f, 0f), built.RotationOffset);
            Assert.AreEqual(2, built.Attributes.Single().IntValue);
            Object.DestroyImmediate(compiled);
        }

        [Test]
        public void Converter_CopiesPalettesIntoEntryNodes()
        {
            GameObject rock = SavedPrefab("Rock");
            GameObject tree = SavedPrefab("Tree");
            var palette = ScriptableObject.CreateInstance<PrefabPalette>();
            palette.Entries.Add(new PrefabPalette.Entry { Prefab = rock, Weight = 2f, RandomScale = true, ScaleMin = 0.5f, ScaleMax = 0.6f });
            palette.Entries.Add(new PrefabPalette.Entry { Prefab = tree, Weight = 1f, PositionOffset = Vector3.up });
            palette.Entries[1].Attributes.Add(new EntryAttribute { Name = "Tall", Type = PointAttributeType.Float, FloatValue = 1f });
            AssetDatabase.CreateAsset(palette, Folder + "/Palette.asset");

            // A Spawn Prefabs with the palette set directly, and a Select Prefab reading it through a variable.
            PlacementGraphHandle graph = PlacementGraphAuthoring.Create(GraphPath);
            PlacementNodeHandle spawn = graph.AddNode("Spawn Prefabs").Set("Palette", palette);
            PlacementNodeHandle select = graph.AddNode("Select Prefab");
            IVariable variable = graph.Graph.CreateVariable("Palette", typeof(PrefabPalette), palette, VariableKind.Input);
            Assert.IsTrue(graph.Graph.Connect(graph.Graph.AddVariableNode(variable, Vector2.zero).GetOutputPort(0), select.Node.GetInputPortByName("Palette")));
            graph.Connect(select, "Out", spawn, "In");
            graph.Save();
            List<PrefabEntry> before = ((SpawnPrefabsNode)graph.Compiled.Nodes[1]).Entries;
            Assert.AreEqual(0, before.Count, "Before converting, the palette is still used through the obsolete port.");

            List<string> report = PlacementPaletteConverter.ConvertGraph(GraphPath);
            Assert.AreEqual(4, report.Count, string.Join("\n", report));
            Assert.IsEmpty(PlacementPaletteConverter.ConvertGraph(GraphPath), "Converting twice changes nothing.");

            PlacementGraphHandle converted = PlacementGraphAuthoring.Open(GraphPath);
            Assert.IsFalse(converted.Graph.GetVariables().Any(), "The palette variable is removed.");
            PlacementGraphAsset asset = converted.Compiled;
            Assert.AreEqual(0, asset.Parameters.Count);
            foreach (PlacementNode node in asset.Nodes)
            {
                List<PrefabEntry> entries = node is SelectPrefabNode s ? s.Entries : ((SpawnPrefabsNode)node).Entries;
                Assert.IsNull(node is SelectPrefabNode sp ? sp.Palette : ((SpawnPrefabsNode)node).Palette);
                Assert.AreEqual(new[] { rock, tree }, entries.Select(e => e.Prefab).ToArray());
                Assert.AreEqual(2f, entries[0].Weight);
                Assert.IsTrue(entries[0].RandomScale);
                Assert.AreEqual(0.6f, entries[0].ScaleMax, 1e-6f);
                Assert.AreEqual(Vector3.up, entries[1].PositionOffset);
                Assert.AreEqual("Tall", entries[1].Attributes.Single().Name);
            }
        }
    }
}
