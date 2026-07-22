using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace InspectorAttributes.Editors
{
    [CustomPropertyDrawer(typeof(ButtonAttribute))]
    public class ButtonDrawer : PropertyDrawer
    {
        private const BindingFlags MethodFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private const float ButtonHeight = 20f;
        private const float TopPadding = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return ButtonHeight + TopPadding;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.HelpBox(position,
                    $"[Button] on '{property.name}' requires a string field.",
                    MessageType.Error);
                return;
            }

            var attr = (ButtonAttribute)attribute;
            string method = property.stringValue;
            bool isPlaying = Application.isPlaying;
            bool canClick = isPlaying || attr.AllowEditMode;

            string btnLabel = !string.IsNullOrEmpty(attr.Label)
                ? attr.Label
                : ObjectNames.NicifyVariableName(method);

            int width = attr.Width > 0 ? attr.Width : (int)position.width;
            Rect btnRect = new Rect(position.x, position.y + TopPadding, width, ButtonHeight);

            EditorGUI.BeginDisabledGroup(!canClick);

            if (GUI.Button(btnRect, btnLabel))
                Invoke(property, method, attr);

            EditorGUI.EndDisabledGroup();

            if (!canClick)
            {
                var tip = new GUIContent(string.Empty,
                    $"'{btnLabel}' only runs in Play Mode. Set allowEditMode:true to enable in Edit Mode.");
                EditorGUI.LabelField(btnRect, tip);
            }
        }

        private static void Invoke(SerializedProperty property, string methodName, ButtonAttribute attr)
        {
            object owner = GetOwner(property);
            if (owner == null)
            {
                Debug.LogError($"[Button] Could not find owner object for property '{property.name}'.");
                return;
            }

            var method = owner.GetType().GetMethod(methodName, MethodFlags);
            if (method == null)
            {
                Debug.LogError(
                    $"[Button] Method '{methodName}' not found on {owner.GetType().Name}. " +
                    "Check the spelling and that it is parameterless.");
                return;
            }

            if (method.GetParameters().Length > 0)
            {
                Debug.LogError($"[Button] Method '{methodName}' must be parameterless.");
                return;
            }

            var unityObj = property.serializedObject.targetObject;

            if (!Application.isPlaying)
                Undo.RecordObject(unityObj, $"Button: {methodName}");

            method.Invoke(owner, null);

            if (!Application.isPlaying)
                EditorUtility.SetDirty(unityObj);
        }

        private static object GetOwner(SerializedProperty property)
        {
            object current = property.serializedObject.targetObject;
            string path = property.propertyPath.Replace(".Array.data[", "[");
            string[] parts = path.Split('.');

            for (int i = 0; i < parts.Length - 1; i++)
            {
                string part = parts[i];
                if (part.Contains("["))
                {
                    string fieldName = part[..part.IndexOf('[')];
                    int index = int.Parse(part[(part.IndexOf('[') + 1)..^1]);
                    var fi = current?.GetType().GetField(fieldName, MethodFlags);
                    var arr = fi?.GetValue(current) as System.Collections.IList;
                    current = arr?[index];
                }
                else
                {
                    var fi = current?.GetType().GetField(part, MethodFlags);
                    current = fi?.GetValue(current);
                }
            }

            return current;
        }
    }
}