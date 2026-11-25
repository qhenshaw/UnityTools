using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityTools.Placement.Editor;
using Object = UnityEngine.Object;

namespace UnityTools.Placement.Tests
{
    /// <summary>Subdivide's Openings input: doorways and hallways cut into walls by points and Placement Zones.</summary>
    [SceneIsolation]
    public class PlacementOpeningTests
    {
        // Far from anything in the scene open in the editor, so its zones don't match.
        private static readonly Vector3 Origin = new Vector3(7000f, 0f, 7000f);
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

        private ModuleDefinition Module(string symbol, float size) =>
            new ModuleDefinition { Prefab = Track(new GameObject(symbol)), Symbol = symbol, Size = size };

        private SubdivideNode Walls(Subdivide.OpeningMode mode = Subdivide.OpeningMode.Gap) => new SubdivideNode
        {
            Modules = new List<ModuleDefinition> { Module("Wall", 1f), Module("Door", 2f) },
            Grammar = "[Wall]*",
            OpeningMode = mode,
            OpeningWidth = 2f
        };

        private static PointData Points(params Vector3[] points)
        {
            var data = new PointData();
            for (int i = 0; i < points.Length; i++) data.Points.Add(PlacementPoint.At(Origin + points[i], (uint)(i + 1)));
            return data;
        }

        /// <summary>A straight wall 10 m long along X, from the origin.</summary>
        private static PointData Line() => Points(Vector3.zero, new Vector3(10f, 0f, 0f));

        private static PointData[] Run(PlacementNode node, PlacementContext context, params PointData[] inputs) =>
            PlacementAttributeTests.Run(node, context, inputs);

        private static float[] CentresAlongX(PointData data) => data.Points.Select(p => p.Position.x - Origin.x).OrderBy(x => x).ToArray();

        private static int Count(PointData data, string symbol) =>
            Enumerable.Range(0, data.Count).Count(i => (data.GetAttribute<Object>(PointAttributeNames.Prefab).Get(i) as GameObject)?.name == symbol);

        [Test]
        public void PointOpening_LeavesAGapAtTheCentre_AndOutputsIt()
        {
            PointData[] outputs = Run(Walls(), new PlacementContext(), Line(), Points(new Vector3(5f, 0f, 0.3f)));
            Assert.AreEqual(8, outputs[0].Count, "Two 4 m runs of 1 m walls either side of a 2 m gap.");
            Assert.IsFalse(CentresAlongX(outputs[0]).Any(x => x > 4f && x < 6f), "Nothing in the gap.");
            Assert.AreEqual(1, outputs[1].Count);
            Assert.That(Vector3.Distance(Origin + new Vector3(5f, 0f, 0f), outputs[1].Points[0].Position), Is.LessThan(1e-3f));
            Assert.AreEqual(2f, outputs[1].GetAttribute<float>(SubdivideNode.WidthAttribute).Get(0), 1e-3f);
            Assert.AreEqual(1, outputs[1].GetAttribute<int>(SubdivideNode.OpeningAttribute).Get(0));
            Assert.IsTrue(Enumerable.Range(0, 8).All(i => outputs[0].GetAttribute<int>(SubdivideNode.OpeningAttribute).Get(i) == 0));
        }

        [Test]
        public void ModuleMode_FitsTheDoorModule_CentredOnTheOpening()
        {
            SubdivideNode node = Walls(Subdivide.OpeningMode.Module);
            node.FitToModule = true;
            node.OpeningWidth = 3f;   // ignored: the door's 2 m size wins
            PointData[] outputs = Run(node, new PlacementContext(), Line(), Points(new Vector3(5f, 0f, 0f)));
            Assert.AreEqual(1, Count(outputs[0], "Door"));
            Assert.AreEqual(8, Count(outputs[0], "Wall"));
            int door = Enumerable.Range(0, outputs[0].Count).Single(i => outputs[0].GetAttribute<int>(SubdivideNode.OpeningAttribute).Get(i) == 1);
            Assert.AreEqual("Door", ((GameObject)outputs[0].GetAttribute<Object>(PointAttributeNames.Prefab).Get(door)).name);
            Assert.AreEqual(1, outputs[0].GetAttribute<int>(SubdivideNode.ModuleIndexAttribute).Get(door));
            Assert.That(Vector3.Distance(Origin + new Vector3(5f, 0f, 0f), outputs[0].Points[door].Position), Is.LessThan(1e-3f));
            Assert.AreEqual(1f, outputs[0].Points[door].Scale.x, 1e-4f, "The door keeps its size.");
            Assert.AreEqual(2f, outputs[1].GetAttribute<float>(SubdivideNode.WidthAttribute).Get(0), 1e-3f);

            var missing = new PlacementContext();
            SubdivideNode noDoor = Walls(Subdivide.OpeningMode.Module);
            noDoor.OpeningSymbol = "Gate";
            Run(noDoor, missing, Line(), Points(new Vector3(5f, 0f, 0f)));
            Assert.IsTrue(missing.Warnings.Any(w => w.Contains("Gate")), string.Join("\n", missing.Warnings));
        }

        [Test]
        public void OverlappingOpenings_Merge()
        {
            PointData[] outputs = Run(Walls(), new PlacementContext(), Line(), Points(new Vector3(4.5f, 0f, 0f), new Vector3(5.5f, 0f, 0f)));
            Assert.AreEqual(1, outputs[1].Count, "One opening from 3.5 to 6.5.");
            Assert.AreEqual(3f, outputs[1].GetAttribute<float>(SubdivideNode.WidthAttribute).Get(0), 1e-3f);
            Assert.IsFalse(CentresAlongX(outputs[0]).Any(x => x > 3.5f && x < 6.5f));
        }

        [Test]
        public void OpeningPastACorner_IsClampedToItsWall_WithAWarning()
        {
            SubdivideNode node = Walls();
            node.Closed = true;
            var context = new PlacementContext();
            // A 10 m square; the point is on the first wall, 0.5 m from the corner at (10, 0, 0).
            PointData square = Points(Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(10f, 0f, 10f), new Vector3(0f, 0f, 10f));
            PointData[] outputs = Run(node, context, square, Points(new Vector3(9.5f, 0f, 0f)));
            Assert.AreEqual(1, outputs[1].Count);
            Assert.AreEqual(1.5f, outputs[1].GetAttribute<float>(SubdivideNode.WidthAttribute).Get(0), 1e-3f, "From 8.5 m to the corner.");
            Assert.IsTrue(context.Warnings.Any(w => w.Contains("end of its wall")), string.Join("\n", context.Warnings));
            Assert.AreEqual(10 + 10 + 10 + 9, outputs[0].Count, "The other walls are whole; the first loses 1.5 m (rounded to 8.5 m of walls).");

            // With Corner Trim the opening stops at the trim.
            node.CornerTrim = 1f;
            outputs = Run(node, new PlacementContext(), square, Points(new Vector3(9.5f, 0f, 0f)));
            Assert.AreEqual(0.5f, outputs[1].GetAttribute<float>(SubdivideNode.WidthAttribute).Get(0), 1e-3f, "From 8.5 m to the trim at 9 m.");
        }

        [Test]
        public void PlainPoints_OpenOnlyTheirNearestWall_WithinMaxDistance()
        {
            PointData wallsOnly = Run(Walls(), new PlacementContext(), Line())[0];
            PointData far = Run(Walls(), new PlacementContext(), Line(), Points(new Vector3(5f, 0f, 3f)))[0];
            Assert.AreEqual(0, Run(Walls(), new PlacementContext(), Line(), Points(new Vector3(5f, 0f, 3f)))[1].Count, "3 m away, Max Distance 1.");
            CollectionAssert.AreEqual(wallsOnly.Points.Select(p => p.Position), far.Points.Select(p => p.Position));

            // Near a corner of an L, a point opens only the wall it's nearest.
            PointData corner = Points(Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(10f, 0f, 10f));
            PointData[] outputs = Run(Walls(), new PlacementContext(), corner, Points(new Vector3(9.8f, 0f, 3f)));
            Assert.AreEqual(1, outputs[1].Count);
            Assert.AreEqual(Origin.x + 10f, outputs[1].Points[0].Position.x, 1e-3f, "On the second wall (x = 10).");
        }

        [Test]
        public void ZoneOpenings_FromSceneInput_OpenWhereTheZoneCrosses()
        {
            var zone = Track(new GameObject("Doorway")).AddComponent<PlacementZone>();
            zone.transform.position = Origin + new Vector3(6f, 0f, 0f);
            zone.Label = "Opening";
            zone.Shape = PlacementZone.ZoneShape.Box;
            zone.Size = new Vector3(3f, 3f, 2f);

            var context = new PlacementContext
            {
                LocalToWorld = Matrix4x4.TRS(Origin + new Vector3(5f, 0f, 0f), Quaternion.identity, Vector3.one),
                LocalBounds = new Bounds(Vector3.zero, new Vector3(20f, 10f, 20f)),
                Scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene()
            };
            PointData zones = Run(new SceneInputNode { Source = SceneInputNode.SceneSource.ZonesByLabel, Label = "Opening" }, context)[0];
            Assert.AreEqual(1, zones.Count);

            PointData[] outputs = Run(Walls(), context, Line(), zones);
            Assert.AreEqual(1, outputs[1].Count);
            Assert.AreEqual(3f, outputs[1].GetAttribute<float>(SubdivideNode.WidthAttribute).Get(0), 0.01f, "The 3 m box crosses the wall from 4.5 to 7.5.");
            Assert.AreEqual(Origin.x + 6f, outputs[1].Points[0].Position.x, 0.01f);
            Assert.IsFalse(CentresAlongX(outputs[0]).Any(x => x > 4.5f && x < 7.5f));

            // Fit To Module: the door's 2 m, centred on the zone.
            SubdivideNode fit = Walls(Subdivide.OpeningMode.Module);
            fit.FitToModule = true;
            outputs = Run(fit, context, Line(), zones);
            Assert.AreEqual(2f, outputs[1].GetAttribute<float>(SubdivideNode.WidthAttribute).Get(0), 0.01f);
            Assert.AreEqual(1, Count(outputs[0], "Door"));
        }

        [Test]
        public void WithoutOpenings_OutputIsUnchanged()
        {
            SubdivideNode node = Walls();
            node.Modules.Add(Module("Window", 1f));
            node.Grammar = "[Wall,Window]*";
            PointData line = Points(Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(10f, 0f, 6f));

            PointData before = Run(node, new PlacementContext(), line)[0];
            PointData[] after = Run(node, new PlacementContext(), line, new PointData());
            CollectionAssert.AreEqual(before.Points.Select(p => (p.Position, p.Seed)), after[0].Points.Select(p => (p.Position, p.Seed)));
            CollectionAssert.AreEqual(Enumerable.Range(0, before.Count).Select(before.GetAttribute<Object>(PointAttributeNames.Prefab).Get),
                Enumerable.Range(0, after[0].Count).Select(after[0].GetAttribute<Object>(PointAttributeNames.Prefab).Get));
            Assert.IsNull(after[0].GetAttribute<int>(SubdivideNode.OpeningAttribute), "No Opening attribute without openings.");
            Assert.AreEqual(0, after[1].Count);
        }

        [Test]
        public void Graph_WiresSceneInputIntoOpenings_AndCompiles()
        {
            const string folder = "Assets/__PlacementOpeningTests";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "__PlacementOpeningTests");
            try
            {
                PlacementGraphHandle graph = PlacementGraphAuthoring.Create(folder + "/Openings.placegraph");
                PlacementNodeHandle zones = graph.AddNode("Scene Input").Set("Label", "Opening");
                PlacementNodeHandle walls = graph.AddNode("Subdivide").Set("Opening Mode", "Module").Set("Fit To Module", true);
                graph.Connect(zones, "Out", walls, "Openings");
                CollectionAssert.Contains(walls.InputPorts, "Openings");
                CollectionAssert.Contains(walls.OutputPorts, "Gaps");
                Assert.IsTrue(graph.Validate().Any(p => p.Contains("Opening Symbol")), "No Door module in the chain.");

                var errors = new List<string>();
                PlacementGraphAsset compiled = graph.Compile(errors);
                Assert.IsEmpty(errors);
                var runtime = compiled.Nodes.OfType<SubdivideNode>().Single();
                Assert.AreEqual(Subdivide.OpeningMode.Module, runtime.OpeningMode);
                Assert.IsTrue(runtime.FitToModule);
                Assert.IsTrue(compiled.Edges.Any(e => e.ToPort == 1 && compiled.Nodes[e.FromNode] is SceneInputNode), "Wired into input 1.");
                Object.DestroyImmediate(compiled);

                StringAssert.Contains("\"Openings\"", PlacementGraphJson.Inspect(folder + "/Openings.placegraph", "Subdivide"));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
