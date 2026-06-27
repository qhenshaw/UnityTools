using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace UnityTools.ToolbarButtons
{
    [InitializeOnLoad]
    public class ToolbarCompileOptions : EditorToolbarButton
    {
        private const string _id = "Compilation Options";

        [MainToolbarElement(_id, defaultDockPosition = MainToolbarDockPosition.Middle)]
        public static IEnumerable<MainToolbarElement> Combined()
        {
            yield return RefreshToggle();
            yield return RecompileButton();
        }

        public static MainToolbarElement RefreshToggle()
        {
            var content = new MainToolbarContent("Auto Compile", "Recompile scripts as they are modified");
            bool refreshSetting = EditorPrefs.GetInt("kAutoRefreshMode") == 1;
            var toggle = new MainToolbarToggle(content, refreshSetting, (value) =>
            {
                EditorPrefs.SetInt("kAutoRefreshMode", value ? 1 : 0);
                if (value) AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            });

            return toggle;
        }

        public static MainToolbarElement RecompileButton()
        {
            var icon = EditorGUIUtility.IconContent("Import").image as Texture2D;
            var content = new MainToolbarContent(icon, "Force recompilation");
            var button = new MainToolbarButton(content, () =>
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            });

            return button;
        }
    }
}