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

        // Height of one button row.
        private const float ButtonHeight = 22f;
        // Small gap above the button so it doesn't crowd the field above it.
        private const float TopPadding = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // Reserve space for the button; hide the field itself (height 0 would clip, so use button height).
            return ButtonHeight + TopPadding;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // Only valid on string fields — the value is the method name.
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.HelpBox(position,
                    $"[Button] on '{property.name}' requires a string field.",
                    MessageType.Error);
                return;
            }

            var attr = (ButtonAttribute)attribute;
            string method = property.stringValue;   // e.g. "SpawnEnemies"
            bool isPlaying = Application.isPlaying;
            bool canClick = isPlaying || attr.AllowEditMode;

            // Nicify "spawnEnemies" / "SpawnEnemies" → "Spawn Enemies" unless overridden.
            string btnLabel = !string.IsNullOrEmpty(attr.Label)
                ? attr.Label
                : ObjectNames.NicifyVariableName(method);

            Rect btnRect = new Rect(position.x, position.y + TopPadding, position.width, ButtonHeight);

            EditorGUI.BeginDisabledGroup(!canClick);

            if (GUI.Button(btnRect, btnLabel))
                Invoke(property, method, attr);

            EditorGUI.EndDisabledGroup();

            // Tooltip when disabled so the user understands why.
            if (!canClick)
            {
                var tip = new GUIContent(string.Empty,
                    $"'{btnLabel}' only runs in Play Mode. Set allowEditMode:true to enable in Edit Mode.");
                EditorGUI.LabelField(btnRect, tip);
            }
        }

        // ── Invocation ────────────────────────────────────────────────────────────

        private static void Invoke(SerializedProperty property, string methodName, ButtonAttribute attr)
        {
            // Walk up to the root object that owns this serialized property.
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

        /// <summary>
        /// Resolves the actual C# object that owns the field — handles nested classes
        /// and SerializedObject wrapping.
        /// </summary>
        private static object GetOwner(SerializedProperty property)
        {
            // property.serializedObject.targetObject is the root UnityEngine.Object.
            object current = property.serializedObject.targetObject;

            // Walk the property path for nested structs/classes, e.g. "subData.spawnButton".
            string path = property.propertyPath.Replace(".Array.data[", "[");
            string[] parts = path.Split('.');

            // Stop one part early — the last part IS the field itself, we want its owner.
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string part = parts[i];
                if (part.Contains("["))
                {
                    // Array element: fieldName[index]
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