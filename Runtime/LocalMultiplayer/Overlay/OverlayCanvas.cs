using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BS.LocalMultiplayer.Overlay
{
    /// <summary>
    /// The overlay's uGUI view, built in code from the built-in font so it needs no assets and renders the same
    /// in Multiplayer Play Mode clones. A screen-space overlay canvas with a GraphicRaycaster: GraphicRaycaster
    /// reports distance 0 for overlay canvases, and the desktop controller's TryGetUIHit takes that as the nearest UI
    /// (BSDesktopController.cs:433-488), so a press on the panel becomes a UI press (:358-366) and starts no grab or
    /// scene click. The EventSystem then hands the click to the topmost graphic, which is this canvas at its
    /// sorting order. It knows nothing about the session; <see cref="OverlayModule"/> fills it.
    /// </summary>
    internal sealed class OverlayCanvas
    {
        // Above any world or screen UI a space is likely to have (the maximum is 32767).
        const int SortingOrder = 32000;
        const float Margin = 12f;
        const float PanelWidth = 430f;
        const int BodySize = 13;
        const int SmallSize = 12;

        static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.1f, 0.88f);
        static readonly Color TextColor = new Color(0.92f, 0.93f, 0.95f, 1f);
        static readonly Color ButtonNormal = new Color(0.2f, 0.23f, 0.28f, 1f);
        static readonly Color ButtonHighlighted = new Color(0.28f, 0.32f, 0.39f, 1f);
        static readonly Color ButtonPressed = new Color(0.15f, 0.17f, 0.21f, 1f);
        static readonly Color ButtonDisabled = new Color(0.2f, 0.23f, 0.28f, 0.45f);

        readonly GameObject _root;
        readonly CanvasScaler _scaler;
        readonly GameObject _pill;
        readonly Text _pillText;
        readonly GameObject _panel;
        readonly Text _title;
        readonly Text _you;
        readonly Text _relay;
        readonly Text _peers;
        readonly Text _diagnostics;
        readonly Text _status;
        readonly Button _rejoin;
        readonly Button _leaveJoin;
        readonly Text _leaveJoinLabel;
        readonly Button _clear;
        readonly Text _clearLabel;
        int _scaledForWidth = -1;
        int _scaledForHeight = -1;

        OverlayCanvas(Transform parent, Font font, Action show, Action hide, Action rejoin, Action leaveJoin, Action clear)
        {
            _root = new GameObject("[LocalMultiplayer Overlay]", typeof(RectTransform));
            if (parent != null)
            {
                _root.transform.SetParent(parent, false);
            }
            else
            {
                UnityEngine.Object.DontDestroyOnLoad(_root);
            }
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            _scaler = _root.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            _root.AddComponent<GraphicRaycaster>();
            UpdateScale();

            // Hidden: one clickable line that opens the panel.
            _pill = CreateUi("Pill", _root.transform);
            PinTopLeft((RectTransform)_pill.transform);
            var pillImage = _pill.AddComponent<Image>();
            pillImage.color = Color.white;
            var pillButton = _pill.AddComponent<Button>();
            Style(pillButton, pillImage, new Color(0.07f, 0.08f, 0.1f, 0.8f), new Color(0.16f, 0.18f, 0.22f, 0.9f));
            pillButton.onClick.AddListener(() => Clicked(show));
            var pillLayout = _pill.AddComponent<HorizontalLayoutGroup>();
            pillLayout.padding = new RectOffset(9, 9, 4, 5);
            SetControl(pillLayout, false);
            var pillFitter = _pill.AddComponent<ContentSizeFitter>();
            pillFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            pillFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _pillText = CreateText(_pill.transform, "Text", font, SmallSize, TextAnchor.MiddleLeft);
            _pillText.horizontalOverflow = HorizontalWrapMode.Overflow;

            // Shown: the panel. The background Image is a raycast target, so the whole panel area is UI.
            _panel = CreateUi("Panel", _root.transform);
            var panelRect = (RectTransform)_panel.transform;
            PinTopLeft(panelRect);
            panelRect.sizeDelta = new Vector2(PanelWidth, 0f);
            _panel.AddComponent<Image>().color = PanelColor;
            var panelLayout = _panel.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(10, 10, 8, 10);
            panelLayout.spacing = 5f;
            SetControl(panelLayout, true);
            var panelFitter = _panel.AddComponent<ContentSizeFitter>();
            panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var header = CreateUi("Header", _panel.transform);
            var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
            headerLayout.spacing = 6f;
            headerLayout.childAlignment = TextAnchor.MiddleLeft;
            SetControl(headerLayout, false);
            _title = CreateText(header.transform, "Title", font, BodySize + 1, TextAnchor.MiddleLeft);
            _title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            CreateButton(header.transform, font, "Hide", hide, 64f, false, out _);

            _you = CreateText(_panel.transform, "You", font, BodySize, TextAnchor.UpperLeft);
            _relay = CreateText(_panel.transform, "Relay", font, BodySize, TextAnchor.UpperLeft);
            _peers = CreateText(_panel.transform, "Peers", font, BodySize, TextAnchor.UpperLeft);
            _diagnostics = CreateText(_panel.transform, "Diagnostics", font, SmallSize, TextAnchor.UpperLeft);
            _status = CreateText(_panel.transform, "Status", font, SmallSize, TextAnchor.UpperLeft);

            var buttons = CreateUi("Buttons", _panel.transform);
            var buttonsLayout = buttons.AddComponent<HorizontalLayoutGroup>();
            buttonsLayout.spacing = 6f;
            SetControl(buttonsLayout, true);
            _rejoin = CreateButton(buttons.transform, font, "Rejoin", rejoin, 0f, true, out _);
            _leaveJoin = CreateButton(buttons.transform, font, "Leave", leaveJoin, 0f, true, out _leaveJoinLabel);
            _clear = CreateButton(buttons.transform, font, "Clear room state", clear, 0f, true, out _clearLabel);
        }

        /// <summary>Builds the overlay under <paramref name="parent"/> (the host object), panel or pill showing.</summary>
        internal static OverlayCanvas Build(Transform parent, bool panelVisible, Action show, Action hide, Action rejoin,
            Action leaveJoin, Action clear)
        {
            var view = new OverlayCanvas(parent, LoadFont(), show, hide, rejoin, leaveJoin, clear);
            view.ShowPanel(panelVisible);
            return view;
        }

        internal void Destroy()
        {
            if (_root == null)
            {
                return;
            }
            // Hidden at once: a deferred Destroy can outlive a script reload, and a frozen panel would mislead.
            _root.SetActive(false);
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_root);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
        }

        internal void ShowPanel(bool visible)
        {
            if (_panel.activeSelf != visible)
            {
                _panel.SetActive(visible);
            }
            if (_pill.activeSelf == visible)
            {
                _pill.SetActive(!visible);
            }
        }

        /// <summary>
        /// Follows the Game view size (<see cref="OverlayLayout.ScaleFactor"/>): down to 0.6x in a small Multiplayer
        /// Play Mode player window, so the panel leaves most of the world clickable, and up to 2x on a big screen.
        /// Cheap enough for every frame: it only touches the scaler when the size changed.
        /// </summary>
        internal void UpdateScale()
        {
            var width = Screen.width;
            var height = Screen.height;
            if (width == _scaledForWidth && height == _scaledForHeight)
            {
                return;
            }
            _scaledForWidth = width;
            _scaledForHeight = height;
            _scaler.scaleFactor = OverlayLayout.ScaleFactor(width, height);
        }

        internal void SetPill(string text) => Set(_pillText, text);

        internal void SetTitle(string text) => Set(_title, text);

        /// <summary>The panel's text blocks; an empty block collapses.</summary>
        internal void SetBlocks(string you, string relay, string peers, string diagnostics, string status)
        {
            SetBlock(_you, you);
            SetBlock(_relay, relay);
            SetBlock(_peers, peers);
            SetBlock(_diagnostics, diagnostics);
            SetBlock(_status, status);
        }

        internal void SetButtons(bool rejoinEnabled, string leaveJoinLabel, bool leaveJoinEnabled, bool showClear,
            string clearLabel, bool clearEnabled)
        {
            SetInteractable(_rejoin, rejoinEnabled);
            Set(_leaveJoinLabel, leaveJoinLabel);
            SetInteractable(_leaveJoin, leaveJoinEnabled);
            if (_clear.gameObject.activeSelf != showClear)
            {
                _clear.gameObject.SetActive(showClear);
            }
            Set(_clearLabel, clearLabel);
            SetInteractable(_clear, clearEnabled);
        }

        static void Set(Text text, string value)
        {
            value = value ?? string.Empty;
            if (!string.Equals(text.text, value, StringComparison.Ordinal))
            {
                text.text = value;
            }
        }

        static void SetBlock(Text text, string value)
        {
            var show = !string.IsNullOrEmpty(value);
            if (show)
            {
                Set(text, value);
            }
            if (text.gameObject.activeSelf != show)
            {
                text.gameObject.SetActive(show);
            }
        }

        static void SetInteractable(Button button, bool interactable)
        {
            if (button.interactable != interactable)
            {
                button.interactable = interactable;
            }
        }

        // A button keeps the EventSystem's selection after a click, and a scene EventSystem that sends navigation
        // events would then press it again on Space or Enter (Space also stands the desktop player up).
        static void Clicked(Action action)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem != null && eventSystem.currentSelectedGameObject != null)
            {
                eventSystem.SetSelectedGameObject(null);
            }
            try
            {
                action?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        static Font LoadFont()
        {
            try
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null)
                {
                    return font;
                }
            }
            catch (Exception)
            {
                // Falls through to an OS font.
            }
            Debug.LogWarning("[LocalMP][Overlay] The built-in LegacyRuntime.ttf is missing; using an OS font.");
            return Font.CreateDynamicFontFromOSFont("Arial", BodySize);
        }

        static GameObject CreateUi(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static void PinTopLeft(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(Margin, -Margin);
        }

        static void SetControl(HorizontalOrVerticalLayoutGroup layout, bool expandWidth)
        {
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = expandWidth;
            layout.childForceExpandHeight = false;
        }

        static Text CreateText(Transform parent, string name, Font font, int size, TextAnchor alignment)
        {
            var text = CreateUi(name, parent).AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = TextColor;
            text.alignment = alignment;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            // Only the panel background and the buttons take clicks.
            text.raycastTarget = false;
            return text;
        }

        static Button CreateButton(Transform parent, Font font, string label, Action onClick, float width, bool flexible,
            out Text labelText)
        {
            var go = CreateUi(label, parent);
            var image = go.AddComponent<Image>();
            image.color = Color.white;
            var button = go.AddComponent<Button>();
            Style(button, image, ButtonNormal, ButtonHighlighted);
            button.onClick.AddListener(() => Clicked(onClick));

            var layout = go.AddComponent<LayoutElement>();
            layout.minHeight = 22f;
            layout.preferredHeight = 22f;
            if (width > 0f)
            {
                layout.minWidth = width;
                layout.preferredWidth = width;
            }
            layout.flexibleWidth = flexible ? 1f : 0f;

            labelText = CreateText(go.transform, "Label", font, SmallSize, TextAnchor.MiddleCenter);
            labelText.text = label;
            labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rect = labelText.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return button;
        }

        static void Style(Button button, Image image, Color normal, Color highlighted)
        {
            button.targetGraphic = image;
            // Mouse only: arrow keys and WASD belong to the desktop player.
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = normal;
            colors.highlightedColor = highlighted;
            colors.pressedColor = ButtonPressed;
            colors.selectedColor = normal;
            colors.disabledColor = ButtonDisabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
        }
    }
}
