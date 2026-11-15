#if HDRP
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace LightingTools.Editor.APVChecklist
{
    internal static class SceneCheckUtility
    {
        public static string SceneGuid(Scene scene) => AssetDatabase.AssetPathToGUID(scene.path);

        public static string SceneFolder(Scene scene)
        {
            return string.IsNullOrEmpty(scene.path) ? "Assets" : Path.GetDirectoryName(scene.path)?.Replace('\\', '/');
        }

        /// <summary>The distinct Lighting Settings assets of the loaded scenes (nulls left out).</summary>
        public static List<LightingSettings> DistinctLightingSettings(ChecklistContext context)
        {
            return context.Scenes.Select(Lightmapping.GetLightingSettingsForScene).Where(s => s != null).Distinct().ToList();
        }

        public static bool IsActiveGlobal(Volume volume)
        {
            return volume != null && volume.isActiveAndEnabled && volume.isGlobal && volume.sharedProfile != null;
        }
    }

    internal sealed class LightingSettingsAssignedCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Scene;
        public override string Id => "scene.lighting-settings";
        public override string Title => "Loaded scenes share one Lighting Settings asset";
        public override string Help =>
            "Each scene needs a Lighting Settings asset (Window > Rendering > Lighting > Scene). Scenes baked together should " +
            "use the same asset so they bake with the same settings.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            if (context.Scenes.Count == 0) return CheckResult.NotApplicable("No scene is loaded.");

            var missing = context.Scenes.Where(s => Lightmapping.GetLightingSettingsForScene(s) == null).ToList();
            var assets = SceneCheckUtility.DistinctLightingSettings(context);
            LightingSettings shared = assets.FirstOrDefault();

            if (missing.Count > 0)
            {
                string names = string.Join(", ", missing.Select(s => s.name));
                string fix = shared != null
                    ? $"Assign '{shared.name}' to {missing.Count} scene(s)"
                    : "Create an APV-ready Lighting Settings asset next to the active scene and assign it to every loaded scene";
                return CheckResult.Fail($"No Lighting Settings asset: {names}.")
                    .WithFix(fix, missing.Count, () =>
                    {
                        LightingSettings target = shared != null ? shared : CreateLightingSettings(SceneManager.GetActiveScene());
                        foreach (Scene scene in context.Scenes) Lightmapping.SetLightingSettingsForScene(scene, target);
                        EditorSceneManager.MarkAllScenesDirty();
                    });
            }

            if (assets.Count > 1)
            {
                return CheckResult.Warn($"The loaded scenes use {assets.Count} different Lighting Settings assets: {ChecklistContext.Names(assets)}.")
                    .WithTargets(assets)
                    .WithFix($"Assign '{shared.name}' (from {context.Scenes[0].name}) to every loaded scene", context.Scenes.Count, () =>
                    {
                        foreach (Scene scene in context.Scenes) Lightmapping.SetLightingSettingsForScene(scene, shared);
                        EditorSceneManager.MarkAllScenesDirty();
                    });
            }

            return CheckResult.Pass($"Using '{shared.name}'.").WithTargets(assets);
        }

        private static LightingSettings CreateLightingSettings(Scene scene)
        {
            var settings = new LightingSettings
            {
                name = $"{(string.IsNullOrEmpty(scene.name) ? "Scene" : scene.name)} Lighting Settings",
                bakedGI = true,
                realtimeGI = false,
                lightmapResolution = LightmapResolutionCheck.TargetResolution,
            };
            string path = AssetDatabase.GenerateUniqueAssetPath($"{SceneCheckUtility.SceneFolder(scene)}/{settings.name}.lighting");
            AssetDatabase.CreateAsset(settings, path);
            Undo.RegisterCreatedObjectUndo(settings, "Create Lighting Settings");
            Debug.Log($"APV Bake Checklist: created {path}.", settings);
            return settings;
        }
    }

    /// <summary>Base for checks that look at one property on every Lighting Settings asset in use.</summary>
    internal abstract class LightingSettingsPropertyCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Scene;

        protected abstract bool IsCorrect(LightingSettings settings);
        protected abstract void MakeCorrect(LightingSettings settings);
        protected abstract string WrongMessage { get; }
        protected abstract string FixLabel { get; }
        protected abstract string PassMessage { get; }
        protected virtual CheckStatus WrongStatus => CheckStatus.Warning;

        public override CheckResult Evaluate(ChecklistContext context)
        {
            var assets = SceneCheckUtility.DistinctLightingSettings(context);
            if (assets.Count == 0) return CheckResult.NotApplicable("Assign a Lighting Settings asset first.");

            var wrong = assets.Where(s => !IsCorrect(s)).ToList();
            if (wrong.Count == 0) return CheckResult.Pass(PassMessage).WithTargets(assets);

            var result = new CheckResult { Status = WrongStatus, Message = $"{WrongMessage} ({ChecklistContext.Names(wrong)})." };
            return result.WithTargets(wrong).WithFix($"{FixLabel} on {wrong.Count} Lighting Settings asset(s)", wrong.Count, () =>
            {
                foreach (LightingSettings settings in wrong) FixUtility.Modify(settings, FixLabel, MakeCorrect);
                AssetDatabase.SaveAssets();
            });
        }
    }

    internal sealed class BakedGICheck : LightingSettingsPropertyCheck
    {
        public override string Id => "scene.baked-gi";
        public override string Title => "Baked Global Illumination is on";
        public override string Help => "APV is baked by the Baked GI pipeline; with Baked Global Illumination off nothing is baked.";
        protected override bool IsCorrect(LightingSettings s) => s.bakedGI;
        protected override void MakeCorrect(LightingSettings s) => s.bakedGI = true;
        protected override string WrongMessage => "Baked Global Illumination is off, so APV can't bake";
        protected override string FixLabel => "Enable Baked Global Illumination";
        protected override string PassMessage => "Baked GI is on.";
        protected override CheckStatus WrongStatus => CheckStatus.Fail;
    }

    internal sealed class RealtimeGICheck : LightingSettingsPropertyCheck
    {
        public override string Id => "scene.realtime-gi";
        public override string Title => "Realtime Global Illumination is off";
        public override string Help => "Enlighten realtime GI builds its own lightmap-like data and isn't needed with APV.";
        protected override bool IsCorrect(LightingSettings s) => !s.realtimeGI;
        protected override void MakeCorrect(LightingSettings s) => s.realtimeGI = false;
        protected override string WrongMessage => "Realtime GI (Enlighten) is on and adds bake time and data";
        protected override string FixLabel => "Disable Realtime Global Illumination";
        protected override string PassMessage => "Realtime GI is off.";
    }

    internal sealed class LightmapResolutionCheck : LightingSettingsPropertyCheck
    {
        public const float TargetResolution = 0.0001f;

        public override string Id => "scene.lightmap-resolution";
        public override string Title => "Lightmap resolution is 0.0001";
        public override string Help =>
            "If any renderer is still set to receive GI from lightmaps, the baker builds lightmaps for it. A tiny lightmap " +
            "resolution keeps that accidental lightmapping from costing bake time and memory.";
        protected override bool IsCorrect(LightingSettings s) => s.lightmapResolution <= TargetResolution + 1e-7f;
        protected override void MakeCorrect(LightingSettings s) => s.lightmapResolution = TargetResolution;
        protected override string WrongMessage => "Lightmap resolution is above 0.0001, so stray lightmapped renderers waste bake time";
        protected override string FixLabel => "Set lightmap resolution to 0.0001";
        protected override string PassMessage => "Stray lightmapping stays negligible.";
    }

    internal sealed class LightingModeCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Scene;
        public override string Id => "scene.lighting-mode";
        public override string Title => "Lighting Mode fits the scene";
        public override string Help =>
            "Baked Indirect: probes give indirect light, all shadows stay realtime. Shadowmask: Mixed lights also bake " +
            "distant shadows into APV probe occlusion, which needs shadowmask support in the HDRP asset and frame settings " +
            "and costs extra memory. Subtractive isn't supported by HDRP.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            var assets = SceneCheckUtility.DistinctLightingSettings(context);
            if (assets.Count == 0) return CheckResult.NotApplicable("Assign a Lighting Settings asset first.");

            var modes = assets.Select(a => a.mixedBakeMode).Distinct().ToList();
            if (modes.Count > 1)
            {
                return CheckResult.Warn($"Lighting Settings assets disagree on Lighting Mode ({string.Join(", ", modes)}).").WithTargets(assets);
            }

            MixedLightingMode mode = modes[0];
            int mixedLights = context.Lights.Count(l => l.lightmapBakeType == LightmapBakeType.Mixed);

            if (mode == MixedLightingMode.Subtractive)
            {
                return CheckResult.Warn("Subtractive isn't supported by HDRP.").WithTargets(assets)
                    .WithFix("Set Lighting Mode to Baked Indirect", assets.Count, () =>
                    {
                        foreach (LightingSettings s in assets) FixUtility.Modify(s, "Set Lighting Mode", x => x.mixedBakeMode = MixedLightingMode.IndirectOnly);
                    });
            }

            if (mode == MixedLightingMode.Shadowmask)
            {
                var noSupport = HdrpAccess.GetAllHdrpAssets().Where(a => !a.currentPlatformRenderPipelineSettings.supportShadowMask).ToList();
                bool? frameSetting = HdrpAccess.GetDefaultCameraFrameSetting(FrameSettingsField.Shadowmask);
                var problems = new List<string>();
                if (noSupport.Count > 0) problems.Add($"shadowmask is off in {ChecklistContext.Names(noSupport)}");
                if (frameSetting == false) problems.Add("Shadowmask is off in the default Camera frame settings");

                if (problems.Count > 0)
                {
                    return CheckResult.Warn($"Lighting Mode is Shadowmask, but {string.Join(" and ", problems)}, so baked shadows won't show.")
                        .WithTargets(noSupport)
                        .WithFix("Enable shadowmask in the HDRP asset(s) and default frame settings", noSupport.Count + 1, () =>
                        {
                            foreach (HDRenderPipelineAsset asset in noSupport) HdrpAccess.SetShadowmaskSupport(asset, true);
                            if (frameSetting == false) HdrpAccess.SetDefaultCameraFrameSetting(FrameSettingsField.Shadowmask, true);
                            AssetDatabase.SaveAssets();
                        });
                }

                if (mixedLights == 0)
                {
                    return CheckResult.Info("Lighting Mode is Shadowmask, but there are no Mixed lights, so it has no effect. Baked Indirect would do the same with less data.");
                }

                return CheckResult.Pass($"Shadowmask: {mixedLights} Mixed light(s) bake distant shadows into probe occlusion.");
            }

            return CheckResult.Pass("Baked Indirect: probes give indirect light, shadows stay realtime.");
        }
    }

    internal sealed class StaticLightingSkyCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Scene;
        public override string Id => "scene.static-sky";
        public override string Title => "Every scene has a Static Lighting Sky";
        public override string Help =>
            "The sky that lights the bake comes from Lighting > Environment (Static Lighting Sky). Without one, APV bakes no sky " +
            "light. The fix uses a global Volume's profile from the loaded scenes, or the HDRP default volume profile.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            if (context.Scenes.Count == 0) return CheckResult.NotApplicable("No scene is loaded.");

            var missing = new List<Scene>();
            var found = new List<StaticLightingSky>();
            foreach (Scene scene in context.Scenes)
            {
                StaticLightingSky sky = LightingSettingsMenuItems.FindStaticLightingSkies(scene).FirstOrDefault(s => s.profile != null);
                if (sky == null || sky.staticLightingSkyUniqueID == 0) missing.Add(scene);
                else found.Add(sky);
            }

            if (missing.Count == 0)
            {
                return CheckResult.Pass($"Sky profile: {ChecklistContext.Names(found.Select(s => s.profile).Distinct())}.").WithTargets(found);
            }

            Volume source = context.Volumes.FirstOrDefault(v => SceneCheckUtility.IsActiveGlobal(v) && v.sharedProfile.Has<VisualEnvironment>());
            VolumeProfile profile = source != null ? source.sharedProfile : HdrpAccess.GetDefaultVolumeProfile();
            var result = CheckResult.Fail($"No Static Lighting Sky with a sky selected: {string.Join(", ", missing.Select(s => s.name))}.");
            if (profile == null) return result.WithUIFix("Open the Lighting window (Environment tab)", HdrpAccess.OpenLightingWindow);

            string from = source != null ? $"global Volume '{source.name}'" : "the HDRP default volume profile";
            return result.WithFix($"Use '{profile.name}' (from {from}) as the static sky in {missing.Count} scene(s)", missing.Count, () =>
            {
                foreach (Scene scene in missing) LightingSettingsMenuItems.AssignEnvironmentProfile(scene, profile);
                EditorSceneManager.MarkAllScenesDirty();
            });
        }
    }

    internal sealed class DuplicateStaticSkyCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Scene;
        public override string Id => "scene.duplicate-static-sky";
        public override string Title => "No duplicate Static Lighting Sky objects";
        public override string Help =>
            "Older versions of 'Assign Volume to Open Scenes' could create extra hidden Runtime-StaticLightingSky objects. " +
            "HDRP uses only one, so the others are clutter that can point at an old sky.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            var extras = new List<StaticLightingSky>();
            foreach (Scene scene in context.Scenes)
            {
                var skies = LightingSettingsMenuItems.FindStaticLightingSkies(scene);
                if (skies.Count < 2) continue;
                StaticLightingSky keep = skies.FirstOrDefault(s => s.profile != null) ?? skies[0];
                extras.AddRange(skies.Where(s => s != keep));
            }

            if (extras.Count == 0) return CheckResult.Pass("One Static Lighting Sky per scene.");

            return CheckResult.Warn($"{context.CountByScene(extras)} extra Static Lighting Sky component(s), most of them hidden in the Hierarchy.")
                .WithTargets(extras.Select(e => (Object)e.gameObject))
                .WithFix($"Remove {extras.Count} duplicate(s), keeping the one with a profile", extras.Count, () =>
                {
                    foreach (StaticLightingSky extra in extras)
                    {
                        GameObject go = extra.gameObject;
                        bool onlySky = go.GetComponents<Component>().Length == 2 && go.transform.childCount == 0;
                        if (onlySky) Undo.DestroyObjectImmediate(go);
                        else Undo.DestroyObjectImmediate(extra);
                    }
                    EditorSceneManager.MarkAllScenesDirty();
                });
        }
    }

    internal sealed class BakingSetCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Scene;
        public override string Id => "scene.baking-set";
        public override string Title => "Loaded scenes are in one Baking Set";
        public override string Help =>
            "APV bakes per Baking Set (Lighting > Adaptive Probe Volumes). Every loaded scene must be saved and in the same " +
            "saved set, or it's skipped by the bake. With several scenes loaded, the Lighting window must use Baking Set " +
            "mode: in Single Scene mode it keeps a temporary, unsaved set for the active scene and pulls that scene out of " +
            "multi-scene sets.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            if (context.Scenes.Count == 0) return CheckResult.NotApplicable("No scene is loaded.");

            var unsaved = context.Scenes.Where(s => string.IsNullOrEmpty(s.path)).ToList();
            if (unsaved.Count > 0)
            {
                return CheckResult.Fail("Save the scene before adding it to a Baking Set.")
                    .WithUIFix("Save the modified scenes (asks first)", () => EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo(), "Save");
            }

            var sets = HdrpAccess.FindAllBakingSets();
            var byScene = context.Scenes.ToDictionary(s => s, s => HdrpAccess.FindBakingSetForScene(sets, SceneCheckUtility.SceneGuid(s)));
            var missing = byScene.Where(p => p.Value == null).Select(p => p.Key).ToList();
            var used = byScene.Values.Where(v => v != null).Distinct().ToList();

            if (used.Count > 1)
            {
                return CheckResult.Warn($"The loaded scenes are split across {used.Count} Baking Sets ({ChecklistContext.Names(used)}); a bake only covers one set.")
                    .WithTargets(used)
                    .WithUIFix("Open the Lighting window to move scenes between sets", HdrpAccess.OpenLightingWindow);
            }

            if (missing.Count > 0)
            {
                ProbeVolumeBakingSet existing = used.FirstOrDefault();
                string fix = existing != null
                    ? $"Add {missing.Count} scene(s) to '{existing.name}' and use it in the Lighting window"
                    : "Create a Baking Set next to the active scene, add the loaded scenes and use it in the Lighting window";
                bool inTemporary = missing.Any(s => HdrpAccess.IsTemporary(HdrpAccess.GetRegisteredBakingSet(SceneCheckUtility.SceneGuid(s))));
                string why = inTemporary ? " (only in the Lighting window's temporary Single Scene set, which isn't saved)" : "";
                return CheckResult.Fail($"Not in any saved Baking Set: {string.Join(", ", missing.Select(s => s.name))}{why}.")
                    .WithFix(fix, missing.Count, () =>
                    {
                        Scene active = SceneManager.GetActiveScene();
                        ProbeVolumeBakingSet set = existing != null
                            ? existing
                            : HdrpAccess.CreateBakingSet($"{SceneCheckUtility.SceneFolder(active)}/{active.name} Baking Set.asset");
                        AddScenes(set, missing);
                    });
            }

            ProbeVolumeBakingSet bakingSet = used[0];
            if (context.IsMultiScene && HdrpAccess.IsLightingTabInSingleSceneMode() == true)
            {
                return CheckResult.Warn($"All scenes are in '{bakingSet.name}', but the Lighting window is in Single Scene mode, which takes the active scene back out of multi-scene sets.")
                    .WithTargets(new Object[] { bakingSet })
                    .WithFix($"Switch the Lighting window to Baking Set mode using '{bakingSet.name}'", 1, () => UseInLightingWindow(bakingSet));
            }

            return CheckResult.Pass($"'{bakingSet.name}': {bakingSet.sceneGUIDs.Count} scene(s), min probe spacing {bakingSet.minDistanceBetweenProbes} m.")
                .WithTargets(new Object[] { bakingSet });
        }

        /// <summary>
        /// Adds the scenes to the set and makes it the Lighting window's active set. Scenes that HDRP maps to the
        /// Lighting window's temporary Single Scene set are released from it first, otherwise TryAddScene refuses them.
        /// Throws (so the window shows the failure) when a scene still can't be added.
        /// </summary>
        private static void AddScenes(ProbeVolumeBakingSet set, List<Scene> scenes)
        {
            Undo.RecordObject(set, "Add Scenes to Baking Set");
            var failed = new List<string>();
            foreach (Scene scene in scenes)
            {
                string guid = SceneCheckUtility.SceneGuid(scene);
                if (set.sceneGUIDs.Contains(guid)) continue;

                ProbeVolumeBakingSet owner = HdrpAccess.ReleaseFromTemporarySet(guid);
                if (owner == set) continue;
                if (set.TryAddScene(guid))
                {
                    set.SetSceneBaking(guid, true);
                }
                else
                {
                    failed.Add(owner != null ? $"'{scene.name}' belongs to Baking Set '{owner.name}'" : $"HDRP refused '{scene.name}'");
                }
            }
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssetIfDirty(set);

            UseInLightingWindow(set);

            if (failed.Count > 0)
            {
                throw new System.InvalidOperationException($"Couldn't add every scene to '{set.name}': {string.Join("; ", failed)}. " +
                                                           "Move them in Window > Rendering > Lighting > Adaptive Probe Volumes.");
            }
        }

        private static void UseInLightingWindow(ProbeVolumeBakingSet set)
        {
            if (!HdrpAccess.MakeActiveBakingSet(set))
            {
                Debug.LogWarning($"APV Bake Checklist: set the Lighting window's Baking Mode to 'Baking Set' and pick '{set.name}' " +
                                 "(Window > Rendering > Lighting > Adaptive Probe Volumes).", set);
            }
            AssetDatabase.SaveAssetIfDirty(set);
        }
    }

    internal sealed class ProbeVolumeCoverageCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Scene;
        public override string Id => "scene.probe-volume";
        public override string Title => "Probe Volumes cover the static geometry";
        public override string Help =>
            "Probes are only placed inside Adaptive Probe Volumes. A Global volume covers every renderer; Local volumes " +
            "only cover what's inside their box. Uncovered objects get no baked lighting.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            var volumes = context.ProbeVolumes.Where(v => v.isActiveAndEnabled).ToList();
            var statics = StaticGeometry(context);

            if (volumes.Count == 0)
            {
                return CheckResult.Fail("No Adaptive Probe Volume in the loaded scenes, so no probes will be placed.")
                    .WithFix("Add a Global Probe Volume to the active scene", 1, () =>
                    {
                        GameObject go = ReflectionProbeHelper.CreateAtSelection("Probe Volume (Global)", Vector3.zero);
                        if (go.scene != SceneManager.GetActiveScene()) SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
                        ProbeVolume volume = Undo.AddComponent<ProbeVolume>(go);
                        volume.mode = ProbeVolume.Mode.Global;
                        Selection.activeGameObject = go;
                    });
            }

            var uncovered = statics.Where(r => !volumes.Any(v => Covers(v, r))).ToList();
            if (uncovered.Count > 0)
            {
                return CheckResult.Warn($"{context.CountByScene(uncovered)} static GI renderer(s) aren't inside any Probe Volume.")
                    .WithTargets(uncovered.Select(r => (Object)r.gameObject))
                    .WithFix($"Add a Local Probe Volume around the {uncovered.Count} uncovered renderer(s)", 1, () =>
                    {
                        if (!BoundsUtils.TryGetBounds(uncovered.OfType<Renderer>(), uncovered.OfType<Terrain>(), out Bounds bounds)) return;
                        bounds.size += Vector3.one * 4f;
                        GameObject go = ReflectionProbeHelper.CreateAtSelection("Probe Volume", bounds.center);
                        ProbeVolume volume = Undo.AddComponent<ProbeVolume>(go);
                        volume.mode = ProbeVolume.Mode.Local;
                        volume.size = bounds.size;
                        Selection.activeGameObject = go;
                    })
                    .NotInFixAll();
            }

            string modes = string.Join(", ", volumes.GroupBy(v => v.mode).Select(g => $"{g.Count()} {g.Key}"));
            return CheckResult.Pass($"{modes} volume(s) cover {statics.Count} static GI object(s).").WithTargets(volumes.Select(v => (Object)v.gameObject));
        }

        /// <summary>MeshRenderers and Terrains with Contribute GI, which are what APV bakes around.</summary>
        public static List<Component> StaticGeometry(ChecklistContext context)
        {
            var list = new List<Component>();
            list.AddRange(context.MeshRenderers.Where(r => r.enabled && r.gameObject.activeInHierarchy && ContributesGI(r.gameObject)));
            list.AddRange(context.Terrains.Where(t => t.enabled && t.gameObject.activeInHierarchy && ContributesGI(t.gameObject)));
            return list;
        }

        public static bool ContributesGI(GameObject go) => GameObjectUtility.AreStaticEditorFlagsSet(go, StaticEditorFlags.ContributeGI);

        private static bool Covers(ProbeVolume volume, Component target)
        {
            switch (volume.mode)
            {
                case ProbeVolume.Mode.Global:
                    return true;
                case ProbeVolume.Mode.Scene:
                    return volume.gameObject.scene == target.gameObject.scene;
                default:
                    Vector3 center = target is Renderer r ? r.bounds.center
                        : target is Terrain t && t.terrainData != null ? BoundsUtils.GetTerrainWorldBounds(t).center
                        : target.transform.position;
                    Vector3 local = volume.transform.InverseTransformPoint(center);
                    Vector3 half = volume.size * 0.5f;
                    return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
            }
        }
    }

    internal sealed class ProbeVolumesOptionsCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Scene;
        public override string Id => "scene.probe-volumes-options";
        public override string Title => "Probe Volumes Options override is set";
        public override string Help =>
            "The Probe Volumes Options volume override controls normal/view bias and leak reduction, the main tools against " +
            "light leaking through walls. It must be on a global Volume or in the HDRP default volume profile.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            foreach (Volume volume in context.Volumes.Where(SceneCheckUtility.IsActiveGlobal))
            {
                if (volume.sharedProfile.TryGet(out ProbeVolumesOptions options) && options.active)
                    return CheckResult.Pass($"On global Volume '{volume.name}': {Describe(options)}.").WithTargets(new Object[] { volume.gameObject });
            }

            VolumeProfile defaultProfile = HdrpAccess.GetDefaultVolumeProfile();
            if (defaultProfile != null && defaultProfile.TryGet(out ProbeVolumesOptions defaults) && defaults.active)
                return CheckResult.Pass($"In the HDRP default volume profile: {Describe(defaults)}.").WithTargets(new Object[] { defaultProfile });

            Volume target = context.Volumes.FirstOrDefault(SceneCheckUtility.IsActiveGlobal);
            VolumeProfile profile = target != null ? target.sharedProfile : defaultProfile;
            var result = CheckResult.Warn("No Probe Volumes Options override, so leak reduction and bias use HDRP's defaults.");
            if (profile == null) return result;

            string where = target != null ? $"global Volume '{target.name}' (profile '{profile.name}')" : "the HDRP default volume profile";
            return result.WithFix($"Add Probe Volumes Options to {where}", 1, () => HdrpAccess.AddOverride<ProbeVolumesOptions>(profile));
        }

        private static string Describe(ProbeVolumesOptions o)
        {
            return $"normal bias {o.normalBias.value:0.###}, view bias {o.viewBias.value:0.###}, leak reduction {o.leakReductionMode.value}";
        }
    }
}
#endif
