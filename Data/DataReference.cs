using Sirenix.OdinInspector;
using UnityEngine;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityTools.Data
{
    [Serializable, BoxGroup, InlineProperty, LabelWidth(100f)]
    public class DataReference<T> where T : ScriptableObject
    {
        [field: SerializeField, Required, InlineEditor, HideIf("_runtime")]
        [field: InlineButton("CreateNew", "New", Icon = SdfIconType.PlusSquare, ShowIf = "@!Persistent")]
        public T Persistent { get; private set; }

        [SerializeField, InlineEditor, ShowIf("_runtime")]
        private T _runtime;
        public T Runtime
        {
            get
            {
                if (_runtime == null) _runtime = GameObject.Instantiate(Persistent) as T;
                return _runtime;
            }
        }

        public void CreateNew()
        {
#if UNITY_EDITOR
            string path = EditorUtility.SaveFilePanelInProject("Save new Data", "New Data Asset", "asset", "");
            if (path.Length != 0)
            {
                CreateAt<T>(path);
                T dataAsset = AssetDatabase.LoadAssetAtPath<T>(path);
                Persistent = dataAsset;
            }
#endif
        }

#if UNITY_EDITOR
        public static U CreateAt<U>(string assetPath) where U : ScriptableObject
        {
            Type assetType = typeof(U);
            ScriptableObject asset = ScriptableObject.CreateInstance(assetType);
            if (asset == null)
            {
                Debug.LogError("failed to create instance of " + assetType.Name + " at " + assetPath);
                return null;
            }
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset as U;
        }
#endif
    }
}