using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScatterTool.Editor
{
    /// <summary>
    /// The Projection Scatter tool: paints the selected Projection Scatter's mask in the Scene view (left click
    /// paints, Ctrl + left click erases). Each stroke is one Undo step.
    /// </summary>
    [EditorTool("Projection Scatter")]
    public class ProjectionScatterTool : EditorTool
    {
        [SerializeField] private Texture2D _toolIcon;
        private bool _mousePressed;
        private bool _controlPressed;
        private DateTime _lastPaintTime;
        private float _paintTick = 0.02f;
        private float _strengthMultiplier = 8f;
        private Dictionary<int, float> _newWeights = new Dictionary<int, float>();

        public override GUIContent toolbarIcon => new GUIContent(_toolIcon)
        {
            text = "Projection Scatter",
            tooltip = "Paint a mask that paints prefabs in the scene"
        };

        public override void OnActivated()
        {
            ProjectionScatterOverlay.IsVisible = true;
            ProjectionScatterOverlay.OnResetMask += OnResetMask;
            ProjectionScatterOverlay.OnFloodMask += OnFloodMask;
        }

        public override void OnWillBeDeactivated()
        {
            EndStroke();
            ProjectionScatterOverlay.IsVisible = false;
            ProjectionScatterOverlay.OnResetMask -= OnResetMask;
            ProjectionScatterOverlay.OnFloodMask -= OnFloodMask;
        }

        private void OnResetMask(object sender, EventArgs e)
        {
            if (Selection.activeGameObject == null) return;
            ProjectionScatter painter = Selection.activeGameObject.GetComponent<ProjectionScatter>();
            if (painter == null) return;

            painter.Flood(0f);
        }

        private void OnFloodMask(object sender, EventArgs e)
        {
            if (Selection.activeGameObject == null) return;
            ProjectionScatter painter = Selection.activeGameObject.GetComponent<ProjectionScatter>();
            if (painter == null) return;

            painter.Flood(1f);
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (Selection.activeGameObject == null) return;
            ProjectionScatter projectionScatter = Selection.activeGameObject.GetComponent<ProjectionScatter>();
            if (projectionScatter == null) return;

            Event current = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (current.type == EventType.Layout) HandleUtility.AddDefaultControl(controlId);

            // Ctrl is read from the event itself, so releasing it anywhere (even outside the Scene view) counts.
            _controlPressed = current.control;

            Ray mouseRay = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            bool maskHit = projectionScatter.GetMouseHit(mouseRay, out RaycastHit hitInfo);

            // Mouse up ends the stroke wherever the cursor is: the brush holds the hot control while painting,
            // so the release reaches it even outside the volume or the Scene view.
            EventType eventType = current.GetTypeForControl(controlId);
            if (eventType == EventType.MouseUp && current.button == 0 && _mousePressed)
            {
                EndStroke();
                if (GUIUtility.hotControl == controlId) GUIUtility.hotControl = 0;
                current.Use();
            }
            else if (eventType == EventType.MouseDown && current.button == 0 && !current.alt && maskHit
                     && HandleUtility.nearestControl == controlId)
            {
                BeginStroke(projectionScatter);
                GUIUtility.hotControl = controlId;
                current.Use();
            }
            else if (_mousePressed && GUIUtility.hotControl != controlId && eventType != EventType.Layout && eventType != EventType.Repaint)
            {
                EndStroke(); // something else took the mouse
            }

            if (!maskHit) return;
            Vector3 hitWorldPos = hitInfo.point;
            Vector3 hitWorldNormal = hitInfo.normal;

            float radius = ProjectionScatterOverlay.Radius;
            float strength = ProjectionScatterOverlay.Strength;
            AnimationCurve falloffCurve = ProjectionScatterOverlay.Falloff;
            float dotSize = ProjectionScatterOverlay.DotSize;
            float dotOpacity = ProjectionScatterOverlay.DotOpacity;

            Handles.color = new Color(1f, 1f, 1f, dotOpacity);

            _newWeights.Clear();
            float weightModifier = _controlPressed ? -1f : 1f;
            float radiusModifier = _controlPressed ? 1.2f : 1f;
            float sqrMaxDistance = radius * radius * radiusModifier;
            for (int i = 0; i < projectionScatter.Weights.Length; i++)
            {
                float weight = projectionScatter.Weights[i];
                Vector3 worldPos = projectionScatter.GetWorldPositionFromWeightIndex(i);
                float sqrDistance = Vector3.SqrMagnitude(worldPos - hitWorldPos);
                if (sqrDistance > sqrMaxDistance) continue;

                float distance = Mathf.Sqrt(sqrDistance);
                float normalizedDistance = Mathf.Clamp01(distance / radius / radiusModifier);
                float distanceStrength = falloffCurve.Evaluate(normalizedDistance);
                float size = dotSize * distanceStrength;

                Handles.DrawSolidDisc(worldPos, hitWorldNormal, size);

                float lerpValue = distanceStrength * strength * _paintTick * _strengthMultiplier;
                float newWeight = weight + lerpValue * weightModifier;
                _newWeights.Add(i, newWeight);
            }

            if (eventType == EventType.MouseDrag && _mousePressed && GUIUtility.hotControl == controlId) current.Use();

            using (new Handles.DrawingScope())
            {
                Handles.color = Color.white;
                Handles.DrawWireDisc(hitInfo.point, hitInfo.normal, radius);
            }

            bool isPainting = _mousePressed && _strokeTarget == projectionScatter;
            if (isPainting && DateTime.Now > _lastPaintTime.AddSeconds(_paintTick))
            {
                _lastPaintTime = DateTime.Now;

                PaintTick(projectionScatter, _newWeights);
            }

            if (_mousePressed) window.Repaint();
        }

        private ProjectionScatter _strokeTarget;
        private int _strokeUndoGroup;

        /// <summary>Starts a paint stroke: everything until mouse up is one Undo step.</summary>
        internal void BeginStroke(ProjectionScatter target)
        {
            _mousePressed = true;
            _strokeTarget = target;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Paint Projection Scatter Mask");
            _strokeUndoGroup = Undo.GetCurrentGroup();
        }

        /// <summary>Applies one tick of the brush. Part of the current stroke's Undo step.</summary>
        internal static void PaintTick(ProjectionScatter target, Dictionary<int, float> weights)
        {
            Undo.RecordObject(target, "Paint Projection Scatter Mask");
            foreach (var kvp in weights)
            {
                target.SetWeight(kvp.Key, kvp.Value);
            }
        }

        internal void EndStroke()
        {
            if (!_mousePressed) return;
            _mousePressed = false;
            _strokeTarget = null;
            Undo.CollapseUndoOperations(_strokeUndoGroup);
        }

        internal bool IsPainting => _mousePressed;
    }

    /// <summary>
    /// Brush settings and Reset/Flood Mask buttons, shown while the Projection Scatter tool is active. Without a
    /// Projection Scatter selected it shows a Create button instead.
    /// </summary>
    [Overlay(typeof(SceneView), "Projection Scatter Overlay", defaultDisplay = false)]
    public class ProjectionScatterOverlay : Overlay, ITransientOverlay
    {
        public static bool IsVisible { get; set; } = false;
        public static float Radius { get; private set; } = 2f;
        public static float Strength { get; private set; } = 0.3f;
        public static AnimationCurve Falloff { get; private set; } = new AnimationCurve(new Keyframe[] { new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 0f, -3f, 3f) });
        public static float DotSize { get; private set; } = 0.05f;
        public static float DotOpacity { get; private set; } = 0.5f;

        public static event EventHandler OnResetMask;
        public static event EventHandler OnFloodMask;

        public bool visible => IsVisible;

        private CurveField _falloff;
        private Vector2 _defaultSize = new Vector2(400f, 200f);

        public override void OnCreated()
        {
            defaultSize = _defaultSize;
            size = _defaultSize;

            Selection.selectionChanged += OnSelectionChanged;
        }

        public override void OnWillBeDestroyed()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            if(displayed) Refresh();
        }

        public void Refresh()
        {
            displayed = false;
            displayed = true;
        }

        public override VisualElement CreatePanelContent()
        {
            var panel = new VisualElement() { name = "Projection Scatter Root" };

            if (Selection.activeGameObject == null || !Selection.activeGameObject.TryGetComponent(out ProjectionScatter _))
            {
                var butonCreate = new Button(() => ProjectionScatter.Create())
                {
                    text = "Create Projection Scatter"
                };
                panel.Add(butonCreate);

                return panel;
            }

            var buttonResetMask = new Button(() => OnResetMask?.Invoke(this, null))
            {
                text = "Reset Mask"
            };

            var buttonFloodMask = new Button(() => OnFloodMask?.Invoke(this, null))
            {
                text = "Flood Mask"
            };

            var radius = new FloatField("Radius");
            radius.value = Radius;
            radius.RegisterCallback<ChangeEvent<float>>((evt) => Radius = evt.newValue);

            var strength = new Slider("Strength", 0f, 1f);
            strength.value = Strength;
            strength.RegisterCallback<ChangeEvent<float>>((evt) => Strength = evt.newValue);

            _falloff = new CurveField("Falloff");
            _falloff.value = Falloff;
            _falloff.RegisterCallback<ChangeEvent<AnimationCurve>>((evt) => Falloff = evt.newValue);
            var resetFalloff = new Button(() => { ResetFalloff(); })
            {
                text = "Reset Falloff"
            };

            var dotSize = new FloatField("Dot Size");
            dotSize.value = DotSize;
            dotSize.RegisterCallback<ChangeEvent<float>>((evt) => DotSize = evt.newValue);

            var dotOpacity = new Slider("Dot Opacity", 0f, 1f);
            dotOpacity.value = DotOpacity;
            dotOpacity.RegisterCallback<ChangeEvent<float>>((evt) => DotOpacity = evt.newValue);

            var notes = new Label($"Left click to paint mask {Environment.NewLine}" +
                                  $"Ctrl + Left click to erase mask");

            var buttonGroup = new VisualElement();
            buttonGroup.style.flexDirection = FlexDirection.Row;
            buttonGroup.Add(buttonResetMask);
            buttonGroup.Add(buttonFloodMask);
            buttonGroup.Add(resetFalloff);

            panel.Add(buttonGroup);
            panel.Add(CreateHorizontalLine());
            panel.Add(radius);
            panel.Add(strength);
            panel.Add(_falloff);
            panel.Add(dotSize);
            panel.Add(dotOpacity);
            panel.Add(CreateHorizontalLine());
            panel.Add(notes);

            return panel;
        }

        private VisualElement CreateHorizontalLine()
        {
            var hr = new VisualElement();
            hr.style.height = 1f;
            hr.style.backgroundColor = new StyleColor(Color.gray);
            hr.style.marginTop = 5f;
            hr.style.marginBottom = 5f;
            return hr;
        }

        private void ResetFalloff()
        {
            Falloff = new AnimationCurve(new Keyframe[] { new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 0f, -3f, 3f) });
            _falloff.value = Falloff;
        }
    }
}