using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// A weighted list of prefabs for the Spawn Prefabs and Select Prefab nodes. Each entry's chance is its weight
    /// divided by the total, and each entry can adjust where and how its prefab is placed (offset, rotation, scale) and
    /// add attributes to the points it is picked for. Create one with Assets > Create > UnityTools > Prefab Palette.
    /// </summary>
    [CreateAssetMenu(menuName = "UnityTools/Prefab Palette", fileName = "Prefab Palette")]
    public class PrefabPalette : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public GameObject Prefab;
            [Min(0f)] public float Weight = 1f;

            [Header("Adjustments")]
            [Tooltip("Moves this prefab from the point, in the point's local space (so it follows the point's rotation).")]
            public Vector3 PositionOffset = Vector3.zero;
            [Tooltip("Rotates this prefab, in Euler degrees, after the point's rotation.")]
            public Vector3 RotationOffset = Vector3.zero;
            [Tooltip("A random extra rotation per placed copy, in Euler degrees, between Rotation Random Min and Max.")]
            public Vector3 RotationRandomMin = Vector3.zero;
            public Vector3 RotationRandomMax = Vector3.zero;
            [Tooltip("Give this prefab a random uniform scale between Scale Min and Scale Max, on top of the point's scale.")]
            public bool RandomScale;
            [Min(0f)] public float ScaleMin = 1f;
            [Min(0f)] public float ScaleMax = 1f;

            [Tooltip("Attributes written onto the points this entry is picked for (by Select Prefab), e.g. for Filter By Attribute or Transform Points.")]
            public List<EntryAttribute> Attributes = new List<EntryAttribute>();

            /// <summary>
            /// This entry's adjustments for one placed copy: the offset, the rotation (offset plus a random value in the
            /// range, in Euler degrees) and the scale factor. Random values come from the point's and node's seeds.
            /// </summary>
            public void Sample(uint pointSeed, uint nodeSeed, out Vector3 offset, out Vector3 rotation, out float scale)
            {
                offset = PositionOffset;
                rotation = RotationOffset + new Vector3(
                    PlacementRandom.Range(pointSeed, nodeSeed, 12u, RotationRandomMin.x, RotationRandomMax.x),
                    PlacementRandom.Range(pointSeed, nodeSeed, 13u, RotationRandomMin.y, RotationRandomMax.y),
                    PlacementRandom.Range(pointSeed, nodeSeed, 14u, RotationRandomMin.z, RotationRandomMax.z));
                // Channel 11 is the one Random Scale used before the other adjustments existed.
                scale = RandomScale ? PlacementRandom.Range(pointSeed, nodeSeed, 11u, ScaleMin, ScaleMax) : 1f;
            }
        }

        /// <summary>A named value a palette entry writes onto its points.</summary>
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

        [SerializeField] private List<Entry> _entries = new List<Entry>();

        /// <summary>The entries. Entries with no prefab or a weight of 0 are never picked.</summary>
        public List<Entry> Entries => _entries;

        /// <summary>Sum of the weights of entries that can be picked.</summary>
        public float TotalWeight
        {
            get
            {
                float total = 0f;
                foreach (Entry entry in _entries)
                {
                    if (entry != null && entry.Prefab != null && entry.Weight > 0f) total += entry.Weight;
                }
                return total;
            }
        }

        /// <summary>Picks a prefab from a value in [0, 1), by weight. Null when nothing can be picked.</summary>
        public GameObject Pick(float value) => PickEntry(value)?.Prefab;

        /// <summary>Picks an entry from a value in [0, 1), by weight. Null when nothing can be picked.</summary>
        public Entry PickEntry(float value) => _entries.Count == 0 ? null : EntryAt(PickIndex(value));

        /// <summary>Picks an entry's index from a value in [0, 1), by weight. -1 when nothing can be picked.</summary>
        public int PickIndex(float value)
        {
            float total = TotalWeight;
            if (total <= 0f) return -1;
            float target = Mathf.Clamp01(value) * total;
            int last = -1;
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                if (entry == null || entry.Prefab == null || entry.Weight <= 0f) continue;
                last = i;
                if (target < entry.Weight) return i;
                target -= entry.Weight;
            }
            return last;
        }

        /// <summary>The entry at <paramref name="index"/>, or null when out of range.</summary>
        public Entry EntryAt(int index) => index >= 0 && index < _entries.Count ? _entries[index] : null;
    }
}
