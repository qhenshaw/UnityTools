using UnityEditor;
using UnityEngine;
using System.Text.RegularExpressions;
using System;
using UnityEngine.Rendering;

namespace ArtPipeline.Editor
{
    public class MaterialCreator
    {
        private static string[] _texurePatterns = new[] { "basecolor", "normaldx" };
        private static string[] _textureNames = new[] { "Base Color", "Normal Map", "Mask Map" };
        private static string[,] _parameterNames = new[,]
        {
            { "_MainTex", "_Normal", "_Mask" },
            { "_BlendBaseColor", "_BlendNormal", "_BlendMask" },
            { "_BlendBaseColor_1", "_BlendNormal_1", "_BlendMask_1" },
            { "_BlendBaseColor_2", "_BlendNormal_2", "_BlendMask_2" },
            { "_BlendBaseColor_3", "_BlendNormal_3", "_BlendMask_3" }
        };
        private static string[] _maskMapChannelNames = new[] { "height", "metal", "emiss", "rough", "smooth", "ao", "occlu", "mher" };
        private static string[] _layerEnableKeywords = new[] 
        {
            "_ENABLED_LAYERS_BASE",
            "_ENABLED_LAYERS_BASE_R",
            "_ENABLED_LAYERS_BASE_RG",
            "_ENABLED_LAYERS_BASE_RGB",
            "_ENABLED_LAYERS_BASE_RGBA" 
        };

        [MenuItem("Assets/Create/VFS Uber/Create Material from Textures", false, -230)]
        public static void CreateUberMaterial() { CreateUberMaterial("VFS/Uber"); }

        [MenuItem("Assets/Create/VFS Uber/Assign Layer (Base)", false, -229)]
        public static void AssignLayer0() { SetMaterialLayer(0); }

        [MenuItem("Assets/Create/VFS Uber/Assign Layer (R-L)", false, -228)]
        public static void AssignLayer1() { SetMaterialLayer(1); }

        [MenuItem("Assets/Create/VFS Uber/Assign Layer (G-E)", false, -227)]
        public static void AssignLayer2() { SetMaterialLayer(2); }

        [MenuItem("Assets/Create/VFS Uber/Assign Layer (B-G)", false, -226)]
        public static void AssignLayer3() { SetMaterialLayer(3); }

        [MenuItem("Assets/Create/VFS Uber/Assign Layer (A-C)", false, -225)]
        public static void AssignLayer4() { SetMaterialLayer(4); }

        private static void CreateUberMaterial(string shaderName)
        {
            if (!ValidateSelectedTextures(out Texture[] textures)) return;

            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            Regex pathPattern = new Regex(@".+[/]");
            path = pathPattern.Match(path).Value;

            Regex fileNamePattern = new Regex(@".+[_]");
            string fileName = fileNamePattern.Match(textures[0].name).Value.Replace("_", "");

            Material material = new Material(Shader.Find(shaderName));
            AssetDatabase.CreateAsset(material, $"{path}/{fileName}.mat");

            for (int i = 0; i < _parameterNames.GetLength(0); i++)
            {
                for (int j = 0; j < _parameterNames.GetLength(1); j++)
                {
                    if (material.HasProperty(_parameterNames[i, j]))
                    {
                        material.SetTexture(_parameterNames[i, j], textures[j]);
                    }
                }
            }

            Debug.Log($"Material created: {material}", material);
        }

        private static bool ValidateSelectedTextures(out Texture[] textures)
        {
            Texture[] selectedTextures = Selection.GetFiltered<Texture>(SelectionMode.Assets);

            if (selectedTextures.Length != 3)
            {
                Debug.LogWarning("Select 3 PBR maps before creating material.");
                textures = null;
                return false;
            }

            textures = new Texture[3];
            for (int i = 0; i < _texurePatterns.Length; i++)
            {
                for (int j = 0; j < selectedTextures.Length; j++)
                {
                    if (selectedTextures[j].name.ToLower().Contains(_texurePatterns[i])) textures[i] = selectedTextures[j];
                }
            }

            for (int i = 0; i < selectedTextures.Length; i++)
            {
                if (CheckMaskMap(selectedTextures[i])) textures[2] = selectedTextures[i];
            }

            string output = $"Textures found:{Environment.NewLine}";
            for (int i = 0; i < _textureNames.Length; i++)
            {
                string result = "Failed";
                if (textures[i] != null)
                {
                    result = textures[i].name;
                    TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(textures[i]));
                    switch (i)
                    {
                        case 0:
                            importer.textureType = TextureImporterType.Default;
                            importer.sRGBTexture = true;
                            break;
                        case 1:
                            importer.textureType = TextureImporterType.NormalMap;
                            break;
                        case 2:
                            importer.textureType = TextureImporterType.Default;
                            importer.sRGBTexture = false;
                            break;
                    }

                    AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(textures[i]), ImportAssetOptions.ForceUpdate);
                }
                output += $"{_textureNames[i]}: {result}{Environment.NewLine}";
            }

            for (int i = 0; i < textures.Length; i++)
            {
                if (textures[i] == null)
                {
                    Debug.LogWarning("Select 3 PBR maps before creating material.");
                    return false;
                }
            }

            return true;
        }

        private static bool CheckMaskMap(Texture texture)
        {            
            int successTarget = 1;
            int successCount = 0;
            
            for (int i = 0; i < _maskMapChannelNames.Length; i++)
            {
                string channel = _maskMapChannelNames[i];
                if(texture.name.ToLower().Contains(channel)) successCount++;
            }

            return successCount >= successTarget;
        }

        private static void SetMaterialLayer(int layerIndex)
        {
            if (!ValidateSelectedTextures(out Texture[] textures)) return;

            Shader uberShader = Shader.Find("VFS/Uber");
            Material[] selectedMaterials = Selection.GetFiltered<Material>(SelectionMode.Assets);
            foreach (var mat in selectedMaterials)
            {
                if (mat.shader != uberShader)
                {
                    Debug.LogWarning($"Material {mat.name} is not using the VFS/Uber shader.");
                    continue;
                }

                for (int i = 0; i < _parameterNames.GetLength(1); i++)
                {
                    if (mat.HasProperty(_parameterNames[layerIndex, i]))
                    {
                        mat.SetTexture(_parameterNames[layerIndex, i], textures[i]);
                    }
                }

                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();

                string[] layerNames = new[] { "Base", "R-L", "G-E", "B-G", "A-C" };
                Debug.Log($"Layer [{layerNames[layerIndex]}] assigned to material: {mat.name}", mat);
            }
        }
    }
}