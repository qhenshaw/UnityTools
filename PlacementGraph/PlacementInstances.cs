using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityTools.Placement
{
    /// <summary>Receives what the Spawn Instanced node places: a prefab drawn with GPU instancing, without GameObjects.</summary>
    public interface IInstanceOutput
    {
        /// <summary>Adds one instanced copy of <paramref name="prefab"/>'s meshes at this world position, rotation and local scale.</summary>
        void AddInstance(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale, InstanceDrawSettings settings);
    }

    /// <summary>How the Spawn Instanced node's instances are drawn.</summary>
    [Serializable]
    public struct InstanceDrawSettings
    {
        public ShadowCastingMode ShadowCasting;
        public bool ReceiveShadows;
        /// <summary>The layer to draw on, or -1 for each part's own layer in the prefab.</summary>
        public int Layer;

        public static InstanceDrawSettings Default => new InstanceDrawSettings { ShadowCasting = ShadowCastingMode.On, ReceiveShadows = true, Layer = -1 };
    }

    /// <summary>
    /// The instanced meshes a Placement Graph generated with Spawn Instanced: one batch of matrices per mesh, submesh,
    /// material and draw settings. The batches are serialized, so they survive domain and scene reloads without
    /// regenerating, and they are drawn with <see cref="Graphics.RenderMeshInstanced{T}(in RenderParams, Mesh, int, List{T}, int, int)"/>
    /// every frame, in edit mode (Scene and Game views) and in Play mode.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public class PlacementInstances : MonoBehaviour
    {
        /// <summary>The most instances drawn in one call.</summary>
        public const int MaxPerDraw = 1023;

        /// <summary>The instances of one mesh, submesh and material with one set of draw settings.</summary>
        [Serializable]
        public class Batch
        {
            public Mesh Mesh;
            public int SubMesh;
            public Material Material;
            public ShadowCastingMode ShadowCasting = ShadowCastingMode.On;
            public bool ReceiveShadows = true;
            public int Layer;
            /// <summary>The partition cell that owns the batch (0 when the graph isn't partitioned).</summary>
            public int Cell;
            public List<Matrix4x4> Matrices = new List<Matrix4x4>();
            public Bounds Bounds;

            public bool Matches(Mesh mesh, int subMesh, Material material, ShadowCastingMode shadows, bool receive, int layer, int cell) =>
                Mesh == mesh && SubMesh == subMesh && Material == material && ShadowCasting == shadows &&
                ReceiveShadows == receive && Layer == layer && Cell == cell;
        }

        /// <summary>One drawable part of a prefab: a mesh and its materials, relative to the prefab's root.</summary>
        private struct Part
        {
            public Mesh Mesh;
            public Material[] Materials;
            public Matrix4x4 LocalToRoot;
            public int Layer;
            public ShadowCastingMode ShadowCasting;
            public bool ReceiveShadows;
        }

        [SerializeField] private List<Batch> _batches = new List<Batch>();

        private readonly Dictionary<GameObject, Part[]> _parts = new Dictionary<GameObject, Part[]>();
        private readonly HashSet<Material> _warned = new HashSet<Material>();

        /// <summary>The batches, for inspection and tests.</summary>
        public IReadOnlyList<Batch> Batches => _batches;

        /// <summary>Total number of instances over all batches.</summary>
        public int InstanceCount
        {
            get
            {
                int count = 0;
                foreach (Batch batch in _batches) count += batch.Matrices.Count;
                return count;
            }
        }

        /// <summary>Draw calls one frame takes: each batch in chunks of <see cref="MaxPerDraw"/>.</summary>
        public int DrawCallCount
        {
            get
            {
                int calls = 0;
                foreach (Batch batch in _batches) calls += (batch.Matrices.Count + MaxPerDraw - 1) / MaxPerDraw;
                return calls;
            }
        }

        /// <summary>Removes every batch, or only those of <paramref name="cell"/>.</summary>
        public void Clear(int cell = -1)
        {
            if (cell < 0) _batches.Clear();
            else _batches.RemoveAll(b => b.Cell == cell);
            _parts.Clear();
        }

        /// <summary>
        /// Adds one copy of <paramref name="prefab"/>: every MeshFilter and MeshRenderer pair in it, at its place relative
        /// to the prefab's root. Returns warnings (a material without GPU instancing), or null.
        /// </summary>
        public string Add(GameObject prefab, Matrix4x4 rootToWorld, InstanceDrawSettings settings, int cell = 0)
        {
            string warning = null;
            foreach (Part part in PartsOf(prefab))
            {
                Matrix4x4 matrix = rootToWorld * part.LocalToRoot;
                int layer = settings.Layer >= 0 ? settings.Layer : part.Layer;
                ShadowCastingMode shadows = settings.ShadowCasting;
                bool receive = settings.ReceiveShadows;
                int subMeshes = Mathf.Min(part.Mesh.subMeshCount, part.Materials.Length);
                for (int s = 0; s < subMeshes; s++)
                {
                    Material material = part.Materials[s];
                    if (material == null) continue;
                    if (!material.enableInstancing && _warned.Add(material))
                        warning = $"Material '{material.name}' doesn't have GPU instancing enabled, so Spawn Instanced can't draw it efficiently (or at all). Turn on Enable GPU Instancing on the material.";
                    Batch batch = FindBatch(part.Mesh, s, material, shadows, receive, layer, cell);
                    batch.Matrices.Add(matrix);
                    Bounds bounds = TransformBounds(part.Mesh.GetSubMesh(s).bounds, matrix);
                    if (batch.Matrices.Count == 1) batch.Bounds = bounds;
                    else batch.Bounds.Encapsulate(bounds);
                }
            }
            return warning;
        }

        private Batch FindBatch(Mesh mesh, int subMesh, Material material, ShadowCastingMode shadows, bool receive, int layer, int cell)
        {
            foreach (Batch batch in _batches)
            {
                if (batch.Matches(mesh, subMesh, material, shadows, receive, layer, cell)) return batch;
            }
            var created = new Batch
            {
                Mesh = mesh, SubMesh = subMesh, Material = material, ShadowCasting = shadows,
                ReceiveShadows = receive, Layer = layer, Cell = cell
            };
            _batches.Add(created);
            return created;
        }

        private Part[] PartsOf(GameObject prefab)
        {
            if (_parts.TryGetValue(prefab, out Part[] parts)) return parts;
            var list = new List<Part>();
            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !renderer.enabled || !renderer.gameObject.activeSelf) continue;
                list.Add(new Part
                {
                    Mesh = filter.sharedMesh,
                    Materials = renderer.sharedMaterials,
                    // Relative to the root, in the root's space: the node applies the root's scale (Keep Prefab Scale) itself.
                    LocalToRoot = prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix,
                    Layer = renderer.gameObject.layer,
                    ShadowCasting = renderer.shadowCastingMode,
                    ReceiveShadows = renderer.receiveShadows
                });
            }
            parts = list.ToArray();
            _parts[prefab] = parts;
            return parts;
        }

        private static Bounds TransformBounds(Bounds local, Matrix4x4 matrix)
        {
            Vector3 center = matrix.MultiplyPoint3x4(local.center);
            Vector3 extents = local.extents;
            var axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            var axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            var axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
            Vector3 size = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, size * 2f);
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            Camera.onPreCull += OnPreCullCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            Camera.onPreCull -= OnPreCullCamera;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera) => Draw(camera);

        // The built-in render pipeline has no beginCameraRendering; Camera.onPreCull covers it.
        private void OnPreCullCamera(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline == null) Draw(camera);
        }

        /// <summary>Issues the instanced draws for one camera.</summary>
        public void Draw(Camera camera)
        {
            if (this == null || !isActiveAndEnabled) return;
            foreach (Batch batch in _batches)
            {
                if (batch.Mesh == null || batch.Material == null || batch.Matrices.Count == 0) continue;
                var parameters = new RenderParams(batch.Material)
                {
                    camera = camera,
                    layer = batch.Layer,
                    shadowCastingMode = batch.ShadowCasting,
                    receiveShadows = batch.ReceiveShadows,
                    worldBounds = batch.Bounds
                };
                for (int start = 0; start < batch.Matrices.Count; start += MaxPerDraw)
                {
                    int count = Mathf.Min(MaxPerDraw, batch.Matrices.Count - start);
                    Graphics.RenderMeshInstanced(parameters, batch.Mesh, batch.SubMesh, batch.Matrices, count, start);
                }
            }
        }
    }
}
