using Sirenix.OdinInspector;
using UnityEngine;

namespace SceneManagement
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    public class PrefabSpawner : MonoBehaviour
    {
        [SerializeField, AssetsOnly] private GameObject _prefab;
        [SerializeField] private bool _spawnOnStart = true;
        [SerializeField] private bool _autoName = true;

        [Header("Preview")]
        [SerializeField] private Color _color = new Color(0f, 1f, 1f, 0.5f);
        [SerializeField] private Material _previewMaterial;

        private void OnValidate()
        {
            if (_autoName && _prefab != null)
            {
                gameObject.name = $"{_prefab.name} Spawner";
            }
        }

        private void Start()
        {
            if (!Application.isPlaying) return;
            if (_spawnOnStart) Spawn();
        }

        public GameObject Spawn()
        {
            GameObject instantiated = Instantiate(_prefab, transform.position, Quaternion.identity);
            return instantiated;
        }

        public GameObject Spawn(GameObject prefab)
        {
            GameObject instantiated = Instantiate(prefab, transform.position, Quaternion.identity);
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
#endif

#if UNITY_EDITOR
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