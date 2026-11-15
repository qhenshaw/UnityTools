#if HDRP
using System.Linq;
using UnityEditor;
using UnityEngine.Rendering.HighDefinition;

namespace LightingTools.Editor.APVChecklist
{
    internal sealed class HdrpUsesApvCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Project;
        public override string Id => "project.hdrp-apv";
        public override string Title => "HDRP assets use Adaptive Probe Volumes";
        public override string Help =>
            "HDRP Asset > Lighting > Light Probe Lighting > Light Probe System must be Adaptive Probe Volumes on the default " +
            "pipeline asset and on every quality level's asset, or that quality level falls back to legacy light probes.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            var assets = HdrpAccess.GetAllHdrpAssets();
            if (assets.Count == 0) return CheckResult.Fail("No HDRP asset is assigned in Graphics or Quality settings.");

            var legacy = assets.Where(a => a.currentPlatformRenderPipelineSettings.lightProbeSystem != RenderPipelineSettings.LightProbeSystem.AdaptiveProbeVolumes).ToList();
            if (legacy.Count > 0)
            {
                return CheckResult.Fail($"Using legacy light probes: {ChecklistContext.Names(legacy)}.")
                    .WithTargets(legacy)
                    .WithFix($"Set Light Probe System to Adaptive Probe Volumes on {legacy.Count} HDRP asset(s)", legacy.Count, () =>
                    {
                        foreach (HDRenderPipelineAsset asset in legacy) HdrpAccess.SetLightProbeSystemToApv(asset);
                        AssetDatabase.SaveAssets();
                    });
            }

            RenderPipelineSettings first = assets[0].currentPlatformRenderPipelineSettings;
            return CheckResult.Pass($"{assets.Count} HDRP asset(s) use APV. {assets[0].name}: {first.probeVolumeSHBands}, memory budget {first.probeVolumeMemoryBudget}.")
                .WithTargets(assets);
        }
    }

    internal sealed class FrameSettingsApvCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Project;
        public override string Id => "project.frame-settings-apv";
        public override string Title => "APV enabled in default Camera Frame Settings";
        public override string Help =>
            "Project Settings > Graphics > HDRP > Frame Settings (Default Values) > Camera > Lighting > Adaptive Probe Volumes. " +
            "Cameras with custom frame settings can still override this.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            bool? enabled = HdrpAccess.GetDefaultCameraFrameSetting(FrameSettingsField.AdaptiveProbeVolume);
            if (enabled == null)
            {
                return CheckResult.Warn("Couldn't read the default frame settings from the HDRP Global Settings asset. Check them by hand.")
                    .WithUIFix("Open HDRP Graphics settings", HdrpAccess.OpenHdrpGraphicsSettings);
            }
            return enabled.Value
                ? CheckResult.Pass("Cameras sample APV by default.")
                : CheckResult.Fail("Adaptive Probe Volumes is off in the default Camera frame settings, so cameras ignore the bake.")
                    .WithFix("Enable Adaptive Probe Volumes in the default Camera frame settings", 1, () =>
                    {
                        if (!HdrpAccess.SetDefaultCameraFrameSetting(FrameSettingsField.AdaptiveProbeVolume, true))
                        {
                            UnityEngine.Debug.LogWarning("APV Bake Checklist: couldn't write the default frame settings; opening HDRP Graphics settings instead.");
                            HdrpAccess.OpenHdrpGraphicsSettings();
                        }
                    });
        }
    }
}
#endif
