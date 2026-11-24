using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityTools.Placement.Tests
{
    /// <summary>Rotation and scale control: Transform Points modes and Spawn Prefabs' prefab rotation and scale.</summary>
    public class PlacementTransformTests
    {
        private readonly List<Object> _created = new();

        private sealed class RecordingOutput : IPlacementOutput
        {
            public readonly List<(Quaternion rotation, Vector3 scale)> Spawned = new();
            public void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale) => Spawned.Add((rotation, scale));
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

        private static PointData[] Run(PlacementNode node, PlacementContext context, PointData input)
        {
            node.Name = node.GetType().Name;
            node.NodeSeed = 99u;
            var outputs = new PointData[node.OutputCount];
            node.Execute(context, new[] { input }, outputs);
            return outputs;
        }

        private static PointData RotatedScaledPoints(int count)
        {
            var data = new PointData();
            for (int i = 0; i < count; i++)
            {
                PlacementPoint point = PlacementPoint.At(new Vector3(i, 0f, 0f), (uint)(i + 1));
                point.Rotation = Quaternion.Euler(0f, 90f, 0f);
                point.Scale = Vector3.one * 2f;
                data.Points.Add(point);
            }
            return data;
        }

        private static void AssertSameRotation(Quaternion expected, Quaternion actual) =>
            Assert.That(Quaternion.Angle(expected, actual), Is.LessThan(0.01f));

        [Test]
        public void RotationMode_AddKeepsPointRotation_ReplaceDropsIt()
        {
            var fixedRotation = new Vector3(10f, 20f, 30f);
            PointData input = RotatedScaledPoints(3);

            PointData added = Run(new TransformPointsNode { RotationMin = fixedRotation, RotationMax = fixedRotation }, new PlacementContext(), input)[0];
            PointData replaced = Run(new TransformPointsNode
            {
                RotationMin = fixedRotation, RotationMax = fixedRotation,
                RotationMode = TransformPointsNode.RotationModes.Replace
            }, new PlacementContext(), input)[0];

            foreach (PlacementPoint point in added.Points)
                AssertSameRotation(Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(fixedRotation), point.Rotation);
            foreach (PlacementPoint point in replaced.Points)
                AssertSameRotation(Quaternion.Euler(fixedRotation), point.Rotation);
        }

        [Test]
        public void Scale_UniformOrPerAxis_MultiplyOrReplace()
        {
            PointData input = RotatedScaledPoints(50);
            var min = new Vector3(0.5f, 1f, 2f);
            var max = new Vector3(1f, 3f, 4f);

            PointData uniform = Run(new TransformPointsNode { ScaleMin = 0.5f, ScaleMax = 1f, AxisScaleMin = min, AxisScaleMax = max }, new PlacementContext(), input)[0];
            foreach (PlacementPoint point in uniform.Points)
            {
                Assert.AreEqual(point.Scale.x, point.Scale.y);
                Assert.AreEqual(point.Scale.x, point.Scale.z);
                Assert.That(point.Scale.x, Is.InRange(1f, 2f), "Multiplied by the point's scale of 2.");
            }

            PointData perAxis = Run(new TransformPointsNode
            {
                AxisScaleMin = min, AxisScaleMax = max, UniformScale = false,
                ScaleMode = TransformPointsNode.ScaleModes.Replace
            }, new PlacementContext(), input)[0];
            foreach (PlacementPoint point in perAxis.Points)
            {
                Assert.That(point.Scale.x, Is.InRange(min.x, max.x));
                Assert.That(point.Scale.y, Is.InRange(min.y, max.y));
                Assert.That(point.Scale.z, Is.InRange(min.z, max.z));
            }
            Assert.IsTrue(perAxis.Points.Any(p => !Mathf.Approximately(p.Scale.y, p.Scale.z)));
        }

        [Test]
        public void SpawnPrefabs_KeepsPrefabRotationAndScale_WhenAsked()
        {
            var prefab = Track(new GameObject("Prefab"));
            prefab.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            prefab.transform.localScale = new Vector3(1f, 3f, 1f);
            var palette = Track(ScriptableObject.CreateInstance<PrefabPalette>());
            palette.Entries.Add(new PrefabPalette.Entry { Prefab = prefab, Weight = 1f });
            PointData input = RotatedScaledPoints(1);
            Quaternion pointRotation = input.Points[0].Rotation;

            var defaults = new RecordingOutput();
            Run(new SpawnPrefabsNode { Palette = palette }, new PlacementContext { Output = defaults }, input);
            AssertSameRotation(pointRotation, defaults.Spawned[0].rotation);
            Assert.AreEqual(new Vector3(2f, 6f, 2f), defaults.Spawned[0].scale, "Keep Prefab Scale is on by default.");

            var kept = new RecordingOutput();
            Run(new SpawnPrefabsNode { Palette = palette, KeepPrefabRotation = true, KeepPrefabScale = false },
                new PlacementContext { Output = kept }, input);
            AssertSameRotation(pointRotation * prefab.transform.localRotation, kept.Spawned[0].rotation);
            Assert.AreEqual(Vector3.one * 2f, kept.Spawned[0].scale);
        }

        [Test]
        public void Component_SpawnedObjectsGetTheGraphsRotationAndScale()
        {
            GameObject ground = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ground.transform.localScale = new Vector3(10f, 1f, 10f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            Physics.SyncTransforms();

            var prefab = Track(new GameObject("Prop"));
            prefab.transform.localScale = Vector3.one * 2f;
            var palette = Track(ScriptableObject.CreateInstance<PrefabPalette>());
            palette.Entries.Add(new PrefabPalette.Entry { Prefab = prefab, Weight = 1f });
            var nodes = new List<PlacementNode>
            {
                new SurfaceSamplerNode { Spacing = 2f, Jitter = 0f },
                new TransformPointsNode { RotationMax = new Vector3(0f, 360f, 0f), ScaleMin = 0.5f, ScaleMax = 1.5f },
                new SpawnPrefabsNode { Palette = palette }
            };
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].Name = i.ToString();
                nodes[i].NodeSeed = (uint)(i + 1) * 31u;
            }
            var graph = Track(ScriptableObject.CreateInstance<PlacementGraphAsset>());
            graph.SetContents(nodes, new List<PlacementEdge> { new(0, 0, 1, 0), new(1, 0, 2, 0) });

            var component = Track(new GameObject("Placement Graph")).AddComponent<PlacementGraph>();
            component.Size = new Vector3(10f, 4f, 10f);
            component.Graph = graph;
            component.Generate();

            List<Transform> spawned = component.transform.Cast<Transform>().ToList();
            Assert.AreEqual(25, spawned.Count);
            Assert.Greater(spawned.Select(t => Mathf.Round(t.eulerAngles.y)).Distinct().Count(), 10, "Random yaw per object.");
            Assert.Greater(spawned.Select(t => Mathf.Round(t.localScale.x * 100f)).Distinct().Count(), 10, "Random scale per object.");
            Assert.IsTrue(spawned.All(t => t.localScale.x >= 1f - 1e-4f && t.localScale.x <= 3f + 1e-4f), "Prefab scale 2 times 0.5 to 1.5.");
        }
    }
}
