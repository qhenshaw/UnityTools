using UnityEngine;

namespace SceneManagement
{
    public static class MeshPreview
    {
        public static void DrawImmediate(GameObject gameObject, Vector3 position, Quaternion rotation, Material material)
        {
            if (Application.isPlaying) return;
            if (gameObject == null) return;
            if (material == null) return;

            Transform[] transforms = gameObject.GetComponentsInChildren<Transform>();

            RenderParams _renderParams = new RenderParams(material);

            Matrix4x4 transform = Matrix4x4.TRS(position, rotation, Vector3.one);

            for (int i = 0; i < transforms.Length; i++)
            {
                Transform t = transforms[i];
                Mesh mesh = null;
                bool useOffset = false;

                if (t.TryGetComponent(out MeshFilter meshFilter))
                {
                    mesh = meshFilter.sharedMesh;
                    if (i != 0) useOffset = true;
                }
                else if (t.TryGetComponent(out SkinnedMeshRenderer skinnedMeshRenderer))
                {
                    mesh = skinnedMeshRenderer.sharedMesh;
                }

                if (mesh == null) continue;

                Vector3 relativePosition = transform.MultiplyVector(t.localPosition);
                if (!useOffset) relativePosition = Vector3.zero;
                Matrix4x4 matrix = Matrix4x4.TRS(position + relativePosition, rotation * t.rotation, t.localScale);
                for (int j = 0; j < mesh.subMeshCount; j++)
                {
                    Graphics.RenderMesh(_renderParams, mesh, j, matrix);
                }
            }
        }
    }
}