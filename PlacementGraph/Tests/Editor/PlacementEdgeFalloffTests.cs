using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
#if UNITYTOOLS_SPLINES
using Unity.Mathematics;
using UnityEngine.Splines;
#endif
using Object = UnityEngine.Object;

namespace UnityTools.Placement.Tests
{
    /// <summary>Edge Falloff on Spline Sampler (Inside) and Scene Input zones, and using it for scale and density.</summary>
    [SceneIsolation]
    public class PlacementEdgeFalloffTests
    {
        // Away from the origin, where other tests put things; near enough that positions keep their precision.
        private static readonly Vector3 Origin = new Vector3(500f, 0f, 500f);
        private readonly List<Object> _created = new();

        private sealed class RecordingOutput : IPlacementOutput
        {
            public readonly List<Vector3> Scales = new List<Vector3>();
            public void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale) => Scales.Add(scale);
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

        private static PlacementContext Context(Vector3 origin, Vector3 size) => new PlacementContext
        {
            LocalToWorld = Matrix4x4.TRS(origin, Quaternion.identity, Vector3.one),
            LocalBounds = new Bounds(Vector3.zero, size),
            Scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene()
        };

        private static PointData[] Run(PlacementNode node, PlacementContext context, params PointData[] inputs) =>
            PlacementAttributeTests.Run(node, context, inputs);

        private static float[] Values(PointData points, string attribute = EdgeFalloff.DefaultAttribute)
        {
            PointAttribute<float> values = points.GetAttribute<float>(attribute);
            Assert.IsNotNull(values, $"The points have a {attribute} attribute.");
            return Enumerable.Range(0, points.Count).Select(values.Get).ToArray();
        }

        [Test]
        public void Evaluate_RisesFromTheEdgeValueToOne_AlongTheCurve()
        {
            Assert.AreEqual(0f, EdgeFalloff.Evaluate(0f, 2f, FalloffCurve.Linear, 0f), 1e-6f, "0 at the edge");
            Assert.AreEqual(0.5f, EdgeFalloff.Evaluate(1f, 2f, FalloffCurve.Linear, 0f), 1e-6f, "halfway in");
            Assert.AreEqual(1f, EdgeFalloff.Evaluate(2f, 2f, FalloffCurve.Linear, 0f), 1e-6f, "1 at the distance");
            Assert.AreEqual(1f, EdgeFalloff.Evaluate(50f, 2f, FalloffCurve.Linear, 0f), 1e-6f, "1 beyond it");
            Assert.AreEqual(1f, EdgeFalloff.Evaluate(0f, 0f, FalloffCurve.Linear, 0f), 1e-6f, "distance 0 turns it off");
            Assert.AreEqual(0.2f, EdgeFalloff.Evaluate(0f, 2f, FalloffCurve.Linear, 0.2f), 1e-6f, "Min at the edge");
            Assert.AreEqual(0.6f, EdgeFalloff.Evaluate(1f, 2f, FalloffCurve.Linear, 0.2f), 1e-6f, "Min remaps the range");
            Assert.AreEqual(0.25f, EdgeFalloff.Evaluate(1f, 2f, FalloffCurve.EaseIn, 0f), 1e-6f);
            Assert.AreEqual(0.75f, EdgeFalloff.Evaluate(1f, 2f, FalloffCurve.EaseOut, 0f), 1e-6f);
            Assert.AreEqual(0.5f, EdgeFalloff.Evaluate(1f, 2f, FalloffCurve.Smooth, 0f), 1e-6f);
            Assert.AreEqual(0.104f, EdgeFalloff.Evaluate(0.4f, 2f, FalloffCurve.Smooth, 0f), 1e-4f, "smoothstep is flat near the edge");
        }

        [Test]
        public void SceneInput_BoxZone_FallsOffFromItsSides_NotItsTopAndBottom()
        {
            // A 4 m box, 1 m grid: an outer ring of points 0.5 m in, and four 1.5 m in. The box is 4 m tall too, but its
            // top and bottom don't count.
            var zone = Track(new GameObject("Zone")).AddComponent<PlacementZone>();
            zone.transform.position = Origin;
            zone.Size = Vector3.one * 4f;
            var node = new SceneInputNode { Output = SceneInputNode.OutputMode.PointsInsideZones, Spacing = 1f, EdgeFalloffDistance = 1f };
            PointData points = Run(node, Context(Origin, new Vector3(20f, 10f, 20f)))[0];

            Assert.AreEqual(16, points.Count);
            float[] values = Values(points);
            Assert.AreEqual(12, values.Count(v => Mathf.Abs(v - 0.5f) < 1e-3f), "the ring 0.5 m in is at 0.5");
            Assert.AreEqual(4, values.Count(v => Mathf.Abs(v - 1f) < 1e-3f), "1.5 m in is past the distance: 1");

            CollectionAssert.AreEqual(values, Values(Run(node, Context(Origin, new Vector3(20f, 10f, 20f)))[0]), "deterministic");
        }

        [Test]
        public void SceneInput_SphereZone_FallsOffWithDistanceFromItsRim()
        {
            var zone = Track(new GameObject("Zone")).AddComponent<PlacementZone>();
            zone.transform.position = Origin;
            zone.Shape = PlacementZone.ZoneShape.Sphere;
            zone.Radius = 3f;
            var node = new SceneInputNode
            {
                Output = SceneInputNode.OutputMode.PointsInsideZones, Spacing = 0.5f,
                EdgeFalloffDistance = 2f, EdgeFalloffAttribute = "Rim"
            };
            PointData points = Run(node, Context(Origin, new Vector3(20f, 10f, 20f)))[0];

            Assert.Greater(points.Count, 50);
            float[] values = Values(points, "Rim");
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 offset = points.Points[i].Position - Origin;
                float inside = 3f - new Vector2(offset.x, offset.z).magnitude;
                Assert.AreEqual(Mathf.Clamp01(inside / 2f), values[i], 1e-3f);
            }
            Assert.IsNull(points.GetAttribute<float>(EdgeFalloff.DefaultAttribute), "only the named attribute is written");
        }

        [Test]
        public void SceneInput_OnePointPerObject_GetsNoFalloff()
        {
            var zone = Track(new GameObject("Zone")).AddComponent<PlacementZone>();
            zone.transform.position = Origin;
            var node = new SceneInputNode { EdgeFalloffDistance = 1f };
            PointData points = Run(node, Context(Origin, new Vector3(20f, 10f, 20f)))[0];
            Assert.AreEqual(1, points.Count);
            Assert.IsNull(points.GetAttribute<float>(EdgeFalloff.DefaultAttribute), "falloff is for points inside zones");
        }

#if UNITYTOOLS_SPLINES
        private SplineContainer Square(Vector3 centre, float half)
        {
            var container = Track(new GameObject("Spline")).AddComponent<SplineContainer>();
            container.transform.position = centre;
            foreach (Vector2 corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
                container.Spline.Add(new BezierKnot(new float3(corner.x * half, 0f, corner.y * half)));
            container.Spline.Closed = true;
            return container;
        }

        [Test]
        public void SplineSampler_Inside_WritesTheFalloff_ZeroAtTheEdge_OneAtTheDistance()
        {
            PlacementContext context = Context(Vector3.zero, new Vector3(10f, 4f, 10f));
            context.Spline = Square(Vector3.zero, 3f);
            var node = new SplineSamplerNode { Mode = SplineSamplerNode.SampleMode.Inside, Spacing = 1f, Jitter = 0f, EdgeFalloffDistance = 2f };
            PointData points = Run(node, context)[0];

            Assert.AreEqual(36, points.Count);
            float[] values = Values(points);
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points.Points[i].Position;
                float inside = 3f - Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z));
                Assert.AreEqual(Mathf.Clamp01(inside / 2f), values[i], 1e-3f, $"at {p}");
            }
            Assert.AreEqual(20, values.Count(v => Mathf.Abs(v - 0.25f) < 1e-3f), "0.5 m in");
            Assert.AreEqual(4, values.Count(v => Mathf.Abs(v - 1f) < 1e-3f), "2.5 m in, beyond the distance");

            CollectionAssert.AreEqual(values, Values(Run(node, context)[0]), "deterministic");
            Assert.IsNull(Run(new SplineSamplerNode { Mode = SplineSamplerNode.SampleMode.Inside, Spacing = 1f }, context)[0]
                .GetAttribute<float>(EdgeFalloff.DefaultAttribute), "distance 0 writes nothing");
        }

        [Test]
        public void SceneInput_SplineZone_MatchesTheSplineSampler()
        {
            SplineContainer container = Square(Origin, 3f);
            var zone = container.gameObject.AddComponent<PlacementZone>();
            zone.Shape = PlacementZone.ZoneShape.Spline;
            var node = new SceneInputNode { Output = SceneInputNode.OutputMode.PointsInsideZones, Spacing = 1f, EdgeFalloffDistance = 2f };
            PointData points = Run(node, Context(Origin, new Vector3(20f, 10f, 20f)))[0];

            Assert.AreEqual(36, points.Count);
            float[] values = Values(points);
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points.Points[i].Position - Origin;
                float inside = 3f - Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z));
                Assert.AreEqual(Mathf.Clamp01(inside / 2f), values[i], 1e-2f, $"at {p}");
            }
        }
#endif

        [Test]
        public void TransformPoints_ScaleAttribute_ScalesByTheFalloff_AndZeroScaleIsNotSpawned()
        {
            var data = new PointData();
            PointAttribute<float> falloff = data.GetOrAddAttribute<float>(EdgeFalloff.DefaultAttribute);
            float[] values = { 0f, 0.25f, 1f };
            for (int i = 0; i < values.Length; i++)
            {
                data.Points.Add(PlacementPoint.At(new Vector3(i, 0f, 0f), (uint)i + 1u));
                falloff.Set(i, values[i]);
            }

            var transform = new TransformPointsNode { ScaleMin = 2f, ScaleMax = 2f, ScaleAttribute = EdgeFalloff.DefaultAttribute };
            PointData scaled = Run(transform, Context(Vector3.zero, Vector3.one * 10f), data)[0];
            CollectionAssert.AreEqual(new[] { 0f, 0.5f, 2f }, scaled.Points.Select(p => p.Scale.x).ToArray());

            GameObject prefab = Track(new GameObject("Prefab"));
            var output = new RecordingOutput();
            PlacementContext context = Context(Vector3.zero, Vector3.one * 10f);
            context.Output = output;
            var spawn = new SpawnPrefabsNode { Entries = { new PrefabEntry { Prefab = prefab, Weight = 1f } } };
            Run(spawn, context, scaled);
            CollectionAssert.AreEqual(new[] { 0.5f, 2f }, output.Scales.Select(s => s.x).ToArray(), "the point scaled to 0 makes no object");
        }

        [Test]
        public void DensityFilter_DensityAttribute_MultipliesTheDensity()
        {
            var data = new PointData();
            PointAttribute<float> falloff = data.GetOrAddAttribute<float>(EdgeFalloff.DefaultAttribute);
            for (int i = 0; i < 2000; i++)
            {
                data.Points.Add(PlacementPoint.At(new Vector3(i * 0.01f, 0f, 0f), (uint)i * 7919u + 1u));
                falloff.Set(i, i < 1000 ? 0f : 0.5f);
            }

            var threshold = new DensityFilterNode { Mode = DensityFilterNode.FilterMode.Threshold, Threshold = 0.4f, DensityAttribute = EdgeFalloff.DefaultAttribute };
            Assert.AreEqual(1000, Run(threshold, Context(Vector3.zero, Vector3.one * 100f), data)[0].Count);

            var random = new DensityFilterNode { Mode = DensityFilterNode.FilterMode.Random, DensityAttribute = EdgeFalloff.DefaultAttribute };
            PointData kept = Run(random, Context(Vector3.zero, Vector3.one * 100f), data)[0];
            Assert.IsTrue(kept.Points.All(p => p.Position.x >= 9.995f), "density 0 keeps nothing");
            Assert.AreEqual(500, kept.Count, 60, "density 0.5 keeps about half");

            PlacementContext missing = Context(Vector3.zero, Vector3.one * 100f);
            Run(new DensityFilterNode { DensityAttribute = "Nope" }, missing, data);
            Assert.AreEqual(1, missing.Warnings.Count, "an attribute that isn't there warns");
        }
    }
}
