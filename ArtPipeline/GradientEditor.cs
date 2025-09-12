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
        [SerializeField] private Gradient _gradient;
        [SerializeField] private Vector2Int _size = new Vector2Int(128, 4);
        [SerializeField] private string _fileName = "New Gradient";

#if UNITY_EDITOR
        public void BakeGradient()
        {

            if (_size.x < 1 || _size.y < 1)
            {
                Debug.LogError("Please enter valid dimensions");
                return;
            }

            Texture2D newTexture = new Texture2D(_size.x, _size.y, TextureFormat.ARGB32, false);
            Color32[] colors = new Color32[_size.x * _size.y];
            for (int i = 0; i < colors.Length; i++)
            {
                int x = i % _size.x;
                float percentage = (float)x / _size.x;
                colors[i] = _gradient.Evaluate(percentage);
            }
            newTexture.SetPixels32(colors, 0);
            newTexture.Apply();

            byte[] bytes = newTexture.EncodeToPNG();
            string path = AssetDatabase.GetAssetPath(this);
            string directory = Path.GetDirectoryName(path);
            path = directory + "/" + _fileName + ".png";
            File.WriteAllBytes(path, bytes);
            AssetDatabase.Refresh();

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