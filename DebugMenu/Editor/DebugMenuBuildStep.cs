using System.IO;
using UnityEditor;
using UnityEditor.Build;

namespace UnityTools.DebugTools.Editor
{
    /// <summary>
    /// Builds that include the debug menu (Development builds, or any build with the DEBUG_MENU define) get a copy
    /// of the Project Settings in StreamingAssets. Nothing is written to Assets/.
    /// </summary>
    internal class DebugMenuBuildStep : BuildPlayerProcessor
    {
        private const string TempFolder = "Temp/UnityToolsBuild";

        public override void PrepareForBuild(BuildPlayerContext context)
        {
            BuildPlayerOptions options = context.BuildPlayerOptions;
            if (!IncludesDebugMenu(options)) return;

            Directory.CreateDirectory(TempFolder);
            string path = Path.Combine(TempFolder, "DebugMenuSettings.json");
            File.WriteAllText(path, DebugMenuProjectSettings.instance.Settings.ToJson());
            context.AddAdditionalPathToStreamingAssets(path, DebugMenuSettings.StreamingAssetsPath);
        }

        internal static bool IncludesDebugMenu(BuildPlayerOptions options)
        {
            if ((options.options & BuildOptions.Development) != 0) return true;
            if (options.extraScriptingDefines != null && System.Array.IndexOf(options.extraScriptingDefines, "DEBUG_MENU") >= 0) return true;

            NamedBuildTarget target = NamedBuildTarget.FromBuildTargetGroup(options.targetGroup);
            foreach (string define in PlayerSettings.GetScriptingDefineSymbols(target).Split(';'))
            {
                if (define.Trim() == "DEBUG_MENU") return true;
            }
            return false;
        }
    }
}
