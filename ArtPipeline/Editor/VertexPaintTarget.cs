using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ArtPipeline.Editor
{
    // Cached paint data for the mesh being painted, so the brush costs scale with the brush size instead of the mesh size:
    // - world-space vertex positions, rebuilt only when the mesh or its transform changes
    // - a uniform spatial grid over those positions, so a brush query only visits nearby vertices
    // - a world-space copy of the mesh on a MeshCollider in a hidden preview physics scene, so raycasts use PhysX's BVH
    internal sealed class VertexPaintTarget : IDisposable
    {
        public VertexPainter Painter { get; private set; }
        public Mesh Mesh { get; private set; }
        public Vector3[] WorldPositions => _worldPositions;
        public List<Vector3> LocalNormals => _localNormals;
        public Matrix4x4 NormalToWorld { get; private set; }
        public int Version { get; private set; }

        private Matrix4x4 _localToWorld;
        private readonly List<Vector3> _localPositions = new List<Vector3>();
        private readonly List<Vector3> _localNormals = new List<Vector3>();
        private readonly List<Vector3> _compareBuffer = new List<Vector3>();
        private Vector3[] _worldPositions = Array.Empty<Vector3>();
        private bool _verifyGeometry;

        // collider
        private Scene _previewScene;
        private GameObject _colliderObject;
        private MeshCollider _collider;
        private Mesh _colliderMesh;
        private readonly List<int> _triangles = new List<int>();
        private readonly List<int> _submeshIndices = new List<int>();

        // spatial grid (counting sort: vertex indices grouped by cell)
        private float _cellSize;
        private readonly Dictionary<Vector3Int, int> _cellLookup = new Dictionary<Vector3Int, int>();
        private int[] _cellStart = Array.Empty<int>();
        private int[] _cellCount = Array.Empty<int>();
        private int[] _sortedIndices = Array.Empty<int>();
        private int[] _vertexCell = Array.Empty<int>();

        // Makes sure the cached data matches this painter and mesh, rebuilding only what is out of date.
        public void Sync(VertexPainter painter, Mesh mesh)
        {
            Matrix4x4 localToWorld = painter.transform.localToWorldMatrix;
            bool sameObject = painter == Painter && Mesh != null && mesh.vertexCount == _localPositions.Count;

            if (sameObject && mesh != Mesh)
            {
                // a copy-on-write copy or a swapped mesh: keep the cache if the vertices are identical
                _verifyGeometry = true;
                Mesh = mesh;
            }

            if (sameObject && _verifyGeometry)
            {
                _verifyGeometry = false;
                mesh.GetVertices(_compareBuffer);
                if (!SamePositions(_compareBuffer, _localPositions)) sameObject = false;
            }

            if (!sameObject)
            {
                Rebuild(painter, mesh, localToWorld);
                return;
            }

            if (localToWorld != _localToWorld) RebuildWorldSpace(localToWorld);
        }

        // Undo/redo can change vertices in place; check them on the next Sync.
        public void VerifyGeometryOnNextSync() => _verifyGeometry = true;

        public void Invalidate() => Mesh = null;

        public bool Raycast(Ray ray, out RaycastHit hit)
        {
            hit = default;
            if (_collider == null || _colliderMesh == null) return false;
            return _previewScene.GetPhysicsScene().Raycast(ray.origin, ray.direction, out hit, float.PositiveInfinity, ~0, QueryTriggerInteraction.Collide);
        }

        // Adds every vertex within radius of center (world space) to indices, with its distance.
        public void Query(Vector3 center, float radius, List<int> indices, List<float> distances)
        {
            indices.Clear();
            distances.Clear();
            if (_worldPositions.Length == 0 || radius <= 0f) return;
            EnsureGrid(radius);

            float sqrRadius = radius * radius;
            Vector3Int min = Cell(center - Vector3.one * radius);
            Vector3Int max = Cell(center + Vector3.one * radius);
            for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
            for (int z = min.z; z <= max.z; z++)
            {
                if (!_cellLookup.TryGetValue(new Vector3Int(x, y, z), out int cell)) continue;
                int end = _cellStart[cell] + _cellCount[cell];
                for (int s = _cellStart[cell]; s < end; s++)
                {
                    int i = _sortedIndices[s];
                    float sqrDistance = (_worldPositions[i] - center).sqrMagnitude;
                    if (sqrDistance > sqrRadius) continue;
                    indices.Add(i);
                    distances.Add(Mathf.Sqrt(sqrDistance));
                }
            }
        }

        public void Dispose()
        {
            if (_colliderMesh != null) Object.DestroyImmediate(_colliderMesh);
            if (_previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(_previewScene);
            _colliderMesh = null;
            _collider = null;
            _colliderObject = null;
            Painter = null;
            Mesh = null;
        }

        private void Rebuild(VertexPainter painter, Mesh mesh, Matrix4x4 localToWorld)
        {
            Painter = painter;
            Mesh = mesh;
            _verifyGeometry = false;
            mesh.GetVertices(_localPositions);
            mesh.GetNormals(_localNormals);

            _triangles.Clear();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                mesh.GetIndices(_submeshIndices, sub);
                _triangles.AddRange(_submeshIndices);
            }

            RebuildWorldSpace(localToWorld);
        }

        private void RebuildWorldSpace(Matrix4x4 localToWorld)
        {
            _localToWorld = localToWorld;
            NormalToWorld = localToWorld.inverse.transpose;
            Version++;

            int count = _localPositions.Count;
            if (_worldPositions.Length != count) _worldPositions = new Vector3[count];
            for (int i = 0; i < count; i++) _worldPositions[i] = localToWorld.MultiplyPoint3x4(_localPositions[i]);

            _cellSize = 0f; // grid is rebuilt lazily on the next query
            RebuildCollider(localToWorld.determinant < 0f);
        }

        private void RebuildCollider(bool mirrored)
        {
            if (!_previewScene.IsValid())
            {
                _previewScene = EditorSceneManager.NewPreviewScene();
                _colliderObject = new GameObject("Vertex Paint Collider") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(_colliderObject, _previewScene);
                _collider = _colliderObject.AddComponent<MeshCollider>();
            }

            if (_colliderMesh == null) _colliderMesh = new Mesh { name = "Vertex Paint Collider", hideFlags = HideFlags.HideAndDontSave };
            _colliderMesh.Clear();
            _colliderMesh.indexFormat = _worldPositions.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _colliderMesh.SetVertices(_worldPositions);

            // a mirrored transform flips the winding, flip it back so raycasts hit the visible side
            if (mirrored)
            {
                for (int t = 0; t + 2 < _triangles.Count; t += 3)
                {
                    (_triangles[t + 1], _triangles[t + 2]) = (_triangles[t + 2], _triangles[t + 1]);
                }
            }
            _colliderMesh.SetTriangles(_triangles, 0, true);
            if (mirrored)
            {
                for (int t = 0; t + 2 < _triangles.Count; t += 3)
                {
                    (_triangles[t + 1], _triangles[t + 2]) = (_triangles[t + 2], _triangles[t + 1]);
                }
            }

            _collider.sharedMesh = null;
            _collider.sharedMesh = _triangles.Count > 0 ? _colliderMesh : null;
        }

        // The grid's cell size follows the brush radius, so a query visits at most 3x3x3 cells.
        // It is only rebuilt when the radius changes a lot.
        private void EnsureGrid(float radius)
        {
            if (_cellSize > 0f && radius >= _cellSize * 0.5f && radius <= _cellSize * 2f) return;

            _cellSize = Mathf.Max(radius, 1e-4f);
            int count = _worldPositions.Length;
            if (_vertexCell.Length != count) _vertexCell = new int[count];
            if (_sortedIndices.Length != count) _sortedIndices = new int[count];
            _cellLookup.Clear();

            var cellCounts = new List<int>();
            for (int i = 0; i < count; i++)
            {
                Vector3Int key = Cell(_worldPositions[i]);
                if (!_cellLookup.TryGetValue(key, out int cell))
                {
                    cell = cellCounts.Count;
                    _cellLookup.Add(key, cell);
                    cellCounts.Add(0);
                }
                _vertexCell[i] = cell;
                cellCounts[cell]++;
            }

            int cells = cellCounts.Count;
            if (_cellStart.Length < cells)
            {
                _cellStart = new int[cells];
                _cellCount = new int[cells];
            }
            int start = 0;
            for (int c = 0; c < cells; c++)
            {
                _cellStart[c] = start;
                _cellCount[c] = 0;
                start += cellCounts[c];
            }
            for (int i = 0; i < count; i++)
            {
                int cell = _vertexCell[i];
                _sortedIndices[_cellStart[cell] + _cellCount[cell]] = i;
                _cellCount[cell]++;
            }
        }

        private Vector3Int Cell(Vector3 position)
        {
            return new Vector3Int(
                Mathf.FloorToInt(position.x / _cellSize),
                Mathf.FloorToInt(position.y / _cellSize),
                Mathf.FloorToInt(position.z / _cellSize));
        }

        private static bool SamePositions(List<Vector3> a, List<Vector3> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }
}
