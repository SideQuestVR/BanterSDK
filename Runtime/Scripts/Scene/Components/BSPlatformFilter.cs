using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /*
    #### Banter Platform Filter
    Shows this GameObject (and everything under it) only where it is wanted: on PC or Android, in VR or not, or any
    combination. The two groups are combined with AND, and within a group any ticked option matches, so "Android + VR
    only" is Android and VR ticked, PC and Non-VR unticked. Everything ticked (the default) shows it everywhere.

    The platform half is also applied when a space bundle is built: an object excluded from a platform is left out of
    that platform's bundle entirely. The VR half can only be known on the player's device, so an object restricted by
    VR / Non-VR keeps the filter and hides itself when it starts. Hiding deactivates the GameObject; it never activates
    one it did not hide.

    **Properties**
    - `includeOnMobile` - Show on Android (Quest).
    - `includeOnDesktop` - Show on PC (Windows).
    - `includeInVR` - Show to players in a headset.
    - `includeInNonVR` - Show to players on a flat screen.

    **Code Example**
    ```js
        // Android headsets only: not on PC, not on a flat screen.
        const gameObject = new BS.GameObject("QuestVrOnly");
        await gameObject.AddComponent(new BS.PlatformFilter({ includeOnDesktop: false, includeInNonVR: false }));
    ```

    */
    [DefaultExecutionOrder(-1)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BSObjectId))]
    [AddComponentMenu("BS/Platform Filter")]
    [WatchComponent]
    public class BSPlatformFilter : BSComponentBase
    {
        [Tooltip("Show this GameObject and all its children on Android (Quest). Also decides whether it ships in mobile builds.")]
        [See(initial = "true")] public bool includeOnMobile = true;

        [Tooltip("Show this GameObject and all its children on PC (Windows). Also decides whether it ships in desktop builds.")]
        [See(initial = "true")] public bool includeOnDesktop = true;

        [Tooltip("Show this GameObject and all its children to players in a headset.")]
        [See(initial = "true")] public bool includeInVR = true;

        [Tooltip("Show this GameObject and all its children to players on a flat screen.")]
        [See(initial = "true")] public bool includeInNonVR = true;

        /// <summary>
        /// Set when this filter deactivated its own GameObject, so it only ever re-activates what it hid. An object
        /// that something else switched off stays off.
        /// </summary>
        bool hiddenByFilter;

        /// <summary>Does the platform half let the object through? <paramref name="isMobile"/> means Android.</summary>
        public static bool IsPlatformIncluded(bool isMobile, bool includeOnMobile, bool includeOnDesktop) =>
            isMobile ? includeOnMobile : includeOnDesktop;

        /// <summary>Does the VR half let the object through?</summary>
        public static bool IsModeIncluded(bool isVR, bool includeInVR, bool includeInNonVR) =>
            isVR ? includeInVR : includeInNonVR;

        /// <summary>Should an object with these settings show on this device? Both halves have to agree.</summary>
        public static bool ShouldShow(bool isAndroid, bool isVR,
                                      bool includeOnMobile, bool includeOnDesktop,
                                      bool includeInVR, bool includeInNonVR) =>
            IsPlatformIncluded(isAndroid, includeOnMobile, includeOnDesktop)
            && IsModeIncluded(isVR, includeInVR, includeInNonVR);

        /// <summary>
        /// True when the filter has something to decide on the device: the VR half restricts the object. A build strips
        /// filters that only restrict by platform, because the bundle for each platform already settled that.
        /// </summary>
        public bool NeedsRuntimeCheck => !(includeInVR && includeInNonVR);

        internal override void StartStuff()
        {
            Apply();
        }

        void OnEnable()
        {
            // Filters added before this was a component predate its RequireComponent, so a scene can hold one with no
            // BSObjectId. Registration needs one (without it the property sync re-enqueues for ever), and Awake has
            // already run and skipped registering, so add it and register here, before Start's Init. Play mode only:
            // in the editor the RequireComponent and the Builder (CustomSceneProcessor) take care of it.
            if (!Application.isPlaying || GetComponent<BSObjectId>() != null) return;
            gameObject.AddComponent<BSObjectId>();
            scene.RegisterComponentOnMainThread(gameObject, this);
        }

        internal override void UpdateStuff()
        {

        }

        internal override void DestroyStuff()
        {
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            // Before Start the scene's answers are not settled yet; Start applies the result once.
            if (alreadyStarted)
                Apply();
        }

        void Apply()
        {
            // The filter has nothing to load, but the space's load waits for every registered component to say so.
            // Here rather than only in StartStuff: a filter that hides its object during a page's AddComponent
            // (Init -> Deserialise) never gets a Start, and the space would then never finish loading.
            SetLoadedIfNot();

            var show = ShouldShow(scene.IsAndroid(), scene.IsVR(),
                                  includeOnMobile, includeOnDesktop, includeInVR, includeInNonVR);
            if (!show && gameObject.activeSelf)
            {
                hiddenByFilter = true;
                gameObject.SetActive(false);
            }
            else if (show && hiddenByFilter)
            {
                hiddenByFilter = false;
                gameObject.SetActive(true);
            }
        }
        // BS COMPILED CODE
        public System.Boolean IncludeOnMobile { get { return includeOnMobile; } set { includeOnMobile = value; UpdateCallback(new List<PropertyName> { PropertyName.includeOnMobile }); } }
        public System.Boolean IncludeOnDesktop { get { return includeOnDesktop; } set { includeOnDesktop = value; UpdateCallback(new List<PropertyName> { PropertyName.includeOnDesktop }); } }
        public System.Boolean IncludeInVR { get { return includeInVR; } set { includeInVR = value; UpdateCallback(new List<PropertyName> { PropertyName.includeInVR }); } }
        public System.Boolean IncludeInNonVR { get { return includeInNonVR; } set { includeInNonVR = value; UpdateCallback(new List<PropertyName> { PropertyName.includeInNonVR }); } }

        BSScene _scene;
        public BSScene scene
        {
            get
            {
                if (_scene == null)
                {
                    _scene = BSScene.Instance();
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.includeOnMobile, PropertyName.includeOnDesktop, PropertyName.includeInVR, PropertyName.includeInNonVR, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "PlatformFilter" + PropertyName.includeOnMobile + includeOnMobile + PropertyName.includeOnDesktop + includeOnDesktop + PropertyName.includeInVR + includeInVR + PropertyName.includeInNonVR + includeInNonVR;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(BSScene.UnityId(gameObject), BSScene.UnityId(this), ComponentType.PlatformFilter);


            oid = BSScene.UnityId(gameObject);
            cid = BSScene.UnityId(this);

            if (constructorProperties != null)
            {
                Deserialise(constructorProperties);
            }

            SyncProperties(true);

        }

        void Awake()
        {
            BSScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }

        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);

            DestroyStuff();
        }
        internal override object CallMethod(string methodName, List<object> parameters)
        {
            return null;
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BSBool)
                {
                    var valincludeOnMobile = (BSBool)values[i];
                    if (valincludeOnMobile.n == PropertyName.includeOnMobile)
                    {
                        includeOnMobile = valincludeOnMobile.x;
                        changedProperties.Add(PropertyName.includeOnMobile);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valincludeOnDesktop = (BSBool)values[i];
                    if (valincludeOnDesktop.n == PropertyName.includeOnDesktop)
                    {
                        includeOnDesktop = valincludeOnDesktop.x;
                        changedProperties.Add(PropertyName.includeOnDesktop);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valincludeInVR = (BSBool)values[i];
                    if (valincludeInVR.n == PropertyName.includeInVR)
                    {
                        includeInVR = valincludeInVR.x;
                        changedProperties.Add(PropertyName.includeInVR);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valincludeInNonVR = (BSBool)values[i];
                    if (valincludeInNonVR.n == PropertyName.includeInNonVR)
                    {
                        includeInNonVR = valincludeInNonVR.x;
                        changedProperties.Add(PropertyName.includeInNonVR);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BSComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.includeOnMobile,
                    type = PropertyType.Bool,
                    value = includeOnMobile,
                    componentType = ComponentType.PlatformFilter,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.includeOnDesktop,
                    type = PropertyType.Bool,
                    value = includeOnDesktop,
                    componentType = ComponentType.PlatformFilter,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.includeInVR,
                    type = PropertyType.Bool,
                    value = includeInVR,
                    componentType = ComponentType.PlatformFilter,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.includeInNonVR,
                    type = PropertyType.Bool,
                    value = includeInNonVR,
                    componentType = ComponentType.PlatformFilter,
                    oid = oid,
                    cid = cid
                });
            }
            scene.SetFromUnityProperties(updates, callback);
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BS COMPILED CODE
    }
}
