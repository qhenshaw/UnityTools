using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// A weighted list of prefabs for the Spawn Prefabs node. Each entry's chance is its weight divided by the total.
    /// Create one with Assets > Create > UnityTools > Prefab Palette.
    /// </summary>
    [CreateAssetMenu(menuName = "UnityTools/Prefab Palette", fileName = "Prefab Palette")]
    public class PrefabPalette : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public GameObject Prefab;
            [Min(0f)] public float Weight = 1f;
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
        public GameObject Pick(float value)
        {
            float total = TotalWeight;
            if (total <= 0f) return null;
            float target = Mathf.Clamp01(value) * total;
            GameObject last = null;
            foreach (Entry entry in _entries)
            {
                if (entry == null || entry.Prefab == null || entry.Weight <= 0f) continue;
                last = entry.Prefab;
                if (target < entry.Weight) return entry.Prefab;
                target -= entry.Weight;
            }
            return last;
        }
    }
}
