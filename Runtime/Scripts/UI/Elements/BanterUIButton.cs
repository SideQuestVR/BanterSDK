using System;
using UnityEngine;
using UnityEngine.UIElements;
using Banter.UICodeGen;

namespace Banter.UI.Elements
{
    /// <summary>
    /// Example UI Button element with code generation attributes.
    /// This demonstrates how to create a UI element that can be controlled from TypeScript.
    /// </summary>
    [UIElement(typeof(Button), "UIButton")]
    public partial class BanterUIButton : Button
    {
        [UIProperty(propertyName: "text")]
        public string Text
        {
            get => text;
            set => text = value;
        }
        
        [UIProperty(propertyName: "enabled")]
        public bool IsEnabled
        {
            get => enabledSelf;
            set => SetEnabled(value);
        }
        
        [UIProperty(propertyName: "tooltip")]
        public string TooltipText
        {
            get => tooltip;
            set => tooltip = value;
        }
        
        [UIStyle(styleName: "width")]
        public StyleLength Width
        {
            get => style.width;
            set => style.width = value;
        }
        
        [UIStyle(styleName: "height")]
        public StyleLength Height
        {
            get => style.height;
            set => style.height = value;
        }
        
        [UIStyle(styleName: "backgroundColor")]
        public StyleColor BackgroundColor
        {
            get => style.backgroundColor;
            set => style.backgroundColor = value;
        }
        
        [UIStyle(styleName: "color")]
        public StyleColor TextColor
        {
            get => style.color;
            set => style.color = value;
        }
        
        [UIStyle(styleName: "fontSize")]
        public StyleLength FontSize
        {
            get => style.fontSize;
            set => style.fontSize = value;
        }
        
        [UIEvent(eventName: "click", bubbles: true)]
        public event Action OnClick;
        
        [UIEvent(eventName: "hover", bubbles: true)]
        public event Action OnHover;
        
        [UIEvent(eventName: "focus")]
        public event Action OnFocusReceived;
        
        [UIEvent(eventName: "blur")]
        public event Action OnFocusLost;
        
        [UIMethod(methodName: "focus")]
        public new void Focus()
        {
            base.Focus();
        }
        
        [UIMethod(methodName: "blur")]
        public new void Blur()
        {
            base.Blur();
        }
        
        [UIMethod(methodName: "click")]
        public void SimulateClick()
        {
            // Simulate a click event
            using (var evt = ClickEvent.GetPooled())
            {
                evt.target = this;
                SendEvent(evt);
            }
        }
        
        [UIMethod(methodName: "setVariant")]
        public void SetVariant(string variant)
        {
            // Apply button variant styling
            RemoveFromClassList("button--primary");
            RemoveFromClassList("button--secondary");
            RemoveFromClassList("button--danger");
            RemoveFromClassList("button--success");
            
            if (!string.IsNullOrEmpty(variant))
            {
                AddToClassList($"button--{variant}");
            }
        }
        
        public BanterUIButton() : base()
        {
            // Set default styles
            AddToClassList("banter-button");
            
            // Register event callbacks
            this.clicked += () => OnClick?.Invoke();
            
            RegisterCallback<MouseEnterEvent>(evt => OnHover?.Invoke());
            RegisterCallback<FocusEvent>(evt => OnFocusReceived?.Invoke());
            RegisterCallback<BlurEvent>(evt => OnFocusLost?.Invoke());
        }
        
        public BanterUIButton(string text) : this()
        {
            this.text = text;
        }
        
        public BanterUIButton(System.Action clickEvent) : this()
        {
            if (clickEvent != null)
            {
                clicked += clickEvent;
            }
        }
    }
    
    // /// <summary>
    // /// Example UI Panel element for containing other elements.
    // /// </summary>
    // [UIElement(typeof(VisualElement), "UIPanel")]
    // public class BanterUIPanel : VisualElement
    // {
        
    //     [UIStyle(styleName: "width")]
    //     public StyleLength Width
    //     {
    //         get => style.width;
    //         set => style.width = value;
    //     }
        
    //     [UIStyle(styleName: "height")]
    //     public StyleLength Height
    //     {
    //         get => style.height;
    //         set => style.height = value;
    //     }
        
    //     [UIStyle(styleName: "backgroundColor")]
    //     public StyleColor BackgroundColor
    //     {
    //         get => style.backgroundColor;
    //         set => style.backgroundColor = value;
    //     }
        
    //     [UIStyle(styleName: "padding")]
    //     public StyleLength Padding
    //     {
    //         get => style.paddingTop;
    //         set
    //         {
    //             style.paddingTop = value;
    //             style.paddingRight = value;
    //             style.paddingBottom = value;
    //             style.paddingLeft = value;
    //         }
    //     }
        
    //     [UIStyle(styleName: "borderRadius")]
    //     public StyleLength BorderRadius
    //     {
    //         get => style.borderTopLeftRadius;
    //         set
    //         {
    //             style.borderTopLeftRadius = value;
    //             style.borderTopRightRadius = value;
    //             style.borderBottomLeftRadius = value;
    //             style.borderBottomRightRadius = value;
    //         }
    //     }
        
    //     [UIMethod(methodName: "clear")]
    //     public new void Clear()
    //     {
    //         base.Clear();
    //     }
        
    //     // [UIMethod(methodName: "scrollTo")]
    //     // public void ScrollTo(float x, float y)
    //     // {
    //     //     if (this is ScrollView scrollView)
    //     //     {
    //     //         scrollView.scrollOffset = new Vector2(x, y);
    //     //     }
    //     // }
        
    //     public BanterUIPanel()
    //     {
    //         AddToClassList("banter-panel");
            
    //         // Set default styles
    //         style.flexDirection = FlexDirection.Column;
    //         style.flexGrow = 1;
    //     }
    // }
    
    /// <summary>
    /// Example UI Label element for displaying text.
    /// </summary>
    [UIElement(typeof(Label), "UILabel")]
    public partial class BanterUILabel : Label
    {
        [UIProperty(propertyName: "text")]
        public string Text
        {
            get => text;
            set => text = value;
        }
        
        [UIStyle(styleName: "color")]
        public StyleColor TextColor
        {
            get => style.color;
            set => style.color = value;
        }
        
        [UIStyle(styleName: "fontSize")]
        public StyleLength FontSize
        {
            get => style.fontSize;
            set => style.fontSize = value;
        }
        
        [UIStyle(styleName: "textAlign")]
        public StyleEnum<TextAnchor> TextAlign
        {
            get => style.unityTextAlign;
            set => style.unityTextAlign = value;
        }
        
        [UIMethod(methodName: "setText")]
        public void SetText(string newText)
        {
            Debug.Log("[BanterUILabel] SetText called with: " + newText);
            text = newText;
        }
        
        public BanterUILabel() : base()
        {
            AddToClassList("banter-label");
        }
        
        public BanterUILabel(string text) : this()
        {
            this.text = text;
        }
    }
}