using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using AllocIs = UnityEngine.TestTools.Constraints.Is;

namespace UnityTools.Messaging.Tests
{
    public class GlobalVariableFeatureTests
    {
        private enum TestVariable { Score, Lives, Player }

        [SetUp]
        public void SetUp() => GlobalVariableService.ResetForTests();

        [TearDown]
        public void TearDown() => GlobalVariableService.ResetForTests();

        private GlobalVariableService Service => GlobalVariableService.Instance;

        // ---- #14 Remove / Clear

        [Test]
        public void Remove_ClearsTheValueAndNotifiesWithDefault()
        {
            var score = new GlobalVariable<int>(TestVariable.Score);
            score.Value = 5;
            var received = new List<int>();
            score.OnUpdated += (_, v) => received.Add(v);
            object changedValue = "not called";
            Service.ValueChanged += (_, v) => changedValue = v;

            Service.Remove(TestVariable.Score);

            Assert.IsFalse(score.HasValue);
            Assert.IsFalse(Service.HasValue(TestVariable.Score));
            CollectionAssert.AreEqual(new[] { 0 }, received);
            Assert.IsNull(changedValue);
        }

        [Test]
        public void Remove_OfAnUnsetTag_DoesNothing()
        {
            int calls = 0;
            Service.ValueChanged += (_, _) => calls++;

            Service.Remove(TestVariable.Score);

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Clear_RemovesEveryValueAndNotifiesEachTag()
        {
            var score = new GlobalVariable<int>(TestVariable.Score) { Value = 1 };
            var player = new GlobalVariable<GameObject>(TestVariable.Player);
            var go = new GameObject("ClearTest");
            try
            {
                player.Value = go;
                int scoreCalls = 0, playerCalls = 0;
                score.OnUpdated += (_, _) => scoreCalls++;
                player.OnUpdated += (_, v) => { if (v == null) playerCalls++; };

                Service.Clear();

                Assert.IsFalse(score.HasValue);
                Assert.IsFalse(player.HasValue);
                Assert.AreEqual(1, scoreCalls);
                Assert.AreEqual(1, playerCalls);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SetAfterRemove_WorksAgain()
        {
            var score = new GlobalVariable<int>(TestVariable.Score) { Value = 1 };
            Service.Remove(TestVariable.Score);

            score.Value = 4;

            Assert.IsTrue(score.HasValue);
            Assert.AreEqual(4, score.Value);
        }

        // ---- #6 boxing-free paths

        [Test]
        public void GlobalVariable_ValueGetAndSet_DoNotAllocate()
        {
            var score = new GlobalVariable<int>(TestVariable.Score);
            int seen = 0;
            score.OnUpdated += (_, v) => seen = v;
            score.Value = 1; // warm up
            _ = score.Value;

            Assert.That(() =>
            {
                score.Value = score.Value + 1;
            }, AllocIs.Not.AllocatingGCMemory());
            Assert.AreEqual(2, seen);
        }

        [Test]
        public void GenericServiceSetAndHasValue_DoNotAllocate()
        {
            Service.Set(TestVariable.Lives, 3); // warm up
            Assert.That(() =>
            {
                Service.Set(TestVariable.Lives, 2);
                Service.HasValue(TestVariable.Lives);
            }, AllocIs.Not.AllocatingGCMemory());
        }

        [Test]
        public void GenericAndEnumOverloads_UseTheSameTag()
        {
            Service.Set(TestVariable.Lives, 9);         // generic overload
            Enum boxed = TestVariable.Lives;

            Assert.IsTrue(Service.HasValue(boxed));     // Enum overload
            Assert.AreEqual(9, Service.Get<int>(boxed));
        }

        [Test]
        public void ReadingAsABaseType_Works()
        {
            var go = new GameObject("BaseTypeTest");
            try
            {
                Service.Set(TestVariable.Player, go);
                Assert.AreSame(go, Service.Get<UnityEngine.Object>(TestVariable.Player));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ---- debug info

        [Test]
        public void DebugInfo_ListsVariables()
        {
            Service.Set(TestVariable.Score, 12);
            var info = new List<GlobalVariableService.VariableInfo>();

            Service.CollectDebugInfo(info);

            Assert.AreEqual(1, info.Count);
            Assert.AreEqual("TestVariable.Score", info[0].Tag);
            Assert.AreEqual("Int32", info[0].ValueType);
            Assert.AreEqual("12", info[0].Value);
        }

        [Test]
        public void MessagingDebugInfo_ListsTagsListenersAndSticky()
        {
            MessagingService.ResetForTests();
            try
            {
                MessagingService.Instance.AddListener<int>(TestVariable.Score, v => { });
                MessagingService.Instance.AddListener(TestVariable.Score, () => { });
                MessagingService.Instance.Send(TestVariable.Score, 7, sendToLate: true);
                var info = new List<MessagingService.TagInfo>();

                MessagingService.Instance.CollectDebugInfo(info);

                Assert.AreEqual(1, info.Count);
                Assert.AreEqual("TestVariable.Score", info[0].Tag);
                Assert.AreEqual("Int32", info[0].PayloadType);
                Assert.AreEqual(2, info[0].Listeners.Count);
                StringAssert.EndsWith("(no payload)", info[0].Listeners[1]);
                Assert.IsTrue(info[0].HasSticky);
                Assert.AreEqual("7", info[0].StickyPayload);
            }
            finally
            {
                MessagingService.ResetForTests();
            }
        }
    }
}
