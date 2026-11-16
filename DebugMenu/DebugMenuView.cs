using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace UnityTools.DebugTools
{
    /// <summary>
    /// Shows the debug menu: one foldout per section registered with <see cref="DebugMenu"/>.
    /// One is created automatically when Play starts, so most projects never add this component themselves.
    /// To restyle the menu, place the Debug Menu sample prefab (or your own UIDocument with this component) in a
    /// scene; it replaces the automatic one. Behaviour comes from Project Settings > Unity Tools > Debug Menu.
    /// Removes itself in builds without the debug menu.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public class DebugMenuView : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEBUG_MENU
        internal const string ResourcesFolder = "UnityToolsDebugMenu/";

        /// <summary>The menu currently in use, or null.</summary>
        public static DebugMenuView Active { get; private set; }

        /// <summary>True while the menu is shown.</summary>
        public bool IsOpen { get; private set; }
        internal bool IsAutoSpawned { get; private set; }

        private UIDocument _document;
        private PanelSettings _ownedPanelSettings;
        private VisualElement _panel;
        private ScrollView _sectionList;
        private InputAction _toggleAction;
        private readonly List<Action> _refreshers = new List<Action>();
        private readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>();
        private float _nextRefresh;
        private bool _rebuildPending = true;
        private bool _rejected;
        private float _smoothedFrameTime;

        // State changed while open, restored on close.
        private CursorLockMode _previousLockState;
        private bool _previousCursorVisible;
        private bool _paused;
        private float _previousTimeScale;
        private readonly List<PlayerInput> _blockedInputs = new List<PlayerInput>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SpawnIfMissing();
        }

        // A menu placed in a scene goes away with that scene; bring the automatic one back.
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => SpawnIfMissing();

        private static void SpawnIfMissing()
        {
            if (Active != null) return;
            if (FindAnyObjectByType<DebugMenuView>() != null) return;
            CreateAutoSpawned();
        }

        internal static DebugMenuView CreateAutoSpawned()
        {
            var go = new GameObject("Debug Menu");
            go.SetActive(false);
            DontDestroyOnLoad(go);

            PanelSettings panelSettings = CreatePanelSettings();
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = Resources.Load<VisualTreeAsset>(ResourcesFolder + "DebugMenu");

            var view = go.AddComponent<DebugMenuView>();
            view.IsAutoSpawned = true;
            view._ownedPanelSettings = panelSettings;
            go.SetActive(true);
            return view;
        }

        private static PanelSettings CreatePanelSettings()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "Debug Menu Panel Settings";
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>(ResourcesFolder + "DebugTheme");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = 1000;
            return settings;
        }

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                if (Active.IsAutoSpawned && !IsAutoSpawned)
                {
                    // A menu placed in a scene replaces the automatic one.
                    Destroy(Active.gameObject);
                }
                else
                {
                    Debug.LogWarning($"Debug Menu: {Active.name} is already the debug menu, so {name} was removed.", this);
                    _rejected = true;
                    Destroy(gameObject);
                    return;
                }
            }

            Active = this;
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            if (_rejected) return;

            VisualElement root = _document.rootVisualElement;
            _panel = root?.Q<VisualElement>("debug-panel");
            _sectionList = root?.Q<ScrollView>("debug-sections");
            if (_panel == null || _sectionList == null)
            {
                Debug.LogWarning("Debug Menu: the UIDocument needs a 'debug-panel' element and a 'debug-sections' ScrollView (see the package's DebugMenu.uxml).", this);
            }

            DebugMenu.SectionsChanged += OnSectionsChanged;

            DebugMenuSettings settings = DebugMenuSettings.Current;
            _toggleAction = settings.CreateToggleAction();
            _toggleAction.performed += OnToggle;
            _toggleAction.Enable();

            Label hint = root?.Q<Label>("debug-hint");
            if (hint != null) hint.text = $"{_toggleAction.GetBindingDisplayString()} to close";

            if (settings.ShowGameSection) RegisterGameSection();

            _rebuildPending = true;
            SetOpen(settings.StartOpen);
        }

        private void OnDisable()
        {
            if (_rejected) return;

            if (IsOpen) SetOpen(false);
            DebugMenu.SectionsChanged -= OnSectionsChanged;
            DebugMenu.Unregister(this);

            if (_toggleAction != null)
            {
                _toggleAction.performed -= OnToggle;
                _toggleAction.Disable();
                _toggleAction.Dispose();
                _toggleAction = null;
            }
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            if (_ownedPanelSettings != null) Destroy(_ownedPanelSettings);
        }

        private void Update()
        {
            float frameTime = Time.unscaledDeltaTime;
            _smoothedFrameTime = _smoothedFrameTime <= 0f ? frameTime : Mathf.Lerp(_smoothedFrameTime, frameTime, 0.1f);

            if (!IsOpen) return;
            if (_rebuildPending) Rebuild();

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + Mathf.Max(0.05f, DebugMenuSettings.Current.RefreshInterval);
                RefreshValues();
            }
        }

        /// <summary>Opens or closes the menu.</summary>
        public void SetOpen(bool open)
        {
            if (open != IsOpen)
            {
                IsOpen = open;
                if (open) SaveStateAndOpen();
                else RestoreState();
            }

            if (_panel != null) _panel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open)
            {
                Rebuild();
                // Focus the first section so a gamepad can navigate straight away.
                _sectionList?.Q<Foldout>()?.Focus();
            }
        }

        /// <summary>Updates every readout and bound value now, instead of waiting for the next refresh.</summary>
        public void RefreshValues()
        {
            foreach (Action refresh in _refreshers)
            {
                try
                {
                    refresh();
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
            }
        }

        private void SaveStateAndOpen()
        {
            DebugMenuSettings settings = DebugMenuSettings.Current;

            _previousLockState = UnityEngine.Cursor.lockState;
            _previousCursorVisible = UnityEngine.Cursor.visible;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;

            if (settings.PauseWhileOpen)
            {
                _previousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                _paused = true;
            }

            if (settings.BlockGameplayInput)
            {
#if UNITY_6000_5_OR_NEWER
                PlayerInput[] playerInputs = FindObjectsByType<PlayerInput>();
#else
                PlayerInput[] playerInputs = FindObjectsByType<PlayerInput>(FindObjectsSortMode.None);
#endif
                foreach (PlayerInput playerInput in playerInputs)
                {
                    if (!playerInput.inputIsActive) continue;
                    playerInput.DeactivateInput();
                    _blockedInputs.Add(playerInput);
                }
            }
        }

        private void RestoreState()
        {
            UnityEngine.Cursor.lockState = _previousLockState;
            UnityEngine.Cursor.visible = _previousCursorVisible;

            if (_paused)
            {
                Time.timeScale = _previousTimeScale;
                _paused = false;
            }

            foreach (PlayerInput playerInput in _blockedInputs)
            {
                if (playerInput != null) playerInput.ActivateInput();
            }
            _blockedInputs.Clear();
        }

        private void OnToggle(InputAction.CallbackContext context) => SetOpen(!IsOpen);

        private void OnSectionsChanged() => _rebuildPending = true;

        private void RegisterGameSection()
        {
            DebugMenu.Register(this, "Game", section => section
                .Readout("FPS", () => _smoothedFrameTime > 0f ? $"{1f / _smoothedFrameTime:0} ({_smoothedFrameTime * 1000f:0.0} ms)" : "-")
                .Slider("Time scale", 0f, 3f, () => Time.timeScale, value => Time.timeScale = value)
                .Button("Reset time scale", () => Time.timeScale = 1f)
                .Button("Reload scene", ReloadScene));
        }

        private static void ReloadScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.buildIndex < 0)
            {
                Debug.LogWarning($"Debug Menu: {scene.name} isn't in the Build Profiles scene list, so it can't be reloaded.");
                return;
            }
            SceneManager.LoadScene(scene.buildIndex);
        }

        private void Rebuild()
        {
            DebugMenu.RemoveDestroyedOwners();
            _rebuildPending = false;
            if (_sectionList == null) return;

            _sectionList.Clear();
            _refreshers.Clear();

            // Sections that share a title go in one foldout, which stays open or closed across rebuilds.
            var foldouts = new Dictionary<string, Foldout>();
            foreach (DebugSection section in DebugMenu.Sections)
            {
                if (!foldouts.TryGetValue(section.Title, out Foldout foldout))
                {
                    string title = section.Title;
                    foldout = new Foldout { text = title, value = _expanded.TryGetValue(title, out bool expanded) && expanded };
                    Foldout thisFoldout = foldout;
                    foldout.RegisterValueChangedCallback(e =>
                    {
                        if (e.target == thisFoldout) _expanded[title] = e.newValue;
                    });
                    foldout.AddToClassList("debug-section");
                    foldouts.Add(title, foldout);
                    _sectionList.Add(foldout);
                }

                foreach (DebugControl control in section.Controls) foldout.Add(CreateControl(control));
            }

            if (DebugMenu.Sections.Count == 0)
            {
                var empty = new Label("No debug controls are registered.");
                empty.AddToClassList("debug-readout");
                _sectionList.Add(empty);
            }
        }

        private VisualElement CreateControl(DebugControl control)
        {
            switch (control.Kind)
            {
                case DebugControlKind.Button:
                {
                    var button = new Button(() => control.OnClick?.Invoke()) { text = control.Label };
                    button.AddToClassList("debug-button");
                    return button;
                }
                case DebugControlKind.Toggle:
                {
                    var toggle = new Toggle(control.Label);
                    toggle.SetValueWithoutNotify(control.GetBool?.Invoke() ?? false);
                    toggle.RegisterValueChangedCallback(e => control.SetBool?.Invoke(e.newValue));
                    _refreshers.Add(() => toggle.SetValueWithoutNotify(control.GetBool?.Invoke() ?? false));
                    return toggle;
                }
                case DebugControlKind.Slider:
                {
                    var slider = new Slider(control.Label, control.Min, control.Max) { showInputField = true };
                    slider.SetValueWithoutNotify(control.GetFloat?.Invoke() ?? control.Min);
                    slider.RegisterValueChangedCallback(e => control.SetFloat?.Invoke(e.newValue));
                    _refreshers.Add(() =>
                    {
                        if (IsBeingEdited(slider)) return;
                        slider.SetValueWithoutNotify(control.GetFloat?.Invoke() ?? control.Min);
                    });
                    return slider;
                }
                case DebugControlKind.IntSlider:
                {
                    var slider = new SliderInt(control.Label, (int)control.Min, (int)control.Max) { showInputField = true };
                    slider.SetValueWithoutNotify(control.GetInt?.Invoke() ?? (int)control.Min);
                    slider.RegisterValueChangedCallback(e => control.SetInt?.Invoke(e.newValue));
                    _refreshers.Add(() =>
                    {
                        if (IsBeingEdited(slider)) return;
                        slider.SetValueWithoutNotify(control.GetInt?.Invoke() ?? (int)control.Min);
                    });
                    return slider;
                }
                case DebugControlKind.Dropdown:
                {
                    var choices = new List<string>(control.Options);
                    var dropdown = new DropdownField(control.Label, choices, Mathf.Clamp(control.GetInt?.Invoke() ?? 0, 0, Mathf.Max(0, choices.Count - 1)));
                    dropdown.RegisterValueChangedCallback(e => control.SetInt?.Invoke(choices.IndexOf(e.newValue)));
                    _refreshers.Add(() =>
                    {
                        int index = control.GetInt?.Invoke() ?? 0;
                        if (index >= 0 && index < choices.Count) dropdown.SetValueWithoutNotify(choices[index]);
                    });
                    return dropdown;
                }
                default:
                {
                    var row = new Label();
                    row.AddToClassList("debug-readout");
                    void Refresh() => row.text = $"{control.Label}: {control.GetText?.Invoke()}";
                    Refresh();
                    _refreshers.Add(Refresh);
                    return row;
                }
            }
        }

        // Don't overwrite a slider the user is dragging or typing in.
        private static bool IsBeingEdited(VisualElement field) =>
            field.focusController?.focusedElement is VisualElement focused && field.Contains(focused);
#else
        private void Awake()
        {
            // The debug menu doesn't exist in this build.
            Destroy(gameObject);
        }
#endif
    }
}
