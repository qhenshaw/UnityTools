using System;
using UnityEditor;
using UnityEngine;

namespace UnityTools.Messaging.Editor
{
    // Draws a GlobalVariable as one read-only row: label, tag, and value.
    // In edit mode the value is the starting value from code, in Play it is the live global value.
    [CustomPropertyDrawer(typeof(GlobalVariable<>), true)]
    public class GlobalVariableDrawer : PropertyDrawer
    {
        private const float TagWidthRatio = 0.45f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty tagName = property.FindPropertyRelative("_tag");
            SerializedProperty tagType = property.FindPropertyRelative("_tagType");
            SerializedProperty startValue = property.FindPropertyRelative("_value");

            EditorGUI.BeginProperty(position, label, property);
            Rect content = EditorGUI.PrefixLabel(position, label);
            Rect tagRect = new Rect(content.x, content.y, content.width * TagWidthRatio - 2f, content.height);
            Rect valueRect = new Rect(tagRect.xMax + 4f, content.y, content.xMax - tagRect.xMax - 4f, content.height);

            Type enumType = ResolveType(tagType?.stringValue);
            string tagLabel = enumType != null ? $"{enumType.Name}.{tagName?.stringValue}" : tagName?.stringValue ?? "(no tag)";

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.TextField(tagRect, GUIContent.none, tagLabel);
                if (Application.isPlaying) DrawLiveValue(valueRect, enumType, tagName?.stringValue);
                else if (startValue != null) EditorGUI.PropertyField(valueRect, startValue, GUIContent.none, false);
            }
            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();

            if (Application.isPlaying) LiveRepaint.Request();
        }

        private static void DrawLiveValue(Rect rect, Type enumType, string tagName)
        {
            TagKey key = default;
            if (enumType != null && !string.IsNullOrEmpty(tagName) && Enum.IsDefined(enumType, tagName))
            {
                key = TagKey.From((Enum)Enum.Parse(enumType, tagName));
            }

            if (!GlobalVariableService.Instance.TryGetBoxed(key, out object value))
            {
                EditorGUI.TextField(rect, GUIContent.none, "(unset)");
                return;
            }

            if (value is UnityEngine.Object unityObject)
            {
                EditorGUI.ObjectField(rect, GUIContent.none, unityObject, unityObject != null ? unityObject.GetType() : typeof(UnityEngine.Object), true);
                return;
            }
            EditorGUI.TextField(rect, GUIContent.none, MessagingService.DescribeValue(value));
        }

        private static Type ResolveType(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName)) return null;
            Type type = Type.GetType(assemblyQualifiedName, false);
            return type != null && type.IsEnum ? type : null;
        }

        // The Inspector only repaints when something changes, so in Play it is repainted a few times a second
        // while a GlobalVariable has been drawn recently, to keep the live values current.
        private static class LiveRepaint
        {
            private const double Interval = 0.25;
            private static double _lastRequest;
            private static double _lastRepaint;
            private static bool _hooked;
            private static readonly Type InspectorType = typeof(EditorWindow).Assembly.GetType("UnityEditor.InspectorWindow");

            public static void Request()
            {
                _lastRequest = EditorApplication.timeSinceStartup;
                if (_hooked) return;
                _hooked = true;
                EditorApplication.update += Update;
            }

            private static void Update()
            {
                double now = EditorApplication.timeSinceStartup;
                if (!Application.isPlaying || now - _lastRequest > 1.0)
                {
                    EditorApplication.update -= Update;
                    _hooked = false;
                    return;
                }
                if (now - _lastRepaint < Interval || InspectorType == null) return;
                _lastRepaint = now;
                foreach (UnityEngine.Object window in Resources.FindObjectsOfTypeAll(InspectorType))
                {
                    ((EditorWindow)window).Repaint();
                }
            }
        }
    }
}
