using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using System.Collections.Generic;

namespace EditorTools
{
    [CustomEditor(typeof(Transform))]
    [CanEditMultipleObjects]
    public class TransformSearchEditor : Editor
    {
        private Editor defaultTransformEditor;
        private Type transformEditorType;

        public static string overallSearchFilter = "";

        // Tracks whether each component block is expanded or collapsed
        private static Dictionary<string, bool> componentFoldoutStates = new Dictionary<string, bool>();

        // Internal struct to hold the exact path and the copied property pointer
        private struct MatchedPropertyInfo
        {
            public string breadcrumbPath;
            public SerializedProperty property;
        }

        private void OnEnable()
        {
            transformEditorType = Assembly.GetAssembly(typeof(Editor)).GetType("UnityEditor.TransformInspector");
            if (transformEditorType != null)
            {
                defaultTransformEditor = CreateEditor(targets, transformEditorType);
            }
        }

        private void OnDisable()
        {
            if (defaultTransformEditor != null)
            {
                DestroyImmediate(defaultTransformEditor);
            }
        }

        public override void OnInspectorGUI()
        {
            if (defaultTransformEditor != null)
            {
                defaultTransformEditor.OnInspectorGUI();
            }
            else
            {
                base.OnInspectorGUI();
            }

            EditorGUILayout.Space(6);

            GUILayout.BeginHorizontal(GUI.skin.box);
            EditorGUI.BeginChangeCheck();

            overallSearchFilter = EditorGUILayout.TextField(GUIContent.none, overallSearchFilter, "SearchTextField");

            if (EditorGUI.EndChangeCheck())
            {
                ApplyFilterToActiveComponents();
            }

            if (!string.IsNullOrEmpty(overallSearchFilter))
            {
                if (GUILayout.Button("", "SearchCancelButton", GUILayout.Width(18)))
                {
                    overallSearchFilter = "";
                    GUI.FocusControl(null);
                    ApplyFilterToActiveComponents();
                }
            }
            GUILayout.EndHorizontal();

            DrawNativeMatchingComponentsInline();
        }

        private void ApplyFilterToActiveComponents()
        {
            ActiveEditorTracker tracker = ActiveEditorTracker.sharedTracker;
            Editor[] activeEditors = tracker.activeEditors;
            if (activeEditors == null) return;

            string query = overallSearchFilter.Trim().ToLower();
            bool isSearchEmpty = string.IsNullOrEmpty(query);

            for (int i = 0; i < activeEditors.Length; i++)
            {
                Editor currentEditor = activeEditors[i];
                if (currentEditor == null || currentEditor.target == null) continue;

                if (currentEditor.target is GameObject || currentEditor.target is Transform)
                {
                    tracker.SetVisible(i, 0);
                    continue;
                }

                if (isSearchEmpty)
                {
                    tracker.SetVisible(i, 0);
                    continue;
                }

                tracker.SetVisible(i, 1);
            }

            tracker.ForceRebuild();
        }

        private void DrawNativeMatchingComponentsInline()
        {
            string query = overallSearchFilter.Trim().ToLower();
            if (string.IsNullOrEmpty(query)) return;

            ActiveEditorTracker tracker = ActiveEditorTracker.sharedTracker;
            Editor[] activeEditors = tracker.activeEditors;
            if (activeEditors == null) return;

            for (int i = 0; i < activeEditors.Length; i++)
            {
                Editor currentEditor = activeEditors[i];
                if (currentEditor == null || currentEditor.target == null) continue;
                if (currentEditor.target is GameObject || currentEditor.target is Transform) continue;

                string componentName = currentEditor.target.GetType().Name.ToLower();
                bool componentNameMatches = componentName.Contains(query);

                List<MatchedPropertyInfo> matchesToDraw = new List<MatchedPropertyInfo>();

                if (currentEditor.serializedObject != null)
                {
                    SerializedObject sObj = currentEditor.serializedObject;
                    sObj.Update();
                    SerializedProperty iterator = sObj.GetIterator();

                    if (iterator.NextVisible(true))
                    {
                        do
                        {
                            if (iterator.name == "m_Script") continue;

                            FindMatchesDeep(iterator.Copy(), query, componentNameMatches, "", matchesToDraw);
                        }
                        while (iterator.NextVisible(false));
                    }
                }

                if (matchesToDraw.Count > 0)
                {
                    EditorGUILayout.Space(2);
                    EditorGUILayout.BeginVertical(GUI.skin.box);

                    // Generate a stable key for this component instance
                    string componentTypeName = currentEditor.target.GetType().Name;
                    string foldoutKey = $"{currentEditor.target.GetEntityId()}_{componentTypeName}";

                    // Default to expanded (true) if it's the first time seeing this component in search results
                    if (!componentFoldoutStates.ContainsKey(foldoutKey))
                    {
                        componentFoldoutStates[foldoutKey] = true;
                    }

                    string nicifiedName = ObjectNames.NicifyVariableName(componentTypeName);

                    // Create the foldout header
                    componentFoldoutStates[foldoutKey] = EditorGUILayout.Foldout(
                        componentFoldoutStates[foldoutKey],
                        nicifiedName,
                        true,
                        EditorStyles.foldoutHeader
                    );

                    // Draw properties only if the foldout is expanded
                    if (componentFoldoutStates[foldoutKey])
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.Space(2);
                        EditorGUI.BeginChangeCheck();

                        foreach (var match in matchesToDraw)
                        {
                            if (!string.IsNullOrEmpty(match.breadcrumbPath))
                            {
                                GUIStyle pathStyle = new GUIStyle(EditorStyles.miniLabel);
                                pathStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.3f, 0.3f, 0.3f);

                                EditorGUILayout.LabelField(match.breadcrumbPath, pathStyle);
                            }

                            EditorGUILayout.PropertyField(match.property, new GUIContent(match.property.displayName), false);
                            EditorGUILayout.Space(2);
                        }

                        if (EditorGUI.EndChangeCheck())
                        {
                            currentEditor.serializedObject.ApplyModifiedProperties();
                        }

                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(2);
                }
            }
        }

        private void FindMatchesDeep(SerializedProperty property, string query, bool forceAll, string currentPath, List<MatchedPropertyInfo> results)
        {
            string displayName = property.displayName;
            string propertyName = property.name.ToLower();

            string nextPath = string.IsNullOrEmpty(currentPath) ? displayName : $"{currentPath} > {displayName}";

            bool isMatch = forceAll || propertyName.Contains(query) || displayName.ToLower().Contains(query);

            // Identify if this is a built-in structural type that Unity handles as a single, terminal UI field
            bool isBuiltInComposite = property.propertyType == SerializedPropertyType.Vector2 ||
                                      property.propertyType == SerializedPropertyType.Vector3 ||
                                      property.propertyType == SerializedPropertyType.Vector4 ||
                                      property.propertyType == SerializedPropertyType.Vector2Int ||
                                      property.propertyType == SerializedPropertyType.Vector3Int ||
                                      property.propertyType == SerializedPropertyType.LayerMask ||
                                      property.propertyType == SerializedPropertyType.AnimationCurve ||
                                      property.propertyType == SerializedPropertyType.Bounds ||
                                      property.propertyType == SerializedPropertyType.BoundsInt ||
                                      property.propertyType == SerializedPropertyType.Rect ||
                                      property.propertyType == SerializedPropertyType.RectInt ||
                                      property.propertyType == SerializedPropertyType.Color ||
                                      property.propertyType == SerializedPropertyType.Quaternion;

            // Treat strings, asset links, and built-in math/editor types as leaf nodes
            bool isTerminalField = !property.hasChildren ||
                                   property.propertyType == SerializedPropertyType.String ||
                                   property.propertyType == SerializedPropertyType.ObjectReference ||
                                   isBuiltInComposite;

            if (!isTerminalField)
            {
                SerializedProperty endProperty = property.GetEndProperty();
                if (property.NextVisible(true))
                {
                    do
                    {
                        if (SerializedProperty.EqualContents(property, endProperty))
                            break;

                        FindMatchesDeep(property.Copy(), query, forceAll, nextPath, results);
                    }
                    while (property.NextVisible(false));
                }
            }
            else
            {
                if (isMatch)
                {
                    results.Add(new MatchedPropertyInfo
                    {
                        breadcrumbPath = currentPath,
                        property = property.Copy()
                    });
                }
            }
        }
    }
}