using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Search;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace SceneManagement
{
    /// <summary>
    /// Stands in for a prefab in a scene and spawns it at runtime, at the spawner's position and rotation.
    /// The prefab keeps its own scale. In the Scene view it previews the prefab's meshes.
    /// </summary>
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    public class PrefabSpawner : MonoBehaviour
    {
        internal enum ParentMode
        {
            None,
            SameScene,
            Self,
            SameParent
        }

        [SerializeField, SearchContext("p: t:Prefab")] private GameObject _prefab;
        [SerializeField] private bool _spawnOnStart = true;
        [SerializeField] private ParentMode _parentMode = ParentMode.SameScene;
        [SerializeField] private bool _destroySpawner = true;
        [SerializeField] private bool _autoName = true;

        [Header("Preview")]
        [SerializeField] private Color _color = new Color(0f, 1f, 1f, 0.5f);
        [SerializeField, Tooltip("Optional. Without a material the preview is drawn as tinted gizmos.")]
        private Material _previewMaterial;

        [NonSerialized] private Material _materialInstance;
        [NonSerialized] private Material _materialSource;

        /// <summary>The prefab this spawner spawns.</summary>
        public GameObject Prefab => _prefab;

        private void OnValidate()
        {
            if (!_autoName) return;
            if (_prefab == null) return;
            gameObject.name = $"{_prefab.name} Spawner";
        }

        private void Start()
        {
            if (!Application.isPlaying) return;
            if (_spawnOnStart) Spawn();
        }

        /// <summary>Spawns the spawner's prefab. Called on Start when Spawn On Start is on.</summary>
        public GameObject Spawn()
        {
            return Spawn(_prefab);
        }

        /// <summary>
        /// Spawns the prefab at the spawner's position and rotation (the prefab keeps its own scale) and parents it
        /// by the Parent Mode. Destroys the spawner afterwards if Destroy Spawner is on, in Play mode only and not
        /// when Parent Mode is Self. Returns the new instance, or null without a prefab.
        /// </summary>
        public GameObject Spawn(GameObject prefab)
        {
            if (prefab == null)
            {
                Debug.LogError($"{name} has no prefab to spawn.", this);
                return null;
            }

            GameObject instantiated = Instantiate(prefab, transform.position, transform.rotation);
            switch (_parentMode)
            {
                case ParentMode.None:
                default:
                    break;
                case ParentMode.SameScene:
                    SceneManager.MoveGameObjectToScene(instantiated, gameObject.scene);
                    break;
                case ParentMode.Self:
                    instantiated.transform.SetParent(transform, true);
                    break;
                case ParentMode.SameParent:
                    if (transform.parent != null) instantiated.transform.SetParent(transform.parent, true);
                    else SceneManager.MoveGameObjectToScene(instantiated, gameObject.scene);
                    break;
            }
            if (_destroySpawner && _parentMode != ParentMode.Self && Application.isPlaying) Destroy(gameObject);
            return instantiated;
        }

        /// <summary>Changes the prefab, renaming the spawner if Auto Name is on.</summary>
        public void SetPrefab(GameObject prefab)
        {
            _prefab = prefab;
            OnValidate();
        }

        internal void Configure(GameObject prefab, ParentMode parentMode, bool destroySpawner)
        {
            _prefab = prefab;
            _parentMode = parentMode;
            _destroySpawner = destroySpawner;
        }

        /// <summary>The cached copy of the Preview Material, tinted with the Preview colour. Null without a material.</summary>
        internal Material GetPreviewMaterial()
        {
            if (_previewMaterial == null)
            {
                DestroyPreviewMaterial();
                return null;
            }

            if (_materialInstance == null || _materialSource != _previewMaterial)
            {
                DestroyPreviewMaterial();
                _materialInstance = new Material(_previewMaterial) { hideFlags = HideFlags.HideAndDontSave };
                _materialSource = _previewMaterial;
            }
            _materialInstance.color = _color;
            return _materialInstance;
        }

        internal void SetPreview(Material material, Color color)
        {
            _previewMaterial = material;
            _color = color;
        }

        private void DestroyPreviewMaterial()
        {
            if (_materialInstance != null) DestroyImmediate(_materialInstance);
            _materialInstance = null;
            _materialSource = null;
        }

        private void OnDisable() => DestroyPreviewMaterial();

#if UNITY_EDITOR
        private void Update()
        {
            if (Application.isPlaying) return;
            Material material = GetPreviewMaterial();
            if (material != null) MeshPreview.DrawImmediate(_prefab, transform.position, transform.rotation, material);
        }

        private void OnDrawGizmos()
        {
            if (Application.isPlaying || _previewMaterial != null) return;
            MeshPreview.DrawGizmos(_prefab, transform.position, transform.rotation, _color);
        }

        [MenuItem("GameObject/Prefab Spawner", false, 0)]
        static void CreateCustomGameObject(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("Prefab Spawner");
            go.AddComponent<PrefabSpawner>();
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }

        /// <summary>Choice for instances with overrides: 0 replace anyway, 1 cancel, 2 skip them. Replaced by tests.</summary>
        internal static Func<string, int> AskAboutOverrides = message => EditorUtility.DisplayDialogComplex(
            "Replace with Prefab Spawner", message, "Replace Anyway", "Cancel", "Skip Those");

        [MenuItem("GameObject/Replace with Prefab Spawner", false, 0)]
        private static void ReplaceWithPrefabSpawner(MenuCommand menuCommand)
        {
            // From the Hierarchy context menu this runs once per selected object; handle the whole selection once.
            GameObject[] selection = Selection.gameObjects;
            if (menuCommand.context != null && selection.Length > 1 && menuCommand.context != selection[0]) return;

            List<GameObject> spawners = ReplaceWithSpawners(selection);
            if (spawners.Count > 0) Selection.objects = spawners.Cast<Object>().ToArray();
        }

        [MenuItem("GameObject/Replace with Prefab Spawner", true)]
        private static bool ValidateReplaceWithPrefabSpawner() => Selection.gameObjects.Length > 0;

        /// <summary>
        /// Replaces each prefab instance among the objects (or the instance they're part of) with a spawner of the
        /// same prefab, as one Undo step. Instances with overrides are listed first: replace anyway, cancel or skip them.
        /// </summary>
        internal static List<GameObject> ReplaceWithSpawners(IEnumerable<GameObject> objects)
        {
            var instances = new List<GameObject>();
            foreach (GameObject go in objects)
            {
                if (go == null) continue;
                GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
                if (root == null)
                {
                    Debug.LogWarning($"{go.name} isn't a prefab instance, so it can't be replaced with a Prefab Spawner.", go);
                    continue;
                }
                if (!instances.Contains(root)) instances.Add(root);
            }

            List<GameObject> withOverrides = instances.Where(i => PrefabUtility.HasPrefabInstanceAnyOverrides(i, false)).ToList();
            if (withOverrides.Count > 0)
            {
                string names = string.Join("\n", withOverrides.Take(10).Select(i => "  " + i.name));
                if (withOverrides.Count > 10) names += $"\n  ...and {withOverrides.Count - 10} more";
                string message = $"These instances have prefab overrides, which a Prefab Spawner can't keep:\n{names}\n\n" +
                                 "Replace them anyway (losing the overrides), or skip them?";
                switch (AskAboutOverrides(message))
                {
                    case 1: return new List<GameObject>();
                    case 2: instances.RemoveAll(withOverrides.Contains); break;
                }
            }

            var spawners = new List<GameObject>();
            if (instances.Count == 0) return spawners;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Replace with Prefab Spawner");
            foreach (GameObject instance in instances)
            {
                spawners.Add(ReplaceWithSpawner(instance));
            }
            Undo.CollapseUndoOperations(group);
            return spawners;
        }

        private static GameObject ReplaceWithSpawner(GameObject instance)
        {
            GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(instance);
            Transform source = instance.transform;

            var go = new GameObject("Prefab Spawner");
            SceneManager.MoveGameObjectToScene(go, instance.scene);
            go.transform.SetParent(source.parent, false);
            go.transform.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
            go.transform.SetSiblingIndex(source.GetSiblingIndex());
            PrefabSpawner spawner = go.AddComponent<PrefabSpawner>();
            spawner.SetPrefab(prefab);
            Undo.RegisterCreatedObjectUndo(go, "Replace with Prefab Spawner");

            Undo.DestroyObjectImmediate(instance);
            return go;
        }
#endif
    }
}
