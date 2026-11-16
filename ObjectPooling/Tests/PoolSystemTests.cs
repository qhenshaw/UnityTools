using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ObjectPooling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace UnityTools.ObjectPooling.Tests
{
    public class PoolSystemTests
    {
        private static readonly Regex DestroyedWhilePooled = new Regex("destroyed while pooled");

        private readonly List<GameObject> _created = new List<GameObject>();
        private PooledTestObject _prefab;

        private PoolSystem Pools => PoolSystem.Instance;

        [SetUp]
        public void SetUp()
        {
            // A scene object stands in for the prefab asset; it sits away from the origin so placement is checked.
            _prefab = Track(new GameObject("Bullet")).AddComponent<PooledTestObject>();
            _prefab.transform.SetPositionAndRotation(new Vector3(100f, 0f, 0f), Quaternion.Euler(0f, 45f, 0f));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
#if UNITY_6000_5_OR_NEWER
            PoolSystem[] systems = Object.FindObjectsByType<PoolSystem>(FindObjectsInactive.Include);
#else
            PoolSystem[] systems = Object.FindObjectsByType<PoolSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#endif
            foreach (PoolSystem system in systems)
            {
                Object.Destroy(system.gameObject);
            }
            foreach (GameObject go in _created)
            {
                if (go != null) Object.Destroy(go);
            }
            _created.Clear();
            yield return null;
            Assert.IsFalse(PoolSystem.HasInstance, "PoolSystem.Instance should clear when its object is destroyed");
        }

        private GameObject Track(GameObject go)
        {
            _created.Add(go);
            return go;
        }

        // ---------- Get ----------

        [Test]
        public void Get_ReturnsTypedInstance_NotThePrefab()
        {
            PooledTestObject bullet = Pools.Get(_prefab, Vector3.zero);

            Assert.IsNotNull(bullet);
            Assert.AreNotSame(_prefab, bullet);
            Assert.IsTrue(bullet.gameObject.activeInHierarchy);
            Assert.IsFalse(bullet.IsInPool);
        }

        [Test]
        public void Get_PlacesObjectBeforeAwakeAndOnEnable()
        {
            Vector3 position = new Vector3(1f, 2f, 3f);
            Quaternion rotation = Quaternion.Euler(0f, 90f, 0f);

            PooledTestObject bullet = Pools.Get(_prefab, position, rotation);

            CollectionAssert.AreEqual(new[] { "Awake", "OnEnable", "Taken" }, bullet.Events);
            foreach (Vector3 p in bullet.EventPositions) Assert.AreEqual(position, p);
            Assert.AreEqual(position, bullet.transform.position);
            Assert.That(Quaternion.Angle(rotation, bullet.transform.rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void Get_WithoutPosition_UsesPrefabPositionAndRotation()
        {
            PooledTestObject bullet = Pools.Get(_prefab);

            Assert.AreEqual(_prefab.transform.position, bullet.transform.position);
            Assert.That(Quaternion.Angle(_prefab.transform.rotation, bullet.transform.rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void Get_WithParent_ParentsAndPlacesInWorldSpace()
        {
            Transform parent = Track(new GameObject("Parent")).transform;
            parent.SetPositionAndRotation(new Vector3(5f, 0f, 0f), Quaternion.Euler(0f, 30f, 0f));
            Vector3 position = new Vector3(1f, 1f, 1f);

            PooledTestObject bullet = Pools.Get(_prefab, position, Quaternion.identity, parent);

            Assert.AreSame(parent, bullet.transform.parent);
            Assert.That(Vector3.Distance(position, bullet.transform.position), Is.LessThan(0.001f));
        }

        [Test]
        public void Get_NullPrefab_ThrowsWithHelpfulMessage()
        {
            PooledTestObject missing = null;
            var exception = Assert.Throws<System.ArgumentNullException>(() => Pools.Get(missing, Vector3.zero));
            StringAssert.Contains("Inspector", exception.Message);
        }

        [Test]
        public void ObsoleteGet_StillWorks()
        {
#pragma warning disable CS0618
            IPoolable pooled = Pools.Get((IPoolable)_prefab, new Vector3(2f, 0f, 0f));
#pragma warning restore CS0618

            Assert.IsInstanceOf<PooledTestObject>(pooled);
            Assert.AreEqual(new Vector3(2f, 0f, 0f), pooled.Transform.position);
        }

        // ---------- Return and reuse ----------

        [Test]
        public void ReturnToPool_DeactivatesAndCallsHookWhileActive()
        {
            PooledTestObject bullet = Pools.Get(_prefab, Vector3.zero);
            bullet.Events.Clear();

            bullet.ReturnToPool();

            CollectionAssert.AreEqual(new[] { "Returned", "OnDisable" }, bullet.Events);
            Assert.IsTrue(bullet.IsInPool);
            Assert.IsFalse(bullet.gameObject.activeSelf);
            Assert.AreEqual(1, Pools.CountInactive(_prefab));
        }

        [Test]
        public void ReturnToPool_Twice_IsIgnored()
        {
            PooledTestObject bullet = Pools.Get(_prefab, Vector3.zero);
            bullet.ReturnToPool();
            bullet.Events.Clear();

            bullet.ReturnToPool();

            CollectionAssert.IsEmpty(bullet.Events);
            Assert.AreEqual(1, Pools.CountInactive(_prefab));
        }

        [Test]
        public void ReturnedObject_IsReused_AndPlacedBeforeOnEnable()
        {
            PooledTestObject first = Pools.Get(_prefab, Vector3.zero);
            first.ReturnToPool();
            first.Events.Clear();
            first.EventPositions.Clear();
            Vector3 position = new Vector3(7f, 0f, 7f);

            PooledTestObject second = Pools.Get(_prefab, position);

            Assert.AreSame(first, second);
            CollectionAssert.AreEqual(new[] { "OnEnable", "Taken" }, second.Events);
            foreach (Vector3 p in second.EventPositions) Assert.AreEqual(position, p);
        }

        [Test]
        public void ReturnToPool_UnparentsFromGivenParent()
        {
            Transform parent = Track(new GameObject("Parent")).transform;
            PooledTestObject bullet = Pools.Get(_prefab, Vector3.zero, Quaternion.identity, parent);

            bullet.ReturnToPool();

            Assert.AreEqual(0, parent.childCount);
            Assert.IsTrue(bullet.transform.IsChildOf(Pools.transform));
        }

        [UnityTest]
        public IEnumerator ReturnToPool_OnObjectNotFromAPool_DestroysIt()
        {
            PooledTestObject placed = Object.Instantiate(_prefab);
            Track(placed.gameObject);

            placed.ReturnToPool();
            yield return null;

            Assert.IsTrue(placed == null);
        }

        // ---------- Sizes ----------

        [Test]
        public void FirstGet_PrewarmsWithoutWakingObjects()
        {
            _prefab.PrewarmCount = 3;

            Pools.Get(_prefab, Vector3.zero);

            Assert.AreEqual(2, Pools.CountInactive(_prefab));
            foreach (PooledTestObject waiting in Pools.GetComponentsInChildren<PooledTestObject>(true))
            {
                if (waiting.gameObject.activeSelf) continue;
                CollectionAssert.IsEmpty(waiting.Events, "A prewarmed object shouldn't get Awake, OnEnable or pool hooks");
            }
        }

        [Test]
        public void Prewarm_IsCappedAtMaxSize()
        {
            _prefab.PrewarmCount = 10;
            _prefab.MaxSize = 4;

            Pools.Prewarm(_prefab);

            Assert.AreEqual(4, Pools.CountInactive(_prefab));
        }

        [Test]
        public void Prewarm_WithCount_FillsToCount()
        {
            Pools.Prewarm(_prefab, 5);

            Assert.AreEqual(5, Pools.CountInactive(_prefab));
        }

        [UnityTest]
        public IEnumerator Return_BeyondMaxSize_DestroysExtrasWithoutError()
        {
            _prefab.MaxSize = 2;
            var bullets = new List<PooledTestObject>();
            for (int i = 0; i < 3; i++) bullets.Add(Pools.Get(_prefab, Vector3.zero));

            foreach (PooledTestObject bullet in bullets) bullet.ReturnToPool();
            yield return null;

            Assert.AreEqual(2, Pools.CountInactive(_prefab));
            Assert.AreEqual(1, bullets.FindAll(b => b == null).Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ZeroMaxSize_IsUnlimited()
        {
            var bullets = new List<PooledTestObject>();
            for (int i = 0; i < 50; i++) bullets.Add(Pools.Get(_prefab, Vector3.zero));

            foreach (PooledTestObject bullet in bullets) bullet.ReturnToPool();

            Assert.AreEqual(50, Pools.CountInactive(_prefab));
        }

        // ---------- Delayed return ----------

        [UnityTest]
        public IEnumerator ReturnToPoolDelay_ReturnsAfterDelay()
        {
            PooledTestObject bullet = Pools.Get(_prefab, Vector3.zero);

            bullet.ReturnToPool(0.3f);
            yield return new WaitForSeconds(0.1f);
            Assert.IsFalse(bullet.IsInPool, "returned too early");
            yield return new WaitForSeconds(0.4f);

            Assert.IsTrue(bullet.IsInPool);
        }

        [UnityTest]
        public IEnumerator ReturnToPoolDelay_IsCancelledWhenReturnedFirst()
        {
            PooledTestObject bullet = Pools.Get(_prefab, Vector3.zero);
            bullet.ReturnToPool(0.2f);
            bullet.ReturnToPool();

            PooledTestObject reused = Pools.Get(_prefab, Vector3.zero);
            Assert.AreSame(bullet, reused);
            yield return new WaitForSeconds(0.4f);

            Assert.IsFalse(reused.IsInPool, "the old timer returned a reused object");
        }

        [UnityTest]
        public IEnumerator ReturnToPoolDelay_CalledAgain_RestartsTimer()
        {
            PooledTestObject bullet = Pools.Get(_prefab, Vector3.zero);
            bullet.ReturnToPool(0.2f);
            yield return new WaitForSeconds(0.1f);

            bullet.ReturnToPool(0.4f);
            yield return new WaitForSeconds(0.25f);
            Assert.IsFalse(bullet.IsInPool, "the first timer still fired");
            yield return new WaitForSeconds(0.35f);

            Assert.IsTrue(bullet.IsInPool);
        }

        [Test]
        public void ReturnToPoolDelay_ZeroDelay_ReturnsNow()
        {
            PooledTestObject bullet = Pools.Get(_prefab, Vector3.zero);

            bullet.ReturnToPool(0f);

            Assert.IsTrue(bullet.IsInPool);
        }

        // ---------- Destroy() misuse ----------

        [UnityTest]
        public IEnumerator DestroyingPooledObject_LogsErrorOncePerPrefab()
        {
            PooledTestObject first = Pools.Get(_prefab, Vector3.zero);
            PooledTestObject second = Pools.Get(_prefab, Vector3.zero);

            LogAssert.Expect(LogType.Error, DestroyedWhilePooled);
            Object.Destroy(first.gameObject);
            yield return null;
            Object.Destroy(second.gameObject);
            yield return null;

            LogAssert.NoUnexpectedReceived();
            Assert.IsNotNull(Pools.Get(_prefab, Vector3.zero));
        }

        [UnityTest]
        public IEnumerator DestroyingParent_OfPooledObject_LogsError()
        {
            Transform enemy = new GameObject("Enemy").transform;
            Pools.Get(_prefab, Vector3.zero, Quaternion.identity, enemy);

            LogAssert.Expect(LogType.Error, DestroyedWhilePooled);
            Object.Destroy(enemy.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ObjectDestroyedWhileInPool_IsSkippedByGet()
        {
            PooledTestObject bullet = Pools.Get(_prefab, Vector3.zero);
            bullet.ReturnToPool();

            LogAssert.Expect(LogType.Error, DestroyedWhilePooled);
            Object.Destroy(bullet.gameObject);
            yield return null;

            PooledTestObject fresh = Pools.Get(_prefab, Vector3.zero);
            Assert.IsTrue(fresh != null);
            Assert.AreNotSame(bullet, fresh);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------- PoolSystem lifetime ----------

        [UnityTest]
        public IEnumerator DestroyingPoolSystem_WithObjectsInUse_LogsNothing()
        {
            Pools.Get(_prefab, Vector3.zero);
            Pools.Get(_prefab, Vector3.zero).ReturnToPool();

            Object.Destroy(Pools.gameObject);
            yield return null;

            Assert.IsFalse(PoolSystem.HasInstance);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator UnloadingScene_DestroysPools_AndANewPoolSystemIsCreated()
        {
            Scene original = SceneManager.GetActiveScene();
            Scene level = SceneManager.CreateScene("PoolTestLevel");
            SceneManager.SetActiveScene(level);

            PoolSystem levelPools = Pools;
            Assert.AreEqual(level, levelPools.gameObject.scene);
            Pools.Get(_prefab, Vector3.zero);
            Pools.Get(_prefab, Vector3.zero).ReturnToPool();

            SceneManager.SetActiveScene(original);
            yield return SceneManager.UnloadSceneAsync(level);

            Assert.IsFalse(PoolSystem.HasInstance);
            PooledTestObject bullet = Pools.Get(_prefab, Vector3.zero);
            Assert.IsNotNull(bullet);
            Assert.AreEqual(original, Pools.gameObject.scene);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ReturnToPool_AfterPoolSceneUnloaded_DestroysObjectSilently()
        {
            Scene original = SceneManager.GetActiveScene();
            Scene level = SceneManager.CreateScene("PoolTestLevel");
            SceneManager.SetActiveScene(level);

            // Parented to an object in the original scene, so it outlives the level's pool.
            Transform survivor = Track(new GameObject("Survivor")).transform;
            SceneManager.MoveGameObjectToScene(survivor.gameObject, original);
            PooledTestObject bullet = Pools.Get(_prefab, Vector3.zero, Quaternion.identity, survivor);

            SceneManager.SetActiveScene(original);
            yield return SceneManager.UnloadSceneAsync(level);
            Assert.IsTrue(bullet != null);

            bullet.ReturnToPool();
            yield return null;

            Assert.IsTrue(bullet == null);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ScenePlacedPoolSystem_BecomesInstance_AndPrewarmsItsList()
        {
            _prefab.PrewarmCount = 3;
            PoolSystem placed = CreateScenePlacedPoolSystem(_prefab);

            Assert.AreSame(placed, PoolSystem.Instance);
            Assert.AreEqual(3, placed.CountInactive(_prefab));
        }

        [UnityTest]
        public IEnumerator SecondPoolSystem_IsRemoved_AndItsListPrewarmedOnTheFirst()
        {
            PoolSystem first = Pools;
            _prefab.PrewarmCount = 2;

            LogAssert.Expect(LogType.Warning, new Regex("A PoolSystem already exists"));
            PoolSystem second = CreateScenePlacedPoolSystem(_prefab);
            yield return null;

            Assert.IsTrue(second == null);
            Assert.AreSame(first, PoolSystem.Instance);
            Assert.AreEqual(2, first.CountInactive(_prefab));
        }

        private PoolSystem CreateScenePlacedPoolSystem(params PooledObject[] prewarm)
        {
            GameObject go = Track(new GameObject("Placed PoolSystem"));
            go.SetActive(false);
            PoolSystem system = go.AddComponent<PoolSystem>();
            FieldInfo field = typeof(PoolSystem).GetField("_prewarm", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(system, new List<PooledObject>(prewarm));
            go.SetActive(true);
            return system;
        }
    }
}
