using System;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>The value types a placement graph parameter can have.</summary>
    public enum PlacementParameterType
    {
        Float,
        Int,
        Bool,
        Vector3,
        Palette,
        LayerMask
    }

    /// <summary>
    /// A named value exposed by a graph (an Input variable in its Blackboard). A compiled graph stores each
    /// parameter's default; a Placement Graph component can override it per instance.
    /// </summary>
    [Serializable]
    public class PlacementParameter
    {
        public string Name;
        public PlacementParameterType Type;
        public float FloatValue;
        public int IntValue;
        public bool BoolValue;
        public Vector3 Vector3Value;
        public PrefabPalette PaletteValue;
        public LayerMask LayerMaskValue;

        public PlacementParameter() { }

        public PlacementParameter(string name, PlacementParameterType type, object value = null)
        {
            Name = name;
            Type = type;
            if (value != null) Value = value;
        }

        /// <summary>The value, boxed, for the parameter's <see cref="Type"/>.</summary>
        public object Value
        {
            get => Type switch
            {
                PlacementParameterType.Float => FloatValue,
                PlacementParameterType.Int => IntValue,
                PlacementParameterType.Bool => BoolValue,
                PlacementParameterType.Vector3 => Vector3Value,
                PlacementParameterType.Palette => PaletteValue,
                PlacementParameterType.LayerMask => LayerMaskValue,
                _ => null
            };
            set
            {
                switch (Type)
                {
                    case PlacementParameterType.Float: FloatValue = Convert.ToSingle(value); break;
                    case PlacementParameterType.Int: IntValue = Convert.ToInt32(value); break;
                    case PlacementParameterType.Bool: BoolValue = Convert.ToBoolean(value); break;
                    case PlacementParameterType.Vector3: Vector3Value = (Vector3)value; break;
                    case PlacementParameterType.Palette: PaletteValue = value as PrefabPalette; break;
                    case PlacementParameterType.LayerMask:
                        LayerMaskValue = value is LayerMask mask ? mask : (LayerMask)Convert.ToInt32(value);
                        break;
                }
            }
        }

        /// <summary>The C# type values of <paramref name="type"/> have.</summary>
        public static Type SystemTypeOf(PlacementParameterType type) => type switch
        {
            PlacementParameterType.Float => typeof(float),
            PlacementParameterType.Int => typeof(int),
            PlacementParameterType.Bool => typeof(bool),
            PlacementParameterType.Vector3 => typeof(Vector3),
            PlacementParameterType.Palette => typeof(PrefabPalette),
            PlacementParameterType.LayerMask => typeof(LayerMask),
            _ => null
        };

        /// <summary>The parameter type for a C# type, if parameters can have it.</summary>
        public static bool TryGetType(Type systemType, out PlacementParameterType type)
        {
            foreach (PlacementParameterType candidate in (PlacementParameterType[])Enum.GetValues(typeof(PlacementParameterType)))
            {
                if (SystemTypeOf(candidate) != systemType) continue;
                type = candidate;
                return true;
            }
            type = default;
            return false;
        }

        public PlacementParameter Clone() => (PlacementParameter)MemberwiseClone();
    }

    /// <summary>A node setting that reads a parameter: node index, the runtime node's field, and the parameter's name.</summary>
    [Serializable]
    public struct PlacementParameterBinding
    {
        public int Node;
        public string Field;
        public string Parameter;

        public PlacementParameterBinding(int node, string field, string parameter)
        {
            Node = node;
            Field = field;
            Parameter = parameter;
        }
    }

    /// <summary>A component's value for one of its graph's parameters, used when <see cref="Override"/> is on.</summary>
    [Serializable]
    public class PlacementParameterOverride
    {
        public bool Override;
        public PlacementParameter Value = new PlacementParameter();
    }
}
