using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UnityTools.Messaging.Tests
{
    public class GlobalVariableTests
    {
        private enum TestVariable { Score, Player, Name }

        private bool _wasPlaying;

        [SetUp]
        public void SetUp()
        {
            _wasPlaying = GlobalVariableService.IsPlaying;
            GlobalVariableService.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            GlobalVariableService.IsPlaying = _wasPlaying;
            GlobalVariableService.ResetForTests();
        }

        [Test]
        public void Instance_IsAvailableInEditMode()
        {
            Assert.IsNotNull(GlobalVariableService.Instance);
        }

        [Test]
        public void Value_IsSharedBetweenInstances()
        {
            var a = new GlobalVariable<int>(TestVariable.Score);
            var b = new GlobalVariable<int>(TestVariable.Score);

            a.Value = 7;

            Assert.AreEqual(7, b.Value);
        }

        [Test]
        public void HasValue_IsFalseWhenUnset()
        {
            Assert.IsFalse(new GlobalVariable<GameObject>(TestVariable.Player).HasValue);
        }

        [Test]
        public void HasValue_IsFalseForNullWithoutThrowing()
        {
            var player = new GlobalVariable<GameObject>(TestVariable.Player);
            player.Value = null;

            bool hasValue = true;
            Assert.DoesNotThrow(() => hasValue = player.HasValue);
            Assert.IsFalse(hasValue);
        }

        [Test]
        public void HasValue_IsFalseForADestroyedObject()
        {
            var player = new GlobalVariable<GameObject>(TestVariable.Player);
            var go = new GameObject("GlobalVariableTest");
            player.Value = go;
            Assert.IsTrue(player.HasValue);

            Object.DestroyImmediate(go);

            Assert.IsFalse(player.HasValue);
        }

        [Test]
        public void HasValue_IsTrueForValueTypes()
        {
            var score = new GlobalVariable<int>(TestVariable.Score);
            score.Value = 0;

            Assert.IsTrue(score.HasValue);
        }

        [Test]
        public void OnUpdated_FiresOnEveryInstanceWithTheTag()
        {
            var setter = new GlobalVariable<int>(TestVariable.Score);
            var listener = new GlobalVariable<int>(TestVariable.Score);
            var received = new List<(object sender, int value)>();
            listener.OnUpdated += (sender, value) => received.Add((sender, value));

            setter.Value = 3;

            Assert.AreEqual(1, received.Count);
            Assert.AreSame(listener, received[0].sender);
            Assert.AreEqual(3, received[0].value);
        }

        [Test]
        public void OnUpdated_FiresOnceOnTheSettingInstance()
        {
            var score = new GlobalVariable<int>(TestVariable.Score);
            int calls = 0;
            score.OnUpdated += (_, _) => calls++;

            score.Value = 1;

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void OnUpdated_FiresWhenSetThroughTheService()
        {
            var score = new GlobalVariable<int>(TestVariable.Score);
            int received = -1;
            score.OnUpdated += (_, value) => received = value;

            GlobalVariableService.Instance.Set(TestVariable.Score, 9);

            Assert.AreEqual(9, received);
        }

        [Test]
        public void OnUpdated_StopsAfterTheLastHandlerIsRemoved()
        {
            var score = new GlobalVariable<int>(TestVariable.Score);
            int calls = 0;
            EventHandler<int> handler = (_, _) => calls++;
            score.OnUpdated += handler;
            score.OnUpdated -= handler;

            GlobalVariableService.Instance.Set(TestVariable.Score, 1);

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void OnUpdated_HandlerException_IsLoggedAndOthersStillRun()
        {
            var a = new GlobalVariable<int>(TestVariable.Score);
            var b = new GlobalVariable<int>(TestVariable.Score);
            int calls = 0;
            a.OnUpdated += (_, _) => throw new InvalidOperationException("boom");
            b.OnUpdated += (_, _) => calls++;

            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: boom"));
            GlobalVariableService.Instance.Set(TestVariable.Score, 1);

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void ValueChanged_ReportsTagAndValue()
        {
            Enum tag = null;
            object value = null;
            GlobalVariableService.Instance.ValueChanged += (t, v) => { tag = t; value = v; };

            new GlobalVariable<string>(TestVariable.Name).Value = "Ada";

            Assert.AreEqual(TestVariable.Name, tag);
            Assert.AreEqual("Ada", value);
        }

        [Test]
        public void Constructor_InPlay_SetsStartingValueOnlyWhenUnset()
        {
            GlobalVariableService.IsPlaying = true;
            var first = new GlobalVariable<int>(TestVariable.Score, 10);
            first.Value = 25;

            var lateSpawned = new GlobalVariable<int>(TestVariable.Score, 10);

            Assert.AreEqual(25, lateSpawned.Value);
        }

        [Test]
        public void Constructor_InPlay_StartingValueIsUsedWhenUnset()
        {
            GlobalVariableService.IsPlaying = true;
            var score = new GlobalVariable<int>(TestVariable.Score, 10);

            Assert.IsTrue(score.HasValue);
            Assert.AreEqual(10, score.Value);
        }

        [Test]
        public void Constructor_InPlay_StartingValueRaisesNoEvents()
        {
            GlobalVariableService.IsPlaying = true;
            int calls = 0;
            GlobalVariableService.Instance.ValueChanged += (_, _) => calls++;

            new GlobalVariable<int>(TestVariable.Score, 10);

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Constructor_InEditMode_WritesNothing()
        {
            GlobalVariableService.IsPlaying = false;

            Assert.DoesNotThrow(() => new GlobalVariable<int>(TestVariable.Score, 10));
            Assert.IsFalse(GlobalVariableService.Instance.HasValue(TestVariable.Score));
        }

        [Test]
        public void Get_WrongType_LogsErrorAndReturnsDefault()
        {
            GlobalVariableService.Instance.Set(TestVariable.Score, 5);

            LogAssert.Expect(LogType.Error, new Regex("TestVariable.Score holds Int32, but was read as String"));
            string value = GlobalVariableService.Instance.Get<string>(TestVariable.Score);

            Assert.IsNull(value);
        }
    }
}
