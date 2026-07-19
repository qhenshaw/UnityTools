using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace ScatterTool.Editor
{
    [CustomEditor(typeof(ProjectionScatter))]
    public class ProjectionScatterEditor : UnityEditor.Editor
    {
        private BoxBoundsHandle _boundsHandle = new BoxBoundsHandle();

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