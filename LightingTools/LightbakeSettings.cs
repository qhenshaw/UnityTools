using UnityEngine;
using UnityEngine.Rendering;
using InspectorAttributes;

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif

namespace LightingTools
{
    /// <summary>
    /// Applies static flags, Receive GI and shadow settings to every MeshRenderer and Terrain below this object
    /// when you press Apply. Nested LightbakeSettings components use their own settings for their children.
    /// </summary>
    [DisallowMultipleComponent]
    public class LightbakeSettings : MonoBehaviour
    {
        // Serialized fields live outside #if UNITY_EDITOR so the serialization layout is identical in the
        // Editor and in players (otherwise builds log "different serialization layout" errors).
        // Static flags are stored as an int because StaticEditorFlags is an Editor-only type;
        // the serialized data is the same as the enum it replaces.
        /// <summary>Starting values for the settings. Choosing a preset overwrites the fields below it; press Apply to use them.</summary>
        public enum LightBakePreset
        {
            FullStatic,
            StaticNoShadows,
            FullDynamic,
            DynamicNoShadows,
            DynamicBakeReflections
        }

        [SerializeField] private LightBakePreset _preset;
        [SerializeField, StaticEditorFlagsField] private int _flags;
        [SerializeField] private ReceiveGI _bakeMode = ReceiveGI.LightProbes;
        [SerializeField] private ShadowCastingMode _shadowCastingMode = ShadowCastingMode.On;
        [SerializeField] private bool _staticShadowCaster = true;
#pragma warning disable CS0414
        [SerializeField, Button("Apply", true, Method = nameof(ApplyCurrentSettings))] private string _applyButton = nameof(ApplyCurrentSettings);
#pragma warning restore CS0414

        // Serialized so a domain reload doesn't look like a preset change and overwrite hand-tuned values.
        [SerializeField, HideInInspector] private LightBakePreset _previousPreset;

        /// <summary>The selected preset.</summary>
        public LightBakePreset Preset => _preset;

#if UNITY_EDITOR
        private const StaticEditorFlags StaticFlags =
            StaticEditorFlags.ContributeGI |
            StaticEditorFlags.BatchingStatic |
            StaticEditorFlags.ReflectionProbeStatic |
            StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic;

        /// <summary>The static flags Apply sets on each object. Editor only.</summary>
        public StaticEditorFlags Flags => (StaticEditorFlags)_flags;

        private void Reset()
        {
            _preset = LightBakePreset.FullStatic;
            ApplyPreset();
            _previousPreset = _preset;
        }

        private void OnValidate()
        {
            if (_previousPreset != _preset)
            {
                ApplyPreset();
                _previousPreset = _preset;
            }
        }

        private void ApplyPreset()
        {
            switch (_preset)
            {
                case LightBakePreset.FullStatic:
                    _flags = (int)StaticFlags;
                    _bakeMode = ReceiveGI.LightProbes;
                    _shadowCastingMode = ShadowCastingMode.On;
                    _staticShadowCaster = true;
                    break;
                case LightBakePreset.FullDynamic:
                    _flags = 0;
                    _bakeMode = ReceiveGI.LightProbes;
                    _shadowCastingMode = ShadowCastingMode.On;
                    _staticShadowCaster = false;
                    break;
                case LightBakePreset.StaticNoShadows:
                    _flags = (int)StaticFlags;
                    _bakeMode = ReceiveGI.LightProbes;
                    _shadowCastingMode = ShadowCastingMode.Off;
                    _staticShadowCaster = false;
                    break;
                case LightBakePreset.DynamicNoShadows:
                    _flags = 0;
                    _bakeMode = ReceiveGI.LightProbes;
                    _shadowCastingMode = ShadowCastingMode.Off;
                    _staticShadowCaster = false;
                    break;
                case LightBakePreset.DynamicBakeReflections:
                    _flags = (int)StaticEditorFlags.ReflectionProbeStatic;
                    _bakeMode = ReceiveGI.LightProbes;
                    _shadowCastingMode = ShadowCastingMode.On;
                    _staticShadowCaster = false;
                    break;
            }
        }

        /// <summary>
        /// Applies this component's settings to every MeshRenderer and Terrain below it, and lets nested
        /// LightbakeSettings apply theirs to their own subtrees. Objects without a MeshRenderer or Terrain
        /// are left untouched. SkinnedMeshRenderers are skipped on purpose. Terrains get only the static flags
        /// and shadow casting mode. Undoable, and recorded as prefab overrides on prefab instances.
        /// Does nothing in a player build.
        /// </summary>
        public void ApplyCurrentSettings()
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"Apply Lightbake Settings ({name})");

            int count = 0;
            foreach (LightbakeSettings settings in GetComponentsInChildren<LightbakeSettings>(true))
            {
                count += settings.ApplyToOwnedObjects();
            }

            Undo.CollapseUndoOperations(group);
            Debug.Log($"Lightbake settings applied to {count} object(s) under: {gameObject.name}", gameObject);
        }

        /// <summary>Applies to renderers/terrains whose nearest LightbakeSettings ancestor is this one.</summary>
        private int ApplyToOwnedObjects()
        {
            int count = 0;
            var visited = new HashSet<GameObject>();

            foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.GetComponentInParent<LightbakeSettings>(true) != this) continue;
                if (!visited.Add(renderer.gameObject)) continue;

                Undo.RecordObject(renderer, "Apply Lightbake Settings");
                renderer.receiveGI = _bakeMode;
                renderer.shadowCastingMode = _shadowCastingMode;
                renderer.staticShadowCaster = _staticShadowCaster;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);

                ApplyStaticFlags(renderer.gameObject);
                count++;
            }

            foreach (Terrain terrain in GetComponentsInChildren<Terrain>(true))
            {
                if (terrain.GetComponentInParent<LightbakeSettings>(true) != this) continue;
                if (!visited.Add(terrain.gameObject)) continue;

                Undo.RecordObject(terrain, "Apply Lightbake Settings");
                terrain.shadowCastingMode = _shadowCastingMode;
                PrefabUtility.RecordPrefabInstancePropertyModifications(terrain);

                ApplyStaticFlags(terrain.gameObject);
                count++;
            }

            return count;
        }

        private void ApplyStaticFlags(GameObject go)
        {
            Undo.RecordObject(go, "Apply Lightbake Settings");
            GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)_flags);
            PrefabUtility.RecordPrefabInstancePropertyModifications(go);
        }
#else
        public void ApplyCurrentSettings() { }
#endif
    }

    /// <summary>Draws an int field as a StaticEditorFlags mask in the inspector.</summary>
    public class StaticEditorFlagsFieldAttribute : PropertyAttribute { }
}
