using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityTools.Placement.Tests
{
    /// <summary>Modular building: the grammar, fitting modules to segments, corners and grids.</summary>
    public class PlacementModularTests
    {
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

        private GameObject Prefab(string name)
        {
            var prefab = new GameObject(name);
            _created.Add(prefab);
            return prefab;
        }

        private ModuleDefinition Module(string symbol, float size, float weight = 1f) =>
            new ModuleDefinition { Prefab = Prefab(symbol), Symbol = symbol, Size = size, Weight = weight };

        private static PointData Polyline(params Vector3[] points)
        {
            var data = new PointData();
            for (int i = 0; i < points.Length; i++) data.Points.Add(PlacementPoint.At(points[i], (uint)(i + 1)));
            return data;
        }

        private static PointData[] Run(PlacementNode node, PlacementContext context, params PointData[] inputs) =>
            PlacementAttributeTests.Run(node, context, inputs);

        [Test]
        public void Grammar_ParsesTermsAndReportsErrors()
        {
            Assert.IsTrue(ModuleGrammar.TryParse("[Corner][Wall]*[Door, Window][Wall]+", out ModuleGrammar grammar, out string error), error);
            Assert.AreEqual(4, grammar.Terms.Count);
            Assert.AreEqual(ModuleGrammar.Repeat.Once, grammar.Terms[0].Repeat);
            Assert.AreEqual(ModuleGrammar.Repeat.ZeroOrMore, grammar.Terms[1].Repeat);
            CollectionAssert.AreEqual(new[] { "Door", "Window" }, grammar.Terms[2].Symbols);
            Assert.AreEqual(ModuleGrammar.Repeat.OneOrMore, grammar.Terms[3].Repeat);
            Assert.AreEqual("[Corner][Wall]*[Door,Window][Wall]+", grammar.ToString());

            Assert.IsFalse(ModuleGrammar.TryParse("", out _, out error));
            Assert.IsFalse(ModuleGrammar.TryParse("[Wall", out _, out error));
            StringAssert.Contains("no matching", error);
            Assert.IsFalse(ModuleGrammar.TryParse("Wall*", out _, out error));
            Assert.IsFalse(ModuleGrammar.TryParse("[Wall,]", out _, out error));
        }

        [Test]
        public void Fit_FillsTheLengthExactly_OrLeavesTheGap()
        {
            var modules = new List<ModuleDefinition> { Module("Corner", 0.5f), Module("Wall", 2f), Module("Door", 1.5f) };
            ModuleGrammar.TryParse("[Corner][Wall]*[Door][Wall]*[Corner]", out ModuleGrammar grammar, out _);
            var slots = new List<ModuleGrammar.Slot>();

            Assert.IsTrue(grammar.TryFit(10.3f, modules, 0, true, _ => 0f, slots, out string error), error);
            // 10.3 - 0.5 - 1.5 - 0.5 = 7.8 for two wall runs of 3.9: two 2 m walls each (3.9 / 2 rounds to 2).
            CollectionAssert.AreEqual(new[] { 0, 1, 1, 2, 1, 1, 0 }, slots.Select(s => s.Module));
            Assert.AreEqual(10.3f, slots.Last().Start + slots.Last().Length, 1e-4f, "Closes the segment.");
            Assert.AreEqual(10.3f / 10.5f, slots[0].Scale, 1e-4f, "Natural length 10.5 m, everything scaled by the same small amount.");

            Assert.IsTrue(grammar.TryFit(11.2f, modules, 0, false, _ => 0f, slots, out error));
            Assert.AreEqual(7, slots.Count);
            Assert.AreEqual(10.5f, slots.Last().Start + slots.Last().Length, 1e-4f, "Modules keep their size; the 0.7 m gap is left.");
            Assert.IsTrue(slots.All(s => s.Scale == 1f));

            ModuleGrammar.TryParse("[Tower]", out ModuleGrammar missing, out _);
            Assert.IsFalse(missing.TryFit(5f, modules, 0, true, _ => 0f, slots, out error));
            StringAssert.Contains("Tower", error);

            ModuleGrammar.TryParse("[Wall]+", out ModuleGrammar atLeastOne, out _);
            Assert.IsTrue(atLeastOne.TryFit(0.5f, modules, 0, true, _ => 0f, slots, out _));
            Assert.AreEqual(1, slots.Count, "+ places one even when it doesn't fit.");
        }

        [Test]
        public void Subdivide_PlacesModulesAlongEachSegment_WithChoicesDeterministic()
        {
            var node = new SubdivideNode
            {
                Modules = new List<ModuleDefinition> { Module("Wall", 1f), Module("Window", 1f) },
                Grammar = "[Wall,Window]*"
            };
            PointData line = Polyline(new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(10, 0, 4));
            PointData[] outputs = Run(node, new PlacementContext(), line);
            Assert.AreEqual(14, outputs[0].Count, "10 + 4 metres of 1 m modules.");
            PointAttribute<Object> prefabs = outputs[0].GetAttribute<Object>(PointAttributeNames.Prefab);
            Assert.IsTrue(Enumerable.Range(0, 14).Select(prefabs.Get).Distinct().Count() == 2, "Both choices appear.");
            // Module centres along the first segment, local X along the segment.
            Assert.That(Vector3.Distance(new Vector3(0.5f, 0, 0), outputs[0].Points[0].Position), Is.LessThan(1e-4f));
            Assert.That(Vector3.Angle(Vector3.right, outputs[0].Points[0].Rotation * Vector3.right), Is.LessThan(0.01f));
            Assert.That(Vector3.Angle(Vector3.forward, outputs[0].Points[13].Rotation * Vector3.right), Is.LessThan(0.01f));
            Vector3 boundsSize = outputs[0].GetAttribute<Vector3>(SubdivideNode.BoundsMaxAttribute).Get(0) - outputs[0].GetAttribute<Vector3>(SubdivideNode.BoundsMinAttribute).Get(0);
            Assert.AreEqual(1f, boundsSize.x, 1e-4f, "Point bounds: the module's size.");

            PointData[] again = Run(node, new PlacementContext(), line);
            CollectionAssert.AreEqual(Enumerable.Range(0, 14).Select(prefabs.Get), Enumerable.Range(0, 14).Select(again[0].GetAttribute<Object>(PointAttributeNames.Prefab).Get));

            var errors = new PlacementContext();
            Run(new SubdivideNode { Grammar = "[Wall" }, errors, line);
            Assert.AreEqual(1, errors.Warnings.Count);
        }

        [Test]
        public void Subdivide_VolumeEdges_AndCornerTrim()
        {
            var node = new SubdivideNode
            {
                Modules = new List<ModuleDefinition> { Module("Wall", 1f) },
                Source = Subdivide.SourceMode.VolumeEdges,
                CornerTrim = 0.5f
            };
            var context = new PlacementContext { LocalBounds = new Bounds(Vector3.zero, new Vector3(10f, 2f, 6f)) };
            PointData[] outputs = Run(node, context, PointData.Empty);
            // Each edge loses 1 m to the two corners: 9 + 5 + 9 + 5.
            Assert.AreEqual(28, outputs[0].Count);
            Assert.IsTrue(outputs[0].Points.All(p => Mathf.Abs(p.Position.y + 1f) < 1e-4f), "On the volume's floor.");
        }

        [Test]
        public void Corner_FindsCornersWithAngleAndInsideFlag()
        {
            // An L-shaped room, counter-clockwise: five convex corners and one concave.
            PointData room = Polyline(new Vector3(0, 0, 0), new Vector3(6, 0, 0), new Vector3(6, 0, 3), new Vector3(3, 0, 3), new Vector3(3, 0, 6), new Vector3(0, 0, 6));
            PointData corners = Run(new CornerNode { Closed = true }, new PlacementContext(), room)[0];
            Assert.AreEqual(6, corners.Count);
            PointAttribute<float> angles = corners.GetAttribute<float>(CornerNode.AngleAttribute);
            PointAttribute<int> inside = corners.GetAttribute<int>(CornerNode.InsideAttribute);
            for (int i = 0; i < 6; i++) Assert.AreEqual(90f, Mathf.Abs(angles.Get(i)), 1e-3f);
            Assert.AreEqual(1, Enumerable.Range(0, 6).Count(i => inside.Get(i) == 1));
            Assert.AreEqual(new Vector3(3, 0, 3), corners.Points[Enumerable.Range(0, 6).Single(i => inside.Get(i) == 1)].Position);

            // Open lines skip their ends; a straight run has no corners.
            Assert.AreEqual(0, Run(new CornerNode(), new PlacementContext(), Polyline(Vector3.zero, Vector3.right, Vector3.right * 2))[0].Count);
            Assert.AreEqual(1, Run(new CornerNode(), new PlacementContext(), Polyline(Vector3.zero, Vector3.right, new Vector3(1, 0, 1)))[0].Count);
        }

        [Test]
        public void SelfPruning_UsePointBounds_KeepsBoundsApart()
        {
            var points = new PointData();
            PointAttribute<Vector3> min = points.GetOrAddAttribute<Vector3>(SubdivideNode.BoundsMinAttribute);
            PointAttribute<Vector3> max = points.GetOrAddAttribute<Vector3>(SubdivideNode.BoundsMaxAttribute);
            for (int i = 0; i < 5; i++)
            {
                points.Points.Add(PlacementPoint.At(new Vector3(i * 1.5f, 0f, 0f), (uint)(i + 1)));
                min.Set(i, new Vector3(-1f, 0f, -1f));
                max.Set(i, new Vector3(1f, 2f, 1f));
            }
            // Centres 1.5 m apart but 2 m wide: without bounds and Radius 1 all are kept; with bounds every other one.
            Assert.AreEqual(5, Run(new SelfPruningNode { Radius = 1f }, new PlacementContext(), points)[0].Count);
            PointData pruned = Run(new SelfPruningNode { Radius = 0.1f, UsePointBounds = true }, new PlacementContext(), points)[0];
            Assert.AreEqual(3, pruned.Count);
            for (int i = 1; i < pruned.Count; i++)
                Assert.GreaterOrEqual(Vector3.Distance(pruned.Points[i].Position, pruned.Points[i - 1].Position), 2.1f - 1e-4f);
        }

        [Test]
        public void CreateGrid_AndSnapToGrid()
        {
            var context = new PlacementContext { LocalBounds = new Bounds(Vector3.zero, new Vector3(4f, 2f, 4f)) };
            PointData grid = Run(new CreateGridNode { CellSize = Vector3.one * 2f }, context)[0];
            Assert.AreEqual(9, grid.Count, "3 x 3 points from corner to corner.");
            Assert.AreEqual(new Vector3(-2f, -1f, -2f), grid.Points[0].Position);
            Assert.AreEqual(9 * 2, Run(new CreateGridNode { CellSize = Vector3.one * 2f, ThreeDimensional = true }, context)[0].Count);

            PointData loose = Polyline(new Vector3(0.4f, 0.3f, 1.6f), new Vector3(-0.6f, 2.2f, 2.4f));
            PointData snapped = Run(new SnapToGridNode { CellSize = Vector3.one }, new PlacementContext(), loose)[0];
            Assert.AreEqual(new Vector3(0f, 0.3f, 2f), snapped.Points[0].Position);
            Assert.AreEqual(new Vector3(-1f, 2.2f, 2f), snapped.Points[1].Position);
            PointData offset = Run(new SnapToGridNode { CellSize = Vector3.one, Offset = Vector3.one * 0.5f, SnapY = true }, new PlacementContext(), loose)[0];
            Assert.AreEqual(new Vector3(0.5f, 0.5f, 1.5f), offset.Points[0].Position);
        }
    }
}
