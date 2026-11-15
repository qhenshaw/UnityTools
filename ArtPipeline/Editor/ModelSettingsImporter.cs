using UnityEditor;

namespace ArtPipeline.Editor
{
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
