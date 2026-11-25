using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityTools.Placement.Editor;

namespace UnityTools.Placement.Tests
{
    /// <summary>
    /// Deleting a .placegraph frees Graph Toolkit's in-memory copy of it. Left loaded, a copy whose subgraph node points
    /// to a deleted subgraph throws in SubgraphNodeModelImp.OnDefineNode on every domain reload.
    /// </summary>
    [SceneIsolation]
    public class PlacementGraphCacheTests
    {
        private const string Folder = "Assets/__PlacementGraphCacheTests";
        private const string SubPath = Folder + "/Sub.placegraph";
        private const string MainPath = Folder + "/Main.placegraph";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__PlacementGraphCacheTests");
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Folder);

        private static bool IsLoaded(string path) => PlacementGraphCache.Loaded().Any(g => PlacementGraphCache.PathOf(g) == path);

        [Test]
        public void DeletingAGraph_FreesItsInMemoryCopy_AndKeepsTheOthers()
        {
            Assert.IsTrue(PlacementGraphCache.Available, "Graph Toolkit's GraphObject wasn't found; the cleanup would do nothing.");
            PlacementGraphHandle sub = PlacementGraphAuthoring.Create(SubPath);
            sub.AddPointOutput("Out");
            sub.Save();
            PlacementGraphHandle main = PlacementGraphAuthoring.Create(MainPath);
            main.AddSubgraph(SubPath);
            main.Save();
            Assert.IsTrue(IsLoaded(SubPath) && IsLoaded(MainPath));

            // The graph using the subgraph goes first: deleting a subgraph that a remaining graph still uses makes
            // Graph Toolkit throw when it reimports that graph, which only Unity can fix.
            AssetDatabase.DeleteAsset(MainPath);
            Assert.IsFalse(IsLoaded(MainPath), "The deleted graph's copy is freed.");
            Assert.IsTrue(IsLoaded(SubPath), "A graph whose file is still there is never touched.");

            AssetDatabase.DeleteAsset(Folder);
            Assert.IsFalse(IsLoaded(SubPath), "Deleting a folder frees the graphs in it.");

            // A graph made again at the same path loads and edits normally.
            AssetDatabase.CreateFolder("Assets", "__PlacementGraphCacheTests");
            PlacementGraphHandle again = PlacementGraphAuthoring.Create(MainPath);
            again.AddNode("Create Grid");
            again.Save();
            Assert.AreEqual(1, PlacementGraphAuthoring.Open(MainPath).Nodes.Count);
        }
    }
}
