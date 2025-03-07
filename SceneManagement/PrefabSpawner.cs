using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        [SerializeField, AssetsOnly] private GameObject _prefab;
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

#if UNITY_EDITOR
        private void Update()
        {
            if (Application.isPlaying) return;
            Material material = Instantiate(_previewMaterial);
            material.color = _color;
            MeshPreview.DrawImmediate(_prefab, transform.position, transform.rotation, material);
        }

        [UnityEditor.MenuItem("GameObject/Prefab Spawner", false, 0)]
        static void CreateCustomGameObject(UnityEditor.MenuCommand menuCommand)
        {
            GameObject go = new GameObject("Prefab Spawner");
            go.AddComponent<PrefabSpawner>();
            UnityEditor.GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            UnityEditor.Selection.activeObject = go;
        }
#endif
    }
}