using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

namespace ArtPipeline
{
    [CreateAssetMenu(menuName = "Color Gradient Editor")]
    public class GradientEditor : ScriptableObject
    {
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

#if UNITY_EDITOR
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
                float percentage = _vertical
                    ? (float)(i / _size.x) / _size.y
                    : (float)(i % _size.x) / _size.x;
                if (_flip) percentage = 1f - percentage;
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