using Sirenix.OdinInspector;
using UnityEngine;
using System;

namespace UnityTools.Messaging
{
    [Serializable, InlineProperty, HideLabel]
    public class GlobalVariable<T>
    {
        [field: SerializeField, HorizontalGroup("Box/Horiz"), BoxGroup("Box", ShowLabel = false), HideLabel, HideInInspector] public Enum Tag { get; private set; }
        [field: SerializeField, HorizontalGroup("Box/Horiz"), BoxGroup("Box", ShowLabel = false), LabelText("@_label", SdfIconType.Link45deg)] private T _value;

        private string _label => $"Global: {Tag}";

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

        public bool HasValue => GlobalVariableService.Instance.HasValue(Tag);

        public GlobalVariable(Enum tag)
        {
            Tag = tag;
        }

        public GlobalVariable(Enum tag, T value)
        {
            Tag = tag;
            _value = value;
        }

        public event EventHandler<T> OnUpdated;
    }
}