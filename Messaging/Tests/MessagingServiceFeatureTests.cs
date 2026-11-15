using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using UnityEngine.TestTools.Constraints;
using AllocIs = UnityEngine.TestTools.Constraints.Is;

namespace UnityTools.Messaging.Tests
{
    public class MessagingServiceFeatureTests
    {
        private enum TestMessage { A, B }
        private enum ByteMessage : byte { High = 200 }
        private enum SignedMessage : sbyte { Negative = -5 }

        private MessagingService Service => MessagingService.Instance;

        [SetUp]
        public void SetUp() => MessagingService.ResetForTests();

        [TearDown]
        public void TearDown() => MessagingService.ResetForTests();

        // ---- #4 removed during a send

        [Test]
        public void ListenerRemovedDuringSend_IsSkippedForTheRestOfIt()
        {
            int secondCalls = 0;
            Action<int> second = _ => secondCalls++;
            Service.AddListener<int>(TestMessage.A, _ => Service.RemoveListener(TestMessage.A, second));
            Service.AddListener(TestMessage.A, second);

            Service.Send(TestMessage.A, 1);

            Assert.AreEqual(0, secondCalls);
        }

        [Test]
        public void ListenerAddedDuringSend_WaitsForTheNextSend()
        {
            int lateCalls = 0;
            bool added = false;
            Service.AddListener<int>(TestMessage.A, _ =>
            {
                if (added) return;
                added = true;
                Service.AddListener<int>(TestMessage.A, __ => lateCalls++);
            });

            Service.Send(TestMessage.A, 1);
            Assert.AreEqual(0, lateCalls);

            Service.Send(TestMessage.A, 2);
            Assert.AreEqual(1, lateCalls);
        }

        // ---- #5 destroyed listeners

        [Test]
        public void ListenerOnDestroyedObject_IsRemovedWithOneWarning()
        {
            var target = ScriptableObject.CreateInstance<ListenerTarget>();
            int otherCalls = 0;
            Service.AddListener<int>(TestMessage.A, target.OnMessage);
            Service.AddListener<int>(TestMessage.A, _ => otherCalls++);
            Object.DestroyImmediate(target);

            LogAssert.Expect(LogType.Warning, new Regex("TestMessage.A: removed listener ListenerTarget.OnMessage because its object was destroyed"));
            Service.Send(TestMessage.A, 1);
            Service.Send(TestMessage.A, 2);

            Assert.AreEqual(2, otherCalls);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- #14 no-payload listeners and sends

        [Test]
        public void NoPayloadListener_FiresOnTypedSends()
        {
            int calls = 0;
            Service.AddListener<int>(TestMessage.A, _ => { });
            Service.AddListener(TestMessage.A, () => calls++);

            Service.Send(TestMessage.A, 3);

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void NoPayloadSend_ReachesNoPayloadListeners()
        {
            int calls = 0;
            Service.AddListener(TestMessage.A, () => calls++);

            Service.Send(TestMessage.A);

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void NoPayloadSend_OnATypedTag_LogsErrorAndDeliversNothing()
        {
            int typedCalls = 0, untypedCalls = 0;
            Service.AddListener<int>(TestMessage.A, _ => typedCalls++);
            Service.AddListener(TestMessage.A, () => untypedCalls++);

            LogAssert.Expect(LogType.Error, new Regex("TestMessage.A listeners expect Int32, but Send was called without a payload"));
            Service.Send(TestMessage.A);

            Assert.AreEqual(0, typedCalls);
            Assert.AreEqual(0, untypedCalls);
        }

        [Test]
        public void NoPayloadListener_CanBeRemoved()
        {
            int calls = 0;
            Action listener = () => calls++;
            Service.AddListener(TestMessage.A, listener);
            Service.RemoveListener(TestMessage.A, listener);

            Service.Send(TestMessage.A);

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void WrongTypedSend_DoesNotReachNoPayloadListeners()
        {
            int calls = 0;
            Service.AddListener<int>(TestMessage.A, _ => { });
            Service.AddListener(TestMessage.A, () => calls++);

            LogAssert.Expect(LogType.Error, new Regex("listeners expect Int32, but Send got String"));
            Service.Send(TestMessage.A, "wrong");

            Assert.AreEqual(0, calls);
        }

        // ---- #14 sticky sends

        [Test]
        public void StickySend_IsReplayedToALateListener()
        {
            Service.Send(TestMessage.A, 42, sendToLate: true);

            int received = 0;
            Service.AddListener<int>(TestMessage.A, v => received = v);

            Assert.AreEqual(42, received);
        }

        [Test]
        public void PlainSend_IsNotReplayed()
        {
            Service.AddListener<int>(TestMessage.A, _ => { });
            Service.Send(TestMessage.A, 42);

            int calls = 0;
            Service.AddListener<int>(TestMessage.A, _ => calls++);

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void LaterPlainSend_ReplacesTheRememberedPayload()
        {
            Service.Send(TestMessage.A, 1, sendToLate: true);
            Service.Send(TestMessage.A, 2);

            int received = 0;
            Service.AddListener<int>(TestMessage.A, v => received = v);

            Assert.AreEqual(2, received);
        }

        [Test]
        public void ClearSticky_StopsTheReplay()
        {
            Service.Send(TestMessage.A, 1, sendToLate: true);
            Service.ClearSticky(TestMessage.A);

            int calls = 0;
            Service.AddListener<int>(TestMessage.A, _ => calls++);

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void StickySend_IsReplayedToALateNoPayloadListener()
        {
            Service.Send(TestMessage.A, 1, sendToLate: true);

            int calls = 0;
            Service.AddListener(TestMessage.A, () => calls++);

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void StickyDestroyedObject_IsNotReplayed()
        {
            var go = new GameObject("StickyTest");
            Service.Send(TestMessage.A, go, sendToLate: true);
            Object.DestroyImmediate(go);

            int calls = 0;
            Service.AddListener<GameObject>(TestMessage.A, _ => calls++);

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void StickySend_ReachesCurrentListenersToo()
        {
            int calls = 0;
            Service.AddListener<int>(TestMessage.A, _ => calls++);

            Service.Send(TestMessage.A, 1, sendToLate: true);

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void StickyPayload_ResetsWithTheService()
        {
            Service.Send(TestMessage.A, 1, sendToLate: true);
            MessagingService.ResetForTests();

            int calls = 0;
            Service.AddListener<int>(TestMessage.A, _ => calls++);

            Assert.AreEqual(0, calls);
        }

        // ---- #6 boxing-free generic overloads

        [Test]
        public void GenericAndEnumOverloads_UseTheSameTag()
        {
            int received = 0;
            Service.AddListener<int>(TestMessage.B, v => received = v);   // Enum overload (explicit type argument)

            Enum boxed = TestMessage.B;
            Service.Send(boxed, 5);                                        // Enum overload
            Assert.AreEqual(5, received);

            Service.Send(TestMessage.B, 6);                                // generic overload
            Assert.AreEqual(6, received);
        }

        [Test]
        public void GenericAndEnumOverloads_AgreeForByteAndSignedEnums()
        {
            int byteValue = 0, signedValue = 0;
            Service.AddListener<int>((Enum)ByteMessage.High, v => byteValue = v);
            Service.AddListener<int>((Enum)SignedMessage.Negative, v => signedValue = v);

            Service.Send(ByteMessage.High, 1);
            Service.Send(SignedMessage.Negative, 2);

            Assert.AreEqual(1, byteValue);
            Assert.AreEqual(2, signedValue);
        }

        [Test]
        public void GenericSend_DoesNotAllocate()
        {
            int sum = 0;
            Service.AddListener<int>(TestMessage.A, v => sum += v);
            Service.AddListener<int>(TestMessage.A, v => sum -= v);
            Service.AddListener(TestMessage.A, () => sum++);
            Service.Send(TestMessage.A, 1); // warm up pools and static caches

            Assert.That(() => Service.Send(TestMessage.A, 1), AllocIs.Not.AllocatingGCMemory());
        }
    }
}
