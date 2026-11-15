using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace ArtPipeline.Editor
{
    [EditorTool("Vertex Painter")]
    public class VertexPaintertool : EditorTool
    {
        [SerializeField] private Texture2D _toolIcon;

        private Mesh _strokeMesh;
        private VertexPainter _strokePainter;
        private List<Color> _colors = new List<Color>();
        private DateTime _lastPaintTime;

        private const int MaxPreviewDots = 4000;
        private const int DotSegments = 8;
        private static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        private static Material _dotMaterial;

        // unlit vertex-colored material for the batched preview dots (same shader Unity uses for GL handles)
        private static Material GetDotMaterial()
        {
            if (_dotMaterial != null) return _dotMaterial;
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return null;
            _dotMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _dotMaterial.SetFloat("_Cull", (float)CullMode.Off);
            _dotMaterial.SetFloat("_ZWrite", 0f);
            _dotMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            _dotMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            return _dotMaterial;
        }

        // cached mesh data and spatial lookup for the painter being painted
        private VertexPaintTarget _target;

        // brush state, updated only when the mouse, camera, radius or mesh changes
        private bool _brushHit;
        private Vector3 _brushPoint;
        private Vector3 _brushNormal;
        private readonly List<int> _brushIndices = new List<int>();
        private readonly List<float> _brushWeights = new List<float>();
        private Vector2 _lastMousePosition;
        private Vector3 _lastCameraPosition;
        private Quaternion _lastCameraRotation;
        private float _lastRadius;
        private int _lastTargetVersion = -1;
        private float _paintTick = 0.05f;
        private float _strengthMultiplier = 2f;

        public override GUIContent toolbarIcon => new GUIContent(_toolIcon)
        {
            text = "Vertex Painter",
            tooltip = "Paint vertex colors on meshes"
        };

        public override void OnActivated()
        {
            VertexPainterOverlay.IsVisible = true;
            VertexPainterOverlay.OnResetColorInput += OnColorsReset;
            Undo.undoRedoPerformed += OnUndoRedo;
            VertexPaintStorage.MeshGeometryChanged += OnMeshGeometryChanged;
            Selection.selectionChanged += DisposeTarget;
            AssemblyReloadEvents.beforeAssemblyReload += DisposeTarget;
        }

        public override void OnWillBeDeactivated()
        {
            EndStroke();
            VertexPainterOverlay.IsVisible = false;
            VertexPainterOverlay.OnResetColorInput -= OnColorsReset;
            Undo.undoRedoPerformed -= OnUndoRedo;
            VertexPaintStorage.MeshGeometryChanged -= OnMeshGeometryChanged;
            Selection.selectionChanged -= DisposeTarget;
            AssemblyReloadEvents.beforeAssemblyReload -= DisposeTarget;
            DisposeTarget();
        }

        private void DisposeTarget()
        {
            _target?.Dispose();
            _target = null;
            _brushHit = false;
        }

        // undo/redo may restore different vertices into the same mesh, re-check them before the next brush query
        private void OnUndoRedo() => _target?.VerifyGeometryOnNextSync();

        private void OnMeshGeometryChanged(Mesh mesh) => _target?.Invalidate();

        private void OnColorsReset(object sender, EventArgs e)
        {
            if (Selection.activeGameObject == null) return;
            VertexPainter painter = Selection.activeGameObject.GetComponent<VertexPainter>();
            if (painter == null) return;

            ResetColors(painter);
        }

        private void ResetColors(VertexPainter painter)
        {
            Mesh previous = painter.PaintedMesh;
            Mesh mesh = VertexPaintStorage.GetWritableMesh(painter);
            if (mesh == null) return;

            Undo.RegisterCompleteObjectUndo(mesh, "Reset Vertex Colors");
            List<Color> colors = new List<Color>();
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                colors.Add(new Color(0f, 0f, 0f, 0f));
            }
            mesh.SetColors(colors);
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);

            if (previous != mesh) VertexPainterOverlay.RequestRefresh();
        }

        private void BeginStroke(VertexPainter painter)
        {
            Mesh previous = painter.PaintedMesh;
            _strokeMesh = VertexPaintStorage.GetWritableMesh(painter);
            if (_strokeMesh == null) return;

            _strokePainter = painter;
            _lastPaintTime = DateTime.MinValue;
            Undo.RegisterCompleteObjectUndo(_strokeMesh, "Vertex Paint");

            // colors are read once per stroke and kept in memory, then uploaded in ranges as they change
            _strokeMesh.GetColors(_colors);
            if (_colors.Count != _strokeMesh.vertexCount)
            {
                _colors.Clear();
                for (int i = 0; i < _strokeMesh.vertexCount; i++) _colors.Add(new Color(0f, 0f, 0f, 0f));
                _strokeMesh.SetColors(_colors);
            }

            if (previous != _strokeMesh) VertexPainterOverlay.RequestRefresh();
        }

        private void EndStroke()
        {
            if (_strokeMesh != null) AssetDatabase.SaveAssetIfDirty(_strokeMesh);
            _strokeMesh = null;
            _strokePainter = null;
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

        public override void OnToolGUI(EditorWindow window)
        {
            Event e = Event.current;
            // control IDs must be requested on every event, including Layout
            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            VertexPainter painter = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<VertexPainter>() : null;
            if (painter != _strokePainter) EndStroke();
            if (painter == null)
            {
                _brushHit = false;
                return;
            }
            if (e.type == EventType.Layout) return;

            Mesh mesh = _strokeMesh != null ? _strokeMesh : VertexPaintStorage.GetDisplayMesh(painter);
            if (mesh == null) return;
            if (_target == null) _target = new VertexPaintTarget();
            _target.Sync(painter, mesh);

            EventType type = e.type;
            if (type == EventType.MouseLeaveWindow) _brushHit = false;
            if (type == EventType.MouseMove || type == EventType.MouseDrag || type == EventType.MouseDown || BrushInputsChanged(e, window))
            {
                UpdateBrush(e.mousePosition, window);
            }

            // handle mouse before the hit check so releasing off the mesh still ends the stroke
            if (e.isMouse && e.button == 0)
            {
                if (type == EventType.MouseDown && _brushHit)
                {
                    BeginStroke(painter);
                    GUIUtility.hotControl = controlId;
                }
                if (type == EventType.MouseUp)
                {
                    EndStroke();
                    if (GUIUtility.hotControl == controlId) GUIUtility.hotControl = 0;
                }

                e.Use();
            }

            bool isPainting = _strokeMesh != null;
            if (isPainting) PaintTick();

            // keep repainting while hovering or painting, so the brush follows the mouse and paint keeps flowing when held still
            if (isPainting || type == EventType.MouseMove || type == EventType.MouseDrag) window.Repaint();

            if (type == EventType.Repaint && _brushHit) DrawBrush();
        }

        // The brush only needs a new raycast when the mouse, camera, brush radius or mesh changed.
        private bool BrushInputsChanged(Event e, EditorWindow window)
        {
            if (e.type != EventType.Repaint) return false;
            Camera camera = (window as SceneView)?.camera;
            Vector3 cameraPosition = camera != null ? camera.transform.position : Vector3.zero;
            Quaternion cameraRotation = camera != null ? camera.transform.rotation : Quaternion.identity;
            bool changed = e.mousePosition != _lastMousePosition
                || cameraPosition != _lastCameraPosition
                || cameraRotation != _lastCameraRotation
                || VertexPainterOverlay.Radius != _lastRadius
                || _target.Version != _lastTargetVersion;
            return changed;
        }

        private void UpdateBrush(Vector2 mousePosition, EditorWindow window)
        {
            Camera camera = (window as SceneView)?.camera;
            _lastMousePosition = mousePosition;
            _lastCameraPosition = camera != null ? camera.transform.position : Vector3.zero;
            _lastCameraRotation = camera != null ? camera.transform.rotation : Quaternion.identity;
            _lastRadius = VertexPainterOverlay.Radius;
            _lastTargetVersion = _target.Version;

            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            _brushHit = _target.Raycast(ray, out RaycastHit hit);
            if (!_brushHit)
            {
                _brushIndices.Clear();
                _brushWeights.Clear();
                return;
            }

            _brushPoint = hit.point;
            _brushNormal = hit.normal;

            // brush distances are measured in world space so the painted area matches the brush circle at any scale
            float radius = VertexPainterOverlay.Radius;
            AnimationCurve falloff = VertexPainterOverlay.Falloff;
            _target.Query(_brushPoint, radius, _brushIndices, _brushWeights);
            for (int k = 0; k < _brushWeights.Count; k++)
            {
                _brushWeights[k] = falloff.Evaluate(Mathf.Clamp01(_brushWeights[k] / radius));
            }
        }

        private void PaintTick()
        {
            if (!_brushHit || DateTime.Now <= _lastPaintTime.AddSeconds(_paintTick)) return;
            _lastPaintTime = DateTime.Now;

            float strength = VertexPainterOverlay.Strength;
            Color color = VertexPainterOverlay.Color;
            bool changed = false;
            for (int k = 0; k < _brushIndices.Count; k++)
            {
                int i = _brushIndices[k];
                if (i >= _colors.Count) continue;
                float lerpValue = _brushWeights[k] * strength * _paintTick * _strengthMultiplier;
                _colors[i] = ColorBlend(_colors[i], color, lerpValue);
                changed = true;
            }
            if (!changed) return;

            // SetColors always replaces the whole color array (start/length only pick a slice of the source list)
            _strokeMesh.SetColors(_colors, 0, _colors.Count, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            EditorUtility.SetDirty(_strokeMesh);
        }

        // Draws the brush circle and one dot per affected vertex, batched into a single GL draw.
        // Very dense brushes are thinned out to MaxPreviewDots so drawing stays cheap.
        private void DrawBrush()
        {
            Handles.color = Color.white;
            Handles.DrawWireDisc(_brushPoint, _brushNormal, VertexPainterOverlay.Radius);

            float dotSize = VertexPainterOverlay.DotSize;
            int count = _brushIndices.Count;
            if (count == 0 || dotSize <= 0f) return;

            int step = count > MaxPreviewDots ? Mathf.CeilToInt(count / (float)MaxPreviewDots) : 1;
            Vector3[] positions = _target.WorldPositions;
            List<Vector3> normals = _target.LocalNormals;
            Matrix4x4 normalToWorld = _target.NormalToWorld;
            Color color = VertexPainterOverlay.Color;

            Material dotMaterial = GetDotMaterial();
            if (dotMaterial == null) return;
            dotMaterial.SetFloat(ZTestId, (float)Handles.zTest);
            dotMaterial.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(Handles.matrix);
            GL.Begin(GL.TRIANGLES);
            GL.Color(new Color(color.r, color.g, color.b, 1f));
            for (int k = 0; k < count; k += step)
            {
                int i = _brushIndices[k];
                float size = dotSize * _brushWeights[k];
                if (size <= 0f) continue;

                Vector3 normal = i < normals.Count ? normalToWorld.MultiplyVector(normals[i]).normalized : _brushNormal;
                Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.99f ? Vector3.up : Vector3.right).normalized * size;
                Vector3 bitangent = Vector3.Cross(normal, tangent);
                Vector3 center = positions[i];
                Vector3 previous = center + tangent;
                for (int s = 1; s <= DotSegments; s++)
                {
                    float angle = s * (Mathf.PI * 2f / DotSegments);
                    Vector3 next = center + tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle);
                    GL.Vertex(center);
                    GL.Vertex(previous);
                    GL.Vertex(next);
                    previous = next;
                }
            }
            GL.End();
            GL.PopMatrix();
        }
    }

    [Overlay(typeof(SceneView), "Vertex Painter Overlay", defaultDisplay = false)]
    public class VertexPainterOverlay : Overlay, ITransientOverlay
    {
        public static bool IsVisible { get; set; } = false;
        public static float Radius { get; private set; } = 1f;
        public static float Strength { get; private set; } = 1f;
        public static Color Color { get; private set; } = new Color(0f, 0f, 0f, 0f);
        public static AnimationCurve Falloff { get; private set; } = new AnimationCurve(new Keyframe[] { new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 0f, -3f, 3f) });
        public static float DotSize { get; private set; } = 0.1f;

        public static event EventHandler OnResetColorInput;
        private static event Action OnRefreshRequested;

        public bool visible => IsVisible;

        private ColorField _color;
        private CurveField _falloff;

        public static void RequestRefresh() => OnRefreshRequested?.Invoke();

        public override void OnCreated()
        {
            defaultSize = new Vector2(400f, 260f);
            size = new Vector2(400f, 260f);

            Selection.selectionChanged += OnSelectionChanged;
            OnRefreshRequested += OnSelectionChanged;
            ObjectChangeEvents.changesPublished += OnObjectChangesPublished;
        }

        public override void OnWillBeDestroyed()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            OnRefreshRequested -= OnSelectionChanged;
            ObjectChangeEvents.changesPublished -= OnObjectChangesPublished;
        }

        private void OnSelectionChanged()
        {
            if (displayed) Refresh();
        }

        // Rebuild when components are added or removed (restore button, Remove Component, undo/redo)
        private void OnObjectChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (!displayed) return;

            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) == ObjectChangeKind.ChangeGameObjectStructure)
                {
                    Refresh();
                    return;
                }
            }
        }

        public void Refresh()
        {
            displayed = false;
            displayed = true;
        }

        public override VisualElement CreatePanelContent()
        {
            var panel = new VisualElement() { name = "Vertex Painter Root" };

            var activeGO = Selection.activeGameObject;
            if (activeGO == null)
            {
                return panel;
            }

            var buttonAddComponent = new Button(() =>
            {
                if (activeGO.TryGetComponent(out VertexPainter painter))
                {
                    Debug.Log("GameObject already has painter component.");
                    return;
                }

                MeshFilter meshFilter = activeGO.GetComponent<MeshFilter>();
                MeshRenderer meshRenderer = activeGO.GetComponent<MeshRenderer>();
                if (meshFilter == null || meshRenderer == null)
                {
                    Debug.LogWarning("GameObject requires MeshFilter and MeshRenderer components for vertex painting.");
                    return;
                }

                Undo.AddComponent<VertexPainter>(activeGO);
                Refresh();
            })
            {
                text = "Add Vertex Painter Component"
            };

            if (!activeGO.TryGetComponent(out VertexPainter activePainter))
            {
                panel.Add(buttonAddComponent);
                return panel;
            }

            var storageLabel = new Label(VertexPaintStorage.Describe(activePainter));
            storageLabel.style.whiteSpace = WhiteSpace.Normal;
            storageLabel.style.marginBottom = 4f;

            var buttonResetColors = new Button(() => OnResetColorInput?.Invoke(this, null))
            {
                text = "Reset Colors"
            };

            _color = new ColorField("Color")
            {

            };
            _color.value = Color;
            _color.RegisterCallback<ChangeEvent<Color>>((evt) => Color = evt.newValue);

            var buttonBase = new Button(() => { SetColor(new Color(0f, 0f, 0f, 0f)); })
            {
                text = "Base"
            };
            var buttonR = new Button(() => { SetColor(new Color(1f, 0f, 0f, 0f)); })
            {
                text = "R"
            };
            var buttonG = new Button(() => { SetColor(new Color(0f, 1f, 0f, 0f)); })
            {
                text = "G"
            };
            var buttonB = new Button(() => { SetColor(new Color(0f, 0f, 1f, 0f)); })
            {
                text = "B"
            };
            var buttonA = new Button(() => { SetColor(new Color(0f, 0f, 0f, 1f)); })
            {
                text = "A"
            };

            var radius = new FloatField("Radius");
            radius.value = 1f;
            radius.RegisterCallback<ChangeEvent<float>>((evt) => Radius = evt.newValue);

            var strength = new Slider("Strength", 0f, 1f);
            strength.value = 1f;
            strength.RegisterCallback<ChangeEvent<float>>((evt) => Strength = evt.newValue);

            _falloff = new CurveField("Falloff");
            _falloff.value = Falloff;
            _falloff.RegisterCallback<ChangeEvent<AnimationCurve>>((evt) => Falloff = evt.newValue);
            var resetFalloff = new Button(() => {  ResetFalloff(); })
            {
                text = "Reset Falloff"
            };

            var dotSize = new FloatField("Dot Size");
            dotSize.value = 0.1f;
            dotSize.RegisterCallback<ChangeEvent<float>>((evt) => DotSize = evt.newValue);

            panel.Add(storageLabel);
            panel.Add(buttonResetColors);
            panel.Add(_color);
            var colorLayout = new VisualElement();
            colorLayout.style.flexDirection = FlexDirection.Row;
            colorLayout.Add(buttonBase);
            colorLayout.Add(buttonR);
            colorLayout.Add(buttonG);
            colorLayout.Add(buttonB);
            colorLayout.Add(buttonA);
            panel.Add(colorLayout);
            panel.Add(radius);
            panel.Add(strength);
            panel.Add(_falloff);
            panel.Add(resetFalloff);
            panel.Add(dotSize);

            return panel;
        }

        private void SetColor(Color color)
        {
            Color = color;
            _color.value = color;
        }

        private void ResetFalloff()
        {
            Falloff = new AnimationCurve(new Keyframe[] { new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 0f, -3f, 3f) });
            _falloff.value = Falloff;
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