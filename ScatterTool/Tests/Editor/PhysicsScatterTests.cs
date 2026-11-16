using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ScatterTool.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ScatterTool.Tests
{
    public class PhysicsScatterTests
    {
        private const string Folder = "Assets/UnityToolsScatterTests";

        private readonly List<Object> _created = new();
        private SimulationMode _modeBefore;
        private PhysicsScatterSession _session;

        [SetUp]
        public void SetUp()
        {
            _modeBefore = Physics.simulationMode;
            _session = new PhysicsScatterSession();
        }

        [TearDown]
        public void TearDown()
        {
            _session.Stop();
            Physics.simulationMode = _modeBefore;
            foreach (Object obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }

        private GameObject Cube(string name, Vector3 position)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            _created.Add(go);
            return go;
        }

        // ---- Start / Stop ----

        [Test]
        public void Start_FreezesOtherBodies_AndFreesTheSelection()
        {
            GameObject other = Cube("Other", new Vector3(10f, 0f, 0f));
            Rigidbody otherBody = other.AddComponent<Rigidbody>();
            GameObject selected = Cube("Selected", Vector3.zero);

            _session.Start(new[] { selected });

            Assert.IsTrue(otherBody.isKinematic);
            Assert.IsTrue(selected.TryGetComponent(out Rigidbody added));
            Assert.IsFalse(added.isKinematic);
            Assert.IsFalse(added.useGravity);
            Assert.AreEqual(SimulationMode.Script, Physics.simulationMode);
        }

        [Test]
        public void Stop_RestoresEveryBodyExactly()
        {
            GameObject dynamic = Cube("Dynamic", new Vector3(10f, 0f, 0f));
            Rigidbody dynamicBody = dynamic.AddComponent<Rigidbody>();
            GameObject kinematic = Cube("Kinematic", new Vector3(20f, 0f, 0f));
            Rigidbody kinematicBody = kinematic.AddComponent<Rigidbody>();
            kinematicBody.isKinematic = true;
            kinematicBody.useGravity = false;
            GameObject selectedWithBody = Cube("Selected With Body", Vector3.zero);
            Rigidbody ownBody = selectedWithBody.AddComponent<Rigidbody>();
            ownBody.isKinematic = true;
            GameObject selectedPlain = Cube("Selected Plain", new Vector3(0f, 0f, 5f));

            _session.Start(new[] { selectedWithBody, selectedPlain });
            _session.Stop();

            Assert.IsFalse(dynamicBody.isKinematic, "plain objects don't stay kinematic");
            Assert.IsTrue(dynamicBody.useGravity);
            Assert.IsTrue(kinematicBody.isKinematic);
            Assert.IsFalse(kinematicBody.useGravity);
            Assert.IsTrue(ownBody != null, "a Rigidbody the object already had is kept");
            Assert.IsTrue(ownBody.isKinematic);
            Assert.IsTrue(ownBody.useGravity);
            Assert.IsFalse(selectedPlain.TryGetComponent(out Rigidbody _), "the added Rigidbody is removed");
        }

        [TestCase(SimulationMode.FixedUpdate)]
        [TestCase(SimulationMode.Script)]
        [TestCase(SimulationMode.Update)]
        public void Stop_RestoresTheSimulationMode(SimulationMode mode)
        {
            Physics.simulationMode = mode;
            _session.Start(new[] { Cube("Selected", Vector3.zero) });
            _session.Stop();
            Assert.AreEqual(mode, Physics.simulationMode);
        }

        [Test]
        public void Stop_WithoutStart_DoesNothing()
        {
            Physics.simulationMode = SimulationMode.Update;
            _session.Stop();
            Assert.AreEqual(SimulationMode.Update, Physics.simulationMode, "stopping no simulation doesn't touch the project setting");
        }

        [Test]
        public void Stop_RecordsTheMovesAsOneUndoStep()
        {
            GameObject a = Cube("A", Vector3.zero);
            GameObject b = Cube("B", new Vector3(3f, 0f, 0f));
            _session.BeforeStep = _ =>
            {
                a.GetComponent<Rigidbody>().AddForce(Vector3.up * 50f, ForceMode.Acceleration);
                b.GetComponent<Rigidbody>().AddForce(Vector3.up * 50f, ForceMode.Acceleration);
            };
            Undo.IncrementCurrentGroup();

            _session.Start(new[] { a, b });
            _session.Step(0.2d, 0.02f, 1);
            _session.Stop();
            Assert.Greater(a.transform.position.y, 0.1f);
            Assert.Greater(b.transform.position.y, 0.1f);

            Undo.PerformUndo();
            Assert.AreEqual(0f, a.transform.position.y, 1e-4f);
            Assert.AreEqual(0f, b.transform.position.y, 1e-4f);
        }

        // ---- Stepping ----

        [Test]
        public void Step_RunsInRealTime()
        {
            _session.Start(new[] { Cube("Selected", Vector3.zero) });

            Assert.AreEqual(5, _session.Step(0.1d, 0.02f, 1));
            Assert.AreEqual(0, _session.Step(0.01d, 0.02f, 1), "less than one step of time waits");
            Assert.AreEqual(1, _session.Step(0.01d, 0.02f, 1), "leftover time carries over");
            Assert.AreEqual(10, _session.Step(0.1d, 0.02f, 2), "step count splits each step");
        }

        [Test]
        public void Step_DoesNotCatchUpAfterAStall()
        {
            _session.Start(new[] { Cube("Selected", Vector3.zero) });
            Assert.AreEqual(12, _session.Step(10d, 0.02f, 1), "at most a quarter second at once");
        }

        [Test]
        public void Step_WhenStopped_DoesNothing()
        {
            Assert.AreEqual(0, _session.Step(1d, 0.02f, 1));
        }

        // ---- Tool ----

        [Test]
        public void Tool_DoesNotPileUpOverlayHandlers()
        {
            var tool = ScriptableObject.CreateInstance<PhysicsScatterTool>();
            _created.Add(tool);
            int before = HandlerCount("OnLaunch");

            for (int i = 0; i < 3; i++)
            {
                tool.OnActivated();
                tool.OnWillBeDeactivated();
            }
            Assert.AreEqual(before, HandlerCount("OnLaunch"), "toggling the tool leaves no handlers behind");

            tool.OnActivated();
            Assert.AreEqual(before + 1, HandlerCount("OnLaunch"));
            Assert.AreEqual(before + 1, HandlerCount("OnStartSim"));
            tool.OnWillBeDeactivated();
        }

        private static int HandlerCount(string eventName)
        {
            FieldInfo field = typeof(PhysicsScatterOverlay).GetField(eventName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field, eventName);
            var handler = (Delegate)field.GetValue(null);
            return handler?.GetInvocationList().Length ?? 0;
        }

        // ---- Copy ----

        [Test]
        public void Duplicate_KeepsScaleComponentsAndParent()
        {
            GameObject parent = new GameObject("Parent");
            _created.Add(parent);
            GameObject source = Cube("Rock", Vector3.zero);
            source.transform.SetParent(parent.transform, false);
            source.transform.localScale = new Vector3(2f, 3f, 4f);
            source.AddComponent<AudioSource>();

            GameObject[] copies = PhysicsScatterTool.Duplicate(new[] { source });
            _created.AddRange(copies);

            Assert.AreEqual(1, copies.Length);
            Assert.AreNotSame(source, copies[0]);
            Assert.AreEqual(new Vector3(2f, 3f, 4f), copies[0].transform.localScale);
            Assert.IsTrue(copies[0].TryGetComponent(out AudioSource _));
            Assert.AreSame(parent.transform, copies[0].transform.parent);
        }

        [Test]
        public void Duplicate_KeepsPrefabLinkAndOverrides()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "UnityToolsScatterTests");
            GameObject source = Cube("Rock", Vector3.zero);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, $"{Folder}/Rock.prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            _created.Add(instance);
            instance.transform.localScale = Vector3.one * 5f;
            instance.AddComponent<AudioSource>();
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);

            GameObject copy = PhysicsScatterTool.Duplicate(new[] { instance })[0];
            _created.Add(copy);

            Assert.AreSame(prefab, PrefabUtility.GetCorrespondingObjectFromSource(copy));
            Assert.AreEqual(Vector3.one * 5f, copy.transform.localScale);
            Assert.IsTrue(copy.TryGetComponent(out AudioSource _), "added components are kept");
        }
    }
}
