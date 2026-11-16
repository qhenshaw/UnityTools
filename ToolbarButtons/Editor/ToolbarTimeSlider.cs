using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace UnityTools.ToolbarButtons
{
    /// <summary>
    /// A Time Scale slider and reset button that work in Play mode only. Exiting Play mode puts the time scale back
    /// to what it was before, so the project's Time Scale setting (ProjectSettings/TimeManager.asset) never changes.
    /// </summary>
    [InitializeOnLoad]
    public class ToolbarTimeSlider : EditorToolbarButton
    {
        private const float _min = 0f;
        private const float _max = 4f;
        private const string _id = "Timescale Options";
        private const string SavedTimeScaleKey = "UnityTools.ToolbarTimeSlider.TimeScaleBeforePlay";

        static ToolbarTimeSlider()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        internal static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    SessionState.SetFloat(SavedTimeScaleKey, Time.timeScale);
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    Time.timeScale = SessionState.GetFloat(SavedTimeScaleKey, Time.timeScale);
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    MainToolbar.Refresh(_id);
                    break;
            }
        }

        [MainToolbarElement(_id, defaultDockPosition = MainToolbarDockPosition.Middle)]
        public static IEnumerable<MainToolbarElement> Combined()
        {
            yield return TimescaleSlider();
            yield return TimeResetButton();
        }

        public static MainToolbarElement TimescaleSlider()
        {
            bool playing = EditorApplication.isPlaying;
            var content = new MainToolbarContent("Time Scale", playing ? "Time Scale" : "Time Scale (Play mode only)");
            var slider = new MainToolbarSlider(content, Time.timeScale, _min, _max, value => SetTimeScale(value))
            {
                enabled = playing
            };
            return slider;
        }

        public static MainToolbarElement TimeResetButton()
        {
            var icon = EditorGUIUtility.IconContent("Refresh").image as Texture2D;
            var content = new MainToolbarContent(icon, "Reset Time Scale to 1 (Play mode only)");
            var button = new MainToolbarButton(content, () =>
            {
                SetTimeScale(1f);
                MainToolbar.Refresh(_id);
            })
            {
                enabled = EditorApplication.isPlaying
            };
            return button;
        }

        /// <summary>Sets the time scale in Play mode; does nothing in edit mode, where it would change the project setting.</summary>
        internal static bool SetTimeScale(float value)
        {
            if (!EditorApplication.isPlaying) return false;
            Time.timeScale = value;
            return true;
        }
    }
}
