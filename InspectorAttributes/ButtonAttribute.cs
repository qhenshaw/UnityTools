using System;
using UnityEngine;

namespace InspectorAttributes
{
    [AttributeUsage(AttributeTargets.Field)]
    public class ButtonAttribute : PropertyAttribute
    {
        public string Label { get; }
        public bool AllowEditMode { get; }
        public int Width { get; set; } = -1;

        public ButtonAttribute(string label = null, bool allowEditMode = false, int width = -1)
        {
            Label = label;
            AllowEditMode = allowEditMode;
            Width = width;
        }
    }
}