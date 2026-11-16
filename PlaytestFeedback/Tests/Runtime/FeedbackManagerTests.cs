using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace UnityTools.PlaytestFeedback.PlayModeTests
{
    public class FeedbackManagerTests
    {
        private readonly List<Object> _created = new List<Object>();
        private readonly List<InputAction> _actions = new List<InputAction>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            FeedbackManager.Close();
            yield return null;
            if (FeedbackManager.Instance != null) Object.Destroy(FeedbackManager.Instance.gameObject);
            foreach (Object obj in _created)
            {
                if (obj != null) Object.Destroy(obj);
            }
            _created.Clear();
            foreach (InputAction action in _actions) action.Dispose();
            _actions.Clear();
            FeedbackSettings.OverrideForTests(null);
            Time.timeScale = 1f;
            AudioListener.pause = false;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            yield return null;

            // Leave a manager with the project's settings behind, like at the start of Play.
            if (FeedbackManager.Instance == null) new GameObject("[Playtest Feedback]").AddComponent<FeedbackManager>();
            yield return null;
        }

        // Replaces any running manager with one using these settings.
        private IEnumerator CreateManager(FeedbackSettings settings = null)
        {
            FeedbackSettings.OverrideForTests(settings ?? new FeedbackSettings());
            if (FeedbackManager.Instance != null) Object.Destroy(FeedbackManager.Instance.gameObject);
            yield return null;
            var go = new GameObject("Test Feedback Manager");
            go.AddComponent<FeedbackManager>();
            yield return null;
        }

        private static IEnumerator Open()
        {
            FeedbackManager.Open();
            // Opening waits for the end of the frame to capture the screen.
            for (int i = 0; i < 5 && !FeedbackManager.IsOpen; i++) yield return null;
            Assert.IsTrue(FeedbackManager.IsOpen, "the panel didn't open");
        }

        private InputAction EnabledAction(string map, string name)
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _created.Add(asset);
            InputAction action = asset.AddActionMap(map).AddAction(name, InputActionType.Button, "<Keyboard>/space");
            action.Enable();
            return action;
        }

        [Test]
        public void Manager_IsCreatedAutomatically_WithDefaultSettings()
        {
            Assert.IsNotNull(FeedbackManager.Instance, "no FeedbackManager was created when Play started");
            Assert.AreEqual("DontDestroyOnLoad", FeedbackManager.Instance.gameObject.scene.name);
        }

        [UnityTest]
        public IEnumerator Open_PausesTimeAudioAndFreesCursor_CloseRestoresThem()
        {
            yield return CreateManager();
            Time.timeScale = 0.75f;
            AudioListener.pause = false;
            UnityEngine.Cursor.lockState = CursorLockMode.Confined;
            UnityEngine.Cursor.visible = false;

            yield return Open();
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsTrue(AudioListener.pause);
            Assert.AreEqual(CursorLockMode.None, UnityEngine.Cursor.lockState);
            Assert.IsTrue(UnityEngine.Cursor.visible);

            FeedbackManager.Close();
            Assert.IsFalse(FeedbackManager.IsOpen);
            Assert.AreEqual(0.75f, Time.timeScale);
            Assert.IsFalse(AudioListener.pause);
            Assert.AreEqual(CursorLockMode.Confined, UnityEngine.Cursor.lockState);
            Assert.IsFalse(UnityEngine.Cursor.visible);
        }

        [UnityTest]
        public IEnumerator PauseOptionsOff_LeaveTimeAndAudioAlone()
        {
            yield return CreateManager(new FeedbackSettings { pauseTime = false, pauseAudio = false });
            Time.timeScale = 0.5f;

            yield return Open();

            Assert.AreEqual(0.5f, Time.timeScale);
            Assert.IsFalse(AudioListener.pause);
        }

        [UnityTest]
        public IEnumerator GameInput_IsDisabledWhileOpen_AndReenabledTheFrameAfterClosing()
        {
            yield return CreateManager();
            InputAction fire = EnabledAction("Player", "Fire");

            yield return Open();
            Assert.IsFalse(fire.enabled);

            FeedbackManager.Close();
            Assert.IsFalse(fire.enabled, "input came back on the same frame, so the closing key could reach the game");
            yield return null;
            yield return null;
            Assert.IsTrue(fire.enabled);
        }

        [UnityTest]
        public IEnumerator ActionMapsToKeepEnabled_StayEnabled()
        {
            yield return CreateManager(new FeedbackSettings { actionMapsToKeepEnabled = new[] { "Voice" } });
            InputAction talk = EnabledAction("Voice", "Talk");
            InputAction fire = EnabledAction("Player", "Fire");

            yield return Open();

            Assert.IsTrue(talk.enabled);
            Assert.IsFalse(fire.enabled);
        }

        [UnityTest]
        public IEnumerator DisableGameInputOff_LeavesActionsEnabled()
        {
            yield return CreateManager(new FeedbackSettings { disableGameInput = false });
            InputAction fire = EnabledAction("Player", "Fire");

            yield return Open();

            Assert.IsTrue(fire.enabled);
        }

        [UnityTest]
        public IEnumerator GameEventSystem_IsDisabledWhileOpen_AndRestoredAfterClosing()
        {
            var go = new GameObject("Game EventSystem");
            _created.Add(go);
            var eventSystem = go.AddComponent<EventSystem>();
            yield return CreateManager();

            yield return Open();
            Assert.IsFalse(eventSystem.enabled);

            FeedbackManager.Close();
            yield return null;
            yield return null;
            Assert.IsTrue(eventSystem.enabled);
        }

        [UnityTest]
        public IEnumerator IsolateUiInputOff_LeavesGameEventSystemOn()
        {
            var go = new GameObject("Game EventSystem");
            _created.Add(go);
            var eventSystem = go.AddComponent<EventSystem>();
            yield return CreateManager(new FeedbackSettings { isolateUiInput = false });

            yield return Open();

            Assert.IsTrue(eventSystem.enabled);
        }

        [UnityTest]
        public IEnumerator OpenedAndClosed_EventsFire()
        {
            yield return CreateManager();
            int opened = 0, closed = 0;
            FeedbackManager.Opened += () => opened++;
            FeedbackManager.Closed += () => closed++;

            yield return Open();
            FeedbackManager.Close();

            Assert.AreEqual(1, opened);
            Assert.AreEqual(1, closed);
        }

        [UnityTest]
        public IEnumerator DestroyingManagerWhileOpen_RestoresGameState()
        {
            yield return CreateManager();
            Time.timeScale = 1f;
            InputAction fire = EnabledAction("Player", "Fire");
            yield return Open();

            Object.Destroy(FeedbackManager.Instance.gameObject);
            yield return null;

            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsTrue(fire.enabled);
            Assert.IsFalse(FeedbackManager.IsOpen);
        }

        [UnityTest]
        public IEnumerator SecondManager_IsRemoved()
        {
            yield return CreateManager();
            FeedbackManager first = FeedbackManager.Instance;

            var go = new GameObject("Second Feedback Manager");
            go.AddComponent<FeedbackManager>();
            yield return null;

            Assert.IsTrue(go == null);
            Assert.AreSame(first, FeedbackManager.Instance);
        }
    }
}
