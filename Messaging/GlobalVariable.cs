using UnityEngine;
using System;

namespace UnityTools.Messaging
{
    // A typed handle to one global value. Every GlobalVariable with the same tag shares the value,
    // and OnUpdated fires on all of them whenever it is set or removed (through any of them or GlobalVariableService).
    [Serializable]
    public class GlobalVariable<T>
    {
        public Enum Tag { get; private set; }
        [SerializeField] private string _tag;
        [SerializeField, HideInInspector] private string _tagType;
        [SerializeField] private T _value;

        // worked out once from Tag, so Value get/set never boxes the tag
        [NonSerialized] private TagKey _key;
        [NonSerialized] private EventHandler<T> _onUpdated;
        [NonSerialized] private Action _serviceListener;
        [NonSerialized] private GlobalVariableService _subscribedService;

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

        // False when the tag has no value, the value is null, or it is a destroyed Unity object.
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

        public GlobalVariable(Enum tag)
        {
            SetTag(tag);
        }

        // value is a starting value: it is only used if the tag has no value yet, so later instances can't reset it.
        // Nothing is written outside Play mode.
        public GlobalVariable(Enum tag, T value)
        {
            SetTag(tag);
            _value = value;
            if (GlobalVariableService.IsPlaying) GlobalVariableService.Instance.SetDefault(_key, value);
        }

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
