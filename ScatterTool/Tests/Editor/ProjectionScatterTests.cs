using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityTools.Scatter.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UnityTools.Scatter.Tests
{
    public class ProjectionScatterTests
    {
        private const string Folder = "Assets/UnityToolsProjectionScatterTests";

        private readonly List<Object> _created = new();
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "UnityToolsProjectionScatterTests");

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(50f, 1f, 50f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _created.Add(ground);

            var rock = new GameObject("Rock");
            _prefab = PrefabUtility.SaveAsPrefabAsset(rock, $"{Folder}/Rock.prefab");
            Object.DestroyImmediate(rock);
            Physics.SyncTransforms();
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

        private ProjectionScatter CreateScatter(bool autoBake = true)
        {
            ProjectionScatter scatter = ProjectionScatter.Create();
            _created.Add(scatter.gameObject);
            scatter.transform.position = new Vector3(0f, 2f, 0f);
            var item = new ProjectionScatter.PrefabItem
            {
                Prefab = _prefab,
                Weight = 1f,
                NormalFilter = new ProjectionScatter.NormalFilter { Mode = ProjectionScatter.NormalFilter.NormalFilterMode.None },
                PositionOffset = new ProjectionScatter.PositionOffset(),
                RotationRange = new ProjectionScatter.RotationRange(),
                ScaleRange = new ProjectionScatter.ValueRange { Min = 1f, Max = 1f, Multiplier = 1f, WeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f) },
            };
            scatter.ConfigureForTests(new[] { item }, LayerMask.GetMask("Default"), autoBake);
            return scatter;
        }

        private static int ChildCount(ProjectionScatter scatter) => scatter.transform.childCount;

        [Test]
        public void Create_PointsDownWithTheDefaultMask()
        {
            ProjectionScatter scatter = ProjectionScatter.Create();
            _created.Add(scatter.gameObject);

            Assert.That(Vector3.Angle(Vector3.down, scatter.transform.forward), Is.LessThan(0.01f));
            Assert.AreEqual(LayerMask.GetMask("Default"), scatter.ProjectionMask.value);
            Assert.AreSame(scatter.gameObject, Selection.activeGameObject);
        }

        [Test]
        public void FloodedMask_SpawnsChildren()
        {
            ProjectionScatter scatter = CreateScatter();
            scatter.Flood(1f);
            scatter.RegenerateNow();

            Assert.Greater(ChildCount(scatter), 0);
        }

        [Test]
        public void Flood_IsOneUndoStep()
        {
            ProjectionScatter scatter = CreateScatter();
            Undo.IncrementCurrentGroup();

            scatter.Flood(1f);
            Assert.IsTrue(scatter.Weights.All(w => w == 1f));

            Undo.PerformUndo();
            Assert.IsTrue(scatter.Weights.All(w => w == 0f));
        }

        [Test]
        public void PaintStroke_IsOneUndoStep()
        {
            ProjectionScatter scatter = CreateScatter();
            var tool = ScriptableObject.CreateInstance<ProjectionScatterTool>();
            _created.Add(tool);
            Undo.IncrementCurrentGroup();

            tool.BeginStroke(scatter);
            for (int tick = 1; tick <= 3; tick++)
            {
                ProjectionScatterTool.PaintTick(scatter, new Dictionary<int, float> { { tick, 1f } });
                Undo.IncrementCurrentGroup(); // each GUI event starts a new group
            }
            tool.EndStroke();
            Assert.AreEqual(1f, scatter.Weights[3]);

            Undo.PerformUndo();
            Assert.IsTrue(scatter.Weights.All(w => w == 0f), "one undo removes the whole stroke");
        }

        [Test]
        public void EndStroke_StopsPainting()
        {
            var tool = ScriptableObject.CreateInstance<ProjectionScatterTool>();
            _created.Add(tool);
            tool.BeginStroke(CreateScatter());
            Assert.IsTrue(tool.IsPainting);

            tool.EndStroke();
            Assert.IsFalse(tool.IsPainting);
            tool.EndStroke(); // a second release is harmless
        }

        [Test]
        public void ClearButton_IsOneUndoStep()
        {
            ProjectionScatter scatter = CreateScatter();
            scatter.Flood(1f);
            scatter.RegenerateNow();
            int spawned = ChildCount(scatter);
            Assert.Greater(spawned, 0);
            Undo.IncrementCurrentGroup();

            scatter.Clear();
            Assert.AreEqual(0, ChildCount(scatter));

            Undo.PerformUndo();
            Assert.AreEqual(spawned, ChildCount(scatter));
        }

        [Test]
        public void BakeButton_IsOneUndoStep()
        {
            ProjectionScatter scatter = CreateScatter(autoBake: false);
            scatter.Flood(1f);
            Undo.IncrementCurrentGroup();

            scatter.Bake();
            Assert.Greater(ChildCount(scatter), 0);

            Undo.PerformUndo();
            Assert.AreEqual(0, ChildCount(scatter));
        }

        [Test]
        public void InPrefabInstance_PausesWithOneWarning()
        {
            ProjectionScatter source = CreateScatter();
            source.Flood(1f);
            source.RegenerateNow();
            int spawned = ChildCount(source);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source.gameObject, $"{Folder}/Scatter.prefab");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            _created.Add(instance);
            ProjectionScatter scatter = instance.GetComponent<ProjectionScatter>();
            Assert.IsTrue(scatter.IsInPrefabInstance);

            LogAssert.Expect(LogType.Warning, new Regex("inside a prefab instance"));
            scatter.RegenerateNow();
            scatter.Clear();
            scatter.Bake();
            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(spawned, ChildCount(scatter), "the prefab's children are left alone");
        }
    }
}
