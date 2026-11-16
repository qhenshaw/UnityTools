using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnityTools.Art.Editor
{
    // Values the import postprocessors use, read fresh from disk for every import (see ArtAssetImportSettings.ReadForImport).
    internal readonly struct ArtImportValues
    {
        public readonly bool GenerateUnrealColliders;
        public readonly bool GenerateMeshLODs;
        public readonly bool ImportCameras;
        public readonly bool ImportLights;

        public ArtImportValues(bool generateUnrealColliders, bool generateMeshLODs, bool importCameras, bool importLights)
        {
            GenerateUnrealColliders = generateUnrealColliders;
            GenerateMeshLODs = generateMeshLODs;
            ImportCameras = importCameras;
            ImportLights = importLights;
        }
    }

    // Stored in ProjectSettings/ (not in Assets/), edited through the settings page in the main Editor process.
    [FilePath(FilePathInProject, FilePathAttribute.Location.ProjectFolder)]
    internal class ArtAssetImportSettings : ScriptableSingleton<ArtAssetImportSettings>
    {
        internal const string FilePathInProject = "ProjectSettings/ArtAssetImportSettings.asset";
        private const string LegacyAssetPath = "Assets/Settings/ArtAssetImportSettings.asset";

        [Header("Model Import Settings")]
        [Tooltip("Children named with an Unreal collision prefix become colliders: UCX_ (convex mesh), UBX_ (box), USP_ (sphere) and UCP_ (capsule). Applies to models as they are imported.")]
        [SerializeField] private bool _generateUCXColliders = true;
        [Tooltip("Generate LOD meshes for imported models. The LOD levels re-use existing vertices and do not create additional meshes. Applies on a model's first import.")]
        [SerializeField] private bool _generateMeshLODs = true;
        [Tooltip("Import cameras from the model file. Applies on a model's first import.")]
        [SerializeField] private bool _importCameras = false;
        [Tooltip("Import lights from the model file. Applies on a model's first import.")]
        [SerializeField] private bool _importLights = false;

        public bool GenerateUnrealColliders { get => _generateUCXColliders; set => _generateUCXColliders = value; }
        public bool GenerateMeshLODs { get => _generateMeshLODs; set => _generateMeshLODs = value; }
        public bool ImportCameras { get => _importCameras; set => _importCameras = value; }
        public bool ImportLights { get => _importLights; set => _importLights = value; }

        private void OnEnable()
        {
            // ScriptableSingleton instances are not editable by default, the settings page needs to edit it.
            hideFlags &= ~HideFlags.NotEditable;
        }

        internal void SaveSettings() => Save(true);

        // Import postprocessors run in parallel import worker processes, which would each keep a stale copy of the
        // singleton. Reading the saved file on every import keeps them in sync with the settings page. It never goes
        // through the AssetDatabase and never writes, so it is safe in workers and mid-import.
        internal static ArtImportValues ReadForImport()
        {
            string yaml = null;
            if (File.Exists(FilePathInProject)) yaml = File.ReadAllText(FilePathInProject);
            else if (File.Exists(LegacyAssetPath)) yaml = File.ReadAllText(LegacyAssetPath);

            return new ArtImportValues(
                ReadBool(yaml, nameof(_generateUCXColliders), true),
                ReadBool(yaml, nameof(_generateMeshLODs), true),
                ReadBool(yaml, nameof(_importCameras), false),
                ReadBool(yaml, nameof(_importLights), false));
        }

        // Older versions stored the settings as an asset in Assets/Settings/. If it is still there, copy its values
        // into ProjectSettings once and delete it. Only the main Editor process does this, never an import worker.
        [InitializeOnLoadMethod]
        private static void MigrateLegacyAsset()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            if (!File.Exists(LegacyAssetPath)) return;

            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(LegacyAssetPath)) return;
                string yaml = File.ReadAllText(LegacyAssetPath);
                ArtAssetImportSettings settings = instance;
                settings._generateUCXColliders = ReadBool(yaml, nameof(_generateUCXColliders), settings._generateUCXColliders);
                settings._generateMeshLODs = ReadBool(yaml, nameof(_generateMeshLODs), settings._generateMeshLODs);
                settings._importCameras = ReadBool(yaml, nameof(_importCameras), settings._importCameras);
                settings._importLights = ReadBool(yaml, nameof(_importLights), settings._importLights);
                settings.SaveSettings();

                if (AssetDatabase.DeleteAsset(LegacyAssetPath))
                    Debug.Log($"Art Asset Import Settings moved to {FilePathInProject}, deleted the old {LegacyAssetPath}.");
            };
        }

        private static bool ReadBool(string yaml, string field, bool fallback)
        {
            if (yaml == null) return fallback;
            Match match = Regex.Match(yaml, $@"^\s*{field}:\s*([01])\s*$", RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value == "1" : fallback;
        }
    }

    internal class ModelImportSettingsEditor
    {
        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            var provider = new SettingsProvider("Project/Unity Tools/Art Asset Import", SettingsScope.Project)
            {
                label = "Art Asset Import",
                guiHandler = (searchContext) =>
                {
                    ArtAssetImportSettings settings = ArtAssetImportSettings.instance;
                    SerializedObject so = new SerializedObject(settings);
                    EditorGUILayout.PropertyField(so.FindProperty("_generateUCXColliders"), new GUIContent("Generate Unreal Colliders"));
                    EditorGUILayout.PropertyField(so.FindProperty("_generateMeshLODs"), new GUIContent("Generate Mesh LODs"));
                    EditorGUILayout.PropertyField(so.FindProperty("_importCameras"), new GUIContent("Import Cameras"));
                    EditorGUILayout.PropertyField(so.FindProperty("_importLights"), new GUIContent("Import Lights"));
                    if (so.ApplyModifiedProperties()) settings.SaveSettings();
                    EditorGUILayout.HelpBox("Changes apply to models as they are imported. Reimport existing models to update them.", MessageType.Info);
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
                    "Collision",
                    "Unreal",
                    "UCX",
                    "UBX",
                    "USP",
                    "UCP"
                }
            };
            return provider;
        }
    }
}
