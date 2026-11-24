using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace UnityTools.Placement.Editor
{
    [CustomEditor(typeof(PlacementGraph))]
    internal class PlacementGraphEditor : UnityEditor.Editor
    {
        private readonly BoxBoundsHandle _boundsHandle = new BoxBoundsHandle();
        private bool _showNodes = true;

        public override void OnInspectorGUI()
        {
            var graph = (PlacementGraph)target;
            EditorGUILayout.HelpBox("The children of a Placement Graph are generated. Anything placed under it is " +
                                    "destroyed when it regenerates, except the Markers and Spline objects.", MessageType.Info);
            if (graph.IsInPrefabInstance)
            {
                EditorGUILayout.HelpBox("This Placement Graph is inside a prefab instance, so it can't regenerate here. " +
                                        "Edit it in prefab mode, or unpack the prefab instance.", MessageType.Warning);
            }

            if (graph.Graph != null && NeedsSync(graph))
            {
                Undo.RecordObject(graph, "Sync Placement Graph Parameters");
                graph.SyncParameters();
                EditorUtility.SetDirty(graph);
            }

            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "_parameters");
            DrawParameters(graph);
            serializedObject.ApplyModifiedProperties();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(graph.Graph == null))
                {
                    if (GUILayout.Button("Generate")) graph.Generate();
                }
                if (GUILayout.Button("Clear")) graph.Clear();
            }

            foreach (string warning in graph.LastWarnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
            PlacementInstances instances = graph.Instances;
            if (instances != null && instances.InstanceCount > 0)
                EditorGUILayout.HelpBox($"Instanced: {instances.InstanceCount} meshes in {instances.Batches.Count} batches, {instances.DrawCallCount} draw calls per camera.", MessageType.None);

            if (graph.Graph == null) return;
            _showNodes = EditorGUILayout.Foldout(_showNodes, "Nodes (points out, last run)", true);
            if (!_showNodes) return;

            using (new EditorGUI.IndentLevelScope())
            {
                var nodes = graph.Graph.Nodes;
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
                            Undo.RecordObject(graph, "Show Placement Graph Node");
                            graph.DebugNode = toggled ? i : -1;
                            if (graph.LastOutputs.Count == 0) graph.Evaluate();
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

        /// <summary>Each parameter on one line: an override toggle, then the value (the graph's default while off).</summary>
        private void DrawParameters(PlacementGraph graph)
        {
            if (graph.Graph == null || graph.Graph.Parameters.Count == 0) return;
            EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);
            SerializedProperty list = serializedObject.FindProperty("_parameters");
            for (int i = 0; i < list.arraySize && i < graph.Graph.Parameters.Count; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                SerializedProperty overrideOn = entry.FindPropertyRelative("Override");
                SerializedProperty value = entry.FindPropertyRelative("Value");
                PlacementParameter declared = graph.Graph.Parameters[i];
                string field = ValueField(declared.Type);

                using (new EditorGUILayout.HorizontalScope())
                {
                    overrideOn.boolValue = EditorGUILayout.Toggle(overrideOn.boolValue, GUILayout.Width(16));
                    if (overrideOn.boolValue)
                    {
                        EditorGUILayout.PropertyField(value.FindPropertyRelative(field), new GUIContent(declared.Name));
                    }
                    else
                    {
                        // Shows the default from the graph; turning the toggle on starts from it.
                        using (new EditorGUI.DisabledScope(true)) DrawValue(declared, declared.Name);
                        CopyDefault(value, declared);
                    }
                }
            }
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
