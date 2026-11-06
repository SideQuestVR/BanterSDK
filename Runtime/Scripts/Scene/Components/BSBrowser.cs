using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;
using Newtonsoft.Json.Linq;
using SideQuest.Ora;

namespace BS
{
    [Serializable]
    public class BrowserAction
    {
        public string actionType;
        public float numParam1;
        public float numParam2;
        public float numParam3;
        public float numParam4;
        public string strParam1;
        public string strParam2;
        public string strParam3;
        public string strParam4;
    }

    [Serializable]
    public class BrowserActions
    {
        public BrowserAction[] actions;
    }

    public static class BrowserActionType
    {
        public const string click2d = "click2d";
        public const string click = "click";
        public const string keypress = "keypress";
        public const string scroll = "scroll";
        public const string delayseconds = "delayseconds";
        public const string runscript = "runscript";
        public const string goback = "goback";
        public const string goforward = "goforward";
        public const string postmessage = "postmessage";
    }

    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSBrowser : BSComponentBase
    {
        [Tooltip("The URL of the webpage to display")]
        [See(initial = "")][SerializeField] internal string url = "";

        [Tooltip("The number of mipmaps to use for the browser texture")]
        [See(initial = "4")][SerializeField] internal int mipMaps = 4;

        [Tooltip("The number of pixels per unit for the browser texture")]
        [See(initial = "1200")][SerializeField] internal float pixelsPerUnit = 1200;

        [Tooltip("The width of the browser page in pixels")]
        [See(initial = "1024")][SerializeField] internal float pageWidth = 1280;

        [Tooltip("The height of the browser page in pixels")]
        [See(initial = "576")][SerializeField] internal float pageHeight = 720;

        [Tooltip("Actions to run as soon as the browser is created, before the page has loaded (start with a delayseconds action to wait for it), as JSON, e.g. {\"actions\":[{\"actionType\":\"click2d\",\"numParam1\":0.5,\"numParam2\":0.5}]}")]
        [See(initial = "")][SerializeField] internal string actions = "";

        [Tooltip("How the page's sound fades with distance. Flat: the same volume at any distance. Logarithmic: like a real sound, quieter the further away from Min Distance, no quieter beyond Max Distance. Linear: evenly, from full volume at Min Distance to silence at Max Distance.")]
        [See(initial = "0")][SerializeField] internal AudioRolloff rolloffMode = AudioRolloff.Flat;

        [Tooltip("Within this distance, in metres, the page's sound plays at full volume (Logarithmic and Linear).")]
        [See(initial = "1")][SerializeField] internal float minDistance = 1;

        [Tooltip("The distance, in metres, where the fade ends: silence for Linear; Logarithmic gets no quieter beyond it.")]
        [See(initial = "500")][SerializeField] internal float maxDistance = 500;
        public UnityEvent<string> OnReceiveBrowserMessage = new UnityEvent<string>();
        /// <summary>
        /// This browser's texture: its first frame, and again whenever a resize replaces it. The same
        /// texture is in the asset registry as <see cref="TextureAssetId"/>, for materials and the page.
        /// </summary>
        public UnityEvent<Texture2D> OnBrowserTexture = new UnityEvent<Texture2D>();
        // Only read in Greenfield (its menu browser sets it), so creators don't see it: it does nothing in their spaces.
#if !GREENFIELD_PROJECT
        [HideInInspector]
#endif
        public bool IsStreamingBrowser = false;

        /// <summary>Asset reference of this browser's texture: <c>asset_browser_</c> + the component's id, which the page sees as the BS.Browser's <c>unityId</c>.</summary>
        public string TextureAssetId => $"asset_browser_{cid}";

        GameObject browser;
        OraView _oraView;
        readonly SpeakerTracker _speakers = new SpeakerTracker();
        Coroutine _actionsCoroutine;
        List<BrowserAction> _pendingActions = new List<BrowserAction>();

        // The page gets a 'bsmessage' event. 'bantermessage', its name before the rename, still fires for older pages.
        const string DISPATCH_MESSAGE_TEMPLATE =
            @"(() => { const detail = { message: '{0}' }; window.dispatchEvent(new CustomEvent('bsmessage', { detail })); window.dispatchEvent(new CustomEvent('bantermessage', { detail })); })();";


        [Method]
        public void _ToggleInteraction(bool enabled)
        {
            if (browser != null)
            {
                var handler = browser.GetComponent<UIToolkitInputHandler>();
                if (handler != null)
                    handler.enabled = enabled;
            }
        }

        [Method]
        public void _ToggleKeyboard(bool enabled)
        {
            // Keyboard open/close is handled by BrowserKeyboardHandler via OraManager events
        }

        [Method]
        public void _RunActions(string actions)
        {
            if (string.IsNullOrWhiteSpace(actions) || _oraView == null)
                return;
            try
            {
                var actionList = JObject.Parse(actions).ToObject<BrowserActions>();
                _pendingActions.AddRange(actionList.actions);
                if (_actionsCoroutine == null)
                    _actionsCoroutine = StartCoroutine(RunActionsCoroutine());
            }
            catch (Exception e)
            {
                Debug.LogError($"[BSBrowser] Failed to parse actions: {actions}");
                Debug.LogException(e);
            }
        }

        internal override void StartStuff()
        {
            if (BSStarterUpper.SafeMode)
                return;

            SetupBrowser();
            OnReceiveBrowserMessage.AddListener((message) => BSScene.Instance().link.OnReceiveBrowserMessage(this, message));
        }

        internal override void UpdateStuff()
        {
        }

        private void SetupBrowser(List<PropertyName> changedProperties = null)
        {
            if (browser == null)
            {
#if GREENFIELD_PROJECT
                browser = Instantiate(Resources.Load<GameObject>(IsStreamingBrowser ? "Prefabs/BanterBrowserStreaming" : "Prefabs/Browser/BSBrowser"), transform);
#else
                browser = Instantiate(Resources.Load<GameObject>("Prefabs/Browser/BSBrowser"), transform);
#endif
                browser.name = "BSBrowser";

                _oraView = browser.GetComponent<OraView>();
                // The SDK's prefab is saved inactive and without Ora's components: a serialized reference to Ora's
                // scripts is a missing script wherever Ora is the precompiled DLL, which is every creator project, and
                // the browser then never loads (as BSStarterUpper notes for the space view). So they're added here,
                // configured before anything on the object wakes.
                if (_oraView == null)
                    _oraView = AddWebView(browser);
                if (!browser.activeSelf)
                    browser.SetActive(true);
                if (_oraView != null)
                {
                    _oraView.browserMessage.AddListener(OnBrowserMessage);
                    _oraView.textureChanged.AddListener(OnTextureChanged);
                    if (_oraView.texture2D != null)
                        OnTextureChanged(_oraView.texture2D);
                }

                if (!string.IsNullOrEmpty(actions))
                    _RunActions(actions);
            }

            if ((changedProperties?.Contains(PropertyName.url) ?? true) && !string.IsNullOrEmpty(url))
            {
                // The url comes from world content: a local file would let a page read the app's files (the session
                // token among them) back with a runscript action. Ora refuses these too; this keeps them off the wire.
                // What is checked is what is loaded: the url as URL parsing reads it, a host name sent as http://.
                var load = UrlToLoad(url);
                if (load != null)
                    _oraView?.LoadUrl(load);
                else if (NormalizeUrl(url).Length > 0)
                    Debug.LogWarning($"[BS] Browser url refused (only web pages may be loaded): '{ShownUrl(url)}'");
            }

            if ((changedProperties?.Contains(PropertyName.pageWidth) ?? true) || (changedProperties?.Contains(PropertyName.pageHeight) ?? true))
            {
                UIDocument doc = browser.GetComponent<UIDocument>();
                if (doc)
                    doc.worldSpaceSize = new Vector2(pageWidth, pageHeight);
            }

            if (changedProperties == null || changedProperties.Contains(PropertyName.rolloffMode)
                || changedProperties.Contains(PropertyName.minDistance) || changedProperties.Contains(PropertyName.maxDistance))
                ApplyRolloff();
            SetLoadedIfNot();
        }

        // The page's sound plays through the speakers Ora's audio emitter builds, which come with Ora's own flat fade. Ora
        // builds them when the view's audio starts and builds new ones whenever it recreates it (an output-rate change,
        // for one), so each new set gets this browser's fade. There are none where the page plays its own sound
        // (audioMode Off).
        void LateUpdate()
        {
            if (_speakers.Refresh(_oraView != null ? _oraView.audioEmitter : null))
                ApplyRolloff();
        }

        void ApplyRolloff()
        {
            foreach (var speaker in _speakers.Speakers)
                if (speaker != null)
                    ApplyRolloff(speaker, rolloffMode, minDistance, maxDistance);
        }

        /// <summary>Sets one speaker's fade. Flat is how Ora sets up its own speakers: a constant custom curve.</summary>
        internal static void ApplyRolloff(AudioSource speaker, AudioRolloff mode, float min, float max)
        {
            // A script can send anything: keep min a real distance (Logarithmic is silent everywhere at 0) and max beyond it.
            min = float.IsNaN(min) || float.IsInfinity(min) ? 1f : Mathf.Max(0.01f, min);
            max = float.IsNaN(max) || float.IsInfinity(max) ? 500f : max;
            max = Mathf.Max(max, min + 0.01f);
            switch (mode)
            {
                case AudioRolloff.Logarithmic:
                    speaker.rolloffMode = AudioRolloffMode.Logarithmic;
                    break;
                case AudioRolloff.Linear:
                    speaker.rolloffMode = AudioRolloffMode.Linear;
                    break;
                default:
                    speaker.rolloffMode = AudioRolloffMode.Custom;
                    speaker.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
                    break;
            }
            // Unity keeps minDistance at or below the current maxDistance, so a larger max goes first.
            if (min > speaker.maxDistance)
            {
                speaker.maxDistance = max;
                speaker.minDistance = min;
            }
            else
            {
                speaker.minDistance = min;
                speaker.maxDistance = max;
            }
        }

        /// <summary>
        /// The speakers Ora's audio emitter built for this browser's view. They're its children, each with an
        /// OraSpeakerFilter pointing back at it, so they're looked for again whenever its children change or one of
        /// them is gone.
        /// </summary>
        internal sealed class SpeakerTracker
        {
            OraAudioEmitter _emitter;
            int _childCount = -1;
            readonly List<OraSpeakerFilter> _filters = new List<OraSpeakerFilter>();
            internal readonly List<AudioSource> Speakers = new List<AudioSource>();

            /// <summary>Looks for the speakers again when they may have changed. True when it found any.</summary>
            internal bool Refresh(OraAudioEmitter emitter)
            {
                // Only the speakers Ora builds itself. A Custom emitter plays through AudioSources someone set up
                // on purpose (Greenfield's streaming browser has its own fade), and Ora leaves those alone too.
                if (emitter != null && emitter.speakerMode != OraSpeakerMode.Managed)
                    emitter = null;
                int childCount = emitter != null ? emitter.transform.childCount : -1;
                if (emitter == _emitter && childCount == _childCount && !AnyGone())
                    return false;
                _emitter = emitter;
                _childCount = childCount;
                Speakers.Clear();
                if (emitter == null)
                    return false;
                emitter.GetComponentsInChildren(true, _filters);
                foreach (var filter in _filters)
                {
                    if (filter.emitter != emitter)
                        continue;
                    var speaker = filter.GetComponent<AudioSource>();
                    if (speaker != null)
                        Speakers.Add(speaker);
                }
                return Speakers.Count > 0;
            }

            bool AnyGone()
            {
                foreach (var speaker in Speakers)
                    if (speaker == null)
                        return true;
                return false;
            }
        }

        // UIToolkitInputHandler keeps its view in a private serialized field and doesn't look for one itself.
        static readonly System.Reflection.FieldInfo InputHandlerWebView =
            typeof(UIToolkitInputHandler).GetField("webview", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        static readonly System.Reflection.FieldInfo InputHandlerScrollSensitivity =
            typeof(UIToolkitInputHandler).GetField("scrollSensitivity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // What Prefabs/Browser/BSBrowser used to serialize on its OraView and UIToolkitInputHandler.
        static OraView AddWebView(GameObject browser)
        {
            var view = browser.AddComponent<OraView>();
            view.userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.6269.2 Safari/537.36";
            view.injectShims = true;
            view.injectMediaAndWebRTC = true;
            view.size = new Vector2Int(1280, 720);
            view.renderMode = SideQuest.Ora.RenderMode.UIToolkit;
            view.uiToolkitElementClass = "world-web-view";
            view.sizingMode = SizingMode.UIToolkit;
            view.scaleTransform = browser.transform;
            view.skipUploadWhenInvisible = true;

            if (InputHandlerWebView == null)
            {
                // Without its view the handler throws in Start, so the browser goes without input instead.
                Debug.LogWarning("[BSBrowser] This Ora's UIToolkitInputHandler has no webview field, so the browser won't take clicks.");
                return view;
            }
            var input = browser.AddComponent<UIToolkitInputHandler>();
            InputHandlerWebView.SetValue(input, view);
            InputHandlerScrollSensitivity?.SetValue(input, 50f);
            return view;
        }

        void OnTextureChanged(Texture2D texture)
        {
            if (texture == null) return;
            BSAssetRegistry.Instance.SetAsset(TextureAssetId, texture, AssetType.Texture2D, "browser");
            OnBrowserTexture?.Invoke(texture);
        }

        void OnBrowserMessage(string arg0, string type, string data)
        {
            if (data != null)
                OnReceiveBrowserMessage?.Invoke(data);
            else
                Debug.LogWarning("[BSBrowser] Received empty browser message");
        }

        IEnumerator RunActionsCoroutine()
        {
            while (_pendingActions.Count > 0)
            {
                var action = _pendingActions[0];
                _pendingActions.RemoveAt(0);
                switch (action.actionType)
                {
                    case BrowserActionType.delayseconds:
                        if (action.numParam1 > 0)
                            yield return new WaitForSeconds(action.numParam1);
                        break;
                    case BrowserActionType.goback:
                        _oraView.GoBack();
                        break;
                    case BrowserActionType.goforward:
                        _oraView.GoForward();
                        break;
                    case BrowserActionType.click2d:
                        try
                        {
                            _oraView.MouseInput((int)action.numParam1, (int)action.numParam2, "MouseDown");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError("[BSBrowser] click2d failed");
                            Debug.LogException(ex);
                        }
                        break;
                    case BrowserActionType.runscript:
                        if (!string.IsNullOrWhiteSpace(action.strParam1))
                        {
                            try
                            {
                                _oraView.EvaluateJS(action.strParam1, result =>
                                    Debug.Log($"[BSBrowser] JS '{action.strParam1}' returned '{result}'"));
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"[BSBrowser] runscript failed: {action.strParam1}");
                                Debug.LogException(ex);
                            }
                        }
                        break;
                    case BrowserActionType.keypress:
                        if (!string.IsNullOrEmpty(action.strParam1))
                        {
                            try
                            {
                                _oraView.KeyInput(action.strParam1.Replace("Space", " "), OraKeyFlags.None);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[BSBrowser] keypress '{action.strParam1}' failed: {ex.Message}");
                            }
                        }
                        break;
                    case BrowserActionType.postmessage:
                        if (!string.IsNullOrWhiteSpace(action.strParam1))
                        {
                            try
                            {
                                // It goes inside a JavaScript string literal, which a quote, backslash or line break would end or break.
                                var msg = action.strParam1.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "\\r").Replace("\n", "\\n");
                                _oraView.EvaluateJS(DISPATCH_MESSAGE_TEMPLATE.Replace("{0}", msg));
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError("[BSBrowser] postmessage failed");
                                Debug.LogException(ex);
                            }
                        }
                        break;
                    default:
                        Debug.LogWarning($"[BSBrowser] Unknown action type: {action.actionType}");
                        break;
                }
            }
            _actionsCoroutine = null;
        }

        internal override void DestroyStuff()
        {
            if (_oraView != null)
            {
                _oraView.browserMessage.RemoveListener(OnBrowserMessage);
                _oraView.textureChanged.RemoveListener(OnTextureChanged);
                BSAssetRegistry.Instance.UnregisterAsset(TextureAssetId);
            }
            _oraView = null;
            _speakers.Refresh(null);

            if (_actionsCoroutine != null)
            {
                StopCoroutine(_actionsCoroutine);
                _actionsCoroutine = null;
            }

            if (browser != null)
            {
                Destroy(browser);
                browser = null;
            }
        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupBrowser(changedProperties);
        }
        /// <summary>
        /// The url as browsers read it. URL parsing (WHATWG, and Chromium's) ignores leading and trailing C0 controls
        /// and spaces, and tabs and line breaks anywhere: "\u0001file:..." and "fi\tle:..." are file URLs.
        /// </summary>
        internal static string NormalizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            var sb = new System.Text.StringBuilder(url.Length);
            foreach (var c in url)
                if (c != '\t' && c != '\n' && c != '\r') sb.Append(c);
            int start = 0, end = sb.Length;
            while (start < end && sb[start] <= ' ') start++;
            while (end > start && sb[end - 1] <= ' ') end--;
            return sb.ToString(start, end - start);
        }

        /// <summary>
        /// What a world browser loads for <paramref name="url"/>, or null when it may not load it. Web pages only: an
        /// http/https url, about:blank, or a host name without a scheme ("www.youtube.com/...", "localhost:8080/x"),
        /// which is sent as http:// so no host has to guess what it is. Anything else is refused: file:, javascript:,
        /// data:, intent: and the rest. The url is read as URL parsing reads it (<see cref="NormalizeUrl"/>). Ora's
        /// hosts enforce the same underneath (Electron viewUrlToLoad, Android OraUrlPolicy and LoadUrl).
        /// </summary>
        internal static string UrlToLoad(string url)
        {
            var u = NormalizeUrl(url);
            if (u.Length == 0) return null;
            if (string.Equals(u, "about:blank", StringComparison.OrdinalIgnoreCase)) return u;
            var scheme = Scheme(u);
            if (scheme == null) return "http://" + u;
            // Sent with the scheme in lower case ("HTTPS://x" as "https://x"): what every host compares against.
            return scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                ? scheme.ToLowerInvariant() + u.Substring(scheme.Length)
                : null;
        }

        static readonly System.Text.RegularExpressions.Regex SchemePrefix = new System.Text.RegularExpressions.Regex(@"^([A-Za-z][A-Za-z0-9+.\-]*):");
        static readonly System.Text.RegularExpressions.Regex HostName = new System.Text.RegularExpressions.Regex(@"^[A-Za-z0-9.\-]+$");
        static readonly System.Text.RegularExpressions.Regex PortThenPath = new System.Text.RegularExpressions.Regex(@"^[0-9]+([/?#]|$)");

        /// <summary>
        /// The url's scheme, or null without one. A host name with a port ("localhost:8080/x", "www.example.com:443/x")
        /// parses as a scheme too: it is a host when the part before the ':' looks like one (a dot, or localhost) and only
        /// digits follow it, up to the end or a / ? #. "javascript:0;..." and "file:0/..." are schemes. Same rule as Ora's
        /// Windows host (app-protocol.ts hasScheme).
        /// </summary>
        static string Scheme(string url)
        {
            var m = SchemePrefix.Match(url);
            if (!m.Success) return null;
            var name = m.Groups[1].Value;
            var hostLike = HostName.IsMatch(name) && (name.IndexOf('.') >= 0 || name.Equals("localhost", StringComparison.OrdinalIgnoreCase));
            return hostLike && PortThenPath.IsMatch(url.Substring(m.Length)) ? null : name;
        }

        /// <summary>A refused url as logged: capped, because Player.log can be uploaded, and on one line.</summary>
        internal static string ShownUrl(string url)
        {
            var chars = (url ?? "").ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (chars[i] < ' ' || chars[i] == '\u007f') chars[i] = '?';
            var shown = new string(chars);
            return shown.Length > 120 ? shown.Substring(0, 120) + "..." : shown;
        }

    }
}