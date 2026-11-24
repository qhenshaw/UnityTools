using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UnityTools.Placement.Tests
{
    /// <summary>Spawn Instanced: GPU-instanced batches instead of GameObjects.</summary>
    [SceneIsolation]
    public class PlacementInstancingTests
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

        private T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }

        private Material InstancedMaterial(bool instancing = true)
        {
            var material = Track(new Material(Shader.Find("Hidden/Internal-Colored")));
            material.enableInstancing = instancing;
            return material;
        }

        /// <summary>A prefab-like object: a root with <paramref name="parts"/> cube children, each with its own material.</summary>
        private GameObject Prefab(string name, int parts, bool instancing = true)
        {
            var root = Track(new GameObject(name));
            for (int i = 0; i < parts; i++)
            {
                GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(part.GetComponent<Collider>());
                part.transform.SetParent(root.transform, false);
                part.transform.localPosition = new Vector3(0f, i, 0f);
                part.GetComponent<MeshRenderer>().sharedMaterial = InstancedMaterial(instancing);
            }
            return root;
        }

        private PlacementGraph Component(List<PrefabEntry> entries, float spacing = 1f, int seed = 1)
        {
            var nodes = new List<PlacementNode>
            {
                new VolumeSamplerNode { Spacing = spacing, Jitter = 1f },
                new SpawnInstancedNode { Entries = entries, ShadowCasting = ShadowCastingMode.Off }
            };
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].Name = i.ToString();
                nodes[i].NodeSeed = (uint)(i + 1) * 977u;
            }
            var graph = Track(ScriptableObject.CreateInstance<PlacementGraphAsset>());
            graph.SetContents(nodes, new List<PlacementEdge> { new(0, 0, 1, 0) });
            var component = Track(new GameObject("Placement Graph")).AddComponent<PlacementGraph>();
            component.Size = new Vector3(10f, 2f, 10f);
            component.Seed = seed;
            component.Graph = graph;
            return component;
        }

        [Test]
        public void SpawnInstanced_MakesBatchesNotGameObjects()
        {
            GameObject single = Prefab("Single", 1);
            GameObject double_ = Prefab("Double", 2);
            PlacementGraph component = Component(new List<PrefabEntry>
            {
                new PrefabEntry { Prefab = single, Weight = 1f },
                new PrefabEntry { Prefab = double_, Weight = 1f }
            });
            component.Generate();

            Assert.AreEqual(0, component.transform.childCount, "No GameObjects.");
            Assert.AreEqual(100, component.InstanceCount);
            PlacementInstances instances = component.Instances;
            Assert.IsNotNull(instances);
            // One batch for the single prefab's material and one per part material of the double prefab.
            Assert.AreEqual(3, instances.Batches.Count);
            int singles = instances.Batches.Single(b => b.Material == single.GetComponentInChildren<MeshRenderer>().sharedMaterial).Matrices.Count;
            int doubles = instances.Batches.Where(b => b.Material != single.GetComponentInChildren<MeshRenderer>().sharedMaterial).Select(b => b.Matrices.Count).Distinct().Single();
            Assert.AreEqual(100, singles + doubles, "Each point is one prefab copy.");
            Assert.That(singles, Is.InRange(30, 70), "Picked by weight.");
            Assert.IsTrue(instances.Batches.All(b => b.ShadowCasting == ShadowCastingMode.Off));
            Assert.IsEmpty(component.LastWarnings);

            component.Clear();
            Assert.AreEqual(0, instances.InstanceCount);
        }

        [Test]
        public void SpawnInstanced_IsDeterministic_AndPlacesPartsRelativeToTheRoot()
        {
            GameObject prefab = Prefab("Stack", 2);
            var entries = new List<PrefabEntry> { new PrefabEntry { Prefab = prefab } };
            PlacementGraph first = Component(entries);
            PlacementGraph second = Component(entries);
            first.Generate();
            second.Generate();
            for (int b = 0; b < first.Instances.Batches.Count; b++)
                CollectionAssert.AreEqual(first.Instances.Batches[b].Matrices, second.Instances.Batches[b].Matrices);

            // The second part sits 1 m above the first in every copy.
            List<Matrix4x4> bottom = first.Instances.Batches[0].Matrices;
            List<Matrix4x4> top = first.Instances.Batches[1].Matrices;
            for (int i = 0; i < bottom.Count; i++)
                Assert.That(Vector3.Distance(bottom[i].GetPosition() + Vector3.up, top[i].GetPosition()), Is.LessThan(1e-4f));

            PlacementGraph otherSeed = Component(entries, seed: 2);
            otherSeed.Generate();
            CollectionAssert.AreNotEqual(first.Instances.Batches[0].Matrices, otherSeed.Instances.Batches[0].Matrices);
        }

        [Test]
        public void LargeBatches_DrawInChunks_AndMaterialsWithoutInstancingWarn()
        {
            GameObject prefab = Prefab("Pebble", 1, instancing: false);
            PlacementGraph component = Component(new List<PrefabEntry> { new PrefabEntry { Prefab = prefab } }, spacing: 0.25f);
            component.Generate();
            Assert.AreEqual(1600, component.Instances.InstanceCount);
            Assert.AreEqual(1, component.Instances.Batches.Count);
            Assert.AreEqual(2, component.Instances.DrawCallCount, "1600 instances in chunks of 1023.");
            Assert.AreEqual(1, component.LastWarnings.Count(w => w.Contains("GPU instancing")));
        }

        [Test]
        public void SpawnPrefabs_StillMakesGameObjects()
        {
            GameObject prefab = Prefab("Prop", 1);
            PlacementGraph component = Component(new List<PrefabEntry> { new PrefabEntry { Prefab = prefab } }, spacing: 5f);
            var graph = component.Graph;
            var nodes = graph.Nodes.ToList();
            nodes[1] = new SpawnPrefabsNode { Entries = new List<PrefabEntry> { new PrefabEntry { Prefab = prefab } }, Name = "1" };
            graph.SetContents(nodes, graph.Edges.ToList());
            component.Generate();
            Assert.AreEqual(4, component.transform.childCount);
            Assert.IsTrue(component.Instances == null || component.Instances.InstanceCount == 0);
        }
    }
}
