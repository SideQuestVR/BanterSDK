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
    [WatchComponent]
    [RequireComponent(typeof(BanterObjectId))]

    public class BanterText : BanterComponentBase
    {
        TextMeshPro _text;
        [See(initial = "")] public string text;
        [See(initial = "1,1,1,1")] public Vector4 color = new Vector4(1, 1, 1, 1);
        [See(initial = "0")] public HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left;
        [See(initial = "0")] public VerticalAlignment verticalAlignment = VerticalAlignment.Top;
        [See(initial = "2")] public float fontSize = 2;
        [See(initial = "true")] public bool richText = true;
        [See(initial = "true")] public bool enableWordWrapping = true;
        [See(initial = "20,5")] public Vector2 rectTransformSizeDelta = new Vector2(20, 5);


        internal override void StartStuff()
        {
            SetupText();
        }

        void SetupText()
        {
            if (_text == null)
            {
                _text = gameObject.GetComponent<TextMeshPro>();
                if (_text == null)
                {
                    _text = gameObject.AddComponent<TextMeshPro>();
                }
            }
            _text.text = text;
            _text.color = new Color(color.x, color.y, color.z, color.w);
            _text.fontSize = fontSize;
            _text.richText = richText;
            switch (horizontalAlignment)
            {
                case HorizontalAlignment.Left:
                    _text.alignment = TextAlignmentOptions.Left;
                    break;
                case HorizontalAlignment.Right:
                    _text.alignment = TextAlignmentOptions.Right;
                    break;
                case HorizontalAlignment.Center:
                    _text.alignment = TextAlignmentOptions.Center;
                    break;
            }
            switch (verticalAlignment)
            {
                case VerticalAlignment.Top:
                    _text.verticalAlignment = VerticalAlignmentOptions.Top;
                    break;
                case VerticalAlignment.Bottom:
                    _text.verticalAlignment = VerticalAlignmentOptions.Bottom;
                    break;
                case VerticalAlignment.Center:
                    _text.verticalAlignment = VerticalAlignmentOptions.Middle;
                    break;
            }
            _text.enableWordWrapping = enableWordWrapping;
            var rect = gameObject.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.sizeDelta = rectTransformSizeDelta;
            }
            SetLoadedIfNot();
        }
        internal override void DestroyStuff()
        {
            if (_text != null)
            {
                Destroy(_text);
            }
        }
        void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupText();
        }
        // BANTER COMPILED CODE 
        BanterScene scene;
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
            scene = BanterScene.Instance();
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