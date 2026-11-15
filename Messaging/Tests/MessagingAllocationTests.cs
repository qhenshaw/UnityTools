using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using AllocIs = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace UnityTools.Messaging.Tests
{
    // Each common way of sending is checked for GC allocations after a warm-up send.
    public class MessagingAllocationTests
    {
        private enum TestMessage { A, B, Unused }
        private struct DamageInfo { public float Amount; public Vector3 Point; }

        private MessagingService Service => MessagingService.Instance;
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            MessagingService.ResetForTests();
            _go = new GameObject("AllocTest");
        }

        [TearDown]
        public void TearDown()
        {
            MessagingService.ResetForTests();
            Object.DestroyImmediate(_go);
        }

        private static void AssertNoAlloc(TestDelegate send)
        {
            send(); // warm up pools and static caches
            send();
            Assert.That(send, AllocIs.Not.AllocatingGCMemory());
        }

        [Test]
        public void StructPayload()
        {
            Service.AddListener<DamageInfo>(TestMessage.A, d => { });
            var info = new DamageInfo { Amount = 3 };
            AssertNoAlloc(() => Service.Send(TestMessage.A, info));
        }

        [Test]
        public void ReferencePayload()
        {
            Service.AddListener<GameObject>(TestMessage.A, g => { });
            GameObject go = _go;
            AssertNoAlloc(() => Service.Send(TestMessage.A, go));
        }

        [Test]
        public void DerivedReferencePayload()
        {
            Service.AddListener<Component>(TestMessage.A, c => { });
            Transform transform = _go.transform;
            AssertNoAlloc(() => Service.Send(TestMessage.A, transform));
        }

        // Send<int>(tag, 1) picks the Enum overload, so the compiler boxes the tag at the call site (unavoidable).
        // Naming both type arguments, or none, uses the generic overload instead.
        [Test]
        public void BothTypeArgumentsNamed()
        {
            Service.AddListener<int>(TestMessage.A, v => { });
            AssertNoAlloc(() => Service.Send<TestMessage, int>(TestMessage.A, 1));
        }

        [Test]
        public void BoxedTagReading_MatchesTheGenericPath()
        {
            Assert.AreEqual(TagKey.From(TestMessage.B), TagKey.From((Enum)TestMessage.B));
            Assert.AreEqual(TagKey.From(SignedTag.Negative), TagKey.From((Enum)SignedTag.Negative));
            Assert.AreEqual(TagKey.From(ULongTag.Huge), TagKey.From((Enum)ULongTag.Huge));
            Assert.AreEqual(-7, TagKey.From((Enum)SignedTag.Negative).Value);
        }

        private enum SignedTag : sbyte { Negative = -7 }
        private enum ULongTag : ulong { Huge = ulong.MaxValue - 1 }

        [Test]
        public void TagHeldAsEnum()
        {
            Service.AddListener<int>(TestMessage.A, v => { });
            Enum tag = TestMessage.A;
            AssertNoAlloc(() => Service.Send(tag, 1));
        }

        [Test]
        public void NoPayloadSend()
        {
            Service.AddListener(TestMessage.A, () => { });
            AssertNoAlloc(() => Service.Send(TestMessage.A));
        }

        [Test]
        public void NoPayloadListenerOnTypedSend()
        {
            Service.AddListener(TestMessage.A, () => { });
            AssertNoAlloc(() => Service.Send(TestMessage.A, 1));
        }

        [Test]
        public void TagWithNoListeners()
        {
            AssertNoAlloc(() => Service.Send(TestMessage.Unused, 1));
        }

        [Test]
        public void StickyStructPayload()
        {
            Service.AddListener<DamageInfo>(TestMessage.A, d => { });
            var info = new DamageInfo { Amount = 3 };
            AssertNoAlloc(() => Service.Send(TestMessage.A, info, sendToLate: true));
        }

        [Test]
        public void StickyReferencePayload()
        {
            Service.AddListener<GameObject>(TestMessage.A, g => { });
            GameObject go = _go;
            AssertNoAlloc(() => Service.Send(TestMessage.A, go, sendToLate: true));
        }

        [Test]
        public void NestedSend()
        {
            Service.AddListener<int>(TestMessage.B, v => { });
            Service.AddListener<int>(TestMessage.A, v => Service.Send(TestMessage.B, v));
            AssertNoAlloc(() => Service.Send(TestMessage.A, 1));
        }

        [Test]
        public void AddAndRemoveListener()
        {
            Action<int> listener = v => { };
            Service.AddListener(TestMessage.A, listener);
            Service.RemoveListener(TestMessage.A, listener);
            AssertNoAlloc(() =>
            {
                Service.AddListener(TestMessage.A, listener);
                Service.RemoveListener(TestMessage.A, listener);
            });
        }
    }
}
