using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LightingTools.Editor.APVChecklist
{
    internal enum CheckStatus
    {
        Pass,
        Info,
        Warning,
        Fail,
        Manual,
        NotApplicable
    }

    internal enum CheckSection
    {
        Project,
        Scene,
        Objects,
        Bake,
        Verify
    }

    internal sealed class CheckResult
    {
        public CheckStatus Status;
        public string Message;
        public readonly List<Object> Targets = new List<Object>();

        /// <summary>What the Fix button will do for this result, e.g. "Set 12 renderers to Light Probes". Null hides Fix.</summary>
        public string FixDescription;

        /// <summary>Objects the fix changes, shown in confirmations. Fixes touching more than <see cref="SetupCheck.ConfirmThreshold"/> ask first.</summary>
        public int FixCount;

        public static CheckResult Pass(string message) => new CheckResult { Status = CheckStatus.Pass, Message = message };
        public static CheckResult Info(string message) => new CheckResult { Status = CheckStatus.Info, Message = message };
        public static CheckResult Warn(string message) => new CheckResult { Status = CheckStatus.Warning, Message = message };
        public static CheckResult Fail(string message) => new CheckResult { Status = CheckStatus.Fail, Message = message };
        public static CheckResult NotApplicable(string message) => new CheckResult { Status = CheckStatus.NotApplicable, Message = message };

        public CheckResult WithTargets(IEnumerable<Object> targets)
        {
            Targets.AddRange(targets.Where(t => t != null));
            return this;
        }

        /// <summary>The change the Fix button applies; set together with the description so they can't drift apart.</summary>
        public Action FixAction;

        /// <summary>A fix that only opens a window or settings page. Never run by Fix All.</summary>
        public bool FixOpensUI;

        public CheckResult WithFix(string description, int count, Action action)
        {
            FixDescription = description;
            FixCount = count;
            FixAction = action;
            return this;
        }

        public CheckResult WithUIFix(string description, Action openUI, string buttonLabel = "Open")
        {
            FixDescription = description;
            FixCount = 0;
            FixAction = openUI;
            FixOpensUI = true;
            FixButtonLabel = buttonLabel;
            return this;
        }

        /// <summary>Label of the Fix button; "Fix" unless the fix opens a window or prompt.</summary>
        public string FixButtonLabel = "Fix";

        /// <summary>A fix that is a judgement call (e.g. adding Contribute GI to meshes). Offered per row, never by Fix All.</summary>
        public bool ManualFixOnly;

        public CheckResult NotInFixAll()
        {
            ManualFixOnly = true;
            return this;
        }

        public bool CanFix => FixAction != null;
        public bool IncludedInFixAll => CanFix && !FixOpensUI && !ManualFixOnly && IsProblem;

        public bool IsProblem => Status == CheckStatus.Warning || Status == CheckStatus.Fail;
    }

    /// <summary>One row of the checklist. Evaluate must not change anything; Fix applies the change with Undo.</summary>
    internal abstract class SetupCheck
    {
        public const int ConfirmThreshold = 20;

        public abstract CheckSection Section { get; }
        public abstract string Id { get; }
        public abstract string Title { get; }

        /// <summary>Longer explanation shown when the row's help is expanded.</summary>
        public virtual string Help => null;

        /// <summary>Reads state and returns a result. Must not change anything; attach the fix with WithFix.</summary>
        public abstract CheckResult Evaluate(ChecklistContext context);

        /// <summary>A manual check: shown with a checkbox the user ticks, stored per scene.</summary>
        public virtual bool IsManual => false;
    }

    /// <summary>A step only a person can confirm, e.g. looking at the debug view for leaks.</summary>
    internal sealed class ManualCheck : SetupCheck
    {
        private readonly CheckSection _section;
        private readonly string _id;
        private readonly string _title;
        private readonly string _help;

        public ManualCheck(CheckSection section, string id, string title, string help, string actionLabel = null, Action action = null)
        {
            _section = section;
            _id = id;
            _title = title;
            _help = help;
            ActionLabel = actionLabel;
            Action = action;
        }

        /// <summary>Optional helper button shown on the row, e.g. "Open Rendering Debugger".</summary>
        public string ActionLabel { get; }
        public Action Action { get; }

        public override CheckSection Section => _section;
        public override string Id => _id;
        public override string Title => _title;
        public override string Help => _help;
        public override bool IsManual => true;

        public override CheckResult Evaluate(ChecklistContext context)
        {
            return ManualCheckStore.Get(context.Scenes, Id)
                ? CheckResult.Pass("Confirmed for the loaded scene(s).")
                : new CheckResult { Status = CheckStatus.Manual, Message = "Tick once you've checked this yourself." };
        }
    }

    /// <summary>
    /// Everything the checks read, gathered once per refresh so 20 checks don't each scan the scenes.
    /// Only loaded scenes are scanned; every object list is filtered to those scenes.
    /// </summary>
    internal sealed class ChecklistContext
    {
        public readonly List<Scene> Scenes = new List<Scene>();
        public readonly List<MeshRenderer> MeshRenderers = new List<MeshRenderer>();
        public readonly List<Terrain> Terrains = new List<Terrain>();
        public readonly List<Light> Lights = new List<Light>();
        public readonly List<ReflectionProbe> ReflectionProbes = new List<ReflectionProbe>();
        public readonly List<UnityEngine.Rendering.ProbeVolume> ProbeVolumes = new List<UnityEngine.Rendering.ProbeVolume>();
        public readonly List<UnityEngine.Rendering.Volume> Volumes = new List<UnityEngine.Rendering.Volume>();
        public readonly List<LightbakeSettings> LightbakeSettings = new List<LightbakeSettings>();

        public static ChecklistContext Gather()
        {
            var context = new ChecklistContext();
            context.Scenes.AddRange(LoadedScenesUtility.LoadedScenes());

            foreach (Scene scene in context.Scenes)
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    context.MeshRenderers.AddRange(root.GetComponentsInChildren<MeshRenderer>(true));
                    context.Terrains.AddRange(root.GetComponentsInChildren<Terrain>(true));
                    context.Lights.AddRange(root.GetComponentsInChildren<Light>(true));
                    context.ReflectionProbes.AddRange(root.GetComponentsInChildren<ReflectionProbe>(true));
                    context.ProbeVolumes.AddRange(root.GetComponentsInChildren<UnityEngine.Rendering.ProbeVolume>(true));
                    context.Volumes.AddRange(root.GetComponentsInChildren<UnityEngine.Rendering.Volume>(true));
                    context.LightbakeSettings.AddRange(root.GetComponentsInChildren<LightbakeSettings>(true));
                }
            }
            return context;
        }

        public bool IsMultiScene => Scenes.Count > 1;

        /// <summary>"12" for one scene, "12 (Level: 8, Lighting: 4)" when several scenes are loaded.</summary>
        public string CountByScene<T>(ICollection<T> objects) where T : Component
        {
            if (!IsMultiScene || objects.Count == 0) return objects.Count.ToString();
            var parts = objects.GroupBy(o => o.gameObject.scene.name).Select(g => $"{g.Key}: {g.Count()}");
            return $"{objects.Count} ({string.Join(", ", parts)})";
        }

        public static string Names<T>(IEnumerable<T> objects, int max = 5) where T : Object
        {
            var list = objects.ToList();
            string names = string.Join(", ", list.Take(max).Select(o => o.name));
            return list.Count > max ? $"{names} and {list.Count - max} more" : names;
        }
    }

    /// <summary>Applies a fix as one undo step and records prefab overrides for everything touched.</summary>
    internal static class FixUtility
    {
        public static void RunUndoGroup(string name, Action action)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            try
            {
                action();
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }
        }

        public static void Modify<T>(T target, string undoName, Action<T> change) where T : Object
        {
            Undo.RecordObject(target, undoName);
            change(target);
            if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            EditorUtility.SetDirty(target);
        }
    }

    internal static class LoadedScenesUtility
    {
        public static IEnumerable<Scene> LoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded) yield return scene;
            }
        }
    }

    /// <summary>Manual check ticks, stored per scene GUID in EditorPrefs (this machine only).</summary>
    internal static class ManualCheckStore
    {
        private const string Prefix = "UnityTools.APVChecklist.";

        private static string Key(Scene scene, string checkId)
        {
            string guid = AssetDatabase.AssetPathToGUID(scene.path);
            return Prefix + (string.IsNullOrEmpty(guid) ? "unsaved" : guid) + "." + checkId;
        }

        /// <summary>Ticked only when ticked for every loaded scene.</summary>
        public static bool Get(IEnumerable<Scene> scenes, string checkId)
        {
            bool any = false;
            foreach (Scene scene in scenes)
            {
                any = true;
                if (!EditorPrefs.GetBool(Key(scene, checkId), false)) return false;
            }
            return any;
        }

        public static void Set(IEnumerable<Scene> scenes, string checkId, bool value)
        {
            foreach (Scene scene in scenes)
            {
                if (value) EditorPrefs.SetBool(Key(scene, checkId), true);
                else EditorPrefs.DeleteKey(Key(scene, checkId));
            }
        }
    }
}
