using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Search;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SceneManagement
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    public class PrefabSpawner : MonoBehaviour
    {
        private enum ParentMode
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
        [SerializeField] private Material _previewMaterial;

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

        public GameObject Spawn()
        {
            return Spawn(_prefab);
        }

        public GameObject Spawn(GameObject prefab)
        {
            GameObject instantiated = Instantiate(prefab, transform.position, Quaternion.identity);
            switch (_parentMode)
            {
                case ParentMode.None:
                default:
                    break;
                case ParentMode.SameScene:
                    SceneManager.MoveGameObjectToScene(instantiated, gameObject.scene);
                    break;
                case ParentMode.Self:
                    instantiated.transform.SetParent(transform);
                    break;
                case ParentMode.SameParent:
                    if(transform.parent != null) instantiated.transform.SetParent(transform.parent);
                    else SceneManager.MoveGameObjectToScene(instantiated, gameObject.scene);
                    break;
            }
            if (_destroySpawner && _parentMode != ParentMode.Self) Destroy(gameObject);
            return instantiated;
        }

        public void SetPrefab(GameObject prefab)
        {
            _prefab = prefab;
            OnValidate();
        }

#if UNITY_EDITOR
        private void Update()
        {
            if (Application.isPlaying) return;
            Material material = Instantiate(_previewMaterial);
            material.color = _color;
            MeshPreview.DrawImmediate(_prefab, transform.position, transform.rotation, material);
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

        [MenuItem("GameObject/Replace with Prefab Spawner", false, 0)]
        private static void ReplaceWithPrefabSpawner(MenuCommand menuCommand)
        {
            if(Selection.activeObject == null)
            {
                Debug.LogWarning("No prefab selected to replace.");
                return;
            }
            GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(Selection.activeObject) as GameObject;
            if (prefab == null)
            {
                Debug.LogWarning("Selected object is not a prefab.");
                return;
            }

            GameObject go = new GameObject("Prefab Spawner");
            PrefabSpawner spawner = go.AddComponent<PrefabSpawner>();
            spawner.SetPrefab(prefab);
            spawner.transform.position = Selection.activeTransform.position;
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);

            if (Selection.activeGameObject != null)
            {
                Undo.DestroyObjectImmediate(Selection.activeGameObject);
            }

            Selection.activeObject = go;
        }
#endif
    }
}