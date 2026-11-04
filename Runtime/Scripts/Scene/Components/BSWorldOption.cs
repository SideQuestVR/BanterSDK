using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;

namespace BS
{
    /*
    #### Banter World Option
    Adds a button for this world to the player's radial menu, there while this component is enabled. A world has two
    slots; if two options want one slot, the last one enabled shows. A press happens only for the player who pressed it.

    **Properties**
    - `slot` - Which world button this is: 1 or 2.
    - `label` - Name shown on the button. Up to 32 characters, plain text.
    - `isToggle` - Stays on or off between presses, instead of a single press.
    - `isOn` - The toggle's state. A press flips it.

    **Code Example**
    ```js
        const gameObject = new BS.GameObject("LightsOption");
        await gameObject.AddComponent(new BS.WorldOption({ slot: 1, label: "Lights", isToggle: true }));
        gameObject.On("world-option", e => console.log("Lights on:", e.detail.isOn));
    ```

    */
    [DefaultExecutionOrder(-1)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BSObjectId))]
    [AddComponentMenu("BS/World Option")]
    [WatchComponent]
    public class BSWorldOption : BSComponentBase
    {
        public const int SlotCount = 2;

        static readonly BSWorldOption[] Slots = new BSWorldOption[SlotCount];

        /// <summary>Raised when an option appears, goes, or changes how it looks.</summary>
        public static event Action Changed;

        /// <summary>The option showing in <paramref name="slot"/> (1 or 2), or null.</summary>
        public static BSWorldOption Get(int slot) => Slots[slot - 1];

        [Tooltip("Which world button this is: 1 or 2.")]
        [Range(1, SlotCount)]
        [See(initial = "1")][SerializeField] internal int slot = 1;
        [Tooltip("Name shown on the button. Up to 32 characters, plain text.")]
        [See(initial = "")][SerializeField] internal string label = "";
        [Tooltip("Icon on the button. Empty uses the default world icon. Page scripts can't set it.")]
        [SerializeField] Sprite icon;
        [Tooltip("Stays on or off between presses, instead of a single press.")]
        [See(initial = "false")][SerializeField] internal bool isToggle = false;
        [Tooltip("The toggle's state. A press flips it.")]
        [See(initial = "false")][SerializeField] internal bool isOn = false;
        [Tooltip("Fires on a press, with the toggle's state after it. Wire it up here to skip graphs and scripts.")]
        public UnityEvent<bool> onPress = new UnityEvent<bool>();

        /// <summary>The icon on the button, or null for the default world icon.</summary>
        public Sprite Icon => icon;

        /// <summary>Press the button: flip a toggle, then tell the Inspector event, graphs and page script.</summary>
        public void Press()
        {
            if (isToggle) isOn = !isOn;
            onPress.Invoke(isOn);
            EventBus.Trigger("OnWorldOption", new CustomEventArgs(BSScene.UnityId(gameObject).ToString(), new object[] { isOn }));
            if (scene.link != null) scene.link.OnWorldOption(gameObject, isOn);
        }

        internal void Claim()
        {
            int held = Array.IndexOf(Slots, this);
            if (held >= 0) Slots[held] = null;
            Slots[Mathf.Clamp(slot, 1, SlotCount) - 1] = this;
            Changed?.Invoke();
        }

        // Only ever clears itself, so an old world's late cleanup can't take the next world's button.
        internal void Release()
        {
            int held = Array.IndexOf(Slots, this);
            if (held < 0) return;
            Slots[held] = null;
            Changed?.Invoke();
        }

        // Before Init a page script's slot and label haven't arrived, so the first claim waits for Start.
        void OnEnable()
        {
            if (alreadyStarted) Claim();
        }

        void OnDisable() => Release();

        void ClaimAfterLoad()
        {
            if (isActiveAndEnabled) Claim();
        }

        internal override void StartStuff()
        {
            scene.events.OnSceneReset.AddListener(Release);
            scene.events.OnLoad.AddListener(ClaimAfterLoad);
            Claim();
            SetLoadedIfNot();
        }

        internal override void UpdateStuff()
        {

        }

        internal override void DestroyStuff()
        {
            scene.events.OnSceneReset.RemoveListener(Release);
            scene.events.OnLoad.RemoveListener(ClaimAfterLoad);
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            // The radial polls isOn, so a toggle flip alone needs no rebuild.
            if (changedProperties.Count == 1 && changedProperties[0] == PropertyName.isOn) return;
            if (isActiveAndEnabled) Claim();
        }
        // BS COMPILED CODE 
        public System.Int32 Slot { get { return slot; } set { slot = value; UpdateCallback(new List<PropertyName> { PropertyName.slot }); } }
        public System.String Label { get { return label; } set { label = value; UpdateCallback(new List<PropertyName> { PropertyName.label }); } }
        public System.Boolean IsToggle { get { return isToggle; } set { isToggle = value; UpdateCallback(new List<PropertyName> { PropertyName.isToggle }); } }
        public System.Boolean IsOn { get { return isOn; } set { isOn = value; UpdateCallback(new List<PropertyName> { PropertyName.isOn }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.slot, PropertyName.label, PropertyName.isToggle, PropertyName.isOn, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "WorldOption" +  PropertyName.slot + slot + PropertyName.label + label + PropertyName.isToggle + isToggle + PropertyName.isOn + isOn;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(BSScene.UnityId(gameObject), BSScene.UnityId(this), ComponentType.WorldOption);


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
                if (values[i] is BSInt)
                {
                    var valslot = (BSInt)values[i];
                    if (valslot.n == PropertyName.slot)
                    {
                        slot = valslot.x;
                        changedProperties.Add(PropertyName.slot);
                    }
                }
                if (values[i] is BSString)
                {
                    var vallabel = (BSString)values[i];
                    if (vallabel.n == PropertyName.label)
                    {
                        label = vallabel.x;
                        changedProperties.Add(PropertyName.label);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valisToggle = (BSBool)values[i];
                    if (valisToggle.n == PropertyName.isToggle)
                    {
                        isToggle = valisToggle.x;
                        changedProperties.Add(PropertyName.isToggle);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valisOn = (BSBool)values[i];
                    if (valisOn.n == PropertyName.isOn)
                    {
                        isOn = valisOn.x;
                        changedProperties.Add(PropertyName.isOn);
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
                    name = PropertyName.slot,
                    type = PropertyType.Int,
                    value = slot,
                    componentType = ComponentType.WorldOption,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.label,
                    type = PropertyType.String,
                    value = label,
                    componentType = ComponentType.WorldOption,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.isToggle,
                    type = PropertyType.Bool,
                    value = isToggle,
                    componentType = ComponentType.WorldOption,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.isOn,
                    type = PropertyType.Bool,
                    value = isOn,
                    componentType = ComponentType.WorldOption,
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