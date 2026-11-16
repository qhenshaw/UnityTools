#if HDRP
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace UnityTools.Lighting.Editor.APVChecklist
{
    internal sealed class ScenesSavedCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Bake;
        public override string Id => "bake.scenes-saved";
        public override string Title => "Scenes are saved";
        public override string Help => "APV writes its data next to the saved scenes; baking unsaved changes can leave data out of sync with the files.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            var dirty = context.Scenes.Where(s => s.isDirty || string.IsNullOrEmpty(s.path)).ToList();
            if (dirty.Count == 0) return CheckResult.Pass("All loaded scenes are saved.");
            return CheckResult.Warn($"Unsaved: {string.Join(", ", dirty.Select(s => string.IsNullOrEmpty(s.name) ? "Untitled" : s.name))}. Bake asks to save first.")
                .WithUIFix("Save the modified scenes (asks first)", () => EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo(), "Save");
        }
    }

    internal sealed class BakedDataCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Verify;
        public override string Id => "verify.baked-data";
        public override string Title => "Baking Set has baked data";
        public override string Help => "Shows whether the loaded scenes' Baking Set has been baked, and how much disk space its data takes.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            var sets = HdrpAccess.FindAllBakingSets();
            var used = context.Scenes
                .Select(s => HdrpAccess.FindBakingSetForScene(sets, SceneCheckUtility.SceneGuid(s)))
                .Where(s => s != null)
                .Distinct()
                .ToList();
            if (used.Count == 0) return CheckResult.NotApplicable("Add the scenes to a Baking Set first.");

            var unbaked = used.Where(s => !HdrpAccess.HasBakedData(s)).ToList();
            if (unbaked.Count > 0) return CheckResult.Fail($"Not baked yet: {ChecklistContext.Names(unbaked)}. Use Bake above.").WithTargets(unbaked);

            long bytes = used.Sum(s => System.Math.Max(0, HdrpAccess.GetBakedDataSize(s)));
            string size = used.Any(s => HdrpAccess.GetBakedDataSize(s) < 0) ? "size unknown" : EditorUtility.FormatBytes(bytes);
            return CheckResult.Pass($"Baked ({size} on disk).").WithTargets(used);
        }
    }

    internal sealed class NoLightmapsCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Verify;
        public override string Id => "verify.no-lightmaps";
        public override string Title => "The bake produced no lightmaps";
        public override string Help =>
            "With every renderer sampling probes, the bake should leave zero lightmaps. Listed renderers were given a " +
            "lightmap; set them to Light Probes and bake again.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            int lightmaps = LightmapSettings.lightmaps.Length;
            if (lightmaps == 0) return CheckResult.Pass("0 lightmaps.");

            var lightmapped = context.MeshRenderers.Where(r => r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE).ToList();
            var result = CheckResult.Fail($"{lightmaps} lightmap(s) in the lighting data; {context.CountByScene(lightmapped)} renderer(s) use one.")
                .WithTargets(lightmapped.Select(r => (Object)r.gameObject));
            if (lightmapped.Count == 0) return result;

            return result.WithFix($"Set {lightmapped.Count} renderer(s) to Light Probes (bake again afterwards)", lightmapped.Count, () =>
            {
                foreach (MeshRenderer renderer in lightmapped)
                    FixUtility.Modify(renderer, "Receive GI from Light Probes", r => r.receiveGI = ReceiveGI.LightProbes);
            });
        }
    }

    internal sealed class ReflectionProbesBakedCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Verify;
        public override string Id => "verify.reflection-probes";
        public override string Title => "Baked reflection probes are baked";
        public override string Help => "Baked reflection probes should be baked after APV so reflections include the probe lighting. Bake (all) does both in order.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            var baked = new List<HDAdditionalReflectionData>();
            foreach (ReflectionProbe probe in context.ReflectionProbes)
            {
                if (probe.isActiveAndEnabled && probe.TryGetComponent(out HDAdditionalReflectionData data) && data.mode == ProbeSettings.Mode.Baked) baked.Add(data);
            }
            if (baked.Count == 0) return CheckResult.NotApplicable("No baked reflection probes.");

            var missing = baked.Where(d => d.bakedTexture == null).ToList();
            if (missing.Count == 0) return CheckResult.Pass($"{baked.Count} baked reflection probe(s) have data.");
            return CheckResult.Warn($"{missing.Count} baked reflection probe(s) have no baked data. Use Bake (all).")
                .WithTargets(missing.Select(d => (Object)d.gameObject));
        }
    }

    internal static class ChecklistRegistry
    {
        public static List<SetupCheck> CreateAll()
        {
            return new List<SetupCheck>
            {
                new HdrpUsesApvCheck(),
                new FrameSettingsApvCheck(),

                new LightingSettingsAssignedCheck(),
                new BakedGICheck(),
                new RealtimeGICheck(),
                new LightmapResolutionCheck(),
                new LightingModeCheck(),
                new StaticLightingSkyCheck(),
                new DuplicateStaticSkyCheck(),
                new BakingSetCheck(),
                new ProbeVolumeCoverageCheck(),
                new ProbeVolumesOptionsCheck(),

                new ReceiveGIFromLightmapsCheck(),
                new ContributeGICheck(),
                new LightbakeSettingsCheck(),
                new BakedLightsCheck(),
                new RealtimeLightsCheck(),
                new EmissiveMaterialsCheck(),

                new ScenesSavedCheck(),

                new BakedDataCheck(),
                new NoLightmapsCheck(),
                new ReflectionProbesBakedCheck(),
                new ManualCheck(CheckSection.Verify, "verify.manual-leaks", "Checked probes and leaks in the debug view",
                    "Rendering Debugger > Probe Volume: turn on Display Probes and look for dark or bright probes inside walls " +
                    "and light leaking through thin geometry. Fix leaks with Probe Adjustment Volumes or the Probe Volumes " +
                    "Options bias values.",
                    "Open Rendering Debugger", HdrpAccess.OpenRenderingDebugger),
                new ManualCheck(CheckSection.Verify, "verify.manual-dynamic", "Dynamic objects pick up the baked light",
                    "Enter Play mode and move a dynamic object (a character) through bright and dark areas; it should brighten " +
                    "and darken with the bake."),
            };
        }
    }
}
#endif
