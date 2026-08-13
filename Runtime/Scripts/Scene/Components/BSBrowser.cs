using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;
#if BANTER_ORA
using Newtonsoft.Json.Linq;
using SideQuest.Ora;
#endif

namespace Banter.SDK
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
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BSBrowser : BanterComponentBase
    {
        [Tooltip("The URL of the webpage to display")]
        [See(initial = "")][SerializeField] internal string url;

        [Tooltip("The number of mipmaps to use for the browser texture")]
        [See(initial = "4")][SerializeField] internal int mipMaps = 4;

        [Tooltip("The number of pixels per unit for the browser texture")]
        [See(initial = "1200")][SerializeField] internal float pixelsPerUnit = 1200;

        [Tooltip("The width of the browser page in pixels")]
        [See(initial = "1024")][SerializeField] internal float pageWidth = 1280;

        [Tooltip("The height of the browser page in pixels")]
        [See(initial = "576")][SerializeField] internal float pageHeight = 720;

        [Tooltip("A comma-separated list of actions to run after the page has loaded (e.g., 'click2d,0.5,0.5')")]
        [See(initial = "")][SerializeField] internal string actions;
        public UnityEvent<string> OnReceiveBrowserMessage = new UnityEvent<string>();
        public bool IsStreamingBrowser = false;

        GameObject browser;
#if BANTER_ORA
        OraView _oraView;
        Coroutine _actionsCoroutine;
        List<BrowserAction> _pendingActions = new List<BrowserAction>();

        const string BANTER_DISPATCH_MESSAGE_TEMPLATE =
            @"window.dispatchEvent(new CustomEvent('bantermessage', { detail: { message: '{0}' } }));";
#endif

        [Method]
        public void _ToggleInteraction(bool enabled)
        {
#if BANTER_ORA
            if (browser != null)
            {
                var handler = browser.GetComponent<UIToolkitInputHandler>();
                if (handler != null)
                    handler.enabled = enabled;
            }
#endif
        }

        [Method]
        public void _ToggleKeyboard(bool enabled)
        {
            // Keyboard open/close is handled by BrowserKeyboardHandler via OraManager events
        }

        [Method]
        public void _RunActions(string actions)
        {
#if BANTER_ORA
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
#endif
        }

        internal override void StartStuff()
        {
            if (BanterStarterUpper.SafeMode)
                return;

            SetupBrowser();
            OnReceiveBrowserMessage.AddListener((message) => BanterScene.Instance().link.OnReceiveBrowserMessage(this, message));
        }

        internal override void UpdateStuff()
        {
        }

        private void SetupBrowser(List<PropertyName> changedProperties = null)
        {
            if (browser == null)
            {
#if BANTER_EDITOR
                browser = Instantiate(Resources.Load<GameObject>(IsStreamingBrowser ? "Prefabs/BanterBrowserStreaming" : "Prefabs/BanterBrowserBuild"), transform);
#else
                browser = Instantiate(Resources.Load<GameObject>("Prefabs/BSBrowser"), transform);
#endif
                browser.name = "BSBrowser";

#if BANTER_ORA
                _oraView = browser.GetComponent<OraView>();
                if (_oraView != null)
                    _oraView.browserMessage.AddListener(OnBrowserMessage);
#endif

                if (!string.IsNullOrEmpty(actions))
                    _RunActions(actions);
            }

#if BANTER_ORA
            if ((changedProperties?.Contains(PropertyName.url) ?? true) && !string.IsNullOrEmpty(url))
            {
                _oraView?.LoadUrl(url);
            }
#endif

            if ((changedProperties?.Contains(PropertyName.pageWidth) ?? true) || (changedProperties?.Contains(PropertyName.pageHeight) ?? true))
            {
                UIDocument doc = browser.GetComponent<UIDocument>();
                if (doc)
                    doc.worldSpaceSize = new Vector2(pageWidth, pageHeight);
            }
            SetLoadedIfNot();
        }

#if BANTER_ORA
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
                                var msg = action.strParam1.Replace("\\", "\\\\").Replace("'", "\\'");
                                _oraView.EvaluateJS(BANTER_DISPATCH_MESSAGE_TEMPLATE.Replace("{0}", msg));
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
#endif

        internal override void DestroyStuff()
        {
#if BANTER_ORA
            if (_oraView != null)
                _oraView.browserMessage.RemoveListener(OnBrowserMessage);
            _oraView = null;

            if (_actionsCoroutine != null)
            {
                StopCoroutine(_actionsCoroutine);
                _actionsCoroutine = null;
            }
#endif

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
        // BANTER COMPILED CODE 
        public System.String Url { get { return url; } set { url = value; UpdateCallback(new List<PropertyName> { PropertyName.url }); } }
        public System.Int32 MipMaps { get { return mipMaps; } set { mipMaps = value; UpdateCallback(new List<PropertyName> { PropertyName.mipMaps }); } }
        public System.Single PixelsPerUnit { get { return pixelsPerUnit; } set { pixelsPerUnit = value; UpdateCallback(new List<PropertyName> { PropertyName.pixelsPerUnit }); } }
        public System.Single PageWidth { get { return pageWidth; } set { pageWidth = value; UpdateCallback(new List<PropertyName> { PropertyName.pageWidth }); } }
        public System.Single PageHeight { get { return pageHeight; } set { pageHeight = value; UpdateCallback(new List<PropertyName> { PropertyName.pageHeight }); } }
        public System.String Actions { get { return actions; } set { actions = value; UpdateCallback(new List<PropertyName> { PropertyName.actions }); } }

        BanterScene _scene;
        public BanterScene scene
        {
            get
            {
                if (_scene == null)
                {
                    _scene = BanterScene.Instance();
                }
                return _scene;
            }
        }
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        internal override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.url, PropertyName.mipMaps, PropertyName.pixelsPerUnit, PropertyName.pageWidth, PropertyName.pageHeight, PropertyName.actions, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "Browser" +  PropertyName.url + url + PropertyName.mipMaps + mipMaps + PropertyName.pixelsPerUnit + pixelsPerUnit + PropertyName.pageWidth + pageWidth + PropertyName.pageHeight + pageHeight + PropertyName.actions + actions;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.Browser);


            oid = gameObject.GetInstanceID();
            cid = GetInstanceID();

            if (constructorProperties != null)
            {
                Deserialise(constructorProperties);
            }

            SyncProperties(true);

        }

        void Awake()
        {
            BanterScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }

        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);

            DestroyStuff();
        }

        void ToggleInteraction(Boolean enabled)
        {
            _ToggleInteraction(enabled);
        }
        void ToggleKeyboard(Boolean enabled)
        {
            _ToggleKeyboard(enabled);
        }
        void RunActions(String actions)
        {
            _RunActions(actions);
        }
        internal override object CallMethod(string methodName, List<object> parameters)
        {

            if (methodName == "ToggleInteraction" && parameters.Count == 1 && parameters[0] is Boolean)
            {
                var enabled = (Boolean)parameters[0];
                ToggleInteraction(enabled);
                return null;
            }
            else if (methodName == "ToggleKeyboard" && parameters.Count == 1 && parameters[0] is Boolean)
            {
                var enabled = (Boolean)parameters[0];
                ToggleKeyboard(enabled);
                return null;
            }
            else if (methodName == "RunActions" && parameters.Count == 1 && parameters[0] is String)
            {
                var actions = (String)parameters[0];
                RunActions(actions);
                return null;
            }
            else
            {
                return null;
            }
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BanterString)
                {
                    var valurl = (BanterString)values[i];
                    if (valurl.n == PropertyName.url)
                    {
                        url = valurl.x;
                        changedProperties.Add(PropertyName.url);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valmipMaps = (BanterInt)values[i];
                    if (valmipMaps.n == PropertyName.mipMaps)
                    {
                        mipMaps = valmipMaps.x;
                        changedProperties.Add(PropertyName.mipMaps);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valpixelsPerUnit = (BanterFloat)values[i];
                    if (valpixelsPerUnit.n == PropertyName.pixelsPerUnit)
                    {
                        pixelsPerUnit = valpixelsPerUnit.x;
                        changedProperties.Add(PropertyName.pixelsPerUnit);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valpageWidth = (BanterFloat)values[i];
                    if (valpageWidth.n == PropertyName.pageWidth)
                    {
                        pageWidth = valpageWidth.x;
                        changedProperties.Add(PropertyName.pageWidth);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valpageHeight = (BanterFloat)values[i];
                    if (valpageHeight.n == PropertyName.pageHeight)
                    {
                        pageHeight = valpageHeight.x;
                        changedProperties.Add(PropertyName.pageHeight);
                    }
                }
                if (values[i] is BanterString)
                {
                    var valactions = (BanterString)values[i];
                    if (valactions.n == PropertyName.actions)
                    {
                        actions = valactions.x;
                        changedProperties.Add(PropertyName.actions);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.url,
                    type = PropertyType.String,
                    value = url,
                    componentType = ComponentType.Browser,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.mipMaps,
                    type = PropertyType.Int,
                    value = mipMaps,
                    componentType = ComponentType.Browser,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.pixelsPerUnit,
                    type = PropertyType.Float,
                    value = pixelsPerUnit,
                    componentType = ComponentType.Browser,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.pageWidth,
                    type = PropertyType.Float,
                    value = pageWidth,
                    componentType = ComponentType.Browser,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.pageHeight,
                    type = PropertyType.Float,
                    value = pageHeight,
                    componentType = ComponentType.Browser,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.actions,
                    type = PropertyType.String,
                    value = actions,
                    componentType = ComponentType.Browser,
                    oid = oid,
                    cid = cid
                });
            }
            scene.SetFromUnityProperties(updates, callback);
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}