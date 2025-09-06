namespace Banter.UI.Bridge
{
    /// <summary>
    /// Enumeration of all supported UI event types for type-safe event handling.
    /// This enum matches the TypeScript UIEventType enum in Shared/ui-event-types.ts
    /// </summary>
    public enum UIEventType
    {
        // Basic events
        Click = 0,
        DoubleClick = 1,
        
        // Mouse events
        MouseDown = 10,
        MouseUp = 11,
        MouseEnter = 12,
        MouseLeave = 13,
        MouseMove = 14,
        MouseOver = 15,
        MouseOut = 16,
        Wheel = 17,
        ContextMenu = 18,
        
        // Touch events (for mobile)
        TouchStart = 20,
        TouchEnd = 21,
        TouchMove = 22,
        TouchCancel = 23,
        
        // Keyboard events
        KeyDown = 30,
        KeyUp = 31,
        
        // Focus events
        Focus = 40,
        Blur = 41,
        FocusIn = 42,
        FocusOut = 43,
        
        // Input events
        Change = 50,
        Input = 51,
        Submit = 52,
        
        // Drag events
        DragStart = 60,
        Drag = 61,
        DragEnd = 62,
        DragEnter = 63,
        DragLeave = 64,
        DragOver = 65,
        Drop = 66,
        
        // UI Layout events
        GeometryChange = 70,
        AttachToPanel = 71,
        DetachFromPanel = 72,
        
        // Custom Banter events
        CustomEvent = 100,
    }
    
    /// <summary>
    /// Utility class for converting between UIEventType enum and string names
    /// </summary>
    public static class UIEventTypeHelper
    {
        /// <summary>
        /// Converts UIEventType enum to string name for backwards compatibility
        /// </summary>
        public static string ToEventName(this UIEventType eventType)
        {
            return eventType switch
            {
                UIEventType.Click => "click",
                UIEventType.DoubleClick => "dblclick",
                
                UIEventType.MouseDown => "mousedown",
                UIEventType.MouseUp => "mouseup",
                UIEventType.MouseEnter => "mouseenter",
                UIEventType.MouseLeave => "mouseleave",
                UIEventType.MouseMove => "mousemove",
                UIEventType.MouseOver => "mouseover",
                UIEventType.MouseOut => "mouseout",
                UIEventType.Wheel => "wheel",
                UIEventType.ContextMenu => "contextmenu",
                
                UIEventType.TouchStart => "touchstart",
                UIEventType.TouchEnd => "touchend",
                UIEventType.TouchMove => "touchmove",
                UIEventType.TouchCancel => "touchcancel",
                
                UIEventType.KeyDown => "keydown",
                UIEventType.KeyUp => "keyup",
                
                UIEventType.Focus => "focus",
                UIEventType.Blur => "blur",
                UIEventType.FocusIn => "focusin",
                UIEventType.FocusOut => "focusout",
                
                UIEventType.Change => "change",
                UIEventType.Input => "input",
                UIEventType.Submit => "submit",
                
                UIEventType.DragStart => "dragstart",
                UIEventType.Drag => "drag",
                UIEventType.DragEnd => "dragend",
                UIEventType.DragEnter => "dragenter",
                UIEventType.DragLeave => "dragleave",
                UIEventType.DragOver => "dragover",
                UIEventType.Drop => "drop",
                
                UIEventType.GeometryChange => "geometrychange",
                UIEventType.AttachToPanel => "attachtopanel",
                UIEventType.DetachFromPanel => "detachfrompanel",
                
                UIEventType.CustomEvent => "custom",
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
                "click" => UIEventType.Click,
                "dblclick" => UIEventType.DoubleClick,
                
                "mousedown" => UIEventType.MouseDown,
                "mouseup" => UIEventType.MouseUp,
                "mouseenter" => UIEventType.MouseEnter,
                "mouseleave" => UIEventType.MouseLeave,
                "mousemove" => UIEventType.MouseMove,
                "mouseover" => UIEventType.MouseOver,
                "mouseout" => UIEventType.MouseOut,
                "wheel" => UIEventType.Wheel,
                "contextmenu" => UIEventType.ContextMenu,
                
                "touchstart" => UIEventType.TouchStart,
                "touchend" => UIEventType.TouchEnd,
                "touchmove" => UIEventType.TouchMove,
                "touchcancel" => UIEventType.TouchCancel,
                
                "keydown" => UIEventType.KeyDown,
                "keyup" => UIEventType.KeyUp,
                
                "focus" => UIEventType.Focus,
                "blur" => UIEventType.Blur,
                "focusin" => UIEventType.FocusIn,
                "focusout" => UIEventType.FocusOut,
                
                "change" => UIEventType.Change,
                "input" => UIEventType.Input,
                "submit" => UIEventType.Submit,
                
                "dragstart" => UIEventType.DragStart,
                "drag" => UIEventType.Drag,
                "dragend" => UIEventType.DragEnd,
                "dragenter" => UIEventType.DragEnter,
                "dragleave" => UIEventType.DragLeave,
                "dragover" => UIEventType.DragOver,
                "drop" => UIEventType.Drop,
                
                "geometrychange" => UIEventType.GeometryChange,
                "attachtopanel" => UIEventType.AttachToPanel,
                "detachfrompanel" => UIEventType.DetachFromPanel,
                
                "custom" => UIEventType.CustomEvent,
                _ => UIEventType.CustomEvent
            };
        }
    }
}