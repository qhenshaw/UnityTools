using System;
using UnityEngine;

namespace InspectorAttributes
{
    /// <summary>
    /// Draws a full-width button in place of the field it's on. Name the method with <see cref="Method"/>:
    /// <code>[SerializeField, Button("Bake", true, Method = nameof(Bake))] private string _bakeButton;</code>
    /// Without <see cref="Method"/>, the field's string value names the method (the older style, kept for
    /// existing code; it breaks when the method is renamed or for new array elements, so prefer Method).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class ButtonAttribute : PropertyAttribute
    {
        /// <summary>Button text. Null uses the method name, made readable.</summary>
        public string Label { get; }
        /// <summary>True lets the button run in Edit Mode. False (the default) disables it outside Play Mode.</summary>
        public bool AllowEditMode { get; }
        /// <summary>Maximum button width in pixels; -1 fills the row.</summary>
        public int Width { get; set; } = -1;

        /// <summary>Name of the parameterless method to call. Use nameof(...) so renames stay in sync.</summary>
        public string Method { get; set; }

        public ButtonAttribute(string label = null, bool allowEditMode = false, int width = -1)
        {
            Label = label;
            AllowEditMode = allowEditMode;
            Width = width;
        }
    }

    /// <summary>
    /// Draws a button on the same row as the field, to the right of it:
    /// <code>[SerializeField, InlineButton(nameof(AddNewScene), "Add")] private string _newSceneName;</code>
    /// Works together with another drawer on the same field (e.g. a dropdown attribute), which draws the field part.
    /// Intended for single-line fields. Runs in Edit Mode by default, unlike <see cref="ButtonAttribute"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class InlineButtonAttribute : PropertyAttribute
    {
        /// <summary>Name of the parameterless method to call. Use nameof(...) so renames stay in sync.</summary>
        public string Method { get; }
        /// <summary>Button text. Null uses the method name, made readable.</summary>
        public string Label { get; }
        /// <summary>True (the default) lets the button run in Edit Mode; false disables it outside Play Mode.</summary>
        public bool AllowEditMode { get; }

        /// <summary>Button width in pixels; 0 fits the label.</summary>
        public float Width { get; }

        public InlineButtonAttribute(string method, string label = null, bool allowEditMode = true, float width = 0f)
        {
            Method = method;
            Label = label;
            AllowEditMode = allowEditMode;
            Width = width;
            // Unity draws a field with a single PropertyDrawer: make sure it's this one, which then hands the
            // field part to any other drawer on the field.
            order = -1000;
        }
    }
}
