using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityTools.Placement
{
    /// <summary>
    /// Runs a placement graph inside a box volume and keeps the result as its children. Its children are generated:
    /// Generate and Clear replace all of them, and with Auto Generate on, any change regenerates them.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("UnityTools/Placement Graph")]
    public partial class PlacementGraph : MonoBehaviour, IPlacementOutput, IInstanceOutput, IMeshOutput
    {
        [Tooltip("The compiled graph: a .placegraph asset.")]
        [SerializeField] private PlacementGraphAsset _graph;
        [SerializeField] private int _seed;
        [Tooltip("Regenerate in edit mode whenever the graph, the settings or the transform change.")]
        [SerializeField] private bool _autoGenerate = true;
        [Tooltip("Size of the generation volume, centred on this object.")]
        [SerializeField] private Vector3 _size = new Vector3(20f, 10f, 20f);
        [Tooltip("0: run the graph once for the whole volume. Above 0: split the volume into square cells of this size " +
                 "(metres) that run separately, so edits regenerate only the cells they touch.")]
        [SerializeField, Min(0f)] private float _cellSize;
        [Tooltip("With cells: how far each cell also looks into its neighbours, so filters like Self Pruning and " +
                 "Difference see points across the border. Use at least their largest radius.")]
        [SerializeField, Min(0f)] private float _cellOverlap = 2f;
        [Tooltip("Optional. The active children of this Transform are the points of Child Markers nodes. If it is a " +
                 "child of this object it is kept when the graph regenerates.")]
        [SerializeField] private Transform _markers;
#if UNITYTOOLS_SPLINES
        [Tooltip("Optional. The spline Spline Sampler nodes read. If it is a child of this object it is kept when the " +
                 "graph regenerates.")]
        [SerializeField] private UnityEngine.Splines.SplineContainer _spline;
#endif
        [Tooltip("Index of the node whose points are drawn in the Scene view while selected. -1 draws none.")]
        [SerializeField] private int _debugNode = -1;
        [Tooltip("Per-instance values for the graph's parameters (its Input variables).")]
        [SerializeField] private List<PlacementParameterOverride> _parameters = new List<PlacementParameterOverride>();

        [field: SerializeField] public int InstanceCount { get; private set; }

        private readonly List<PointData[]> _lastOutputs = new List<PointData[]>();
        private readonly List<int> _lastCounts = new List<int>();
        private readonly List<double> _lastMilliseconds = new List<double>();

        /// <summary>How long each node took on the last run, in milliseconds, by node index (the last cell, when partitioned).</summary>
        public IReadOnlyList<double> LastNodeMilliseconds => _lastMilliseconds;
        private readonly List<string> _lastWarnings = new List<string>();
        private bool _record;

        /// <summary>The compiled graph this component runs.</summary>
        public PlacementGraphAsset Graph
        {
            get => _graph;
            set { _graph = value; MarkDirty(); }
        }

        /// <summary>Seed for every random choice the graph makes. The same seed gives the same result.</summary>
        public int Seed
        {
            get => _seed;
            set { _seed = value; MarkDirty(); }
        }

        /// <summary>The Transform whose active children are the points of Child Markers nodes. Optional.</summary>
        public Transform Markers
        {
            get => _markers;
            set { _markers = value; MarkDirty(); }
        }

#if UNITYTOOLS_SPLINES
        /// <summary>The spline Spline Sampler nodes read. Optional.</summary>
        public UnityEngine.Splines.SplineContainer Spline
        {
            get => _spline;
            set { _spline = value; MarkDirty(); }
        }
#endif

        /// <summary>Size of the generation volume in local space.</summary>
        public Vector3 Size
        {
            get => _size;
            set { _size = ClampSize(value); MarkDirty(); }
        }

        /// <summary>The generation volume in local space.</summary>
        public Bounds LocalBounds => new Bounds(Vector3.zero, _size);

        /// <summary>The node drawn in the Scene view, or -1.</summary>
        public int DebugNode { get => _debugNode; set => _debugNode = value; }

        /// <summary>Points on each node's first output from the last run, by node index.</summary>
        public IReadOnlyList<int> LastNodeCounts => _lastCounts;

        /// <summary>Every node's outputs from the last run, by node index, for the debug view.</summary>
        public IReadOnlyList<PointData[]> LastOutputs => _lastOutputs;

        /// <summary>Warnings from the last run.</summary>
        public IReadOnlyList<string> LastWarnings => _lastWarnings;

        /// <summary>One entry per graph parameter, in the graph's order. Call <see cref="SyncParameters"/> after changing the graph.</summary>
        public IReadOnlyList<PlacementParameterOverride> ParameterOverrides => _parameters;

        /// <summary>
        /// Makes the override list match the graph's parameters: adds new ones, drops removed ones and keeps the
        /// values of parameters that still exist with the same type. Returns true if the list changed.
        /// </summary>
        public bool SyncParameters()
        {
            IReadOnlyList<PlacementParameter> declared = _graph != null ? _graph.Parameters : Array.Empty<PlacementParameter>();
            bool inSync = declared.Count == _parameters.Count;
            for (int i = 0; inSync && i < declared.Count; i++)
            {
                PlacementParameter value = _parameters[i]?.Value;
                inSync = value != null && declared[i] != null && value.Name == declared[i].Name && value.Type == declared[i].Type;
            }
            if (inSync) return false;

            var synced = new List<PlacementParameterOverride>(declared.Count);
            foreach (PlacementParameter parameter in declared)
            {
                if (parameter == null) continue;
                PlacementParameterOverride existing = _parameters.Find(p =>
                    p?.Value != null && p.Value.Name == parameter.Name && p.Value.Type == parameter.Type);
                synced.Add(existing ?? new PlacementParameterOverride { Override = false, Value = parameter.Clone() });
            }
            _parameters = synced;
            return true;
        }

        /// <summary>Overrides the parameter called <paramref name="name"/> with <paramref name="value"/>. Throws if the graph has no such parameter.</summary>
        public void SetParameter(string name, object value)
        {
            SyncParameters();
            PlacementParameterOverride entry = _parameters.Find(p => p.Value.Name == name)
                ?? throw new ArgumentException($"The graph has no parameter called '{name}'.", nameof(name));
            entry.Value.Value = value;
            entry.Override = true;
            MarkDirty();
        }

        /// <summary>Stops overriding the parameter called <paramref name="name"/>, so the graph's default is used.</summary>
        public void ClearParameter(string name)
        {
            PlacementParameterOverride entry = _parameters.Find(p => p?.Value != null && p.Value.Name == name);
            if (entry == null) return;
            entry.Override = false;
            MarkDirty();
        }

        private static Vector3 ClampSize(Vector3 size) =>
            new Vector3(Mathf.Max(0.1f, size.x), Mathf.Max(0.1f, size.y), Mathf.Max(0.1f, size.z));

        /// <summary>Runs the graph without touching the scene and returns every node's outputs, by node index.</summary>
        public PointData[][] Evaluate(IPlacementOutput output = null) => EvaluateRegion(output, LocalBounds, null);

        /// <summary>Runs the graph over <paramref name="bounds"/>; with <paramref name="owned"/> set, it's one partition cell.</summary>
        private PointData[][] EvaluateRegion(IPlacementOutput output, Bounds bounds, Bounds? owned)
        {
            if (_graph == null) return Array.Empty<PointData[]>();
            var context = new PlacementContext
            {
                LocalToWorld = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one),
                LocalBounds = bounds,
                OwnedBounds = owned,
                AnchorGridToOrigin = owned.HasValue,
                Scene = gameObject.scene,
                Seed = _seed,
                Output = output,
                Instances = output != null ? this : null,
                Meshes = output != null ? this : null,
                NodeOutputs = _lastOutputs
            };
            if (_markers != null)
            {
                foreach (Transform marker in _markers)
                {
                    if (marker.gameObject.activeSelf) context.Markers.Add(marker);
                }
            }
#if UNITYTOOLS_SPLINES
            context.Spline = _spline;
#endif
            foreach (PlacementParameterOverride entry in _parameters)
            {
                if (entry != null && entry.Override && entry.Value != null) context.ParameterOverrides.Add(entry.Value);
            }
            PointData[][] outputs = PlacementExecutor.Run(_graph, context);
            _lastCounts.Clear();
            _lastCounts.AddRange(context.NodeOutputCounts);
            _lastMilliseconds.Clear();
            _lastMilliseconds.AddRange(context.NodeMilliseconds);
            _lastWarnings.Clear();
            _lastWarnings.AddRange(context.Warnings);
            return outputs;
        }

        void IPlacementOutput.Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            GameObject instance;
#if UNITY_EDITOR
            if (!IsRuntime)
            {
                // Prefab assets keep their prefab connection; a part inside a prefab (from Assembly) keeps the link to
                // its own prefab when it is a nested prefab, and is copied otherwise, as is any scene object.
                GameObject source = prefab;
                bool linked = PrefabUtility.IsPartOfPrefabAsset(prefab);
                if (linked && prefab.transform.parent != null)
                {
                    source = PrefabUtility.IsAnyPrefabInstanceRoot(prefab) ? PrefabUtility.GetCorrespondingObjectFromSource(prefab) : null;
                    linked = source != null;
                    if (!linked) source = prefab;
                }
                instance = linked
                    ? PrefabUtility.InstantiatePrefab(source, SpawnParent) as GameObject
                    : Instantiate(source, SpawnParent);
                if (instance == null) return;
                if (_record) Undo.RegisterCreatedObjectUndo(instance, "Generate Placement Graph");
            }
            else
#endif
            {
                instance = SpawnRuntime(prefab);
            }
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.localScale = scale;
            if (_spawnParent == null) FlagGenerated(instance);
            InstanceCount++;
        }

        private readonly List<string> _instanceWarnings = new List<string>();

        /// <summary>The instanced meshes from Spawn Instanced nodes, kept on a companion component. Null until there are some.</summary>
        public PlacementInstances Instances => GetComponent<PlacementInstances>();

        private PlacementInstances GetOrAddInstances()
        {
            PlacementInstances instances = GetComponent<PlacementInstances>();
            if (instances != null) return instances;
#if UNITY_EDITOR
            if (_record && !Application.isPlaying) return Undo.AddComponent<PlacementInstances>(gameObject);
#endif
            return gameObject.AddComponent<PlacementInstances>();
        }

        void IInstanceOutput.AddInstance(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale, InstanceDrawSettings settings)
        {
            PlacementInstances instances = GetOrAddInstances();
            instances.hideFlags = GeneratedFlags;
            string warning = instances.Add(prefab, Matrix4x4.TRS(position, rotation, scale), settings, _currentCell);
            if (warning != null) _instanceWarnings.Add($"Spawn Instanced: {warning}");
            InstanceCount++;
        }

        void IMeshOutput.AddMesh(Mesh mesh, Material material, UnityEngine.Rendering.ShadowCastingMode shadows)
        {
            var holder = new GameObject("Spline Mesh");
            holder.transform.SetParent(SpawnParent, false);
            FlagGenerated(holder);
            holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = holder.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows;
#if UNITY_EDITOR
            if (_record && !Application.isPlaying) Undo.RegisterCreatedObjectUndo(holder, "Generate Placement Graph");
#endif
            InstanceCount++;
        }

        /// <summary>
        /// Destroys the meshes Spawn Spline Mesh generated under <paramref name="root"/>. Skipped when the destroy is
        /// recorded for Undo, which needs the meshes to bring the objects back.
        /// </summary>
        private static void DestroyGeneratedMeshes(GameObject root, bool record)
        {
            if (record) return;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || mesh.name != SpawnSplineMeshNode.GeneratedMeshName) continue;
#if UNITY_EDITOR
                if (AssetDatabase.Contains(mesh)) continue;
                if (!Application.isPlaying) { DestroyImmediate(mesh); continue; }
#endif
                Destroy(mesh);
            }
        }

        private void ClearInstances(bool record)
        {
            PlacementInstances instances = GetComponent<PlacementInstances>();
            if (instances == null) return;
#if UNITY_EDITOR
            if (record && !Application.isPlaying) Undo.RecordObject(instances, "Clear Placement Graph");
#endif
            instances.Clear();
        }

        /// <summary>Destroys every child (they're all generated) and runs the graph again. One Undo step in the editor.</summary>
        public void Generate()
        {
#if UNITY_EDITOR
            if (!CanRegenerate()) return;
            Undo.SetCurrentGroupName("Generate Placement Graph");
            Regenerate(record: true, force: true);
#else
            Regenerate(record: false, force: true);
#endif
        }

        /// <summary>Destroys every child (they're all generated). One Undo step in the editor.</summary>
        public void Clear()
        {
#if UNITY_EDITOR
            if (!CanRegenerate()) return;
            Undo.SetCurrentGroupName("Clear Placement Graph");
            Undo.RecordObject(this, "Clear Placement Graph");
            ClearChildren(record: true);
            ClearInstances(record: true);
#else
            ClearChildren(record: false);
            ClearInstances(record: false);
#endif
            _cells.Clear();
            InstanceCount = 0;
        }

        private void Regenerate(bool record, bool force)
        {
            if (ActiveCellSize > 0f)
            {
                RegeneratePartitioned(record, force);
                return;
            }
            _cells.Clear();
#if UNITY_EDITOR
            if (record) Undo.RecordObject(this, "Generate Placement Graph");
#endif
            ClearChildren(record);
            ClearInstances(record);
            InstanceCount = 0;
            _record = record;
            _instanceWarnings.Clear();
            try
            {
                Evaluate(this);
            }
            finally
            {
                _record = false;
            }
            _lastWarnings.AddRange(_instanceWarnings);
            foreach (string warning in _lastWarnings) Debug.LogWarning($"{name}: {warning}", this);
        }

        private void ClearChildren(bool record)
        {
            var children = new List<GameObject>();
            foreach (Transform child in transform)
            {
                if (IsInput(child)) continue;
                children.Add(child.gameObject);
            }
            foreach (GameObject child in children)
            {
                DestroyGeneratedMeshes(child, record);
#if UNITY_EDITOR
                if (record && !Application.isPlaying) { Undo.DestroyObjectImmediate(child); continue; }
                if (!Application.isPlaying) { DestroyImmediate(child); continue; }
#endif
                Destroy(child);
            }
        }

        /// <summary>True for a child that holds the graph's inputs (the Markers or Spline object), which regenerating keeps.</summary>
        private bool IsInput(Transform child)
        {
            if (_markers != null && child == _markers) return true;
            if (_poolRoot != null && child == _poolRoot) return true;
#if UNITYTOOLS_SPLINES
            if (_spline != null && child == _spline.transform) return true;
#endif
            return false;
        }

        private void MarkDirty()
        {
            _runtimeDirty = true;
#if UNITY_EDITOR
            _isDirty = true;
            _dirtySince = EditorApplication.timeSinceStartup;
#endif
        }

#if UNITY_EDITOR
        private bool _isDirty;
        private double _dirtySince;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        [NonSerialized] private bool _warnedAboutPrefab;

        /// <summary>Seconds a change has to settle before Auto Generate reruns the graph, so dragging stays smooth.</summary>
        private const double SettleTime = 0.15;

        /// <summary>
        /// True when this component is part of a prefab instance in a scene. Its children belong to the prefab there
        /// and can't be destroyed, so it doesn't regenerate.
        /// </summary>
        public bool IsInPrefabInstance => PrefabUtility.IsPartOfPrefabInstance(gameObject);

        private bool CanRegenerate()
        {
            if (!IsInPrefabInstance) return true;
            if (!_warnedAboutPrefab)
            {
                _warnedAboutPrefab = true;
                Debug.LogWarning($"{name} is inside a prefab instance, so it can't regenerate its children here. " +
                                 "Edit it in prefab mode, or unpack the prefab instance.", this);
            }
            return false;
        }

        private void OnValidate()
        {
            _size = ClampSize(_size);
            SyncParameters();
            _isDirty = true;
            _dirtySince = EditorApplication.timeSinceStartup;
        }

        private void OnEnable()
        {
            _previousPosition = transform.position;
            _previousRotation = transform.rotation;
            _previousInputsHash = InputsHash();
            EditorApplication.update += EditorUpdate;
            // Generated objects that weren't saved with the scene are rebuilt when it opens.
            if (!_saveGenerated && !Application.isPlaying && !HasGenerated()) MarkDirty();
        }

        private void OnDisable()
        {
            EditorApplication.update -= EditorUpdate;
        }

        /// <summary>
        /// Marks every enabled component that runs <paramref name="graph"/> to regenerate, e.g. after a reimport.
        /// Null marks every component.
        /// </summary>
        internal static void MarkUsersDirty(PlacementGraphAsset graph)
        {
            string path = graph != null ? AssetDatabase.GetAssetPath(graph) : null;
            foreach (PlacementGraph user in FindObjectsByType<PlacementGraph>(FindObjectsInactive.Exclude))
            {
                if (graph == null || user._graph == graph ||
                    (user._graph != null && AssetDatabase.GetAssetPath(user._graph) == path))
                {
                    user._isDirty = true;
                    user._dirtySince = EditorApplication.timeSinceStartup;
                }
            }
        }

        private void EditorUpdate()
        {
            if (this == null || Application.isPlaying || !_autoGenerate) return;

            if (transform.position != _previousPosition || transform.rotation != _previousRotation)
            {
                _previousPosition = transform.position;
                _previousRotation = transform.rotation;
                _isDirty = true;
                _dirtySince = EditorApplication.timeSinceStartup;
            }

            int inputsHash = InputsHash();
            if (inputsHash != _previousInputsHash)
            {
                _previousInputsHash = inputsHash;
                _isDirty = true;
                _dirtySince = EditorApplication.timeSinceStartup;
            }

            if (!_isDirty || EditorApplication.timeSinceStartup - _dirtySince < SettleTime) return;
            _isDirty = false;
            if (_graph == null || !CanRegenerate()) return;

            // Automatic runs aren't recorded: undoing the change that caused one marks this dirty and it reruns to match.
            // Partitioned graphs regenerate only the cells the change touched.
            Regenerate(record: false, force: false);
            SceneView.RepaintAll();
        }

        private int _previousInputsHash;

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, _size);
            Gizmos.matrix = Matrix4x4.identity;

            if (_debugNode < 0 || _debugNode >= _lastOutputs.Count || _lastOutputs[_debugNode] == null) return;
            PointData[] outputs = _lastOutputs[_debugNode];
            if (outputs.Length == 0) return;
            float size = Mathf.Clamp(Mathf.Min(_size.x, _size.z) / 200f, 0.05f, 0.5f);
            foreach (PlacementPoint point in outputs[0].Points)
            {
                Gizmos.color = Color.Lerp(Color.red, Color.green, point.Density);
                Gizmos.DrawCube(point.Position, Vector3.one * size);
                Gizmos.DrawLine(point.Position, point.Position + point.Normal * size * 4f);
            }
        }

        /// <summary>Creates an empty Placement Graph at the Scene view's pivot, as one Undo step.</summary>
        [MenuItem("GameObject/Placement Graph", false, 0)]
        private static void CreateFromMenu(MenuCommand command)
        {
            var go = new GameObject("Placement Graph");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            if (command.context == null && SceneView.lastActiveSceneView != null)
                go.transform.position = SceneView.lastActiveSceneView.pivot;
            go.AddComponent<PlacementGraph>();
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeGameObject = go;
        }
#endif
    }
}
