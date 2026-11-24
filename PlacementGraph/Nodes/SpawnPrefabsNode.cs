using System;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// Spawner: places a prefab from a <see cref="PrefabPalette"/> at every point, picked by weight. Passes its
    /// points through, so another branch can build on what was placed.
    /// </summary>
    [Serializable]
    public class SpawnPrefabsNode : PlacementNode
    {
        public PrefabPalette Palette;
        [Tooltip("Apply the prefab's own root rotation after the point's.")]
        public bool KeepPrefabRotation;
        [Tooltip("Multiply the point's scale by the prefab's own root scale. Off: the point's scale is used as is.")]
        public bool KeepPrefabScale = true;

        public override int InputCount => 1;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            outputs[0] = inputs[0];
            if (Palette == null || Palette.TotalWeight <= 0f)
            {
                context.Warn(this, "No Prefab Palette, or the palette has no prefabs with a weight above 0.");
                return;
            }
            if (context.Output == null) return;

            uint seed = PlacementRandom.Hash(context.SeedHash, NodeSeed);
            foreach (PlacementPoint point in inputs[0].Points)
            {
                PrefabPalette.Entry entry = Palette.PickEntry(PlacementRandom.Value(PlacementRandom.Hash(point.Seed, seed)));
                if (entry == null) continue;
                GameObject prefab = entry.Prefab;
                Transform root = prefab.transform;
                Quaternion rotation = KeepPrefabRotation ? point.Rotation * root.localRotation : point.Rotation;
                Vector3 scale = KeepPrefabScale ? Vector3.Scale(root.localScale, point.Scale) : point.Scale;
                // The entry's own scale variation uses its own random channel, so turning it on doesn't change the picks.
                if (entry.RandomScale) scale *= PlacementRandom.Range(point.Seed, seed, 11u, entry.ScaleMin, entry.ScaleMax);
                context.Output.Spawn(prefab, point.Position, rotation, scale);
            }
        }
    }
}
