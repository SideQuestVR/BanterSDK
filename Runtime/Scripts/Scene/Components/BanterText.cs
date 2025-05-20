using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using PropertyName = Banter.SDK.PropertyName;

namespace Banter.SDK
{
    /* 
    #### Banter Text
    This component will add a text component to the object and set the text, color, alignment, font size, rich text, word wrapping and size delta of the text.

    **Properties**
     - `text` - The text to display.
     - `color` - The color of the text.
     - `horizontalAlignment` - The horizontal alignment of the text.
     - `verticalAlignment` - The vertical alignment of the text.
     - `fontSize` - The font size of the text.
     - `richText` - Whether the text is rich text.
     - `enableWordWrapping` - Whether to enable word wrapping.
     - `rectTransformSizeDelta` - The size delta of the text.

    **Code Example**
    ```js
        const text = "Hello World";
        const color = new BS.Vector4(1,1,1,1);
        const horizontalAlignment = BS.HorizontalAlignment.Center;
        const verticalAlignment = BS.VerticalAlignment.Middle;
        const fontSize = 20;
        const richText = true;
        const enableWordWrapping = true;
        const rectTransformSizeDelta = new BS.Vector2(20,5);
        const gameObject = new BS.GameObject("MyText");
        const text = await gameObject.AddComponent(new BS.BanterText(text, color, horizontalAlignment, verticalAlignment, fontSize, richText, enableWordWrapping, rectTransformSizeDelta));
    ```
    */
    [DefaultExecutionOrder(-1)]
    [WatchComponent]
    [RequireComponent(typeof(BanterObjectId))]

    public class BanterText : BanterComponentBase
    {
        TextMeshPro tmpComponent;
        [Tooltip("The text content to display.")]
        [See(initial = "")][SerializeField] internal string text;

        [Tooltip("The color of the text in RGBA format.")]
        [See(initial = "1,1,1,1")][SerializeField] internal Vector4 color = new Vector4(1, 1, 1, 1);

        [Tooltip("The horizontal alignment of the text.")]
        [See(initial = "0")][SerializeField] internal HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left;

        [Tooltip("The vertical alignment of the text.")]
        [See(initial = "0")][SerializeField] internal VerticalAlignment verticalAlignment = VerticalAlignment.Top;

        [Tooltip("The font size of the text.")]
        [See(initial = "2")][SerializeField] internal float fontSize = 2;

        [Tooltip("Enable or disable rich text formatting.")]
        [See(initial = "true")][SerializeField] internal bool richText = true;

        [Tooltip("Enable or disable word wrapping.")]
        [See(initial = "true")][SerializeField] internal bool enableWordWrapping = true;

        [Tooltip("The size delta of the text RectTransform.")]
        [See(initial = "20,5")][SerializeField] internal Vector2 rectTransformSizeDelta = new Vector2(20, 5);



        internal override void StartStuff()
        {
            SetupText();
        }

        void SetupText()
        {
            if (tmpComponent == null)
            {
                tmpComponent = gameObject.GetComponent<TextMeshPro>();
                if (tmpComponent == null)
                {
                    tmpComponent = gameObject.AddComponent<TextMeshPro>();
                }
            }
            tmpComponent.text = text;
            tmpComponent.color = new Color(color.x, color.y, color.z, color.w);
            tmpComponent.fontSize = fontSize;
            tmpComponent.richText = richText;
            switch (horizontalAlignment)
            {
                case HorizontalAlignment.Left:
                    tmpComponent.alignment = TextAlignmentOptions.Left;
                    break;
                case HorizontalAlignment.Right:
                    tmpComponent.alignment = TextAlignmentOptions.Right;
                    break;
                case HorizontalAlignment.Center:
                    tmpComponent.alignment = TextAlignmentOptions.Center;
                    break;
            }
            switch (verticalAlignment)
            {
                case VerticalAlignment.Top:
                    tmpComponent.verticalAlignment = VerticalAlignmentOptions.Top;
                    break;
                case VerticalAlignment.Bottom:
                    tmpComponent.verticalAlignment = VerticalAlignmentOptions.Bottom;
                    break;
                case VerticalAlignment.Center:
                    tmpComponent.verticalAlignment = VerticalAlignmentOptions.Middle;
                    break;
            }
            tmpComponent.enableWordWrapping = enableWordWrapping;
            var rect = gameObject.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.sizeDelta = rectTransformSizeDelta;
            }
            SetLoadedIfNot();
        }
        internal override void DestroyStuff()
        {
            if (tmpComponent != null)
            {
                Destroy(tmpComponent);
            }
        }
        void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupText();
        }
        // BANTER COMPILED CODE 
        public System.String Text { get { return text; } set { text = value; UpdateCallback(new List<PropertyName> { PropertyName.text }); } }
        public UnityEngine.Vector4 Color { get { return color; } set { color = value; UpdateCallback(new List<PropertyName> { PropertyName.color }); } }
        public HorizontalAlignment HorizontalAlignment { get { return horizontalAlignment; } set { horizontalAlignment = value; UpdateCallback(new List<PropertyName> { PropertyName.horizontalAlignment }); } }
        public VerticalAlignment VerticalAlignment { get { return verticalAlignment; } set { verticalAlignment = value; UpdateCallback(new List<PropertyName> { PropertyName.verticalAlignment }); } }
        public System.Single FontSize { get { return fontSize; } set { fontSize = value; UpdateCallback(new List<PropertyName> { PropertyName.fontSize }); } }
        public System.Boolean RichText { get { return richText; } set { richText = value; UpdateCallback(new List<PropertyName> { PropertyName.richText }); } }
        public System.Boolean EnableWordWrapping { get { return enableWordWrapping; } set { enableWordWrapping = value; UpdateCallback(new List<PropertyName> { PropertyName.enableWordWrapping }); } }
        public UnityEngine.Vector2 RectTransformSizeDelta { get { return rectTransformSizeDelta; } set { rectTransformSizeDelta = value; UpdateCallback(new List<PropertyName> { PropertyName.rectTransformSizeDelta }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.text, PropertyName.color, PropertyName.horizontalAlignment, PropertyName.verticalAlignment, PropertyName.fontSize, PropertyName.richText, PropertyName.enableWordWrapping, PropertyName.rectTransformSizeDelta, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterText);


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

        internal override object CallMethod(string methodName, List<object> parameters)
        {
            return null;
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BanterString)
                {
                    var valtext = (BanterString)values[i];
                    if (valtext.n == PropertyName.text)
                    {
                        text = valtext.x;
                        changedProperties.Add(PropertyName.text);
                    }
                }
                if (values[i] is BanterVector4)
                {
                    var valcolor = (BanterVector4)values[i];
                    if (valcolor.n == PropertyName.color)
                    {
                        color = new Vector4(valcolor.x, valcolor.y, valcolor.z, valcolor.w);
                        changedProperties.Add(PropertyName.color);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valhorizontalAlignment = (BanterInt)values[i];
                    if (valhorizontalAlignment.n == PropertyName.horizontalAlignment)
                    {
                        horizontalAlignment = (HorizontalAlignment)valhorizontalAlignment.x;
                        changedProperties.Add(PropertyName.horizontalAlignment);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valverticalAlignment = (BanterInt)values[i];
                    if (valverticalAlignment.n == PropertyName.verticalAlignment)
                    {
                        verticalAlignment = (VerticalAlignment)valverticalAlignment.x;
                        changedProperties.Add(PropertyName.verticalAlignment);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valfontSize = (BanterFloat)values[i];
                    if (valfontSize.n == PropertyName.fontSize)
                    {
                        fontSize = valfontSize.x;
                        changedProperties.Add(PropertyName.fontSize);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valrichText = (BanterBool)values[i];
                    if (valrichText.n == PropertyName.richText)
                    {
                        richText = valrichText.x;
                        changedProperties.Add(PropertyName.richText);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valenableWordWrapping = (BanterBool)values[i];
                    if (valenableWordWrapping.n == PropertyName.enableWordWrapping)
                    {
                        enableWordWrapping = valenableWordWrapping.x;
                        changedProperties.Add(PropertyName.enableWordWrapping);
                    }
                }
                if (values[i] is BanterVector2)
                {
                    var valrectTransformSizeDelta = (BanterVector2)values[i];
                    if (valrectTransformSizeDelta.n == PropertyName.rectTransformSizeDelta)
                    {
                        rectTransformSizeDelta = new Vector2(valrectTransformSizeDelta.x, valrectTransformSizeDelta.y);
                        changedProperties.Add(PropertyName.rectTransformSizeDelta);
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
                    name = PropertyName.text,
                    type = PropertyType.String,
                    value = text,
                    componentType = ComponentType.BanterText,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.color,
                    type = PropertyType.Vector4,
                    value = color,
                    componentType = ComponentType.BanterText,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.horizontalAlignment,
                    type = PropertyType.Int,
                    value = horizontalAlignment,
                    componentType = ComponentType.BanterText,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.verticalAlignment,
                    type = PropertyType.Int,
                    value = verticalAlignment,
                    componentType = ComponentType.BanterText,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.fontSize,
                    type = PropertyType.Float,
                    value = fontSize,
                    componentType = ComponentType.BanterText,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.richText,
                    type = PropertyType.Bool,
                    value = richText,
                    componentType = ComponentType.BanterText,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.enableWordWrapping,
                    type = PropertyType.Bool,
                    value = enableWordWrapping,
                    componentType = ComponentType.BanterText,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.rectTransformSizeDelta,
                    type = PropertyType.Vector2,
                    value = rectTransformSizeDelta,
                    componentType = ComponentType.BanterText,
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