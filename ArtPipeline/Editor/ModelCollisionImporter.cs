using UnityEngine;
using UnityEditor;

namespace ArtPipeline.Editor
{
    // Turns Unreal style collision meshes into Unity colliders:
    // UCX_ = convex MeshCollider, UBX_ = BoxCollider, USP_ = SphereCollider, UCP_ = CapsuleCollider.
    // The collider stays on the collision child (keeping its DCC transform) and the child's renderer is removed.
    public class ModelCollisionImporter : AssetPostprocessor
    {
        private enum CollisionType { None, Convex, Box, Sphere, Capsule }

        private const int ConvexTriangleLimit = 255;
        private const float CapsuleAxisTieTolerance = 0.01f;

        private void OnPostprocessModel(GameObject gameObject)
        {
            if (!ArtAssetImportSettings.ReadForImport().GenerateUnrealColliders) return;

            Transform[] children = gameObject.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                CollisionType type = GetCollisionType(child.name);
                if (type == CollisionType.None) continue;

                MeshFilter meshFilter = child.GetComponent<MeshFilter>();
                if (meshFilter == null) continue;
                Mesh mesh = meshFilter.sharedMesh;
                if (mesh == null) continue;

                switch (type)
                {
                    case CollisionType.Convex: AddConvex(child.gameObject, mesh); break;
                    case CollisionType.Box: AddBox(child.gameObject, mesh.bounds); break;
                    case CollisionType.Sphere: AddSphere(child.gameObject, mesh.bounds); break;
                    case CollisionType.Capsule: AddCapsule(child.gameObject, mesh.bounds); break;
                }

                Object.DestroyImmediate(meshFilter);
                MeshRenderer meshRenderer = child.GetComponent<MeshRenderer>();
                if (meshRenderer != null) Object.DestroyImmediate(meshRenderer);
            }
        }

        private static CollisionType GetCollisionType(string name)
        {
            if (name.Length < 4 || name[3] != '_') return CollisionType.None;
            switch (name.Substring(0, 3).ToUpperInvariant())
            {
                case "UCX": return CollisionType.Convex;
                case "UBX": return CollisionType.Box;
                case "USP": return CollisionType.Sphere;
                case "UCP": return CollisionType.Capsule;
                default: return CollisionType.None;
            }
        }

        private void AddConvex(GameObject target, Mesh mesh)
        {
            int triangles = mesh.triangles.Length / 3;
            if (triangles > ConvexTriangleLimit)
            {
                Debug.LogWarning($"{assetPath}: UCX collision mesh '{target.name}' has {triangles} triangles. Unity limits convex colliders to {ConvexTriangleLimit}, so the hull will be simplified.");
            }

            MeshCollider collider = target.GetComponent<MeshCollider>();
            if (collider == null) collider = target.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;
        }

        private static void AddBox(GameObject target, Bounds bounds)
        {
            BoxCollider collider = target.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = bounds.size;
        }

        private static void AddSphere(GameObject target, Bounds bounds)
        {
            Vector3 extents = bounds.extents;
            SphereCollider collider = target.AddComponent<SphereCollider>();
            collider.center = bounds.center;
            collider.radius = Mathf.Max(extents.x, extents.y, extents.z);
        }

        private static void AddCapsule(GameObject target, Bounds bounds)
        {
            Vector3 size = bounds.size;
            int axis = GetCapsuleAxis(size);
            float radius = 0f;
            for (int a = 0; a < 3; a++)
            {
                if (a != axis) radius = Mathf.Max(radius, size[a] * 0.5f);
            }

            CapsuleCollider collider = target.AddComponent<CapsuleCollider>();
            collider.center = bounds.center;
            collider.direction = axis;
            collider.radius = radius;
            collider.height = Mathf.Max(size[axis], radius * 2f);
        }

        // Longest axis of the bounds. When the longest axes are (nearly) tied, prefer Y, then the first one.
        private static int GetCapsuleAxis(Vector3 size)
        {
            float longest = Mathf.Max(size.x, size.y, size.z);
            float tolerance = longest * CapsuleAxisTieTolerance;
            if (longest - size.y <= tolerance) return 1;
            if (longest - size.x <= tolerance) return 0;
            return 2;
        }
    }
}
