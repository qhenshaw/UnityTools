using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace UnityTools.Optimization
{
    public class RuntimeMeshCombine : MonoBehaviour
    {
        [SerializeField] private bool _combineOnStart = true;

        private void Start()
        {
            if(_combineOnStart) CombineMeshes(gameObject);
        }

        public void CombineMeshes(GameObject rootObject)
        {
            string meshName = rootObject.name + "_combinedMesh";
            MeshFilter[] allMeshFilters = rootObject.GetComponentsInChildren<MeshFilter>();
            List<MeshFilter> combinedMeshFilters = new List<MeshFilter>();
            MeshRenderer firstMeshRenderer = null;
            Dictionary<Material, List<CombineInstance>> combines = new Dictionary<Material, List<CombineInstance>>();

            for (int i = 0; i < allMeshFilters.Length; i++)
            {
                MeshFilter mf = allMeshFilters[i];
                if (mf.sharedMesh.subMeshCount > 1) continue;
                if (!mf.TryGetComponent(out MeshRenderer mr) || !mr.enabled) continue;
                combinedMeshFilters.Add(mf);
            }

            for (int i = 0; i < combinedMeshFilters.Count; i++)
            {
                MeshFilter mf = combinedMeshFilters[i];
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (firstMeshRenderer == null) firstMeshRenderer = mr.GetComponent<MeshRenderer>();
                Material mat = mr.sharedMaterial;
                if (!combines.ContainsKey(mat)) combines.Add(mat, new List<CombineInstance>());
                CombineInstance combine = new CombineInstance();
                Transform mfTransform = mf.transform;
                combine.mesh = mf.sharedMesh;
                combine.transform = transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                combine.subMeshIndex = 0;
                combines[mat].Add(combine);
            }

            List<Mesh> subMeshes = new List<Mesh>();
            foreach (var kvp in combines)
            {
                Mesh mesh = new Mesh();
                mesh.name = meshName;
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.CombineMeshes(kvp.Value.ToArray(), true, true, false);
                subMeshes.Add(mesh);
            }

            CombineInstance[] finalCombine = new CombineInstance[subMeshes.Count];
            for (int i = 0; i < subMeshes.Count; i++)
            {
                finalCombine[i].mesh = subMeshes[i];
                finalCombine[i].transform = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one);
            }
            Mesh finalMesh = new Mesh();
            finalMesh.name = meshName;
            finalMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            finalMesh.subMeshCount = subMeshes.Count;
            finalMesh.CombineMeshes(finalCombine, false, true, false);

            GameObject meshGO = new GameObject(meshName);
            meshGO.transform.SetParent(rootObject.transform);
            meshGO.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            meshGO.transform.localScale = Vector3.one;

            MeshFilter meshFilter = meshGO.AddComponent<MeshFilter>();
            meshFilter.mesh = finalMesh;

            MeshRenderer meshRenderer = meshGO.AddComponent<MeshRenderer>();
            meshRenderer.SetMaterials(combines.Keys.ToList());
            meshRenderer.shadowCastingMode = firstMeshRenderer.shadowCastingMode;
            meshRenderer.staticShadowCaster = firstMeshRenderer.staticShadowCaster;
            meshRenderer.rayTracingMode = firstMeshRenderer.rayTracingMode;
            meshRenderer.rayTracingAccelerationStructureBuildFlags = firstMeshRenderer.rayTracingAccelerationStructureBuildFlags;
            meshRenderer.rayTracingAccelerationStructureBuildFlagsOverride = firstMeshRenderer.rayTracingAccelerationStructureBuildFlagsOverride;
            meshRenderer.motionVectorGenerationMode = firstMeshRenderer.motionVectorGenerationMode;
            meshRenderer.allowOcclusionWhenDynamic = firstMeshRenderer.allowOcclusionWhenDynamic;
            meshRenderer.renderingLayerMask = firstMeshRenderer.renderingLayerMask;
            meshRenderer.rendererPriority = firstMeshRenderer.rendererPriority;

            for (int i = 0; i < combinedMeshFilters.Count; i++)
            {
                GameObject mfGO = combinedMeshFilters[i].gameObject;
                Destroy(combinedMeshFilters[i]);
                Destroy(combinedMeshFilters[i].GetComponent("ProBuilderMesh"));
                Destroy(combinedMeshFilters[i].GetComponent("ProBuilderShape"));
                Destroy(combinedMeshFilters[i].GetComponent<MeshRenderer>());

                Component[] monos = mfGO.GetComponents<Component>();
                if (monos.Length == 0) Destroy(mfGO);
            }
        }
    }
}