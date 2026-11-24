using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// One prefab that Select Prefab or Spawn Prefabs can pick, with its weight and how it is placed: an offset, a
    /// rotation and a random rotation range, a random scale, and attributes it writes onto the points it is picked
    /// for. In a graph each entry is a Prefab Entry node wired into the node's Entries input.
    /// </summary>
    [Serializable]
    public class PrefabEntry
    {
        public GameObject Prefab;
        [Min(0f)] public float Weight = 1f;
        [Tooltip("Moves the prefab from the point, in the point's local space (so it follows the point's rotation).")]
        public Vector3 PositionOffset = Vector3.zero;
        [Tooltip("Rotates the prefab, in Euler degrees, after the point's rotation.")]
        public Vector3 RotationOffset = Vector3.zero;
        [Tooltip("A random extra rotation per placed copy, in Euler degrees, between Rotation Random Min and Max.")]
        public Vector3 RotationRandomMin = Vector3.zero;
        public Vector3 RotationRandomMax = Vector3.zero;
        [Tooltip("Give the prefab a random uniform scale between Scale Min and Scale Max, on top of the point's scale.")]
        public bool RandomScale;
        [Min(0f)] public float ScaleMin = 1f;
        [Min(0f)] public float ScaleMax = 1f;
        [Tooltip("Attributes Select Prefab writes onto the points this entry is picked for.")]
        public List<EntryAttribute> Attributes = new List<EntryAttribute>();

        /// <summary>
        /// The entry's adjustments for one placed copy: the offset, the rotation (offset plus a random value in the
        /// range, in Euler degrees) and the scale factor. Random values come from the point's and node's seeds.
        /// </summary>
        public void Sample(uint pointSeed, uint nodeSeed, out Vector3 offset, out Vector3 rotation, out float scale)
        {
            offset = PositionOffset;
            rotation = RotationOffset + new Vector3(
                PlacementRandom.Range(pointSeed, nodeSeed, 12u, RotationRandomMin.x, RotationRandomMax.x),
                PlacementRandom.Range(pointSeed, nodeSeed, 13u, RotationRandomMin.y, RotationRandomMax.y),
                PlacementRandom.Range(pointSeed, nodeSeed, 14u, RotationRandomMin.z, RotationRandomMax.z));
            scale = RandomScale ? PlacementRandom.Range(pointSeed, nodeSeed, 11u, ScaleMin, ScaleMax) : 1f;
        }

        /// <summary>Sum of the weights of entries that can be picked (with a prefab and a weight above 0).</summary>
        public static float TotalWeight(IReadOnlyList<PrefabEntry> entries)
        {
            float total = 0f;
            if (entries == null) return total;
            foreach (PrefabEntry entry in entries)
            {
                if (entry != null && entry.Prefab != null && entry.Weight > 0f) total += entry.Weight;
            }
            return total;
        }

        /// <summary>Picks an entry's index from a value in [0, 1), by weight. -1 when nothing can be picked.</summary>
        public static int PickIndex(IReadOnlyList<PrefabEntry> entries, float value)
        {
            float total = TotalWeight(entries);
            if (total <= 0f) return -1;
            float target = Mathf.Clamp01(value) * total;
            int last = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                PrefabEntry entry = entries[i];
                if (entry == null || entry.Prefab == null || entry.Weight <= 0f) continue;
                last = i;
                if (target < entry.Weight) return i;
                target -= entry.Weight;
            }
            return last;
        }
    }

    /// <summary>A named value a prefab entry writes onto its points.</summary>
    [Serializable]
    public class EntryAttribute
    {
        public string Name = "Attribute";
        public PointAttributeType Type = PointAttributeType.Float;
        public float FloatValue;
        public int IntValue;
        public Vector3 Vector3Value;
        public UnityEngine.Object ObjectValue;

        /// <summary>The value for <see cref="Type"/>, boxed.</summary>
        public object Value => Type switch
        {
            PointAttributeType.Float => FloatValue,
            PointAttributeType.Int => IntValue,
            PointAttributeType.Vector3 => Vector3Value,
            _ => ObjectValue
        };
    }
}
