using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace SceneManagement
{
    /// <summary>
    /// Keeps one set of scenes loaded at a time, by name. <see cref="LoadSet"/> loads the scenes of a new set, then
    /// unloads every other scene except the Persistent ones and the scene this manager is in.
    /// </summary>
    /// <remarks>
    /// Calling LoadSet while a set is loading switches to the newest set once the scene being loaded or unloaded
    /// finishes; sets asked for in between are skipped. Asking for the set that's already loaded or loading does
    /// nothing. Scenes must be in Build Settings: a scene that isn't is skipped with an error.
    /// </remarks>
    [DefaultExecutionOrder(-9000)]
    [DisallowMultipleComponent]
    public class StreamingSceneManager : MonoBehaviour
    {
        /// <summary>The loaded manager, or null. A second manager destroys itself on Awake.</summary>
        public static StreamingSceneManager Instance { get; private set; }

        [SerializeField] private bool _unloadUnusedAssets = true;
        [SerializeField, Tooltip("Hide scenes by deactivating their root objects instead of unloading them. Showing a scene again re-activates only the objects that were active when it was hidden.")]
        private bool _disableInsteadOfUnload = false;
        [SerializeField, SceneDropdown(storeName: true)] private string[] _persistent;
        [SerializeField, SceneDropdown(storeName: true)] private string[] _initial;

        /// <summary>Raised when the manager starts loading a set, with the set's scene names.</summary>
        public event Action<string[]> SetLoadStarted;

        /// <summary>Raised when a set has finished loading and the scenes it replaced are unloaded.</summary>
        public event Action<string[]> SetLoaded;

        /// <summary>True while a set is loading or the old scenes are unloading.</summary>
        public bool IsLoading { get; private set; }

        /// <summary>How far the current set has got, from 0 to 1, counting its loads and unloads. 1 when idle.</summary>
        public float Progress { get; private set; } = 1f;

        /// <summary>The set most recently asked for (it may still be loading).</summary>
        public IReadOnlyList<string> CurrentSet => _requested ?? Array.Empty<string>();

        private string[] _requested;
        private string[] _pending;
        private Coroutine _routine;

        // Scenes hidden by "Disable Instead Of Unload", with the root objects that were active when hidden.
        private readonly Dictionary<string, List<GameObject>> _hidden = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Multiple StreamingSceneManager singletons loaded. Destroying the new one.", gameObject);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            LoadSet((_persistent ?? Array.Empty<string>()).Concat(_initial ?? Array.Empty<string>()).ToArray());
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Loads the given scenes and then unloads every other scene except the Persistent ones and this manager's own.
        /// </summary>
        public void LoadSet(params string[] sceneNames)
        {
            string[] set = (sceneNames ?? Array.Empty<string>())
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct()
                .ToArray();

            if (_requested != null && SameScenes(set, _requested)) return;
            _requested = set;
            _pending = set;

            if (_routine == null) _routine = StartCoroutine(RunRoutine());
        }

        /// <summary>True when a scene with this name is loaded (or loading) and not hidden.</summary>
        public bool IsSceneLoaded(string sceneName) => IsLoaded(sceneName) && !_hidden.ContainsKey(sceneName);

        internal void Configure(string[] persistent, string[] initial, bool disableInsteadOfUnload, bool unloadUnusedAssets = false)
        {
            _persistent = persistent;
            _initial = initial;
            _disableInsteadOfUnload = disableInsteadOfUnload;
            _unloadUnusedAssets = unloadUnusedAssets;
        }

        private IEnumerator RunRoutine()
        {
            IsLoading = true;
            while (_pending != null)
            {
                string[] set = _pending;
                _pending = null;
                Progress = 0f;
                SetLoadStarted?.Invoke(set);

                bool finished = false;
                yield return LoadSetRoutine(set, done => finished = done);
                if (finished && _pending == null)
                {
                    Progress = 1f;
                    SetLoaded?.Invoke(set);
                }
            }
            IsLoading = false;
            Progress = 1f;
            _routine = null;
        }

        private IEnumerator LoadSetRoutine(string[] set, Action<bool> result)
        {
            var keep = new HashSet<string>(set);
            foreach (string name in _persistent ?? Array.Empty<string>())
            {
                if (!string.IsNullOrEmpty(name)) keep.Add(name);
            }
            keep.Add(gameObject.scene.name);

            List<string> toLoad = set.Where(name => !IsSceneLoaded(name)).ToList();
            int total = Mathf.Max(1, toLoad.Count + LoadedScenesNotIn(keep).Count);
            int done = 0;

            foreach (string sceneName in toLoad)
            {
                if (_pending != null) { result(false); yield break; }

                if (_hidden.ContainsKey(sceneName))
                {
                    Show(sceneName);
                }
                else if (!IsLoaded(sceneName))
                {
                    if (!Application.CanStreamedLevelBeLoaded(sceneName))
                    {
                        Debug.LogError($"StreamingSceneManager can't load scene '{sceneName}': it isn't in Build Settings (File > Build Profiles > Scene List). Skipping it.", this);
                    }
                    else
                    {
                        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                        while (load != null && !load.isDone)
                        {
                            Progress = (done + Mathf.Clamp01(load.progress / 0.9f)) / total;
                            yield return null;
                        }
                    }
                }
                done++;
                Progress = (float)done / total;
            }

            bool unloadedAny = false;
            foreach (string sceneName in LoadedScenesNotIn(keep))
            {
                if (_pending != null) { result(false); yield break; }

                if (_disableInsteadOfUnload)
                {
                    Hide(sceneName);
                }
                else if (IsLoaded(sceneName))
                {
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(sceneName);
                    while (unload != null && !unload.isDone)
                    {
                        Progress = (done + Mathf.Clamp01(unload.progress)) / total;
                        yield return null;
                    }
                    unloadedAny = true;
                }
                done++;
                Progress = (float)done / total;
            }

            if (unloadedAny && _unloadUnusedAssets) yield return Resources.UnloadUnusedAssets();
            result(true);
        }

        private List<string> LoadedScenesNotIn(HashSet<string> keep)
        {
            var scenes = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!keep.Contains(scene.name) && !_hidden.ContainsKey(scene.name) && !scenes.Contains(scene.name))
                    scenes.Add(scene.name);
            }
            return scenes;
        }

        private static bool IsLoaded(string sceneName) => SceneManager.GetSceneByName(sceneName).IsValid();

        private void Hide(string sceneName)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded) return;

            var wasActive = new List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (!root.activeSelf) continue;
                wasActive.Add(root);
                root.SetActive(false);
            }
            _hidden[sceneName] = wasActive;
        }

        private void Show(string sceneName)
        {
            if (!_hidden.TryGetValue(sceneName, out List<GameObject> wasActive)) return;
            _hidden.Remove(sceneName);
            foreach (GameObject root in wasActive)
            {
                if (root != null) root.SetActive(true);
            }
        }

        private static bool SameScenes(string[] a, string[] b) =>
            a.Length == b.Length && new HashSet<string>(a).SetEquals(b);

#if UNITY_EDITOR
        [MenuItem("GameObject/Scene Management/Streaming Scene Manager", false, 5)]
        static void CreateCustomGameObject(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("Streaming Scene Manager");
            go.AddComponent<StreamingSceneManager>();
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }

        [MenuItem("GameObject/Scene Management/Add Empty Scene", false, 5)]
        static void AddEmptyScene(MenuCommand menuCommand)
        {
            Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            newScene.name = "New Empty Scene";
        }
#endif
    }
}
