using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UnityTools.Data.Editor
{
    /// <summary>
    /// Draws a DataReference as its asset field with a "New" button beside it. In Play mode a foldout
    /// underneath shows the field's runtime copy, live and editable.
    /// </summary>
    [CustomPropertyDrawer(typeof(DataReferenceBase), true)]
    public class DataReferenceDrawer : PropertyDrawer
    {
        private const string PersistentField = "_persistent";
        private const float ButtonWidth = 44f;

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            Type dataType = GetDataType(fieldInfo.FieldType);
            SerializedProperty persistent = property.FindPropertyRelative(PersistentField);

            var root = new VisualElement();

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var field = new ObjectField(property.displayName)
            {
                objectType = dataType,
                allowSceneObjects = false,
                style = { flexGrow = 1, flexShrink = 1 }
            };
            field.AddToClassList(BaseField<Object>.alignedFieldUssClassName);
            field.BindProperty(persistent);
            row.Add(field);

            var newButton = new Button(() => CreateAsset(property.serializedObject, persistent.propertyPath, dataType))
            {
                text = "New",
                tooltip = $"Create a new {dataType.Name} asset and assign it",
                style = { width = ButtonWidth, marginRight = 0 }
            };
            row.Add(newButton);
            root.Add(row);

            var runtimeFoldout = new Foldout { text = "Runtime copy", value = false, style = { marginLeft = 15 } };
            var runtimeHelp = new HelpBox("", HelpBoxMessageType.Info);
            var runtimeBody = new VisualElement();
            runtimeFoldout.Add(runtimeHelp);
            runtimeFoldout.Add(runtimeBody);
            root.Add(runtimeFoldout);

            ScriptableObject shownCopy = null;
            bool hadCopy = false;

            void Refresh()
            {
                bool playing = EditorApplication.isPlaying;
                runtimeFoldout.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
                if (!playing) return;

                ScriptableObject copy = null;
                try { copy = FindInstance(property)?.RuntimeObject; }
                catch (Exception) { /* the property went away (array resized, object destroyed) */ }

                bool hasCopy = copy != null;
                if (hasCopy == hadCopy && copy == shownCopy) return;
                hadCopy = hasCopy;
                shownCopy = copy;

                runtimeBody.Clear();
                if (hasCopy)
                {
                    runtimeHelp.style.display = DisplayStyle.None;
                    runtimeBody.Add(new InspectorElement(copy));
                }
                else
                {
                    runtimeHelp.style.display = DisplayStyle.Flex;
                    runtimeHelp.text = "No runtime copy yet. It's made the first time the game reads Runtime.";
                }
            }

            Refresh();
            root.schedule.Execute(Refresh).Every(250);
            return root;
        }

        // Fallback for inspectors drawn with IMGUI: the asset field and the New button, no runtime foldout.
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Type dataType = GetDataType(fieldInfo.FieldType);
            SerializedProperty persistent = property.FindPropertyRelative(PersistentField);

            EditorGUI.BeginProperty(position, label, property);
            var fieldRect = new Rect(position.x, position.y, position.width - ButtonWidth - 2f, position.height);
            var buttonRect = new Rect(position.xMax - ButtonWidth, position.y, ButtonWidth, position.height);
            EditorGUI.ObjectField(fieldRect, persistent, dataType, label);
            if (GUI.Button(buttonRect, "New"))
            {
                CreateAsset(property.serializedObject, persistent.propertyPath, dataType);
                GUIUtility.ExitGUI();
            }
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

        private static void CreateAsset(SerializedObject serializedObject, string persistentPath, Type dataType)
        {
            string path = EditorUtility.SaveFilePanelInProject($"New {dataType.Name}", $"New {dataType.Name}", "asset",
                $"Choose where to save the new {dataType.Name} asset.");
            if (string.IsNullOrEmpty(path)) return;

            ScriptableObject asset = ScriptableObject.CreateInstance(dataType);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            serializedObject.Update();
            serializedObject.FindProperty(persistentPath).objectReferenceValue = asset;
            serializedObject.ApplyModifiedProperties();
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>The T of the DataReference&lt;T&gt; held by a field, an array of them or a List of them.</summary>
        internal static Type GetDataType(Type fieldType)
        {
            if (fieldType.IsArray) fieldType = fieldType.GetElementType();
            else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>))
                fieldType = fieldType.GetGenericArguments()[0];

            for (Type type = fieldType; type != null; type = type.BaseType)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DataReference<>))
                    return type.GetGenericArguments()[0];
            }
            return typeof(ScriptableObject);
        }

        /// <summary>
        /// The live DataReference object behind a property, found by walking its path with reflection.
        /// SerializedProperty.boxedValue would return a deserialized copy without the runtime copy.
        /// </summary>
        internal static DataReferenceBase FindInstance(SerializedProperty property)
        {
            object current = property.serializedObject.targetObject;
            string path = property.propertyPath.Replace(".Array.data[", "[");
            foreach (string part in path.Split('.'))
            {
                if (current == null) return null;
                int bracket = part.IndexOf('[');
                if (bracket >= 0)
                {
                    current = GetField(current, part.Substring(0, bracket));
                    int index = int.Parse(part.Substring(bracket + 1, part.Length - bracket - 2));
                    current = current is IList list && index < list.Count ? list[index] : null;
                }
                else
                {
                    current = GetField(current, part);
                }
            }
            return current as DataReferenceBase;
        }

        private static object GetField(object source, string name)
        {
            for (Type type = source.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) return field.GetValue(source);
            }
            return null;
        }
    }
}
