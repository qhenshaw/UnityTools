using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static SceneManagement.PlayModeTests.StreamingTestScenes;

namespace SceneManagement.PlayModeTests
{
    [PrebuildSetup(typeof(StreamingTestScenes)), PostBuildCleanup(typeof(StreamingTestScenes))]
    public class StreamingSceneManagerTests
    {
        private const float Timeout = 10f;

        private readonly List<Object> _created = new();
        private readonly List<string> _started = new();
        private readonly List<string> _loaded = new();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return UnloadTestScenes();
            _started.Clear();
            _loaded.Clear();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (Object obj in _created)
            {
                if (obj != null) Object.Destroy(obj);
            }
            _created.Clear();
            yield return null;
            yield return UnloadTestScenes();
        }

        private static IEnumerator UnloadTestScenes()
        {
            foreach (string name in All)
            {
                Scene scene = SceneManager.GetSceneByName(name);
                if (!scene.IsValid()) continue;
                AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                while (unload != null && !unload.isDone) yield return null;
            }
        }

        private StreamingSceneManager CreateManager(string[] persistent, string[] initial, bool disableInsteadOfUnload = false)
        {
            var go = new GameObject("Streaming Scene Manager");
            go.SetActive(false);
            _created.Add(go);
            StreamingSceneManager manager = go.AddComponent<StreamingSceneManager>();
            manager.Configure(persistent, initial, disableInsteadOfUnload);
            manager.SetLoadStarted += set => _started.Add(string.Join(",", set));
            manager.SetLoaded += set => _loaded.Add(string.Join(",", set));
            go.SetActive(true);
            return manager;
        }

        private static IEnumerator WaitIdle(StreamingSceneManager manager)
        {
            float end = Time.realtimeSinceStartup + Timeout;
            yield return null;
            while (manager != null && manager.IsLoading)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for the set to load");
                yield return null;
            }
        }

        private static bool Loaded(string name)
        {
            Scene scene = SceneManager.GetSceneByName(name);
            return scene.IsValid() && scene.isLoaded;
        }

        private static GameObject FindRoot(string sceneName, string objectName)
        {
            foreach (GameObject root in SceneManager.GetSceneByName(sceneName).GetRootGameObjects())
            {
                if (root.name == objectName) return root;
            }
            return null;
        }

        // ---- Loading sets ----

        [UnityTest]
        public IEnumerator Awake_LoadsPersistentAndInitial()
        {
            StreamingSceneManager manager = CreateManager(new[] { Persist }, new[] { A });
            yield return WaitIdle(manager);

            Assert.IsTrue(Loaded(Persist));
            Assert.IsTrue(Loaded(A));
            Assert.AreSame(manager, StreamingSceneManager.Instance);
            CollectionAssert.AreEqual(new[] { Persist + "," + A }, _loaded);
            Assert.AreEqual(1f, manager.Progress);
        }

        [UnityTest]
        public IEnumerator LoadSet_UnloadsOldScenes_KeepsPersistentAndOwnScene()
        {
            StreamingSceneManager manager = CreateManager(new[] { Persist }, new[] { A });
            yield return WaitIdle(manager);
            Scene own = manager.gameObject.scene;

            manager.LoadSet(B);
            yield return WaitIdle(manager);

            Assert.IsFalse(Loaded(A));
            Assert.IsTrue(Loaded(B));
            Assert.IsTrue(Loaded(Persist));
            Assert.IsTrue(own.isLoaded, "the manager's own scene isn't in Persistent but must stay loaded");
            Assert.IsTrue(manager != null);
        }

        [UnityTest]
        public IEnumerator LoadSet_WhileLoading_EndsOnTheNewestSet()
        {
            StreamingSceneManager manager = CreateManager(null, new[] { A });
            yield return WaitIdle(manager);

            manager.LoadSet(B);
            manager.LoadSet(C);
            Assert.IsTrue(manager.IsLoading);
            yield return WaitIdle(manager);

            Assert.IsTrue(Loaded(C));
            Assert.IsFalse(Loaded(B));
            Assert.IsFalse(Loaded(A));
            CollectionAssert.AreEqual(new[] { A, C }, _loaded, "the skipped set never reports loaded");
            CollectionAssert.AreEqual(new[] { A, B, C }, _started);
            CollectionAssert.AreEqual(new[] { C }, manager.CurrentSet);
        }

        [UnityTest]
        public IEnumerator LoadSet_AfterAnOperationStarted_SwitchesToTheNewestSet()
        {
            StreamingSceneManager manager = CreateManager(null, new[] { A });
            yield return WaitIdle(manager);

            manager.LoadSet(B);
            yield return null; // B starts loading
            manager.LoadSet(C);
            yield return WaitIdle(manager);

            Assert.IsTrue(Loaded(C));
            Assert.IsFalse(Loaded(B));
            Assert.IsFalse(Loaded(A));
            Assert.AreEqual(C, _loaded[_loaded.Count - 1]);
            CollectionAssert.DoesNotContain(_loaded, B);
        }

        [UnityTest]
        public IEnumerator LoadSet_SameSet_IsIgnored()
        {
            StreamingSceneManager manager = CreateManager(null, new[] { A, B });
            yield return WaitIdle(manager);
            int started = _started.Count;

            manager.LoadSet(A, B);
            manager.LoadSet(B, A);
            Assert.IsFalse(manager.IsLoading);
            Assert.AreEqual(started, _started.Count);
            yield return null;
            Assert.IsTrue(Loaded(A) && Loaded(B));
        }

        [UnityTest]
        public IEnumerator LoadSet_SameSetWhileLoading_IsIgnored()
        {
            StreamingSceneManager manager = CreateManager(null, new[] { A });
            yield return WaitIdle(manager);

            manager.LoadSet(B);
            manager.LoadSet(B);
            manager.LoadSet(B);
            yield return WaitIdle(manager);

            Assert.AreEqual(1, _started.FindAll(s => s == B).Count);
            Assert.AreEqual(1, _loaded.FindAll(s => s == B).Count);
        }

        [UnityTest]
        public IEnumerator SceneNotInBuild_IsSkippedWithAnError()
        {
            StreamingSceneManager manager = CreateManager(null, new[] { A });
            yield return WaitIdle(manager);

            LogAssert.Expect(LogType.Error, new Regex($"can't load scene '{NotInBuild}'.*Build Settings"));
            manager.LoadSet(NotInBuild, B);
            yield return WaitIdle(manager);

            Assert.IsTrue(Loaded(B));
            Assert.IsFalse(Loaded(NotInBuild));
            Assert.IsFalse(Loaded(A));
        }

        [UnityTest]
        public IEnumerator EmptyLists_DoNotThrow()
        {
            StreamingSceneManager manager = CreateManager(null, null);
            yield return WaitIdle(manager);

            Assert.AreSame(manager, StreamingSceneManager.Instance);
            Assert.IsFalse(manager.IsLoading);
            CollectionAssert.IsEmpty(manager.CurrentSet);
        }

        // ---- Progress ----

        [UnityTest]
        public IEnumerator Progress_GoesFromZeroToOne()
        {
            StreamingSceneManager manager = CreateManager(null, null);
            yield return WaitIdle(manager);

            var seen = new List<float>();
            manager.LoadSet(A, B);
            Assert.IsTrue(manager.IsLoading);
            while (manager.IsLoading)
            {
                seen.Add(manager.Progress);
                yield return null;
            }

            Assert.AreEqual(1f, manager.Progress);
            Assert.Less(seen[0], 1f);
            for (int i = 1; i < seen.Count; i++) Assert.GreaterOrEqual(seen[i], seen[i - 1], "progress never goes backwards");
        }

        // ---- Disable instead of unload ----

        [UnityTest]
        public IEnumerator DisableMode_HidesAndRestoresOnlyWhatWasActive()
        {
            StreamingSceneManager manager = CreateManager(null, new[] { A }, disableInsteadOfUnload: true);
            yield return WaitIdle(manager);
            GameObject root = FindRoot(A, A + "_Root");
            GameObject inactive = FindRoot(A, A + "_Inactive");

            manager.LoadSet(B);
            yield return WaitIdle(manager);
            Assert.IsTrue(Loaded(A), "hidden, not unloaded");
            Assert.IsFalse(manager.IsSceneLoaded(A));
            Assert.IsFalse(root.activeSelf);

            manager.LoadSet(A);
            yield return WaitIdle(manager);
            Assert.IsTrue(manager.IsSceneLoaded(A));
            Assert.IsTrue(root.activeSelf);
            Assert.IsFalse(inactive.activeSelf, "an object authored inactive stays inactive");
            Assert.IsFalse(FindRoot(B, B + "_Root").activeSelf);
        }

        // ---- Singleton ----

        [UnityTest]
        public IEnumerator Destroy_ClearsInstance()
        {
            StreamingSceneManager manager = CreateManager(null, null);
            yield return WaitIdle(manager);

            Object.Destroy(manager.gameObject);
            yield return null;
            Assert.IsNull(StreamingSceneManager.Instance);
        }

        [UnityTest]
        public IEnumerator SecondManager_IsDestroyed()
        {
            StreamingSceneManager first = CreateManager(null, null);
            yield return WaitIdle(first);

            LogAssert.Expect(LogType.Warning, new Regex("Multiple StreamingSceneManager"));
            StreamingSceneManager second = CreateManager(null, new[] { A });
            yield return null;

            Assert.IsTrue(second == null);
            Assert.AreSame(first, StreamingSceneManager.Instance);
        }

        // ---- Transition trigger ----

        [UnityTest]
        public IEnumerator Transition_RepeatedEnters_LoadOnce()
        {
            StreamingSceneManager manager = CreateManager(null, new[] { A });
            yield return WaitIdle(manager);

            var triggerGo = new GameObject("Transition");
            triggerGo.SetActive(false);
            _created.Add(triggerGo);
            var box = triggerGo.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = Vector3.one * 4f;
            StreamingSceneTransition transition = triggerGo.AddComponent<StreamingSceneTransition>();
            SetOverride(transition, new[] { B });
            triggerGo.SetActive(true);

            // A player with two colliders fires OnTriggerEnter twice.
            var player = new GameObject("Player") { tag = "Player" };
            _created.Add(player);
            player.transform.position = new Vector3(0f, 0f, 20f);
            var body = player.AddComponent<Rigidbody>();
            body.isKinematic = true;
            player.AddComponent<SphereCollider>();
            var second = new GameObject("Second Collider");
            second.transform.SetParent(player.transform, false);
            second.transform.localPosition = Vector3.right * 0.5f;
            second.AddComponent<SphereCollider>();

            yield return new WaitForFixedUpdate();
            player.transform.position = Vector3.zero;
            body.position = Vector3.zero;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return WaitIdle(manager);

            Assert.IsTrue(Loaded(B));
            Assert.AreEqual(1, _started.FindAll(s => s == B).Count);
        }

        private static void SetOverride(StreamingSceneTransition transition, string[] scenes)
        {
            typeof(StreamingSceneTransition)
                .GetField("_overrideOnEnter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(transition, scenes);
        }
    }
}
