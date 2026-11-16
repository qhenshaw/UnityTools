using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UnityTools.DebugTools
{
    /// <summary>
    /// Debug menu options, edited in Project Settings > Unity Tools > Debug Menu. In the Editor they're read from
    /// ProjectSettings/; builds that include the menu get a copy in StreamingAssets.
    /// </summary>
    [Serializable]
    public class DebugMenuSettings
    {
        internal const string StreamingAssetsPath = "UnityTools/DebugMenuSettings.json";

        [Tooltip("Opens and closes the menu. With no bindings it uses F5 on keyboard and Select + Start on gamepad.")]
        public InputAction ToggleAction = new InputAction("ToggleDebugMenu", InputActionType.Button);

        [Tooltip("Open the menu as soon as it's created")]
        public bool StartOpen = false;

        [Tooltip("Set the time scale to 0 while the menu is open, and restore the previous time scale when it closes")]
        public bool PauseWhileOpen = false;

        [Tooltip("Turn off every active PlayerInput while the menu is open (so clicking the menu doesn't shoot), and turn them back on when it closes")]
        public bool BlockGameplayInput = false;

        [Tooltip("Add a Game section with FPS, time scale and reload scene")]
        public bool ShowGameSection = true;

        [Min(0.05f), Tooltip("Seconds between refreshes of readouts and bound values")]
        public float RefreshInterval = 0.25f;

        // Set by the editor assembly so the Editor reads the live Project Settings values.
        internal static Func<DebugMenuSettings> EditorSource;
        private static DebugMenuSettings _override;
        private static DebugMenuSettings _loaded;

        /// <summary>
        /// The settings in effect: Project Settings in the Editor, the embedded copy in builds
        /// (the defaults on Android and WebGL, which can't read it).
        /// </summary>
        public static DebugMenuSettings Current
        {
            get
            {
                if (_override != null) return _override;
                if (Application.isEditor && EditorSource != null) return EditorSource() ?? Defaults();
                return _loaded ??= LoadFromStreamingAssets();
            }
        }

        internal static void OverrideForTests(DebugMenuSettings settings) => _override = settings;

        /// <summary>A new settings object with the default values.</summary>
        public static DebugMenuSettings Defaults() => new DebugMenuSettings();

        /// <summary>The settings as JSON, as copied into builds.</summary>
        public string ToJson() => JsonUtility.ToJson(this, true);

        /// <summary>Settings read from JSON made by <see cref="ToJson"/>; missing values keep their defaults.</summary>
        public static DebugMenuSettings FromJson(string json)
        {
            var settings = Defaults();
            if (!string.IsNullOrEmpty(json)) JsonUtility.FromJsonOverwrite(json, settings);
            return settings;
        }

        /// <summary>A copy of the toggle action to enable, with the default bindings if it has none. The settings stay untouched.</summary>
        internal InputAction CreateToggleAction()
        {
            InputAction action = ToggleAction != null && ToggleAction.bindings.Count > 0
                ? ToggleAction.Clone()
                : new InputAction("ToggleDebugMenu", InputActionType.Button);
            if (action.bindings.Count == 0)
            {
                action.AddBinding("<Keyboard>/f5");
                action.AddCompositeBinding("OneModifier")
                    .With("Modifier", "<Gamepad>/select")
                    .With("Binding", "<Gamepad>/start");
            }
            return action;
        }

        private static DebugMenuSettings LoadFromStreamingAssets()
        {
            if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.WebGLPlayer)
            {
                Debug.LogWarning("Debug Menu: Project Settings can't be read from StreamingAssets on this platform, so the defaults are used.");
                return Defaults();
            }

            string path = Path.Combine(Application.streamingAssetsPath, StreamingAssetsPath);
            try
            {
                return File.Exists(path) ? FromJson(File.ReadAllText(path)) : Defaults();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Debug Menu: couldn't read {path}, so the defaults are used. {e.Message}");
                return Defaults();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _loaded = null;
        }
    }
}
