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

            base.OnInspectorGUI();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(graph.Graph == null))
                {
                    if (GUILayout.Button("Generate")) graph.Generate();
                }
                if (GUILayout.Button("Clear")) graph.Clear();
            }

            foreach (string warning in graph.LastWarnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);

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
                }
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
