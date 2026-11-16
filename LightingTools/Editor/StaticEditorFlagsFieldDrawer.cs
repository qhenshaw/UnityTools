using UnityEditor;
using UnityEngine;

namespace LightingTools.Editor
{
    /// <summary>Draws an int field marked [StaticEditorFlagsField] as a StaticEditorFlags dropdown.</summary>
    [CustomPropertyDrawer(typeof(StaticEditorFlagsFieldAttribute))]
    public class StaticEditorFlagsFieldDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.Integer)
            {
                EditorGUI.HelpBox(position, $"[StaticEditorFlagsField] on '{property.name}' requires an int field.", MessageType.Error);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            var value = (StaticEditorFlags)EditorGUI.EnumFlagsField(position, label, (StaticEditorFlags)property.intValue);
            if (EditorGUI.EndChangeCheck()) property.intValue = (int)value;
            EditorGUI.EndProperty();
        }
    }
}
