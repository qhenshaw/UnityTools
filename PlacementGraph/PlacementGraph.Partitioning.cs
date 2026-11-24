using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityTools.Placement
{
    // Partitioning: with a Cell Size, the volume is split into square cells that each run the graph on their own
    // part of the volume (plus an overlap into their neighbours) and own their own generated objects and instances.
    // Each cell remembers a signature of everything that affects it, so a change regenerates only the cells whose
    // signature changed.
    public partial class PlacementGraph
    {
        /// <summary>A partition cell and what it generated.</summary>
        [Serializable]
        internal class CellRecord
        {
            public Vector2Int Index;
            public int Signature;
            public GameObject Container;
        }

        [SerializeField, HideInInspector] private List<CellRecord> _cells = new List<CellRecord>();

        // Set while a cell runs: where its spawned objects go and which batches its instances join.
        [NonSerialized] private Transform _spawnParent;
        [NonSerialized] private int _currentCell;

        private Transform SpawnParent => _spawnParent != null ? _spawnParent : transform;

        /// <summary>Cell size in metres for partitioning; 0 runs the graph once over the whole volume.</summary>
        public float CellSize
        {
            get => _cellSize;
            set { _cellSize = Mathf.Max(0f, value); MarkDirty(); }
        }

        /// <summary>How far each cell looks into its neighbours, in metres.</summary>
        public float CellOverlap
        {
            get => _cellOverlap;
            set { _cellOverlap = Mathf.Max(0f, value); MarkDirty(); }
        }

        /// <summary>The partition cells that exist, by index, for inspection and tests.</summary>
        public IEnumerable<Vector2Int> CellIndices
        {
            get { foreach (CellRecord cell in _cells) yield return cell.Index; }
        }

        /// <summary>The object that holds a cell's spawned objects, or null.</summary>
        public GameObject CellContainer(Vector2Int index) => _cells.Find(c => c.Index == index)?.Container;

        /// <summary>The cells a regenerate ran last time (all of them after Generate, only changed ones after an edit).</summary>
        public IReadOnlyList<Vector2Int> LastRegeneratedCells => _lastRegenerated;
        private readonly List<Vector2Int> _lastRegenerated = new List<Vector2Int>();

        /// <summary>
        /// Regenerates without Undo, like Auto Generate does: with cells, only the cells whose inputs changed since
        /// they last ran; without, everything.
        /// </summary>
        public void RegenerateChanged() => Regenerate(record: false, force: false);

        internal static int CellId(Vector2Int index) => ((index.x + 16384) << 15 | (index.y + 16384) & 0x7FFF) + 1;

        /// <summary>The part of the volume a cell owns, in local space (full height).</summary>
        private Bounds OwnedBoundsOf(Vector2Int index)
        {
            Bounds volume = LocalBounds;
            float size = ActiveCellSize;
            float minX = Mathf.Max(volume.min.x, index.x * size), maxX = Mathf.Min(volume.max.x, (index.x + 1) * size);
            float minZ = Mathf.Max(volume.min.z, index.y * size), maxZ = Mathf.Min(volume.max.z, (index.y + 1) * size);
            var bounds = new Bounds();
            bounds.SetMinMax(new Vector3(minX, volume.min.y, minZ), new Vector3(maxX, volume.max.y, maxZ));
            return bounds;
        }

        /// <summary>What a cell runs on: its owned part plus the overlap, kept inside the volume.</summary>
        private Bounds RunBoundsOf(Bounds owned)
        {
            Bounds volume = LocalBounds;
            var bounds = new Bounds();
            bounds.SetMinMax(
                new Vector3(Mathf.Max(volume.min.x, owned.min.x - _cellOverlap), volume.min.y, Mathf.Max(volume.min.z, owned.min.z - _cellOverlap)),
                new Vector3(Mathf.Min(volume.max.x, owned.max.x + _cellOverlap), volume.max.y, Mathf.Min(volume.max.z, owned.max.z + _cellOverlap)));
            return bounds;
        }

        private List<Vector2Int> DesiredCells()
        {
            var cells = new List<Vector2Int>();
            Bounds volume = LocalBounds;
            float size = ActiveCellSize;
            int x0 = Mathf.FloorToInt(volume.min.x / size), x1 = Mathf.CeilToInt(volume.max.x / size);
            int z0 = Mathf.FloorToInt(volume.min.z / size), z1 = Mathf.CeilToInt(volume.max.z / size);
            for (int z = z0; z < z1; z++)
            for (int x = x0; x < x1; x++)
            {
                Bounds owned = OwnedBoundsOf(new Vector2Int(x, z));
                if (owned.size.x > 1e-5f && owned.size.z > 1e-5f) cells.Add(new Vector2Int(x, z));
            }
            return cells;
        }

        private void RegeneratePartitioned(bool record, bool force)
        {
#if UNITY_EDITOR
            if (record) Undo.RecordObject(this, "Generate Placement Graph");
#endif
            _lastRegenerated.Clear();
            _lastWarnings.Clear();
            _instanceWarnings.Clear();
            List<Vector2Int> desired = DesiredCells();
            int graphSignature = GraphSignature();

            // Children that aren't inputs or cell containers come from an unpartitioned run.
            var known = new HashSet<GameObject>();
            foreach (CellRecord cell in _cells) if (cell.Container != null) known.Add(cell.Container);
            var stray = new List<GameObject>();
            foreach (Transform child in transform)
            {
                if (!IsInput(child) && !known.Contains(child.gameObject)) stray.Add(child.gameObject);
            }
            foreach (GameObject child in stray) DestroyGenerated(child, record);
            PlacementInstances instances = GetComponent<PlacementInstances>();
            if (instances != null)
            {
#if UNITY_EDITOR
                if (record && !Application.isPlaying) Undo.RecordObject(instances, "Generate Placement Graph");
#endif
                instances.Clear(0);
            }

            for (int i = _cells.Count - 1; i >= 0; i--)
            {
                if (desired.Contains(_cells[i].Index)) continue;
                ClearCell(_cells[i], record);
                _cells.RemoveAt(i);
            }

            var counts = new List<int>();
            foreach (Vector2Int index in desired)
            {
                Bounds owned = OwnedBoundsOf(index);
                Bounds run = RunBoundsOf(owned);
                int signature = CellSignature(graphSignature, owned, run);
                CellRecord cell = _cells.Find(c => c.Index == index);
                if (!force && cell != null && cell.Signature == signature && cell.Container != null) continue;
                if (cell == null)
                {
                    cell = new CellRecord { Index = index };
                    _cells.Add(cell);
                }
                RunCell(cell, owned, run, signature, record);
                for (int n = 0; n < _lastCounts.Count; n++)
                {
                    if (n >= counts.Count) counts.Add(0);
                    counts[n] += _lastCounts[n];
                }
                foreach (string warning in _lastWarnings) if (!_cellWarnings.Contains(warning)) _cellWarnings.Add(warning);
            }

            _cells.Sort((a, b) => a.Index.y != b.Index.y ? a.Index.y.CompareTo(b.Index.y) : a.Index.x.CompareTo(b.Index.x));
            if (_lastRegenerated.Count > 0)
            {
                _lastCounts.Clear();
                _lastCounts.AddRange(counts);
            }
            _lastWarnings.Clear();
            _lastWarnings.AddRange(_cellWarnings);
            _lastWarnings.AddRange(_instanceWarnings);
            _cellWarnings.Clear();
            RecountInstances();
            foreach (string warning in _lastWarnings) Debug.LogWarning($"{name}: {warning}", this);
        }

        private readonly List<string> _cellWarnings = new List<string>();

        /// <summary>Runs the graph for one cell, replacing what it generated before.</summary>
        private void RunCell(CellRecord cell, Bounds owned, Bounds run, int signature, bool record)
        {
            Vector2Int index = cell.Index;
            ClearCell(cell, record);
            cell.Container = new GameObject($"Cell {index.x},{index.y}");
            cell.Container.transform.SetParent(transform, false);
            _spawnParent = cell.Container.transform;
            _currentCell = CellId(index);
            // Objects are spawned without their own Undo entries: registering the container afterwards covers
            // them all, which is far cheaper than one entry per object. Instances still record the component.
            _record = false;
            try
            {
                EvaluateRegion(this, run, owned);
            }
            finally
            {
                _spawnParent = null;
                _currentCell = 0;
            }
#if UNITY_EDITOR
            if (record && !Application.isPlaying) Undo.RegisterCreatedObjectUndo(cell.Container, "Generate Placement Graph");
#endif
            cell.Signature = signature;
            _lastRegenerated.Add(index);
        }

        private void RecountInstances()
        {
            int count = 0;
            foreach (CellRecord cell in _cells)
            {
                if (cell.Container != null) count += cell.Container.transform.childCount;
            }
            PlacementInstances instances = GetComponent<PlacementInstances>();
            if (instances != null) count += instances.InstanceCount;
            InstanceCount = count;
        }

        private void ClearCell(CellRecord cell, bool record)
        {
            if (cell.Container != null) ReleaseToPool(cell.Container);
            if (cell.Container != null) DestroyGenerated(cell.Container, record);
            cell.Container = null;
            PlacementInstances instances = GetComponent<PlacementInstances>();
            if (instances == null) return;
#if UNITY_EDITOR
            if (record && !Application.isPlaying) Undo.RecordObject(instances, "Generate Placement Graph");
#endif
            instances.Clear(CellId(cell.Index));
        }

        private static void DestroyGenerated(GameObject target, bool record)
        {
            DestroyGeneratedMeshes(target, record);
#if UNITY_EDITOR
            if (record && !Application.isPlaying) { Undo.DestroyObjectImmediate(target); return; }
            if (!Application.isPlaying) { DestroyImmediate(target); return; }
#endif
            Destroy(target);
        }

        /// <summary>A hash of everything about the graph and component that affects every cell.</summary>
        private int GraphSignature()
        {
            unchecked
            {
                int hash = 17;
                if (_graph != null)
                {
                    foreach (PlacementNode node in _graph.Nodes) hash = hash * 31 + (node == null ? 0 : JsonUtility.ToJson(node).GetHashCode());
                    foreach (PlacementEdge edge in _graph.Edges) hash = hash * 31 + edge.GetHashCode();
                    hash = hash * 31 + _graph.GetEntityId().GetHashCode();
                }
                hash = hash * 31 + _seed;
                hash = hash * 31 + transform.position.GetHashCode();
                hash = hash * 31 + transform.rotation.GetHashCode();
                hash = hash * 31 + _cellSize.GetHashCode();
                hash = hash * 31 + _cellOverlap.GetHashCode();
                foreach (PlacementParameterOverride entry in _parameters)
                {
                    if (entry?.Value == null || !entry.Override) continue;
                    hash = hash * 31 + entry.Value.Name.GetHashCode();
                    hash = hash * 31 + (entry.Value.Value?.GetHashCode() ?? 0);
                }
                return hash;
            }
        }

        /// <summary>
        /// A hash of what affects one cell: the graph and settings, the cell's bounds (they change when the volume
        /// grows or shrinks across it), and the markers and spline inside the part of the volume it runs on.
        /// </summary>
        private int CellSignature(int graphSignature, Bounds owned, Bounds run)
        {
            unchecked
            {
                int hash = graphSignature;
                hash = hash * 31 + owned.GetHashCode();
                hash = hash * 31 + run.GetHashCode();
                Matrix4x4 worldToLocal = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse;
                if (_markers != null)
                {
                    foreach (Transform marker in _markers)
                    {
                        if (!marker.gameObject.activeSelf) continue;
                        Vector3 local = worldToLocal.MultiplyPoint3x4(marker.position);
                        if (local.x < run.min.x || local.x > run.max.x || local.z < run.min.z || local.z > run.max.z) continue;
                        hash = hash * 31 + marker.position.GetHashCode();
                        hash = hash * 31 + marker.rotation.GetHashCode();
                        hash = hash * 31 + marker.lossyScale.GetHashCode();
                    }
                }
#if UNITYTOOLS_SPLINES
                if (_spline != null && SplineTouches(worldToLocal, run))
                {
                    hash = hash * 31 + _spline.transform.localToWorldMatrix.GetHashCode();
                    foreach (UnityEngine.Splines.Spline spline in _spline.Splines)
                    {
                        hash = hash * 31 + (spline.Closed ? 1 : 0);
                        foreach (UnityEngine.Splines.BezierKnot knot in spline.Knots) hash = hash * 31 + knot.GetHashCode();
                    }
                }
#endif
                hash = hash * 31 + SceneInputSignature(worldToLocal, run);
                return hash;
            }
        }

        /// <summary>
        /// A hash of the scene objects the graph's Scene Input nodes read inside <paramref name="region"/> (local
        /// space): which ones match, and where and how big they are. A zone moving into, out of or within a region
        /// changes it.
        /// </summary>
        private int SceneInputSignature(Matrix4x4 worldToLocal, Bounds region)
        {
            if (_graph == null) return 0;
            unchecked
            {
                int hash = 0;
                PlacementContext context = null;
                foreach (PlacementNode node in _graph.Nodes)
                {
                    if (node is not SceneInputNode sceneInput) continue;
                    context ??= new PlacementContext
                    {
                        LocalToWorld = worldToLocal.inverse,
                        LocalBounds = region,
                        Scene = gameObject.scene
                    };
                    foreach (SceneInputNode.Match match in sceneInput.FindMatches(context))
                    {
                        hash = hash * 31 + match.Key.GetHashCode();
                        hash = hash * 31 + (match.Zone != null ? match.Zone.StateHash : match.GameObject.transform.localToWorldMatrix.GetHashCode());
                    }
                }
                return hash;
            }
        }

#if UNITYTOOLS_SPLINES
        private bool SplineTouches(Matrix4x4 worldToLocal, Bounds run)
        {
            foreach (UnityEngine.Splines.Spline spline in _spline.Splines)
            {
                Bounds world = UnityEngine.Splines.SplineUtility.GetBounds(spline, _spline.transform.localToWorldMatrix);
                Vector3 min = world.min, max = world.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3((corner & 1) == 0 ? min.x : max.x, (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z);
                    Vector3 local = worldToLocal.MultiplyPoint3x4(point);
                    if (local.x >= run.min.x && local.x <= run.max.x && local.z >= run.min.z && local.z <= run.max.z) return true;
                }
                // A spline bigger than the cell, with no corner inside it, still touches it if the boxes overlap.
                Vector3 a = worldToLocal.MultiplyPoint3x4(min), b = worldToLocal.MultiplyPoint3x4(max);
                if (Mathf.Min(a.x, b.x) <= run.max.x && Mathf.Max(a.x, b.x) >= run.min.x &&
                    Mathf.Min(a.z, b.z) <= run.max.z && Mathf.Max(a.z, b.z) >= run.min.z) return true;
            }
            return false;
        }
#endif
    }
}
