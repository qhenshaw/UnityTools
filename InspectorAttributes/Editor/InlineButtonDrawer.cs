using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace InspectorAttributes.Editors
{
    /// <summary>
    /// Draws "Label [field] [button]". Unity gives a field only one PropertyDrawer, so this drawer draws the field
    /// part itself: with the drawer of another attribute on the field (e.g. a dropdown), else the drawer for the
    /// field's type, else Unity's default field.
    /// </summary>
    [CustomPropertyDrawer(typeof(InlineButtonAttribute))]
    public class InlineButtonDrawer : PropertyDrawer
    {
        private PropertyDrawer _fieldDrawer;
        private bool _fieldDrawerResolved;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            PropertyDrawer inner = GetFieldDrawer();
            return inner != null ? inner.GetPropertyHeight(property, label) : EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect fieldRect = InlineButtonGUI.Draw(position, property, fieldInfo);

            // The inner drawer may call InlineButtonGUI.Draw itself (for inspectors that pick it directly);
            // suppress that here so the button isn't drawn twice.
            using (InlineButtonGUI.Suppress())
            {
                DrawField(fieldRect, property, label);
            }
        }

        private void DrawField(Rect rect, SerializedProperty property, GUIContent label)
        {
            PropertyDrawer inner = GetFieldDrawer();
            if (inner != null)
            {
                inner.OnGUI(rect, property, label);
                return;
            }

            if (!DefaultField.Draw(rect, property, label))
            {
                EditorGUI.LabelField(rect, label, new GUIContent("(field can't be drawn inline)"));
            }
        }

        /// <summary>The drawer for another attribute on this field or for its type, configured as Unity would.</summary>
        private PropertyDrawer GetFieldDrawer()
        {
            if (_fieldDrawerResolved) return _fieldDrawer;
            _fieldDrawerResolved = true;
            if (fieldInfo == null) return null;

            foreach (PropertyAttribute other in fieldInfo.GetCustomAttributes<PropertyAttribute>(true)
                         .Where(a => !(a is InlineButtonAttribute) && !(a is ButtonAttribute))
                         .OrderBy(a => a.order))
            {
                _fieldDrawer = DrawerRegistry.Create(other.GetType(), other, fieldInfo);
                if (_fieldDrawer != null) return _fieldDrawer;
            }

            Type valueType = fieldInfo.FieldType;
            if (valueType.IsArray) valueType = valueType.GetElementType();
            else if (valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(List<>)) valueType = valueType.GetGenericArguments()[0];

            _fieldDrawer = DrawerRegistry.Create(valueType, null, fieldInfo);
            return _fieldDrawer;
        }

        /// <summary>Maps attribute and field types to their [CustomPropertyDrawer] classes, the way Unity does.</summary>
        private static class DrawerRegistry
        {
            private static Dictionary<Type, (Type drawer, bool forChildren)> s_Drawers;
            private static readonly FieldInfo s_TargetType = typeof(CustomPropertyDrawer).GetField("m_Type", BindingFlags.NonPublic | BindingFlags.Instance);
            private static readonly FieldInfo s_UseForChildren = typeof(CustomPropertyDrawer).GetField("m_UseForChildren", BindingFlags.NonPublic | BindingFlags.Instance);
            private static readonly FieldInfo s_Attribute = typeof(PropertyDrawer).GetField("m_Attribute", BindingFlags.NonPublic | BindingFlags.Instance);
            private static readonly FieldInfo s_FieldInfo = typeof(PropertyDrawer).GetField("m_FieldInfo", BindingFlags.NonPublic | BindingFlags.Instance);

            public static PropertyDrawer Create(Type target, PropertyAttribute attribute, FieldInfo field)
            {
                Type drawerType = Find(target);
                if (drawerType == null || drawerType == typeof(InlineButtonDrawer) || s_Attribute == null || s_FieldInfo == null) return null;

                try
                {
                    var drawer = (PropertyDrawer)Activator.CreateInstance(drawerType);
                    s_Attribute.SetValue(drawer, attribute);
                    s_FieldInfo.SetValue(drawer, field);
                    return drawer;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[InlineButton] Couldn't use {drawerType.Name} for '{field.Name}': {e.GetBaseException().Message}");
                    return null;
                }
            }

            private static Type Find(Type target)
            {
                if (s_Drawers == null) Build();
                for (Type t = target; t != null; t = t.BaseType)
                {
                    if (s_Drawers.TryGetValue(t, out var entry) && (t == target || entry.forChildren)) return entry.drawer;
                }
                return null;
            }

            private static void Build()
            {
                s_Drawers = new Dictionary<Type, (Type, bool)>();
                if (s_TargetType == null) return;

                foreach (Type drawerType in TypeCache.GetTypesWithAttribute<CustomPropertyDrawer>())
                {
                    if (drawerType.IsAbstract || !typeof(PropertyDrawer).IsAssignableFrom(drawerType)) continue;
                    foreach (CustomPropertyDrawer cpd in drawerType.GetCustomAttributes<CustomPropertyDrawer>(false))
                    {
                        var target = s_TargetType.GetValue(cpd) as Type;
                        if (target == null || s_Drawers.ContainsKey(target)) continue;
                        s_Drawers[target] = (drawerType, s_UseForChildren != null && (bool)s_UseForChildren.GetValue(cpd));
                    }
                }
            }
        }

        /// <summary>Unity's default field drawing (EditorGUI.DefaultPropertyField is internal), with a fallback for common types.</summary>
        private static class DefaultField
        {
            private static readonly MethodInfo s_DefaultPropertyField = typeof(EditorGUI).GetMethod("DefaultPropertyField",
                BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Rect), typeof(SerializedProperty), typeof(GUIContent) }, null);

            public static bool Draw(Rect rect, SerializedProperty property, GUIContent label)
            {
                if (s_DefaultPropertyField != null)
                {
                    try
                    {
                        s_DefaultPropertyField.Invoke(null, new object[] { rect, property, label });
                    }
                    catch (TargetInvocationException e) when (e.InnerException is ExitGUIException)
                    {
                        // Unity ends the GUI pass this way (e.g. when an object picker opens); let it through unwrapped.
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                    }
                    return true;
                }

                switch (property.propertyType)
                {
                    case SerializedPropertyType.String: property.stringValue = EditorGUI.TextField(rect, label, property.stringValue); return true;
                    case SerializedPropertyType.Integer: property.intValue = EditorGUI.IntField(rect, label, property.intValue); return true;
                    case SerializedPropertyType.Float: property.floatValue = EditorGUI.FloatField(rect, label, property.floatValue); return true;
                    case SerializedPropertyType.Boolean: property.boolValue = EditorGUI.Toggle(rect, label, property.boolValue); return true;
                    case SerializedPropertyType.ObjectReference:
                        property.objectReferenceValue = EditorGUI.ObjectField(rect, label, property.objectReferenceValue, typeof(UnityEngine.Object), true);
                        return true;
                    default: return false;
                }
            }
        }
    }
}
