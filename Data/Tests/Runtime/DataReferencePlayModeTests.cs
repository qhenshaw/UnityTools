using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityTools.Data.PlayModeTests
{
    public class DataReferencePlayModeTests
    {
        private readonly List<Object> _created = new();
        private PlayStats _stats;

        [SetUp]
        public void SetUp()
        {
            _stats = ScriptableObject.CreateInstance<PlayStats>();
            _stats.name = "Enemy Stats";
            _stats.Health = 25;
            _created.Add(_stats);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
            {
                if (obj != null) Object.Destroy(obj);
            }
            _created.Clear();
            DataReferenceCopies.DestroyAll();
        }

        private PlayStatsHolder CreateHolder()
        {
            var go = new GameObject("Holder");
            _created.Add(go);
            PlayStatsHolder holder = go.AddComponent<PlayStatsHolder>();
            holder.Stats.Persistent = _stats;
            return holder;
        }

        [Test]
        public void Runtime_InPlayMode_IsACopy()
        {
            PlayStatsHolder holder = CreateHolder();

            PlayStats copy = holder.Stats.Runtime;
            Assert.AreNotSame(_stats, copy);
            Assert.AreEqual(25, copy.Health);

            copy.Health = 1;
            Assert.AreEqual(25, _stats.Health);
        }

        [Test]
        public void InstantiatedObject_GetsItsOwnCopy()
        {
            PlayStatsHolder original = CreateHolder();
            original.Stats.Runtime.Health = 1;

            PlayStatsHolder clone = Object.Instantiate(original);
            _created.Add(clone.gameObject);

            Assert.AreSame(_stats, clone.Stats.Persistent);
            Assert.AreNotSame(original.Stats.Runtime, clone.Stats.Runtime);
            Assert.AreEqual(25, clone.Stats.Runtime.Health, "the clone's copy comes from the asset, not the original's copy");
        }

        [Test]
        public void InstantiatedObject_DoesNotSerializeTheCopy()
        {
            PlayStatsHolder original = CreateHolder();
            Assert.IsNotNull(original.Stats.Runtime);

            string json = JsonUtility.ToJson(original);
            StringAssert.DoesNotContain("_runtime", json);
        }

        [UnityTest]
        public IEnumerator ResetRuntime_DestroysTheCopy()
        {
            PlayStatsHolder holder = CreateHolder();
            PlayStats first = holder.Stats.Runtime;
            first.Health = 1;

            holder.Stats.ResetRuntime();
            yield return null;

            Assert.IsTrue(first == null, "the old copy should be destroyed by the next frame");
            Assert.AreEqual(25, holder.Stats.Runtime.Health);
        }

        [UnityTest]
        public IEnumerator Copy_OutlivesItsOwner_UntilReset()
        {
            PlayStatsHolder holder = CreateHolder();
            DataReference<PlayStats> reference = holder.Stats;
            PlayStats copy = reference.Runtime;

            Object.Destroy(holder.gameObject);
            yield return null;

            Assert.IsTrue(copy != null, "copies live until Play mode ends or ResetRuntime");
            reference.ResetRuntime();
            yield return null;
            Assert.IsTrue(copy == null);
        }
    }
}
