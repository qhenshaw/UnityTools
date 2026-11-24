using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityTools.Placement.Tests
{
    /// <summary>Choosing prefabs into attributes, filtering and transforming per prefab, and palette entry adjustments.</summary>
    public class PlacementPrefabAttributeTests
    {
        private readonly List<Object> _created = new();

        private sealed class RecordingOutput : IPlacementOutput
        {
            public readonly List<(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)> Spawned = new();
            public void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale) => Spawned.Add((prefab, position, rotation, scale));
        }

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

        private (PrefabPalette palette, GameObject a, GameObject b) TwoPrefabPalette()
        {
            GameObject a = Track(new GameObject("A"));
            GameObject b = Track(new GameObject("B"));
            var palette = Track(ScriptableObject.CreateInstance<PrefabPalette>());
            palette.Entries.Add(new PrefabPalette.Entry { Prefab = a, Weight = 1f });
            palette.Entries.Add(new PrefabPalette.Entry { Prefab = b, Weight = 1f });
            return (palette, a, b);
        }

        private static PointData Grid(int count)
        {
            var data = new PointData();
            for (int i = 0; i < count; i++) data.Points.Add(PlacementPoint.At(new Vector3(i, 0f, 0f), (uint)(i + 1)));
            return data;
        }

        private static PointData[] Run(PlacementNode node, PlacementContext context, params PointData[] inputs) =>
            PlacementAttributeTests.Run(node, context, inputs);

        [Test]
        public void SelectPrefab_WritesAttributes_SpawnUsesThem()
        {
            (PrefabPalette palette, GameObject a, GameObject b) = TwoPrefabPalette();
            PointData input = Grid(100);
            PointData selected = Run(new SelectPrefabNode { Palette = palette }, new PlacementContext(), input)[0];
            Assert.AreEqual(0, input.Attributes.Count, "The input isn't changed.");

            PointAttribute<Object> prefabs = selected.GetAttribute<Object>(PointAttributeNames.Prefab);
            PointAttribute<int> entries = selected.GetAttribute<int>(PointAttributeNames.PaletteEntry);
            Assert.AreEqual(palette, selected.GetAttribute<Object>(PointAttributeNames.Palette).Get(0));
            for (int i = 0; i < selected.Count; i++) Assert.AreEqual(palette.Entries[entries.Get(i)].Prefab, prefabs.Get(i));
            int aCount = Enumerable.Range(0, 100).Count(i => prefabs.Get(i) == a);
            Assert.That(aCount, Is.InRange(30, 70), "Picked by weight.");

            var output = new RecordingOutput();
            Run(new SpawnPrefabsNode(), new PlacementContext { Output = output }, selected);
            Assert.AreEqual(100, output.Spawned.Count, "No palette needed on Spawn Prefabs.");
            for (int i = 0; i < 100; i++) Assert.AreEqual(prefabs.Get(i), output.Spawned[i].prefab);
        }

        [Test]
        public void FilterByPrefab_ThenTransformOneBranch()
        {
            (PrefabPalette palette, GameObject a, GameObject b) = TwoPrefabPalette();
            var nodes = new List<PlacementNode>
            {
                new VolumeSamplerNode { Spacing = 1f },
                new SelectPrefabNode { Palette = palette },
                new FilterByAttributeNode { Compare = FilterByAttributeNode.Comparison.PrefabIs, Object = a },
                new TransformPointsNode { RotationMax = Vector3.zero, ScaleMin = 2f, ScaleMax = 2f },
                new SpawnPrefabsNode(),
                new SpawnPrefabsNode()
            };
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].Name = i.ToString();
                nodes[i].NodeSeed = (uint)(i + 1) * 101u;
            }
            var graph = Track(ScriptableObject.CreateInstance<PlacementGraphAsset>());
            graph.SetContents(nodes, new List<PlacementEdge>
            {
                new(0, 0, 1, 0), new(1, 0, 2, 0), new(2, 0, 3, 0), new(3, 0, 4, 0), new(2, 1, 5, 0)
            });

            var output = new RecordingOutput();
            PlacementExecutor.Run(graph, new PlacementContext { LocalBounds = new Bounds(Vector3.zero, new Vector3(10f, 2f, 10f)), Output = output });
            Assert.AreEqual(100, output.Spawned.Count);
            Assert.IsTrue(output.Spawned.Where(s => s.prefab == a).All(s => s.scale == Vector3.one * 2f));
            Assert.IsTrue(output.Spawned.Where(s => s.prefab == b).All(s => s.scale == Vector3.one));
            Assert.Greater(output.Spawned.Count(s => s.prefab == a), 0);
            Assert.Greater(output.Spawned.Count(s => s.prefab == b), 0);
        }

        [Test]
        public void EntryAdjustments_AppliedBySpawnPrefabs_UnlessTurnedOff()
        {
            GameObject prefab = Track(new GameObject("Lamp"));
            var palette = Track(ScriptableObject.CreateInstance<PrefabPalette>());
            palette.Entries.Add(new PrefabPalette.Entry
            {
                Prefab = prefab,
                PositionOffset = new Vector3(0f, 0f, 1f),
                RotationOffset = new Vector3(0f, 90f, 0f),
                RandomScale = true, ScaleMin = 3f, ScaleMax = 3f
            });
            PointData input = Grid(1);
            PlacementPoint point = input.Points[0];
            point.Rotation = Quaternion.Euler(0f, 90f, 0f);
            input.Points[0] = point;

            var adjusted = new RecordingOutput();
            Run(new SpawnPrefabsNode { Palette = palette }, new PlacementContext { Output = adjusted }, input);
            // The offset is in the point's space: forward turned 90 degrees is +X.
            Assert.That(Vector3.Distance(new Vector3(1f, 0f, 0f), adjusted.Spawned[0].position), Is.LessThan(1e-4f));
            Assert.That(Quaternion.Angle(Quaternion.Euler(0f, 180f, 0f), adjusted.Spawned[0].rotation), Is.LessThan(0.01f));
            Assert.AreEqual(Vector3.one * 3f, adjusted.Spawned[0].scale);

            var plain = new RecordingOutput();
            Run(new SpawnPrefabsNode { Palette = palette, ApplyEntryAdjustments = false }, new PlacementContext { Output = plain }, input);
            Assert.AreEqual(Vector3.zero, plain.Spawned[0].position);
            Assert.AreEqual(Vector3.one, plain.Spawned[0].scale);
        }

        [Test]
        public void SelectPrefab_EntryValuesAndCustomAttributes_UsedDownstream()
        {
            (PrefabPalette palette, GameObject a, GameObject b) = TwoPrefabPalette();
            palette.Entries[0].RotationRandomMin = new Vector3(0f, -45f, 0f);
            palette.Entries[0].RotationRandomMax = new Vector3(0f, 45f, 0f);
            palette.Entries[0].Attributes.Add(new PrefabPalette.EntryAttribute { Name = "Height", Type = PointAttributeType.Float, FloatValue = 5f });
            palette.Entries[1].Attributes.Add(new PrefabPalette.EntryAttribute { Name = "Height", Type = PointAttributeType.Float, FloatValue = 1f });

            PointData selected = Run(new SelectPrefabNode { Palette = palette }, new PlacementContext(), Grid(60))[0];
            PointAttribute<float> height = selected.GetAttribute<float>("Height");
            PointAttribute<Object> prefabs = selected.GetAttribute<Object>(PointAttributeNames.Prefab);
            PointAttribute<Vector3> rotations = selected.GetAttribute<Vector3>(PointAttributeNames.EntryRotation);
            for (int i = 0; i < selected.Count; i++)
            {
                Assert.AreEqual(prefabs.Get(i) == a ? 5f : 1f, height.Get(i));
                if (prefabs.Get(i) == a) Assert.That(rotations.Get(i).y, Is.InRange(-45f, 45f));
                else Assert.AreEqual(Vector3.zero, rotations.Get(i));
            }

            PointData[] tall = Run(new FilterByAttributeNode { Compare = FilterByAttributeNode.Comparison.Greater, Attribute = "Height", Value = 2f }, new PlacementContext(), selected);
            Assert.IsTrue(Enumerable.Range(0, tall[0].Count).All(i => tall[0].GetAttribute<Object>(PointAttributeNames.Prefab).Get(i) == a));
            Assert.AreEqual(selected.Count, tall[0].Count + tall[1].Count);

            // Spawn Prefabs uses the rotation Select Prefab sampled, so the point keeps it whatever Spawn's own seed is.
            var output = new RecordingOutput();
            Run(new SpawnPrefabsNode(), new PlacementContext { Output = output }, selected);
            for (int i = 0; i < selected.Count; i++)
                Assert.That(Quaternion.Angle(Quaternion.Euler(rotations.Get(i)), output.Spawned[i].rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void FilterByAttribute_NumberComparisons()
        {
            PointData input = PlacementAttributeTests.Tagged(10);
            int Kept(FilterByAttributeNode.Comparison compare, float value, float max = 0f) =>
                Run(new FilterByAttributeNode { Compare = compare, Attribute = "Id", Value = value, Max = max }, new PlacementContext(), input)[0].Count;
            Assert.AreEqual(1, Kept(FilterByAttributeNode.Comparison.Equal, 3f));
            Assert.AreEqual(9, Kept(FilterByAttributeNode.Comparison.NotEqual, 3f));
            Assert.AreEqual(3, Kept(FilterByAttributeNode.Comparison.Less, 3f));
            Assert.AreEqual(6, Kept(FilterByAttributeNode.Comparison.Greater, 3f));
            Assert.AreEqual(4, Kept(FilterByAttributeNode.Comparison.InRange, 2f, 5f));

            var context = new PlacementContext();
            Run(new FilterByAttributeNode { Compare = FilterByAttributeNode.Comparison.Equal, Attribute = "Missing" }, context, input);
            Assert.AreEqual(1, context.Warnings.Count);
        }

        [Test]
        public void SetAttribute_ConstantsAndSeededRanges_FeedTransformPoints()
        {
            PointData input = Grid(200);
            PointData floats = Run(new SetAttributeNode { Attribute = "S", Random = true, FloatMin = 2f, FloatMax = 4f }, new PlacementContext(), input)[0];
            PointAttribute<float> s = floats.GetAttribute<float>("S");
            Assert.IsTrue(Enumerable.Range(0, 200).All(i => s.Get(i) >= 2f && s.Get(i) <= 4f));
            PointData again = Run(new SetAttributeNode { Attribute = "S", Random = true, FloatMin = 2f, FloatMax = 4f }, new PlacementContext(), input)[0];
            CollectionAssert.AreEqual(s.Values, again.GetAttribute<float>("S").Values, "Deterministic.");

            PointData ints = Run(new SetAttributeNode { Attribute = "N", Type = SetAttributeNode.ValueType.Int, Random = true, IntMin = 1, IntMax = 3 }, new PlacementContext(), input)[0];
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, ints.GetAttribute<int>("N").Values.Distinct());

            PointData vectors = Run(new SetAttributeNode { Attribute = "V", Type = SetAttributeNode.ValueType.Vector3, Vector3Min = Vector3.up }, new PlacementContext(), input)[0];
            Assert.IsTrue(vectors.GetAttribute<Vector3>("V").Values.All(v => v == Vector3.up));

            PointData moved = Run(new TransformPointsNode
            {
                RotationMax = Vector3.zero, ScaleAttribute = "S", OffsetAttribute = "V"
            }, new PlacementContext(), CombineAttributes(floats, vectors))[0];
            for (int i = 0; i < moved.Count; i++)
            {
                Assert.AreEqual(Vector3.one * s.Get(i), moved.Points[i].Scale);
                Assert.AreEqual(input.Points[i].Position + Vector3.up, moved.Points[i].Position);
            }
        }

        /// <summary>The points of <paramref name="a"/> with the attributes of both (same points in the same order).</summary>
        private static PointData CombineAttributes(PointData a, PointData b)
        {
            PointData result = PointData.Copy(a);
            foreach (PointAttribute attribute in b.Attributes)
            {
                if (attribute is PointAttribute<Vector3> vectors)
                {
                    PointAttribute<Vector3> column = result.GetOrAddAttribute<Vector3>(attribute.Name);
                    for (int i = 0; i < vectors.Count; i++) column.Set(i, vectors.Get(i));
                }
            }
            return result;
        }
    }
}
