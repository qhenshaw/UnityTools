using UnityEngine;
using UnityEditor;
using System.IO;

namespace ArtPipeline.Editor
{
    internal class ArtAssetImportSettings : ScriptableObject
    {
        const string AssetPath = "Assets/Settings/";
        const string AssetName = "ArtAssetImportSettings.asset";
        const string FullAssetPath = AssetPath + AssetName;

        [Header("Model Import Settings")]
        [Tooltip("The importer will look for UCX_ prefixed meshes to generate colliders. Each UCX_ mesh will generate a separate collider.")]
        [SerializeField] private bool _generateUCXColliders = true;
        [Tooltip("Generate LOD meshes for imported models. The LOD levels re-use existing vertices and do not create additional meshes.")]
        [SerializeField] private bool _generateMeshLODs = true;
        [Tooltip("Import cameras from the model file.")]
        [SerializeField] private bool _importCameras = false;
        [Tooltip("Import lights from the model file.")]
        [SerializeField] private bool _importLights = false;

        public bool GenerateMeshLODs { get => _generateMeshLODs; set => _generateMeshLODs = value; }
        public bool ImportCameras { get => _importCameras; set => _importCameras = value; }
        public bool ImportLights { get => _importLights; set => _importLights = value; }
        public bool GenerateUCXColliders { get => _generateUCXColliders; set => _generateUCXColliders = value; }

        internal static ArtAssetImportSettings GetOrCreateSettings()
        {
            if (!Directory.Exists(AssetPath)) Directory.CreateDirectory(AssetPath);
            ArtAssetImportSettings settings = AssetDatabase.LoadAssetAtPath<ArtAssetImportSettings>(FullAssetPath);
            if (settings == null)
            {
                settings = CreateInstance<ArtAssetImportSettings>();
                AssetDatabase.CreateAsset(settings, FullAssetPath);
                AssetDatabase.SaveAssets();
            }
            return settings;
        }

        internal static SerializedObject GetSO() => new SerializedObject(GetOrCreateSettings());
    }

    internal class ModelImportSettingsEditor
    {
        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            var provider = new SettingsProvider("Project/Art Asset Import Settings", SettingsScope.Project)
            {
                label = "[Custom] Art Asset Import Settings",
                guiHandler = (searchContext) =>
                {
                    SerializedObject so = ArtAssetImportSettings.GetSO();
                    EditorGUILayout.PropertyField(so.FindProperty("_generateUCXColliders"), new GUIContent("Generate UCX Colliders"));
                    EditorGUILayout.PropertyField(so.FindProperty("_generateMeshLODs"), new GUIContent("Generate Mesh LODs"));
                    EditorGUILayout.PropertyField(so.FindProperty("_importCameras"), new GUIContent("Import Cameras"));
                    EditorGUILayout.PropertyField(so.FindProperty("_importLights"), new GUIContent("Import Lights"));
                    so.ApplyModifiedProperties();
                },
                keywords = new[]
                {
                "Art",
                "Model",
                "Mesh",
                "Texture",
                "Import",
                "LOD",
                "Cameras",
                "Lights",
                "Collider",
                "UCX"
            }
            };
            return provider;
        }
    }
}