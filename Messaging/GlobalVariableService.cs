using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace UnityTools.Messaging
{
    /// <summary>
    /// Stores global values by enum tag and tells listeners when a tag's value changes. Usually used through
    /// <see cref="GlobalVariable{T}"/> fields.
    /// </summary>
    // Values are kept in typed slots, so reading and writing value types doesn't box.
    // Thread-safe, because GlobalVariable constructors can run on Unity's loading thread.
    public class GlobalVariableService
    {
        private static readonly object _instanceLock = new object();
        private static GlobalVariableService _instance;

        /// <summary>The shared service. Created on first use (also in edit mode) and reset when Play starts.</summary>
        public static GlobalVariableService Instance
        {
            get
            {
                lock (_instanceLock)
                {
                    return _instance ??= new GlobalVariableService();
                }
            }
        }

        // True while the game is running. Kept in a field because Application.isPlaying can't be read off the main thread.
        internal static bool IsPlaying { get; set; }

        /// <summary>Raised whenever any value is set or removed, with the tag and the new value (null after a remove).</summary>
        public event Action<Enum, object> ValueChanged;

        private readonly object _lock = new object();
        private readonly Dictionary<TagKey, Slot> _slots = new Dictionary<TagKey, Slot>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlay()
        {
            lock (_instanceLock) _instance = null;
            IsPlaying = true;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void TrackPlayMode()
        {
            IsPlaying = UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode || state == UnityEditor.PlayModeStateChange.EnteredEditMode)
                    IsPlaying = false;
            };
        }
#endif

        internal static void ResetForTests()
        {
            lock (_instanceLock) _instance = null;
        }

        // ---- public API (Set, HasValue and Remove also have a generic overload that doesn't box the tag)

        /// <summary>The value stored under <paramref name="tag"/>, or the default value if it has none.</summary>
        public T Get<T>(Enum tag) => Get<T>(TagKey.From(tag));

        /// <summary>Stores <paramref name="value"/> under <paramref name="tag"/> and notifies its listeners.</summary>
        public void Set<T>(Enum tag, T value) => Set(TagKey.From(tag), value);
        public void Set<TTag, T>(TTag tag, T value) where TTag : struct, Enum => Set(TagKey.From(tag), value);

        /// <summary>True if <paramref name="tag"/> has a value (even a null one).</summary>
        public bool HasValue(Enum tag) => HasValue(TagKey.From(tag));
        public bool HasValue<TTag>(TTag tag) where TTag : struct, Enum => HasValue(TagKey.From(tag));

        /// <summary>Removes the value and notifies listeners with the default value (null / 0).</summary>
        public void Remove(Enum tag) => Remove(TagKey.From(tag));
        public void Remove<TTag>(TTag tag) where TTag : struct, Enum => Remove(TagKey.From(tag));

        /// <summary>Removes every value, notifying each tag's listeners with the default value.</summary>
        public void Clear()
        {
            var removed = new List<Slot>();
            lock (_lock)
            {
                foreach (Slot slot in _slots.Values)
                {
                    if (!slot.HasValue) continue;
                    slot.ClearValue();
                    removed.Add(slot);
                }
            }
            foreach (Slot slot in removed) Notify(slot, null);
        }

        // ---- key-based implementation (also used by GlobalVariable, which works out its key once)

        internal T Get<T>(TagKey key)
        {
            if (!CheckTag(key, "Get")) return default;
            Slot slot;
            lock (_lock)
            {
                if (!_slots.TryGetValue(key, out slot) || !slot.HasValue) return default;
                if (slot is Slot<T> typed) return typed.Value;
            }

            object value = slot.BoxedValue;
            if (value is T converted) return converted;
            if (value != null)
            {
                Debug.LogError($"GlobalVariableService: {key} holds {value.GetType().Name}, but was read as {typeof(T).Name}. Returning the default value.");
            }
            return default;
        }

        internal void Set<T>(TagKey key, T value)
        {
            if (!CheckTag(key, "Set")) return;
            Slot<T> slot;
            lock (_lock)
            {
                slot = GetOrCreateSlot<T>(key);
                slot.Value = value;
                slot.HasValue = true;
            }
            Notify(slot, slot);
        }

        internal bool HasValue(TagKey key)
        {
            if (!key.IsValid) return false;
            lock (_lock) return _slots.TryGetValue(key, out Slot slot) && slot.HasValue;
        }

        internal void Remove(TagKey key)
        {
            if (!key.IsValid) return;
            Slot slot;
            lock (_lock)
            {
                if (!_slots.TryGetValue(key, out slot) || !slot.HasValue) return;
                slot.ClearValue();
            }
            Notify(slot, null);
        }

        // Sets a starting value only if the tag has none yet. Raises no events (it can run on the loading thread).
        internal void SetDefault<T>(TagKey key, T value)
        {
            if (!key.IsValid) return;
            lock (_lock)
            {
                Slot<T> slot = GetOrCreateSlot<T>(key);
                if (slot.HasValue) return;
                slot.Value = value;
                slot.HasValue = true;
            }
        }

        internal void AddTagListener<T>(TagKey key, Action listener)
        {
            if (!key.IsValid || listener == null) return;
            lock (_lock) GetOrCreateSlot<T>(key).Listeners.Add(listener);
        }

        internal void RemoveTagListener(TagKey key, Action listener)
        {
            if (!key.IsValid || listener == null) return;
            lock (_lock)
            {
                if (_slots.TryGetValue(key, out Slot slot)) slot.Listeners.Remove(listener);
            }
        }

        internal bool TryGetBoxed(TagKey key, out object value)
        {
            lock (_lock)
            {
                if (key.IsValid && _slots.TryGetValue(key, out Slot slot) && slot.HasValue)
                {
                    value = slot.BoxedValue;
                    return true;
                }
            }
            value = null;
            return false;
        }

        // ---- debug info (used by the Messaging Debugger window)

        internal struct VariableInfo
        {
            public string Tag;
            public string ValueType;
            public bool HasValue;
            public string Value;
            public int Listeners;
        }

        internal void CollectDebugInfo(List<VariableInfo> into)
        {
            lock (_lock)
            {
                foreach (Slot slot in _slots.Values)
                {
                    into.Add(new VariableInfo
                    {
                        Tag = slot.Key.ToString(),
                        ValueType = slot.ValueType.Name,
                        HasValue = slot.HasValue,
                        Value = slot.HasValue ? MessagingService.DescribeValue(slot.BoxedValue) : "(unset)",
                        Listeners = slot.Listeners.Count,
                    });
                }
            }
        }

        // ---- internals

        // Must be called inside the lock. If the tag held another type, the slot is replaced and keeps its listeners.
        private Slot<T> GetOrCreateSlot<T>(TagKey key)
        {
            if (_slots.TryGetValue(key, out Slot existing))
            {
                if (existing is Slot<T> typed) return typed;
                var replacement = new Slot<T>(key, existing.Listeners);
                _slots[key] = replacement;
                return replacement;
            }

            var slot = new Slot<T>(key, new List<Action>());
            _slots.Add(key, slot);
            return slot;
        }

        // valueSource is the slot itself after Set (boxed only if ValueChanged has subscribers), or null after Remove.
        private void Notify(Slot slot, Slot valueSource)
        {
            using (ListPool<Action>.Get(out List<Action> listeners))
            {
                lock (_lock) listeners.AddRange(slot.Listeners);
                foreach (Action listener in listeners)
                {
                    try { listener(); }
                    catch (Exception exception) { Debug.LogException(exception, listener.Target as UnityEngine.Object); }
                }
            }

            Action<Enum, object> valueChanged = ValueChanged;
            if (valueChanged == null) return;
            Enum tag = slot.Tag;
            object value = valueSource?.BoxedValue;
            foreach (Delegate handler in valueChanged.GetInvocationList())
            {
                try { ((Action<Enum, object>)handler)(tag, value); }
                catch (Exception exception) { Debug.LogException(exception, handler.Target as UnityEngine.Object); }
            }
        }

        private static bool CheckTag(TagKey key, string action)
        {
            if (key.IsValid) return true;
            Debug.LogError($"GlobalVariableService: {action} was called without a tag.");
            return false;
        }

        private abstract class Slot
        {
            public readonly TagKey Key;
            public readonly List<Action> Listeners;
            public bool HasValue;
            private Enum _tag;

            protected Slot(TagKey key, List<Action> listeners)
            {
                Key = key;
                Listeners = listeners;
            }

            public Enum Tag => _tag ??= Key.ToEnum();
            public abstract Type ValueType { get; }
            public abstract object BoxedValue { get; }
            public abstract void ClearValue();
        }

        private sealed class Slot<T> : Slot
        {
            public T Value;

            public Slot(TagKey key, List<Action> listeners) : base(key, listeners) { }

            public override Type ValueType => typeof(T);
            public override object BoxedValue => Value;

            public override void ClearValue()
            {
                Value = default;
                HasValue = false;
            }
        }
    }
}
