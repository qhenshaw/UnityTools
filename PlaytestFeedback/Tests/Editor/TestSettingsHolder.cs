using UnityEngine;

namespace UnityTools.PlaytestFeedback.Tests
{
    // Holds FeedbackSettings the way the ProjectSettings singleton does, to test Unity's own serializer.
    public class TestSettingsHolder : ScriptableObject
    {
        public FeedbackSettings settings = new FeedbackSettings();
    }
}
