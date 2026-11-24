using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// Obsolete: a weighted list of prefabs, kept only so Tools > UnityTools > Convert Placement Graph Palettes can copy
    /// its entries into Prefab Entry nodes. Graphs now list their prefabs with Prefab Entry nodes.
    /// </summary>
    public class PrefabPalette : ScriptableObject
    {
        /// <summary>A palette entry: the same values as a <see cref="PrefabEntry"/>.</summary>
        [Serializable]
        public class Entry : PrefabEntry { }

        [SerializeField] private List<Entry> _entries = new List<Entry>();

        /// <summary>The entries. Entries with no prefab or a weight of 0 are never picked.</summary>
        public List<Entry> Entries => _entries;

        /// <summary>Sum of the weights of entries that can be picked.</summary>
        public float TotalWeight => PrefabEntry.TotalWeight(_entries);

        /// <summary>Picks an entry's index from a value in [0, 1), by weight. -1 when nothing can be picked.</summary>
        public int PickIndex(float value) => PrefabEntry.PickIndex(_entries, value);

        /// <summary>Picks an entry from a value in [0, 1), by weight. Null when nothing can be picked.</summary>
        public Entry PickEntry(float value) => EntryAt(PickIndex(value));

        /// <summary>Picks a prefab from a value in [0, 1), by weight. Null when nothing can be picked.</summary>
        public GameObject Pick(float value) => PickEntry(value)?.Prefab;

        /// <summary>The entry at <paramref name="index"/>, or null when out of range.</summary>
        public Entry EntryAt(int index) => index >= 0 && index < _entries.Count ? _entries[index] : null;
    }
}
