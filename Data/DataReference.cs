using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityTools.Data
{
    /// <summary>
    /// Non-generic base of <see cref="DataReference{T}"/>, used by the inspector drawer.
    /// </summary>
    [Serializable]
    public abstract class DataReferenceBase
    {
        /// <summary>The ScriptableObject type this reference holds.</summary>
        public abstract Type DataType { get; }

        /// <summary>The assigned asset as a ScriptableObject, without logging when it's missing.</summary>
        public abstract ScriptableObject PersistentObject { get; }

        /// <summary>The current runtime copy, or null if none has been made yet. Never creates one.</summary>
        public abstract ScriptableObject RuntimeObject { get; }
    }

    /// <summary>
    /// Wraps a ScriptableObject asset. Read <see cref="Persistent"/> for the asset itself, or
    /// <see cref="Runtime"/> for a copy that the game can change without editing the asset.
    /// </summary>
    /// <remarks>
    /// Each DataReference field gets its own runtime copy, including each instance of a prefab. Two fields
    /// that point at the same asset don't share changes: to show the player's data on a HUD, have the HUD
    /// read the player's DataReference. Outside Play mode, Runtime returns the asset itself. Copies are
    /// destroyed when Play mode ends, or when <see cref="ResetRuntime"/> is called.
    /// </remarks>
    [Serializable]
    public class DataReference<T> : DataReferenceBase where T : ScriptableObject
    {
        [SerializeField, FormerlySerializedAs("<Persistent>k__BackingField")]
        private T _persistent;

        [NonSerialized] private T _runtime;
        [NonSerialized] private T _runtimeSource;
        [NonSerialized] private bool _loggedMissing;

        public DataReference() { }

        /// <summary>Creates a reference to <paramref name="persistent"/>, e.g. for a DataReference made in code.</summary>
        public DataReference(T persistent) => _persistent = persistent;

        /// <summary>The assigned asset. Changes made to it in Play mode stay in the asset.</summary>
        public T Persistent
        {
            get
            {
                if (_persistent == null)
                {
                    LogMissing();
                    return null;
                }
                _loggedMissing = false;
                return _persistent;
            }
            set
            {
                _persistent = value;
                _loggedMissing = false;
            }
        }

        /// <summary>
        /// In Play mode, this field's own copy of the asset, made the first time it's read.
        /// Outside Play mode, the asset itself.
        /// </summary>
        public T Runtime
        {
            get
            {
                T persistent = Persistent;
                if (persistent == null) return null;
                if (!DataReferenceCopies.IsPlaying) return persistent;

                if (_runtime == null || _runtimeSource != persistent)
                {
                    DataReferenceCopies.Destroy(_runtime);
                    _runtime = Object.Instantiate(persistent);
                    _runtime.name = persistent.name + " (Runtime)";
                    _runtimeSource = persistent;
                    DataReferenceCopies.Track(_runtime);
                }
                return _runtime;
            }
        }

        /// <summary>True when an asset is assigned. Doesn't log an error when it isn't.</summary>
        public bool HasAsset => _persistent != null;

        /// <summary>
        /// Destroys this field's runtime copy. The next read of <see cref="Runtime"/> makes a fresh copy of the asset.
        /// </summary>
        public void ResetRuntime()
        {
            DataReferenceCopies.Destroy(_runtime);
            _runtime = null;
            _runtimeSource = null;
        }

        public override Type DataType => typeof(T);
        public override ScriptableObject PersistentObject => _persistent;
        public override ScriptableObject RuntimeObject => _runtime;

        private void LogMissing()
        {
            if (_loggedMissing) return;
            _loggedMissing = true;
            Debug.LogError($"DataReference<{typeof(T).Name}> has no asset assigned. Assign a {typeof(T).Name} asset to the field in the Inspector, or check HasAsset before reading it.");
        }

#if UNITY_EDITOR
        /// <summary>Creates a new asset of type <typeparamref name="U"/> at <paramref name="assetPath"/>.</summary>
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

    /// <summary>Keeps track of runtime copies so they can be destroyed when Play mode ends.</summary>
    internal static class DataReferenceCopies
    {
        private static readonly List<ScriptableObject> _copies = new();

        /// <summary>Lets EditMode tests act as if the game were playing.</summary>
        internal static bool? PlayingOverride;

        internal static bool IsPlaying => PlayingOverride ?? Application.isPlaying;

        internal static int Count
        {
            get
            {
                _copies.RemoveAll(copy => copy == null);
                return _copies.Count;
            }
        }

        internal static void Track(ScriptableObject copy) => _copies.Add(copy);

        internal static void Destroy(ScriptableObject copy)
        {
            if (copy == null) return;
            _copies.Remove(copy);
            if (Application.isPlaying) Object.Destroy(copy);
            else Object.DestroyImmediate(copy);
        }

        internal static void DestroyAll()
        {
            foreach (ScriptableObject copy in _copies)
            {
                if (copy != null) Object.DestroyImmediate(copy);
            }
            _copies.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _copies.Clear();

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void HookPlayModeExit()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) DestroyAll();
            };
        }
#endif
    }
}
