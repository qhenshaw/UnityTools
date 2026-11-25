using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityTools.Placement.Editor;
using Object = UnityEngine.Object;

namespace UnityTools.Placement.Tests
{
    /// <summary>The Placement Graph inspector: New with each template, and the rules for which fields show.</summary>
    [SceneIsolation]
    public class PlacementInspectorTests
    {
        private const string Folder = "Assets/__PlacementInspectorTests";
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__PlacementInspectorTests");
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

        private PlacementGraph Component()
        {
            var go = new GameObject("Placement Graph");
            _created.Add(go);
            return go.AddComponent<PlacementGraph>();
        }

        private static IEnumerable<PlacementGraphTemplate> Templates() =>
            System.Enum.GetValues(typeof(PlacementGraphTemplate)).Cast<PlacementGraphTemplate>().Where(PlacementGraphTemplates.IsAvailable);

        [Test]
        public void New_WithEachTemplate_CreatesAssignsAndCompiles()
        {
            foreach (PlacementGraphTemplate template in Templates())
            {
                PlacementGraph component = Component();
                string path = PlacementGraphTemplates.SuggestPath(Folder, component.name + " " + template);
                PlacementGraphAsset asset = PlacementGraphEditor.CreateAndAssign(component, template, path, open: false);

                Assert.IsNotNull(asset, $"{template}: a compiled graph at {path}");
                Assert.AreSame(asset, component.Graph, $"{template}: assigned to the component");
                Assert.AreEqual(path, AssetDatabase.GetAssetPath(asset));
                var types = asset.Nodes.Select(n => n.GetType()).ToList();
                switch (template)
                {
                    case PlacementGraphTemplate.Empty:
                        Assert.AreEqual(0, asset.Nodes.Count);
                        break;
                    case PlacementGraphTemplate.ScatterOnSurface:
                        CollectionAssert.AreEquivalent(new[] { typeof(VolumeSamplerNode), typeof(SurfaceSamplerNode), typeof(SpawnPrefabsNode) }, types);
                        Assert.AreEqual(2, asset.Edges.Count);
                        break;
                    case PlacementGraphTemplate.ModularWall:
                        CollectionAssert.IsSubsetOf(new[] { typeof(SubdivideNode), typeof(CornerNode), typeof(MergeNode), typeof(SpawnPrefabsNode) }, types);
                        Assert.AreEqual(Subdivide.SourceMode.VolumeEdges, asset.Nodes.OfType<SubdivideNode>().Single().Source);
                        Assert.AreEqual(1, asset.Nodes.OfType<SubdivideNode>().Single().Modules.Count, "the Wall module is wired in");
                        break;
#if UNITYTOOLS_SPLINES
                    case PlacementGraphTemplate.FillSplineArea:
                        Assert.AreEqual(SplineSamplerNode.SampleMode.Inside, asset.Nodes.OfType<SplineSamplerNode>().Single().Mode);
                        Assert.IsTrue(PlacementGraphInspectorRules.UsesSpline(asset));
                        break;
#endif
                }
            }
        }

        [Test]
        public void AddNodeMenu_ItemNamesAreUnique()
        {
            IReadOnlyList<PlacementNodeTypeInfo> nodes = PlacementGraphAuthoring.ListNodeTypes();
            Assert.Greater(nodes.Count, 20);

            // Nodes: by category and title.
            var paths = nodes.Select(n => (n.Category + "/" + n.Title).ToLowerInvariant()).ToList();
            CollectionAssert.AllItemsAreUnique(paths, "two node types share a menu path");

            // Constants: Graph Toolkit lists one per setting type, named after the type's short name ("Source Mode
            // Constant"), so two setting types with the same short name make a duplicate item and a console warning.
            var settingTypes = nodes.SelectMany(n => n.Settings.Select(s => s.Type)).Distinct().ToList();
            var clashes = settingTypes.GroupBy(t => ObjectNames.NicifyVariableName(t.Name).ToLowerInvariant())
                .Where(g => g.Count() > 1)
                .Select(g => g.Key + ": " + string.Join(", ", g.Select(t => t.FullName)))
                .ToList();
            Assert.IsEmpty(clashes, "setting types with the same name: " + string.Join("; ", clashes));
        }

        [Test]
        public void Duplicate_CopiesTheGraph()
        {
            string source = PlacementGraphTemplates.SuggestPath(Folder, "Source");
            PlacementGraphTemplates.Create(PlacementGraphTemplate.ScatterOnSurface, source);
            string copy = PlacementGraphTemplates.SuggestPath(Folder, "Source");
            Assert.AreNotEqual(source, copy, "a free path");
            PlacementGraphAsset duplicate = PlacementGraphTemplates.Duplicate(source, copy);
            Assert.AreEqual(3, duplicate.Nodes.Count);
            Assert.AreNotSame(AssetDatabase.LoadAssetAtPath<PlacementGraphAsset>(source), duplicate);
        }

        [Test]
        public void Rules_SceneInputs_ShowWhatTheGraphUses_OrWhatIsAssigned()
        {
            var asset = ScriptableObject.CreateInstance<PlacementGraphAsset>();
            try
            {
                Assert.IsFalse(PlacementGraphInspectorRules.UsesMarkers(asset));
                Assert.IsFalse(PlacementGraphInspectorRules.UsesSpline(asset));
                Assert.IsFalse(PlacementGraphInspectorRules.UsesSpline(null));

                asset.SetContents(new List<PlacementNode> { new ChildMarkersNode(), new SubdivideNode() }, new List<PlacementEdge>());
                Assert.IsTrue(PlacementGraphInspectorRules.UsesMarkers(asset));
                Assert.IsFalse(PlacementGraphInspectorRules.UsesSpline(asset), "Subdivide on its input points doesn't read the spline");

                asset.SetContents(new List<PlacementNode> { new CornerNode { Source = Subdivide.SourceMode.SplineKnots } }, new List<PlacementEdge>());
                Assert.IsTrue(PlacementGraphInspectorRules.UsesSpline(asset), "Spline Knots reads it");
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }

            Assert.IsTrue(PlacementGraphInspectorRules.ShowSceneInput(used: true, assigned: false));
            Assert.IsTrue(PlacementGraphInspectorRules.ShowSceneInput(used: false, assigned: true), "an assigned field is never hidden");
            Assert.IsFalse(PlacementGraphInspectorRules.ShowSceneInput(used: false, assigned: false));
        }

        [Test]
        public void Rules_Runtime_AndCells()
        {
            const PlacementGraph.GenerateMode editor = PlacementGraph.GenerateMode.EditorOnly;
            const PlacementGraph.GenerateMode onLoad = PlacementGraph.GenerateMode.OnLoad;
            const PlacementGraph.GenerateMode near = PlacementGraph.GenerateMode.NearPlayer;

            Assert.IsFalse(PlacementGraphInspectorRules.ShowStreamingFields(editor));
            Assert.IsFalse(PlacementGraphInspectorRules.ShowStreamingFields(onLoad));
            Assert.IsTrue(PlacementGraphInspectorRules.ShowStreamingFields(near));

            Assert.IsFalse(PlacementGraphInspectorRules.ShowRuntimeFields(editor));
            Assert.IsTrue(PlacementGraphInspectorRules.ShowRuntimeFields(onLoad));
            Assert.IsTrue(PlacementGraphInspectorRules.ShowRuntimeFields(near));

            Assert.IsFalse(PlacementGraphInspectorRules.ShowMaxPoolSize(editor, pooling: true), "nothing is pooled in the editor");
            Assert.IsFalse(PlacementGraphInspectorRules.ShowMaxPoolSize(near, pooling: false));
            Assert.IsTrue(PlacementGraphInspectorRules.ShowMaxPoolSize(near, pooling: true));

            Assert.IsFalse(PlacementGraphInspectorRules.ShowCellFields(0f));
            Assert.IsTrue(PlacementGraphInspectorRules.ShowCellFields(16f));
            Assert.AreEqual(20f, PlacementGraphInspectorRules.RestoredCellSize(0f), "default");
            Assert.AreEqual(12f, PlacementGraphInspectorRules.RestoredCellSize(12f), "the last size used");

            Assert.AreEqual("12 objects · 4 cells · last run 3.2 ms", PlacementGraphInspectorRules.Status(12, 4, 3.24, 0, 0));
            Assert.AreEqual("1 object · 500 instanced (3 draw calls) · not run yet", PlacementGraphInspectorRules.Status(1, 0, 0, 500, 3));
            Assert.AreEqual("Seed 3 · 20×10×20 m · one volume", PlacementGraphInspectorRules.PlacementSummary(3, new Vector3(20f, 10f, 20f), 0f));
        }
    }
}
