using System.IO;
using UnityEditor;
using UnityEditor.Build;

namespace UnityTools.PlaytestFeedback.Editor
{
    /// <summary>
    /// Builds that include the feedback tool get a copy of the Project Settings in StreamingAssets.
    /// Nothing is written to Assets/.
    /// </summary>
    internal class PlaytestFeedbackBuildStep : BuildPlayerProcessor
    {
        private const string TempFolder = "Temp/UnityToolsBuild";

        public override void PrepareForBuild(BuildPlayerContext context)
        {
            FeedbackSettings settings = PlaytestFeedbackProjectSettings.instance.Settings;
            if (!IncludesTool(context.BuildPlayerOptions, settings)) return;

            Directory.CreateDirectory(TempFolder);
            string path = Path.Combine(TempFolder, "PlaytestFeedbackSettings.json");
            File.WriteAllText(path, settings.ToJson());
            context.AddAdditionalPathToStreamingAssets(path, FeedbackSettings.StreamingAssetsPath);
        }

        /// <summary>False when the tool is off for this build: PLAYTEST_FEEDBACK_DISABLED, or Development Builds Only on a release build.</summary>
        internal static bool IncludesTool(BuildPlayerOptions options, FeedbackSettings settings)
        {
            if (HasDefine(options, "PLAYTEST_FEEDBACK_DISABLED")) return false;
            bool development = (options.options & BuildOptions.Development) != 0;
            return development || !settings.developmentBuildsOnly;
        }

        private static bool HasDefine(BuildPlayerOptions options, string define)
        {
            if (options.extraScriptingDefines != null && System.Array.IndexOf(options.extraScriptingDefines, define) >= 0) return true;
            NamedBuildTarget target = NamedBuildTarget.FromBuildTargetGroup(options.targetGroup);
            foreach (string symbol in PlayerSettings.GetScriptingDefineSymbols(target).Split(';'))
            {
                if (symbol.Trim() == define) return true;
            }
            return false;
        }
    }
}
