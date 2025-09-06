using UnityEngine;
using UnityEngine.UIElements;

namespace Banter.UI.Bridge
{
    /// <summary>
    /// Enumeration of all supported USS style properties for Unity UI Toolkit.
    /// This enum matches the TypeScript UIStyleProperty enum in Shared/ui-style-properties.ts
    /// </summary>
    public enum UIStyleProperty
    {
        // Layout Properties (Flexbox)
        AlignContent = 0,
        AlignItems = 1,
        AlignSelf = 2,
        FlexBasis = 3,
        FlexDirection = 4,
        FlexGrow = 5,
        FlexShrink = 6,
        FlexWrap = 7,
        JustifyContent = 8,
        
        // Size Properties
        Width = 10,
        Height = 11,
        MinWidth = 12,
        MinHeight = 13,
        MaxWidth = 14,
        MaxHeight = 15,
        
        // Position Properties
        Position = 20,
        Left = 21,
        Top = 22,
        Right = 23,
        Bottom = 24,
        
        // Margin Properties
        Margin = 30,
        MarginTop = 31,
        MarginRight = 32,
        MarginBottom = 33,
        MarginLeft = 34,
        
        // Padding Properties
        Padding = 40,
        PaddingTop = 41,
        PaddingRight = 42,
        PaddingBottom = 43,
        PaddingLeft = 44,
        
        // Border Properties
        BorderWidth = 50,
        BorderTopWidth = 51,
        BorderRightWidth = 52,
        BorderBottomWidth = 53,
        BorderLeftWidth = 54,
        BorderRadius = 55,
        BorderTopLeftRadius = 56,
        BorderTopRightRadius = 57,
        BorderBottomLeftRadius = 58,
        BorderBottomRightRadius = 59,
        
        // Border Color Properties
        BorderColor = 60,
        BorderTopColor = 61,
        BorderRightColor = 62,
        BorderBottomColor = 63,
        BorderLeftColor = 64,
        
        // Background Properties
        BackgroundColor = 70,
        BackgroundImage = 71,
        BackgroundSize = 72,
        BackgroundRepeat = 73,
        BackgroundPosition = 74,
        
        // Color Properties
        Color = 80,
        Opacity = 81,
        
        // Text Properties
        FontSize = 90,
        FontStyle = 91,
        FontWeight = 92,
        LineHeight = 93,
        TextAlign = 94,
        TextOverflow = 95,
        TextShadow = 96,
        WhiteSpace = 97,
        WordWrap = 98,
        LetterSpacing = 99,
        
        // Unity-specific Text Properties
        UnityFontDefinition = 100,
        UnityFont = 101,
        UnityTextAlign = 102,
        UnityTextOutlineColor = 103,
        UnityTextOutlineWidth = 104,
        
        // Display Properties
        Display = 110,
        Visibility = 111,
        Overflow = 112,
        
        // Transform Properties
        Rotate = 120,
        Scale = 121,
        Translate = 122,
        TransformOrigin = 123,
        
        // Cursor and Interaction
        Cursor = 130,
        
        // Unity-specific Properties
        UnitySliceLeft = 140,
        UnitySliceRight = 141,
        UnitySliceTop = 142,
        UnitySliceBottom = 143,
        UnityBackgroundScaleMode = 144,
        UnityBackgroundImageTintColor = 145,
        
        // Transition Properties
        TransitionProperty = 150,
        TransitionDuration = 151,
        TransitionTimingFunction = 152,
        TransitionDelay = 153,
    }
    
    /// <summary>
    /// Utility class for converting between UIStyleProperty enum and USS property names
    /// </summary>
    public static class UIStylePropertyHelper
    {
        /// <summary>
        /// Converts UIStyleProperty enum to USS property name
        /// </summary>
        public static string ToUSSName(this UIStyleProperty property)
        {
            return property switch
            {
                // Layout Properties (Flexbox)
                UIStyleProperty.AlignContent => "align-content",
                UIStyleProperty.AlignItems => "align-items",
                UIStyleProperty.AlignSelf => "align-self",
                UIStyleProperty.FlexBasis => "flex-basis",
                UIStyleProperty.FlexDirection => "flex-direction",
                UIStyleProperty.FlexGrow => "flex-grow",
                UIStyleProperty.FlexShrink => "flex-shrink",
                UIStyleProperty.FlexWrap => "flex-wrap",
                UIStyleProperty.JustifyContent => "justify-content",
                
                // Size Properties
                UIStyleProperty.Width => "width",
                UIStyleProperty.Height => "height",
                UIStyleProperty.MinWidth => "min-width",
                UIStyleProperty.MinHeight => "min-height",
                UIStyleProperty.MaxWidth => "max-width",
                UIStyleProperty.MaxHeight => "max-height",
                
                // Position Properties
                UIStyleProperty.Position => "position",
                UIStyleProperty.Left => "left",
                UIStyleProperty.Top => "top",
                UIStyleProperty.Right => "right",
                UIStyleProperty.Bottom => "bottom",
                
                // Margin Properties
                UIStyleProperty.Margin => "margin",
                UIStyleProperty.MarginTop => "margin-top",
                UIStyleProperty.MarginRight => "margin-right",
                UIStyleProperty.MarginBottom => "margin-bottom",
                UIStyleProperty.MarginLeft => "margin-left",
                
                // Padding Properties
                UIStyleProperty.Padding => "padding",
                UIStyleProperty.PaddingTop => "padding-top",
                UIStyleProperty.PaddingRight => "padding-right",
                UIStyleProperty.PaddingBottom => "padding-bottom",
                UIStyleProperty.PaddingLeft => "padding-left",
                
                // Border Properties
                UIStyleProperty.BorderWidth => "border-width",
                UIStyleProperty.BorderTopWidth => "border-top-width",
                UIStyleProperty.BorderRightWidth => "border-right-width",
                UIStyleProperty.BorderBottomWidth => "border-bottom-width",
                UIStyleProperty.BorderLeftWidth => "border-left-width",
                UIStyleProperty.BorderRadius => "border-radius",
                UIStyleProperty.BorderTopLeftRadius => "border-top-left-radius",
                UIStyleProperty.BorderTopRightRadius => "border-top-right-radius",
                UIStyleProperty.BorderBottomLeftRadius => "border-bottom-left-radius",
                UIStyleProperty.BorderBottomRightRadius => "border-bottom-right-radius",
                
                // Border Color Properties
                UIStyleProperty.BorderColor => "border-color",
                UIStyleProperty.BorderTopColor => "border-top-color",
                UIStyleProperty.BorderRightColor => "border-right-color",
                UIStyleProperty.BorderBottomColor => "border-bottom-color",
                UIStyleProperty.BorderLeftColor => "border-left-color",
                
                // Background Properties
                UIStyleProperty.BackgroundColor => "background-color",
                UIStyleProperty.BackgroundImage => "background-image",
                UIStyleProperty.BackgroundSize => "background-size",
                UIStyleProperty.BackgroundRepeat => "background-repeat",
                UIStyleProperty.BackgroundPosition => "background-position",
                
                // Color Properties
                UIStyleProperty.Color => "color",
                UIStyleProperty.Opacity => "opacity",
                
                // Text Properties
                UIStyleProperty.FontSize => "font-size",
                UIStyleProperty.FontStyle => "font-style",
                UIStyleProperty.FontWeight => "font-weight",
                UIStyleProperty.LineHeight => "line-height",
                UIStyleProperty.TextAlign => "text-align",
                UIStyleProperty.TextOverflow => "text-overflow",
                UIStyleProperty.TextShadow => "text-shadow",
                UIStyleProperty.WhiteSpace => "white-space",
                UIStyleProperty.WordWrap => "word-wrap",
                UIStyleProperty.LetterSpacing => "letter-spacing",
                
                // Unity-specific Text Properties
                UIStyleProperty.UnityFontDefinition => "-unity-font-definition",
                UIStyleProperty.UnityFont => "-unity-font",
                UIStyleProperty.UnityTextAlign => "-unity-text-align",
                UIStyleProperty.UnityTextOutlineColor => "-unity-text-outline-color",
                UIStyleProperty.UnityTextOutlineWidth => "-unity-text-outline-width",
                
                // Display Properties
                UIStyleProperty.Display => "display",
                UIStyleProperty.Visibility => "visibility",
                UIStyleProperty.Overflow => "overflow",
                
                // Transform Properties
                UIStyleProperty.Rotate => "rotate",
                UIStyleProperty.Scale => "scale",
                UIStyleProperty.Translate => "translate",
                UIStyleProperty.TransformOrigin => "transform-origin",
                
                // Cursor and Interaction
                UIStyleProperty.Cursor => "cursor",
                
                // Unity-specific Properties
                UIStyleProperty.UnitySliceLeft => "-unity-slice-left",
                UIStyleProperty.UnitySliceRight => "-unity-slice-right",
                UIStyleProperty.UnitySliceTop => "-unity-slice-top",
                UIStyleProperty.UnitySliceBottom => "-unity-slice-bottom",
                UIStyleProperty.UnityBackgroundScaleMode => "-unity-background-scale-mode",
                UIStyleProperty.UnityBackgroundImageTintColor => "-unity-background-image-tint-color",
                
                // Transition Properties
                UIStyleProperty.TransitionProperty => "transition-property",
                UIStyleProperty.TransitionDuration => "transition-duration",
                UIStyleProperty.TransitionTimingFunction => "transition-timing-function",
                UIStyleProperty.TransitionDelay => "transition-delay",
                
                _ => "unknown"
            };
        }
        
        /// <summary>
        /// Converts USS property name to UIStyleProperty enum
        /// </summary>
        public static UIStyleProperty FromUSSName(string ussName)
        {
            return ussName.ToLower() switch
            {
                // Layout Properties (Flexbox)
                "align-content" => UIStyleProperty.AlignContent,
                "align-items" => UIStyleProperty.AlignItems,
                "align-self" => UIStyleProperty.AlignSelf,
                "flex-basis" => UIStyleProperty.FlexBasis,
                "flex-direction" => UIStyleProperty.FlexDirection,
                "flex-grow" => UIStyleProperty.FlexGrow,
                "flex-shrink" => UIStyleProperty.FlexShrink,
                "flex-wrap" => UIStyleProperty.FlexWrap,
                "justify-content" => UIStyleProperty.JustifyContent,
                
                // Size Properties
                "width" => UIStyleProperty.Width,
                "height" => UIStyleProperty.Height,
                "min-width" => UIStyleProperty.MinWidth,
                "min-height" => UIStyleProperty.MinHeight,
                "max-width" => UIStyleProperty.MaxWidth,
                "max-height" => UIStyleProperty.MaxHeight,
                
                // Position Properties
                "position" => UIStyleProperty.Position,
                "left" => UIStyleProperty.Left,
                "top" => UIStyleProperty.Top,
                "right" => UIStyleProperty.Right,
                "bottom" => UIStyleProperty.Bottom,
                
                // Margin Properties
                "margin" => UIStyleProperty.Margin,
                "margin-top" => UIStyleProperty.MarginTop,
                "margin-right" => UIStyleProperty.MarginRight,
                "margin-bottom" => UIStyleProperty.MarginBottom,
                "margin-left" => UIStyleProperty.MarginLeft,
                
                // Padding Properties
                "padding" => UIStyleProperty.Padding,
                "padding-top" => UIStyleProperty.PaddingTop,
                "padding-right" => UIStyleProperty.PaddingRight,
                "padding-bottom" => UIStyleProperty.PaddingBottom,
                "padding-left" => UIStyleProperty.PaddingLeft,
                
                // Border Properties
                "border-width" => UIStyleProperty.BorderWidth,
                "border-top-width" => UIStyleProperty.BorderTopWidth,
                "border-right-width" => UIStyleProperty.BorderRightWidth,
                "border-bottom-width" => UIStyleProperty.BorderBottomWidth,
                "border-left-width" => UIStyleProperty.BorderLeftWidth,
                "border-radius" => UIStyleProperty.BorderRadius,
                "border-top-left-radius" => UIStyleProperty.BorderTopLeftRadius,
                "border-top-right-radius" => UIStyleProperty.BorderTopRightRadius,
                "border-bottom-left-radius" => UIStyleProperty.BorderBottomLeftRadius,
                "border-bottom-right-radius" => UIStyleProperty.BorderBottomRightRadius,
                
                // Border Color Properties
                "border-color" => UIStyleProperty.BorderColor,
                "border-top-color" => UIStyleProperty.BorderTopColor,
                "border-right-color" => UIStyleProperty.BorderRightColor,
                "border-bottom-color" => UIStyleProperty.BorderBottomColor,
                "border-left-color" => UIStyleProperty.BorderLeftColor,
                
                // Background Properties
                "background-color" => UIStyleProperty.BackgroundColor,
                "background-image" => UIStyleProperty.BackgroundImage,
                "background-size" => UIStyleProperty.BackgroundSize,
                "background-repeat" => UIStyleProperty.BackgroundRepeat,
                "background-position" => UIStyleProperty.BackgroundPosition,
                
                // Color Properties
                "color" => UIStyleProperty.Color,
                "opacity" => UIStyleProperty.Opacity,
                
                // Text Properties
                "font-size" => UIStyleProperty.FontSize,
                "font-style" => UIStyleProperty.FontStyle,
                "font-weight" => UIStyleProperty.FontWeight,
                "line-height" => UIStyleProperty.LineHeight,
                "text-align" => UIStyleProperty.TextAlign,
                "text-overflow" => UIStyleProperty.TextOverflow,
                "text-shadow" => UIStyleProperty.TextShadow,
                "white-space" => UIStyleProperty.WhiteSpace,
                "word-wrap" => UIStyleProperty.WordWrap,
                "letter-spacing" => UIStyleProperty.LetterSpacing,
                
                // Unity-specific Text Properties
                "-unity-font-definition" => UIStyleProperty.UnityFontDefinition,
                "-unity-font" => UIStyleProperty.UnityFont,
                "-unity-text-align" => UIStyleProperty.UnityTextAlign,
                "-unity-text-outline-color" => UIStyleProperty.UnityTextOutlineColor,
                "-unity-text-outline-width" => UIStyleProperty.UnityTextOutlineWidth,
                
                // Display Properties
                "display" => UIStyleProperty.Display,
                "visibility" => UIStyleProperty.Visibility,
                "overflow" => UIStyleProperty.Overflow,
                
                // Transform Properties
                "rotate" => UIStyleProperty.Rotate,
                "scale" => UIStyleProperty.Scale,
                "translate" => UIStyleProperty.Translate,
                "transform-origin" => UIStyleProperty.TransformOrigin,
                
                // Cursor and Interaction
                "cursor" => UIStyleProperty.Cursor,
                
                // Unity-specific Properties
                "-unity-slice-left" => UIStyleProperty.UnitySliceLeft,
                "-unity-slice-right" => UIStyleProperty.UnitySliceRight,
                "-unity-slice-top" => UIStyleProperty.UnitySliceTop,
                "-unity-slice-bottom" => UIStyleProperty.UnitySliceBottom,
                "-unity-background-scale-mode" => UIStyleProperty.UnityBackgroundScaleMode,
                "-unity-background-image-tint-color" => UIStyleProperty.UnityBackgroundImageTintColor,
                
                // Transition Properties
                "transition-property" => UIStyleProperty.TransitionProperty,
                "transition-duration" => UIStyleProperty.TransitionDuration,
                "transition-timing-function" => UIStyleProperty.TransitionTimingFunction,
                "transition-delay" => UIStyleProperty.TransitionDelay,
                
                _ => UIStyleProperty.BackgroundColor // Default fallback
            };
        }
    }
}