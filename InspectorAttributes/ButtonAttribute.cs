using System;
using UnityEngine;

namespace InspectorAttributes
{
    [AttributeUsage(AttributeTargets.Field)]
    public class ButtonAttribute : PropertyAttribute
    {
        public string Label { get; }
        public bool AllowEditMode { get; }

        public ButtonAttribute(string label = null, bool allowEditMode = false)
        {
            Label = label;
            AllowEditMode = allowEditMode;
        }
    }
}