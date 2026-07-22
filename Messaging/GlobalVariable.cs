using UnityEngine;
using System;

namespace UnityTools.Messaging
{
    [Serializable]
    public class GlobalVariable<T>
    {
        public Enum Tag { get; private set; }
        [SerializeField] private string _tag;
        [field: SerializeField] private T _value;

        public T Value
        {
            get
            {
                T value = GlobalVariableService.Instance.Get<T>(Tag);
                _value = value;
                return value;
            }
            set
            {
                _value = value;
                GlobalVariableService.Instance.Set(Tag, value);
                OnUpdated?.Invoke(this, _value);
            }
        }

        public bool HasValue => GlobalVariableService.Instance.HasValue(Tag) && !Value.Equals(null);

        public GlobalVariable(Enum tag)
        {
            Tag = tag;
            _tag = tag.ToString();
        }

        public GlobalVariable(Enum tag, T value)
        {
            Tag = tag;
            Value = value;
            _tag = tag.ToString();
        }

        public event EventHandler<T> OnUpdated;
    }
}