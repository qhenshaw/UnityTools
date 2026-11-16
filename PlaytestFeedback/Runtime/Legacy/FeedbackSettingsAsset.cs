using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace UnityTools.PlaytestFeedback
{
    /// <summary>
    /// The settings asset of the standalone Playtest Feedback package (Assets/Resources/PlaytestFeedbackSettings.asset).
    /// Kept only so that asset can still be loaded once and copied into Project Settings > Unity Tools >
    /// Playtest Feedback; this script keeps the old script's GUID for that. Don't use it.
    /// </summary>
    [Obsolete("Playtest Feedback settings now live in Project Settings > Unity Tools > Playtest Feedback. Use FeedbackSettings.Current.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class FeedbackSettingsAsset : ScriptableObject
    {
        public string webhookUrl = "";
        public bool postToForumChannel = true;
        public string botUsername = "Playtest Feedback";
        public string botAvatarUrl = "";
        public bool autoCreate = true;
        public bool enabledInEditor = true;
        public bool developmentBuildsOnly = false;
        public InputAction toggleAction;
        public bool pauseTime = true;
        public bool pauseAudio = true;
        public bool disableGameInput = true;
        public bool isolateUiInput = true;
        public string[] actionMapsToKeepEnabled = Array.Empty<string>();
        public bool askForPlayerName = true;
        public bool requireCategory = true;
        public int maxTextLength = 3000;
        public List<FeedbackCategory> categories = new List<FeedbackCategory>();
        public int jpgQuality = 85;
        public int maxImageSize = 1920;
        public bool includeLogs = true;
        public int logLines = 300;
        public bool includeStackTraces = true;
        public bool showGameContextInEmbed = true;
        public bool queueFailedSubmissions = true;

        // Dropped in UnityTools: settings are copied into builds as JSON, which can't hold asset references.
        public PanelSettings panelSettingsOverride;
        public StyleSheet extraStyleSheet;
    }
}
