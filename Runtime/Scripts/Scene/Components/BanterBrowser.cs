using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Banter.SDK
{
    // [Serializable]
    // public class BrowserObject
    // {
    //     public bool enabled;
    //     public string url;
    //     public string instanceId;
    //     public bool remote;
    //     public float pixelsPerUnit;
    //     public float width;
    //     public float height;
    //     public int mipMaps;
    //     public BrowserAction[] afterLoadActions;
    // }

    // [Serializable]
    // public static class BrowserActionType
    // {
    //     public const string click2d = "click2d";
    //     public const string click = "click";
    //     public const string keypress = "keypress";
    //     public const string scroll = "scroll";
    //     public const string delayseconds = "delayseconds";
    //     public const string runscript = "runscript";
    //     public const string goback = "goback";
    //     public const string goforward = "goforward";
    //     public const string postmessage = "postmessage";
    // }

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
    /* 
    #### Banter Browser
    A browser component that can be added to a GameObject to display a webpage.

    **Properties**
    - `url` - The URL of the webpage to display.
    - `mipMaps` - The number of mipmaps to use.
    - `pixelsPerUnit` - The number of pixels per unit.
    - `actions` - A list of actions to run after the page has loaded.

    **Methods**
    - `ToggleInteraction(enabled: boolean)` - Toggles the interaction of the browser.
    - `RunActions(actions: string)` - Runs a list of actions on the browser.

    **Code Example**
    ```js
        const url = "https://www.google.com";
        const mipMaps = 4;
        const pixelsPerUnit = 1200;
        const actions = "click2d,0.5,0.5";
        const gameObject = new BS.GameObject("MyBrowser"); 
        const browser = await gameObject.AddComponent(new BS.BanterBrowser(url, mipMaps, pixelsPerUnit, actions));
        // ...
        browser.ToggleInteraction(true);
        // ...
        browser.RunActions("click2d,0.5,0.5");
    ```

    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterBrowser : BanterComponentBase
    {
        [Tooltip("The URL of the webpage to display")]
        [See(initial = "")][SerializeField] internal string url;

        [Tooltip("The number of mipmaps to use for the browser texture")]
        [See(initial = "4")][SerializeField] internal int mipMaps = 4;

        [Tooltip("The number of pixels per unit for the browser texture")]
        [See(initial = "1200")][SerializeField] internal float pixelsPerUnit = 1200;

        [Tooltip("The width of the browser page in pixels")]
        [See(initial = "1024")][SerializeField] internal float pageWidth = 1024;

        [Tooltip("The height of the browser page in pixels")]
        [See(initial = "576")][SerializeField] internal float pageHeight = 576;

        [Tooltip("A comma-separated list of actions to run after the page has loaded (e.g., 'click2d,0.5,0.5')")]
        [See(initial = "")][SerializeField] internal string actions;
        public UnityEvent<string> OnReceiveBrowserMessage = new UnityEvent<string>();
        [Method]
        public void _ToggleInteraction(bool enabled)
        {
            browser.SendMessage("ToggleInteraction", enabled);
        }
        [Method]
        public void _ToggleKeyboard(bool enabled)
        {
            browser.SendMessage("ToggleKeyboard", enabled);
        }
        [Method]
        public void _RunActions(string actions)
        {
            browser.SendMessage("RunActions", actions);
        }
        GameObject browser;

        internal override void StartStuff()
        {
            SetupBrowser();
            OnReceiveBrowserMessage.AddListener((message) => BanterScene.Instance().link.OnReceiveBrowserMessage(this, message));
        }

        private void SetupBrowser(List<PropertyName> changedProperties = null)
        {
            if (browser == null)
            {
#if BANTER_EDITOR
                browser = Instantiate(Resources.Load<GameObject>("Prefabs/BanterBrowserBuild"), transform);
#else
                browser = Instantiate(Resources.Load<GameObject>("Prefabs/BanterBrowser"), transform);
#endif
                browser.name = "BanterBrowser";
                browser.SendMessage("RunActions", actions);
            }

            if (changedProperties?.Contains(PropertyName.url) ?? true)
            {
                browser.SendMessage("LoadUrl", url);
            }
            if (changedProperties?.Contains(PropertyName.mipMaps) ?? true)
            {
                browser.SendMessage("SetMipMaps", mipMaps);
            }
            if (changedProperties?.Contains(PropertyName.pixelsPerUnit) ?? true)
            {
                browser.SendMessage("SetPixelsPerUnit", pixelsPerUnit);
            }
            if ((changedProperties?.Contains(PropertyName.pageWidth) ?? true) || (changedProperties?.Contains(PropertyName.pageHeight) ?? true))
            {
                RectTransform rt = browser.GetComponent(typeof(RectTransform)) as RectTransform;
                rt.sizeDelta = new Vector2(pageWidth, pageHeight);
            }
            SetLoadedIfNot();
        }

        internal override void DestroyStuff() { }
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

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterBrowser);


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
                    componentType = ComponentType.BanterBrowser,
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
                    componentType = ComponentType.BanterBrowser,
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
                    componentType = ComponentType.BanterBrowser,
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
                    componentType = ComponentType.BanterBrowser,
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
                    componentType = ComponentType.BanterBrowser,
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
                    componentType = ComponentType.BanterBrowser,
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