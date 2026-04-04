using UnityEditor;

namespace ArtPipeline.Editor
{
    public class ModelSettingsImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            ModelImporter importer = (ModelImporter)assetImporter;
            ArtAssetImportSettings settings = ArtAssetImportSettings.GetOrCreateSettings();
            importer.generateMeshLods = settings.GenerateMeshLODs;
            importer.importCameras = settings.ImportCameras;
            importer.importLights = settings.ImportLights;
        }
    }
}