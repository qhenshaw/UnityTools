using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace UnityTools.Placement.Editor
{
    /// <summary>
    /// The Placement Graph inspector: a New button with templates while no graph is assigned; then Generate, Clear and
    /// a status line, and foldout sections (Placement, Scene Inputs, Parameters, Runtime, Debug) that show only the
    /// fields that apply. Warnings stay visible below the sections.
    /// </summary>
    [CustomEditor(typeof(PlacementGraph))]
    internal class PlacementGraphEditor : UnityEditor.Editor
    {
        private const string PrefsPrefix = "UnityTools.PlacementGraph.";
        internal const string LastFolderKey = PrefsPrefix + "LastFolder";
        internal const string LastTemplateKey = PrefsPrefix + "LastTemplate";
        private const string LastCellSizeKey = PrefsPrefix + "LastCellSize";

        private readonly BoxBoundsHandle _boundsHandle = new BoxBoundsHandle();

        private static GUIStyle _summaryStyle;
        private static GUIStyle SummaryStyle => _summaryStyle ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        private static GUIStyle _noteStyle;
        private static GUIStyle NoteStyle => _noteStyle ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };

        public override void OnInspectorGUI()
        {
            var graph = (PlacementGraph)target;
            serializedObject.Update();

            if (graph.Graph == null)
            {
                DrawEmptyState(graph);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            DrawGraphRow(graph);
            if (graph.Graph == null)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }

            if (NeedsSync(graph))
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(graph, "Sync Placement Graph Parameters");
                graph.SyncParameters();
                EditorUtility.SetDirty(graph);
                serializedObject.Update();
            }

            DrawHeader(graph);
            DrawPlacement(graph);
            DrawSceneInputs(graph);
            DrawParameters(graph);
            DrawRuntime(graph);
            DrawDebug(graph);
            serializedObject.ApplyModifiedProperties();

            foreach (string warning in graph.LastWarnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
        }

        // ---- Graph slot and New ----

        private void DrawEmptyState(PlacementGraph graph)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_graph"));
                DrawNewButtons(graph, "New", GUILayout.Width(44));
            }
            EditorGUILayout.HelpBox("No graph yet. Press New to create a .placegraph, assign it here and open it for editing.", MessageType.Info);
        }

        private void DrawGraphRow(PlacementGraph graph)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_graph"));
                if (GUILayout.Button(new GUIContent("Open", "Open the graph in the graph window."), EditorStyles.miniButton, GUILayout.Width(44)))
                    OpenGraph(AssetDatabase.GetAssetPath(graph.Graph));
                if (GUILayout.Button(new GUIContent("＋", "New graph: a template, or a copy of this one."), EditorStyles.miniButton, GUILayout.Width(22)))
                    ShowNewMenu(graph);
            }
        }

        /// <summary>"New" (the last template) and "▾" (the template menu).</summary>
        private void DrawNewButtons(PlacementGraph graph, string label, GUILayoutOption width)
        {
            PlacementGraphTemplate last = LastTemplate;
            if (GUILayout.Button(new GUIContent(label, $"New graph: {PlacementGraphTemplates.DisplayName(last)}."), EditorStyles.miniButtonLeft, width))
                Defer(() => NewFromTemplate(graph, last));
            if (GUILayout.Button(new GUIContent("▾", "Choose a template."), EditorStyles.miniButtonRight, GUILayout.Width(18)))
                ShowNewMenu(graph);
        }

        private void ShowNewMenu(PlacementGraph graph)
        {
            var menu = new GenericMenu();
            PlacementGraphTemplate last = LastTemplate;
            foreach (PlacementGraphTemplate template in System.Enum.GetValues(typeof(PlacementGraphTemplate)))
            {
                if (!PlacementGraphTemplates.IsAvailable(template)) continue;
                PlacementGraphTemplate chosen = template;
                menu.AddItem(new GUIContent(PlacementGraphTemplates.DisplayName(template)), template == last, () => Defer(() => NewFromTemplate(graph, chosen)));
            }
            if (graph.Graph != null)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Duplicate current graph…"), false, () => Defer(() => DuplicateCurrent(graph)));
            }
            menu.ShowAsContext();
        }

        // The save panel is modal: running it from a later editor tick keeps it out of the inspector's layout pass.
        private static void Defer(System.Action action) => EditorApplication.delayCall += () => action();

        internal static PlacementGraphTemplate LastTemplate
        {
            get
            {
                var template = (PlacementGraphTemplate)EditorPrefs.GetInt(LastTemplateKey, (int)PlacementGraphTemplate.ScatterOnSurface);
                return PlacementGraphTemplates.IsAvailable(template) ? template : PlacementGraphTemplate.Empty;
            }
            set => EditorPrefs.SetInt(LastTemplateKey, (int)value);
        }

        private static string AskForPath(PlacementGraph graph, string title)
        {
            string folder = EditorPrefs.GetString(LastFolderKey, "Assets");
            if (!AssetDatabase.IsValidFolder(folder)) folder = "Assets";
            string suggested = Path.GetFileNameWithoutExtension(PlacementGraphTemplates.SuggestPath(folder, graph.name));
            string path = EditorUtility.SaveFilePanelInProject(title, suggested, PlacementGraphDefinition.AssetExtension,
                "Where to save the new Placement Graph.", folder);
            if (!string.IsNullOrEmpty(path)) EditorPrefs.SetString(LastFolderKey, Path.GetDirectoryName(path)?.Replace('\\', '/'));
            return path;
        }

        private static void NewFromTemplate(PlacementGraph graph, PlacementGraphTemplate template)
        {
            if (graph == null) return;
            string path = AskForPath(graph, "New Placement Graph");
            if (string.IsNullOrEmpty(path)) return;
            LastTemplate = template;
            CreateAndAssign(graph, template, path, open: true);
        }

        private static void DuplicateCurrent(PlacementGraph graph)
        {
            if (graph == null || graph.Graph == null) return;
            string source = AssetDatabase.GetAssetPath(graph.Graph);
            string path = AskForPath(graph, "Duplicate Placement Graph");
            if (string.IsNullOrEmpty(path) || path == source) return;
            Assign(graph, PlacementGraphTemplates.Duplicate(source, path));
            OpenGraph(path);
        }

        /// <summary>
        /// Creates a graph from <paramref name="template"/> at <paramref name="path"/>, assigns it to
        /// <paramref name="graph"/> with Undo, optionally opens it, and generates once if Auto Generate is on.
        /// </summary>
        internal static PlacementGraphAsset CreateAndAssign(PlacementGraph graph, PlacementGraphTemplate template, string path, bool open)
        {
            PlacementGraphAsset asset = PlacementGraphTemplates.Create(template, path);
            Assign(graph, asset);
            if (open) OpenGraph(path);
            bool autoGenerate = new SerializedObject(graph).FindProperty("_autoGenerate").boolValue;
            if (template != PlacementGraphTemplate.Empty && autoGenerate && asset != null) graph.Generate();
            return asset;
        }

        private static void Assign(PlacementGraph graph, PlacementGraphAsset asset)
        {
            var serialized = new SerializedObject(graph);
            serialized.FindProperty("_graph").objectReferenceValue = asset;
            serialized.ApplyModifiedProperties();
            graph.SyncParameters();
        }

        /// <summary>Opens a .placegraph the way double-clicking it does.</summary>
        private static void OpenGraph(string path)
        {
            Object main = AssetDatabase.LoadMainAssetAtPath(path);
            if (main != null) AssetDatabase.OpenAsset(main);
        }

        // ---- Header ----

        private void DrawHeader(PlacementGraph graph)
        {
            EditorGUILayout.HelpBox("The children of a Placement Graph are generated. Anything placed under it is " +
                                    "destroyed when it regenerates, except the Markers and Spline objects.", MessageType.Info);
            if (graph.IsInPrefabInstance)
            {
                EditorGUILayout.HelpBox("This Placement Graph is inside a prefab instance, so it can't regenerate here. " +
                                        "Edit it in prefab mode, or unpack the prefab instance.", MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate", GUILayout.Height(24)))
                {
                    serializedObject.ApplyModifiedProperties();
                    graph.Generate();
                    serializedObject.Update();
                }
                if (GUILayout.Button("Clear", GUILayout.Height(24), GUILayout.Width(60)))
                {
                    serializedObject.ApplyModifiedProperties();
                    graph.Clear();
                    serializedObject.Update();
                }
                SerializedProperty auto = serializedObject.FindProperty("_autoGenerate");
                using (new PropertyScope(auto))
                {
                    auto.boolValue = GUILayout.Toggle(auto.boolValue, new GUIContent("Auto", auto.tooltip), EditorStyles.miniButton, GUILayout.Height(24), GUILayout.Width(44));
                }
            }

            PlacementInstances instances = graph.Instances;
            int instanced = instances != null ? instances.InstanceCount : 0;
            int drawCalls = instances != null ? instances.DrawCallCount : 0;
            EditorGUILayout.LabelField(PlacementGraphInspectorRules.Status(graph.InstanceCount, graph.CellIndices.Count(),
                graph.LastNodeMilliseconds.Sum(), instanced, drawCalls), EditorStyles.miniLabel);
        }

        // ---- Sections ----

        /// <summary>A foldout header whose open state is remembered per user; shows <paramref name="summary"/> while closed.</summary>
        private static bool Section(string name, string summary, bool openByDefault = true)
        {
            string key = PrefsPrefix + "Section." + name;
            bool open = EditorPrefs.GetBool(key, openByDefault);
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
            bool toggled = EditorGUI.BeginFoldoutHeaderGroup(rect, open, name);
            if (!toggled && !string.IsNullOrEmpty(summary))
            {
                Rect right = new Rect(rect.x + 110f, rect.y, rect.width - 114f, rect.height);
                GUI.Label(right, summary, SummaryStyle);
            }
            EditorGUI.EndFoldoutHeaderGroup();
            if (toggled != open) EditorPrefs.SetBool(key, toggled);
            return toggled;
        }

        private void DrawPlacement(PlacementGraph graph)
        {
            SerializedProperty seed = serializedObject.FindProperty("_seed");
            SerializedProperty size = serializedObject.FindProperty("_size");
            SerializedProperty cellSize = serializedObject.FindProperty("_cellSize");
            if (!Section("Placement", PlacementGraphInspectorRules.PlacementSummary(seed.intValue, size.vector3Value, cellSize.floatValue))) return;

            using (new EditorGUI.IndentLevelScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(seed);
                    if (GUILayout.Button(new GUIContent("Random", "Pick a new random seed."), EditorStyles.miniButton, GUILayout.Width(56)))
                        seed.intValue = Random.Range(0, 100000);
                }
                EditorGUILayout.PropertyField(size);

                bool partitioned = PlacementGraphInspectorRules.ShowCellFields(cellSize.floatValue);
                using (new PropertyScope(cellSize))
                {
                    bool partition = EditorGUILayout.Toggle(new GUIContent("Partition Into Cells",
                        "Split the volume into square cells that regenerate separately, so edits only redo the cells they touch."), partitioned);
                    if (partition != partitioned)
                    {
                        if (partition) cellSize.floatValue = PlacementGraphInspectorRules.RestoredCellSize(EditorPrefs.GetFloat(LastCellSizeKey, 0f));
                        else
                        {
                            EditorPrefs.SetFloat(LastCellSizeKey, cellSize.floatValue);
                            cellSize.floatValue = 0f;
                        }
                    }
                }
                if (PlacementGraphInspectorRules.ShowCellFields(cellSize.floatValue))
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.PropertyField(cellSize);
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("_cellOverlap"));
                    }
                }
            }
        }

        private void DrawSceneInputs(PlacementGraph graph)
        {
            SerializedProperty markers = serializedObject.FindProperty("_markers");
            bool usesMarkers = PlacementGraphInspectorRules.UsesMarkers(graph.Graph);
            bool showMarkers = PlacementGraphInspectorRules.ShowSceneInput(usesMarkers, markers.objectReferenceValue != null);
#if UNITYTOOLS_SPLINES
            SerializedProperty spline = serializedObject.FindProperty("_spline");
            bool usesSpline = PlacementGraphInspectorRules.UsesSpline(graph.Graph);
            bool showSpline = PlacementGraphInspectorRules.ShowSceneInput(usesSpline, spline.objectReferenceValue != null);
#else
            SerializedProperty spline = null;
            bool usesSpline = false, showSpline = false;
#endif
            if (!showMarkers && !showSpline) return;

            var summary = new List<string>();
            if (showSpline) summary.Add(spline.objectReferenceValue != null ? $"Spline: {spline.objectReferenceValue.name}" : "Spline: none");
            if (showMarkers) summary.Add(markers.objectReferenceValue != null ? $"Markers: {markers.objectReferenceValue.name}" : "Markers: none");
            if (!Section("Scene Inputs", string.Join(" · ", summary))) return;

            using (new EditorGUI.IndentLevelScope())
            {
                if (showSpline)
                {
                    EditorGUILayout.PropertyField(spline);
                    if (!usesSpline) EditorGUILayout.LabelField("No node reads the Spline; it's shown because one is assigned.", NoteStyle);
                }
                if (showMarkers)
                {
                    EditorGUILayout.PropertyField(markers);
                    if (!usesMarkers) EditorGUILayout.LabelField("No Child Markers node reads the Markers; they're shown because they're assigned.", NoteStyle);
                }
                var hidden = new List<string>();
#if UNITYTOOLS_SPLINES
                if (!showSpline) hidden.Add("Spline (no Spline Sampler or Spline Knots source)");
#endif
                if (!showMarkers) hidden.Add("Markers (no Child Markers node)");
                if (hidden.Count > 0) EditorGUILayout.LabelField("Hidden: " + string.Join(", ", hidden) + ".", NoteStyle);
            }
        }

        /// <summary>Each parameter on one line: an override toggle, the value (the graph's default while off) and a reset.</summary>
        private void DrawParameters(PlacementGraph graph)
        {
            if (graph.Graph.Parameters.Count == 0) return;
            SerializedProperty list = serializedObject.FindProperty("_parameters");
            int overridden = 0;
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("Override").boolValue) overridden++;
            }
            string summary = $"{graph.Graph.Parameters.Count}" + (overridden > 0 ? $" · {overridden} overridden" : "");
            if (!Section($"Parameters ({graph.Graph.Parameters.Count})", summary)) return;

            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < list.arraySize && i < graph.Graph.Parameters.Count; i++)
                {
                    SerializedProperty entry = list.GetArrayElementAtIndex(i);
                    SerializedProperty overrideOn = entry.FindPropertyRelative("Override");
                    SerializedProperty value = entry.FindPropertyRelative("Value");
                    PlacementParameter declared = graph.Graph.Parameters[i];
                    string field = ValueField(declared.Type);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        overrideOn.boolValue = EditorGUILayout.Toggle(overrideOn.boolValue, GUILayout.Width(28));
                        if (overrideOn.boolValue)
                        {
                            EditorGUILayout.PropertyField(value.FindPropertyRelative(field), new GUIContent(declared.Name + " •", "Overridden on this object."));
                            if (GUILayout.Button(new GUIContent("Reset", "Use the graph's default again."), EditorStyles.miniButton, GUILayout.Width(44)))
                            {
                                overrideOn.boolValue = false;
                                CopyDefault(value, declared);
                            }
                        }
                        else
                        {
                            // Shows the default from the graph; turning the toggle on starts from it.
                            using (new EditorGUI.DisabledScope(true)) DrawValue(declared, declared.Name);
                            CopyDefault(value, declared);
                            GUILayout.Space(48);
                        }
                    }
                }
            }
        }

        private void DrawRuntime(PlacementGraph graph)
        {
            SerializedProperty mode = serializedObject.FindProperty("_generateOn");
            var generateOn = (PlacementGraph.GenerateMode)mode.enumValueIndex;
            if (!Section("Runtime", ObjectNames.NicifyVariableName(generateOn.ToString()))) return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(mode);
                generateOn = (PlacementGraph.GenerateMode)mode.enumValueIndex;
                if (PlacementGraphInspectorRules.ShowStreamingFields(generateOn))
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_loadRadius"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_unloadRadius"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_frameBudgetMs"), new GUIContent("Frame Budget (ms)"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_use3DDistance"), new GUIContent("Use 3D Distance"));
                }
                if (PlacementGraphInspectorRules.ShowRuntimeFields(generateOn))
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_trackSceneChanges"));
                    SerializedProperty pooling = serializedObject.FindProperty("_pooling");
                    EditorGUILayout.PropertyField(pooling);
                    if (PlacementGraphInspectorRules.ShowMaxPoolSize(generateOn, pooling.boolValue))
                    {
                        using (new EditorGUI.IndentLevelScope()) EditorGUILayout.PropertyField(serializedObject.FindProperty("_maxPoolSize"));
                    }
                }
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_saveGenerated"));

                if (PlacementGraphInspectorRules.ShowStreamingFields(generateOn))
                {
                    int sources = Object.FindObjectsByType<GenerationSource>(FindObjectsInactive.Exclude).Length;
                    EditorGUILayout.LabelField(sources > 0
                        ? $"{sources} Generation Source{(sources == 1 ? "" : "s")} in the open scenes."
                        : "No Generation Source in the open scenes: the main camera is used.", NoteStyle);
                    if (serializedObject.FindProperty("_cellSize").floatValue <= 0f)
                        EditorGUILayout.LabelField($"Partition Into Cells is off, so Near Player streams {PlacementGraph.DefaultRuntimeCellSize:0} m cells.", NoteStyle);
                }
            }
        }

        private void DrawDebug(PlacementGraph graph)
        {
            IReadOnlyList<PlacementNode> nodes = graph.Graph.Nodes;
            int warnings = graph.LastWarnings.Count;
            string summary = $"{nodes.Count} node{(nodes.Count == 1 ? "" : "s")} · {warnings} warning{(warnings == 1 ? "" : "s")}";
            if (!Section("Debug", summary, openByDefault: false)) return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("Points out and time of each node, last run", EditorStyles.miniLabel);
                for (int i = 0; i < nodes.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string count = i < graph.LastNodeCounts.Count ? graph.LastNodeCounts[i].ToString() : "-";
                        if (i < graph.LastNodeMilliseconds.Count) count += $"   {graph.LastNodeMilliseconds[i]:0.0} ms";
                        EditorGUILayout.LabelField(nodes[i]?.Name ?? $"{i}: (missing)", count);
                        bool debugging = graph.DebugNode == i;
                        bool toggled = GUILayout.Toggle(debugging, "Show", EditorStyles.miniButton, GUILayout.Width(48));
                        if (toggled != debugging)
                        {
                            serializedObject.ApplyModifiedProperties();
                            Undo.RecordObject(graph, "Show Placement Graph Node");
                            graph.DebugNode = toggled ? i : -1;
                            if (graph.LastOutputs.Count == 0) graph.Evaluate();
                            serializedObject.Update();
                            SceneView.RepaintAll();
                        }
                    }
                    if (graph.DebugNode == i) DrawAttributes(graph, i);
                }
            }
        }

        /// <summary>Under the shown node: the attributes on its first output, with the values of the first few points.</summary>
        private static void DrawAttributes(PlacementGraph graph, int node)
        {
            if (node >= graph.LastOutputs.Count || graph.LastOutputs[node] == null || graph.LastOutputs[node].Length == 0) return;
            using (new EditorGUI.IndentLevelScope())
            {
                foreach (string line in DescribeAttributes(graph.LastOutputs[node][0], 3))
                    EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            }
        }

        /// <summary>One line per attribute: its name, type and the values of the first <paramref name="points"/> points.</summary>
        internal static List<string> DescribeAttributes(PointData data, int points)
        {
            var lines = new List<string>();
            if (data == null) return lines;
            if (data.Attributes.Count == 0)
            {
                lines.Add("No attributes");
                return lines;
            }
            int shown = Mathf.Min(points, data.Count);
            foreach (PointAttribute attribute in data.Attributes)
            {
                var values = new List<string>(shown);
                for (int i = 0; i < shown; i++) values.Add(Format(attribute.GetBoxed(i)));
                string more = data.Count > shown ? ", ..." : "";
                lines.Add($"{attribute.Name} ({attribute.Type}): {string.Join(", ", values)}{more}");
            }
            return lines;
        }

        private static string Format(object value) => value switch
        {
            null => "none",
            Object obj => obj != null ? obj.name : "none",
            float f => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            Vector3 v => $"({v.x:0.##}, {v.y:0.##}, {v.z:0.##})",
            _ => value.ToString()
        };

        private static bool NeedsSync(PlacementGraph graph)
        {
            var declared = graph.Graph.Parameters;
            var overrides = graph.ParameterOverrides;
            if (declared.Count != overrides.Count) return true;
            for (int i = 0; i < declared.Count; i++)
            {
                if (overrides[i]?.Value == null || overrides[i].Value.Name != declared[i].Name || overrides[i].Value.Type != declared[i].Type)
                    return true;
            }
            return false;
        }

        private static string ValueField(PlacementParameterType type) => type switch
        {
            PlacementParameterType.Float => nameof(PlacementParameter.FloatValue),
            PlacementParameterType.Int => nameof(PlacementParameter.IntValue),
            PlacementParameterType.Bool => nameof(PlacementParameter.BoolValue),
            PlacementParameterType.Vector3 => nameof(PlacementParameter.Vector3Value),
            PlacementParameterType.Prefab => nameof(PlacementParameter.PrefabValue),
            _ => nameof(PlacementParameter.LayerMaskValue)
        };

        private static void DrawValue(PlacementParameter parameter, string label)
        {
            switch (parameter.Type)
            {
                case PlacementParameterType.Float: EditorGUILayout.FloatField(label, parameter.FloatValue); break;
                case PlacementParameterType.Int: EditorGUILayout.IntField(label, parameter.IntValue); break;
                case PlacementParameterType.Bool: EditorGUILayout.Toggle(label, parameter.BoolValue); break;
                case PlacementParameterType.Vector3: EditorGUILayout.Vector3Field(label, parameter.Vector3Value); break;
                case PlacementParameterType.Prefab: EditorGUILayout.ObjectField(label, parameter.PrefabValue, typeof(GameObject), false); break;
                default: EditorGUILayout.MaskField(label, parameter.LayerMaskValue.value, UnityEditorInternal.InternalEditorUtility.layers); break;
            }
        }

        private static void CopyDefault(SerializedProperty value, PlacementParameter declared)
        {
            switch (declared.Type)
            {
                case PlacementParameterType.Float: value.FindPropertyRelative(nameof(PlacementParameter.FloatValue)).floatValue = declared.FloatValue; break;
                case PlacementParameterType.Int: value.FindPropertyRelative(nameof(PlacementParameter.IntValue)).intValue = declared.IntValue; break;
                case PlacementParameterType.Bool: value.FindPropertyRelative(nameof(PlacementParameter.BoolValue)).boolValue = declared.BoolValue; break;
                case PlacementParameterType.Vector3: value.FindPropertyRelative(nameof(PlacementParameter.Vector3Value)).vector3Value = declared.Vector3Value; break;
                case PlacementParameterType.Prefab: value.FindPropertyRelative(nameof(PlacementParameter.PrefabValue)).objectReferenceValue = declared.PrefabValue; break;
                default: value.FindPropertyRelative(nameof(PlacementParameter.LayerMaskValue)).intValue = declared.LayerMaskValue.value; break;
            }
        }

        /// <summary>Draws a hand-made control for a property with prefab-override bolding and the property's context menu.</summary>
        private readonly struct PropertyScope : System.IDisposable
        {
            public PropertyScope(SerializedProperty property)
            {
                EditorGUI.BeginProperty(GUILayoutUtility.GetRect(0f, 0f), GUIContent.none, property);
            }

            public void Dispose() => EditorGUI.EndProperty();
        }

        private void OnSceneGUI()
        {
            var graph = (PlacementGraph)target;
            _boundsHandle.center = Vector3.zero;
            _boundsHandle.size = graph.Size;
            _boundsHandle.handleColor = Color.white;
            _boundsHandle.wireframeColor = Color.clear;

            EditorGUI.BeginChangeCheck();
            using (new Handles.DrawingScope(Matrix4x4.TRS(graph.transform.position, graph.transform.rotation, Vector3.one)))
            {
                _boundsHandle.DrawHandle();
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(graph, "Resize Placement Graph");
                // The volume stays centred on the object, so only the size changes.
                graph.Size = _boundsHandle.size;
            }
        }
    }
}
