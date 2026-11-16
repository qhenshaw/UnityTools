using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace UnityTools.Messaging
{
    /// <summary>
    /// Sends messages between objects and scenes, using any enum value as the tag.
    /// Each tag carries one payload type, set by its first typed listener. No-payload listeners can listen to any tag.
    /// </summary>
    /// <remarks>
    /// Every method has a generic overload (TTag : struct, Enum) that the compiler picks when you write no type
    /// arguments (or both), so the tag isn't boxed.
    /// </remarks>
    public class MessagingService
    {
        private static MessagingService _instance;

        /// <summary>The shared service. Created on first use (also in edit mode) and reset when Play starts.</summary>
        public static MessagingService Instance => _instance ??= new MessagingService();

        private readonly Dictionary<TagKey, TagEntry> _tags = new Dictionary<TagKey, TagEntry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlay() => _instance = null;

        internal static void ResetForTests() => _instance = null;

        // ---- typed listeners

        /// <summary>
        /// Calls <paramref name="listenerMethod"/> with the payload of every later send to <paramref name="tag"/>.
        /// The first typed listener sets the tag's payload type; a listener for another type logs an error and isn't added.
        /// </summary>
        public void AddListener<T>(Enum tag, Action<T> listenerMethod) => AddTyped(TagKey.From(tag), listenerMethod);
        public void AddListener<TTag, T>(TTag tag, Action<T> listenerMethod) where TTag : struct, Enum => AddTyped(TagKey.From(tag), listenerMethod);

        /// <summary>Removes a listener added with AddListener. Pass the same method, e.g. from OnDisable.</summary>
        public void RemoveListener<T>(Enum tag, Action<T> toRemove) => Remove(TagKey.From(tag), toRemove);
        public void RemoveListener<TTag, T>(TTag tag, Action<T> toRemove) where TTag : struct, Enum => Remove(TagKey.From(tag), toRemove);

        // ---- no-payload listeners: called on every send to the tag, whatever the payload

        /// <summary>Calls <paramref name="listenerMethod"/> on every later send to <paramref name="tag"/>, whatever the payload.</summary>
        public void AddListener(Enum tag, Action listenerMethod) => AddUntyped(TagKey.From(tag), listenerMethod);
        public void AddListener<TTag>(TTag tag, Action listenerMethod) where TTag : struct, Enum => AddUntyped(TagKey.From(tag), listenerMethod);

        /// <summary>Removes a no-payload listener added with AddListener.</summary>
        public void RemoveListener(Enum tag, Action toRemove) => Remove(TagKey.From(tag), toRemove);
        public void RemoveListener<TTag>(TTag tag, Action toRemove) where TTag : struct, Enum => Remove(TagKey.From(tag), toRemove);

        // ---- sending

        /// <summary>
        /// Sends <paramref name="payload"/> to every listener of <paramref name="tag"/>. With <paramref name="sendToLate"/>,
        /// the payload is also remembered and given to listeners added later, until <see cref="ClearSticky(Enum)"/>.
        /// Once a tag has a remembered payload, every later send to it replaces it.
        /// </summary>
        public void Send<T>(Enum tag, T payload, bool sendToLate = false) => SendTyped(TagKey.From(tag), payload, sendToLate);
        public void Send<TTag, T>(TTag tag, T payload, bool sendToLate = false) where TTag : struct, Enum => SendTyped(TagKey.From(tag), payload, sendToLate);

        /// <summary>Sends without a payload. Only valid on tags that have no typed listeners; otherwise it logs an error.</summary>
        public void Send(Enum tag) => SendUntyped(TagKey.From(tag));
        public void Send<TTag>(TTag tag) where TTag : struct, Enum => SendUntyped(TagKey.From(tag));

        /// <summary>Forgets the payload remembered by a <c>sendToLate</c> send, so listeners added later don't get it.</summary>
        public void ClearSticky(Enum tag) => ClearSticky(TagKey.From(tag));
        public void ClearSticky<TTag>(TTag tag) where TTag : struct, Enum => ClearSticky(TagKey.From(tag));

        // ---- implementation

        private void AddTyped<T>(TagKey key, Action<T> listener)
        {
            if (!CheckTag(key, "AddListener") || listener == null) return;
            TagEntry entry = GetOrCreate(key);

            if (entry.Typed is Channel<T> typed)
            {
                typed.Listeners.Add(listener);
            }
            else if (entry.Typed != null && entry.Typed.Count > 0)
            {
                Debug.LogError($"MessagingService: {entry.Name} already has listeners for {entry.Typed.PayloadType.Name}, " +
                               $"so the listener {ListenerList<Action<T>>.Describe(listener)} for {typeof(T).Name} was not added. Each tag carries one payload type.",
                               listener.Target as UnityEngine.Object);
                return;
            }
            else
            {
                // a new tag, or one whose typed listeners were all removed: this listener sets its payload type
                var channel = new Channel<T>();
                channel.Listeners.Add(listener);
                entry.Typed = channel;
            }

            if (entry.HasSticky && !entry.Sticky.IsDestroyed && entry.Sticky.TryGet(out T value))
            {
                Call(listener, value);
            }
        }

        private void AddUntyped(TagKey key, Action listener)
        {
            if (!CheckTag(key, "AddListener") || listener == null) return;
            TagEntry entry = GetOrCreate(key);
            entry.Untyped.Add(listener);
            if (entry.HasSticky && !entry.Sticky.IsDestroyed) Call(listener);
        }

        private void Remove(TagKey key, Delegate listener)
        {
            if (!key.IsValid || listener == null || !_tags.TryGetValue(key, out TagEntry entry)) return;
            if (entry.Typed != null && entry.Typed.Remove(listener)) return;
            entry.Untyped.Remove(listener);
        }

        private void SendTyped<T>(TagKey key, T payload, bool sendToLate)
        {
            if (!CheckTag(key, "Send")) return;
            if (!_tags.TryGetValue(key, out TagEntry entry))
            {
                if (!sendToLate) return;
                entry = GetOrCreate(key);
            }

            Channel typed = entry.Typed != null && entry.Typed.Count > 0 ? entry.Typed : null;
            if (typed != null && !typed.Accepts(payload))
            {
                string sent = payload == null ? $"null ({typeof(T).Name})" : payload.GetType().Name;
                Debug.LogError($"MessagingService: {entry.Name} listeners expect {typed.PayloadType.Name}, but Send got {sent}. Nothing was delivered.");
                return;
            }

            if (sendToLate || entry.HasSticky)
            {
                // reuse the holder when the payload type is unchanged, so remembering a struct doesn't box it
                if (entry.Sticky is StickyValue<T> holder) holder.Value = payload;
                else entry.Sticky = new StickyValue<T> { Value = payload };
                entry.HasSticky = true;
            }

            typed?.Deliver(payload, entry.Name);
            entry.Untyped.Invoke(0, static (listener, _) => listener(), entry.Name);
        }

        private void SendUntyped(TagKey key)
        {
            if (!CheckTag(key, "Send") || !_tags.TryGetValue(key, out TagEntry entry)) return;
            if (entry.Typed != null && entry.Typed.Count > 0)
            {
                Debug.LogError($"MessagingService: {entry.Name} listeners expect {entry.Typed.PayloadType.Name}, but Send was called without a payload. Nothing was delivered.");
                return;
            }
            entry.Untyped.Invoke(0, static (listener, _) => listener(), entry.Name);
        }

        private void ClearSticky(TagKey key)
        {
            if (!key.IsValid || !_tags.TryGetValue(key, out TagEntry entry)) return;
            entry.HasSticky = false;
            entry.Sticky = null;
        }

        private TagEntry GetOrCreate(TagKey key)
        {
            if (!_tags.TryGetValue(key, out TagEntry entry))
            {
                entry = new TagEntry(key);
                _tags.Add(key, entry);
            }
            return entry;
        }

        private static bool CheckTag(TagKey key, string action)
        {
            if (key.IsValid) return true;
            Debug.LogError($"MessagingService: {action} was called without a tag.");
            return false;
        }

        private static bool TryConvert<T>(object payload, out T value)
        {
            if (payload is T typed) { value = typed; return true; }
            value = default;
            return payload == null && default(T) == null;
        }

        private static void Call<T>(Action<T> listener, T value)
        {
            try { listener(value); }
            catch (Exception exception) { Debug.LogException(exception, listener.Target as UnityEngine.Object); }
        }

        private static void Call(Action listener)
        {
            try { listener(); }
            catch (Exception exception) { Debug.LogException(exception, listener.Target as UnityEngine.Object); }
        }

        // ---- debug info (used by the Messaging Debugger window)

        internal struct TagInfo
        {
            public string Tag;
            public string PayloadType;
            public List<string> Listeners;
            public bool HasSticky;
            public string StickyPayload;
        }

        internal void CollectDebugInfo(List<TagInfo> into)
        {
            foreach (TagEntry entry in _tags.Values)
            {
                var listeners = new List<string>();
                entry.Typed?.Describe(listeners);
                int typedCount = listeners.Count;
                entry.Untyped.Describe(listeners);
                for (int i = typedCount; i < listeners.Count; i++) listeners[i] += " (no payload)";

                into.Add(new TagInfo
                {
                    Tag = entry.Name,
                    PayloadType = entry.Typed != null && entry.Typed.Count > 0 ? entry.Typed.PayloadType.Name : "-",
                    Listeners = listeners,
                    HasSticky = entry.HasSticky,
                    StickyPayload = entry.HasSticky ? DescribeValue(entry.Sticky.Boxed) : null,
                });
            }
        }

        internal static string DescribeValue(object value)
        {
            if (value is UnityEngine.Object unityObject) return unityObject != null ? $"{unityObject.name} ({unityObject.GetType().Name})" : "destroyed object";
            return value == null ? "null" : value.ToString();
        }

        private sealed class TagEntry
        {
            public readonly string Name;
            public readonly ListenerList<Action> Untyped = new ListenerList<Action>();
            public Channel Typed;
            public bool HasSticky;
            public StickyValue Sticky;

            public TagEntry(TagKey key) => Name = key.ToString();
        }

        // The remembered payload of a sticky tag, kept typed so updating it doesn't box structs.
        private abstract class StickyValue
        {
            public abstract object Boxed { get; }
            public bool IsDestroyed => Boxed is UnityEngine.Object unityObject && unityObject == null;
            public abstract bool TryGet<TListener>(out TListener value);
        }

        private sealed class StickyValue<T> : StickyValue
        {
            public T Value;

            public override object Boxed => Value;

            public override bool TryGet<TListener>(out TListener value)
            {
                if (typeof(T) == typeof(TListener))
                {
                    T copy = Value;
                    value = UnsafeUtility.As<T, TListener>(ref copy);
                    return true;
                }
                return TryConvert(Value, out value);
            }
        }

        private abstract class Channel
        {
            public abstract Type PayloadType { get; }
            public abstract int Count { get; }
            public abstract bool Accepts<TSent>(TSent payload);
            public abstract void Deliver<TSent>(TSent payload, string tagName);
            public abstract bool Remove(Delegate listener);
            public abstract void Describe(List<string> into);
        }

        private sealed class Channel<TPayload> : Channel
        {
            public readonly ListenerList<Action<TPayload>> Listeners = new ListenerList<Action<TPayload>>();

            public override Type PayloadType => typeof(TPayload);
            public override int Count => Listeners.Count;
            public override bool Remove(Delegate listener) => Listeners.Remove(listener);
            public override void Describe(List<string> into) => Listeners.Describe(into);

            public override bool Accepts<TSent>(TSent payload) => TryGet(payload, out _);

            public override void Deliver<TSent>(TSent payload, string tagName)
            {
                if (!TryGet(payload, out TPayload value)) return;
                Listeners.Invoke(value, static (listener, v) => listener(v), tagName);
            }

            // The payload fits when it is the same type (no boxing), a derived type, or null for a reference type.
            private static bool TryGet<TSent>(TSent payload, out TPayload value)
            {
                if (typeof(TSent) == typeof(TPayload))
                {
                    value = UnsafeUtility.As<TSent, TPayload>(ref payload);
                    return true;
                }
                if (payload is TPayload typed) { value = typed; return true; }
                value = default;
                return payload == null && default(TPayload) == null;
            }
        }
    }
}
