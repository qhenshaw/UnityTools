using UnityEngine;
using System;

namespace UnityTools.Messaging
{
    /// <summary>
    /// A typed handle to one global value, found by an enum tag. Every GlobalVariable with the same tag shares the value,
    /// and OnUpdated fires on all of them whenever it is set or removed (through any of them or GlobalVariableService).
    /// </summary>
    [Serializable]
    public class GlobalVariable<T>
    {
        /// <summary>The enum value this variable is stored under.</summary>
        public Enum Tag { get; private set; }
        [SerializeField] private string _tag;
        [SerializeField, HideInInspector] private string _tagType;
        [SerializeField] private T _value;

        // worked out once from Tag, so Value get/set never boxes the tag
        [NonSerialized] private TagKey _key;
        [NonSerialized] private EventHandler<T> _onUpdated;
        [NonSerialized] private Action _serviceListener;
        [NonSerialized] private GlobalVariableService _subscribedService;

        /// <summary>The shared value for this tag (default if unset). Setting it notifies every GlobalVariable with the tag.</summary>
        public T Value
        {
            get
            {
                T value = GlobalVariableService.Instance.Get<T>(_key);
                _value = value;
                return value;
            }
            set
            {
                _value = value;
                GlobalVariableService.Instance.Set(_key, value);
            }
        }

        /// <summary>False when the tag has no value, the value is null, or it is a destroyed Unity object.</summary>
        public bool HasValue
        {
            get
            {
                if (!GlobalVariableService.Instance.HasValue(_key)) return false;
                T value = Value;
                if (value is UnityEngine.Object unityObject) return unityObject != null;
                return value != null;
            }
        }

        /// <summary>Creates a handle to the global value stored under <paramref name="tag"/>.</summary>
        public GlobalVariable(Enum tag)
        {
            SetTag(tag);
        }

        /// <summary>
        /// Creates a handle with a starting value. <paramref name="value"/> is only used if the tag has no value yet,
        /// so later instances can't reset it. Nothing is written outside Play mode.
        /// </summary>
        public GlobalVariable(Enum tag, T value)
        {
            SetTag(tag);
            _value = value;
            if (GlobalVariableService.IsPlaying) GlobalVariableService.Instance.SetDefault(_key, value);
        }

        /// <summary>
        /// Raised with the new value whenever the tag's value is set or removed (default value after a remove).
        /// Unsubscribe in OnDisable.
        /// </summary>
        // Subscribes to the service only while something listens, so unused variables cost nothing and can't leak.
        public event EventHandler<T> OnUpdated
        {
            add
            {
                _onUpdated += value;
                SubscribeToService();
            }
            remove
            {
                _onUpdated -= value;
                if (_onUpdated == null) UnsubscribeFromService();
            }
        }

        private void SetTag(Enum tag)
        {
            Tag = tag;
            _key = TagKey.From(tag);
            _tag = tag?.ToString();
            _tagType = tag?.GetType().AssemblyQualifiedName;
        }

        private void SubscribeToService()
        {
            GlobalVariableService service = GlobalVariableService.Instance;
            if (_subscribedService == service) return;
            UnsubscribeFromService();
            _serviceListener ??= OnServiceValueChanged;
            service.AddTagListener<T>(_key, _serviceListener);
            _subscribedService = service;
        }

        private void UnsubscribeFromService()
        {
            if (_subscribedService == null) return;
            _subscribedService.RemoveTagListener(_key, _serviceListener);
            _subscribedService = null;
        }

        private void OnServiceValueChanged()
        {
            T value = GlobalVariableService.Instance.Get<T>(_key);
            _value = value;
            _onUpdated?.Invoke(this, value);
        }
    }
}
