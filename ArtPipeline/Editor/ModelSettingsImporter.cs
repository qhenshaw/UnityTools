using UnityEditor;

namespace ArtPipeline.Editor
{
    /// <summary>
    /// Sets Generate Mesh LODs, Import Cameras and Import Lights from Project Settings > Unity Tools > Art Asset Import
    /// on a model's first import only, so later changes made in the model's import settings are kept.
    /// </summary>
    public class ModelSettingsImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            ModelImporter importer = (ModelImporter)assetImporter;
            if (!importer.importSettingsMissing) return;

            ArtImportValues settings = ArtAssetImportSettings.ReadForImport();
            importer.generateMeshLods = settings.GenerateMeshLODs;
            importer.importCameras = settings.ImportCameras;
            importer.importLights = settings.ImportLights;
        }
    }
}
