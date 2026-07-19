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
    [EditorTool("Projection Scatter", typeof(ProjectionScatter))]
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

            Ray mouseRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            bool maskHit = projectionScatter.GetMouseHit(mouseRay, out RaycastHit hitInfo);
            if (!maskHit) return;
            Vector3 hitWorldPos = hitInfo.point;
            Vector3 hitWorldNormal = hitInfo.normal;

            float radius = ProjectionScatterOverlay.Radius;
            float strength = ProjectionScatterOverlay.Strength;
            AnimationCurve falloffCurve = ProjectionScatterOverlay.Falloff;
            float dotSize = ProjectionScatterOverlay.DotSize;

            Handles.color = Color.white;

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
                Handles.DrawWireDisc(hitInfo.point, hitInfo.normal, radius);
            }

            bool isPainting = _mousePressed;
            if (isPainting && DateTime.Now > _lastPaintTime.AddSeconds(_paintTick))
            {
                _lastPaintTime = DateTime.Now;

                foreach (var kvp in _newWeights)
                {
                    projectionScatter.SetWeight(kvp.Key, kvp.Value);
                }
            }
        }
    }

    [Overlay(typeof(SceneView), "Projection Scatter Overlay", defaultDisplay = false)]
    public class ProjectionScatterOverlay : Overlay, ITransientOverlay
    {
        public static bool IsVisible { get; set; } = false;
        public static float Radius { get; private set; } = 1f;
        public static float Strength { get; private set; } = 1f;
        public static AnimationCurve Falloff { get; private set; } = new AnimationCurve(new Keyframe[] { new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 0f, -3f, 3f) });
        public static float DotSize { get; private set; } = 0.1f;

        public static event EventHandler OnResetMask;
        public static event EventHandler OnFloodMask;

        public bool visible => IsVisible;

        private CurveField _falloff;
        private Vector2 _defaultSize = new Vector2(400f, 180f);

        public override void OnCreated()
        {
            defaultSize = _defaultSize;
            size = _defaultSize;
        }

        public override VisualElement CreatePanelContent()
        {
            var buttonResetMask = new Button(() => OnResetMask?.Invoke(this, null))
            {
                text = "Reset Mask"
            };

            var buttonFloodMask = new Button(() => OnFloodMask?.Invoke(this, null))
            {
                text = "Flood Mask"
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
            var resetFalloff = new Button(() => { ResetFalloff(); })
            {
                text = "Reset Falloff"
            };

            var dotSize = new FloatField("Dot Size");
            dotSize.value = 0.1f;
            dotSize.RegisterCallback<ChangeEvent<float>>((evt) => DotSize = evt.newValue);

            var notes = new Label($"Left click to paint mask {Environment.NewLine}" +
                                  $"Ctrl + Left click to erase mask");

            var panel = new VisualElement() { name = "Projection Scatter Root" };
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