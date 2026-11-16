using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace SceneManagement.Editor
{
    /// <summary>
    /// Draws a [SceneDropdown] string as a button that opens a searchable scene picker (Unity's AdvancedDropdown,
    /// like the Add Component menu). Scenes are grouped by folder; the search matches scene names and folders.
    /// </summary>
    [CustomPropertyDrawer(typeof(SceneDropdownAttribute))]
    public class SceneDropdownDrawer : PropertyDrawer
    {
        private const string NoneLabel = "None";
        private const float AddToBuildWidth = 84f;

        // Scene paths in Assets/, rebuilt when the project changes rather than on every repaint.
        private static string[] s_ScenePaths;

        private static readonly AdvancedDropdownState s_DropdownState = new AdvancedDropdownState();

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.projectChanged += () => s_ScenePaths = null;
        }

        private static string[] ScenePaths
        {
            get
            {
                if (s_ScenePaths == null)
                {
                    s_ScenePaths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                        .Select(AssetDatabase.GUIDToAssetPath)
                        .Where(path => path.EndsWith(".unity"))
                        .OrderBy(path => path)
                        .ToArray();
                }
                return s_ScenePaths;
            }
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.HelpBox(position, $"[SceneDropdown] on '{property.name}' requires a string field.", MessageType.Error);
                return;
            }

            // Odin and other inspectors may pick this drawer over [InlineButton]'s; keep the button either way.
            position = InspectorAttributes.Editors.InlineButtonGUI.Draw(position, property, fieldInfo);

            bool storeName = attribute is SceneDropdownAttribute sceneAttribute && sceneAttribute.StoreName;
            string stored = property.stringValue;

            // Find the scene(s) the stored value points at. A name can match several scenes in different folders.
            string[] matches = string.IsNullOrEmpty(stored) ? Array.Empty<string>()
                : storeName ? ScenePaths.Where(p => Path.GetFileNameWithoutExtension(p) == stored).ToArray()
                : ScenePaths.Contains(Resolve(stored)) ? new[] { Resolve(stored) } : Array.Empty<string>();
            bool missing = !string.IsNullOrEmpty(stored) && matches.Length == 0;
            bool ambiguous = matches.Length > 1;

            bool notInBuild = !property.hasMultipleDifferentValues && matches.Length > 0
                && !SceneBuildCheck.IsInBuild(storeName ? stored : matches[0], storeName);

            EditorGUI.BeginProperty(position, label, property);
            Rect buttonRect = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

            if (notInBuild)
            {
                var addRect = new Rect(buttonRect.xMax - AddToBuildWidth, buttonRect.y, AddToBuildWidth, buttonRect.height);
                buttonRect.width -= AddToBuildWidth + 2f;
                var addContent = new GUIContent("Add to Build", $"Add {matches[0]} to Build Settings so it can be loaded at runtime.");
                if (GUI.Button(addRect, addContent, EditorStyles.miniButton))
                {
                    SceneBuildCheck.AddToBuild(matches[0]);
                    GUIUtility.ExitGUI();
                }
            }

            string text = property.hasMultipleDifferentValues ? "—"
                : string.IsNullOrEmpty(stored) ? NoneLabel
                : missing ? $"Missing: {stored}"
                : ambiguous ? $"{stored}  ({matches.Length} scenes have this name)"
                : RelativeName(matches[0]);
            if (notInBuild) text += "  (not in build)";
            string tooltip = ambiguous ? $"Loading by name picks one of:\n{string.Join("\n", matches)}"
                : matches.Length == 1 ? matches[0]
                : null;
            if (notInBuild) tooltip = "Not in Build Settings: loading it at runtime fails.\n" + tooltip;
            var content = new GUIContent(text, tooltip);

            Color previous = GUI.color;
            if (missing) GUI.color = new Color(1f, 0.55f, 0.5f);
            else if (notInBuild) GUI.color = new Color(1f, 0.7f, 0.4f);
            else if (ambiguous) GUI.color = new Color(1f, 0.85f, 0.45f);
            if (EditorGUI.DropdownButton(buttonRect, content, FocusType.Keyboard))
            {
                // The pick arrives after this GUI pass, so write it through a fresh SerializedObject (keeps Undo
                // and multi-object editing working) instead of the SerializedProperty from this frame.
                SerializedObject serializedObject = property.serializedObject;
                string path = property.propertyPath;
                new SceneDropdown(s_DropdownState, ScenePaths, picked =>
                {
                    if (serializedObject.targetObject == null) return;
                    serializedObject.Update();
                    SerializedProperty target = serializedObject.FindProperty(path);
                    if (target == null) return;
                    target.stringValue = storeName && !string.IsNullOrEmpty(picked) ? Path.GetFileNameWithoutExtension(picked) : picked;
                    serializedObject.ApplyModifiedProperties();
                }).Show(buttonRect);
            }
            GUI.color = previous;
            EditorGUI.EndProperty();
        }

        /// <summary>
        /// Older Add New Scene versions saved paths like "Assets/Levels//Level-A.unity"; match them to the real scene.
        /// </summary>
        private static string Resolve(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.Contains("//")) return path;
            string normalized = path.Replace("//", "/");
            return ScenePaths.Contains(normalized) ? normalized : path;
        }

        /// <summary>"Assets/Scenes/Level 1.unity" → "Scenes/Level 1".</summary>
        private static string RelativeName(string path)
        {
            string withoutAssets = path.StartsWith("Assets/") ? path.Substring("Assets/".Length) : path;
            return withoutAssets.EndsWith(".unity") ? withoutAssets.Substring(0, withoutAssets.Length - ".unity".Length) : withoutAssets;
        }

        private sealed class SceneDropdown : AdvancedDropdown
        {
            // AdvancedDropdownItem.displayName is internal: the search matches 'name', the list shows displayName.
            private static readonly PropertyInfo s_DisplayName =
                typeof(AdvancedDropdownItem).GetProperty("displayName", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            private readonly string[] _paths;
            private readonly Action<string> _onPicked;

            public SceneDropdown(AdvancedDropdownState state, string[] paths, Action<string> onPicked) : base(state)
            {
                _paths = paths;
                _onPicked = onPicked;
                minimumSize = new Vector2(280f, 320f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("Scenes");
                root.AddChild(new SceneItem(NoneLabel, NoneLabel, string.Empty));
                root.AddSeparator();

                // Scenes sharing a name show their folder, so search results (a flat list) stay unambiguous.
                var duplicateNames = new HashSet<string>(_paths
                    .GroupBy(Path.GetFileNameWithoutExtension)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key));

                var folders = new Dictionary<string, AdvancedDropdownItem>();
                foreach (string path in _paths)
                {
                    string relative = RelativeName(path);
                    string folder = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? string.Empty;
                    string sceneName = Path.GetFileName(relative);

                    string display = duplicateNames.Contains(sceneName) && folder.Length > 0
                        ? $"{sceneName}  ({Path.GetFileName(folder)})"
                        : sceneName;
                    // Searchable text: scene name plus its folders, so "levelstreaming sub" finds the sub levels.
                    string searchable = folder.Length > 0 ? $"{sceneName} {folder}" : sceneName;

                    GetFolder(root, folders, folder).AddChild(new SceneItem(searchable, display, path));
                }
                return root;
            }

            private static AdvancedDropdownItem GetFolder(AdvancedDropdownItem root, Dictionary<string, AdvancedDropdownItem> folders, string folder)
            {
                if (string.IsNullOrEmpty(folder)) return root;
                if (folders.TryGetValue(folder, out AdvancedDropdownItem existing)) return existing;

                string parentFolder = Path.GetDirectoryName(folder)?.Replace('\\', '/');
                var item = new AdvancedDropdownItem(Path.GetFileName(folder));
                GetFolder(root, folders, parentFolder).AddChild(item);
                folders[folder] = item;
                return item;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item is SceneItem scene) _onPicked(scene.Path);
            }

            private sealed class SceneItem : AdvancedDropdownItem
            {
                public string Path { get; }

                public SceneItem(string searchable, string display, string path) : base(display)
                {
                    Path = path;
                    if (s_DisplayName == null || !s_DisplayName.CanWrite) return;
                    try
                    {
                        // Search on the full text, show the short one. Without this (if HDRP/Unity changes the
                        // internal property) the item simply shows its display text and searches on it.
                        name = searchable;
                        s_DisplayName.SetValue(this, display);
                    }
                    catch (Exception)
                    {
                        name = display;
                    }
                }
            }
        }
    }
}
