using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace UnityTools.ToolbarButtons
{
    [InitializeOnLoad]
    public class ToolbarTimeSlider : EditorToolbarButton
    {
        private const float _min = 0f;
        private const float _max = 4f;
        private const string _id = "Timescale Options";

        [MainToolbarElement(_id, defaultDockPosition = MainToolbarDockPosition.Middle)]
        public static IEnumerable<MainToolbarElement> Combined()
        {
            yield return TimescaleSlider();
            yield return TimeResetButton();
        }

        public static MainToolbarElement TimescaleSlider()
        {
            var content = new MainToolbarContent("Time Scale", "Time Scale");
            var slider = new MainToolbarSlider(content, Time.timeScale, _min, _max, (value) => Time.timeScale = value);

            return slider;
        }

        public static MainToolbarElement TimeResetButton()
        {
            var icon = EditorGUIUtility.IconContent("Refresh").image as Texture2D;
            var content = new MainToolbarContent(icon, "Reset");
            var button = new MainToolbarButton(content, () =>
            {
                Time.timeScale = 1f;
                MainToolbar.Refresh(_id);
            });

            return button;
        }
    }
}