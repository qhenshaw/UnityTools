using System;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// Spawner: places a prefab at every point and passes the points through, so another branch can build on what was
    /// placed. A point with a Prefab attribute (from Select Prefab) gets that prefab; any other point gets one picked
    /// from the Palette by weight. With Apply Entry Adjustments on, the palette entry's offset, rotation and scale are
    /// applied too.
    /// </summary>
    [Serializable]
    public class SpawnPrefabsNode : PlacementNode
    {
        [Tooltip("Where prefabs are picked from for points without a Prefab attribute.")]
        public PrefabPalette Palette;
        [Tooltip("Apply the prefab's own root rotation after the point's.")]
        public bool KeepPrefabRotation;
        [Tooltip("Multiply the point's scale by the prefab's own root scale. Off: the point's scale is used as is.")]
        public bool KeepPrefabScale = true;
        [Tooltip("Apply the palette entry's position offset, rotation and scale.")]
        public bool ApplyEntryAdjustments = true;

        public override int InputCount => 1;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            PointData input = inputs[0];
            outputs[0] = input;
            PointAttribute<UnityEngine.Object> prefabs = input.GetAttribute<UnityEngine.Object>(PointAttributeNames.Prefab);
            bool hasPalette = Palette != null && Palette.TotalWeight > 0f;
            if (prefabs == null && !hasPalette)
            {
                context.Warn(this, "No Prefab Palette (or it has no prefabs with a weight above 0), and the points have no Prefab attribute from Select Prefab.");
                return;
            }
            if (context.Output == null) return;

            PointAttribute<UnityEngine.Object> palettes = input.GetAttribute<UnityEngine.Object>(PointAttributeNames.Palette);
            PointAttribute<int> entries = input.GetAttribute<int>(PointAttributeNames.PaletteEntry);
            PointAttribute<Vector3> entryOffsets = input.GetAttribute<Vector3>(PointAttributeNames.EntryOffset);
            PointAttribute<Vector3> entryRotations = input.GetAttribute<Vector3>(PointAttributeNames.EntryRotation);
            PointAttribute<float> entryScales = input.GetAttribute<float>(PointAttributeNames.EntryScale);

            uint seed = PlacementRandom.Hash(context.SeedHash, NodeSeed);
            int missing = 0;
            for (int i = 0; i < input.Count; i++)
            {
                PlacementPoint point = input.Points[i];
                GameObject prefab = prefabs?.Get(i) as GameObject;
                PrefabPalette.Entry entry = null;
                if (prefab != null)
                {
                    entry = (palettes?.Get(i) as PrefabPalette)?.EntryAt(entries?.Get(i) ?? -1);
                }
                else if (hasPalette)
                {
                    entry = Palette.PickEntry(PlacementRandom.Value(PlacementRandom.Hash(point.Seed, seed)));
                    prefab = entry?.Prefab;
                }
                if (prefab == null)
                {
                    missing++;
                    continue;
                }

                Vector3 position = point.Position;
                Quaternion rotation = point.Rotation;
                Vector3 scale = point.Scale;
                if (ApplyEntryAdjustments)
                {
                    // Values Select Prefab already sampled win, so a point keeps the same adjustment downstream.
                    Vector3 offset = Vector3.zero, euler = Vector3.zero;
                    float factor = 1f;
                    if (entry != null) entry.Sample(point.Seed, seed, out offset, out euler, out factor);
                    if (entryOffsets != null) offset = entryOffsets.Get(i);
                    if (entryRotations != null) euler = entryRotations.Get(i);
                    if (entryScales != null) factor = entryScales.Get(i);
                    position += rotation * offset;
                    rotation *= Quaternion.Euler(euler);
                    scale *= factor;
                }

                Transform root = prefab.transform;
                if (KeepPrefabRotation) rotation *= root.localRotation;
                if (KeepPrefabScale) scale = Vector3.Scale(root.localScale, scale);
                context.Output.Spawn(prefab, position, rotation, scale);
            }
            if (missing > 0) context.Warn(this, $"{missing} points had no prefab (a Prefab attribute that's empty or not a prefab, and no Palette).");
        }
    }
}
