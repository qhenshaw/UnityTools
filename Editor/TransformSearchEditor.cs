using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace EditorTools
{
    [CustomEditor(typeof(Transform))]
    [CanEditMultipleObjects]
    public class TransformSearchEditor : SearchableTransformEditor
    {
        protected override string DefaultEditorTypeName => "UnityEditor.TransformInspector";
    }

    [CustomEditor(typeof(RectTransform))]
    [CanEditMultipleObjects]
    public class RectTransformSearchEditor : SearchableTransformEditor
    {
        protected override string DefaultEditorTypeName => "UnityEditor.RectTransformEditor";
    }

    // Draws Unity's own Transform/RectTransform inspector, then a search field that lists matching fields
    // from the other components on the inspected object(s). Each Inspector window has its own search.
    public abstract class SearchableTransformEditor : Editor
    {
        protected abstract string DefaultEditorTypeName { get; }

        private Editor _defaultEditor;
        private MethodInfo _defaultOnSceneGUI;
        private static GUIStyle _breadcrumbStyle;

        private void OnEnable()
        {
            Type editorType = typeof(Editor).Assembly.GetType(DefaultEditorTypeName);
            if (editorType == null) return;
            _defaultEditor = CreateEditor(targets, editorType);
            _defaultOnSceneGUI = editorType.GetMethod("OnSceneGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private void OnDisable()
        {
            if (_defaultEditor != null) DestroyImmediate(_defaultEditor);
        }

        // RectTransformEditor draws the anchor and pivot handles in OnSceneGUI
        private void OnSceneGUI()
        {
            if (_defaultEditor != null && _defaultOnSceneGUI != null) _defaultOnSceneGUI.Invoke(_defaultEditor, null);
        }

        public override void OnInspectorGUI()
        {
            if (_defaultEditor != null) _defaultEditor.OnInspectorGUI();
            else base.OnInspectorGUI();

            TransformSearch search = TransformSearch.For(this);

            EditorGUILayout.Space(6);
            GUILayout.BeginHorizontal(GUI.skin.box);
            EditorGUI.BeginChangeCheck();
            string query = EditorGUILayout.TextField(GUIContent.none, search.Query, "SearchTextField");
            if (EditorGUI.EndChangeCheck()) search.SetQuery(query);
            if (!string.IsNullOrEmpty(search.Query) && GUILayout.Button("", "SearchCancelButton", GUILayout.Width(18)))
            {
                search.SetQuery("");
                GUI.FocusControl(null);
            }
            GUILayout.EndHorizontal();

            search.Refresh();
            DrawResults(search);
        }

        private static void DrawResults(TransformSearch search)
        {
            if (search.Results.Count == 0) return;
            if (_breadcrumbStyle == null)
            {
                _breadcrumbStyle = new GUIStyle(EditorStyles.miniLabel);
                _breadcrumbStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.3f, 0.3f, 0.3f);
            }

            foreach (TransformSearch.ComponentResult result in search.Results)
            {
                SerializedObject serializedObject = result.Editor != null ? result.Editor.serializedObject : null;
                if (serializedObject == null || serializedObject.targetObject == null) continue;
                serializedObject.Update();

                EditorGUILayout.Space(2);
                EditorGUILayout.BeginVertical(GUI.skin.box);
                bool expanded = search.IsResultExpanded(result.FoldoutKey);
                expanded = EditorGUILayout.Foldout(expanded, result.Title, true, EditorStyles.foldoutHeader);
                search.SetResultExpanded(result.FoldoutKey, expanded);

                if (expanded)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.Space(2);
                    EditorGUI.BeginChangeCheck();

                    string previousPath = null;
                    foreach (TransformSearch.Match match in result.Matches)
                    {
                        // one breadcrumb per group of fields that share a parent
                        if (!string.IsNullOrEmpty(match.BreadcrumbPath) && match.BreadcrumbPath != previousPath)
                        {
                            EditorGUILayout.LabelField(match.BreadcrumbPath, _breadcrumbStyle);
                        }
                        previousPath = match.BreadcrumbPath;

                        // arrays are listed as one field and keep their own expandable element list
                        EditorGUILayout.PropertyField(match.Property, new GUIContent(match.Property.displayName), match.Property.isArray);
                        EditorGUILayout.Space(2);
                    }

                    if (EditorGUI.EndChangeCheck()) serializedObject.ApplyModifiedProperties();
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }
        }
    }

    // Search state for one Inspector window: the query and the cached matches. Component foldouts are never changed.
    internal sealed class TransformSearch
    {
        internal struct Match
        {
            public string BreadcrumbPath;
            public SerializedProperty Property;
        }

        internal sealed class ComponentResult
        {
            public Editor Editor;
            public string Title;
            public string FoldoutKey;
            public readonly List<Match> Matches = new List<Match>();
        }

        private static readonly Dictionary<EditorWindow, TransformSearch> _byWindow = new Dictionary<EditorWindow, TransformSearch>();
        private static TransformSearch _fallback;
        private static int _undoVersion;

        public string Query { get; private set; } = "";
        public readonly List<ComponentResult> Results = new List<ComponentResult>();

        private readonly EditorWindow _window;
        private readonly ActiveEditorTracker _tracker;
        private string _builtQuery = "";
        private Editor[] _builtEditors = Array.Empty<Editor>();
        private int _builtUndoVersion = -1;
        private readonly Dictionary<string, bool> _resultFoldouts = new Dictionary<string, bool>();

        private TransformSearch(EditorWindow window, ActiveEditorTracker tracker)
        {
            _window = window;
            _tracker = tracker;
        }

        static TransformSearch()
        {
            Selection.selectionChanged += OnSelectionChanged;
            Undo.undoRedoPerformed += () => _undoVersion++;
        }

        // Finds the search for the Inspector window that is drawing this editor (falls back to the main Inspector).
        public static TransformSearch For(Editor editor)
        {
            foreach (EditorWindow window in InspectorWindows.All())
            {
                ActiveEditorTracker tracker = InspectorWindows.GetTracker(window);
                if (tracker == null || Array.IndexOf(tracker.activeEditors, editor) < 0) continue;
                if (!_byWindow.TryGetValue(window, out TransformSearch search))
                {
                    search = new TransformSearch(window, tracker);
                    _byWindow.Add(window, search);
                }
                return search;
            }

            if (_fallback == null) _fallback = new TransformSearch(null, ActiveEditorTracker.sharedTracker);
            return _fallback;
        }

        public void SetQuery(string query)
        {
            Query = query ?? "";
            if (string.IsNullOrWhiteSpace(Query)) End();
        }

        public bool IsResultExpanded(string key) => !_resultFoldouts.TryGetValue(key, out bool expanded) || expanded;
        public void SetResultExpanded(string key, bool expanded) => _resultFoldouts[key] = expanded;

        // Rebuilds the matches only when the query, the inspected components or undo state changed.
        public void Refresh()
        {
            string query = Query.Trim().ToLowerInvariant();
            Editor[] editors = _tracker.activeEditors ?? Array.Empty<Editor>();
            if (query == _builtQuery && _builtUndoVersion == _undoVersion && SameEditors(editors, _builtEditors)) return;

            _builtQuery = query;
            _builtUndoVersion = _undoVersion;
            _builtEditors = (Editor[])editors.Clone();
            Results.Clear();
            if (query.Length == 0) return;

            foreach (Editor editor in editors)
            {
                if (!IsSearchable(editor)) continue;
                ComponentResult result = BuildResult(editor, query);
                if (result.Matches.Count > 0) Results.Add(result);
            }
        }

        private static ComponentResult BuildResult(Editor editor, string query)
        {
            Type type = editor.target.GetType();
            var result = new ComponentResult
            {
                Editor = editor,
                Title = ObjectNames.NicifyVariableName(type.Name),
                FoldoutKey = editor.target.GetEntityId().ToString() + "_" + type.FullName,
            };

            bool componentNameMatches = type.Name.ToLowerInvariant().Contains(query);
            SerializedProperty iterator = editor.serializedObject.GetIterator();
            if (iterator.NextVisible(true))
            {
                do
                {
                    if (iterator.name == "m_Script") continue;
                    FindMatches(iterator.Copy(), query, componentNameMatches, "", result.Matches);
                }
                while (iterator.NextVisible(false));
            }
            return result;
        }

        private static void FindMatches(SerializedProperty property, string query, bool matchAll, string currentPath, List<Match> results)
        {
            if (IsLeaf(property))
            {
                if (matchAll || property.name.ToLowerInvariant().Contains(query) || property.displayName.ToLowerInvariant().Contains(query))
                {
                    results.Add(new Match { BreadcrumbPath = currentPath, Property = property.Copy() });
                }
                return;
            }

            string path = string.IsNullOrEmpty(currentPath) ? property.displayName : currentPath + " > " + property.displayName;
            SerializedProperty end = property.GetEndProperty();
            if (!property.NextVisible(true)) return;
            do
            {
                if (SerializedProperty.EqualContents(property, end)) break;
                FindMatches(property.Copy(), query, matchAll, path, results);
            }
            while (property.NextVisible(false));
        }

        // Fields Unity draws as one control. Arrays and lists are matched by their own name only, never element by element.
        private static bool IsLeaf(SerializedProperty property)
        {
            if (!property.hasChildren || property.isArray) return true;
            switch (property.propertyType)
            {
                case SerializedPropertyType.String:
                case SerializedPropertyType.ObjectReference:
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector4:
                case SerializedPropertyType.Vector2Int:
                case SerializedPropertyType.Vector3Int:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.AnimationCurve:
                case SerializedPropertyType.Bounds:
                case SerializedPropertyType.BoundsInt:
                case SerializedPropertyType.Rect:
                case SerializedPropertyType.RectInt:
                case SerializedPropertyType.Color:
                case SerializedPropertyType.Quaternion:
                    return true;
                default:
                    return false;
            }
        }

        // Only real components the Inspector shows: no GameObject/Transform header, no hidden data components
        // (e.g. URP/HDRP additional light data), no material or importer editors.
        internal static bool IsSearchable(Editor editor)
        {
            if (editor == null || editor.target == null || editor.serializedObject == null) return false;
            if (!(editor.target is Component) || editor.target is Transform) return false;
            return (editor.target.hideFlags & HideFlags.HideInInspector) == 0;
        }

        // Clears the query and forgets its results.
        private void End()
        {
            Query = "";
            _resultFoldouts.Clear();
            Results.Clear();
            _builtQuery = "";
            _builtEditors = Array.Empty<Editor>();
            if (_window != null) _window.Repaint();
        }

        private static void OnSelectionChanged()
        {
            var closed = new List<EditorWindow>();
            foreach (var pair in _byWindow)
            {
                if (pair.Key == null) { pair.Value.End(); closed.Add(pair.Key); continue; }
                // a locked Inspector keeps showing the same object, so it keeps its search
                if (!pair.Value._tracker.isLocked) pair.Value.End();
            }
            foreach (EditorWindow window in closed) _byWindow.Remove(window);
            _fallback?.End();
        }

        private static bool SameEditors(Editor[] a, Editor[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }

    // Reflection access to Unity's internal InspectorWindow list (used to give each Inspector its own search).
    internal static class InspectorWindows
    {
        private static readonly Type _type = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
        private static readonly MethodInfo _getAll = _type?.GetMethod("GetAllInspectorWindows", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo _tracker = _type?.GetProperty("tracker", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static IEnumerable<EditorWindow> All()
        {
            if (_getAll == null || _tracker == null) yield break;
            if (!(_getAll.Invoke(null, null) is Array windows)) yield break;
            foreach (object window in windows)
            {
                if (window is EditorWindow editorWindow && editorWindow != null) yield return editorWindow;
            }
        }

        public static ActiveEditorTracker GetTracker(EditorWindow window)
        {
            return _tracker != null ? _tracker.GetValue(window) as ActiveEditorTracker : null;
        }
    }
}
