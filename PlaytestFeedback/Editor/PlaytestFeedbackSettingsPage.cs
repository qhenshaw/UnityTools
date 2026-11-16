using UnityTools.PlaytestFeedback.Internal;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityTools.PlaytestFeedback.Editor
{
    /// <summary>Project Settings > Unity Tools > Playtest Feedback.</summary>
    internal static class PlaytestFeedbackSettingsPage
    {
        internal const string Path = "Project/Unity Tools/Playtest Feedback";

        private static bool s_ShowWebhook;
        private static string s_TestStatus;
        private static MessageType s_TestStatusType;
        private static bool s_TestRunning;

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(Path, SettingsScope.Project)
            {
                label = "Playtest Feedback",
                guiHandler = _ => Draw(),
                keywords = new[] { "Playtest", "Feedback", "Discord", "Webhook", "Bug", "Report", "Screenshot" }
            };
        }

        private static void Draw()
        {
            PlaytestFeedbackProjectSettings project = PlaytestFeedbackProjectSettings.instance;
            var so = new SerializedObject(project);
            SerializedProperty root = so.FindProperty("_settings");

            EditorGUILayout.HelpBox(
                "Press the hotkey in Play mode to open the feedback panel (F8, or L3 + R3 on a gamepad, unless changed under Hotkey below).",
                MessageType.Info);

            DrawWebhookSection(project, so, root.FindPropertyRelative(nameof(FeedbackSettings.webhookUrl)));

            SerializedProperty property = root.Copy();
            SerializedProperty end = root.GetEndProperty();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren) && !SerializedProperty.EqualContents(property, end))
            {
                enterChildren = false;
                if (property.name == nameof(FeedbackSettings.webhookUrl)) continue;
                EditorGUILayout.PropertyField(property, true);
            }

            if (so.ApplyModifiedProperties()) project.SaveSettings();
        }

        private static void DrawWebhookSection(PlaytestFeedbackProjectSettings project, SerializedObject so, SerializedProperty webhook)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Discord Webhook", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                var label = new GUIContent("Webhook URL", webhook.tooltip);
                webhook.stringValue = s_ShowWebhook
                    ? EditorGUILayout.TextField(label, webhook.stringValue)
                    : EditorGUILayout.PasswordField(label, webhook.stringValue);
                s_ShowWebhook = GUILayout.Toggle(s_ShowWebhook, "Show", EditorStyles.miniButton, GUILayout.Width(50));
            }

            string url = webhook.stringValue?.Trim();
            bool valid = FeedbackSettings.IsValidWebhookUrl(url);
            if (string.IsNullOrEmpty(url))
            {
                EditorGUILayout.HelpBox("Paste a webhook URL from Discord: Channel settings > Integrations > Webhooks > New Webhook > Copy Webhook URL.", MessageType.Warning);
            }
            else if (!valid)
            {
                EditorGUILayout.HelpBox("This doesn't look like a Discord webhook URL (https://discord.com/api/webhooks/...).", MessageType.Error);
            }

            EditorGUILayout.HelpBox(
                "The URL is saved in ProjectSettings/UnityToolsPlaytestFeedback.asset, which is committed with your project: " +
                "if the repository is public, anyone can find it. Anyone with a build can also extract it. Use a separate webhook " +
                "per playtest round, and delete it in Discord when the round ends or if it leaks.",
                MessageType.Warning);

            using (new EditorGUI.DisabledScope(!valid || s_TestRunning))
            {
                if (GUILayout.Button(s_TestRunning ? "Sending…" : "Send Test Message"))
                {
                    if (so.ApplyModifiedProperties()) project.SaveSettings();
                    SendTestMessage(project.Settings, url);
                }
            }

            if (!string.IsNullOrEmpty(s_TestStatus)) EditorGUILayout.HelpBox(s_TestStatus, s_TestStatusType);
            EditorGUILayout.Space();
        }

        private static void SendTestMessage(FeedbackSettings settings, string url)
        {
            var request = DiscordWebhook.BuildTest(settings);
            var www = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(request.Body) { contentType = request.ContentType },
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 20,
            };
            www.SetRequestHeader("Content-Type", request.ContentType);

            s_TestRunning = true;
            s_TestStatus = null;

            www.SendWebRequest().completed += _ =>
            {
                if (www.result == UnityWebRequest.Result.Success)
                {
                    s_TestStatus = "Test message sent. Check your Discord channel.";
                    s_TestStatusType = MessageType.Info;
                }
                else
                {
                    string body = www.downloadHandler?.text;
                    s_TestStatus = $"Failed ({www.responseCode} {www.error}).\n{DiscordWebhook.Truncate(body ?? "", 400)}";
                    if (body != null && body.Contains("thread_name"))
                    {
                        s_TestStatus += "\nIf the webhook isn't in a Forum channel, untick 'Post To Forum Channel'.";
                    }
                    s_TestStatusType = MessageType.Error;
                }

                s_TestRunning = false;
                www.Dispose();
                SettingsService.RepaintAllSettingsWindow();
            };
        }
    }
}
