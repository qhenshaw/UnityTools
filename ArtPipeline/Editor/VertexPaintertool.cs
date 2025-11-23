using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArtPipeline.Editor
{
    [EditorTool("Vertex Painter", typeof(MeshFilter))]
    public class VertexPaintertool : EditorTool
    {
        [SerializeField] private Texture2D _toolIcon;

        private bool _mousePressed;
        private List<Vector3> _vertices = new List<Vector3>();
        private List<Vector3> _normals = new List<Vector3>();
        private List<Color> _colors = new List<Color>();
        Dictionary<int, Color> _newColors = new Dictionary<int, Color>();
        private DateTime _lastPaintTime;
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
            VertexPainterOverlay.OnAddComponentInput += OnAddComponent;
        }

        public override void OnWillBeDeactivated()
        {
            VertexPainterOverlay.IsVisible = false;
            VertexPainterOverlay.OnResetColorInput -= OnColorsReset;
            VertexPainterOverlay.OnAddComponentInput -= OnAddComponent;
        }

        private void OnAddComponent(object sender, EventArgs e)
        {
            if (Selection.activeGameObject == null) return;
            VertexPainter painter = Selection.activeGameObject.GetComponent<VertexPainter>();
            if (painter != null)
            {
                Debug.Log("GameObject already has painter component.");
                return;
            }
            MeshFilter meshFilter = Selection.activeGameObject.GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = Selection.activeGameObject.GetComponent<MeshRenderer>();
            if (meshFilter == null || meshRenderer == null)
            {
                Debug.LogWarning("GameObject requires MeshFilter and MeshRenderer components for vertex painting.");
                return;
            }

            Selection.activeGameObject.AddComponent<VertexPainter>();
        }

        private void OnColorsReset(object sender, EventArgs e)
        {
            if (Selection.activeGameObject == null) return;
            VertexPainter painter = Selection.activeGameObject.GetComponent<VertexPainter>();
            if (painter == null) return;

            ResetColors(painter);
        }

        private void ResetColors(VertexPainter painter)
        {
            List<Color> colors = new List<Color>();
            painter.Mesh.GetColors(colors);
            if (colors.Count == 0)
            {
                for (int i = 0; i < painter.Mesh.vertexCount; i++)
                {
                    colors.Add(Color.clear);
                }
            }
            for (int i = 0; i < colors.Count; i++)
            {
                colors[i] = new Color(0f, 0f, 0f, 0f);
            }
            painter.Mesh.SetColors(colors);
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
            if(Selection.activeGameObject == null) return;
            VertexPainter painter = Selection.activeGameObject.GetComponent<VertexPainter>();
            if (painter == null) return;

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

            float radius = VertexPainterOverlay.Radius;
            float strength = VertexPainterOverlay.Strength;
            Color color = VertexPainterOverlay.Color;
            AnimationCurve falloffCurve = VertexPainterOverlay.Falloff;
            float dotSize = VertexPainterOverlay.DotSize;

            float sqrMaxDistance = radius * radius;
            _newColors.Clear();
            for (int i = 0; i < _vertices.Count; i++)
            {
                float sqrDistance = Vector3.Distance(_vertices[i], hitLocalPos);
                if (sqrDistance > sqrMaxDistance) continue;

                float distance = Mathf.Sqrt(sqrDistance);
                float normalizedDistance = Mathf.Clamp01(distance / radius);
                float distanceStrength = falloffCurve.Evaluate(normalizedDistance);
                float size = dotSize * distanceStrength;

                Vector3 worldPos = painter.transform.TransformPoint(_vertices[i]);
                Handles.color = new Color(color.r, color.g, color.b, 1f);
                Handles.DrawSolidDisc(worldPos, _normals[i], size);

                float lerpValue = distanceStrength * strength * _paintTick * _strengthMultiplier;
                Color newColor = ColorBlend(_colors[i], color, lerpValue);
                _newColors.Add(i, newColor);
            }

            if (Event.current.isMouse && Event.current.button == 0)
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

            using (new Handles.DrawingScope())
            {
                Color discColor = Color.white;
                Handles.color = discColor;
                Handles.DrawWireDisc(hitInfo.point, hitInfo.normal, radius);
            }

            bool isPainting = _mousePressed;
            if (isPainting && DateTime.Now > _lastPaintTime.AddSeconds(_paintTick))
            {
                _lastPaintTime = DateTime.Now;

                foreach (var indexedColor in _newColors)
                {
                    _colors[indexedColor.Key] = indexedColor.Value;
                }

                painter.Mesh.SetColors(_colors);
            }
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
        public static event EventHandler OnAddComponentInput;

        public bool visible => IsVisible;

        private ColorField _color;
        private CurveField _falloff;

        public override void OnCreated()
        {
            defaultSize = new Vector2(400f, 220f);
            size = new Vector2(400f, 220f);
        }

        public override VisualElement CreatePanelContent()
        {
            var panel = new VisualElement() { name = "Vertex Painter Root" };

            var buttonAddComponent = new Button(() => OnAddComponentInput?.Invoke(this, null))
            {
                text = "Add Component"
            };

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

            panel.Add(buttonAddComponent);
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