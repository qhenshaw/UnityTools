using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityTools.Placement.Tests
{
    /// <summary>Per-point attributes: storage, and every node carrying them along with its points.</summary>
    public class PlacementAttributeTests
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

        protected T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }

        /// <summary>Points along X at 0..count-1, with an Int attribute "Id" equal to the index and density index/count.</summary>
        internal static PointData Tagged(int count)
        {
            var data = new PointData();
            PointAttribute<int> id = data.GetOrAddAttribute("Id", -1);
            for (int i = 0; i < count; i++)
            {
                PlacementPoint point = PlacementPoint.At(new Vector3(i, 0f, 0f), (uint)(i + 1));
                point.Density = i / (float)count;
                data.Points.Add(point);
                id.Set(i, i);
            }
            return data;
        }

        internal static PointData[] Run(PlacementNode node, PlacementContext context, params PointData[] inputs)
        {
            node.Name = node.GetType().Name;
            node.NodeSeed = 4242u;
            context.InputConnected = inputs.Select(_ => true).ToArray();
            var outputs = new PointData[node.OutputCount];
            node.Execute(context, inputs, outputs);
            return outputs;
        }

        private static void AssertIdsMatchPositions(PointData data)
        {
            PointAttribute<int> id = data.GetAttribute<int>("Id");
            Assert.IsNotNull(id, "The attribute was dropped.");
            for (int i = 0; i < data.Count; i++) Assert.AreEqual(Mathf.RoundToInt(data.Points[i].Position.x), id.Get(i));
        }

        [Test]
        public void Column_ReadsDefaultsPastItsEnd_AndPadsOnSet()
        {
            var data = new PointData();
            for (int i = 0; i < 3; i++) data.Points.Add(PlacementPoint.At(Vector3.zero, (uint)i));
            PointAttribute<float> weight = data.GetOrAddAttribute("Weight", 0.5f);
            Assert.AreEqual(0.5f, weight.Get(2));
            weight.Set(2, 9f);
            Assert.AreEqual(3, weight.Count);
            Assert.AreEqual(0.5f, weight.Get(0));
            Assert.AreEqual(9f, weight.Get(2));
            Assert.AreEqual(PointAttributeType.Float, weight.Type);
            Assert.IsNull(data.GetAttribute<int>("Weight"), "Typed lookup checks the type.");
        }

        [Test]
        public void Filters_KeepAttributesWithTheirPoints()
        {
            PointData input = Tagged(10);
            var context = new PlacementContext();
            PointData[] density = Run(new DensityFilterNode { Mode = DensityFilterNode.FilterMode.Threshold, Threshold = 0.5f }, context, input);
            AssertIdsMatchPositions(density[0]);
            AssertIdsMatchPositions(density[1]);
            Assert.AreEqual(5, density[0].Count);

            PointData[] height = Run(new HeightFilterNode { Min = -1f, Max = 1f }, context, input);
            AssertIdsMatchPositions(height[0]);
            PointData[] slope = Run(new SlopeFilterNode(), context, input);
            AssertIdsMatchPositions(slope[0]);
            PointData[] prune = Run(new SelfPruningNode { Radius = 1.5f }, context, input);
            AssertIdsMatchPositions(prune[0]);
            AssertIdsMatchPositions(prune[1]);
            PointData[] difference = Run(new DifferenceNode { Radius = 0.5f }, context, input, Tagged(3));
            AssertIdsMatchPositions(difference[0]);
            Assert.AreEqual(7, difference[0].Count);
        }

        [Test]
        public void Modifiers_KeepAttributes()
        {
            PointData input = Tagged(6);
            var context = new PlacementContext();
            AssertIdsMatchPositions(Run(new NoiseDensityNode(), context, input)[0]);
            AssertIdsMatchPositions(Run(new DistanceDensityNode(), context, input, Tagged(1))[0]);
            AssertIdsMatchPositions(Run(new TransformPointsNode { RotationMax = Vector3.zero }, context, input)[0]);
        }

        [Test]
        public void Merge_UnionsAttributes_WithDefaultsForMissing()
        {
            PointData a = Tagged(2);
            var b = new PointData();
            b.Points.Add(PlacementPoint.At(Vector3.one, 9u));
            b.GetOrAddAttribute("Height", 0f).Set(0, 3f);

            PointData merged = Run(new MergeNode(), new PlacementContext(), a, b)[0];
            Assert.AreEqual(3, merged.Count);
            PointAttribute<int> id = merged.GetAttribute<int>("Id");
            PointAttribute<float> height = merged.GetAttribute<float>("Height");
            CollectionAssert.AreEqual(new[] { 0, 1, -1 }, Enumerable.Range(0, 3).Select(id.Get));
            CollectionAssert.AreEqual(new[] { 0f, 0f, 3f }, Enumerable.Range(0, 3).Select(height.Get));
        }

        [Test]
        public void CopyPoints_TakesSourceAttributes_AndPatternOnlyOnes()
        {
            PointData sources = Tagged(2);
            var pattern = new PointData();
            pattern.Points.Add(PlacementPoint.At(Vector3.zero, 1u));
            pattern.Points.Add(PlacementPoint.At(Vector3.forward, 2u));
            PointAttribute<float> size = pattern.GetOrAddAttribute("Size", 0f);
            size.Set(0, 1f);
            size.Set(1, 2f);
            pattern.GetOrAddAttribute("Id", 100).Set(1, 100);

            PointData result = Run(new CopyPointsNode(), new PlacementContext(), sources, pattern)[0];
            Assert.AreEqual(4, result.Count);
            CollectionAssert.AreEqual(new[] { 0, 0, 1, 1 }, Enumerable.Range(0, 4).Select(result.GetAttribute<int>("Id").Get), "Source wins.");
            CollectionAssert.AreEqual(new[] { 1f, 2f, 1f, 2f }, Enumerable.Range(0, 4).Select(result.GetAttribute<float>("Size").Get));
        }

        [Test]
        public void Executor_MergesAttributesOfSeveralWires_AndLeavesInputsAlone()
        {
            PointData input = Tagged(4);
            int before = input.GetAttribute<int>("Id").Count;
            PointData merged = PointData.Merge(new[] { input, PointData.Empty, input });
            Assert.AreEqual(8, merged.Count);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 0, 1, 2, 3 }, Enumerable.Range(0, 8).Select(merged.GetAttribute<int>("Id").Get));
            Assert.AreEqual(before, input.GetAttribute<int>("Id").Count);
        }
    }
}
