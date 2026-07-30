using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.IMGUI.Controls;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScatterTool.Editor
{
    [EditorTool("Phyisics Scatter")]
    public class PhysicsScatterTool : EditorTool
    {
        private enum GizmoMode
        {
            Move,
            Rotate,
            Scale
        }

        [SerializeField] private Texture2D _toolIcon;
        public override GUIContent toolbarIcon => new GUIContent(_toolIcon)
        {
            text = "Physics Scatter",
            tooltip = "Place objects in the scene using edit mode physics"
        };

        public bool IsSimulating { get; private set; }
        public bool IsGravityAllowed => EditorApplication.timeSinceStartup > _lastDragTime + _gravityKillDuration;

        private BoxBoundsHandle _boundsHandle = new BoxBoundsHandle();
        private int _previousSelectionCount;
        private Vector3 _selectionCenter;
        private Bounds _bounds;
        private Vector3[] _localPositions;
        private Vector3[] _normalizedLocalPositions;
        private GizmoMode _gizmoMode = GizmoMode.Move;
        private GameObject _previousSelected;

        private float _gravityKillDuration = 0.5f;
        private double _lastDragTime = 0f;
        private bool _previousIsPlaying;
        private Quaternion _rotation = Quaternion.identity;

        private LayerMask _worldMask;

        public override void OnActivated()
        {
            PhysicsScatterOverlay.IsVisible = true;
            PhysicsScatterOverlay.OnStartSim += (s, e) => StartSim();
            PhysicsScatterOverlay.OnStopSim += (s, e) => StopSim();
            PhysicsScatterOverlay.OnLaunch += (s, e) => Launch();
            PhysicsScatterOverlay.OnRandomizeInBounds += (s, e) => RandomizeInBounds();

            _worldMask = LayerMask.GetMask("Default");
        }

        public override void OnWillBeDeactivated()
        {
            PhysicsScatterOverlay.IsVisible = false;
            PhysicsScatterOverlay.OnStartSim -= (s, e) => StartSim();
            PhysicsScatterOverlay.OnStopSim -= (s, e) => StopSim();
            PhysicsScatterOverlay.OnLaunch -= (s, e) => Launch();
            PhysicsScatterOverlay.OnRandomizeInBounds -= (s, e) => RandomizeInBounds();

            StopSim();
        }

        private void StartSim()
        {
            _lastDragTime = 0f;
            Physics.simulationMode = SimulationMode.Script;
            IsSimulating = true;

            Rigidbody[] rigidbodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
            foreach (Rigidbody rb in rigidbodies)
            {
                rb.isKinematic = true;
            }

            var selected = Selection.gameObjects;
            foreach (GameObject go in selected)
            {
                if (go.TryGetComponent(out Rigidbody rigidbody))
                {
                    rigidbody.isKinematic = false;
                    rigidbody.useGravity = false;
                }
            }

            RecalculateBounds(Selection.gameObjects);
            RefreshToolOverlay();
        }

        private void StopSim()
        {
            Physics.simulationMode = SimulationMode.FixedUpdate;
            IsSimulating = false;
            ResetPhysicsOverrides();
            RefreshToolOverlay();
            RecalculateBounds(Selection.gameObjects);
        }

        private void SetGravity(bool enabled)
        {
            PhysicsScatterOverlay.IsGravityEnabled = enabled;
            RefreshToolOverlay();
        }

        private void Launch()
        {
            if (!IsSimulating) StartSim();
            SetGravity(true);

            foreach (GameObject go in Selection.gameObjects)
            {
                if (!go.TryGetComponent(out Rigidbody rb)) continue;

                Vector3 launchDirection = Vector3.up;
                switch (PhysicsScatterOverlay.CurrentLaunchMode)
                {
                    case PhysicsScatterOverlay.LaunchMode.Random:
                        launchDirection = UnityEngine.Random.onUnitSphere;
                        break;
                    case PhysicsScatterOverlay.LaunchMode.Sphere:
                        launchDirection = (rb.transform.position - _selectionCenter).normalized;
                        break;
                    case PhysicsScatterOverlay.LaunchMode.Custom:
                        launchDirection = PhysicsScatterOverlay.LaunchDirection.normalized;
                        float angle = UnityEngine.Random.Range(0f, PhysicsScatterOverlay.LaunchAngle);
                        float normalizedAngle = angle / 180f;
                        Quaternion randomRotation = UnityEngine.Random.rotation;
                        Quaternion launchAngle = Quaternion.LookRotation(launchDirection);
                        Quaternion rotation = Quaternion.Slerp(launchAngle, randomRotation, normalizedAngle);
                        launchDirection = rotation * Vector3.forward;
                        break;
                }

                float launchForce = PhysicsScatterOverlay.LaunchForce;
                rb.AddForce(launchDirection * launchForce, ForceMode.Impulse);
            }

            RefreshToolOverlay();
        }

        private void RefreshToolOverlay()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && sceneView.TryGetOverlay("Physics Scatter Overlay", out Overlay overlay))
            {
                if (overlay is PhysicsScatterOverlay physicsScatterOverlay) physicsScatterOverlay.Refresh();
            }
        }

        private void RandomizeInBounds()
        {
            var selectedGOs = Selection.gameObjects;
            int selectedCount = selectedGOs.Length;
            for (int i = 0; i < selectedCount; i++)
            {
                Vector3 randomPosition = new Vector3(
                        Random.Range(-_bounds.extents.x, _bounds.extents.x),
                        Random.Range(-_bounds.extents.y, _bounds.extents.y),
                        Random.Range(-_bounds.extents.z, _bounds.extents.z));
                SetLocalPosition(randomPosition, i);
                Transform transform = selectedGOs[i].transform;
                Undo.RecordObject(transform, "Randomize Selected Objects");
                transform.position = _selectionCenter + randomPosition;
                EditorUtility.SetDirty(transform);
            }
        }

        public override void OnToolGUI(EditorWindow window)
        {
            bool isPlaying = Application.isPlaying;
            if (isPlaying != _previousIsPlaying)
            {
                _previousIsPlaying = isPlaying;
                if (isPlaying && IsSimulating) StopSim();
                RefreshToolOverlay();
            }

            if (isPlaying) return;

            var selectedGOs = Selection.gameObjects;
            int selectedCount = selectedGOs.Length;
            if (selectedCount == 0) return;
            if (selectedCount == 1 && selectedGOs[0] != _previousSelected)
            {
                _previousSelected = selectedGOs[0];
                RecalculateBounds(selectedGOs);
                if (IsSimulating)
                {
                    StopSim();
                    StartSim();
                }
                _previousSelectionCount = 1;
            }

            if (selectedCount != _previousSelectionCount)
            {
                RecalculateBounds(selectedGOs);
                if (IsSimulating)
                {
                    StopSim();
                    StartSim();
                }
                _previousSelectionCount = selectedCount;
            }

            bool movingCamera = Mouse.current.rightButton.isPressed;
            bool shiftPressed = Keyboard.current.leftShiftKey.isPressed;
            bool controlPressed = Keyboard.current.leftCtrlKey.isPressed;

            if (!movingCamera && GetKeyDown(KeyCode.W))
            {
                _gizmoMode = GizmoMode.Move;
            }

            if (!movingCamera && GetKeyDown(KeyCode.E))
            {
                _gizmoMode = GizmoMode.Rotate;
            }

            if (!movingCamera && GetKeyDown(KeyCode.R))
            {
                _gizmoMode = GizmoMode.Scale;
            }

            if (!shiftPressed && !controlPressed && GetKeyDown(KeyCode.Space))
            {
                if (IsSimulating) StopSim();
                else StartSim();
            }

            if (GetKeyDown(KeyCode.G))
            {
                SetGravity(!PhysicsScatterOverlay.IsGravityEnabled);
            }

            if (GetMouseWorldPosition(out Vector3 mousePosition, out Vector3 mouseNormal))
            {
                Handles.DrawWireDisc(mousePosition, mouseNormal, 0.25f);
                Handles.DrawDottedLine(mousePosition, mousePosition + mouseNormal * 0.5f, 5f);

                if (!shiftPressed && !controlPressed && GetKeyDown(KeyCode.M))
                {
                    TeleportSelection(selectedGOs);
                }

                if (!shiftPressed && !controlPressed && GetKeyDown(KeyCode.C))
                {
                    CopySelection(selectedGOs);
                }
            }

            Color color = IsSimulating ? Color.yellow : Color.white;
            Handles.DrawOutline(Selection.gameObjects, color);
            DrawGroundingGizmo();

            BoundsScaleSelection(selectedGOs);

            switch (_gizmoMode)
            {
                case GizmoMode.Move:
                    _selectionCenter = DragSelection(selectedGOs);
                    break;
                case GizmoMode.Rotate:
                    RotateSelection(selectedGOs);
                    break;
                case GizmoMode.Scale:
                    ScaleSelection(selectedGOs);
                    break;
            }

            if (!IsSimulating) return;

            foreach (var go in Selection.gameObjects)
            {
                if (go.transform.position.y < PhysicsScatterOverlay.ResetHeight && go.TryGetComponent(out Rigidbody rb))
                {
                    Vector3 position = go.transform.position;
                    position.y = PhysicsScatterOverlay.ResetHeight + 10f;
                    go.transform.position = position;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }

            AccelerateToPosition(selectedGOs, _selectionCenter);
            if (PhysicsScatterOverlay.IsGravityEnabled && IsGravityAllowed) ApplyGravity(selectedGOs);
            for (int i = 0; i < PhysicsScatterOverlay.StepCount; i++)
            {
                Physics.Simulate(PhysicsScatterOverlay.TimeStep);
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        }

        private void DrawGroundingGizmo()
        {
            Vector3 start = _selectionCenter - Vector3.up * _bounds.extents.y;
            Vector3 dir = Vector3.down;
            Ray groundRay = new Ray(start, dir);
            if (Physics.Raycast(groundRay, out RaycastHit hitInfo, Mathf.Infinity, _worldMask))
            {
                Vector3 end = hitInfo.point;
                Handles.DrawDottedLine(start, end, 5f);
                Handles.DrawWireDisc(end, hitInfo.normal, 0.5f);
            }
        }

        private bool GetMouseWorldPosition(out Vector3 position, out Vector3 normal)
        {
            Vector2 guiMousePos = Event.current.mousePosition;
            bool hitGeometry = HandleUtility.PlaceObject(guiMousePos, out position, out normal);
            if (hitGeometry) return true;

            position = Vector3.zero;
            normal = Vector3.up;
            return false;
        }

        private static bool GetKeyState(KeyCode key, out bool isPressed)
        {
            if (Event.current.isKey && Event.current.keyCode == key)
            {
                isPressed = Event.current.type == EventType.KeyDown;
                Event.current.Use();
                return true;
            }
            isPressed = false;
            return false;
        }

        private static bool GetKeyDown(KeyCode key)
        {
            if (Event.current.isKey && Event.current.keyCode == key && Event.current.type == EventType.KeyDown)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        private static void EatInput(KeyCode key)
        {
            if (Event.current.isKey && Event.current.keyCode == key)
            {
                Event.current.Use();
            }
        }

        private void RecalculateBounds(GameObject[] selectedGOs)
        {
            if (selectedGOs.Length == 0) return;
            _rotation = Quaternion.identity;

            for (int i = 0; i < selectedGOs.Length; i++)
            {
                GameObject sb = selectedGOs[i];
                Transform transform = sb.transform;
                if (i == 0) _bounds = new Bounds(transform.position, Vector3.one);
                Bounds objBounds = GetObjectBounds(sb);
                _bounds.Encapsulate(objBounds);
            }

            _selectionCenter = _bounds.center;

            for (int i = 0; i < selectedGOs.Length; i++)
            {
                GameObject sb = selectedGOs[i];
                Transform transform = sb.transform;
                Vector3 localPos = transform.position - _selectionCenter;
                SetLocalPosition(localPos, i);
            }
        }

        private Bounds GetObjectBounds(GameObject go)
        {
            Bounds bounds = new Bounds(go.transform.position, Vector3.zero);
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
            }

            return bounds;
        }

        private void SetLocalPosition(Vector3 position, int index, bool setNormalized = true)
        {
            int selectedCount = Selection.gameObjects.Length;
            if (_localPositions == null || _localPositions.Length != selectedCount) _localPositions = new Vector3[selectedCount];
            if (_normalizedLocalPositions == null || _normalizedLocalPositions.Length != selectedCount) _normalizedLocalPositions = new Vector3[selectedCount];

            _localPositions[index] = position;
            Vector3 localNorm = new Vector3(
                    position.x / _bounds.extents.x,
                    position.y / _bounds.extents.y,
                    position.z / _bounds.extents.z
            );
            if (setNormalized) _normalizedLocalPositions[index] = localNorm;
        }

        private void BoundsScaleSelection(GameObject[] selectedGOs)
        {
            Vector3 newSize;
            Vector3 newCenter;
            EditorGUI.BeginChangeCheck();
            _boundsHandle.center = _selectionCenter;
            _boundsHandle.size = _bounds.size;
            _boundsHandle.handleColor = Color.white;
            _boundsHandle.wireframeColor = Color.white;

            using (new Handles.DrawingScope(Handles.matrix))
            {
                _boundsHandle.DrawHandle();
                newSize = _boundsHandle.size;
                newCenter = _boundsHandle.center;
            }

            if (EditorGUI.EndChangeCheck())
            {
                _lastDragTime = EditorApplication.timeSinceStartup;
                Vector3 sizeDelta = newSize - _bounds.size;
                Vector3 scaleDelta = new Vector3(
                    sizeDelta.x / _bounds.size.x,
                    sizeDelta.y / _bounds.size.y,
                    sizeDelta.z / _bounds.size.z
                );

                for (int i = 0; i < selectedGOs.Length; i++)
                {
                    GameObject go = selectedGOs[i];
                    Transform transform = go.transform;
                    Undo.RecordObject(transform, "Move Selected Objects");
                    Vector3 localNormPos = _normalizedLocalPositions[i];
                    Vector3 localPosition = Vector3.Scale(newSize * 0.5f, localNormPos);
                    SetLocalPosition(localPosition, i);
                    if (!IsSimulating) transform.position = newCenter + localPosition;
                    EditorUtility.SetDirty(transform);
                }

                _bounds.size = newSize;
                _bounds.center = newCenter;
            }
        }

        private void ScaleSelection(GameObject[] selectedGOs)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 newSize = Handles.ScaleHandle(_bounds.size, _selectionCenter, Quaternion.identity, HandleUtility.GetHandleSize(_selectionCenter));

            if (EditorGUI.EndChangeCheck())
            {
                _lastDragTime = EditorApplication.timeSinceStartup;
                Vector3 sizeDelta = newSize - _bounds.size;
                for (int i = 0; i < selectedGOs.Length; i++)
                {
                    GameObject go = selectedGOs[i];
                    Transform transform = go.transform;
                    Undo.RecordObject(transform, "Move Selected Objects");
                    Vector3 localNormPos = _normalizedLocalPositions[i];
                    Vector3 localPosition = Vector3.Scale(newSize * 0.5f, localNormPos);
                    SetLocalPosition(localPosition, i, false);
                    if (!IsSimulating) transform.position = _selectionCenter + localPosition;
                    EditorUtility.SetDirty(transform);
                }
                _bounds.size = newSize;
            }
        }

        private Vector3 DragSelection(GameObject[] selectedGOs)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 currentCenter = Handles.PositionHandle(_selectionCenter, Quaternion.identity);

            if (EditorGUI.EndChangeCheck())
            {
                _lastDragTime = EditorApplication.timeSinceStartup;
                Vector3 delta = currentCenter - _selectionCenter;
                foreach (GameObject go in selectedGOs)
                {
                    Transform transform = go.transform;
                    Undo.RecordObject(transform, "Move Selected Objects");
                    Vector3 position = transform.position;
                    Vector3 newPosition = transform.position + delta;

                    if (!IsSimulating)
                    {
                        transform.position = newPosition;
                    }

                    EditorUtility.SetDirty(transform);
                }
            }

            return currentCenter;
        }

        private void RotateSelection(GameObject[] selectedGOs)
        {
            EditorGUI.BeginChangeCheck();
            Quaternion currentRotation = Handles.RotationHandle(_rotation, _selectionCenter);
            if (EditorGUI.EndChangeCheck())
            {
                _lastDragTime = EditorApplication.timeSinceStartup;
                Quaternion deltaRotation = currentRotation * Quaternion.Inverse(_rotation);
                if (IsSimulating)
                {
                    for (int i = 0; i < _localPositions.Length; i++)
                    {
                        GameObject go = selectedGOs[i];
                        Transform transform = go.transform;
                        Undo.RecordObject(transform, "Rotate Selected Objects");
                        Vector3 localPosition = _localPositions[i];
                        Vector3 rotatedPosition = deltaRotation * localPosition;
                        SetLocalPosition(rotatedPosition, i, true);
                        transform.rotation = deltaRotation * transform.rotation;
                        EditorUtility.SetDirty(transform);
                    }
                }
                else
                {
                    for (int i = 0; i < selectedGOs.Length; i++)
                    {
                        GameObject go = selectedGOs[i];
                        Transform transform = go.transform;
                        Undo.RecordObject(transform, "Rotate Selected Objects");
                        Vector3 localPosition = transform.position - _selectionCenter;
                        Vector3 rotatedPosition = deltaRotation * localPosition;
                        Vector3 newPosition = _selectionCenter + rotatedPosition;
                        SetLocalPosition(rotatedPosition, i, true);
                        transform.rotation = deltaRotation * transform.rotation;
                        transform.position = newPosition;
                        EditorUtility.SetDirty(transform);
                    }
                }
                _rotation = currentRotation;
            }
        }

        private void TeleportSelection(GameObject[] selectedGOs)
        {
            if (GetMouseWorldPosition(out Vector3 mousePosition, out Vector3 mouseNormal))
            {
                mousePosition += mouseNormal * PhysicsScatterOverlay.MoveCopyFloorDistance;
                mousePosition += new Vector3(0f, _bounds.extents.y, 0f);
                _selectionCenter = mousePosition;
                for (int i = 0; i < selectedGOs.Length; i++)
                {
                    GameObject go = selectedGOs[i];
                    Transform transform = go.transform;
                    Undo.RecordObject(transform, "Move Selected Objects");
                    Vector3 localNormPos = _normalizedLocalPositions[i];
                    Vector3 localPosition = Vector3.Scale(_bounds.size * 0.5f, localNormPos);
                    transform.position = _selectionCenter + localPosition;
                    if (PhysicsScatterOverlay.RemainUpright)
                    {
                        Quaternion uprightRotation = Quaternion.FromToRotation(transform.up, Vector3.up) * transform.rotation;
                        transform.rotation = uprightRotation;
                    }

                    EditorUtility.SetDirty(transform);

                    if (go.TryGetComponent(out Rigidbody rb))
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                }
            }
        }

        private void CopySelection(GameObject[] gameObjects)
        {
            RecalculateBounds(gameObjects);

            List<GameObject> copies = new List<GameObject>();
            foreach (GameObject gameObject in gameObjects)
            {
                GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                GameObject spawned = PrefabUtility.InstantiatePrefab(prefab, gameObject.scene) as GameObject;
                spawned.transform.SetParent(gameObject.transform.parent);
                spawned.transform.SetPositionAndRotation(gameObject.transform.position, gameObject.transform.rotation);
                Undo.RegisterCreatedObjectUndo(spawned, "Copy " + spawned.name);
                copies.Add(spawned);

                if (IsSimulating && gameObject.TryGetComponent(out Rigidbody rb))
                {
                    if (spawned.TryGetComponent(out Rigidbody spawnedRb))
                    {
                        rb.isKinematic = true;
                    }
                }
            }

            GameObject[] copiesArray = copies.ToArray();
            Selection.objects = null;
            Selection.objects = copiesArray;
            StopSim();
            TeleportSelection(copiesArray);
            StartSim();
        }

        private void AccelerateToPosition(GameObject[] selectedGOs, Vector3 currentCenter)
        {
            for (int i = 0; i < selectedGOs.Length; i++)
            {
                GameObject go = selectedGOs[i];
                Vector3 localPosition = _localPositions[i];
                Transform transform = go.transform;
                Vector3 position = transform.position;
                Vector3 target = currentCenter + localPosition;

                if (IsSimulating && (!PhysicsScatterOverlay.IsGravityEnabled || !IsGravityAllowed))
                {
                    if (go.TryGetComponent(out Rigidbody rb) && !rb.isKinematic)
                    {
                        Vector3 vec = target - position;
                        float force = PhysicsScatterOverlay.MoveForce;
                        float linearDrag = PhysicsScatterOverlay.LinearDrag / 100f;
                        float angularDrag = PhysicsScatterOverlay.AngularDrag / 100f;
                        rb.AddForce(force * PhysicsScatterOverlay.TimeStep * vec, ForceMode.Acceleration);

                        if (PhysicsScatterOverlay.RemainUpright)
                        {
                            Vector3 correctionAxis = Vector3.Cross(transform.up, Vector3.up);
                            float correctionAngle = Vector3.Angle(transform.up, Vector3.up);
                            if (correctionAngle > 1f)
                            {
                                float uprightTorque = PhysicsScatterOverlay.UprightTorque;
                                rb.AddTorque(correctionAxis * uprightTorque * correctionAngle, ForceMode.Acceleration);
                            }
                        }

                        rb.linearVelocity *= 1f - linearDrag;
                        rb.angularVelocity *= 1f - angularDrag;
                    }
                }
            }
        }

        private void ApplyGravity(GameObject[] selectedGOs)
        {
            Vector3 gravity = new Vector3(0f, -9.81f, 0f);
            foreach (GameObject go in selectedGOs)
            {
                if (go.TryGetComponent(out Rigidbody rb))
                {
                    rb.AddForce(gravity, ForceMode.Acceleration);
                }
            }
        }

        private void ResetPhysicsOverrides()
        {
            var rigidbodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
            foreach (Rigidbody rb in rigidbodies)
            {
                SerializedObject serializedObj = new SerializedObject(rb);

                SerializedProperty kinematicProp = serializedObj.FindProperty("m_IsKinematic");
                if (kinematicProp != null && kinematicProp.isInstantiatedPrefab)
                {
                    PrefabUtility.RevertPropertyOverride(kinematicProp, InteractionMode.AutomatedAction);
                }

                SerializedProperty gravityProp = serializedObj.FindProperty("m_UseGravity");
                if (gravityProp != null && gravityProp.isInstantiatedPrefab)
                {
                    PrefabUtility.RevertPropertyOverride(gravityProp, InteractionMode.AutomatedAction);
                }
            }
        }
    }
}