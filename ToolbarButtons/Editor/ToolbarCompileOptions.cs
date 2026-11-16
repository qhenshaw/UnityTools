using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.Toolbars;
using UnityEngine;

namespace UnityTools.Toolbar
{
    /// <summary>The Auto Refresh mode (all three of Unity's modes) and a button that always recompiles.</summary>
    [InitializeOnLoad]
    public class ToolbarCompileOptions : EditorToolbarButton
    {
        private const string _id = "Compilation Options";
        internal const string AutoRefreshKey = "kAutoRefreshMode";

        /// <summary>Unity's Auto Refresh preference values.</summary>
        internal enum AutoRefreshMode
        {
            Disabled = 0,
            Enabled = 1,
            EnabledOutsidePlaymode = 2,
        }

        internal static readonly string[] ModeLabels = { "Disabled", "Enabled", "Enabled Outside Playmode" };

        [MainToolbarElement(_id, defaultDockPosition = MainToolbarDockPosition.Middle)]
        public static IEnumerable<MainToolbarElement> Combined()
        {
            yield return RefreshModeDropdown();
            yield return RecompileButton();
        }

        internal static AutoRefreshMode CurrentMode
        {
            get
            {
                int value = EditorPrefs.GetInt(AutoRefreshKey, (int)AutoRefreshMode.Enabled);
                return value >= 0 && value <= 2 ? (AutoRefreshMode)value : AutoRefreshMode.Enabled;
            }
        }

        internal static void SetMode(AutoRefreshMode mode)
        {
            EditorPrefs.SetInt(AutoRefreshKey, (int)mode);
            if (mode != AutoRefreshMode.Disabled) AssetDatabase.Refresh();
            MainToolbar.Refresh(_id);
        }

        public static MainToolbarElement RefreshModeDropdown()
        {
            AutoRefreshMode mode = CurrentMode;
            var content = new MainToolbarContent($"Auto Compile: {ModeLabels[(int)mode]}",
                "Auto Refresh: whether Unity reimports and recompiles changed files by itself");
            return new MainToolbarDropdown(content, rect =>
            {
                var menu = new GenericMenu();
                for (int i = 0; i < ModeLabels.Length; i++)
                {
                    var option = (AutoRefreshMode)i;
                    menu.AddItem(new GUIContent(ModeLabels[i]), CurrentMode == option, () => SetMode(option));
                }
                menu.DropDown(rect);
            });
        }

        public static MainToolbarElement RecompileButton()
        {
            var icon = EditorGUIUtility.IconContent("Import").image as Texture2D;
            var content = new MainToolbarContent(icon, "Refresh and recompile scripts");
            return new MainToolbarButton(content, Recompile);
        }

        /// <summary>Picks up new and changed files (even with Auto Refresh off), then always recompiles.</summary>
        internal static void Recompile()
        {
            AssetDatabase.Refresh();
            CompilationPipeline.RequestScriptCompilation();
        }
    }
}
