using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityTools.Placement
{
    /// <summary>
    /// Spawner: like Spawn Prefabs (same entries, Prefab attributes and adjustments), but draws each prefab's meshes
    /// with GPU instancing instead of creating GameObjects. Good for thousands of small props (grass, pebbles) that
    /// need no colliders or scripts. Only MeshFilter and MeshRenderer parts are drawn; their materials need GPU
    /// instancing enabled.
    /// </summary>
    [Serializable]
    public class SpawnInstancedNode : SpawnPrefabsNode
    {
        [Tooltip("Whether the instances cast shadows.")]
        public ShadowCastingMode ShadowCasting = ShadowCastingMode.On;
        public bool ReceiveShadows = true;
        [Tooltip("The layer to draw on, or -1 for each part's own layer in the prefab.")]
        public int Layer = -1;

        protected override bool HasOutput(PlacementContext context) => context.Instances != null;

        protected override void Emit(PlacementContext context, GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale) =>
            context.Instances.AddInstance(prefab, position, rotation, scale,
                new InstanceDrawSettings { ShadowCasting = ShadowCasting, ReceiveShadows = ReceiveShadows, Layer = Layer });
    }
}
