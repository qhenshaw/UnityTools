using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

#if HDRP
using UnityEngine.Rendering;
#endif

namespace LightingTools.Editor.APVChecklist
{
    /// <summary>
    /// Walkthrough for an HDRP light bake that uses Adaptive Probe Volumes and no lightmaps. Every row checks one
    /// setting in the project or the loaded scenes and offers a Fix and a Select button. Re-checks itself when the
    /// scenes, hierarchy, undo stack or project change.
    /// </summary>
    public class APVBakeChecklistWindow : EditorWindow
    {
        private const string StyleSheetPath = "Packages/com.qhenshaw.unitytools/LightingTools/Editor/APVChecklist/APVBakeChecklist.uss";
        private const double RefreshInterval = 0.5;

        [MenuItem("Window/Rendering/APV Bake Checklist")]
        public static void Open()
        {
            var window = GetWindow<APVBakeChecklistWindow>();
            window.titleContent = new GUIContent("APV Bake Checklist", EditorGUIUtility.IconContent("LightingSettings Icon").image);
            window.minSize = new Vector2(420, 300);
        }

#if HDRP
        private List<SetupCheck> _checks;
        private readonly Dictionary<SetupCheck, CheckResult> _results = new Dictionary<SetupCheck, CheckResult>();
        private ChecklistContext _context;

        private bool _dirty = true;
        private double _lastRefresh;

        private VisualElement _summary;
        private Button _fixAllButton;
        private ScrollView _scroll;
        private readonly Dictionary<CheckSection, Foldout> _sections = new Dictionary<CheckSection, Foldout>();
        private readonly Dictionary<CheckSection, VisualElement> _sectionBodies = new Dictionary<CheckSection, VisualElement>();
        private Label _bakeStatus;
        private VisualElement _bakeButtons;
        private Button _cancelButton;

        private void OnEnable()
        {
            _checks = ChecklistRegistry.CreateAll();

            EditorApplication.hierarchyChanged += MarkDirty;
            EditorApplication.projectChanged += MarkDirty;
            Undo.undoRedoPerformed += MarkDirty;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            Lightmapping.bakeCompleted += MarkDirty;
            Lightmapping.lightingDataCleared += MarkDirty;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= MarkDirty;
            EditorApplication.projectChanged -= MarkDirty;
            Undo.undoRedoPerformed -= MarkDirty;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            Lightmapping.bakeCompleted -= MarkDirty;
            Lightmapping.lightingDataCleared -= MarkDirty;
        }

        private void OnFocus() => MarkDirty();
        private void MarkDirty() => _dirty = true;
        private void OnSceneOpened(UnityEngine.SceneManagement.Scene s, OpenSceneMode m) => MarkDirty();
        private void OnSceneClosed(UnityEngine.SceneManagement.Scene s) => MarkDirty();
        private void OnSceneSaved(UnityEngine.SceneManagement.Scene s) => MarkDirty();
        private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene a, UnityEngine.SceneManagement.Scene b) => MarkDirty();

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.AddToClassList("apv-root");
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (sheet != null) root.styleSheets.Add(sheet);

            var toolbar = new VisualElement();
            toolbar.AddToClassList("apv-toolbar");
            _summary = new VisualElement();
            _summary.AddToClassList("apv-summary");
            toolbar.Add(_summary);
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            toolbar.Add(spacer);
            toolbar.Add(new Button(() => { MarkDirty(); RefreshNow(); }) { text = "Refresh", tooltip = "Run every check again" });
            _fixAllButton = new Button(FixAll) { text = "Fix All" };
            _fixAllButton.AddToClassList("apv-fix-all");
            toolbar.Add(_fixAllButton);
            root.Add(toolbar);

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.AddToClassList("apv-scroll");
            root.Add(_scroll);

            foreach (CheckSection section in Enum.GetValues(typeof(CheckSection)))
            {
                var foldout = new Foldout { text = SectionTitle(section), value = true, viewDataKey = "apv-section-" + section };
                foldout.AddToClassList("apv-section");
                if (section == CheckSection.Bake) foldout.Add(CreateBakeControls());
                var body = new VisualElement();
                foldout.Add(body);
                _sections[section] = foldout;
                _sectionBodies[section] = body;
                _scroll.Add(foldout);
            }

            root.schedule.Execute(Tick).Every(250);
            RefreshNow();
        }

        private static string SectionTitle(CheckSection section)
        {
            switch (section)
            {
                case CheckSection.Project: return "1. Project";
                case CheckSection.Scene: return "2. Scene";
                case CheckSection.Objects: return "3. Objects";
                case CheckSection.Bake: return "4. Bake";
                default: return "5. Verify";
            }
        }

        private void Tick()
        {
            UpdateBakeStatus();
            bool baking = IsBaking;
            if (_dirty && !baking && EditorApplication.timeSinceStartup - _lastRefresh > RefreshInterval) RefreshNow();
        }

        // ---------- checks ----------

        private void RefreshNow()
        {
            if (_scroll == null) return;
            _dirty = false;
            _lastRefresh = EditorApplication.timeSinceStartup;

            _context = ChecklistContext.Gather();
            _results.Clear();
            foreach (SetupCheck check in _checks)
            {
                CheckResult result;
                try
                {
                    result = check.Evaluate(_context);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    result = CheckResult.Warn($"This check failed to run: {e.GetBaseException().Message}. See the Console.");
                }
                _results[check] = result;
            }

            RebuildRows();
        }

        private void RebuildRows()
        {
            foreach (var body in _sectionBodies.Values) body.Clear();

            foreach (SetupCheck check in _checks)
            {
                _sectionBodies[check.Section].Add(CreateRow(check, _results[check]));
            }

            foreach (var pair in _sections)
            {
                var sectionResults = _checks.Where(c => c.Section == pair.Key).Select(c => _results[c]).ToList();
                int fails = sectionResults.Count(r => r.Status == CheckStatus.Fail);
                int warns = sectionResults.Count(r => r.Status == CheckStatus.Warning);
                int manual = sectionResults.Count(r => r.Status == CheckStatus.Manual);
                string state = fails > 0 ? $"{fails} to fix" : warns > 0 ? $"{warns} warning(s)" : manual > 0 ? $"{manual} to confirm" : "done";
                pair.Value.text = $"{SectionTitle(pair.Key)}  ·  {state}";
                pair.Value.EnableInClassList("apv-section-done", fails == 0 && warns == 0 && manual == 0);
            }

            _summary.Clear();
            AddSummaryChip(CheckStatus.Fail);
            AddSummaryChip(CheckStatus.Warning);
            AddSummaryChip(CheckStatus.Manual);
            AddSummaryChip(CheckStatus.Pass);

            int fixable = _results.Values.Count(r => r.IncludedInFixAll);
            _fixAllButton.text = fixable > 0 ? $"Fix All ({fixable})" : "Fix All";
            _fixAllButton.SetEnabled(fixable > 0 && !IsBaking);
            _fixAllButton.tooltip = fixable > 0 ? "Apply every safe fix below, as one undo step" : "Nothing to fix automatically";
        }

        private void AddSummaryChip(CheckStatus status)
        {
            int count = _results.Values.Count(r => r.Status == status);
            if (count == 0) return;
            var chip = new Label($"{Glyph(status)} {count}") { tooltip = status.ToString() };
            chip.AddToClassList("apv-chip");
            chip.AddToClassList(StatusClass(status));
            _summary.Add(chip);
        }

        private VisualElement CreateRow(SetupCheck check, CheckResult result)
        {
            var row = new VisualElement();
            row.AddToClassList("apv-row");
            row.AddToClassList(StatusClass(result.Status));

            var badge = new Label(Glyph(result.Status)) { tooltip = result.Status.ToString() };
            badge.AddToClassList("apv-badge");
            badge.AddToClassList(StatusClass(result.Status));
            row.Add(badge);

            var text = new VisualElement();
            text.AddToClassList("apv-text");
            var title = new Label(check.Title);
            title.AddToClassList("apv-title");
            text.Add(title);
            if (!string.IsNullOrEmpty(result.Message))
            {
                var message = new Label(result.Message);
                message.AddToClassList("apv-message");
                text.Add(message);
            }

            Label help = null;
            if (!string.IsNullOrEmpty(check.Help))
            {
                help = new Label(check.Help);
                help.AddToClassList("apv-help");
                help.style.display = DisplayStyle.None;
                text.Add(help);
            }
            row.Add(text);

            var buttons = new VisualElement();
            buttons.AddToClassList("apv-buttons");

            if (check is ManualCheck manual)
            {
                if (manual.Action != null) buttons.Add(new Button(manual.Action) { text = manual.ActionLabel });
                var toggle = new Toggle { value = result.Status == CheckStatus.Pass, tooltip = "Done" };
                toggle.RegisterValueChangedCallback(e =>
                {
                    ManualCheckStore.Set(_context.Scenes, check.Id, e.newValue);
                    RefreshNow();
                });
                buttons.Add(toggle);
            }

            if (result.CanFix)
            {
                var fix = new Button(() => RunFix(check, result))
                {
                    text = result.FixButtonLabel,
                    tooltip = result.FixDescription + (result.ManualFixOnly ? "\n(not included in Fix All)" : ""),
                };
                fix.AddToClassList("apv-fix");
                fix.SetEnabled(!IsBaking);
                buttons.Add(fix);
            }

            if (result.Targets.Count > 0)
            {
                buttons.Add(new Button(() => SelectTargets(result.Targets))
                {
                    text = "Select",
                    tooltip = $"Select {result.Targets.Count} object(s)",
                });
            }

            if (help != null)
            {
                var helpButton = new Button { text = "?", tooltip = "Show what this checks and why" };
                helpButton.AddToClassList("apv-help-button");
                helpButton.clicked += () =>
                    help.style.display = help.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
                buttons.Add(helpButton);
            }

            row.Add(buttons);
            return row;
        }

        private static void SelectTargets(List<Object> targets)
        {
            Object[] alive = targets.Where(t => t != null).ToArray();
            Selection.objects = alive;
            if (alive.Length > 0) EditorGUIUtility.PingObject(alive[0]);
            if (alive.Length > 0 && alive[0] is GameObject && SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
        }

        private void RunFix(SetupCheck check, CheckResult result)
        {
            if (!result.FixOpensUI && result.FixCount > SetupCheck.ConfirmThreshold &&
                !EditorUtility.DisplayDialog("APV Bake Checklist", $"{result.FixDescription}?\n\nThis changes {result.FixCount} objects. You can undo it.", "Apply", "Cancel"))
            {
                return;
            }

            try
            {
                if (result.FixOpensUI) result.FixAction();
                else FixUtility.RunUndoGroup($"APV Checklist: {check.Title}", result.FixAction);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("APV Bake Checklist", $"The fix for '{check.Title}' failed: {e.GetBaseException().Message}", "OK");
            }
            MarkDirty();
            RefreshNow();
        }

        private void FixAll()
        {
            var fixes = _checks.Where(c => _results[c].IncludedInFixAll).Select(c => (check: c, result: _results[c])).ToList();
            if (fixes.Count == 0) return;

            string list = string.Join("\n", fixes.Select(f => $"• {f.result.FixDescription}"));
            if (!EditorUtility.DisplayDialog("APV Bake Checklist: Fix All", $"Apply {fixes.Count} fix(es)?\n\n{list}\n\nThis is one undo step.", "Apply", "Cancel"))
            {
                return;
            }

            var failed = new List<string>();
            FixUtility.RunUndoGroup("APV Checklist: Fix All", () =>
            {
                foreach (var (check, result) in fixes)
                {
                    try
                    {
                        result.FixAction();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        failed.Add($"{check.Title}: {e.GetBaseException().Message}");
                    }
                }
            });

            if (failed.Count > 0) EditorUtility.DisplayDialog("APV Bake Checklist", "Some fixes failed:\n\n" + string.Join("\n", failed), "OK");
            MarkDirty();
            RefreshNow();

            // Fixes can unlock others (e.g. a new Baking Set makes the scene checks meaningful); show what's left.
            int remaining = _results.Values.Count(r => r.IncludedInFixAll);
            if (remaining > 0) Debug.Log($"APV Bake Checklist: {remaining} more fix(es) became available after Fix All. Press Fix All again to apply them.");
        }

        // ---------- bake controls ----------

        private static bool IsBaking => Lightmapping.isRunning || AdaptiveProbeVolumes.isRunning;

        private VisualElement CreateBakeControls()
        {
            var container = new VisualElement();
            container.AddToClassList("apv-bake");

            _bakeButtons = new VisualElement();
            _bakeButtons.AddToClassList("apv-bake-buttons");
            var bakeAll = new Button(() => StartBake(false))
            {
                text = "Bake (probes + reflections)",
                tooltip = "Generate Lighting: bakes APV, then the baked reflection probes. Same as the Lighting window's Generate Lighting.",
            };
            bakeAll.AddToClassList("apv-primary");
            _bakeButtons.Add(bakeAll);
            _bakeButtons.Add(new Button(() => StartBake(true)) { text = "Bake probes only", tooltip = "Bakes Adaptive Probe Volumes only (faster while tuning probe placement)." });
            _bakeButtons.Add(new Button(ClearBakedData) { text = "Clear baked data", tooltip = "Clears the loaded scenes' baked lighting data (Lighting window > Clear Baked Data)." });
            _bakeButtons.Add(new Button(AddProbeAdjustmentVolume) { text = "Add Adjustment Volume", tooltip = "Adds a Probe Adjustment Volume at the Scene view pivot, for fixing leaks or invalid probes in one spot." });
            container.Add(_bakeButtons);

            var statusRow = new VisualElement();
            statusRow.AddToClassList("apv-bake-status-row");
            _bakeStatus = new Label();
            _bakeStatus.AddToClassList("apv-bake-status");
            statusRow.Add(_bakeStatus);
            _cancelButton = new Button(CancelBake) { text = "Cancel" };
            statusRow.Add(_cancelButton);
            container.Add(statusRow);

            UpdateBakeStatus();
            return container;
        }

        private void UpdateBakeStatus()
        {
            if (_bakeStatus == null) return;
            bool baking = IsBaking;
            _bakeButtons.SetEnabled(!baking);
            _cancelButton.style.display = baking ? DisplayStyle.Flex : DisplayStyle.None;
            if (_fixAllButton != null && baking) _fixAllButton.SetEnabled(false);

            if (Lightmapping.isRunning) _bakeStatus.text = $"Baking… {Lightmapping.buildProgress * 100f:0}%";
            else if (AdaptiveProbeVolumes.isRunning) _bakeStatus.text = "Baking probes…";
            else
            {
                int blocking = _results.Count(p => p.Key.Section != CheckSection.Verify && p.Value.Status == CheckStatus.Fail);
                _bakeStatus.text = blocking > 0 ? $"{blocking} check(s) above still fail; the bake may be incomplete." : "Ready to bake.";
            }
        }

        private void StartBake(bool probesOnly)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            bool started = probesOnly ? AdaptiveProbeVolumes.BakeAsync() : Lightmapping.BakeAsync();
            if (!started) Debug.LogWarning("APV Bake Checklist: the bake didn't start. See the Console and the Lighting window for the reason.");
            UpdateBakeStatus();
        }

        private void CancelBake()
        {
            if (AdaptiveProbeVolumes.isRunning) AdaptiveProbeVolumes.Cancel();
            if (Lightmapping.isRunning) Lightmapping.Cancel();
            MarkDirty();
        }

        private void ClearBakedData()
        {
            if (!EditorUtility.DisplayDialog("Clear baked data",
                    "Clear the baked lighting data of the loaded scenes? You'll need to bake again. This can't be undone.",
                    "Clear", "Cancel"))
            {
                return;
            }
            Lightmapping.Clear();
            MarkDirty();
        }

        private void AddProbeAdjustmentVolume()
        {
            SceneView view = SceneView.lastActiveSceneView;
            Vector3 position = view != null ? view.pivot : Vector3.zero;
            GameObject go = ReflectionProbeHelper.CreateAtSelection("Probe Adjustment Volume", position);
            var volume = Undo.AddComponent<ProbeAdjustmentVolume>(go);
            volume.size = Vector3.one * 2f;
            Selection.activeGameObject = go;
        }

        private static string StatusClass(CheckStatus status) => "apv-status-" + status.ToString().ToLowerInvariant();

        private static string Glyph(CheckStatus status)
        {
            switch (status)
            {
                case CheckStatus.Pass: return "✓";
                case CheckStatus.Info: return "i";
                case CheckStatus.Warning: return "!";
                case CheckStatus.Fail: return "✕";
                case CheckStatus.Manual: return "?";
                default: return "–";
            }
        }
#else
        public void CreateGUI()
        {
            rootVisualElement.Add(new HelpBox(
                "The APV Bake Checklist needs the High Definition Render Pipeline package (com.unity.render-pipelines.high-definition).",
                HelpBoxMessageType.Info));
        }
#endif
    }
}
