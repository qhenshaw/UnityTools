using UnityEngine;

#if UNITY_EDITOR
using InspectorAttributes;
using UnityEngine.Rendering;
using UnityEditor;
#endif

namespace LightingTools
{
    [DisallowMultipleComponent]
    public class LightbakeSettings : MonoBehaviour
    {
#if UNITY_EDITOR
        private enum LightBakePreset
        {
            FullStatic,
            StaticNoShadows,
            FullDynamic,
            DynamicNoShadows,
            DynamicBakeReflections
        }

        [SerializeField] private LightBakePreset _preset;
        [SerializeField] private StaticEditorFlags _flags;
        [SerializeField] private ReceiveGI _bakeMode = ReceiveGI.LightProbes;
        [SerializeField] private ShadowCastingMode _shadowCastingMode = ShadowCastingMode.On;
        [SerializeField] private bool _staticShadowCaster = true;
#pragma warning disable CS0414
        [SerializeField, Button("Apply", true)] private string _applyButton = nameof(ApplyCurrentSettings);
#pragma warning restore CS0414

        private LightBakePreset _previousPreset;

        private void Reset()
        {
            _preset = LightBakePreset.FullStatic;
            ApplyPreset();
        }

        private void OnValidate()
        {
            if(_previousPreset != _preset)
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
                    _flags = StaticEditorFlags.ContributeGI |
                             StaticEditorFlags.BatchingStatic |
                             StaticEditorFlags.ReflectionProbeStatic |
                             StaticEditorFlags.OccluderStatic |
                             StaticEditorFlags.OccludeeStatic;
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
                    _flags = StaticEditorFlags.ContributeGI |
                             StaticEditorFlags.BatchingStatic |
                             StaticEditorFlags.ReflectionProbeStatic |
                             StaticEditorFlags.OccluderStatic |
                             StaticEditorFlags.OccludeeStatic;
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
                    _flags = StaticEditorFlags.ReflectionProbeStatic;
                    _bakeMode = ReceiveGI.LightProbes;
                    _shadowCastingMode = ShadowCastingMode.On;
                    _staticShadowCaster = false;
                    break;
            }
        }

        public void ApplyCurrentSettings()
        {
            Debug.Log($"Lighting settings applied under object: {gameObject.name}", gameObject);
            Transform[] transforms = GetComponentsInChildren<Transform>();
            foreach (Transform t in transforms)
            {
                if (t.TryGetComponent(out MeshRenderer renderer))
                {
                    t.gameObject.isStatic = true;
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, _flags);
                    renderer.receiveGI = _bakeMode;
                    renderer.shadowCastingMode = _shadowCastingMode;
                    renderer.staticShadowCaster = _staticShadowCaster;
                }
                else
                {
                    t.gameObject.isStatic = false;
                }
            }

            LightbakeSettings[] childSettings = GetComponentsInChildren<LightbakeSettings>();
            foreach (LightbakeSettings child in childSettings)
            {
                if (child == this) continue;
                child.ApplyCurrentSettings();
            }
        }
#endif
    }
}