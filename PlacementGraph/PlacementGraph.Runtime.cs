using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Placement
{
    // Runtime generation: Generate On picks whether the graph also runs in builds and Play mode, once when the scene
    // loads or cell by cell around Generation Sources, within a time budget per frame, reusing spawned objects through a
    // per-prefab pool. Nothing here depends on UnityEditor or Graph Toolkit: it runs the compiled PlacementGraphAsset.
    public partial class PlacementGraph
    {
        public enum GenerateMode
        {
            /// <summary>Generate in the editor only; builds keep what was generated and saved with the scene.</summary>
            EditorOnly,
            /// <summary>Also generate at runtime when the scene loads, if nothing was saved with it, and again when a parameter changes.</summary>
            OnLoad,
            /// <summary>Generate cells near Generation Sources at runtime and unload far ones, spread over frames.</summary>
            NearPlayer
        }

        /// <summary>The cell size Near Player uses when Cell Size is 0.</summary>
        public const float DefaultRuntimeCellSize = 32f;

        [Header("Runtime")]
        [Tooltip("Editor Only: generated in the editor and saved with the scene. On Load: also generated when the scene " +
                 "loads (if nothing was saved) and when a parameter changes. Near Player: cells around Generation Sources " +
                 "(or the main camera) are generated as they come near and unloaded when far.")]
        [SerializeField] private GenerateMode _generateOn = GenerateMode.EditorOnly;
        [Tooltip("Near Player: cells whose centre is within this distance of a source are generated.")]
        [SerializeField, Min(0f)] private float _loadRadius = 64f;
        [Tooltip("Near Player: cells whose centre is further than this from every source are unloaded. Keep it above Load Radius.")]
        [SerializeField, Min(0f)] private float _unloadRadius = 96f;
        [Tooltip("Near Player: milliseconds of generation per frame. At least one cell runs each frame there is work.")]
        [SerializeField, Min(0f)] private float _frameBudgetMs = 2f;
        [Tooltip("At runtime, keep the objects of unloaded cells and reuse them for new cells instead of destroying and creating them.")]
        [SerializeField] private bool _pooling = true;

        [NonSerialized] private bool _runtimeDirty;
        [NonSerialized] private bool _simulateRuntime;
        [NonSerialized] private int _cachedGraphSignature;
        [NonSerialized] private bool _graphSignatureValid;
        [NonSerialized] private Transform _poolRoot;
        private readonly Dictionary<GameObject, Stack<GameObject>> _pool = new Dictionary<GameObject, Stack<GameObject>>();
        private readonly Dictionary<GameObject, GameObject> _instanceSources = new Dictionary<GameObject, GameObject>();
        private readonly HashSet<Vector2Int> _staleCells = new HashSet<Vector2Int>();

        public GenerateMode GenerateOn { get => _generateOn; set => _generateOn = value; }
        public float LoadRadius { get => _loadRadius; set => _loadRadius = Mathf.Max(0f, value); }
        public float UnloadRadius { get => _unloadRadius; set => _unloadRadius = Mathf.Max(0f, value); }
        public float FrameBudgetMs { get => _frameBudgetMs; set => _frameBudgetMs = Mathf.Max(0f, value); }
        public bool Pooling { get => _pooling; set => _pooling = value; }

        /// <summary>Objects waiting in the pool, over all prefabs.</summary>
        public int PooledCount
        {
            get
            {
                int count = 0;
                foreach (Stack<GameObject> stack in _pool.Values) count += stack.Count;
                return count;
            }
        }

        /// <summary>True in Play mode and builds (and in tests that simulate runtime).</summary>
        private bool IsRuntime => Application.isPlaying || _simulateRuntime;

        /// <summary>The cell size in use: Cell Size, or for Near Player at runtime the default when Cell Size is 0.</summary>
        private float ActiveCellSize =>
            _cellSize > 0f ? _cellSize : _generateOn == GenerateMode.NearPlayer && IsRuntime ? DefaultRuntimeCellSize : 0f;

        /// <summary>For tests: behave as at runtime (pooling, Near Player cells) in edit mode.</summary>
        internal void SimulateRuntime(bool on) => _simulateRuntime = on;

        private void Start()
        {
            if (!Application.isPlaying) return;
            if (_generateOn == GenerateMode.OnLoad && InstanceCount == 0) Regenerate(record: false, force: false);
            _runtimeDirty = false;
        }

        private void Update()
        {
            if (!Application.isPlaying) return;
            switch (_generateOn)
            {
                case GenerateMode.OnLoad when _runtimeDirty:
                    _runtimeDirty = false;
                    _graphSignatureValid = false;
                    Regenerate(record: false, force: false);
                    break;
                case GenerateMode.NearPlayer:
                    RuntimeTick(_frameBudgetMs, GenerationSource.Positions());
                    break;
            }
        }

        /// <summary>
        /// One frame of Near Player work: unloads cells beyond Unload Radius from every source, then generates the
        /// missing (or out of date) cells within Load Radius, nearest first, until <paramref name="budgetMs"/> is
        /// used (at least one cell). Returns the number of cells generated.
        /// </summary>
        public int RuntimeTick(float budgetMs, IReadOnlyList<Vector3> sources)
        {
            _lastRegenerated.Clear();
            if (_graph == null || ActiveCellSize <= 0f) return 0;
            if (_runtimeDirty || !_graphSignatureValid)
            {
                _cachedGraphSignature = GraphSignature();
                _graphSignatureValid = true;
            }
            // After a change every loaded cell is checked again, over as many frames as the budget needs.
            if (_runtimeDirty) foreach (CellRecord cell in _cells) _staleCells.Add(cell.Index);
            _runtimeDirty = false;

            Matrix4x4 localToWorld = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            float Nearest(Vector2Int index)
            {
                Vector3 centre = localToWorld.MultiplyPoint3x4(OwnedBoundsOf(index).center);
                float best = float.PositiveInfinity;
                foreach (Vector3 source in sources)
                {
                    var flat = new Vector2(source.x - centre.x, source.z - centre.z);
                    best = Mathf.Min(best, flat.magnitude);
                }
                return best;
            }

            // Unload what's far from every source.
            for (int i = _cells.Count - 1; i >= 0; i--)
            {
                if (Nearest(_cells[i].Index) <= _unloadRadius) continue;
                ClearCell(_cells[i], record: false);
                _staleCells.Remove(_cells[i].Index);
                _cells.RemoveAt(i);
            }

            // Cells near the sources that are missing, or out of date after a change, nearest first.
            var candidates = new List<(Vector2Int index, float distance)>();
            var seen = new HashSet<Vector2Int>();
            Matrix4x4 worldToLocal = localToWorld.inverse;
            float size = ActiveCellSize;
            foreach (Vector3 source in sources)
            {
                Vector3 local = worldToLocal.MultiplyPoint3x4(source);
                int x0 = Mathf.FloorToInt((local.x - _loadRadius) / size), x1 = Mathf.FloorToInt((local.x + _loadRadius) / size);
                int z0 = Mathf.FloorToInt((local.z - _loadRadius) / size), z1 = Mathf.FloorToInt((local.z + _loadRadius) / size);
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    var index = new Vector2Int(x, z);
                    if (!seen.Add(index)) continue;
                    Bounds owned = OwnedBoundsOf(index);
                    if (owned.size.x <= 1e-5f || owned.size.z <= 1e-5f) continue;
                    float distance = Nearest(index);
                    if (distance > _loadRadius) continue;
                    CellRecord existing = _cells.Find(c => c.Index == index);
                    if (existing != null && existing.Container != null && !_staleCells.Contains(index)) continue;
                    candidates.Add((index, distance));
                }
            }
            candidates.Sort((a, b) => a.distance != b.distance ? a.distance.CompareTo(b.distance) :
                a.index.y != b.index.y ? a.index.y.CompareTo(b.index.y) : a.index.x.CompareTo(b.index.x));

            var watch = System.Diagnostics.Stopwatch.StartNew();
            int generated = 0;
            foreach ((Vector2Int index, _) in candidates)
            {
                if (generated > 0 && watch.Elapsed.TotalMilliseconds >= budgetMs) break;
                Bounds owned = OwnedBoundsOf(index);
                Bounds run = RunBoundsOf(owned);
                int signature = CellSignature(_cachedGraphSignature, owned, run);
                CellRecord cell = _cells.Find(c => c.Index == index);
                _staleCells.Remove(index);
                if (cell != null && cell.Container != null && cell.Signature == signature) continue;
                if (cell == null)
                {
                    cell = new CellRecord { Index = index };
                    _cells.Add(cell);
                }
                RunCell(cell, owned, run, signature, record: false);
                generated++;
            }
            if (generated > 0 || candidates.Count > 0) RecountInstances();
            return generated;
        }

        /// <summary>A spawned copy of <paramref name="prefab"/> at runtime: from the pool when there's one waiting.</summary>
        private GameObject SpawnRuntime(GameObject prefab)
        {
            GameObject instance = null;
            if (_pooling && _pool.TryGetValue(prefab, out Stack<GameObject> stack))
            {
                while (stack.Count > 0 && instance == null) instance = stack.Pop();
            }
            if (instance != null)
            {
                instance.transform.SetParent(SpawnParent, false);
                instance.SetActive(true);
            }
            else
            {
                instance = Instantiate(prefab, SpawnParent);
            }
            if (_pooling) _instanceSources[instance] = prefab;
            return instance;
        }

        /// <summary>At runtime with pooling, moves the spawned objects under <paramref name="container"/> into the pool.</summary>
        private void ReleaseToPool(GameObject container)
        {
            if (!IsRuntime || !_pooling || _instanceSources.Count == 0) return;
            if (_poolRoot == null)
            {
                _poolRoot = new GameObject("Pool").transform;
                _poolRoot.SetParent(transform, false);
                _poolRoot.gameObject.SetActive(false);
            }
            var children = new List<Transform>();
            foreach (Transform child in container.transform) children.Add(child);
            foreach (Transform child in children)
            {
                if (!_instanceSources.TryGetValue(child.gameObject, out GameObject prefab)) continue;
                child.gameObject.SetActive(false);
                child.SetParent(_poolRoot, false);
                if (!_pool.TryGetValue(prefab, out Stack<GameObject> stack)) _pool[prefab] = stack = new Stack<GameObject>();
                stack.Push(child.gameObject);
            }
        }
    }
}
