using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UnityTools.Messaging.Tests
{
    // GlobalVariable.Tag is set by the constructor. These check it is still there after Unity rebuilds the object.
    public class GlobalVariableSerializationTests
    {
        private const string AssetPath = "Assets/__GlobalVariableSerializationTest.asset";

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(AssetPath);

        [Test]
        public void Tag_IsSetOnANewObject()
        {
            var holder = ScriptableObject.CreateInstance<GlobalVariableHolder>();
            try
            {
                Assert.AreEqual(HolderTag.Score, holder.Score.Tag);
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void Tag_SurvivesInstantiate()
        {
            var holder = ScriptableObject.CreateInstance<GlobalVariableHolder>();
            GlobalVariableHolder clone = Object.Instantiate(holder);
            try
            {
                Assert.AreEqual(HolderTag.Score, clone.Score.Tag);
            }
            finally
            {
                Object.DestroyImmediate(holder);
                Object.DestroyImmediate(clone);
            }
        }

        [Test]
        public void Tag_SurvivesCopySerialized()
        {
            var source = ScriptableObject.CreateInstance<GlobalVariableHolder>();
            var target = ScriptableObject.CreateInstance<GlobalVariableHolder>();
            try
            {
                EditorUtility.CopySerialized(source, target);
                Assert.AreEqual(HolderTag.Score, target.Score.Tag);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
            }
        }

        // saved to disk, unloaded, and loaded back: the same deserialization as loading a scene or prefab
        [Test]
        public void Tag_SurvivesSaveAndReloadFromDisk()
        {
            var holder = ScriptableObject.CreateInstance<GlobalVariableHolder>();
            AssetDatabase.CreateAsset(holder, AssetPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(holder);

            var loaded = AssetDatabase.LoadAssetAtPath<GlobalVariableHolder>(AssetPath);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(HolderTag.Score, loaded.Score.Tag);
        }
    }
}
