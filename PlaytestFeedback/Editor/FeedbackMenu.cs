using System.IO;
using UnityTools.PlaytestFeedback.Internal;
using UnityEditor;

namespace UnityTools.PlaytestFeedback.Editor
{
    internal static class FeedbackMenu
    {
        [MenuItem("Tools/UnityTools/Playtest Feedback/Open Settings", priority = 0)]
        private static void OpenSettings()
        {
            SettingsService.OpenProjectSettings(PlaytestFeedbackSettingsPage.Path);
        }

        [MenuItem("Tools/UnityTools/Playtest Feedback/Open Outbox Folder", priority = 20)]
        private static void OpenOutboxFolder()
        {
            Directory.CreateDirectory(FeedbackOutbox.FolderPath);
            EditorUtility.RevealInFinder(FeedbackOutbox.FolderPath);
        }
    }
}
