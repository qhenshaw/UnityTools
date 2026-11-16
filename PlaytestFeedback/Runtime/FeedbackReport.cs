using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.PlaytestFeedback
{
    /// <summary>
    /// One feedback submission, passed to <see cref="FeedbackManager.Submitted"/>.
    /// </summary>
    public sealed class FeedbackReport
    {
        public string Id { get; internal set; }
        public DateTime TimestampUtc { get; internal set; }

        /// <summary>The category the player picked, or null if none.</summary>
        public FeedbackCategory Category { get; internal set; }

        public string Text { get; internal set; }
        public string PlayerName { get; internal set; }

        /// <summary>The annotated screenshot, or null. Owned by the feedback tool; copy it if you need to keep it.</summary>
        public Texture2D Screenshot { get; internal set; }

        /// <summary>Game version, platform, hardware, FPS, scene, log counts...</summary>
        public IReadOnlyDictionary<string, object> SystemInfo { get; internal set; }

        /// <summary>Values from context providers and <see cref="FeedbackManager.AddContext"/>.</summary>
        public IReadOnlyDictionary<string, object> GameContext { get; internal set; }

        /// <summary>Recent log lines, or null if logs are turned off.</summary>
        public string Log { get; internal set; }
    }
}
