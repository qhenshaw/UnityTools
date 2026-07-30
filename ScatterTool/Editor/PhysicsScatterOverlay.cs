using System;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;
using Color = UnityEngine.Color;
using FontStyle = UnityEngine.FontStyle;

namespace ScatterTool.Editor
{
    [Overlay(typeof(SceneView), "Physics Scatter Overlay", defaultDisplay = false)]
    public class PhysicsScatterOverlay : Overlay, ITransientOverlay
    {
        public enum LaunchMode
        {
            Sphere,
            Random,
            Custom,
        }

        public static bool IsVisible { get; set; } = false;
        public static float TimeStep { get; private set; } = 0.02f;
        public static int StepCount { get; private set; } = 1;
        public static bool IsGravityEnabled { get; set; } = false;
        public static float MoveForce { get; private set; } = 500f;
        public static float LinearDrag { get; private set; } = 5f;
        public static float AngularDrag { get; private set; } = 5f;
        public static LaunchMode CurrentLaunchMode { get; private set; } = LaunchMode.Random;
        public static float LaunchForce { get; private set; } = 10f;
        public static Vector3 LaunchDirection { get; private set; } = new Vector3(0f, 1f, 0f);
        public static float LaunchAngle { get; private set; } = 45f;
        public static float MoveCopyFloorDistance { get; private set; } = 0.5f;
        public static bool RemainUpright { get; private set; } = false;
        public static float UprightTorque { get; private set; } = 1f;
        public static float ResetHeight { get; private set; } = -10f;

        public static event EventHandler OnStartSim;
        public static event EventHandler OnStopSim;
        public static event EventHandler OnLaunch;
        public static event EventHandler OnRandomizeInBounds;

        public bool visible => IsVisible;

        private Vector2 _defaultSize = new Vector2(280f, 350f);

        public override void OnCreated()
        {
            defaultSize = _defaultSize;
            size = _defaultSize;
        }

        public override VisualElement CreatePanelContent()
        {
            if (Application.isPlaying)
            {
                var disabledPanel = new VisualElement() { name = "Disabled Tool Root" };
                var diabledLabel = new Label("Disabled in Play Mode.")
                {
                    style =
                {
                    unityTextAlign = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    marginTop = 10f,
                    marginBottom = 10f,
                    unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold)
                }
                };
                disabledPanel.Add(diabledLabel);
                return disabledPanel;
            }

            var buttonStartSim = new Button(() => OnStartSim?.Invoke(this, null))
            {
                text = "Start Simulation"
            };
            buttonStartSim.iconImage = EditorGUIUtility.IconContent("Animation.Play").image as Texture2D;
            SetBorder(buttonStartSim, new Color(0f, 1f, 0f), 2f);

            var buttonStopSim = new Button(() => OnStopSim?.Invoke(this, null))
            {
                text = "Stop Simulation"
            };
            buttonStopSim.iconImage = EditorGUIUtility.IconContent("Stop").image as Texture2D;
            SetBorder(buttonStopSim, new Color(1f, 0f, 0f), 2f);

            var toggleGravity = new Button(() =>
            {
                IsGravityEnabled = !IsGravityEnabled;
                Refresh();
            });
            toggleGravity.text = IsGravityEnabled ? "Disable Gravity" : "Enable Gravity";
            toggleGravity.iconImage = EditorGUIUtility.IconContent("Download-Available-Selected-Focused").image as Texture2D;
            if (IsGravityEnabled) SetBorder(toggleGravity, new Color(1f, 0f, 0f), 2f);
            else SetBorder(toggleGravity, new Color(0f, 1f, 0f), 2f);

            var buttonLaunch = new Button(() => OnLaunch?.Invoke(this, null))
            {
                text = "Launch"
            };

            var remainUprightButton = new Button(() =>
            {
                RemainUpright = !RemainUpright;
                Refresh();
            });
            remainUprightButton.text = RemainUpright ? "Disable Remain Upright" : "Enable Remain Upright";
            if (RemainUpright) SetBorder(remainUprightButton, new Color(1f, 1f, 1f), 2f);

            var buttonRandomizeInBounds = new Button(() => OnRandomizeInBounds?.Invoke(this, null))
            {
                text = "Randomize in Bounds"
            };

            var launchForce = new FloatField("Launch Force");
            launchForce.value = LaunchForce;
            launchForce.RegisterCallback<ChangeEvent<float>>((evt) => LaunchForce = evt.newValue);

            var launchDirection = new Vector3Field("Launch Direction");
            LaunchDirection = LaunchDirection.normalized;
            launchDirection.value = LaunchDirection;
            launchDirection.RegisterCallback<ChangeEvent<Vector3>>((evt) => LaunchDirection = evt.newValue);

            var launchAngle = new FloatField("Launch Angle");
            launchAngle.value = LaunchAngle;
            launchAngle.RegisterCallback<ChangeEvent<float>>((evt) => LaunchAngle = evt.newValue);

            var launchMode = new EnumField("Launch Mode", CurrentLaunchMode);
            launchMode.Init(CurrentLaunchMode);
            launchMode.RegisterCallback<ChangeEvent<Enum>>((evt) =>
            {
                CurrentLaunchMode = (LaunchMode)evt.newValue;
                Refresh();
            });

            var timeStep = new FloatField("Time Step");
            timeStep.value = TimeStep;
            timeStep.RegisterCallback<ChangeEvent<float>>((evt) => TimeStep = evt.newValue);

            var stepCount = new IntegerField("Step Count");
            stepCount.value = StepCount;
            stepCount.RegisterCallback<ChangeEvent<int>>((evt) => StepCount = evt.newValue);

            var moveForce = new FloatField("Move Force");
            moveForce.value = MoveForce;
            moveForce.RegisterCallback<ChangeEvent<float>>((evt) => MoveForce = evt.newValue);

            var linearDrag = new FloatField("Linear Drag");
            linearDrag.value = LinearDrag;
            linearDrag.RegisterCallback<ChangeEvent<float>>((evt) => LinearDrag = evt.newValue);

            var angularDrag = new FloatField("Angular Drag");
            angularDrag.value = AngularDrag;
            angularDrag.RegisterCallback<ChangeEvent<float>>((evt) => AngularDrag = evt.newValue);

            var uprightTorque = new FloatField("Upright Torque");
            uprightTorque.value = UprightTorque;
            uprightTorque.RegisterCallback<ChangeEvent<float>>((evt) => UprightTorque = evt.newValue);

            var moveCopyFloorDistance = new FloatField("Move/Copy Floor Distance");
            moveCopyFloorDistance.value = MoveCopyFloorDistance;
            moveCopyFloorDistance.RegisterCallback<ChangeEvent<float>>((evt) => MoveCopyFloorDistance = evt.newValue);

            var resetHeight = new FloatField("Reset Height");
            resetHeight.value = ResetHeight;
            resetHeight.RegisterCallback<ChangeEvent<float>>((evt) => ResetHeight = evt.newValue);

            var notes = new Label($"[Space] Toggle Simulation {Environment.NewLine}" +
                                  $"[G] Toggle Gravity {Environment.NewLine}" +
                                  $"[Control + Space] Quick Copy {Environment.NewLine}" +
                                  $"[Shift + Space] Quick Move");

            var launchHeader = new Label("Launch")
            {
                style =
            {
                unityTextAlign = TextAnchor.MiddleCenter,
                fontSize = 12,
                marginTop = 2f,
                marginBottom = 8f,
                unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold)
            }
            };

            var placementHeader = new Label("Placement")
            {
                style =
            {
                unityTextAlign = TextAnchor.MiddleCenter,
                fontSize = 12,
                marginTop = 2f,
                marginBottom = 8f,
                unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold)
            }
            };

            var panel = new VisualElement() { name = "Physics Scatter Root" };

            var scroll = new ScrollView(ScrollViewMode.Vertical);

            var toggleSimGroup = new VisualElement();
            toggleSimGroup.style.flexDirection = FlexDirection.Row;
            if (Physics.simulationMode == SimulationMode.FixedUpdate) toggleSimGroup.Add(buttonStartSim);
            if (Physics.simulationMode == SimulationMode.Script) toggleSimGroup.Add(buttonStopSim);
            toggleSimGroup.Add(toggleGravity);

            var physicsFoldout = new Foldout() { text = "Physics Settings", value = false };
            physicsFoldout.Add(moveForce);
            physicsFoldout.Add(linearDrag);
            physicsFoldout.Add(angularDrag);
            physicsFoldout.Add(uprightTorque);

            scroll.Add(placementHeader);
            scroll.Add(buttonRandomizeInBounds);
            scroll.Add(remainUprightButton);
            scroll.Add(moveCopyFloorDistance);
            scroll.Add(resetHeight);
            scroll.Add(CreateHorizontalLine());
            scroll.Add(launchHeader);
            scroll.Add(launchMode);
            if (CurrentLaunchMode == LaunchMode.Custom) scroll.Add(launchDirection);
            if (CurrentLaunchMode == LaunchMode.Custom) scroll.Add(launchAngle);
            scroll.Add(launchForce);
            scroll.Add(buttonLaunch);
            scroll.Add(CreateHorizontalLine());
            scroll.Add(physicsFoldout);

            panel.Add(toggleSimGroup);
            panel.Add(CreateHorizontalLine());
            panel.Add(scroll);
            panel.Add(CreateHorizontalLine());
            panel.Add(notes);

            return panel;
        }

        private void SetBorder(VisualElement element, Color color, float width)
        {
            element.style.borderTopColor = new StyleColor(color);
            element.style.borderBottomColor = new StyleColor(color);
            element.style.borderLeftColor = new StyleColor(color);
            element.style.borderRightColor = new StyleColor(color);
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
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

        public void Refresh()
        {
            displayed = false;
            displayed = true;
        }
    }
}