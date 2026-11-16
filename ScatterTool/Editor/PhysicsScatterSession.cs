using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ScatterTool.Editor
{
    /// <summary>
    /// One edit-mode physics simulation of the selected objects. Start freezes every other Rigidbody in the open
    /// scenes and gives the selected objects a free Rigidbody (adding one if needed); Stop puts every Rigidbody back
    /// exactly as it was, removes the added ones, restores the physics simulation mode and records where the
    /// selected objects ended up as one Undo step.
    /// </summary>
    internal class PhysicsScatterSession
    {
        internal const string UndoName = "Physics Scatter";

        private struct BodyState
        {
            public Rigidbody Body;
            public bool IsKinematic;
            public bool UseGravity;
        }

        private struct Pose
        {
            public Transform Transform;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        private readonly List<BodyState> _bodies = new();
        private readonly List<Rigidbody> _added = new();
        private readonly List<Pose> _startPoses = new();
        private SimulationMode _previousMode;
        private double _accumulated;

        public bool IsRunning { get; private set; }
        public IReadOnlyList<GameObject> Objects { get; private set; } = Array.Empty<GameObject>();

        /// <summary>Called before each physics step with the step length, to apply forces.</summary>
        public Action<float> BeforeStep;

        public void Start(IEnumerable<GameObject> objects)
        {
            if (IsRunning) Stop();

            Objects = objects.Where(go => go != null).Distinct().ToArray();
            _previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            _accumulated = 0d;

#if UNITY_6000_5_OR_NEWER
            Rigidbody[] allBodies = Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include);
#else
            Rigidbody[] allBodies = Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#endif
            foreach (Rigidbody body in allBodies)
            {
                _bodies.Add(new BodyState { Body = body, IsKinematic = body.isKinematic, UseGravity = body.useGravity });
                body.isKinematic = true;
            }

            foreach (GameObject go in Objects)
            {
                if (!go.TryGetComponent(out Rigidbody body))
                {
                    body = go.AddComponent<Rigidbody>();
                    body.hideFlags = HideFlags.DontSave;
                    body.mass = 1f;
                    body.linearDamping = 0.5f;
                    body.angularDamping = 0.05f;
                    _added.Add(body);
                }
                body.isKinematic = false;
                body.useGravity = false;
                _startPoses.Add(new Pose { Transform = go.transform, Position = go.transform.position, Rotation = go.transform.rotation });
            }

            IsRunning = true;
        }

        /// <summary>Advances the simulation by real time: stepCount steps per timeStep of real time.</summary>
        public int Step(double realSeconds, float timeStep, int stepCount)
        {
            if (!IsRunning || timeStep <= 0f) return 0;
            stepCount = Mathf.Max(1, stepCount);
            float subStep = timeStep / stepCount;

            // Don't try to catch up after a long editor stall.
            _accumulated = Math.Min(_accumulated + Math.Max(0d, realSeconds), 0.25d);
            int steps = 0;
            while (_accumulated >= timeStep)
            {
                _accumulated -= timeStep;
                for (int i = 0; i < stepCount; i++)
                {
                    BeforeStep?.Invoke(subStep);
                    Physics.Simulate(subStep);
                    steps++;
                }
            }
            return steps;
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;

            // Record the moves as one Undo step: put the objects back where they started, record, then move them.
            var endPoses = _startPoses.Where(p => p.Transform != null)
                .Select(p => new Pose { Transform = p.Transform, Position = p.Transform.position, Rotation = p.Transform.rotation })
                .ToList();
            foreach (Pose pose in _startPoses)
            {
                if (pose.Transform != null) pose.Transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            Undo.RecordObjects(endPoses.Select(p => (Object)p.Transform).ToArray(), UndoName);
            var dirtyScenes = new HashSet<Scene>();
            for (int i = 0; i < endPoses.Count; i++)
            {
                Pose end = endPoses[i];
                Pose start = _startPoses.First(p => p.Transform == end.Transform);
                end.Transform.SetPositionAndRotation(end.Position, end.Rotation);
                if (end.Position != start.Position || end.Rotation != start.Rotation) dirtyScenes.Add(end.Transform.gameObject.scene);
            }
            Undo.CollapseUndoOperations(group);

            foreach (Rigidbody body in _added)
            {
                if (body != null) Object.DestroyImmediate(body);
            }
            foreach (BodyState state in _bodies)
            {
                if (state.Body == null) continue;
                state.Body.isKinematic = state.IsKinematic;
                state.Body.useGravity = state.UseGravity;
                if (!state.IsKinematic) // a non-kinematic body keeps no leftover motion
                {
                    state.Body.linearVelocity = Vector3.zero;
                    state.Body.angularVelocity = Vector3.zero;
                }
            }

            Physics.simulationMode = _previousMode;
            foreach (Scene scene in dirtyScenes)
            {
                if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            }

            _bodies.Clear();
            _added.Clear();
            _startPoses.Clear();
            Objects = Array.Empty<GameObject>();
        }
    }
}
