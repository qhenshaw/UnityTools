using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArtPipeline.Editor
{
    /// <summary>
    /// Builds VFS/Uber materials from a base colour, normal and mask texture, and assigns textures to its blend layers.
    /// Textures are identified by the part of their name after the last underscore.
    /// </summary>
    public class MaterialCreator
    {
        internal const string UberShaderName = "VFS/Uber";

        internal enum Slot { BaseColor = 0, Normal = 1, Mask = 2 }

        private static readonly string[] _textureNames = { "Base Color", "Normal Map", "Mask Map" };
        private static readonly string[] _baseColorSuffixes = { "basecolor", "albedo", "diffuse" };
        private static readonly string[] _normalSuffixes = { "normal", "normaldx", "normalgl" };
        private static readonly string[] _maskSuffixes = { "mher", "mask", "orm" };

        private static readonly string[,] _parameterNames = new[,]
        {
            { "_MainTex", "_Normal", "_Mask" },
            { "_BlendBaseColor", "_BlendNormal", "_BlendMask" },
            { "_BlendBaseColor_1", "_BlendNormal_1", "_BlendMask_1" },
            { "_BlendBaseColor_2", "_BlendNormal_2", "_BlendMask_2" },
            { "_BlendBaseColor_3", "_BlendNormal_3", "_BlendMask_3" }
        };

        // The shader's "Enabled Layers" keyword enum, in order: the float holds the index, one keyword is on.
        internal const string EnabledLayersProperty = "_ENABLED_LAYERS";
        internal static readonly string[] EnabledLayersKeywords =
        {
            "_ENABLED_LAYERS_BASE",
            "_ENABLED_LAYERS_BASE_BLEND_1_R",
            "_ENABLED_LAYERS_BASE_BLEND_12_RG",
            "_ENABLED_LAYERS_BASE_BLEND_123_RGB",
            "_ENABLED_LAYERS_BASE_BLEND_1234_RGBA",
        };

        private static readonly string[] _layerNames = { "Base", "R-L", "G-E", "B-G", "A-C" };

        [MenuItem("Assets/Create/VFS Uber/Create Material from Textures", false, -230)]
        public static void CreateUberMaterial() { CreateUberMaterial(UberShaderName); }

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
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"Can't create the material: the {shaderName} shader isn't in this project.");
                return;
            }
            Texture[] selected = Selection.GetFiltered<Texture>(SelectionMode.Assets);
            if (!TryMatchTextures(selected, out Texture[] textures, out string error))
            {
                Debug.LogWarning(error);
                return;
            }
            ApplyImportSettings(textures);
            CreateMaterial(shader, textures);
        }

        internal static Material CreateMaterial(Shader shader, Texture[] textures)
        {
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(textures[(int)Slot.BaseColor]))?.Replace('\\', '/');
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{BaseName(textures[(int)Slot.BaseColor].name)}.mat");

            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            AssignLayer(material, 0, textures);
            AssetDatabase.SaveAssets();

            Debug.Log($"Material created: {material}", material);
            return material;
        }

        /// <summary>
        /// The material name: the texture name without its suffix or underscores, as before
        /// ("Rock_Moss_BaseColor" becomes "RockMoss").
        /// </summary>
        internal static string BaseName(string textureName)
        {
            int underscore = textureName.LastIndexOf('_');
            string name = underscore > 0 ? textureName.Substring(0, underscore) : textureName;
            return name.Replace("_", "");
        }

        /// <summary>Which slot a texture name belongs to, by the part after its last underscore.</summary>
        internal static Slot? GetSlot(string textureName)
        {
            int underscore = textureName.LastIndexOf('_');
            if (underscore < 0) return null;
            string suffix = textureName.Substring(underscore + 1).ToLowerInvariant();
            if (_baseColorSuffixes.Contains(suffix)) return Slot.BaseColor;
            if (_normalSuffixes.Contains(suffix)) return Slot.Normal;
            if (_maskSuffixes.Contains(suffix)) return Slot.Mask;
            return null;
        }

        /// <summary>
        /// Sorts exactly three textures into base colour, normal and mask. Fails with a message naming the problem
        /// (wrong count, an unrecognised name, a missing or doubled slot).
        /// </summary>
        internal static bool TryMatchTextures(IReadOnlyList<Texture> selected, out Texture[] textures, out string error)
        {
            textures = new Texture[3];
            error = null;
            if (selected.Count != 3)
            {
                error = $"Select 3 textures (BaseColor, Normal and MHER) before creating a material; {selected.Count} selected.";
                return false;
            }

            var problems = new List<string>();
            foreach (Texture texture in selected)
            {
                Slot? slot = GetSlot(texture.name);
                if (slot == null)
                {
                    problems.Add($"'{texture.name}' doesn't end in a known suffix (_BaseColor, _Normal/_NormalDX/_NormalGL, _MHER/_Mask/_ORM)");
                    continue;
                }
                if (textures[(int)slot] != null)
                {
                    problems.Add($"'{textures[(int)slot].name}' and '{texture.name}' are both a {_textureNames[(int)slot]}");
                    continue;
                }
                textures[(int)slot] = texture;
            }
            for (int i = 0; i < textures.Length; i++)
            {
                if (textures[i] == null && problems.Count == 0) problems.Add($"No {_textureNames[i]} selected");
            }

            if (problems.Count > 0)
            {
                error = "Can't create the material:" + Environment.NewLine + string.Join(Environment.NewLine, problems);
                return false;
            }

            if (textures[(int)Slot.Normal].name.ToLowerInvariant().EndsWith("_normalgl"))
            {
                Debug.LogWarning($"{textures[(int)Slot.Normal].name} is an OpenGL normal map. VFS/Uber expects DirectX normals: flip its green channel.", textures[(int)Slot.Normal]);
            }
            return true;
        }

        /// <summary>Base colour sRGB, normal as Normal Map, mask linear. Called only once all three are found.</summary>
        internal static void ApplyImportSettings(Texture[] textures)
        {
            for (int i = 0; i < textures.Length; i++)
            {
                string path = AssetDatabase.GetAssetPath(textures[i]);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;

                bool changed = false;
                switch ((Slot)i)
                {
                    case Slot.BaseColor:
                        changed = Set(importer.textureType != TextureImporterType.Default || !importer.sRGBTexture, () =>
                        {
                            importer.textureType = TextureImporterType.Default;
                            importer.sRGBTexture = true;
                        });
                        break;
                    case Slot.Normal:
                        changed = Set(importer.textureType != TextureImporterType.NormalMap, () => importer.textureType = TextureImporterType.NormalMap);
                        break;
                    case Slot.Mask:
                        changed = Set(importer.textureType != TextureImporterType.Default || importer.sRGBTexture, () =>
                        {
                            importer.textureType = TextureImporterType.Default;
                            importer.sRGBTexture = false;
                        });
                        break;
                }
                if (changed) importer.SaveAndReimport();
            }
        }

        private static bool Set(bool needed, Action apply)
        {
            if (needed) apply();
            return needed;
        }

        private static void SetMaterialLayer(int layerIndex)
        {
            Shader uberShader = Shader.Find(UberShaderName);
            if (uberShader == null)
            {
                Debug.LogError($"Can't assign the layer: the {UberShaderName} shader isn't in this project.");
                return;
            }
            Texture[] selected = Selection.GetFiltered<Texture>(SelectionMode.Assets);
            if (!TryMatchTextures(selected, out Texture[] textures, out string error))
            {
                Debug.LogWarning(error);
                return;
            }

            Material[] selectedMaterials = Selection.GetFiltered<Material>(SelectionMode.Assets);
            if (selectedMaterials.Length == 0)
            {
                Debug.LogWarning("Select the material(s) to assign the layer to, together with the 3 textures.");
                return;
            }

            ApplyImportSettings(textures);
            foreach (Material mat in selectedMaterials)
            {
                if (mat.shader != uberShader)
                {
                    Debug.LogWarning($"Material {mat.name} is not using the {UberShaderName} shader.", mat);
                    continue;
                }
                AssignLayer(mat, layerIndex, textures);
                Debug.Log($"Layer [{_layerNames[layerIndex]}] assigned to material: {mat.name}", mat);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Sets the layer's textures and raises Enabled Layers to include the layer (never lowers it). Undoable.
        /// </summary>
        internal static void AssignLayer(Material material, int layerIndex, Texture[] textures)
        {
            Undo.RecordObject(material, $"Assign {_layerNames[layerIndex]} Layer");
            for (int i = 0; i < _parameterNames.GetLength(1); i++)
            {
                if (material.HasProperty(_parameterNames[layerIndex, i]))
                {
                    material.SetTexture(_parameterNames[layerIndex, i], textures[i]);
                }
            }

            if (material.HasProperty(EnabledLayersProperty))
            {
                int current = Mathf.Clamp(Mathf.RoundToInt(material.GetFloat(EnabledLayersProperty)), 0, EnabledLayersKeywords.Length - 1);
                SetEnabledLayers(material, Mathf.Max(current, layerIndex));
            }
            EditorUtility.SetDirty(material);
        }

        internal static void SetEnabledLayers(Material material, int index)
        {
            material.SetFloat(EnabledLayersProperty, index);
            for (int i = 0; i < EnabledLayersKeywords.Length; i++)
            {
                var keyword = new LocalKeyword(material.shader, EnabledLayersKeywords[i]);
                if (keyword.isValid) material.SetKeyword(keyword, i == index);
                else if (i == index) material.EnableKeyword(EnabledLayersKeywords[i]);
                else material.DisableKeyword(EnabledLayersKeywords[i]);
            }
        }
    }
}
