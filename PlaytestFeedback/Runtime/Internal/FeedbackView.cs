using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UnityTools.PlaytestFeedback.Internal
{
    internal enum FeedbackViewMode
    {
        Hidden,
        Panel,
        Snip,
        Draw,
    }

    internal enum StatusKind
    {
        Info,
        Success,
        Error,
    }

    /// <summary>
    /// Wires up the UXML: the feedback form, the screenshot snip overlay and the draw overlay.
    /// Holds the player's draft (text, category, screenshot) between openings.
    /// </summary>
    internal sealed class FeedbackView : IDisposable
    {
        const string PlayerNamePrefsKey = "PlaytestFeedback.PlayerName";
        const float MinSnipSize = 8f;

        static readonly Color[] BrushColors =
        {
            new Color(1.00f, 0.23f, 0.19f), // red
            new Color(1.00f, 0.80f, 0.00f), // yellow
            new Color(0.20f, 0.78f, 0.35f), // green
            new Color(0.20f, 0.68f, 0.90f), // blue
            new Color(1.00f, 1.00f, 1.00f), // white
        };

        static readonly float[] BrushSizes = { 3f, 6f, 12f }; // Radius in panel units.

        readonly FeedbackSettings _settings;

        readonly VisualElement _root;
        readonly VisualElement _panelMode;
        readonly VisualElement _snipMode;
        readonly VisualElement _drawMode;

        // Form
        readonly VisualElement _categoryRow;
        readonly TextField _nameField;
        readonly TextField _feedbackText;
        readonly Label _charCount;
        readonly Button _addScreenshot;
        readonly VisualElement _screenshotPreview;
        readonly VisualElement _screenshotThumb;
        readonly Button _editScreenshot;
        readonly Button _retakeScreenshot;
        readonly Button _removeScreenshot;
        readonly Label _statusLabel;
        readonly Button _cancelButton;
        readonly Button _submitButton;
        readonly List<Button> _categoryButtons = new List<Button>();

        // Snip
        readonly VisualElement _snipToolbar;
        readonly VisualElement _snipSelection;
        readonly VisualElement _dimTop, _dimBottom, _dimLeft, _dimRight;
        bool _snipping;
        Vector2 _snipStart;
        Rect _snipRect;

        // Draw
        readonly VisualElement _drawArea;
        readonly VisualElement _drawImage;
        readonly Button _undoButton;
        readonly List<Button> _colorButtons = new List<Button>();
        readonly List<Button> _sizeButtons = new List<Button>();
        AnnotationTool _annotation;
        bool _drawing;
        int _colorIndex;
        int _sizeIndex = 1;

        // Draft
        int _categoryIndex = -1;
        Texture2D _screenshot;
        Texture2D _frozenFrame;

        public event Action CloseRequested;
        public event Action SubmitRequested;

        public FeedbackViewMode Mode { get; private set; } = FeedbackViewMode.Hidden;
        public bool IsBusy { get; private set; }

        public FeedbackCategory SelectedCategory =>
            _categoryIndex >= 0 && _categoryIndex < _settings.categories.Count ? _settings.categories[_categoryIndex] : null;

        public string FeedbackText => _feedbackText.value?.Trim() ?? "";
        public string PlayerName => _settings.askForPlayerName ? _nameField.value?.Trim() ?? "" : "";
        public Texture2D Screenshot => _screenshot;

        public bool CanSubmit
        {
            get
            {
                if (IsBusy) return false;
                bool needsCategory = _settings.requireCategory && _settings.categories.Count > 0;
                if (needsCategory && SelectedCategory == null) return false;
                return FeedbackText.Length > 0 || _screenshot != null;
            }
        }

        public FeedbackView(VisualElement documentRoot, FeedbackSettings settings)
        {
            _settings = settings;

            _root = Required(documentRoot, "feedback-root");
            _panelMode = Required(_root, "panel-mode");
            _snipMode = Required(_root, "snip-mode");
            _drawMode = Required(_root, "draw-mode");

            _categoryRow = Required(_root, "category-row");
            _nameField = Required<TextField>(_root, "name-field");
            _feedbackText = Required<TextField>(_root, "feedback-text");
            _charCount = Required<Label>(_root, "char-count");
            _addScreenshot = Required<Button>(_root, "add-screenshot");
            _screenshotPreview = Required(_root, "screenshot-preview");
            _screenshotThumb = Required(_root, "screenshot-thumb");
            _editScreenshot = Required<Button>(_root, "edit-screenshot");
            _retakeScreenshot = Required<Button>(_root, "retake-screenshot");
            _removeScreenshot = Required<Button>(_root, "remove-screenshot");
            _statusLabel = Required<Label>(_root, "status-label");
            _cancelButton = Required<Button>(_root, "cancel-button");
            _submitButton = Required<Button>(_root, "submit-button");

            _snipToolbar = Required(_root, "snip-toolbar");
            _snipSelection = Required(_root, "snip-selection");
            _dimTop = Required(_root, "snip-dim-top");
            _dimBottom = Required(_root, "snip-dim-bottom");
            _dimLeft = Required(_root, "snip-dim-left");
            _dimRight = Required(_root, "snip-dim-right");

            _drawArea = Required(_root, "draw-area");
            _drawImage = Required(_root, "draw-image");
            _undoButton = Required<Button>(_root, "draw-undo");

            SetupForm(documentRoot);
            SetupSnip();
            SetupDraw();

            SetMode(FeedbackViewMode.Hidden);
            RefreshState();
        }

        // ================================================================== Public API

        /// <summary>Shows the form. The view takes ownership of <paramref name="frozenFrame"/>.</summary>
        public void Show(Texture2D frozenFrame)
        {
            if (_frozenFrame != null && _frozenFrame != frozenFrame) Object.Destroy(_frozenFrame);
            _frozenFrame = frozenFrame;

            SetStatus(null, StatusKind.Info);
            SetMode(FeedbackViewMode.Panel);
            RefreshState();
            _feedbackText.schedule.Execute(() => _feedbackText.Focus()).StartingIn(50);
        }

        public void Hide()
        {
            CancelSnip();
            DiscardDrawing();
            SetMode(FeedbackViewMode.Hidden);
        }

        public void SetBusy(bool busy)
        {
            IsBusy = busy;
            foreach (var button in _categoryButtons) button.SetEnabled(!busy);
            _nameField.SetEnabled(!busy);
            _feedbackText.SetEnabled(!busy);
            _addScreenshot.SetEnabled(!busy);
            _editScreenshot.SetEnabled(!busy);
            _retakeScreenshot.SetEnabled(!busy);
            _removeScreenshot.SetEnabled(!busy);
            _cancelButton.SetEnabled(!busy);
            RefreshState();
        }

        public void SetStatus(string message, StatusKind kind)
        {
            _statusLabel.text = message ?? "";
            _statusLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
            _statusLabel.EnableInClassList("pf-status--error", kind == StatusKind.Error);
            _statusLabel.EnableInClassList("pf-status--success", kind == StatusKind.Success);
        }

        public void ClearDraft()
        {
            _feedbackText.SetValueWithoutNotify("");
            SelectCategory(-1);
            SetScreenshot(null);
            RefreshState();
        }

        public void SavePlayerName()
        {
            if (!_settings.askForPlayerName) return;
            PlayerPrefs.SetString(PlayerNamePrefsKey, PlayerName);
            PlayerPrefs.Save();
        }

        /// <summary>Called every frame while the panel is open.</summary>
        public void Tick()
        {
            if (_annotation != null && _annotation.Flush()) _drawImage.MarkDirtyRepaint();
            HandleShortcuts();
        }

        public void Dispose()
        {
            DiscardDrawing();
            SetScreenshot(null);
            if (_frozenFrame != null) Object.Destroy(_frozenFrame);
            _frozenFrame = null;
        }

        // ================================================================== Form

        void SetupForm(VisualElement documentRoot)
        {
            // Categories
            var categorySection = documentRoot.Q("category-section");
            if (_settings.categories.Count == 0 && categorySection != null) categorySection.style.display = DisplayStyle.None;

            for (int i = 0; i < _settings.categories.Count; i++)
            {
                int index = i;
                var category = _settings.categories[i];
                var button = new Button(() => SelectCategory(index)) { text = category.name };
                button.AddToClassList("pf-category");
                button.style.borderBottomColor = Opaque(category.color);
                _categoryRow.Add(button);
                _categoryButtons.Add(button);
            }

            // Name
            var nameSection = documentRoot.Q("name-section");
            if (nameSection != null) nameSection.style.display = _settings.askForPlayerName ? DisplayStyle.Flex : DisplayStyle.None;
            _nameField.SetValueWithoutNotify(PlayerPrefs.GetString(PlayerNamePrefsKey, ""));

            // Feedback text
            _feedbackText.maxLength = Mathf.Clamp(_settings.maxTextLength, 200, 4000);
            _feedbackText.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _feedbackText.textEdition.placeholder = "What happened? What did you expect? How did it feel?";
            _feedbackText.RegisterValueChangedCallback(_ => RefreshState());

            // Info line
            var info = documentRoot.Q<Label>("info-label");
            if (info != null)
            {
                info.text = _settings.includeLogs
                    ? "Your report also includes the game version, system specs and recent game logs."
                    : "Your report also includes the game version and system specs.";
            }

            // Buttons
            _addScreenshot.clicked += BeginSnip;
            _retakeScreenshot.clicked += BeginSnip;
            _editScreenshot.clicked += () =>
            {
                if (_screenshot != null) BeginDraw(ImageUtility.Clone(_screenshot));
            };
            _removeScreenshot.clicked += () =>
            {
                SetScreenshot(null);
                RefreshState();
            };
            _cancelButton.clicked += () => CloseRequested?.Invoke();
            _submitButton.clicked += () =>
            {
                if (CanSubmit) SubmitRequested?.Invoke();
            };
        }

        void SelectCategory(int index)
        {
            _categoryIndex = index;
            for (int i = 0; i < _categoryButtons.Count; i++)
            {
                var button = _categoryButtons[i];
                var color = Opaque(_settings.categories[i].color);
                if (i == index)
                {
                    button.style.backgroundColor = new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.45f, 1f);
                    button.style.borderTopColor = color;
                    button.style.borderLeftColor = color;
                    button.style.borderRightColor = color;
                }
                else
                {
                    button.style.backgroundColor = StyleKeyword.Null;
                    button.style.borderTopColor = StyleKeyword.Null;
                    button.style.borderLeftColor = StyleKeyword.Null;
                    button.style.borderRightColor = StyleKeyword.Null;
                }
            }
            RefreshState();
        }

        void RefreshState()
        {
            _charCount.text = $"{_feedbackText.value?.Length ?? 0} / {_feedbackText.maxLength}";
            _submitButton.SetEnabled(CanSubmit);

            bool hasScreenshot = _screenshot != null;
            bool canCapture = _frozenFrame != null;
            _addScreenshot.style.display = hasScreenshot || !canCapture ? DisplayStyle.None : DisplayStyle.Flex;
            _retakeScreenshot.style.display = canCapture ? DisplayStyle.Flex : DisplayStyle.None;
            _screenshotPreview.style.display = hasScreenshot ? DisplayStyle.Flex : DisplayStyle.None;
            _screenshotThumb.style.backgroundImage = hasScreenshot ? new StyleBackground(_screenshot) : new StyleBackground(StyleKeyword.None);
        }

        void SetScreenshot(Texture2D texture)
        {
            if (_screenshot != null && _screenshot != texture) Object.Destroy(_screenshot);
            _screenshot = texture;
        }

        void SetMode(FeedbackViewMode mode)
        {
            Mode = mode;
            _root.style.display = mode == FeedbackViewMode.Hidden ? DisplayStyle.None : DisplayStyle.Flex;
            _panelMode.style.display = mode == FeedbackViewMode.Panel ? DisplayStyle.Flex : DisplayStyle.None;
            _snipMode.style.display = mode == FeedbackViewMode.Snip ? DisplayStyle.Flex : DisplayStyle.None;
            _drawMode.style.display = mode == FeedbackViewMode.Draw ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void HandleShortcuts()
        {
            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;

            bool escape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            bool back = escape || (gamepad != null && gamepad.buttonEast.wasPressedThisFrame);
            bool enter = keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
            bool ctrl = keyboard != null && (keyboard.ctrlKey.isPressed || keyboard.leftMetaKey.isPressed || keyboard.rightMetaKey.isPressed);

            switch (Mode)
            {
                case FeedbackViewMode.Panel:
                    if (IsBusy) return;
                    if (escape) CloseRequested?.Invoke();
                    else if (ctrl && enter && CanSubmit) SubmitRequested?.Invoke();
                    break;

                case FeedbackViewMode.Snip:
                    if (back) CancelSnip();
                    else if (enter) FinishSnip(new Rect(0, 0, _snipMode.layout.width, _snipMode.layout.height));
                    break;

                case FeedbackViewMode.Draw:
                    if (back) CancelDraw();
                    else if (enter) FinishDraw();
                    else if (ctrl && keyboard.zKey.wasPressedThisFrame) Undo();
                    break;
            }
        }

        // ================================================================== Snip

        void SetupSnip()
        {
            Required<Button>(_root, "snip-full").clicked += () =>
                FinishSnip(new Rect(0, 0, _snipMode.layout.width, _snipMode.layout.height));
            Required<Button>(_root, "snip-cancel").clicked += CancelSnip;

            _snipMode.RegisterCallback<PointerDownEvent>(OnSnipPointerDown);
            _snipMode.RegisterCallback<PointerMoveEvent>(OnSnipPointerMove);
            _snipMode.RegisterCallback<PointerUpEvent>(OnSnipPointerUp);
            _snipMode.RegisterCallback<PointerCaptureOutEvent>(_ => _snipping = false);
            _snipMode.RegisterCallback<GeometryChangedEvent>(_ => UpdateSnipVisuals());
        }

        void BeginSnip()
        {
            if (_frozenFrame == null || IsBusy) return;
            _snipMode.style.backgroundImage = new StyleBackground(_frozenFrame);
            _snipping = false;
            _snipRect = Rect.zero;
            SetMode(FeedbackViewMode.Snip);
            UpdateSnipVisuals();
        }

        void CancelSnip()
        {
            if (Mode != FeedbackViewMode.Snip) return;
            _snipping = false;
            SetMode(FeedbackViewMode.Panel);
        }

        void OnSnipPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            if (evt.target is VisualElement target && (target == _snipToolbar || _snipToolbar.Contains(target))) return;

            _snipStart = ClampToSnipArea(_snipMode.WorldToLocal(evt.position));
            _snipRect = new Rect(_snipStart, Vector2.zero);
            _snipping = true;
            _snipMode.CapturePointer(evt.pointerId);
            UpdateSnipVisuals();
            evt.StopPropagation();
        }

        void OnSnipPointerMove(PointerMoveEvent evt)
        {
            if (!_snipping || !_snipMode.HasPointerCapture(evt.pointerId)) return;
            var point = ClampToSnipArea(_snipMode.WorldToLocal(evt.position));
            _snipRect = Rect.MinMaxRect(
                Mathf.Min(_snipStart.x, point.x), Mathf.Min(_snipStart.y, point.y),
                Mathf.Max(_snipStart.x, point.x), Mathf.Max(_snipStart.y, point.y));
            UpdateSnipVisuals();
        }

        void OnSnipPointerUp(PointerUpEvent evt)
        {
            if (!_snipping) return;
            _snipping = false;
            if (_snipMode.HasPointerCapture(evt.pointerId)) _snipMode.ReleasePointer(evt.pointerId);

            if (_snipRect.width >= MinSnipSize && _snipRect.height >= MinSnipSize)
            {
                FinishSnip(_snipRect);
            }
            else
            {
                _snipRect = Rect.zero;
                UpdateSnipVisuals();
            }
        }

        Vector2 ClampToSnipArea(Vector2 point)
        {
            var size = _snipMode.layout.size;
            return new Vector2(Mathf.Clamp(point.x, 0, size.x), Mathf.Clamp(point.y, 0, size.y));
        }

        void UpdateSnipVisuals()
        {
            float width = _snipMode.layout.width;
            float height = _snipMode.layout.height;
            if (float.IsNaN(width) || float.IsNaN(height)) return;

            var r = _snipRect;
            SetRect(_dimTop, 0, 0, width, r.yMin);
            SetRect(_dimBottom, 0, r.yMax, width, height - r.yMax);
            SetRect(_dimLeft, 0, r.yMin, r.xMin, r.height);
            SetRect(_dimRight, r.xMax, r.yMin, width - r.xMax, r.height);

            bool hasSelection = r.width > 0 && r.height > 0;
            _snipSelection.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasSelection) SetRect(_snipSelection, r.x, r.y, r.width, r.height);
        }

        void FinishSnip(Rect panelRect)
        {
            if (_frozenFrame == null) return;

            float areaWidth = _snipMode.layout.width;
            float areaHeight = _snipMode.layout.height;
            if (areaWidth <= 0 || areaHeight <= 0) return;

            // Panel space (origin top-left) -> texture space (origin bottom-left).
            int texWidth = _frozenFrame.width;
            int texHeight = _frozenFrame.height;
            float sx = texWidth / areaWidth;
            float sy = texHeight / areaHeight;

            int x = Mathf.Clamp(Mathf.FloorToInt(panelRect.xMin * sx), 0, texWidth - 1);
            int xMax = Mathf.Clamp(Mathf.CeilToInt(panelRect.xMax * sx), x + 1, texWidth);
            int top = Mathf.Clamp(Mathf.FloorToInt(panelRect.yMin * sy), 0, texHeight - 1);
            int bottom = Mathf.Clamp(Mathf.CeilToInt(panelRect.yMax * sy), top + 1, texHeight);

            var rect = new RectInt(x, texHeight - bottom, xMax - x, bottom - top);
            var cropped = ImageUtility.Crop(_frozenFrame, rect);
            cropped = ImageUtility.FitWithin(cropped, _settings.maxImageSize);

            _snipping = false;
            BeginDraw(cropped);
        }

        // ================================================================== Draw

        void SetupDraw()
        {
            var colorRow = Required(_root, "color-row");
            for (int i = 0; i < BrushColors.Length; i++)
            {
                int index = i;
                var swatch = new Button(() => SelectColor(index));
                swatch.AddToClassList("pf-swatch");
                swatch.style.backgroundColor = BrushColors[i];
                colorRow.Add(swatch);
                _colorButtons.Add(swatch);
            }

            var sizeRow = Required(_root, "size-row");
            for (int i = 0; i < BrushSizes.Length; i++)
            {
                int index = i;
                var button = new Button(() => SelectSize(index));
                button.AddToClassList("pf-size");
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("pf-size-dot");
                float diameter = BrushSizes[i] * 2f;
                dot.style.width = diameter;
                dot.style.height = diameter;
                dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius =
                    dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = diameter / 2f;
                button.Add(dot);
                sizeRow.Add(button);
                _sizeButtons.Add(button);
            }

            SelectColor(0);
            SelectSize(1);

            _undoButton.clicked += Undo;
            Required<Button>(_root, "draw-clear").clicked += () =>
            {
                _annotation?.Clear();
                UpdateDrawButtons();
            };
            Required<Button>(_root, "draw-cancel").clicked += CancelDraw;
            Required<Button>(_root, "draw-done").clicked += FinishDraw;

            _drawArea.RegisterCallback<GeometryChangedEvent>(_ => LayoutDrawImage());
            _drawImage.RegisterCallback<PointerDownEvent>(OnDrawPointerDown);
            _drawImage.RegisterCallback<PointerMoveEvent>(OnDrawPointerMove);
            _drawImage.RegisterCallback<PointerUpEvent>(OnDrawPointerUp);
            _drawImage.RegisterCallback<PointerCaptureOutEvent>(_ => EndStroke());
        }

        /// <summary>Starts drawing on <paramref name="workingTexture"/>, which the draw tool takes ownership of.</summary>
        void BeginDraw(Texture2D workingTexture)
        {
            DiscardDrawing();
            _annotation = new AnnotationTool(workingTexture);
            _drawImage.style.backgroundImage = new StyleBackground(workingTexture);
            SetMode(FeedbackViewMode.Draw);
            UpdateDrawButtons();
            _drawImage.schedule.Execute(LayoutDrawImage);
        }

        void FinishDraw()
        {
            if (_annotation == null) return;
            EndStroke();
            SetScreenshot(_annotation.TakeResult());
            _annotation = null;
            _drawImage.style.backgroundImage = new StyleBackground(StyleKeyword.None);
            SetMode(FeedbackViewMode.Panel);
            RefreshState();
        }

        void CancelDraw()
        {
            if (Mode != FeedbackViewMode.Draw) return;
            DiscardDrawing();
            SetMode(FeedbackViewMode.Panel);
        }

        void DiscardDrawing()
        {
            _drawing = false;
            _annotation?.Dispose();
            _annotation = null;
            _drawImage.style.backgroundImage = new StyleBackground(StyleKeyword.None);
        }

        void Undo()
        {
            _annotation?.Undo();
            UpdateDrawButtons();
        }

        void SelectColor(int index)
        {
            _colorIndex = index;
            for (int i = 0; i < _colorButtons.Count; i++) _colorButtons[i].EnableInClassList("pf-swatch--selected", i == index);
        }

        void SelectSize(int index)
        {
            _sizeIndex = index;
            for (int i = 0; i < _sizeButtons.Count; i++) _sizeButtons[i].EnableInClassList("pf-size--selected", i == index);
        }

        void UpdateDrawButtons() => _undoButton.SetEnabled(_annotation != null && _annotation.CanUndo);

        void LayoutDrawImage()
        {
            if (_annotation == null) return;
            var area = _drawArea.contentRect.size;
            var texture = _annotation.Texture;
            if (area.x <= 0 || area.y <= 0 || float.IsNaN(area.x) || float.IsNaN(area.y)) return;

            float scale = Mathf.Min(area.x / texture.width, area.y / texture.height, 4f);
            _drawImage.style.width = Mathf.Floor(texture.width * scale);
            _drawImage.style.height = Mathf.Floor(texture.height * scale);
        }

        void OnDrawPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || _annotation == null) return;
            _drawing = true;
            _drawImage.CapturePointer(evt.pointerId);
            _annotation.BeginStroke(ToTexture(evt.position), BrushRadiusInTexels(), BrushColors[_colorIndex]);
            evt.StopPropagation();
        }

        void OnDrawPointerMove(PointerMoveEvent evt)
        {
            if (!_drawing || _annotation == null || !_drawImage.HasPointerCapture(evt.pointerId)) return;
            _annotation.StrokeTo(ToTexture(evt.position));
        }

        void OnDrawPointerUp(PointerUpEvent evt)
        {
            if (_drawImage.HasPointerCapture(evt.pointerId)) _drawImage.ReleasePointer(evt.pointerId);
            EndStroke();
        }

        void EndStroke()
        {
            if (!_drawing) return;
            _drawing = false;
            _annotation?.EndStroke();
            UpdateDrawButtons();
        }

        Vector2 ToTexture(Vector2 panelPosition)
        {
            var local = _drawImage.WorldToLocal(panelPosition);
            var size = _drawImage.layout.size;
            var texture = _annotation.Texture;
            return new Vector2(
                local.x / size.x * texture.width,
                (1f - local.y / size.y) * texture.height);
        }

        float BrushRadiusInTexels()
        {
            float displayWidth = _drawImage.layout.width;
            if (displayWidth <= 0) return BrushSizes[_sizeIndex];
            return BrushSizes[_sizeIndex] * _annotation.Texture.width / displayWidth;
        }

        // ================================================================== Helpers

        static void SetRect(VisualElement element, float x, float y, float width, float height)
        {
            element.style.left = x;
            element.style.top = y;
            element.style.width = Mathf.Max(0, width);
            element.style.height = Mathf.Max(0, height);
        }

        static Color Opaque(Color color) => new Color(color.r, color.g, color.b, 1f);

        static VisualElement Required(VisualElement root, string name) => Required<VisualElement>(root, name);

        static T Required<T>(VisualElement root, string name) where T : VisualElement
        {
            var element = root.Q<T>(name);
            if (element == null) throw new InvalidOperationException($"[PlaytestFeedback] FeedbackPanel.uxml is missing a {typeof(T).Name} named '{name}'.");
            return element;
        }
    }
}
