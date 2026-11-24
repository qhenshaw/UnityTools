using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// A building module for Subdivide: a prefab and the symbol the grammar names it by (Wall, Door, Window...). In a
    /// graph each module is a Module node chained into Subdivide's Modules input.
    /// </summary>
    [Serializable]
    public class ModuleDefinition
    {
        public GameObject Prefab;
        [Tooltip("The name the grammar uses for this module, e.g. Wall.")]
        public string Symbol = "Wall";
        [Tooltip("For a choice like [Wall,Window]: this module's chance is its weight divided by the others' total.")]
        [Min(0f)] public float Weight = 1f;
        [Tooltip("The module's length along the subdivision axis, in metres. 0 measures it from the prefab's meshes.")]
        [Min(0f)] public float Size;
        [Tooltip("Moves the module from its slot, in the slot's local space.")]
        public Vector3 PositionOffset = Vector3.zero;
        [Tooltip("Rotates the module, in Euler degrees, after the slot's rotation.")]
        public Vector3 RotationOffset = Vector3.zero;

        /// <summary>The module's length along <paramref name="axis"/> (0 = X, 2 = Z): <see cref="Size"/>, or measured from its meshes.</summary>
        public float LengthAlong(int axis)
        {
            if (Size > 0f) return Size;
            Bounds bounds = MeasureBounds(Prefab);
            return Mathf.Max(0.01f, bounds.size[axis]);
        }

        /// <summary>The bounds of every mesh in <paramref name="prefab"/>, in the prefab root's space.</summary>
        public static Bounds MeasureBounds(GameObject prefab)
        {
            if (prefab == null) return new Bounds(Vector3.zero, Vector3.one);
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            bool any = false;
            var bounds = new Bounds();
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Matrix4x4 matrix = toRoot * filter.transform.localToWorldMatrix;
                Bounds local = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = local.center + Vector3.Scale(local.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    Vector3 rooted = matrix.MultiplyPoint3x4(point);
                    if (!any) { bounds = new Bounds(rooted, Vector3.zero); any = true; }
                    else bounds.Encapsulate(rooted);
                }
            }
            return any ? bounds : new Bounds(Vector3.zero, Vector3.one);
        }
    }

    /// <summary>
    /// A Subdivide grammar: a sequence of terms like <c>[Corner][Wall]*[Door][Wall]*[Corner]</c>. Each term names one or
    /// more module symbols in brackets (several separated by commas are a weighted choice, picked per slot) and may end
    /// with * (repeat to fill, zero or more) or + (repeat to fill, at least one).
    /// </summary>
    public sealed class ModuleGrammar
    {
        public enum Repeat
        {
            Once,
            ZeroOrMore,
            OneOrMore
        }

        public sealed class Term
        {
            public readonly string[] Symbols;
            public readonly Repeat Repeat;

            public Term(string[] symbols, Repeat repeat)
            {
                Symbols = symbols;
                Repeat = repeat;
            }

            public override string ToString() => $"[{string.Join(",", Symbols)}]{(Repeat == Repeat.ZeroOrMore ? "*" : Repeat == Repeat.OneOrMore ? "+" : "")}";
        }

        public readonly List<Term> Terms = new List<Term>();

        /// <summary>Parses <paramref name="text"/>. Returns false with an explanation in <paramref name="error"/> if it isn't valid.</summary>
        public static bool TryParse(string text, out ModuleGrammar grammar, out string error)
        {
            grammar = new ModuleGrammar();
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "The grammar is empty. Write terms like [Wall]* or [Corner][Wall]*[Corner].";
                return false;
            }
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c != '[')
                {
                    error = $"Expected '[' at position {i + 1} but found '{c}'.";
                    return false;
                }
                int close = text.IndexOf(']', i + 1);
                if (close < 0)
                {
                    error = $"The '[' at position {i + 1} has no matching ']'.";
                    return false;
                }
                string inside = text.Substring(i + 1, close - i - 1);
                var symbols = new List<string>();
                foreach (string part in inside.Split(','))
                {
                    string symbol = part.Trim();
                    if (symbol.Length == 0)
                    {
                        error = $"Empty symbol in '[{inside}]' at position {i + 1}.";
                        return false;
                    }
                    if (symbol.IndexOfAny(new[] { '[', '*', '+' }) >= 0)
                    {
                        error = $"'{symbol}' isn't a valid symbol (at position {i + 1}).";
                        return false;
                    }
                    symbols.Add(symbol);
                }
                i = close + 1;
                Repeat repeat = Repeat.Once;
                if (i < text.Length && text[i] == '*') { repeat = Repeat.ZeroOrMore; i++; }
                else if (i < text.Length && text[i] == '+') { repeat = Repeat.OneOrMore; i++; }
                grammar.Terms.Add(new Term(symbols.ToArray(), repeat));
            }
            return true;
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            foreach (Term term in Terms) builder.Append(term);
            return builder.ToString();
        }

        /// <summary>One module placed along a segment: which module, where its slot starts and how long the slot is.</summary>
        public struct Slot
        {
            public int Module;
            public float Start;
            public float Length;
            /// <summary>The module's own length times this is the slot length.</summary>
            public float Scale;
        }

        /// <summary>
        /// Fits the grammar to a segment <paramref name="length"/> long. Repeated terms share the space left by the
        /// single ones, as many modules as fit (rounded); with <paramref name="scaleToFit"/> every slot is scaled so the
        /// modules close the segment exactly, otherwise they keep their size and the gap is left at the end. Choices are
        /// picked with <paramref name="random"/> (a value in [0, 1) per pick, deterministic for the caller). Returns
        /// false with a reason when a symbol has no module.
        /// </summary>
        public bool TryFit(float length, IReadOnlyList<ModuleDefinition> modules, int axis, bool scaleToFit,
            Func<int, float> random, List<Slot> slots, out string error)
        {
            slots.Clear();
            error = null;
            int pick = 0;
            var sizes = new float[modules.Count];
            for (int m = 0; m < modules.Count; m++) sizes[m] = modules[m] != null && modules[m].Prefab != null ? modules[m].LengthAlong(axis) : 0f;

            int Choose(Term term)
            {
                float total = 0f;
                var candidates = new List<int>();
                foreach (string symbol in term.Symbols)
                {
                    for (int m = 0; m < modules.Count; m++)
                    {
                        if (modules[m] == null || modules[m].Prefab == null || modules[m].Symbol != symbol || modules[m].Weight <= 0f) continue;
                        candidates.Add(m);
                        total += modules[m].Weight;
                    }
                }
                if (candidates.Count == 0) return -1;
                float target = random(pick++) * total;
                foreach (int m in candidates)
                {
                    if (target < modules[m].Weight) return m;
                    target -= modules[m].Weight;
                }
                return candidates[candidates.Count - 1];
            }

            // The single terms first: they always get their module.
            var sequence = new List<List<int>>();
            float fixedLength = 0f;
            int repeats = 0;
            foreach (Term term in Terms)
            {
                var run = new List<int>();
                if (term.Repeat == Repeat.Once)
                {
                    int module = Choose(term);
                    if (module < 0) { error = $"No module has the symbol {string.Join(" or ", term.Symbols)}."; return false; }
                    run.Add(module);
                    fixedLength += sizes[module];
                }
                else repeats++;
                sequence.Add(run);
            }

            // Then the repeats share what's left, each filling its share with picks until the next one would overflow.
            float share = repeats > 0 ? Mathf.Max(0f, length - fixedLength) / repeats : 0f;
            for (int t = 0; t < Terms.Count; t++)
            {
                Term term = Terms[t];
                if (term.Repeat == Repeat.Once) continue;
                float used = 0f;
                for (int guard = 0; guard < 10000; guard++)
                {
                    int module = Choose(term);
                    if (module < 0) { error = $"No module has the symbol {string.Join(" or ", term.Symbols)}."; return false; }
                    float size = sizes[module];
                    bool mustHaveOne = term.Repeat == Repeat.OneOrMore && sequence[t].Count == 0;
                    // Round to the nearest count: take the module if at least half of it fits.
                    if (!mustHaveOne && used + size * 0.5f > share) break;
                    sequence[t].Add(module);
                    used += size;
                }
            }

            float natural = 0f;
            foreach (List<int> run in sequence) foreach (int module in run) natural += sizes[module];
            float scale = scaleToFit && natural > 0f ? length / natural : 1f;
            float cursor = 0f;
            foreach (List<int> run in sequence)
            {
                foreach (int module in run)
                {
                    float slotLength = sizes[module] * scale;
                    slots.Add(new Slot { Module = module, Start = cursor, Length = slotLength, Scale = scale });
                    cursor += slotLength;
                }
            }
            return true;
        }
    }
}
