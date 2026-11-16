using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UnityTools.PlaytestFeedback
{
    /// <summary>
    /// A feedback category the player can pick (Bug, Feedback, Idea...).
    /// </summary>
    [Serializable]
    public sealed class FeedbackCategory
    {
        [Tooltip("Name shown on the category button and in the Discord post title.")]
        public string name = "Feedback";

        [Tooltip("Button highlight and Discord embed colour.")]
        public Color color = new Color(0.35f, 0.4f, 0.95f);

        [Tooltip("Optional emoji put in front of the Discord forum post title.")]
        public string discordEmoji = "";

        [Tooltip("Optional Discord forum tag ID (a long number) applied to the post. Leave empty to skip. See the README for how to find tag IDs.")]
        public string forumTagId = "";

        public FeedbackCategory() { }

        public FeedbackCategory(string name, Color color, string discordEmoji)
        {
            this.name = name;
            this.color = color;
            this.discordEmoji = discordEmoji;
        }
    }

    /// <summary>
    /// Settings for the playtest feedback tool, edited in Project Settings > Unity Tools > Playtest Feedback.
    /// In the Editor they're read from ProjectSettings/UnityToolsPlaytestFeedback.asset; builds that include the
    /// tool get a copy in StreamingAssets.
    /// </summary>
    [Serializable]
    public sealed class FeedbackSettings
    {
        internal const string StreamingAssetsPath = "UnityTools/PlaytestFeedbackSettings.json";

        [Header("Discord")]
        [Tooltip("Discord webhook URL (Channel settings > Integrations > Webhooks). It's saved in ProjectSettings/, which is committed to version control, and anyone with a build can extract it. Use one webhook per playtest round and delete it afterwards.")]
        public string webhookUrl = "";

        [Tooltip("Enable if the webhook points at a Forum channel. Each report then becomes its own forum post (thread), and category tags can be applied.")]
        public bool postToForumChannel = true;

        [Tooltip("Name the webhook posts as. Discord rejects names containing 'discord' or 'clyde'.")]
        public string botUsername = "Playtest Feedback";

        [Tooltip("Optional avatar image URL for the webhook posts.")]
        public string botAvatarUrl = "";

        [Header("Availability")]
        [Tooltip("Automatically create the feedback tool when the game starts. Turn off to add a FeedbackManager to a scene yourself.")]
        public bool autoCreate = true;

        [Tooltip("Allow the tool to run in the Unity Editor.")]
        public bool enabledInEditor = true;

        [Tooltip("Only enable the tool in Development Builds (and the Editor).")]
        public bool developmentBuildsOnly = false;

        [Header("Hotkey")]
        // Starts with no bindings on purpose: adding bindings here would build the action's binding cache, and
        // deserializing over it (Project Settings, the build's JSON copy) would then keep the defaults instead of
        // your bindings. With no bindings, F8 / L3 + R3 are used.
        [Tooltip("Opens and closes the feedback panel. Add bindings here; use 'Listen' in the binding path picker to press the key you want. With no bindings it uses F8, or L3 + R3 on a gamepad.")]
        public InputAction toggleAction = new InputAction("Toggle Feedback Panel", InputActionType.Button);

        [Header("While the panel is open")]
        [Tooltip("Set Time.timeScale to 0 while the panel is open.")]
        public bool pauseTime = true;

        [Tooltip("Pause all audio (AudioListener.pause) while the panel is open.")]
        public bool pauseAudio = true;

        [Tooltip("Disable every enabled Input System action while the panel is open (gameplay and UI maps), so nothing in the game reacts to typing, clicks or gamepad input. Re-enabled on close.")]
        public bool disableGameInput = true;

        [Tooltip("While the panel is open, disable the game's EventSystems and drive the panel with a private EventSystem and its own copy of the UI actions. This stops uGUI and UI Toolkit menus behind the panel from receiving clicks, navigation and submit.")]
        public bool isolateUiInput = true;

        [Tooltip("Action maps to leave enabled while the panel is open (for example a push-to-talk map). Empty by default: leaving a map enabled lets the game react to it behind the panel.")]
        public string[] actionMapsToKeepEnabled = Array.Empty<string>();

        [Header("Form")]
        [Tooltip("Show a 'Your name' field. The name is remembered between sessions.")]
        public bool askForPlayerName = true;

        [Tooltip("Require the player to pick a category before sending.")]
        public bool requireCategory = true;

        [Tooltip("Maximum feedback text length. Discord embeds allow up to 4096 characters.")]
        [Range(200, 4000)]
        public int maxTextLength = 3000;

        public List<FeedbackCategory> categories = DefaultCategories();

        [Header("Attachments")]
        [Range(50, 100)]
        public int jpgQuality = 85;

        [Tooltip("Screenshots larger than this (in pixels, longest side) are scaled down. 0 = never scale.")]
        public int maxImageSize = 1920;

        [Tooltip("Attach the most recent log messages as log.txt.")]
        public bool includeLogs = true;

        [Range(20, 1000)]
        public int logLines = 300;

        [Tooltip("Include stack traces for errors, exceptions and asserts in log.txt.")]
        public bool includeStackTraces = true;

        [Tooltip("Show game context values (from IFeedbackContextProvider / FeedbackManager.AddContext) as fields in the Discord post. They are always included in context.json.")]
        public bool showGameContextInEmbed = true;

        [Header("Reliability")]
        [Tooltip("If a report can't be sent (offline, Discord down), save it to disk and retry on the next launch.")]
        public bool queueFailedSubmissions = true;

        /// <summary>
        /// Set at runtime to use a different webhook than the one in the settings
        /// (for example one read from a command-line argument or a file next to the build).
        /// </summary>
        public static string WebhookUrlOverride { get; set; }

        /// <summary>The webhook reports are sent to: <see cref="WebhookUrlOverride"/> if set, otherwise the Webhook URL setting.</summary>
        public string EffectiveWebhookUrl =>
            string.IsNullOrWhiteSpace(WebhookUrlOverride) ? webhookUrl?.Trim() : WebhookUrlOverride.Trim();

        /// <summary>Whether Enabled In Editor (in the Editor) or Development Builds Only (in builds) lets the tool run here.</summary>
        public bool IsEnabledForThisBuild
        {
            get
            {
                if (Application.isEditor) return enabledInEditor;
                return !developmentBuildsOnly || Debug.isDebugBuild;
            }
        }

        // ------------------------------------------------------------------ Where the settings come from

        // Set by the editor assembly so the Editor reads the live Project Settings values.
        internal static Func<FeedbackSettings> EditorSource;
        private static FeedbackSettings _override;
        private static FeedbackSettings _loaded;

        /// <summary>
        /// The settings in effect: Project Settings in the Editor, the embedded copy in builds
        /// (the defaults, with no webhook, on Android and WebGL, which can't read it).
        /// </summary>
        public static FeedbackSettings Current
        {
            get
            {
                if (_override != null) return _override;
                if (Application.isEditor && EditorSource != null) return EditorSource() ?? new FeedbackSettings();
                return _loaded ??= LoadFromStreamingAssets();
            }
        }

        internal static void OverrideForTests(FeedbackSettings settings) => _override = settings;

        /// <summary>The settings as JSON, as copied into builds.</summary>
        public string ToJson() => JsonUtility.ToJson(this, true);

        /// <summary>Settings read from JSON made by <see cref="ToJson"/>; missing values keep their defaults.</summary>
        public static FeedbackSettings FromJson(string json)
        {
            var settings = new FeedbackSettings();
            if (!string.IsNullOrEmpty(json)) JsonUtility.FromJsonOverwrite(json, settings);
            return settings;
        }

        private static FeedbackSettings LoadFromStreamingAssets()
        {
            if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.WebGLPlayer)
            {
                Debug.LogWarning("[PlaytestFeedback] Project Settings can't be read from StreamingAssets on this platform, so no webhook is set. Set FeedbackSettings.WebhookUrlOverride at startup to send reports.");
                return new FeedbackSettings();
            }

            string path = Path.Combine(Application.streamingAssetsPath, StreamingAssetsPath);
            try
            {
                if (File.Exists(path)) return FromJson(File.ReadAllText(path));
                Debug.LogWarning($"[PlaytestFeedback] {path} is missing, so the default settings (and no webhook) are used.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlaytestFeedback] Couldn't read {path}, so the default settings are used. {e.Message}");
            }
            return new FeedbackSettings();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _loaded = null;
        }

        // ------------------------------------------------------------------ Defaults and helpers

        /// <summary>The default categories: Bug, Feedback and Idea.</summary>
        public static List<FeedbackCategory> DefaultCategories() => new List<FeedbackCategory>
        {
            new FeedbackCategory("Bug", new Color(0.93f, 0.26f, 0.27f), "\U0001F41E"),
            new FeedbackCategory("Feedback", new Color(0.35f, 0.4f, 0.95f), "\U0001F4AC"),
            new FeedbackCategory("Idea", new Color(0.98f, 0.75f, 0.18f), "\U0001F4A1"),
        };

        /// <summary>A new hotkey action with the default bindings: F8 on keyboard, L3 + R3 (in either order) on gamepad.</summary>
        public static InputAction CreateDefaultToggleAction()
        {
            var action = new InputAction("Toggle Feedback Panel", InputActionType.Button);
            action.AddBinding("<Keyboard>/f8");
            // modifiersOrder=2 (Unordered): the two buttons can be pressed in any order.
            action.AddCompositeBinding("OneModifier(modifiersOrder=2)")
                .With("Modifier", "<Gamepad>/leftStickPress")
                .With("Binding", "<Gamepad>/rightStickPress");
            return action;
        }

        /// <summary>A copy of the configured hotkey for runtime use, or the default if none is set up.</summary>
        internal InputAction CreateToggleActionInstance()
        {
            if (toggleAction == null || toggleAction.bindings.Count == 0) return CreateDefaultToggleAction();

            // Clone so enabling it at runtime never touches the settings.
            var clone = toggleAction.Clone();
            clone.Rename("Toggle Feedback Panel");
            return clone;
        }

        /// <summary>True if <paramref name="url"/> looks like a Discord webhook URL (https://discord.com/api/webhooks/...).</summary>
        public static bool IsValidWebhookUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            url = url.Trim();
            return url.StartsWith("https://discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://discordapp.com/api/webhooks/", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://canary.discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://ptb.discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
