using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// Spawner: places a prefab at every point and passes the points through, so another branch can build on what was
    /// placed. A point with a Prefab attribute (from Select Prefab) gets that prefab, with the adjustments Select
    /// Prefab recorded (EntryOffset, EntryRotation, EntryScale); any other point gets one picked from Entries by weight,
    /// with that entry's adjustments. Apply Entry Adjustments turns the adjustments off.
    /// </summary>
    [Serializable]
    public class SpawnPrefabsNode : PlacementNode
    {
        [Tooltip("The prefabs to pick from for points without a Prefab attribute. In a graph: the Prefab Entry nodes wired into Entries.")]
        public List<PrefabEntry> Entries = new List<PrefabEntry>();
        [Tooltip("Obsolete: used only when Entries is empty, until the palette is converted to Prefab Entry nodes.")]
        public PrefabPalette Palette;
        [Tooltip("Apply the prefab's own root rotation after the point's.")]
        public bool KeepPrefabRotation;
        [Tooltip("Multiply the point's scale by the prefab's own root scale. Off: the point's scale is used as is.")]
        public bool KeepPrefabScale = true;
        [Tooltip("Apply the entry's position offset, rotation and scale.")]
        public bool ApplyEntryAdjustments = true;

        public override int InputCount => 1;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            PointData input = inputs[0];
            outputs[0] = input;
            IReadOnlyList<PrefabEntry> entries = Entries.Count > 0 || Palette == null ? Entries : Palette.Entries;
            PointAttribute<UnityEngine.Object> prefabs = input.GetAttribute<UnityEngine.Object>(PointAttributeNames.Prefab);
            bool hasEntries = PrefabEntry.TotalWeight(entries) > 0f;
            if (prefabs == null && !hasEntries)
            {
                context.Warn(this, "No entries with a prefab and a weight above 0, and the points have no Prefab attribute from Select Prefab. Wire Prefab Entry nodes into Entries.");
                return;
            }
            if (context.Output == null) return;

            PointAttribute<Vector3> entryOffsets = input.GetAttribute<Vector3>(PointAttributeNames.EntryOffset);
            PointAttribute<Vector3> entryRotations = input.GetAttribute<Vector3>(PointAttributeNames.EntryRotation);
            PointAttribute<float> entryScales = input.GetAttribute<float>(PointAttributeNames.EntryScale);

            uint seed = PlacementRandom.Hash(context.SeedHash, NodeSeed);
            int missing = 0;
            for (int i = 0; i < input.Count; i++)
            {
                PlacementPoint point = input.Points[i];
                Vector3 position = point.Position;
                Quaternion rotation = point.Rotation;
                Vector3 scale = point.Scale;
                Vector3 offset = Vector3.zero, euler = Vector3.zero;
                float factor = 1f;

                GameObject prefab = prefabs?.Get(i) as GameObject;
                if (prefab != null)
                {
                    // Chosen by Select Prefab: use the adjustments it recorded for this point.
                    if (entryOffsets != null) offset = entryOffsets.Get(i);
                    if (entryRotations != null) euler = entryRotations.Get(i);
                    if (entryScales != null) factor = entryScales.Get(i);
                }
                else if (hasEntries)
                {
                    int index = PrefabEntry.PickIndex(entries, PlacementRandom.Value(PlacementRandom.Hash(point.Seed, seed)));
                    PrefabEntry entry = entries[index];
                    prefab = entry.Prefab;
                    entry.Sample(point.Seed, seed, out offset, out euler, out factor);
                }
                if (prefab == null)
                {
                    missing++;
                    continue;
                }

                if (ApplyEntryAdjustments)
                {
                    position += rotation * offset;
                    rotation *= Quaternion.Euler(euler);
                    scale *= factor;
                }
                Transform root = prefab.transform;
                if (KeepPrefabRotation) rotation *= root.localRotation;
                if (KeepPrefabScale) scale = Vector3.Scale(root.localScale, scale);
                context.Output.Spawn(prefab, position, rotation, scale);
            }
            if (missing > 0) context.Warn(this, $"{missing} points had no prefab (a Prefab attribute that's empty or not a prefab, and no entries).");
        }
    }
}
