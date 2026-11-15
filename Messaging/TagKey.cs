using System;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityTools.Messaging
{
    // An enum tag as a struct (enum type + numeric value), so dictionaries keyed by tag never box.
    // Built the same way from a generic enum (no allocation) or from an Enum reference (already boxed by the caller).
    internal readonly struct TagKey : IEquatable<TagKey>
    {
        public readonly Type Type;
        public readonly long Value;

        private TagKey(Type type, long value)
        {
            Type = type;
            Value = value;
        }

        public bool IsValid => Type != null;

        public static TagKey From(Enum tag)
        {
            if (tag == null) return default;
            return new TagKey(tag.GetType(), BoxedEnum.ToLong(tag));
        }

        public static TagKey From<TTag>(TTag tag) where TTag : struct, Enum => new TagKey(typeof(TTag), EnumBits<TTag>.ToLong(tag));

        public Enum ToEnum() => Type == null ? null : (Enum)Enum.ToObject(Type, Value);

        public bool Equals(TagKey other) => Type == other.Type && Value == other.Value;
        public override bool Equals(object obj) => obj is TagKey other && Equals(other);
        public override int GetHashCode() => unchecked(((Type != null ? Type.GetHashCode() : 0) * 397) ^ Value.GetHashCode());
        public override string ToString() => Type == null ? "(no tag)" : $"{Type.Name}.{ToEnum()}";
    }

    // Reads the numeric value of an already boxed enum without allocating (Convert.ToInt64 boxes it again internally).
    // A boxed value sits right after the object header, exactly where StrongBox<T> keeps its field, so the reference
    // can be read as a StrongBox of the underlying type. A self-check at startup falls back to Convert if that ever fails.
    internal static class BoxedEnum
    {
        private enum SelfCheckSigned : short { Value = -12345 }
        private enum SelfCheckUnsigned : uint { Value = 4000000000 }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, TypeCode> _typeCodes =
            new System.Collections.Concurrent.ConcurrentDictionary<Type, TypeCode>();
        private static readonly bool _canReadDirectly = SelfCheck();

        public static long ToLong(Enum tag)
        {
            Type type = tag.GetType();
            if (!_typeCodes.TryGetValue(type, out TypeCode code))
            {
                code = Type.GetTypeCode(Enum.GetUnderlyingType(type));
                _typeCodes.TryAdd(type, code);
            }
            return _canReadDirectly ? ReadDirect(tag, code) : ReadWithConvert(tag, code);
        }

        private static long ReadDirect(Enum tag, TypeCode code)
        {
            object boxed = tag;
            switch (code)
            {
                case TypeCode.SByte: return UnsafeUtility.As<object, System.Runtime.CompilerServices.StrongBox<sbyte>>(ref boxed).Value;
                case TypeCode.Byte: return UnsafeUtility.As<object, System.Runtime.CompilerServices.StrongBox<byte>>(ref boxed).Value;
                case TypeCode.Int16: return UnsafeUtility.As<object, System.Runtime.CompilerServices.StrongBox<short>>(ref boxed).Value;
                case TypeCode.UInt16: return UnsafeUtility.As<object, System.Runtime.CompilerServices.StrongBox<ushort>>(ref boxed).Value;
                case TypeCode.Int32: return UnsafeUtility.As<object, System.Runtime.CompilerServices.StrongBox<int>>(ref boxed).Value;
                case TypeCode.UInt32: return UnsafeUtility.As<object, System.Runtime.CompilerServices.StrongBox<uint>>(ref boxed).Value;
                default: return UnsafeUtility.As<object, System.Runtime.CompilerServices.StrongBox<long>>(ref boxed).Value;
            }
        }

        private static long ReadWithConvert(Enum tag, TypeCode code) =>
            code == TypeCode.UInt64 ? unchecked((long)Convert.ToUInt64(tag)) : Convert.ToInt64(tag);

        private static bool SelfCheck()
        {
            try
            {
                return ReadDirect(SelfCheckSigned.Value, TypeCode.Int16) == -12345
                    && ReadDirect(SelfCheckUnsigned.Value, TypeCode.UInt32) == 4000000000
                    && ReadDirect(DayOfWeek.Saturday, TypeCode.Int32) == (long)DayOfWeek.Saturday;
            }
            catch
            {
                return false;
            }
        }
    }

    // Reads an enum's numeric value without boxing, matching Convert.ToInt64 (signed types sign-extend).
    internal static class EnumBits<T> where T : struct, Enum
    {
        private static readonly int Size = UnsafeUtility.SizeOf<T>();
        private static readonly bool Signed = IsSigned(Type.GetTypeCode(Enum.GetUnderlyingType(typeof(T))));

        public static long ToLong(T value)
        {
            switch (Size)
            {
                case 1: return Signed ? UnsafeUtility.As<T, sbyte>(ref value) : UnsafeUtility.As<T, byte>(ref value);
                case 2: return Signed ? UnsafeUtility.As<T, short>(ref value) : UnsafeUtility.As<T, ushort>(ref value);
                case 4: return Signed ? UnsafeUtility.As<T, int>(ref value) : UnsafeUtility.As<T, uint>(ref value);
                default: return UnsafeUtility.As<T, long>(ref value);
            }
        }

        private static bool IsSigned(TypeCode code) =>
            code == TypeCode.SByte || code == TypeCode.Int16 || code == TypeCode.Int32 || code == TypeCode.Int64;
    }
}
