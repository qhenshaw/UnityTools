using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

namespace UnityTools.Art
{
    /// <summary>
    /// An asset that bakes a gradient into a texture file. Create it with Create > Color Gradient Editor,
    /// then press Bake in its inspector.
    /// </summary>
    [CreateAssetMenu(menuName = "Color Gradient Editor")]
    public class GradientEditor : ScriptableObject
    {
        /// <summary>The image format the baked texture is saved as.</summary>
        public enum FileType { PNG, TGA, EXR }

        [SerializeField, GradientUsage(true)] private Gradient _gradient;
        [SerializeField] private Vector2Int _size = new Vector2Int(128, 4);
        [SerializeField] private bool _flip;
        [SerializeField, Tooltip("Gradient runs bottom to top instead of left to right")] private bool _vertical;

        [Header("Export")]
        [SerializeField] private FileType _fileType = FileType.PNG;
        [SerializeField, Tooltip("Ignored for EXR, which Unity always imports as linear")] private bool _sRGB = true;
        [SerializeField] private TextureWrapMode _wrapMode = TextureWrapMode.Clamp;
        [SerializeField] private bool _generateMipmaps = false;

        /// <summary>
        /// Where pixel i samples the gradient, from 0 (first pixel) to 1 (last pixel), so both end colours appear.
        /// A texture one pixel long uses the start colour.
        /// </summary>
        internal static float SamplePosition(int index, Vector2Int size, bool vertical, bool flip)
        {
            int position = vertical ? index / size.x : index % size.x;
            int length = vertical ? size.y : size.x;
            float percentage = length > 1 ? (float)position / (length - 1) : 0f;
            return flip ? 1f - percentage : percentage;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Saves the gradient as a texture next to this asset, named after it (overwriting any earlier bake),
        /// and applies the sRGB, wrap mode and mipmap import settings. Editor only.
        /// </summary>
        public void BakeGradient()
        {

            if (_size.x < 1 || _size.y < 1)
            {
                Debug.LogError("Please enter valid dimensions");
                return;
            }

            bool isEXR = _fileType == FileType.EXR;
            TextureFormat format = isEXR ? TextureFormat.RGBAFloat : TextureFormat.RGBA32;
            Texture2D newTexture = new Texture2D(_size.x, _size.y, format, false);
            Color[] colors = new Color[_size.x * _size.y];
            for (int i = 0; i < colors.Length; i++)
            {
                float percentage = SamplePosition(i, _size, _vertical, _flip);
                Color color = _gradient.Evaluate(percentage);
                colors[i] = isEXR ? color.linear : color;
            }
            newTexture.SetPixels(colors, 0);
            newTexture.Apply();

            byte[] bytes;
            string extension;
            switch (_fileType)
            {
                case FileType.TGA:
                    bytes = newTexture.EncodeToTGA();
                    extension = ".tga";
                    break;
                case FileType.EXR:
                    bytes = newTexture.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
                    extension = ".exr";
                    break;
                default:
                    bytes = newTexture.EncodeToPNG();
                    extension = ".png";
                    break;
            }
            DestroyImmediate(newTexture);

            string path = AssetDatabase.GetAssetPath(this);
            string directory = Path.GetDirectoryName(path);
            path = directory + "/" + name + extension;
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.sRGBTexture = _sRGB;
                importer.wrapMode = _wrapMode;
                importer.mipmapEnabled = _generateMipmaps;
                importer.SaveAndReimport();
            }

            Debug.Log($"Gradient Baked: {path}", AssetDatabase.LoadAssetAtPath(path, typeof(Texture2D)));
        }
#endif
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(GradientEditor))]
    public class GradientEditorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var editor = (GradientEditor)target;
            if (GUILayout.Button("Bake", GUILayout.Height(30)))
            {
                editor.BakeGradient();
            }
        }
    }
#endif
}