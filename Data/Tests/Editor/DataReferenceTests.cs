using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityTools.Data.Editor;

namespace UnityTools.Data.Tests
{
    public class DataReferenceTests
    {
        private readonly List<Object> _created = new();

        private T Create<T>() where T : ScriptableObject
        {
            T obj = ScriptableObject.CreateInstance<T>();
            _created.Add(obj);
            return obj;
        }

        private TestStats CreateStats(int health = 10, string name = "Stats")
        {
            TestStats stats = Create<TestStats>();
            stats.Health = health;
            stats.name = name;
            return stats;
        }

        [TearDown]
        public void TearDown()
        {
            DataReferenceCopies.PlayingOverride = null;
            DataReferenceCopies.DestroyAll();
            foreach (Object obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
        }

        // ---- Missing asset ----

        [Test]
        public void Runtime_NoAsset_LogsOnceAndReturnsNull()
        {
            var reference = new DataReference<TestStats>();

            LogAssert.Expect(LogType.Error, new Regex(@"DataReference<TestStats> has no asset assigned"));
            Assert.IsNull(reference.Runtime);
            Assert.IsNull(reference.Runtime);
            Assert.IsNull(reference.Persistent);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Persistent_NoAsset_LogsOnceAndReturnsNull()
        {
            var reference = new DataReference<TestStats>();

            LogAssert.Expect(LogType.Error, new Regex("has no asset assigned"));
            Assert.IsNull(reference.Persistent);
            Assert.IsNull(reference.Persistent);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Runtime_NoAssetInPlay_LogsOnceAndReturnsNull()
        {
            DataReferenceCopies.PlayingOverride = true;
            var reference = new DataReference<TestStats>();

            LogAssert.Expect(LogType.Error, new Regex("has no asset assigned"));
            Assert.IsNull(reference.Runtime);
            Assert.IsNull(reference.Runtime);
            Assert.AreEqual(0, DataReferenceCopies.Count);
        }

        [Test]
        public void HasAsset_ReportsAssignmentWithoutLogging()
        {
            var reference = new DataReference<TestStats>();
            Assert.IsFalse(reference.HasAsset);

            reference.Persistent = CreateStats();
            Assert.IsTrue(reference.HasAsset);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AssigningAsset_ResetsMissingError()
        {
            var reference = new DataReference<TestStats>();
            LogAssert.Expect(LogType.Error, new Regex("has no asset assigned"));
            Assert.IsNull(reference.Runtime);

            reference.Persistent = CreateStats();
            Assert.IsNotNull(reference.Runtime);

            reference.Persistent = null;
            LogAssert.Expect(LogType.Error, new Regex("has no asset assigned"));
            Assert.IsNull(reference.Runtime);
        }

        [Test]
        public void DestroyedAsset_CountsAsMissing()
        {
            TestStats stats = CreateStats();
            var reference = new DataReference<TestStats>(stats);
            Object.DestroyImmediate(stats);

            Assert.IsFalse(reference.HasAsset);
            LogAssert.Expect(LogType.Error, new Regex("has no asset assigned"));
            Assert.IsNull(reference.Runtime);
        }

        // ---- Edit mode ----

        [Test]
        public void Runtime_OutsidePlayMode_ReturnsTheAsset()
        {
            TestStats stats = CreateStats();
            var reference = new DataReference<TestStats>(stats);

            Assert.AreSame(stats, reference.Runtime);
            Assert.IsNull(reference.RuntimeObject);
            Assert.AreEqual(0, DataReferenceCopies.Count);
        }

        [Test]
        public void ResetRuntime_OutsidePlayMode_DoesNothing()
        {
            TestStats stats = CreateStats();
            var reference = new DataReference<TestStats>(stats);

            reference.ResetRuntime();
            Assert.AreSame(stats, reference.Runtime);
            Assert.IsTrue(stats != null);
        }

        // ---- Copies (Play mode simulated) ----

        [Test]
        public void Runtime_InPlay_IsACopyWithTheAssetsValues()
        {
            DataReferenceCopies.PlayingOverride = true;
            TestStats stats = CreateStats(42, "Player Stats");
            var reference = new DataReference<TestStats>(stats);

            TestStats copy = reference.Runtime;
            Assert.AreNotSame(stats, copy);
            Assert.AreEqual(42, copy.Health);
            Assert.AreEqual("Player Stats (Runtime)", copy.name);
            Assert.AreSame(copy, reference.RuntimeObject);
        }

        [Test]
        public void Runtime_InPlay_ReturnsTheSameCopyEachTime()
        {
            DataReferenceCopies.PlayingOverride = true;
            var reference = new DataReference<TestStats>(CreateStats());

            Assert.AreSame(reference.Runtime, reference.Runtime);
            Assert.AreEqual(1, DataReferenceCopies.Count);
        }

        [Test]
        public void ChangingTheCopy_LeavesTheAssetAlone()
        {
            DataReferenceCopies.PlayingOverride = true;
            TestStats stats = CreateStats(10);
            var reference = new DataReference<TestStats>(stats);

            reference.Runtime.Health = 3;
            Assert.AreEqual(10, stats.Health);
            Assert.AreEqual(3, reference.Runtime.Health);
        }

        [Test]
        public void TwoFieldsOnTheSameAsset_GetSeparateCopies()
        {
            DataReferenceCopies.PlayingOverride = true;
            TestStats stats = CreateStats(10);
            var player = new DataReference<TestStats>(stats);
            var hud = new DataReference<TestStats>(stats);

            player.Runtime.Health = 1;
            Assert.AreNotSame(player.Runtime, hud.Runtime);
            Assert.AreEqual(10, hud.Runtime.Health);
        }

        [Test]
        public void ResetRuntime_DestroysTheCopyAndStartsFresh()
        {
            DataReferenceCopies.PlayingOverride = true;
            var reference = new DataReference<TestStats>(CreateStats(10));
            TestStats first = reference.Runtime;
            first.Health = 1;

            reference.ResetRuntime();
            Assert.IsTrue(first == null, "the old copy should be destroyed");
            Assert.IsNull(reference.RuntimeObject);

            TestStats second = reference.Runtime;
            Assert.AreEqual(10, second.Health);
            Assert.AreEqual(1, DataReferenceCopies.Count);
        }

        [Test]
        public void ChangingTheAsset_ReplacesTheCopy()
        {
            DataReferenceCopies.PlayingOverride = true;
            var reference = new DataReference<TestStats>(CreateStats(10));
            TestStats first = reference.Runtime;

            reference.Persistent = CreateStats(99);
            TestStats second = reference.Runtime;

            Assert.IsTrue(first == null, "the copy of the old asset should be destroyed");
            Assert.AreEqual(99, second.Health);
            Assert.AreEqual(1, DataReferenceCopies.Count);
        }

        [Test]
        public void DestroyedCopy_IsRemade()
        {
            DataReferenceCopies.PlayingOverride = true;
            var reference = new DataReference<TestStats>(CreateStats(10));
            Object.DestroyImmediate(reference.Runtime);

            Assert.IsNotNull(reference.Runtime);
            Assert.AreEqual(10, reference.Runtime.Health);
        }

        [Test]
        public void DestroyAll_DestroysEveryCopy()
        {
            DataReferenceCopies.PlayingOverride = true;
            TestStats stats = CreateStats();
            var a = new DataReference<TestStats>(stats);
            var b = new DataReference<TestStats>(stats);
            TestStats copyA = a.Runtime;
            TestStats copyB = b.Runtime;

            DataReferenceCopies.DestroyAll();

            Assert.IsTrue(copyA == null);
            Assert.IsTrue(copyB == null);
            Assert.IsTrue(stats != null, "the asset itself must survive");
            Assert.AreEqual(0, DataReferenceCopies.Count);
        }

        // ---- Serialization ----

        [Test]
        public void RuntimeCopy_IsNotSerialized()
        {
            DataReferenceCopies.PlayingOverride = true;
            TestHolder holder = Create<TestHolder>();
            holder.Stats.Persistent = CreateStats();
            Assert.IsNotNull(holder.Stats.Runtime);

            string json = EditorJsonUtility.ToJson(holder);
            StringAssert.Contains("_persistent", json);
            StringAssert.DoesNotContain("_runtime", json);
            StringAssert.DoesNotContain("_createNewButton", json);
        }

        [Test]
        public void OldSerializedData_StillLoadsTheAsset()
        {
            // Scenes and assets saved by the old version store the asset under the auto-property's backing
            // field name. The YAML loader honours FormerlySerializedAs (checked by hand with a saved asset);
            // EditorJsonUtility doesn't, so this checks the attribute is in place.
            System.Reflection.FieldInfo field = typeof(DataReference<TestStats>).GetField("_persistent",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field);

            var renames = (UnityEngine.Serialization.FormerlySerializedAsAttribute[])field.GetCustomAttributes(
                typeof(UnityEngine.Serialization.FormerlySerializedAsAttribute), false);
            Assert.That(renames, Has.Some.Matches<UnityEngine.Serialization.FormerlySerializedAsAttribute>(
                a => a.oldName == "<Persistent>k__BackingField"));
        }

        [Test]
        public void CopiedHolder_GetsItsOwnCopy()
        {
            DataReferenceCopies.PlayingOverride = true;
            TestHolder holder = Create<TestHolder>();
            holder.Stats.Persistent = CreateStats();
            TestStats original = holder.Stats.Runtime;

            TestHolder clone = Object.Instantiate(holder);
            _created.Add(clone);

            Assert.IsNull(clone.Stats.RuntimeObject);
            Assert.AreNotSame(original, clone.Stats.Runtime);
        }

        // ---- Drawer helpers ----

        [Test]
        public void GetDataType_HandlesFieldsArraysAndLists()
        {
            Assert.AreEqual(typeof(TestStats), DataReferenceDrawer.GetDataType(typeof(DataReference<TestStats>)));
            Assert.AreEqual(typeof(OtherStats), DataReferenceDrawer.GetDataType(typeof(DataReference<OtherStats>[])));
            Assert.AreEqual(typeof(TestStats), DataReferenceDrawer.GetDataType(typeof(List<DataReference<TestStats>>)));
        }

        [Test]
        public void FindInstance_ReturnsTheLiveObject()
        {
            TestHolder holder = Create<TestHolder>();
            holder.StatsList.Add(new DataReference<TestStats>());
            holder.StatsList.Add(new DataReference<TestStats>());
            holder.OtherArray = new[] { new DataReference<OtherStats>() };

            using var serialized = new SerializedObject(holder);
            Assert.AreSame(holder.Stats, DataReferenceDrawer.FindInstance(serialized.FindProperty("Stats")));
            Assert.AreSame(holder.StatsList[1], DataReferenceDrawer.FindInstance(serialized.FindProperty("StatsList.Array.data[1]")));
            Assert.AreSame(holder.OtherArray[0], DataReferenceDrawer.FindInstance(serialized.FindProperty("OtherArray.Array.data[0]")));
        }

        [Test]
        public void SerializedAssetField_IsNamedPersistent()
        {
            TestHolder holder = Create<TestHolder>();
            TestStats stats = CreateStats();
            using var serialized = new SerializedObject(holder);

            SerializedProperty field = serialized.FindProperty("Stats._persistent");
            Assert.IsNotNull(field);
            field.objectReferenceValue = stats;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreSame(stats, holder.Stats.Persistent);
        }
    }
}
