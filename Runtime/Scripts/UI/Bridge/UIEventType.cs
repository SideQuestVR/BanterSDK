namespace Banter.UI.Bridge
{
    /// <summary>
    /// Enumeration of all supported UI event types for type-safe event handling.
    /// This enum matches the TypeScript UIEventType constants in UIEvent.ts
    /// </summary>
    public enum UIEventType
    {
        // Mouse events
        Click = 0,
        DoubleClick = 1,
        MouseDown = 2,
        MouseUp = 3,
        MouseMove = 4,
        MouseEnter = 5,
        MouseLeave = 6,
        MouseOver = 7,
        MouseOut = 8,
        ContextMenu = 9,
        
        // Pointer events
        PointerDown = 10,
        PointerUp = 11,
        PointerMove = 12,
        PointerEnter = 13,
        PointerLeave = 14,
        PointerCancel = 15,
        
        // Touch events
        TouchStart = 20,
        TouchEnd = 21,
        TouchMove = 22,
        TouchCancel = 23,
        
        // Focus events
        Focus = 30,
        Blur = 31,
        FocusIn = 32,
        FocusOut = 33,
        
        // Keyboard events
        KeyDown = 40,
        KeyUp = 41,
        KeyPress = 42,
        
        // Input events
        Input = 50,
        Change = 51,
        Submit = 52,
        Reset = 53,
        
        // Drag events
        DragStart = 60,
        Drag = 61,
        DragEnd = 62,
        DragEnter = 63,
        DragLeave = 64,
        DragOver = 65,
        Drop = 66,
        
        // Wheel event
        Wheel = 70,
        
        // UI-specific events
        GeometryChanged = 80,
        Attach = 81,
        Detach = 82,
        Tooltip = 83,
    }
    
    /// <summary>
    /// Utility class for converting between UIEventType enum and string names
    /// </summary>
    public static class UIEventTypeHelper
    {
        /// <summary>
        /// Converts UIEventType enum to string name matching TypeScript constants
        /// </summary>
        public static string ToEventName(this UIEventType eventType)
        {
            return eventType switch
            {
                // Mouse events
                UIEventType.Click => "click",
                UIEventType.DoubleClick => "dblclick",
                UIEventType.MouseDown => "mousedown",
                UIEventType.MouseUp => "mouseup",
                UIEventType.MouseMove => "mousemove",
                UIEventType.MouseEnter => "mouseenter",
                UIEventType.MouseLeave => "mouseleave",
                UIEventType.MouseOver => "mouseover",
                UIEventType.MouseOut => "mouseout",
                UIEventType.ContextMenu => "contextmenu",
                
                // Pointer events
                UIEventType.PointerDown => "pointerdown",
                UIEventType.PointerUp => "pointerup",
                UIEventType.PointerMove => "pointermove",
                UIEventType.PointerEnter => "pointerenter",
                UIEventType.PointerLeave => "pointerleave",
                UIEventType.PointerCancel => "pointercancel",
                
                // Touch events
                UIEventType.TouchStart => "touchstart",
                UIEventType.TouchEnd => "touchend",
                UIEventType.TouchMove => "touchmove",
                UIEventType.TouchCancel => "touchcancel",
                
                // Focus events
                UIEventType.Focus => "focus",
                UIEventType.Blur => "blur",
                UIEventType.FocusIn => "focusin",
                UIEventType.FocusOut => "focusout",
                
                // Keyboard events
                UIEventType.KeyDown => "keydown",
                UIEventType.KeyUp => "keyup",
                UIEventType.KeyPress => "keypress",
                
                // Input events
                UIEventType.Input => "input",
                UIEventType.Change => "change",
                UIEventType.Submit => "submit",
                UIEventType.Reset => "reset",
                
                // Drag events
                UIEventType.DragStart => "dragstart",
                UIEventType.Drag => "drag",
                UIEventType.DragEnd => "dragend",
                UIEventType.DragEnter => "dragenter",
                UIEventType.DragLeave => "dragleave",
                UIEventType.DragOver => "dragover",
                UIEventType.Drop => "drop",
                
                // Wheel event
                UIEventType.Wheel => "wheel",
                
                // UI-specific events
                UIEventType.GeometryChanged => "geometrychanged",
                UIEventType.Attach => "attach",
                UIEventType.Detach => "detach",
                UIEventType.Tooltip => "tooltip",
                _ => "unknown"
            };
        }
        
        /// <summary>
        /// Converts string event name to UIEventType enum
        /// </summary>
        public static UIEventType FromEventName(string eventName)
        {
            return eventName.ToLower() switch
            {
                // Mouse events
                "click" => UIEventType.Click,
                "dblclick" => UIEventType.DoubleClick,
                "mousedown" => UIEventType.MouseDown,
                "mouseup" => UIEventType.MouseUp,
                "mousemove" => UIEventType.MouseMove,
                "mouseenter" => UIEventType.MouseEnter,
                "mouseleave" => UIEventType.MouseLeave,
                "mouseover" => UIEventType.MouseOver,
                "mouseout" => UIEventType.MouseOut,
                "contextmenu" => UIEventType.ContextMenu,
                
                // Pointer events
                "pointerdown" => UIEventType.PointerDown,
                "pointerup" => UIEventType.PointerUp,
                "pointermove" => UIEventType.PointerMove,
                "pointerenter" => UIEventType.PointerEnter,
                "pointerleave" => UIEventType.PointerLeave,
                "pointercancel" => UIEventType.PointerCancel,
                
                // Touch events
                "touchstart" => UIEventType.TouchStart,
                "touchend" => UIEventType.TouchEnd,
                "touchmove" => UIEventType.TouchMove,
                "touchcancel" => UIEventType.TouchCancel,
                
                // Focus events
                "focus" => UIEventType.Focus,
                "blur" => UIEventType.Blur,
                "focusin" => UIEventType.FocusIn,
                "focusout" => UIEventType.FocusOut,
                
                // Keyboard events
                "keydown" => UIEventType.KeyDown,
                "keyup" => UIEventType.KeyUp,
                "keypress" => UIEventType.KeyPress,
                
                // Input events
                "input" => UIEventType.Input,
                "change" => UIEventType.Change,
                "submit" => UIEventType.Submit,
                "reset" => UIEventType.Reset,
                
                // Drag events
                "dragstart" => UIEventType.DragStart,
                "drag" => UIEventType.Drag,
                "dragend" => UIEventType.DragEnd,
                "dragenter" => UIEventType.DragEnter,
                "dragleave" => UIEventType.DragLeave,
                "dragover" => UIEventType.DragOver,
                "drop" => UIEventType.Drop,
                
                // Wheel event
                "wheel" => UIEventType.Wheel,
                
                // UI-specific events
                "geometrychanged" => UIEventType.GeometryChanged,
                "attach" => UIEventType.Attach,
                "detach" => UIEventType.Detach,
                "tooltip" => UIEventType.Tooltip,
                _ => UIEventType.Click
            };
        }
    }
}