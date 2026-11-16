using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityTools.PlaytestFeedback.Internal
{
    /// <summary>
    /// Collects the automatic metadata attached to every report.
    /// </summary>
    internal static class SystemInfoCollector
    {
        public static readonly string SessionId = Guid.NewGuid().ToString("N").Substring(0, 8);

        public static Dictionary<string, object> Collect(float averageFps, float worstFrameMs)
        {
            var scenes = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                scenes.Add(SceneManager.GetSceneAt(i).name);
            }

            var resolution = Screen.currentResolution;

            return new Dictionary<string, object>
            {
                ["gameVersion"] = Application.version,
                ["productName"] = Application.productName,
                ["unityVersion"] = Application.unityVersion,
                ["buildGuid"] = Application.buildGUID,
                ["developmentBuild"] = Debug.isDebugBuild,
                ["isEditor"] = Application.isEditor,
                ["platform"] = Application.platform.ToString(),
                ["operatingSystem"] = SystemInfo.operatingSystem,
                ["deviceModel"] = SystemInfo.deviceModel,
                ["cpu"] = SystemInfo.processorType,
                ["cpuCores"] = SystemInfo.processorCount,
                ["systemMemoryMB"] = SystemInfo.systemMemorySize,
                ["gpu"] = SystemInfo.graphicsDeviceName,
                ["gpuVendor"] = SystemInfo.graphicsDeviceVendor,
                ["gpuMemoryMB"] = SystemInfo.graphicsMemorySize,
                ["graphicsApi"] = SystemInfo.graphicsDeviceType.ToString(),
                ["resolution"] = $"{Screen.width}x{Screen.height}",
                ["displayResolution"] = $"{resolution.width}x{resolution.height}",
                ["refreshRate"] = Math.Round(resolution.refreshRateRatio.value, 1),
                ["fullScreenMode"] = Screen.fullScreenMode.ToString(),
                ["qualityLevel"] = QualitySettings.names.Length > 0
                    ? QualitySettings.names[QualitySettings.GetQualityLevel()]
                    : QualitySettings.GetQualityLevel().ToString(),
                ["language"] = Application.systemLanguage.ToString(),
                ["activeScene"] = SceneManager.GetActiveScene().name,
                ["loadedScenes"] = scenes,
                ["playtimeSeconds"] = Math.Round(Time.realtimeSinceStartupAsDouble, 1),
                ["averageFps"] = Mathf.Round(averageFps * 10f) / 10f,
                ["worstFrameMs"] = Mathf.Round(worstFrameMs * 10f) / 10f,
                ["logErrors"] = LogBuffer.ErrorCount,
                ["logExceptions"] = LogBuffer.ExceptionCount,
                ["logWarnings"] = LogBuffer.WarningCount,
                ["sessionId"] = SessionId,
            };
        }
    }
}
