using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InspectorAttributes.Editors
{
    /// <summary>
    /// Finds and calls the method behind a [Button] or [InlineButton]. Shared by both drawers.
    /// <list type="bullet">
    /// <item>Looks up private and public parameterless methods through the whole class hierarchy.</item>
    /// <item>Resolves the object that owns the field, also inside nested serializable classes and arrays.</item>
    /// <item>Calls the method on every selected object, as one undo step, after the inspector has finished drawing,
    /// so methods that open scenes or dialogs don't break the inspector's layout, and pending field edits are applied.</item>
    /// <item>Logs exceptions from the method with the object as context instead of letting them escape OnGUI.</item>
    /// </list>
    /// </summary>
    internal static class InspectorMethodInvoker
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<(Type, string), MethodInfo> MethodCache = new Dictionary<(Type, string), MethodInfo>();

        /// <summary>Why the button can't work, or null when it can. Shown on the button instead of failing on click.</summary>
        public static string Validate(SerializedProperty property, string methodName)
        {
            if (string.IsNullOrEmpty(methodName)) return "No method set. Add Method = nameof(YourMethod) to the attribute.";

            object owner = ResolveOwner(property.serializedObject.targetObject, property.propertyPath);
            if (owner == null) return $"Couldn't find the object that owns '{property.propertyPath}'.";

            return FindMethod(owner.GetType(), methodName) == null
                ? $"No parameterless method '{methodName}' on {owner.GetType().Name}."
                : null;
        }

        public static void InvokeDeferred(SerializedProperty property, string methodName, string undoName)
        {
            Object[] targets = property.serializedObject.targetObjects;
            string path = property.propertyPath;

            EditorApplication.delayCall += () =>
            {
                Object[] alive = targets.Where(t => t != null).ToArray();
                if (alive.Length == 0) return;

                bool record = !Application.isPlaying;
                if (record)
                {
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName(undoName);
                    Undo.RecordObjects(alive, undoName);
                }
                int group = Undo.GetCurrentGroup();

                foreach (Object target in alive)
                {
                    object owner = ResolveOwner(target, path);
                    MethodInfo method = owner != null ? FindMethod(owner.GetType(), methodName) : null;
                    if (method == null)
                    {
                        Debug.LogError($"[Button] Couldn't call '{methodName}' on {target.name}: {Validate(new SerializedObject(target).FindProperty(path), methodName)}", target);
                        continue;
                    }

                    try
                    {
                        method.Invoke(owner, null);
                    }
                    catch (TargetInvocationException e)
                    {
                        Debug.LogException(e.InnerException ?? e, target);
                    }

                    if (record && target != null) EditorUtility.SetDirty(target);
                }

                if (record) Undo.CollapseUndoOperations(group);
            };
        }

        /// <summary>A parameterless instance method declared on the type or any base class.</summary>
        public static MethodInfo FindMethod(Type type, string name)
        {
            if (MethodCache.TryGetValue((type, name), out MethodInfo cached)) return cached;

            MethodInfo found = null;
            for (Type t = type; t != null && found == null; t = t.BaseType)
            {
                found = t.GetMethods(InstanceMembers).FirstOrDefault(m => m.Name == name && m.GetParameters().Length == 0);
            }

            MethodCache[(type, name)] = found;
            return found;
        }

        /// <summary>
        /// The object whose field the property is: the target itself for top-level fields, or the nested
        /// serializable class / array element that contains the field.
        /// </summary>
        public static object ResolveOwner(object target, string propertyPath)
        {
            object current = target;
            string[] parts = propertyPath.Replace(".Array.data[", "[").Split('.');

            for (int i = 0; i < parts.Length - 1 && current != null; i++)
            {
                string part = parts[i];
                int bracket = part.IndexOf('[');
                string fieldName = bracket >= 0 ? part.Substring(0, bracket) : part;

                current = FindField(current.GetType(), fieldName)?.GetValue(current);

                // A path part can index several times, e.g. "grid[2][3]" for nested arrays.
                while (bracket >= 0 && current != null)
                {
                    int close = part.IndexOf(']', bracket);
                    int index = int.Parse(part.Substring(bracket + 1, close - bracket - 1));
                    current = current is IList list && index < list.Count ? list[index] : null;
                    bracket = part.IndexOf('[', close);
                }
            }

            return current;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, InstanceMembers);
                if (field != null) return field;
            }
            return null;
        }
    }

    /// <summary>
    /// Draws a field's [InlineButton] at the right of the row and returns the space left for the field.
    /// Call it at the start of a custom PropertyDrawer's OnGUI so the button shows even when the inspector
    /// (e.g. Odin) picks your drawer instead of InlineButtonDrawer for a field that has both attributes:
    /// <code>position = InlineButtonGUI.Draw(position, property, fieldInfo);</code>
    /// Does nothing when the field has no [InlineButton], or when InlineButtonDrawer already drew it.
    /// </summary>
    public static class InlineButtonGUI
    {
        private const float Spacing = 2f;
        private static int s_Suppress;

        public static Rect Draw(Rect position, SerializedProperty property, FieldInfo field)
        {
            if (s_Suppress > 0 || field == null) return position;
            var attr = field.GetCustomAttribute<InlineButtonAttribute>(true);
            if (attr == null) return position;

            string label = !string.IsNullOrEmpty(attr.Label) ? attr.Label : ObjectNames.NicifyVariableName(attr.Method ?? "Button");
            float width = attr.Width > 0f ? attr.Width : Mathf.Max(40f, GUI.skin.button.CalcSize(new GUIContent(label + " (!)")).x);
            var buttonRect = new Rect(position.xMax - width, position.y, width, EditorGUIUtility.singleLineHeight);

            // Buttons are positioned absolutely, so draw at indent 0.
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            InspectorButtonGUI.Draw(buttonRect, property, attr.Method, label, attr.AllowEditMode);
            EditorGUI.indentLevel = indent;

            return new Rect(position.x, position.y, position.width - width - Spacing, position.height);
        }

        /// <summary>While open, Draw does nothing: used when InlineButtonDrawer hands the field to another drawer.</summary>
        internal static IDisposable Suppress() => new SuppressScope();

        private sealed class SuppressScope : IDisposable
        {
            private bool _disposed;
            public SuppressScope() => s_Suppress++;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                s_Suppress--;
            }
        }
    }

    /// <summary>Common button drawing for both drawers.</summary>
    internal static class InspectorButtonGUI
    {
        public static void Draw(Rect rect, SerializedProperty property, string methodName, string label, bool allowEditMode)
        {
            string error = InspectorMethodInvoker.Validate(property, methodName);
            bool canClick = error == null && (allowEditMode || Application.isPlaying);

            string tooltip = error ?? (canClick ? null : $"'{label}' only runs in Play Mode. Set allowEditMode: true to use it in Edit Mode.");
            var content = new GUIContent(error == null ? label : $"{label} (!)", tooltip);

            Color previous = GUI.backgroundColor;
            if (error != null) GUI.backgroundColor = new Color(1f, 0.55f, 0.5f);
            using (new EditorGUI.DisabledScope(!canClick))
            {
                if (GUI.Button(rect, content)) InspectorMethodInvoker.InvokeDeferred(property, methodName, label);
            }
            GUI.backgroundColor = previous;

            // Disabled controls don't show tooltips; draw it on top so the reason stays discoverable.
            if (!canClick) GUI.Label(rect, new GUIContent(string.Empty, tooltip));
        }
    }
}
