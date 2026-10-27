using UnityEditor;
using UnityEngine;

namespace ArtPipeline.Editor
{
    [CustomEditor(typeof(VertexPainter))]
    public class VertexPainterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            VertexPainter painter = (VertexPainter)target;

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Source Mesh", painter.SourceMesh, typeof(Mesh), false);
                EditorGUILayout.ObjectField("Painted Mesh", painter.PaintedMesh, typeof(Mesh), false);
            }

            EditorGUILayout.HelpBox(VertexPaintStorage.Describe(painter), MessageType.None);

            if (painter.PaintedMesh != null && !VertexPaintStorage.IsSavedAsAsset(painter))
            {
                EditorGUILayout.HelpBox("The painted mesh is embedded in the scene. Save it to an asset so it can be used in prefabs.", MessageType.Warning);
                if (GUILayout.Button("Save to Asset")) VertexPaintStorage.MakeUnique(painter);
            }

            if (VertexPaintStorage.SourceTopologyChanged(painter))
            {
                EditorGUILayout.HelpBox("The source mesh vertex count no longer matches the painted mesh. Rebuilding will reset vertex colors.", MessageType.Warning);
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(painter.SourceMesh == null))
            {
                if (GUILayout.Button(new GUIContent("Rebuild from Source", "Copy the latest source mesh into the painted asset, keeping vertex colors when the vertex count matches.")))
                {
                    VertexPaintStorage.RebuildFromSource(painter);
                }
            }
            if (GUILayout.Button(new GUIContent("Make Unique", "Create a new painted mesh asset for this object only.")))
            {
                VertexPaintStorage.MakeUnique(painter);
            }
            if (GUILayout.Button(new GUIContent("Remove Painter & Restore Source Mesh", "The painted mesh asset is left in the project.")))
            {
                VertexPaintStorage.RestoreSource(painter);
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Find Unused Paint Meshes..."))
            {
                VertexPaintCleanupWindow.Open();
            }
        }
    }
}
