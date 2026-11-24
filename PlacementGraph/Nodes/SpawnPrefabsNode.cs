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
                GameObject prefab = Palette.Pick(PlacementRandom.Value(PlacementRandom.Hash(point.Seed, seed)));
                if (prefab != null) context.Output.Spawn(prefab, point.Position, point.Rotation, point.Scale);
            }
        }
    }
}
