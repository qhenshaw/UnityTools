#if HDRP
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LightingTools.Editor.APVChecklist
{
    internal sealed class ReceiveGIFromLightmapsCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Objects;
        public override string Id => "objects.receive-gi";
        public override string Title => "No renderer receives GI from lightmaps";
        public override string Help =>
            "A Contribute GI renderer set to Receive Global Illumination > Lightmaps gets a lightmap instead of sampling " +
            "APV. Set them to Light Probes so the bake produces no lightmaps.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            var lightmapped = context.MeshRenderers
                .Where(r => ProbeVolumeCoverageCheck.ContributesGI(r.gameObject) && r.receiveGI == ReceiveGI.Lightmaps)
                .ToList();

            if (lightmapped.Count == 0) return CheckResult.Pass("Every static GI renderer samples probes.");

            return CheckResult.Fail($"{context.CountByScene(lightmapped)} renderer(s) are set to receive GI from lightmaps.")
                .WithTargets(lightmapped.Select(r => (Object)r.gameObject))
                .WithFix($"Set {lightmapped.Count} renderer(s) to receive GI from Light Probes", lightmapped.Count, () =>
                {
                    foreach (MeshRenderer renderer in lightmapped)
                        FixUtility.Modify(renderer, "Receive GI from Light Probes", r => r.receiveGI = ReceiveGI.LightProbes);
                });
        }
    }

    internal sealed class ContributeGICheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Objects;
        public override string Id => "objects.contribute-gi";
        public override string Title => "Static geometry contributes GI";
        public override string Help =>
            "Only objects with the Contribute GI static flag block and bounce light in the bake. Listed here: objects " +
            "marked static in other ways (batching, occlusion) and terrains that don't contribute. This can be on purpose " +
            "(e.g. small props), so Fix All skips it.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            const StaticEditorFlags otherStatic = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.ReflectionProbeStatic;
            var candidates = new List<GameObject>();
            candidates.AddRange(context.MeshRenderers
                .Where(r => !ProbeVolumeCoverageCheck.ContributesGI(r.gameObject) && (GameObjectUtility.GetStaticEditorFlags(r.gameObject) & otherStatic) != 0)
                .Select(r => r.gameObject));
            var terrains = context.Terrains.Where(t => !ProbeVolumeCoverageCheck.ContributesGI(t.gameObject)).Select(t => t.gameObject).ToList();
            candidates.AddRange(terrains);
            candidates = candidates.Distinct().ToList();

            int staticCount = ProbeVolumeCoverageCheck.StaticGeometry(context).Count;
            if (staticCount == 0 && candidates.Count == 0)
            {
                return CheckResult.Fail("Nothing has Contribute GI, so the bake has no geometry to bounce light off.");
            }
            if (candidates.Count == 0) return CheckResult.Pass($"{staticCount} object(s) contribute GI.");

            var result = terrains.Count > 0
                ? CheckResult.Warn($"{terrains.Count} terrain(s) and {candidates.Count - terrains.Count} other static object(s) don't contribute GI.")
                : CheckResult.Info($"{candidates.Count} static object(s) don't contribute GI: {ChecklistContext.Names(candidates)}.");

            return result.WithTargets(candidates)
                .WithFix($"Add Contribute GI to {candidates.Count} object(s)", candidates.Count, () =>
                {
                    foreach (GameObject go in candidates)
                    {
                        FixUtility.Modify(go, "Add Contribute GI", g =>
                            GameObjectUtility.SetStaticEditorFlags(g, GameObjectUtility.GetStaticEditorFlags(g) | StaticEditorFlags.ContributeGI));
                    }
                })
                .NotInFixAll();
        }
    }

    internal sealed class LightbakeSettingsCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Objects;
        public override string Id => "objects.lightbake-settings";
        public override string Title => "LightbakeSettings components applied";
        public override string Help =>
            "LightbakeSettings only changes objects when you press Apply. Re-applying makes sure children added since then " +
            "follow their preset.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            if (context.LightbakeSettings.Count == 0) return CheckResult.NotApplicable("No LightbakeSettings components in the loaded scenes.");

            // Only top-level components: applying one already applies the nested ones under it.
            var roots = context.LightbakeSettings
                .Where(s => s.transform.parent == null || s.transform.parent.GetComponentInParent<LightbakeSettings>(true) == null)
                .ToList();

            return CheckResult.Info($"{context.CountByScene(context.LightbakeSettings)} LightbakeSettings component(s).")
                .WithTargets(context.LightbakeSettings.Select(s => (Object)s.gameObject))
                .WithFix($"Apply all {context.LightbakeSettings.Count} LightbakeSettings", context.LightbakeSettings.Count, () =>
                {
                    foreach (LightbakeSettings settings in roots) settings.ApplyCurrentSettings();
                })
                .NotInFixAll();
        }
    }

    internal sealed class RealtimeLightsCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Objects;
        public override string Id => "objects.realtime-lights";
        public override string Title => "Realtime lights";
        public override string Help =>
            "Realtime lights don't contribute to the APV bake. That's often intended (flickering, moving or gameplay lights), " +
            "so they're only listed here, never changed.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            var realtime = context.Lights.Where(l => l.isActiveAndEnabled && l.lightmapBakeType == LightmapBakeType.Realtime).ToList();
            if (realtime.Count == 0) return CheckResult.Pass("No realtime lights; every light contributes to the bake.");

            return CheckResult.Info($"{context.CountByScene(realtime)} realtime light(s) won't be baked: {ChecklistContext.Names(realtime)}.")
                .WithTargets(realtime.Select(l => (Object)l.gameObject));
        }
    }

    internal sealed class BakedLightsCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Objects;
        public override string Id => "objects.baked-lights";
        public override string Title => "Lights contribute to the bake";
        public override string Help =>
            "Mixed and Baked lights are baked into APV. Indirect Multiplier 0 on a light means it adds no bounce light to the bake.";

        public override CheckResult Evaluate(ChecklistContext context)
        {
            var baked = context.Lights.Where(l => l.isActiveAndEnabled && l.lightmapBakeType != LightmapBakeType.Realtime).ToList();
            if (baked.Count == 0)
            {
                return CheckResult.Warn("No Mixed or Baked lights, so only the sky lights the bake.");
            }

            var noBounce = baked.Where(l => l.bounceIntensity <= 0f).ToList();
            if (noBounce.Count > 0)
            {
                return CheckResult.Info($"{noBounce.Count} baked light(s) have Indirect Multiplier 0 and add no bounce light: {ChecklistContext.Names(noBounce)}.")
                    .WithTargets(noBounce.Select(l => (Object)l.gameObject));
            }

            int mixed = baked.Count(l => l.lightmapBakeType == LightmapBakeType.Mixed);
            return CheckResult.Pass($"{mixed} Mixed and {baked.Count - mixed} Baked light(s).");
        }
    }

    internal sealed class EmissiveMaterialsCheck : SetupCheck
    {
        public override CheckSection Section => CheckSection.Objects;
        public override string Id => "objects.emissive";
        public override string Title => "Emissive materials bake their emission";
        public override string Help =>
            "An emissive material on a Contribute GI renderer only lights the bake when its Global Illumination flag is Baked " +
            "(material inspector > Emission > Global Illumination).";

        private static readonly string[] ColorProperties = { "_EmissiveColor", "_EmissionColor" };

        public override CheckResult Evaluate(ChecklistContext context)
        {
            var materials = ProbeVolumeCoverageCheck.StaticGeometry(context)
                .OfType<Renderer>()
                .SelectMany(r => r.sharedMaterials)
                .Where(m => m != null)
                .Distinct()
                .Where(IsEmissive)
                .ToList();

            var notBaked = materials.Where(m => (m.globalIlluminationFlags & MaterialGlobalIlluminationFlags.BakedEmissive) == 0).ToList();
            if (notBaked.Count == 0)
            {
                return materials.Count == 0
                    ? CheckResult.Pass("No emissive materials on static GI renderers.")
                    : CheckResult.Pass($"{materials.Count} emissive material(s) bake their emission.");
            }

            var editable = notBaked.Where(m => AssetDatabase.GetAssetPath(m).StartsWith("Assets/")).ToList();
            var result = CheckResult.Warn($"{notBaked.Count} emissive material(s) don't contribute to the bake: {ChecklistContext.Names(notBaked)}.").WithTargets(notBaked);
            if (editable.Count == 0) return result;

            string skipped = editable.Count < notBaked.Count ? $" ({notBaked.Count - editable.Count} read-only skipped)" : "";
            return result.WithFix($"Set Global Illumination to Baked on {editable.Count} material(s){skipped}", editable.Count, () =>
            {
                foreach (Material material in editable)
                {
                    FixUtility.Modify(material, "Bake Emission", m =>
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive);
                }
            });
        }

        /// <summary>
        /// HDRP materials keep the real emission in _EmissiveColor; their legacy _EmissionColor is white for the
        /// lightmapper's sake and says nothing. Only fall back to _EmissionColor on non-HDRP shaders.
        /// </summary>
        private static bool IsEmissive(Material material)
        {
            foreach (string property in ColorProperties)
            {
                if (!material.HasProperty(property)) continue;
                return material.GetColor(property).maxColorComponent > 0.001f;
            }
            return false;
        }
    }
}
#endif
