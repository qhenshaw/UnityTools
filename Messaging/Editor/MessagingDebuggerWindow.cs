using System.Collections.Generic;
using ServiceAccess;
using UnityEditor;
using UnityEngine;

namespace UnityTools.Messaging.Editor
{
    /// <summary>
    /// Tools > UnityTools > Messaging Debugger. Lists every message tag (payload type, listeners, remembered payload),
    /// every global variable (type, value, listener count) and every service registered with the ServiceLocator.
    /// </summary>
    public class MessagingDebuggerWindow : EditorWindow
    {
        private const double RefreshInterval = 0.25;

        private readonly List<MessagingService.TagInfo> _tags = new List<MessagingService.TagInfo>();
        private readonly List<GlobalVariableService.VariableInfo> _variables = new List<GlobalVariableService.VariableInfo>();
        private readonly List<ServiceLocator.ServiceInfo> _services = new List<ServiceLocator.ServiceInfo>();
        private bool _showServices = true;
        private readonly HashSet<string> _expandedTags = new HashSet<string>();
        private Vector2 _scroll;
        private string _filter = "";
        private double _lastRefresh;
        private bool _showMessages = true;
        private bool _showVariables = true;

        [MenuItem("Tools/UnityTools/Messaging Debugger")]
        public static void Open() => GetWindow<MessagingDebuggerWindow>("Messaging Debugger");

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            Refresh();
        }

        private void OnDisable() => EditorApplication.update -= OnEditorUpdate;

        private void OnEditorUpdate()
        {
            if (!Application.isPlaying) return;
            if (EditorApplication.timeSinceStartup - _lastRefresh < RefreshInterval) return;
            Refresh();
            Repaint();
        }

        private void Refresh()
        {
            _lastRefresh = EditorApplication.timeSinceStartup;
            _tags.Clear();
            _variables.Clear();
            _services.Clear();
            MessagingService.Instance.CollectDebugInfo(_tags);
            GlobalVariableService.Instance.CollectDebugInfo(_variables);
            ServiceLocator.CollectDebugInfo(_services);
            _tags.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));
            _variables.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));
            _services.Sort((a, b) => string.CompareOrdinal(a.RegisteredAs.Name, b.RegisteredAs.Name));
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _filter = EditorGUILayout.TextField(_filter, EditorStyles.toolbarSearchField);
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60))) Refresh();
            }
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Messages, global variables and services reset when Play starts. Enter Play mode to see them update live.", MessageType.Info);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _showMessages = EditorGUILayout.BeginFoldoutHeaderGroup(_showMessages, $"Messages ({_tags.Count})");
            if (_showMessages) DrawMessages();
            EditorGUILayout.EndFoldoutHeaderGroup();

            _showVariables = EditorGUILayout.BeginFoldoutHeaderGroup(_showVariables, $"Global Variables ({_variables.Count})");
            if (_showVariables) DrawVariables();
            EditorGUILayout.EndFoldoutHeaderGroup();

            _showServices = EditorGUILayout.BeginFoldoutHeaderGroup(_showServices, $"Services ({_services.Count})");
            if (_showServices) DrawServices();
            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.EndScrollView();
        }

        private void DrawServices()
        {
            if (_services.Count == 0) EditorGUILayout.LabelField("No services are registered.", EditorStyles.miniLabel);
            foreach (ServiceLocator.ServiceInfo service in _services)
            {
                if (!Matches(service.RegisteredAs.Name)) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(service.RegisteredAs.Name, GUILayout.MinWidth(120));
                    if (service.IsDestroyed)
                    {
                        EditorGUILayout.LabelField("(destroyed, removed on next lookup)", EditorStyles.miniLabel, GUILayout.MinWidth(80));
                    }
                    else if (service.Service is Object unityObject)
                    {
                        // Enabled so a click pings the object; whatever is dropped on it is ignored.
                        EditorGUILayout.ObjectField(unityObject, unityObject.GetType(), true, GUILayout.MinWidth(80));
                    }
                    else
                    {
                        EditorGUILayout.LabelField(service.Service.GetType().Name, EditorStyles.miniLabel, GUILayout.MinWidth(80));
                    }
                }
            }
        }

        private void DrawMessages()
        {
            if (_tags.Count == 0) EditorGUILayout.LabelField("No message tags have been used yet.", EditorStyles.miniLabel);
            foreach (MessagingService.TagInfo tag in _tags)
            {
                if (!Matches(tag.Tag)) continue;
                bool expanded = _expandedTags.Contains(tag.Tag);
                string summary = $"{tag.Tag}   [{tag.PayloadType}]   {tag.Listeners.Count} listener{(tag.Listeners.Count == 1 ? "" : "s")}";
                if (tag.HasSticky) summary += $"   remembered: {tag.StickyPayload}";

                bool newExpanded = EditorGUILayout.Foldout(expanded, summary, true);
                if (newExpanded != expanded)
                {
                    if (newExpanded) _expandedTags.Add(tag.Tag);
                    else _expandedTags.Remove(tag.Tag);
                }
                if (!newExpanded) continue;

                EditorGUI.indentLevel++;
                if (tag.Listeners.Count == 0) EditorGUILayout.LabelField("(no listeners)", EditorStyles.miniLabel);
                foreach (string listener in tag.Listeners) EditorGUILayout.LabelField(listener, EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawVariables()
        {
            if (_variables.Count == 0) EditorGUILayout.LabelField("No global variables have been used yet.", EditorStyles.miniLabel);
            foreach (GlobalVariableService.VariableInfo variable in _variables)
            {
                if (!Matches(variable.Tag)) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(variable.Tag, GUILayout.MinWidth(120));
                    EditorGUILayout.LabelField(variable.ValueType, EditorStyles.miniLabel, GUILayout.Width(90));
                    EditorGUILayout.LabelField(variable.Value, GUILayout.MinWidth(80));
                    EditorGUILayout.LabelField($"{variable.Listeners} listening", EditorStyles.miniLabel, GUILayout.Width(70));
                }
            }
        }

        private bool Matches(string tag) => string.IsNullOrEmpty(_filter) || tag.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
