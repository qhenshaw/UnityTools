using UnityEngine;
using System;
using InspectorAttributes;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityTools.Data
{
    [Serializable]
    public class DataReference<T> where T : ScriptableObject
    {
#pragma warning disable CS0414
        [SerializeField, Button("Create New", true, 80)] private string _createNewButton = nameof(CreateNew);
#pragma warning restore CS0414
        [field: SerializeField]
        public T Persistent { get; private set; }

        [SerializeField]
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