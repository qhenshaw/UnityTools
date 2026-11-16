using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace UnityTools.Scatter.Editor
{
    [CustomEditor(typeof(ProjectionScatter))]
    public class ProjectionScatterEditor : UnityEditor.Editor
    {
        private BoxBoundsHandle _boundsHandle = new BoxBoundsHandle();

        public override void OnInspectorGUI()
        {
            var ps = (ProjectionScatter)target;
            EditorGUILayout.HelpBox("The children of a Projection Scatter are generated. Anything placed under it is " +
                                    "destroyed when it regenerates.", MessageType.Info);
            if (ps.IsInPrefabInstance)
            {
                EditorGUILayout.HelpBox("This scatter is inside a prefab instance, so it can't regenerate here. " +
                                        "Edit it in prefab mode, or unpack the prefab instance.", MessageType.Warning);
            }
            base.OnInspectorGUI();
        }

        private void OnSceneGUI()
        {
            ProjectionScatter ps = (ProjectionScatter)target;
            EditorGUI.BeginChangeCheck();

            _boundsHandle.center = ps.LocalBounds.center;
            _boundsHandle.size = ps.LocalBounds.size;
            _boundsHandle.handleColor = Color.white;
            _boundsHandle.wireframeColor = Color.white;

            EditorGUI.BeginChangeCheck();

            using (new Handles.DrawingScope(ps.transform.localToWorldMatrix))
            {
                _boundsHandle.DrawHandle();
                _boundsHandle.center = new Vector3(0f, 0f, _boundsHandle.size.z / 2f);
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(ps, "Modify Projection Bounds");
                ps.Size = _boundsHandle.size;
            }
        }
    }
}