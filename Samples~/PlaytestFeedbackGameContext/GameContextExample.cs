using System.Collections.Generic;
using UnityTools.PlaytestFeedback;
using UnityEngine;

/// <summary>
/// Example of attaching game state to every feedback report.
/// Drop this on any GameObject and assign the player.
///
/// Values appear as fields in the Discord post (if "Show Game Context In Embed" is on)
/// and always in the attached context.json.
/// </summary>
public class GameContextExample : MonoBehaviour, IFeedbackContextProvider
{
    [SerializeField] Transform player;
    [SerializeField] string currentCheckpoint = "Start";
    [SerializeField] int gold = 120;

    void OnEnable()
    {
        // Option 1: implement IFeedbackContextProvider and register the component.
        FeedbackManager.RegisterProvider(this);

        // Option 2: add single values with a lambda. They are read when the panel opens.
        FeedbackManager.AddContext("Gold", () => gold);

        // Pause or mute your own systems that ignore Time.timeScale.
        FeedbackManager.Opened += OnFeedbackOpened;
        FeedbackManager.Closed += OnFeedbackClosed;
    }

    void OnDisable()
    {
        FeedbackManager.UnregisterProvider(this);
        FeedbackManager.RemoveContext("Gold");
        FeedbackManager.Opened -= OnFeedbackOpened;
        FeedbackManager.Closed -= OnFeedbackClosed;
    }

    public void CollectFeedbackContext(IDictionary<string, object> context)
    {
        if (player != null)
        {
            context["Player Position"] = player.position;
            context["Player Rotation"] = player.eulerAngles.y;
        }
        context["Checkpoint"] = currentCheckpoint;
    }

    void OnFeedbackOpened() => Debug.Log("Feedback panel opened: the game is paused.");

    void OnFeedbackClosed() => Debug.Log("Feedback panel closed: the game resumed.");

    // Hook this up to a "Send Feedback" button in your pause menu.
    public void OpenFeedback() => FeedbackManager.Open();
}
