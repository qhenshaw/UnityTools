using System;
using System.Collections;
using System.Collections.Generic;
using UnityTools.PlaytestFeedback.Internal;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;
using Object = UnityEngine.Object;

namespace UnityTools.PlaytestFeedback
{
    /// <summary>
    /// Entry point of the playtest feedback tool. Created automatically at startup (see
    /// Project Settings > Unity Tools > Playtest Feedback), or add one to a scene with Auto Create turned off.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-500)]
    [AddComponentMenu("UnityTools/Playtest Feedback Manager")]
    public sealed class FeedbackManager : MonoBehaviour
    {
        const string UxmlPath = "PlaytestFeedback/FeedbackPanel";
        const string ThemePath = "PlaytestFeedback/PlaytestFeedbackTheme";
        const int FrameSamples = 300;

        static readonly List<IFeedbackContextProvider> s_Providers = new List<IFeedbackContextProvider>();
        static readonly Dictionary<string, Func<object>> s_ContextValues = new Dictionary<string, Func<object>>();
        static readonly WaitForEndOfFrame s_EndOfFrame = new WaitForEndOfFrame();

        // Read from FeedbackSettings.Current when the manager wakes up.
        FeedbackSettings settings;

        FeedbackView _view;
        UIDocument _document;
        PanelSettings _runtimePanelSettings;
        InputAction _toggleAction;

        bool _isOpen;
        bool _opening;
        bool _sending;
        bool _flushing;

        // Game state saved while the panel is open.
        float _previousTimeScale = 1f;
        bool _previousAudioPaused;
        CursorLockMode _previousLockState;
        bool _previousCursorVisible;
        readonly List<InputAction> _disabledActions = new List<InputAction>();
        readonly List<(EventSystem eventSystem, GameObject selected)> _disabledEventSystems = new List<(EventSystem, GameObject)>();
        Coroutine _reenableInputRoutine;

        // Private UI input used while the panel is open, so the game's UI actions can all be disabled.
        GameObject _privateInputObject;
        EventSystem _privateEventSystem;
        DefaultInputActions _privateActions;

        // Snapshot taken when the panel opens.
        Dictionary<string, object> _systemSnapshot;
        Dictionary<string, object> _contextSnapshot;
        string _logSnapshot;

        // Frame time history for the FPS stats.
        readonly float[] _frameTimes = new float[FrameSamples];
        int _frameIndex;
        int _frameCount;

        // ================================================================== Public API

        public static FeedbackManager Instance { get; private set; }

        /// <summary>True while the feedback panel is open (and the game is paused by it).</summary>
        public static bool IsOpen => Instance != null && Instance._isOpen;

        /// <summary>Raised after the panel opens and the game is paused. Use it to pause your own systems.</summary>
        public static event Action Opened;

        /// <summary>Raised after the panel closes and the game is resumed.</summary>
        public static event Action Closed;

        /// <summary>Raised when a report was sent, or saved to be sent later.</summary>
        public static event Action<FeedbackReport> Submitted;

        public FeedbackSettings Settings => settings;

        /// <summary>Opens the feedback panel, same as pressing the hotkey. Useful for a "Send Feedback" menu button.</summary>
        public static void Open()
        {
            if (Instance != null) Instance.OpenInternal();
        }

        /// <summary>Closes the feedback panel, keeping the player's draft.</summary>
        public static void Close()
        {
            if (Instance != null) Instance.CloseInternal();
        }

        public static void RegisterProvider(IFeedbackContextProvider provider)
        {
            if (provider != null && !s_Providers.Contains(provider)) s_Providers.Add(provider);
        }

        public static void UnregisterProvider(IFeedbackContextProvider provider) => s_Providers.Remove(provider);

        /// <summary>
        /// Adds a value to every report, read when the panel opens.
        /// Example: <c>FeedbackManager.AddContext("Gold", () => inventory.Gold);</c>
        /// </summary>
        public static void AddContext(string key, Func<object> getValue)
        {
            if (string.IsNullOrEmpty(key) || getValue == null) return;
            s_ContextValues[key] = getValue;
        }

        public static void RemoveContext(string key)
        {
            if (key != null) s_ContextValues.Remove(key);
        }

        // ================================================================== Bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // Needed when "Enter Play Mode Options" skips the domain reload.
            Instance = null;
            Opened = null;
            Closed = null;
            Submitted = null;
            s_Providers.Clear();
            s_ContextValues.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
#if PLAYTEST_FEEDBACK_DISABLED
            return;
#else
            if (Instance != null) return;

            FeedbackSettings loaded = FeedbackSettings.Current;
            if (!loaded.autoCreate || !loaded.IsEnabledForThisBuild) return;

            var go = new GameObject("[Playtest Feedback]");
            go.SetActive(false);
            go.AddComponent<FeedbackManager>();
            go.SetActive(true);
#endif
        }

        // ================================================================== Unity messages

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            settings = FeedbackSettings.Current;

            Instance = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);

            CreateInput();
            CreatePrivateUiInput();
            CreateUi();
        }

        void OnEnable() => _toggleAction?.Enable();

        void OnDisable() => _toggleAction?.Disable();

        void Start()
        {
            if (Instance == this) StartCoroutine(FlushOutboxAfterDelay(3f));
        }

        void Update()
        {
            if (_isOpen)
            {
                _view?.Tick();
            }
            else
            {
                _frameTimes[_frameIndex] = Time.unscaledDeltaTime;
                _frameIndex = (_frameIndex + 1) % FrameSamples;
                if (_frameCount < FrameSamples) _frameCount++;
            }
        }

        void OnDestroy()
        {
            if (Instance != this) return;

            if (_isOpen)
            {
                RestoreGameState(reenableInputImmediately: true);
                _isOpen = false;
            }

            if (_toggleAction != null)
            {
                _toggleAction.performed -= OnTogglePerformed;
                _toggleAction.Dispose();
                _toggleAction = null;
            }

            _view?.Dispose();
            _privateActions?.Dispose();
            _privateActions = null;
            if (_runtimePanelSettings != null) Destroy(_runtimePanelSettings);
            Instance = null;
        }

        // ================================================================== Setup

        void CreateInput()
        {
            _toggleAction = settings.CreateToggleActionInstance();
            _toggleAction.performed += OnTogglePerformed;
        }

        void CreatePrivateUiInput()
        {
            if (!settings.isolateUiInput) return;

            // A separate DefaultInputActions instance: the module's own AssignDefaultActions() uses a
            // static instance that a game module on default actions would share with us.
            _privateActions = new DefaultInputActions();

            _privateInputObject = new GameObject("Feedback UI Input");
            _privateInputObject.SetActive(false);
            _privateInputObject.transform.SetParent(transform, false);

            _privateEventSystem = _privateInputObject.AddComponent<EventSystem>();
            var module = _privateInputObject.AddComponent<InputSystemUIInputModule>();
            module.actionsAsset = _privateActions.asset;
            module.point = InputActionReference.Create(_privateActions.UI.Point);
            module.leftClick = InputActionReference.Create(_privateActions.UI.Click);
            module.rightClick = InputActionReference.Create(_privateActions.UI.RightClick);
            module.middleClick = InputActionReference.Create(_privateActions.UI.MiddleClick);
            module.scrollWheel = InputActionReference.Create(_privateActions.UI.ScrollWheel);
            module.move = InputActionReference.Create(_privateActions.UI.Navigate);
            module.submit = InputActionReference.Create(_privateActions.UI.Submit);
            module.cancel = InputActionReference.Create(_privateActions.UI.Cancel);
            module.trackedDevicePosition = InputActionReference.Create(_privateActions.UI.TrackedDevicePosition);
            module.trackedDeviceOrientation = InputActionReference.Create(_privateActions.UI.TrackedDeviceOrientation);
        }

        void CreateUi()
        {
            var tree = Resources.Load<VisualTreeAsset>(UxmlPath);
            if (tree == null)
            {
                Debug.LogError($"[PlaytestFeedback] Couldn't load Resources/{UxmlPath}.uxml.", this);
                enabled = false;
                return;
            }

            _runtimePanelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _runtimePanelSettings.name = "Playtest Feedback Panel Settings";
            _runtimePanelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>(ThemePath);
            _runtimePanelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _runtimePanelSettings.referenceResolution = new Vector2Int(1920, 1080);
            _runtimePanelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            _runtimePanelSettings.match = 0.5f;
            _runtimePanelSettings.sortingOrder = 30000; // Above the game's own UI.

            // Configure while inactive so UIDocument builds the tree once, with everything set.
            var uiObject = new GameObject("Feedback UI");
            uiObject.SetActive(false);
            uiObject.transform.SetParent(transform, false);
            _document = uiObject.AddComponent<UIDocument>();
            _document.panelSettings = _runtimePanelSettings;
            _document.visualTreeAsset = tree;
            uiObject.SetActive(true);

            var root = _document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore; // Never block the game's UI while closed.
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.top = 0;
            root.style.right = 0;
            root.style.bottom = 0;

            _view = new FeedbackView(root, settings);
            _view.CloseRequested += CloseInternal;
            _view.SubmitRequested += Submit;
        }

        // ================================================================== Open / close

        void OnTogglePerformed(InputAction.CallbackContext context)
        {
            if (_isOpen) CloseInternal();
            else OpenInternal();
        }

        void OpenInternal()
        {
            if (_isOpen || _opening || _view == null || !isActiveAndEnabled) return;
            StartCoroutine(OpenRoutine());
        }

        IEnumerator OpenRoutine()
        {
            _opening = true;

            // Take the snapshot before pausing, so FPS and game state reflect actual play.
            TakeSnapshot();

            // Capture the frame before the panel is visible so it never appears in screenshots.
            // Batch mode never renders, so WaitForEndOfFrame would never resume: open without a frame.
            Texture2D frame = null;
            if (!Application.isBatchMode)
            {
                yield return s_EndOfFrame;
                frame = ScreenCapture.CaptureScreenshotAsTexture();
                if (frame != null)
                {
                    frame.name = "Feedback Frozen Frame";
                    frame.wrapMode = TextureWrapMode.Clamp;
                }
            }

            _opening = false;
            if (this == null) yield break;

            PauseGame();
            _isOpen = true;
            _view.Show(frame);
            InvokeSafely(Opened);
        }

        void CloseInternal()
        {
            if (!_isOpen || _sending) return;
            _view.Hide();
            _isOpen = false;
            RestoreGameState(reenableInputImmediately: false);
            InvokeSafely(Closed);
        }

        void PauseGame()
        {
            if (settings.pauseTime)
            {
                _previousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }

            if (settings.pauseAudio)
            {
                _previousAudioPaused = AudioListener.pause;
                AudioListener.pause = true;
            }

            _previousLockState = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (_reenableInputRoutine != null)
            {
                // Reopened before the previous close re-enabled input: those actions and
                // EventSystems are still off and still listed, so keep the lists.
                StopCoroutine(_reenableInputRoutine);
                _reenableInputRoutine = null;
            }
            else
            {
                _disabledActions.Clear();
                _disabledEventSystems.Clear();
            }

            if (settings.disableGameInput)
            {
                var keep = new HashSet<string>(settings.actionMapsToKeepEnabled ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                foreach (var action in InputSystem.ListEnabledActions())
                {
                    if (action == _toggleAction) continue;
                    if (_privateActions != null && action.actionMap?.asset == _privateActions.asset) continue;
                    var mapName = action.actionMap?.name;
                    if (mapName != null && keep.Contains(mapName)) continue;
                    action.Disable();
                    if (!_disabledActions.Contains(action)) _disabledActions.Add(action);
                }
            }

            if (_privateInputObject != null)
            {
                // The game's EventSystems go first, so ours becomes EventSystem.current.
                // Resources.FindObjectsOfTypeAll works on every Unity 6 version (FindObjectsSortMode is
                // obsolete in newer versions, and the overloads without it don't exist in 6000.0).
                foreach (var eventSystem in Resources.FindObjectsOfTypeAll<EventSystem>())
                {
                    if (eventSystem == _privateEventSystem || !eventSystem.isActiveAndEnabled) continue;
                    if (!eventSystem.gameObject.scene.IsValid()) continue; // Prefab assets.
                    _disabledEventSystems.Add((eventSystem, eventSystem.currentSelectedGameObject));
                    eventSystem.enabled = false;
                }
                _privateInputObject.SetActive(true);
            }
        }

        void RestoreGameState(bool reenableInputImmediately)
        {
            if (settings.pauseTime) Time.timeScale = _previousTimeScale;
            if (settings.pauseAudio) AudioListener.pause = _previousAudioPaused;
            Cursor.lockState = _previousLockState;
            Cursor.visible = _previousCursorVisible;

            // Stop the panel's private input right away; the game's input comes back below.
            if (_privateInputObject != null) _privateInputObject.SetActive(false);

            if (reenableInputImmediately || !isActiveAndEnabled)
            {
                ReenableInput();
            }
            else
            {
                // Wait a frame so the key that closed the panel (Esc) doesn't also reach the game.
                _reenableInputRoutine = StartCoroutine(ReenableInputNextFrame());
            }
        }

        IEnumerator ReenableInputNextFrame()
        {
            yield return null;
            _reenableInputRoutine = null;
            ReenableInput();
        }

        void ReenableInput()
        {
            // Same order as they were disabled, so the game's original EventSystem is current again.
            foreach (var (eventSystem, selected) in _disabledEventSystems)
            {
                if (eventSystem == null) continue;
                eventSystem.enabled = true;
                if (selected != null && selected.activeInHierarchy && eventSystem.currentSelectedGameObject != selected)
                {
                    eventSystem.SetSelectedGameObject(selected);
                }
            }
            _disabledEventSystems.Clear();

            foreach (var action in _disabledActions)
            {
                try
                {
                    action?.Enable();
                }
                catch (Exception)
                {
                    // The action's asset may have been destroyed while the panel was open.
                }
            }
            _disabledActions.Clear();
        }

        void TakeSnapshot()
        {
            float total = 0f;
            float worst = 0f;
            for (int i = 0; i < _frameCount; i++)
            {
                total += _frameTimes[i];
                worst = Mathf.Max(worst, _frameTimes[i]);
            }
            float averageFps = total > 0f ? _frameCount / total : 0f;

            _systemSnapshot = SystemInfoCollector.Collect(averageFps, worst * 1000f);
            _contextSnapshot = CollectGameContext();
            _logSnapshot = settings.includeLogs ? LogBuffer.Dump(settings.logLines, settings.includeStackTraces) : null;
        }

        static Dictionary<string, object> CollectGameContext()
        {
            var context = new Dictionary<string, object>();

            foreach (var pair in s_ContextValues)
            {
                try
                {
                    context[pair.Key] = pair.Value();
                }
                catch (Exception e)
                {
                    context[pair.Key] = $"<error: {e.Message}>";
                }
            }

            for (int i = s_Providers.Count - 1; i >= 0; i--)
            {
                var provider = s_Providers[i];
                if (provider == null || (provider is Object unityObject && unityObject == null))
                {
                    s_Providers.RemoveAt(i);
                    continue;
                }

                try
                {
                    provider.CollectFeedbackContext(context);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return context;
        }

        // ================================================================== Submit

        void Submit()
        {
            if (_sending || !_isOpen || !_view.CanSubmit) return;

            var report = new FeedbackReport
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                TimestampUtc = DateTime.UtcNow,
                Category = _view.SelectedCategory,
                Text = _view.FeedbackText,
                PlayerName = _view.PlayerName,
                Screenshot = _view.Screenshot,
                SystemInfo = _systemSnapshot ?? new Dictionary<string, object>(),
                GameContext = _contextSnapshot ?? new Dictionary<string, object>(),
                Log = _logSnapshot,
            };

            StartCoroutine(SubmitRoutine(report));
        }

        IEnumerator SubmitRoutine(FeedbackReport report)
        {
            _sending = true;
            _view.SetBusy(true);
            _view.SetStatus("Sending…", StatusKind.Info);
            _view.SavePlayerName();

            DiscordWebhook.Request request;
            try
            {
                request = DiscordWebhook.Build(report, settings);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _view.SetStatus("Something went wrong while building the report. Please try again.", StatusKind.Error);
                _view.SetBusy(false);
                _sending = false;
                yield break;
            }

            string url = settings.EffectiveWebhookUrl;
            bool queued = false;
            string message;
            bool success;

            if (!FeedbackSettings.IsValidWebhookUrl(url))
            {
                Debug.LogWarning("[PlaytestFeedback] No valid Discord webhook URL is set in the Playtest Feedback settings.");
                success = false;
                if (settings.queueFailedSubmissions)
                {
                    FeedbackOutbox.Save(report.Id, request);
                    queued = true;
                }
                message = queued
                    ? "Feedback isn't fully set up yet, so your report was saved on this computer. Thanks!"
                    : "Feedback isn't set up yet (no Discord webhook).";
            }
            else
            {
                var result = default(DiscordWebhook.Result);
                yield return DiscordWebhook.Send(url, request, r => result = r);
                success = result.Success;

                if (success)
                {
                    message = "Thanks! Your feedback was sent.";
                }
                else if (result.Retryable && settings.queueFailedSubmissions)
                {
                    FeedbackOutbox.Save(report.Id, request);
                    queued = true;
                    message = "Couldn't reach the server. Your report was saved and will be sent automatically later.";
                    Debug.LogWarning($"[PlaytestFeedback] Send failed, queued for later: {result.Message}");
                }
                else
                {
                    message = $"Couldn't send your feedback (error {result.StatusCode}). Please try again.";
                    Debug.LogError($"[PlaytestFeedback] Discord rejected the report: {result.Message}");
                }
            }

            if (this == null) yield break;

            if (success || queued)
            {
                _view.SetStatus(message, StatusKind.Success);
                InvokeSafely(Submitted, report);
                yield return new WaitForSecondsRealtime(queued ? 2.5f : 1.2f);

                _view.ClearDraft();
                _view.SetBusy(false);
                _sending = false;
                CloseInternal();
                if (success) StartCoroutine(FlushOutbox());
            }
            else
            {
                // Keep the draft so the player can try again.
                _view.SetStatus(message, StatusKind.Error);
                _view.SetBusy(false);
                _sending = false;
            }
        }

        // ================================================================== Outbox

        IEnumerator FlushOutboxAfterDelay(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            yield return FlushOutbox();
        }

        IEnumerator FlushOutbox()
        {
            if (_flushing) yield break;

            string url = settings.EffectiveWebhookUrl;
            if (!FeedbackSettings.IsValidWebhookUrl(url)) yield break;

            _flushing = true;
            try
            {
                foreach (var id in FeedbackOutbox.GetPending())
                {
                    if (!FeedbackOutbox.TryLoad(id, out var request))
                    {
                        FeedbackOutbox.Delete(id);
                        continue;
                    }

                    var result = default(DiscordWebhook.Result);
                    yield return DiscordWebhook.Send(url, request, r => result = r);

                    if (result.Success)
                    {
                        FeedbackOutbox.Delete(id);
                        Debug.Log($"[PlaytestFeedback] Sent queued report {id}.");
                    }
                    else if (result.Retryable)
                    {
                        break; // Still offline; try again next launch.
                    }
                    else
                    {
                        FeedbackOutbox.MarkFailed(id);
                        Debug.LogWarning($"[PlaytestFeedback] Queued report {id} was rejected and won't be retried: {result.Message}");
                    }

                    yield return new WaitForSecondsRealtime(1f); // Be gentle with Discord's rate limits.
                }
            }
            finally
            {
                _flushing = false;
            }
        }

        // ================================================================== Helpers

        static void InvokeSafely(Action callback)
        {
            try
            {
                callback?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        static void InvokeSafely<T>(Action<T> callback, T arg)
        {
            try
            {
                callback?.Invoke(arg);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
