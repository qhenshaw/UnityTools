using UnityEditor;
using UnityEngine;

namespace UnityTools.Attributes.Editor
{
    [CustomPropertyDrawer(typeof(ButtonAttribute))]
    public class ButtonDrawer : PropertyDrawer
    {
        private const float TopPadding = 2f;
        private static float ButtonHeight => EditorGUIUtility.singleLineHeight + 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return ButtonHeight + TopPadding;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var attr = (ButtonAttribute)attribute;

            // Older style: the field's string value names the method.
            string method = attr.Method;
            if (string.IsNullOrEmpty(method) && property.propertyType == SerializedPropertyType.String) method = property.stringValue;

            string buttonLabel = !string.IsNullOrEmpty(attr.Label) ? attr.Label : ObjectNames.NicifyVariableName(method ?? property.name);

            Rect rect = EditorGUI.IndentedRect(position);
            rect.y += TopPadding;
            rect.height = ButtonHeight;
            if (attr.Width > 0) rect.width = Mathf.Min(rect.width, attr.Width);

            InspectorButtonGUI.Draw(rect, property, method, buttonLabel, attr.AllowEditMode);
        }
    }
}
