using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityTools.Placement
{
    /// <summary>Receives meshes that nodes generate (Spawn Spline Mesh). The component makes an object for each and cleans them up.</summary>
    public interface IMeshOutput
    {
        /// <summary>Adds a generated mesh drawn with <paramref name="material"/>; its vertices are in world space.</summary>
        void AddMesh(Mesh mesh, Material material, ShadowCastingMode shadows);
    }

    /// <summary>
    /// Stamps an assembly at every point: the children of Prefab (a prefab whose children are laid out together, such as
    /// a campfire with logs and stones) become points at their place relative to the prefab's root, moved, rotated and
    /// scaled with the point. Each child point carries the Prefab attribute (the child), so Spawn Prefabs or Spawn
    /// Instanced place it, and AssemblyChild (the child's index). Per-point variation is deterministic: Drop Chance
    /// leaves children out, Rotation Jitter turns them about Y and Scale Jitter scales them.
    /// </summary>
    [Serializable]
    public class AssemblyNode : PlacementNode
    {
        public const string ChildAttribute = "AssemblyChild";

        [Tooltip("A prefab whose direct children are the parts to stamp.")]
        public GameObject Prefab;
        [Tooltip("The chance each part is left out at a point.")]
        [Range(0f, 1f)] public float DropChance;
        [Tooltip("Turns each part about its up axis by up to this many degrees either way.")]
        [Min(0f)] public float RotationJitter;
        [Tooltip("Scales each part by up to this fraction either way (0.1 = 90% to 110%).")]
        [Range(0f, 1f)] public float ScaleJitter;

        public override int InputCount => 1;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            PointData input = inputs[0];
            PointData result = PointData.Derive(input);
            outputs[0] = result;
            if (Prefab == null)
            {
                context.Warn(this, "No Prefab: set the assembly prefab whose children are stamped.");
                return;
            }

            var children = new List<Transform>();
            foreach (Transform child in Prefab.transform)
            {
                if (child.gameObject.activeSelf) children.Add(child);
            }
            if (children.Count == 0)
            {
                context.Warn(this, $"{Prefab.name} has no active children to stamp.");
                return;
            }

            PointAttribute<UnityEngine.Object> prefabs = result.GetOrAddAttribute<UnityEngine.Object>(PointAttributeNames.Prefab);
            PointAttribute<int> childIndices = result.GetOrAddAttribute(ChildAttribute, -1);
            uint seed = PlacementRandom.Hash(context.SeedHash, NodeSeed);
            for (int i = 0; i < input.Count; i++)
            {
                PlacementPoint point = input.Points[i];
                for (int c = 0; c < children.Count; c++)
                {
                    uint s = PlacementRandom.Hash(point.Seed, unchecked((uint)c));
                    if (DropChance > 0f && PlacementRandom.Value(PlacementRandom.Hash(s, seed, 1u)) < DropChance) continue;
                    Transform child = children[c];
                    float yaw = RotationJitter > 0f ? PlacementRandom.Range(s, seed, 2u, -RotationJitter, RotationJitter) : 0f;
                    float factor = ScaleJitter > 0f ? PlacementRandom.Range(s, seed, 3u, 1f - ScaleJitter, 1f + ScaleJitter) : 1f;

                    // The child's own scale isn't folded in: Spawn Prefabs' Keep Prefab Scale applies it.
                    PlacementPoint part = point;
                    part.Position = point.Position + point.Rotation * Vector3.Scale(point.Scale, child.localPosition);
                    part.Rotation = point.Rotation * child.localRotation * Quaternion.Euler(0f, yaw, 0f);
                    part.Scale = point.Scale * factor;
                    part.Normal = point.Rotation * Vector3.up;
                    part.Seed = s;
                    int index = result.Count;
                    result.AddFrom(input, i, part);
                    prefabs.Set(index, child.gameObject);
                    childIndices.Set(index, c);
                }
            }
        }
    }

    /// <summary>
    /// Deforms a mesh along each polyline (the input points in order, the volume's edges or the spline's knots): the
    /// mesh is repeated along its length axis to cover the line, stretched so the copies fit exactly (or left at their
    /// length), and bent to follow it, staying upright. The generated meshes belong to the Placement Graph component,
    /// which removes them on Clear.
    /// </summary>
    [Serializable]
    public class SpawnSplineMeshNode : PlacementNode
    {
        public Mesh Mesh;
        public Material Material;
        public Subdivide.SourceMode Source = Subdivide.SourceMode.InputPoints;
        [Tooltip("Input Points: also join the last point back to the first.")]
        public bool Closed;
        [Tooltip("Which local axis of the mesh runs along the line.")]
        public Subdivide.Axis MeshAxis = Subdivide.Axis.X;
        [Tooltip("Stretch the copies so they cover the line exactly. Off: whole copies at their length, the rest left empty.")]
        public bool StretchToFit = true;
        public ShadowCastingMode ShadowCasting = ShadowCastingMode.On;

        /// <summary>Above this many vertices per line the node stops and warns.</summary>
        public const int MaxVertices = 2000000;

        public override int InputCount => 1;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            outputs[0] = inputs[0];
            if (Mesh == null)
            {
                context.Warn(this, "No Mesh.");
                return;
            }
            if (!Mesh.isReadable)
            {
                context.Warn(this, $"Mesh '{Mesh.name}' isn't readable. Turn on Read/Write in its import settings.");
                return;
            }
            if (context.Meshes == null) return;

            int axis = (int)MeshAxis;
            foreach ((List<Vector3> points, bool closed) in Polylines.Gather(context, this, Source, inputs[0], Closed))
            {
                if (!context.IsOwned(points[0])) continue;
                Mesh bent = Bend(points, closed, axis, out string warning);
                if (warning != null) context.Warn(this, warning);
                if (bent != null) context.Meshes.AddMesh(bent, Material, ShadowCasting);
            }
        }

        /// <summary>Builds the bent mesh for one polyline, in world space.</summary>
        internal Mesh Bend(List<Vector3> line, bool closed, int axis, out string warning)
        {
            warning = null;
            var points = new List<Vector3>(line);
            if (closed) points.Add(line[0]);
            var distances = new float[points.Count];
            for (int i = 1; i < points.Count; i++) distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            float total = distances[distances.Length - 1];
            Bounds bounds = Mesh.bounds;
            float meshLength = bounds.size[axis];
            if (total <= 1e-4f || meshLength <= 1e-5f) return null;

            int copies = StretchToFit ? Mathf.Max(1, Mathf.RoundToInt(total / meshLength)) : Mathf.FloorToInt(total / meshLength);
            if (copies == 0) return null;
            float stretch = StretchToFit ? total / (copies * meshLength) : 1f;

            Vector3[] sourceVertices = Mesh.vertices;
            Vector3[] sourceNormals = Mesh.normals;
            Vector2[] uvs = Mesh.uv;
            if ((long)sourceVertices.Length * copies > MaxVertices)
            {
                warning = $"Bending '{Mesh.name}' along a {total:0.#} m line would make more than {MaxVertices} vertices.";
                return null;
            }

            var vertices = new List<Vector3>(sourceVertices.Length * copies);
            var normals = new List<Vector3>(sourceVertices.Length * copies);
            var uv = new List<Vector2>(uvs.Length > 0 ? sourceVertices.Length * copies : 0);
            int side = axis == 0 ? 2 : 0;
            for (int k = 0; k < copies; k++)
            {
                for (int v = 0; v < sourceVertices.Length; v++)
                {
                    Vector3 vertex = sourceVertices[v];
                    float along = (k * meshLength + vertex[axis] - bounds.min[axis]) * stretch;
                    Frame(points, distances, along, axis, out Vector3 position, out Vector3 forward, out Vector3 sideways);
                    vertices.Add(position + Vector3.up * vertex.y + sideways * vertex[side]);
                    if (sourceNormals.Length == sourceVertices.Length)
                    {
                        Vector3 n = sourceNormals[v];
                        normals.Add((forward * n[axis] + Vector3.up * n.y + sideways * n[side]).normalized);
                    }
                    if (uvs.Length == sourceVertices.Length) uv.Add(uvs[v]);
                }
            }

            var mesh = new Mesh { name = GeneratedMeshName, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            if (normals.Count == vertices.Count) mesh.SetNormals(normals);
            if (uv.Count == vertices.Count) mesh.SetUVs(0, uv);
            mesh.subMeshCount = Mesh.subMeshCount;
            for (int s = 0; s < Mesh.subMeshCount; s++)
            {
                int[] triangles = Mesh.GetTriangles(s);
                var all = new List<int>(triangles.Length * copies);
                for (int k = 0; k < copies; k++)
                {
                    int offset = k * sourceVertices.Length;
                    foreach (int index in triangles) all.Add(index + offset);
                }
                mesh.SetTriangles(all, s);
            }
            mesh.RecalculateBounds();
            if (normals.Count != vertices.Count) mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>The name generated meshes get, so the component knows which meshes it may destroy.</summary>
        public const string GeneratedMeshName = "Placement Graph Spline Mesh";

        private static void Frame(List<Vector3> points, float[] distances, float along, int axis,
            out Vector3 position, out Vector3 forward, out Vector3 sideways)
        {
            int segment = Array.BinarySearch(distances, along);
            if (segment < 0) segment = ~segment - 1;
            segment = Mathf.Clamp(segment, 0, points.Count - 2);
            float length = distances[segment + 1] - distances[segment];
            float t = length > 0f ? (along - distances[segment]) / length : 0f;
            Vector3 a = points[segment], b = points[segment + 1];
            position = Vector3.LerpUnclamped(a, b, t);
            forward = (b - a).sqrMagnitude > 1e-10f ? (b - a).normalized : Vector3.forward;
            // Upright frame: the mesh's Y stays world up; its side axis is perpendicular to the line.
            sideways = axis == 0 ? Vector3.Cross(forward, Vector3.up) : Vector3.Cross(Vector3.up, forward);
            sideways = sideways.sqrMagnitude > 1e-10f ? sideways.normalized : Vector3.right;
        }
    }
}
