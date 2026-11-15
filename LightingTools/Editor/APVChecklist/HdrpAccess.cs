#if HDRP
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace LightingTools.Editor.APVChecklist
{
    /// <summary>
    /// HDRP and APV settings the checklist reads and writes. Public APIs are used where they exist; everything
    /// else goes through guarded reflection or SerializedObject paths that return null/false when HDRP changes,
    /// so callers can fall back to opening the right settings page instead of failing silently.
    /// </summary>
    internal static class HdrpAccess
    {
        private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // ---------- HDRP assets ----------

        /// <summary>The default pipeline asset plus every quality level's override, without duplicates.</summary>
        public static List<HDRenderPipelineAsset> GetAllHdrpAssets()
        {
            var assets = new List<HDRenderPipelineAsset>();
            void Add(RenderPipelineAsset asset)
            {
                if (asset is HDRenderPipelineAsset hd && !assets.Contains(hd)) assets.Add(hd);
            }

            Add(GraphicsSettings.defaultRenderPipeline);
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                Add(QualitySettings.GetRenderPipelineAssetAt(i));
            }
            return assets;
        }

        public static void SetLightProbeSystemToApv(HDRenderPipelineAsset asset)
        {
            Undo.RecordObject(asset, "Use Adaptive Probe Volumes");
            RenderPipelineSettings settings = asset.currentPlatformRenderPipelineSettings;
            settings.lightProbeSystem = RenderPipelineSettings.LightProbeSystem.AdaptiveProbeVolumes;
            asset.currentPlatformRenderPipelineSettings = settings;
            EditorUtility.SetDirty(asset);
        }

        public static void SetShadowmaskSupport(HDRenderPipelineAsset asset, bool enabled)
        {
            Undo.RecordObject(asset, "Enable Shadowmask Support");
            RenderPipelineSettings settings = asset.currentPlatformRenderPipelineSettings;
            settings.supportShadowMask = enabled;
            asset.currentPlatformRenderPipelineSettings = settings;
            EditorUtility.SetDirty(asset);
        }

        // ---------- Default frame settings (internal in HDRP 17, read through the serialized asset) ----------

        public static Object GetGlobalSettingsAsset()
        {
            Object settings = GraphicsSettings.GetSettingsForRenderPipeline<HDRenderPipeline>();
            if (settings != null) return settings;

            string guid = AssetDatabase.FindAssets("t:HDRenderPipelineGlobalSettings").FirstOrDefault();
            return guid == null ? null : AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
        }

        /// <summary>
        /// Reads a default Camera frame setting. Returns null when the serialized layout couldn't be found
        /// (HDRP changed), so the caller can say so instead of reporting a wrong value.
        /// </summary>
        public static bool? GetDefaultCameraFrameSetting(FrameSettingsField field)
        {
            if (!TryFindCameraFrameSettingsBits(out SerializedObject so, out SerializedProperty data1, out SerializedProperty data2)) return null;
            int bit = (int)field;
            ulong value = bit < 64 ? data1.ulongValue : data2.ulongValue;
            so.Dispose();
            return (value & (1UL << (bit % 64))) != 0;
        }

        public static bool SetDefaultCameraFrameSetting(FrameSettingsField field, bool enabled)
        {
            if (!TryFindCameraFrameSettingsBits(out SerializedObject so, out SerializedProperty data1, out SerializedProperty data2)) return false;
            int bit = (int)field;
            SerializedProperty property = bit < 64 ? data1 : data2;
            ulong mask = 1UL << (bit % 64);
            property.ulongValue = enabled ? property.ulongValue | mask : property.ulongValue & ~mask;
            so.ApplyModifiedProperties(); // records Undo
            so.Dispose();
            return true;
        }

        private static bool TryFindCameraFrameSettingsBits(out SerializedObject so, out SerializedProperty data1, out SerializedProperty data2)
        {
            so = null;
            data1 = data2 = null;
            Object asset = GetGlobalSettingsAsset();
            if (asset == null) return false;

            so = new SerializedObject(asset);
            SerializedProperty iterator = so.GetIterator();
            while (iterator.Next(true))
            {
                string path = iterator.propertyPath;
                if (path.Contains("Obsolete")) continue;
                if (path.EndsWith("m_Camera.bitDatas.data1")) data1 = iterator.Copy();
                else if (path.EndsWith("m_Camera.bitDatas.data2")) data2 = iterator.Copy();
                if (data1 != null && data2 != null) return true;
            }

            so.Dispose();
            so = null;
            return false;
        }

        public static void OpenHdrpGraphicsSettings()
        {
            SettingsService.OpenProjectSettings("Project/Graphics/Pipeline Specific Settings/HDRP");
        }

        // ---------- Volumes ----------

        public static VolumeProfile GetDefaultVolumeProfile()
        {
            try
            {
                return GraphicsSettings.GetRenderPipelineSettings<HDRPDefaultVolumeProfileSettings>()?.volumeProfile;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Adds an override to a profile asset, persisting the component as a sub-asset like the Volume inspector does.</summary>
        public static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            Undo.RecordObject(profile, $"Add {typeof(T).Name}");
            T component = profile.Add<T>(true);
            if (EditorUtility.IsPersistent(profile))
            {
                AssetDatabase.AddObjectToAsset(component, profile);
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            }
            Undo.RegisterCreatedObjectUndo(component, $"Add {typeof(T).Name}");
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            return component;
        }

        // ---------- Baking sets ----------

        public static List<ProbeVolumeBakingSet> FindAllBakingSets()
        {
            return AssetDatabase.FindAssets("t:ProbeVolumeBakingSet")
                .Select(guid => AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(set => set != null)
                .ToList();
        }

        /// <summary>
        /// The saved Baking Set asset that lists the scene. Uses HDRP's own scene-to-set lookup when it points at a saved
        /// asset that really lists the scene, and otherwise searches the assets' scene lists. Temporary sets made by
        /// the Lighting window's Single Scene mode are never returned: they aren't saved and vanish when it switches.
        /// </summary>
        public static ProbeVolumeBakingSet FindBakingSetForScene(IEnumerable<ProbeVolumeBakingSet> sets, string sceneGuid)
        {
            if (string.IsNullOrEmpty(sceneGuid)) return null;
            ProbeVolumeBakingSet registered = GetRegisteredBakingSet(sceneGuid);
            if (registered != null && !IsTemporary(registered) && registered.sceneGUIDs.Contains(sceneGuid)) return registered;
            return sets.FirstOrDefault(set => set.sceneGUIDs.Contains(sceneGuid));
        }

        /// <summary>
        /// The set HDRP's in-memory scene-to-set lookup maps the scene to. This is what TryAddScene checks, so it can
        /// differ from the saved assets (e.g. a temporary Single Scene set). Null when unmapped or HDRP changed.
        /// </summary>
        public static ProbeVolumeBakingSet GetRegisteredBakingSet(string sceneGuid)
        {
            try
            {
                MethodInfo lookup = typeof(ProbeVolumeBakingSet).GetMethod("GetBakingSetForScene",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                return lookup?.Invoke(null, new object[] { sceneGuid }) as ProbeVolumeBakingSet;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>An unsaved set, like the one the Lighting window makes in Single Scene mode.</summary>
        public static bool IsTemporary(ProbeVolumeBakingSet set) => set != null && !EditorUtility.IsPersistent(set);

        /// <summary>
        /// If HDRP maps the scene to a temporary (unsaved) set, takes it out of that set so TryAddScene accepts it.
        /// Returns the saved set that still claims the scene, if any, so the caller can report it.
        /// </summary>
        public static ProbeVolumeBakingSet ReleaseFromTemporarySet(string sceneGuid)
        {
            ProbeVolumeBakingSet registered = GetRegisteredBakingSet(sceneGuid);
            if (registered == null) return null;
            if (!IsTemporary(registered)) return registered;
            registered.RemoveScene(sceneGuid);
            return null;
        }

        // ---------- Lighting window APV tab (internal ProbeVolumeLightingTab) ----------

        private static object GetLightingTab(out Type tabType)
        {
            tabType = typeof(AdaptiveProbeVolumes).Assembly.GetType("UnityEngine.Rendering.ProbeVolumeLightingTab");
            return tabType?.GetField("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        }

        /// <summary>
        /// Whether the Lighting window's Adaptive Probe Volumes tab is in Single Scene baking mode. In that mode it
        /// keeps a temporary set for the active scene and takes that scene out of multi-scene sets. Null if unknown.
        /// </summary>
        public static bool? IsLightingTabInSingleSceneMode()
        {
            try
            {
                object tab = GetLightingTab(out Type tabType);
                if (tab == null) return null;
                return tabType.GetField("m_SingleSceneMode", AnyInstance)?.GetValue(tab) as bool?;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Makes the set active in the Lighting window and in APV, the way picking it in the Lighting window does.
        /// That drops the tab's temporary set and switches it to Baking Set mode (the set has singleSceneMode off).
        /// Returns false when HDRP's internals changed; the caller then asks the user to pick the set by hand.
        /// </summary>
        public static bool MakeActiveBakingSet(ProbeVolumeBakingSet set)
        {
            try
            {
                Undo.RecordObject(set, "Use Baking Set");
                SetFieldIfPresent(set, "singleSceneMode", false);
                EditorUtility.SetDirty(set);

                object tab = GetLightingTab(out Type tabType);
                PropertyInfo activeSet = tabType?.GetProperty("activeSet", AnyInstance);
                if (tab == null || activeSet == null) return false;
                activeSet.SetValue(tab, set);
                tabType.GetField("m_SingleSceneMode", AnyInstance)?.SetValue(tab, false);

                MethodInfo setActive = typeof(ProbeReferenceVolume).GetMethod("SetActiveBakingSet", AnyInstance, null, new[] { typeof(ProbeVolumeBakingSet) }, null);
                if (setActive != null)
                {
                    typeof(ProbeReferenceVolume).GetMethod("Clear", AnyInstance, null, Type.EmptyTypes, null)?.Invoke(ProbeReferenceVolume.instance, null);
                    setActive.Invoke(ProbeReferenceVolume.instance, new object[] { set });
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"APV Bake Checklist: couldn't switch the Lighting window to '{set.name}' ({e.GetBaseException().Message}).");
                return false;
            }
        }

        /// <summary>Creates a baking set with HDRP's defaults (SetDefaults is internal, so it's called by reflection).</summary>
        public static ProbeVolumeBakingSet CreateBakingSet(string assetPath)
        {
            var set = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
            set.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            InvokeIfPresent(set, "SetDefaults");
            SetFieldIfPresent(set, "singleSceneMode", false);
            AssetDatabase.CreateAsset(set, AssetDatabase.GenerateUniqueAssetPath(assetPath));
            Undo.RegisterCreatedObjectUndo(set, "Create Baking Set");
            return set;
        }

        /// <summary>Disk size of the set's baked data in bytes, or -1 when HDRP's internal size methods aren't found.</summary>
        public static long GetBakedDataSize(ProbeVolumeBakingSet set)
        {
            try
            {
                Type type = typeof(ProbeVolumeBakingSet);
                MethodInfo shared = type.GetMethod("GetDiskSizeOfSharedData", AnyInstance);
                MethodInfo scenario = type.GetMethod("GetDiskSizeOfScenarioData", AnyInstance);
                if (shared == null || scenario == null) return -1;

                long total = (long)shared.Invoke(set, null);
                foreach (string name in set.lightingScenarios)
                {
                    total += (long)scenario.Invoke(set, new object[] { name });
                }
                return total;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        public static bool HasBakedData(ProbeVolumeBakingSet set)
        {
            return set.lightingScenarios.Any(set.HasBakedData);
        }

        // ---------- Windows ----------

        public static void OpenLightingWindow()
        {
            EditorApplication.ExecuteMenuItem("Window/Rendering/Lighting");
        }

        public static void OpenRenderingDebugger()
        {
            EditorApplication.ExecuteMenuItem("Window/Analysis/Rendering Debugger");
        }

        // ---------- reflection helpers ----------

        private static void InvokeIfPresent(object target, string method)
        {
            try
            {
                target.GetType().GetMethod(method, AnyInstance, null, Type.EmptyTypes, null)?.Invoke(target, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"APV Bake Checklist: couldn't call {target.GetType().Name}.{method} ({e.GetBaseException().Message}). Check the new asset in the Lighting window.");
            }
        }

        private static void SetFieldIfPresent(object target, string field, object value)
        {
            try
            {
                target.GetType().GetField(field, AnyInstance)?.SetValue(target, value);
            }
            catch (Exception)
            {
                // Optional setting; the Lighting window shows the real value.
            }
        }
    }
}
#endif
