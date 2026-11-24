using System;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// Picks a prefab for every point from a palette, by weight, and records it as point attributes: Prefab, Palette
    /// and PaletteEntry. With Write Entry Values on it also writes the entry's adjustments (EntryOffset, EntryRotation,
    /// EntryScale) and its custom attributes. Spawn Prefabs then spawns the chosen prefab, so the points can be
    /// filtered or transformed per prefab in between.
    /// </summary>
    [Serializable]
    public class SelectPrefabNode : PlacementNode
    {
        public PrefabPalette Palette;
        [Tooltip("Also write the entry's offset, rotation and scale (sampled per point) and its custom attributes.")]
        public bool WriteEntryValues = true;

        public override int InputCount => 1;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            PointData result = PointData.Copy(inputs[0]);
            outputs[0] = result;
            if (Palette == null || Palette.TotalWeight <= 0f)
            {
                context.Warn(this, "No Prefab Palette, or the palette has no prefabs with a weight above 0.");
                return;
            }

            uint seed = PlacementRandom.Hash(context.SeedHash, NodeSeed);
            PointAttribute<UnityEngine.Object> prefabs = result.GetOrAddAttribute<UnityEngine.Object>(PointAttributeNames.Prefab);
            PointAttribute<UnityEngine.Object> palettes = result.GetOrAddAttribute<UnityEngine.Object>(PointAttributeNames.Palette);
            PointAttribute<int> indices = result.GetOrAddAttribute(PointAttributeNames.PaletteEntry, -1);
            PointAttribute<Vector3> offsets = null, rotations = null;
            PointAttribute<float> scales = null;
            if (WriteEntryValues)
            {
                offsets = result.GetOrAddAttribute<Vector3>(PointAttributeNames.EntryOffset);
                rotations = result.GetOrAddAttribute<Vector3>(PointAttributeNames.EntryRotation);
                scales = result.GetOrAddAttribute(PointAttributeNames.EntryScale, 1f);
            }

            for (int i = 0; i < result.Count; i++)
            {
                PlacementPoint point = result.Points[i];
                int index = Palette.PickIndex(PlacementRandom.Value(PlacementRandom.Hash(point.Seed, seed)));
                PrefabPalette.Entry entry = Palette.EntryAt(index);
                prefabs.Set(i, entry.Prefab);
                palettes.Set(i, Palette);
                indices.Set(i, index);
                if (!WriteEntryValues) continue;

                entry.Sample(point.Seed, seed, out Vector3 offset, out Vector3 rotation, out float scale);
                offsets.Set(i, offset);
                rotations.Set(i, rotation);
                scales.Set(i, scale);
                foreach (PrefabPalette.EntryAttribute attribute in entry.Attributes)
                {
                    if (attribute == null || string.IsNullOrEmpty(attribute.Name)) continue;
                    WriteValue(result, attribute.Name, attribute.Type, attribute.Value, i);
                }
            }
        }

        /// <summary>Writes a boxed value of <paramref name="type"/> into the attribute called <paramref name="name"/>.</summary>
        internal static void WriteValue(PointData data, string name, PointAttributeType type, object value, int index)
        {
            switch (type)
            {
                case PointAttributeType.Float: data.GetOrAddAttribute<float>(name).Set(index, (float)value); break;
                case PointAttributeType.Int: data.GetOrAddAttribute<int>(name).Set(index, (int)value); break;
                case PointAttributeType.Vector3: data.GetOrAddAttribute<Vector3>(name).Set(index, (Vector3)value); break;
                default: data.GetOrAddAttribute<UnityEngine.Object>(name).Set(index, value as UnityEngine.Object); break;
            }
        }
    }

    /// <summary>
    /// Filter: keeps points by an attribute's value. Numbers (and the length of a Vector3) are compared with Value
    /// (and Max for In Range); objects are compared with Object. Prefab Is keeps points whose Prefab attribute is
    /// Object, whatever Attribute says. Points without the attribute use its default (0, or none).
    /// </summary>
    [Serializable]
    public class FilterByAttributeNode : PlacementNode
    {
        public enum Comparison
        {
            Equal,
            NotEqual,
            Less,
            Greater,
            InRange,
            /// <summary>The point's Prefab attribute is <see cref="Object"/>.</summary>
            PrefabIs
        }

        public Comparison Compare = Comparison.PrefabIs;
        [Tooltip("The attribute to test (not used by Prefab Is).")]
        public string Attribute = "";
        [Tooltip("The number to compare with; the minimum for In Range.")]
        public float Value;
        [Tooltip("The maximum for In Range.")]
        public float Max = 1f;
        [Tooltip("The prefab or object Equal, Not Equal and Prefab Is compare object attributes with.")]
        public GameObject Object;

        public override int InputCount => 1;
        /// <summary>Output 0: kept. Output 1: rejected.</summary>
        public override int OutputCount => 2;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            PointData input = inputs[0];
            var kept = PointData.Derive(input, input.Count);
            var rejected = PointData.Derive(input);
            outputs[0] = kept;
            outputs[1] = rejected;

            string name = Compare == Comparison.PrefabIs ? PointAttributeNames.Prefab : Attribute;
            PointAttribute attribute = string.IsNullOrEmpty(name) ? null : input.GetAttribute(name);
            if (attribute == null && Compare != Comparison.PrefabIs && !string.IsNullOrEmpty(name))
                context.Warn(this, $"The points have no attribute called '{name}', so every point reads its default.");

            for (int i = 0; i < input.Count; i++)
                (Test(attribute, i) ? kept : rejected).AddFrom(input, i);
        }

        private bool Test(PointAttribute attribute, int index)
        {
            if (attribute is PointAttribute<UnityEngine.Object> || Compare == Comparison.PrefabIs)
            {
                UnityEngine.Object value = (attribute as PointAttribute<UnityEngine.Object>)?.Get(index);
                bool same = value == Object;
                return Compare switch
                {
                    Comparison.Equal or Comparison.PrefabIs => same,
                    Comparison.NotEqual => !same,
                    _ => false
                };
            }

            float number = attribute switch
            {
                PointAttribute<float> floats => floats.Get(index),
                PointAttribute<int> ints => ints.Get(index),
                PointAttribute<Vector3> vectors => vectors.Get(index).magnitude,
                _ => 0f
            };
            return Compare switch
            {
                Comparison.Equal => Mathf.Approximately(number, Value),
                Comparison.NotEqual => !Mathf.Approximately(number, Value),
                Comparison.Less => number < Value,
                Comparison.Greater => number > Value,
                Comparison.InRange => number >= Value && number <= Max,
                _ => false
            };
        }
    }

    /// <summary>
    /// Writes a value to a named attribute on every point: a constant (Min), or with Random on a seeded random value
    /// between Min and Max per point. The Float, Int or Vector3 settings are used, by Type.
    /// </summary>
    [Serializable]
    public class SetAttributeNode : PlacementNode
    {
        public enum ValueType
        {
            Float,
            Int,
            Vector3
        }

        public string Attribute = "Value";
        public ValueType Type = ValueType.Float;
        [Tooltip("A random value between Min and Max per point, instead of Min for every point.")]
        public bool Random;
        public float FloatMin;
        public float FloatMax = 1f;
        [Tooltip("Int range; Max is included.")]
        public int IntMin;
        public int IntMax = 1;
        public Vector3 Vector3Min = Vector3.zero;
        public Vector3 Vector3Max = Vector3.one;

        public override int InputCount => 1;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            PointData result = PointData.Copy(inputs[0]);
            outputs[0] = result;
            if (string.IsNullOrEmpty(Attribute))
            {
                context.Warn(this, "No attribute name.");
                return;
            }

            uint seed = PlacementRandom.Hash(context.SeedHash, NodeSeed);
            switch (Type)
            {
                case ValueType.Float:
                {
                    PointAttribute<float> column = result.GetOrAddAttribute<float>(Attribute);
                    for (int i = 0; i < result.Count; i++)
                        column.Set(i, Random ? PlacementRandom.Range(result.Points[i].Seed, seed, 1u, FloatMin, FloatMax) : FloatMin);
                    break;
                }
                case ValueType.Int:
                {
                    PointAttribute<int> column = result.GetOrAddAttribute<int>(Attribute);
                    for (int i = 0; i < result.Count; i++)
                    {
                        int value = IntMin;
                        if (Random && IntMax > IntMin)
                            value = Mathf.Min(IntMax, IntMin + Mathf.FloorToInt(PlacementRandom.Value(PlacementRandom.Hash(result.Points[i].Seed, seed, 1u)) * (IntMax - IntMin + 1)));
                        column.Set(i, value);
                    }
                    break;
                }
                default:
                {
                    PointAttribute<Vector3> column = result.GetOrAddAttribute<Vector3>(Attribute);
                    for (int i = 0; i < result.Count; i++)
                    {
                        uint s = result.Points[i].Seed;
                        column.Set(i, !Random ? Vector3Min : new Vector3(
                            PlacementRandom.Range(s, seed, 1u, Vector3Min.x, Vector3Max.x),
                            PlacementRandom.Range(s, seed, 2u, Vector3Min.y, Vector3Max.y),
                            PlacementRandom.Range(s, seed, 3u, Vector3Min.z, Vector3Max.z)));
                    }
                    break;
                }
            }
        }
    }
}
