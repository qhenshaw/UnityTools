using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>The value types a point attribute can have.</summary>
    public enum PointAttributeType
    {
        Float,
        Int,
        Vector3,
        Object
    }

    /// <summary>
    /// One named attribute of a point set: a value per point, stored in a list parallel to
    /// <see cref="PointData.Points"/>. A column can be shorter than the point list; missing values read as
    /// <see cref="PointAttribute{T}.Default"/>, so points added without values are still valid.
    /// </summary>
    public abstract class PointAttribute
    {
        /// <summary>The attribute's name, e.g. "Prefab".</summary>
        public string Name { get; }

        protected PointAttribute(string name) => Name = name;

        /// <summary>The attribute's value type.</summary>
        public abstract PointAttributeType Type { get; }

        /// <summary>Number of stored values (at most the number of points).</summary>
        public abstract int Count { get; }

        /// <summary>A point's value, boxed (for display and generic code).</summary>
        public abstract object GetBoxed(int index);

        /// <summary>An empty column with the same name, type and default.</summary>
        internal abstract PointAttribute CreateEmpty(int capacity);

        /// <summary>Appends <paramref name="source"/>'s value at <paramref name="index"/> (or the default) at <paramref name="at"/>.</summary>
        internal abstract void CopyValue(PointAttribute source, int index, int at);

        /// <summary>A copy with the same values.</summary>
        internal abstract PointAttribute Clone();

        /// <summary>Creates a column for a C# type: float, int, Vector3 or a UnityEngine.Object type.</summary>
        public static PointAttribute Create(string name, PointAttributeType type) => type switch
        {
            PointAttributeType.Float => new PointAttribute<float>(name, 0f),
            PointAttributeType.Int => new PointAttribute<int>(name, 0),
            PointAttributeType.Vector3 => new PointAttribute<Vector3>(name, Vector3.zero),
            _ => new PointAttribute<UnityEngine.Object>(name, null)
        };

        /// <summary>The attribute type for a C# type, if attributes can have it.</summary>
        public static bool TryGetType(Type type, out PointAttributeType attributeType)
        {
            if (type == typeof(float)) { attributeType = PointAttributeType.Float; return true; }
            if (type == typeof(int)) { attributeType = PointAttributeType.Int; return true; }
            if (type == typeof(Vector3)) { attributeType = PointAttributeType.Vector3; return true; }
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) { attributeType = PointAttributeType.Object; return true; }
            attributeType = default;
            return false;
        }
    }

    /// <summary>A typed attribute column. See <see cref="PointAttribute"/>.</summary>
    public sealed class PointAttribute<T> : PointAttribute
    {
        /// <summary>The stored values, by point index.</summary>
        public readonly List<T> Values;

        /// <summary>The value of points that have none stored.</summary>
        public readonly T Default;

        public PointAttribute(string name, T defaultValue, int capacity = 0) : base(name)
        {
            Default = defaultValue;
            Values = new List<T>(capacity);
        }

        public override PointAttributeType Type =>
            PointAttribute.TryGetType(typeof(T), out PointAttributeType type) ? type : PointAttributeType.Object;

        public override int Count => Values.Count;

        /// <summary>A point's value, or <see cref="Default"/> if none is stored.</summary>
        public T Get(int index) => index >= 0 && index < Values.Count ? Values[index] : Default;

        /// <summary>Sets a point's value, filling any gap before it with the default.</summary>
        public void Set(int index, T value)
        {
            while (Values.Count <= index) Values.Add(Default);
            Values[index] = value;
        }

        public override object GetBoxed(int index) => Get(index);

        internal override PointAttribute CreateEmpty(int capacity) => new PointAttribute<T>(Name, Default, capacity);

        internal override void CopyValue(PointAttribute source, int index, int at)
        {
            T value = source is PointAttribute<T> typed ? typed.Get(index) : Default;
            // Skip storing trailing defaults: a short column reads as defaults anyway.
            if (Values.Count == at) Values.Add(value);
            else Set(at, value);
        }

        internal override PointAttribute Clone()
        {
            var copy = new PointAttribute<T>(Name, Default, Values.Count);
            copy.Values.AddRange(Values);
            return copy;
        }
    }

    /// <summary>Well-known attribute names written by the built-in nodes.</summary>
    public static class PointAttributeNames
    {
        /// <summary>The prefab chosen for the point (Object). Spawn Prefabs spawns it.</summary>
        public const string Prefab = "Prefab";
        /// <summary>The palette the prefab was chosen from (Object).</summary>
        public const string Palette = "Palette";
        /// <summary>Index of the chosen palette entry (Int).</summary>
        public const string PaletteEntry = "PaletteEntry";
        /// <summary>The chosen entry's position offset, in the point's local space (Vector3).</summary>
        public const string EntryOffset = "EntryOffset";
        /// <summary>The chosen entry's rotation: its offset plus its random range, in Euler degrees (Vector3).</summary>
        public const string EntryRotation = "EntryRotation";
        /// <summary>The chosen entry's scale factor (Float).</summary>
        public const string EntryScale = "EntryScale";
    }
}
