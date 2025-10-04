using Sirenix.OdinInspector.Editor;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ArtPipeline.Editor
{
    [CustomEditor(typeof(VertexPainter))]
    [CanEditMultipleObjects]
    public class VertexPainterEditor : OdinEditor
    {
        private bool _controlPressed;
        private bool _mousePressed;
        private DateTime _lastPaintTime;
        private float _paintTick = 0.05f;
        private float _strengthMultiplier = 2f;

        private List<Vector3> _vertices = new List<Vector3>();
        private List<Vector3> _normals = new List<Vector3>();
        private List<Color> _colors = new List<Color>();
        Dictionary<int, Color> _newColors = new Dictionary<int, Color>();

        private void OnSceneGUI()
        {
            if (Application.isPlaying) return;

            VertexPainter painter = (VertexPainter)target;
            if (!painter.EnablePainting) return;

            Ray mouseRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

            bool hit = SceneMeshRaycast.Raycast(mouseRay, new Renderer[] { painter.Renderer }, out RaycastHit hitInfo, out GameObject go);
            if (!hit) return;
            Vector3 hitLocalPos = painter.transform.InverseTransformPoint(hitInfo.point);

            painter.Mesh.GetVertices(_vertices);
            painter.Mesh.GetNormals(_normals);
            painter.Mesh.GetColors(_colors);
            if (_colors.Count == 0)
            {
                for (int i = 0; i < _vertices.Count; i++) _colors.Add(new Color(0f, 0f, 0f, 0f));
            }

            float sqrMaxDistance = painter.Settings.Radius * painter.Settings.Radius;
            _newColors.Clear();
            for (int i = 0; i < _vertices.Count; i++)
            {
                float sqrDistance = Vector3.Distance(_vertices[i], hitLocalPos);
                if (sqrDistance > sqrMaxDistance) continue;

                float distance = Mathf.Sqrt(sqrDistance);
                float normalizedDistance = Mathf.Clamp01(distance / painter.Settings.Radius);
                float strength = painter.Settings.Falloff.Evaluate(normalizedDistance);
                float size = painter.Settings.DebugSize * strength;

                Vector3 worldPos = painter.transform.TransformPoint(_vertices[i]);
                Color color = painter.Settings.Color;
                Handles.color = new Color(color.r, color.g, color.b, 1f);
                Handles.DrawSolidDisc(worldPos, _normals[i], size);

                float lerpValue = strength * painter.Settings.Strength * _paintTick * _strengthMultiplier;
                Color newColor = ColorBlend(_colors[i], color, lerpValue);
                _newColors.Add(i, newColor);
            }

            if (Event.current.isKey && Event.current.keyCode == KeyCode.LeftControl)
            {
                if (Event.current.type == EventType.KeyDown)
                {
                    _controlPressed = true;
                }
                if (Event.current.type == EventType.KeyUp)
                {
                    _controlPressed = false;
                }

                Event.current.Use();
            }

            if (_controlPressed && Event.current.isMouse && Event.current.button == 0)
            {
                if (Event.current.type == EventType.MouseDown)
                {
                    _mousePressed = true;
                }
                if (Event.current.type == EventType.MouseUp)
                {
                    _mousePressed = false;
                }

                Event.current.Use();
            }

            Color discColor = Color.white;
            discColor.a = _controlPressed ? 1f : 0.5f;
            Handles.color = discColor;
            Handles.DrawWireDisc(hitInfo.point, hitInfo.normal, painter.Settings.Radius);
            bool isPainting = _controlPressed && _mousePressed;
            if (isPainting && DateTime.Now > _lastPaintTime.AddSeconds(_paintTick))
            {
                _lastPaintTime = DateTime.Now;

                //Undo.RecordObject(painter.MeshFilter, "Painted Vertices");
                foreach (var indexedColor in _newColors)
                {
                    _colors[indexedColor.Key] = indexedColor.Value;
                }

                painter.Mesh.SetColors(_colors);
            }
        }

        private Color ColorBlend(Color a, Color b, float alpha)
        {
            Color color = a;
            color.r += Mathf.Sign(b.r - a.r) * alpha;
            color.g += Mathf.Sign(b.g - a.g) * alpha;
            color.b += Mathf.Sign(b.b - a.b) * alpha;
            color.a += Mathf.Sign(b.a - a.a) * alpha;

            return color;
        }
    }

    public class SceneMeshRaycast
    {
        // Raycast against all scene geometry (not just colliders). Editor use only because it uses undocumented editor functionality
        // (and also because it is extremely slow).
        public static bool Raycast(Ray ray, Renderer[] renderers, out RaycastHit hit, out GameObject gameObject, System.Func<GameObject, bool> validate = null)
        {
            float bestDist = float.MaxValue;
            gameObject = null;
            hit = new RaycastHit();

            foreach (var r in renderers)
            {
                if (validate != null && !validate(r.gameObject))
                    continue;

                Mesh mesh = null;
                if (r.TryGetComponent(out MeshFilter meshFilter))
                    mesh = meshFilter.sharedMesh;
                if (mesh != null && r is SkinnedMeshRenderer)
                    mesh = (r as SkinnedMeshRenderer).sharedMesh;
                if (mesh == null) continue;

                if (r.bounds.IntersectRay(ray, out float distance) && distance < bestDist)
                {
                    object[] rayMeshParameters = new object[] { ray, mesh, r.transform.localToWorldMatrix, null };
                    if ((bool)intersectRayMesh.Invoke(null, rayMeshParameters))
                    {
                        var rhit = (RaycastHit)rayMeshParameters[3];
                        if (rhit.distance < bestDist)
                        {
                            hit = rhit;
                            bestDist = rhit.distance;
                            gameObject = r.gameObject;
                        }
                    }
                }
            }

            return gameObject != null;
        }

        static readonly MethodInfo intersectRayMesh = typeof(HandleUtility).GetMethod("IntersectRayMesh", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
    }
}