using System.Collections.Generic;

namespace UnityTools.PlaytestFeedback
{
    /// <summary>
    /// Implement this to attach game-specific state to every feedback report
    /// (current level, player position, quest step, seed...).
    /// Register with <see cref="FeedbackManager.RegisterProvider"/>, usually in OnEnable,
    /// and unregister in OnDisable.
    /// </summary>
    /// <remarks>
    /// Called once when the player opens the feedback panel, before the game is paused.
    /// Values can be strings, numbers, bools, vectors, lists or dictionaries.
    /// </remarks>
    public interface IFeedbackContextProvider
    {
        /// <summary>Adds this provider's values to <paramref name="context"/>, keyed by the name shown in the report.</summary>
        void CollectFeedbackContext(IDictionary<string, object> context);
    }
}
