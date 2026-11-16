using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Scenes
{
    /// <summary>
    /// Draws the meshes of a prefab where it would spawn, without instantiating it. Each mesh is placed with its
    /// full position, rotation and scale relative to the prefab's root, and the root keeps its own scale.
    /// </summary>
    public static class MeshPreview
    {
        private static readonly List<(Mesh mesh, Matrix4x4 matrix)> s_Meshes = new();

        /// <summary>Renders the prefab's meshes with a material (Scene view preview, edit mode only).</summary>
        public static void DrawImmediate(GameObject gameObject, Vector3 position, Quaternion rotation, Material material)
        {
            if (Application.isPlaying) return;
            if (gameObject == null) return;
            if (material == null) return;

            var renderParams = new RenderParams(material);
            foreach ((Mesh mesh, Matrix4x4 matrix) in GetMeshes(gameObject, position, rotation))
            {
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    Graphics.RenderMesh(renderParams, mesh, i, matrix);
                }
            }
        }

        /// <summary>Draws the prefab's meshes as tinted gizmos. Call from OnDrawGizmos.</summary>
        public static void DrawGizmos(GameObject gameObject, Vector3 position, Quaternion rotation, Color color)
        {
            if (gameObject == null) return;

            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.color = color;
            foreach ((Mesh mesh, Matrix4x4 matrix) in GetMeshes(gameObject, position, rotation))
            {
                Gizmos.matrix = matrix;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    Gizmos.DrawMesh(mesh, i);
                }
            }
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        /// <summary>
        /// The meshes the prefab would draw if spawned at position and rotation, with their world matrices.
        /// Inactive objects and disabled renderers are skipped; skinned meshes use their bind pose.
        /// The returned list is reused by the next call.
        /// </summary>
        internal static List<(Mesh mesh, Matrix4x4 matrix)> GetMeshes(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            s_Meshes.Clear();
            if (prefab == null) return s_Meshes;

            Transform root = prefab.transform;
            Matrix4x4 placement = Matrix4x4.TRS(position, rotation, root.localScale);
            Matrix4x4 rootInverse = root.worldToLocalMatrix;

            foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (!IsActive(t, root)) continue;

                Mesh mesh = null;
                if (t.TryGetComponent(out MeshFilter filter) && t.TryGetComponent(out MeshRenderer meshRenderer) && meshRenderer.enabled)
                {
                    mesh = filter.sharedMesh;
                }
                else if (t.TryGetComponent(out SkinnedMeshRenderer skinned) && skinned.enabled)
                {
                    mesh = skinned.sharedMesh;
                }
                if (mesh == null) continue;

                s_Meshes.Add((mesh, placement * rootInverse * t.localToWorldMatrix));
            }
            return s_Meshes;
        }

        // activeInHierarchy isn't meaningful for prefab assets, so walk the parents.
        private static bool IsActive(Transform t, Transform root)
        {
            for (Transform current = t; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == root) break;
            }
            return true;
        }
    }
}
