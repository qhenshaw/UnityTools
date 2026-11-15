using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityTools.Messaging.Tests
{
    public class MessagingServiceTests
    {
        private enum TestMessage { A, B }

        private MessagingService Service => MessagingService.Instance;

        [SetUp]
        public void SetUp() => MessagingService.ResetForTests();

        [TearDown]
        public void TearDown() => MessagingService.ResetForTests();

        [Test]
        public void Instance_IsAvailableInEditMode()
        {
            Assert.IsNotNull(MessagingService.Instance);
        }

        [Test]
        public void Send_ReachesEveryListener()
        {
            var received = new List<int>();
            Service.AddListener<int>(TestMessage.A, v => received.Add(v));
            Service.AddListener<int>(TestMessage.A, v => received.Add(v * 10));

            Service.Send(TestMessage.A, 2);

            CollectionAssert.AreEqual(new[] { 2, 20 }, received);
        }

        [Test]
        public void Send_OnlyReachesItsOwnTag()
        {
            int calls = 0;
            Service.AddListener<int>(TestMessage.A, _ => calls++);

            Service.Send(TestMessage.B, 1);

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Send_WrongPayloadType_LogsOneErrorAndDeliversNothing()
        {
            int calls = 0;
            Service.AddListener<int>(TestMessage.A, _ => calls++);
            Service.AddListener<int>(TestMessage.A, _ => calls++);

            LogAssert.Expect(LogType.Error, new Regex("TestMessage.A listeners expect Int32, but Send got String"));
            Service.Send(TestMessage.A, "wrong");

            Assert.AreEqual(0, calls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Send_NullPayload_ReachesReferenceTypeListeners()
        {
            bool called = false;
            Service.AddListener<GameObject>(TestMessage.A, go => called = go == null);

            Service.Send<GameObject>(TestMessage.A, null);

            Assert.IsTrue(called);
        }

        [Test]
        public void Send_NullPayload_ForValueTypeTag_LogsError()
        {
            Service.AddListener<int>(TestMessage.A, _ => { });

            LogAssert.Expect(LogType.Error, new Regex("listeners expect Int32, but Send got null"));
            Service.Send<string>(TestMessage.A, null);
        }

        [Test]
        public void Send_DerivedPayload_ReachesBaseTypeListener()
        {
            object received = null;
            Service.AddListener<object>(TestMessage.A, v => received = v);

            Service.Send(TestMessage.A, "derived");

            Assert.AreEqual("derived", received);
        }

        [Test]
        public void Send_LooselyTypedPayload_ReachesListenerWhenRuntimeTypeFits()
        {
            string received = null;
            Service.AddListener<string>(TestMessage.A, v => received = v);

            Service.Send<object>(TestMessage.A, "fits");

            Assert.AreEqual("fits", received);
        }

        [Test]
        public void AddListener_DifferentType_LogsErrorAndIsNotAdded()
        {
            int intCalls = 0, stringCalls = 0;
            Service.AddListener<int>(TestMessage.A, _ => intCalls++);

            LogAssert.Expect(LogType.Error, new Regex("TestMessage.A already has listeners for Int32.*for String was not added"));
            Service.AddListener<string>(TestMessage.A, _ => stringCalls++);

            Service.Send(TestMessage.A, 5);
            Assert.AreEqual(1, intCalls);
            Assert.AreEqual(0, stringCalls);
        }

        [Test]
        public void AddListener_AfterAllListenersRemoved_CanUseANewType()
        {
            Action<int> intListener = _ => { };
            Service.AddListener(TestMessage.A, intListener);
            Service.RemoveListener(TestMessage.A, intListener);

            string received = null;
            Service.AddListener<string>(TestMessage.A, v => received = v);
            Service.Send(TestMessage.A, "retyped");

            Assert.AreEqual("retyped", received);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ListenerException_IsLoggedAndOtherListenersStillRun()
        {
            int calls = 0;
            Service.AddListener<int>(TestMessage.A, _ => throw new InvalidOperationException("boom"));
            Service.AddListener<int>(TestMessage.A, _ => calls++);

            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: boom"));
            Service.Send(TestMessage.A, 1);

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void RemoveListener_StopsDelivery()
        {
            int calls = 0;
            Action<int> listener = _ => calls++;
            Service.AddListener(TestMessage.A, listener);
            Service.RemoveListener(TestMessage.A, listener);

            Service.Send(TestMessage.A, 1);

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Listener_CanSendAgainDuringASend()
        {
            var received = new List<int>();
            Service.AddListener<int>(TestMessage.A, v =>
            {
                received.Add(v);
                if (v < 3) Service.Send(TestMessage.A, v + 1);
            });
            Service.AddListener<int>(TestMessage.A, v => received.Add(-v));

            Service.Send(TestMessage.A, 1);

            CollectionAssert.AreEqual(new[] { 1, 2, 3, -3, -2, -1 }, received);
        }
    }
}
