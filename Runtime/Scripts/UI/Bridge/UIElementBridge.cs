using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using Banter.SDK;
using Banter.UI.Elements;

namespace Banter.UI.Bridge
{
    public class UIElementBridge : MonoBehaviour
    {
        private const string LogPrefix = "[UIElementBridge]";

        [System.Diagnostics.Conditional("BANTER_UI_DEBUG")]
        private static void LogVerbose(string message)
        {
            Debug.Log($"{LogPrefix} {message}");
        }

        // Registry for multiple panel instances
        private static Dictionary<string, UIElementBridge> _panelInstances = new Dictionary<string, UIElementBridge>();
        
        public string panelId;
        public BanterUIPanel banterUiPanel;
        
        public static UIElementBridge GetPanelInstance(string panelId)
        {
            _panelInstances.TryGetValue(panelId, out var instance);
            return instance;
        }

        public static bool IsWorldSpaceUIPanel(IPanel panel)
        {
            foreach (var instance in _panelInstances.Values)
            {
                if (instance.mainDocument.runtimePanel == panel && !instance.banterUiPanel.ScreenSpace)
                {
                    return true;
                }
            }
            return false;
        }
        
        public static void RegisterPanelInstance(string panelId, UIElementBridge instance, BanterUIPanel banterUIPanel)
        {
            if (string.IsNullOrEmpty(panelId))
            {
                Debug.LogWarning("[UIElementBridge] Attempting to register panel with null/empty ID");
                return;
            }

            _panelInstances[panelId] = instance;
            instance.panelId = panelId;
            instance.banterUiPanel = banterUIPanel;
            LogVerbose($"Registered panel instance: {panelId}");
        }
        
        public static void UnregisterPanelInstance(string panelId)
        {
            if (_panelInstances.ContainsKey(panelId))
            {
                _panelInstances.Remove(panelId);
                LogVerbose($"Unregistered panel instance: {panelId}");
            }
        }
        
        private Dictionary<string, VisualElement> _elements = new Dictionary<string, VisualElement>();
        private Dictionary<VisualElement, string> _elementToId = new Dictionary<VisualElement, string>(); // Reverse lookup for O(1) element ID retrieval
        private Dictionary<string, UIDocument> _documents = new Dictionary<string, UIDocument>();
        public UIDocument mainDocument;
        public BanterLink banterLink;
        
        // Store event callbacks for unregistration
        private Dictionary<(string elementId, UIEventType eventType), object> _registeredCallbacks = new Dictionary<(string, UIEventType), object>();
        
        // Texture cache for background images
        private static Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>();
        private static Dictionary<string, Task<Texture2D>> _downloadingTextures = new Dictionary<string, Task<Texture2D>>();
        
        // Static HashSet for efficient UI command checking
        private static readonly HashSet<string> _uiCommandPrefixes = new HashSet<string>
        {
            UICommands.CREATE_UI_ELEMENT,
            UICommands.DESTROY_UI_ELEMENT,
            UICommands.ATTACH_UI_CHILD,
            UICommands.DETACH_UI_CHILD,
            UICommands.SET_UI_PROPERTY,
            UICommands.GET_UI_PROPERTY,
            UICommands.SET_UI_STYLE,
            UICommands.GET_UI_STYLE,
            UICommands.BATCH_UI_UPDATE,
            UICommands.UI_EVENT,
            UICommands.REGISTER_UI_EVENT,
            UICommands.UNREGISTER_UI_EVENT,
            UICommands.CALL_UI_METHOD,
            UICommands.UI_METHOD_RETURN,
            UICommands.INSTANTIATE_UXML,
            UICommands.QUERY_UI_ELEMENT,
            UICommands.GET_UI_SLOT,
            UICommands.BIND_UI_DATA,
            UICommands.UNBIND_UI_DATA,
            UICommands.UPDATE_UI_BINDING,
            UICommands.SET_UI_PARENT,
            UICommands.GET_UI_CHILDREN,
            UICommands.SET_UI_VISIBLE,
            UICommands.SET_UI_ENABLED,
            UICommands.SET_UI_FOCUS,
            UICommands.CLEAR_UI_FOCUS,
            UICommands.FORCE_UI_LAYOUT,
            UICommands.MEASURE_UI_ELEMENT
        };
        
        /// <summary>
        /// Check if a message is a UI command
        /// </summary>
        /// <param name="message">The message to check</param>
        /// <returns>True if the message is a UI command</returns>
        public static bool IsUICommand(string message)
        {
            // With panel targeting, format is: panelId|command|data
            // So we need to check the second part for the UI command
            var parts = message.Split(MessageDelimiters.PRIMARY);
            if (parts.Length < 2) return false;
            
            string command = parts[1]; // Second part is the command
            return _uiCommandPrefixes.Contains(command);
        }
        
        // Handle all UI messages with required panel targeting
        public static void HandleMessage(string message)
        {
            try
            {
                LogVerbose($"Handling message: {message}");
                var parts = message.Split(MessageDelimiters.PRIMARY);
                if (parts.Length < 2)
                {
                    Debug.LogError($"[UIElementBridge] Invalid message format, expected panelId|command|data: {message}");
                    return;
                }

                var panelId = parts[0];
                var command = parts[1];
                var data = parts.Length > 2 ? parts[2].Split(MessageDelimiters.SECONDARY) : new string[0];

                var targetBridge = GetPanelInstance(panelId);
                if (targetBridge != null)
                {
                    targetBridge.ProcessCommand(command, data);
                }
                else
                {
                    Debug.LogWarning($"[UIElementBridge] No panel found with ID: {panelId}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIElementBridge] Error handling message: {e.Message}\nMessage: {message}");
            }
        }
        
        private void ProcessCommand(string command, string[] data)
        {
            switch (command)
            {
                case UICommands.CREATE_UI_ELEMENT:
                    CreateUIElement(data);
                    break;
                    
                case UICommands.DESTROY_UI_ELEMENT:
                    DestroyUIElement(data);
                    break;
                    
                case UICommands.ATTACH_UI_CHILD:
                    AttachChild(data);
                    break;
                    
                case UICommands.DETACH_UI_CHILD:
                    DetachChild(data);
                    break;
                    
                case UICommands.SET_UI_PROPERTY:
                    SetProperty(data);
                    break;
                    
                case UICommands.GET_UI_PROPERTY:
                    GetProperty(data);
                    break;
                    
                case UICommands.SET_UI_STYLE:
                    SetStyle(data);
                    break;

                case UICommands.GET_UI_STYLE:
                    GetStyle(data);
                    break;

                case UICommands.CALL_UI_METHOD:
                    CallMethod(data);
                    break;
                    
                case UICommands.REGISTER_UI_EVENT:
                    RegisterEvent(data);
                    break;
                    
                case UICommands.UNREGISTER_UI_EVENT:
                    UnregisterEvent(data);
                    break;
                    
                case UICommands.SET_UI_FOCUS:
                    SetFocus(data);
                    break;
                    
                case UICommands.CLEAR_UI_FOCUS:
                    ClearFocus(data);
                    break;
                    
                case UICommands.BATCH_UI_UPDATE:
                    ProcessBatchUpdate(data);
                    break;
                    
                default:
                    Debug.LogWarning($"[UIElementBridge] Unknown command: {command}");
                    break;
            }
        }
        
        private void CreateUIElement(string[] data)
        {

            if (mainDocument != null && mainDocument.rootVisualElement != null && !_elements.ContainsKey("root"))
            {
                _elements["root"] = mainDocument.rootVisualElement;
                _elementToId[mainDocument.rootVisualElement] = "root";
            }
            LogVerbose("Creating element" + string.Join(",", data));
            if (data.Length < 3) return;
            
            var elementId = data[0];
            var elementType = data[1];
            var parentId = data[2];
            LogVerbose("Creating element: " + elementId + " of type " + elementType + " with parent " + parentId);
            // Create the appropriate VisualElement based on type
            VisualElement element = CreateElementByType(elementType);
            
            if (element != null)
            {
                _elements[elementId] = element;
                _elementToId[element] = elementId;

                // Attach to parent if specified
                if (parentId != "null" && _elements.TryGetValue(parentId, out var parent))
                {
                    LogVerbose("Attaching to parent element: " + parentId);
                    parent.Add(element);
                }
                else
                {
                    // If no valid parent, attach to root
                    if (_elements.TryGetValue("root", out var root))
                    {
                        LogVerbose("Attaching to root element.");
                        root.Add(element);
                    }
                    else
                    {
                        Debug.LogWarning("[UIElementBridge] No valid parent found and root element is missing.");
                    }
                }
                
                LogVerbose($"Created element: {elementId} of type {elementType}");
            }
        }
        
        private VisualElement CreateElementByType(string type)
        {
            // Parse the UIElementType enum value
            if (int.TryParse(type, out int typeValue))
            {
                return typeValue switch
                {
                    0 => new VisualElement(), // VisualElement
                    1 => new ScrollView(), // ScrollView
                    2 => new ListView(), // ListView
                    10 => new BanterUIButton(), // Button -> BanterUIButton
                    11 => new BanterUILabel(), // Label -> BanterUILabel
                    12 => new TextField(), // TextField
                    13 => new Toggle(), // Toggle
                    14 => new Slider(), // Slider
                    15 => new DropdownField(), // DropdownField
                    20 => new UnityEngine.UIElements.Box(), // Box
                    21 => new GroupBox(), // GroupBox
                    22 => new Foldout(), // Foldout
                    100 => new VisualElement() { name = "UIPanel" }, // Custom UIPanel
                    _ => new VisualElement()
                };
            }
            
            return new VisualElement();
        }
        
        private void DestroyUIElement(string[] data)
        {
            if (data.Length < 1) return;
            
            var elementId = data[0];
            
            if (_elements.TryGetValue(elementId, out var element))
            {
                // Clean up any registered event callbacks for this element
                var callbacksToRemove = _registeredCallbacks.Keys
                    .Where(key => key.elementId == elementId)
                    .ToList();
                    
                foreach (var key in callbacksToRemove)
                {
                    _registeredCallbacks.Remove(key);
                }
                
                element.RemoveFromHierarchy();
                _elements.Remove(elementId);
                _elementToId.Remove(element);

                LogVerbose($"Destroyed element: {elementId} and cleaned up {callbacksToRemove.Count} event callbacks");
            }
        }
        
        private void AttachChild(string[] data)
        {
            if (data.Length < 3) return;
            
            var parentId = data[0];
            var childId = data[1];
            var index = int.Parse(data[2]);
            
            if (_elements.TryGetValue(parentId, out var parent) &&
                _elements.TryGetValue(childId, out var child))
            {
                if (index >= 0 && index < parent.childCount)
                {
                    parent.Insert(index, child);
                }
                else
                {
                    parent.Add(child);
                }
                
                LogVerbose($"Attached {childId} to {parentId}");
            }
        }
        
        private void DetachChild(string[] data)
        {
            if (data.Length < 2) return;
            
            var parentId = data[0];
            var childId = data[1];
            
            if (_elements.TryGetValue(childId, out var child))
            {
                child.RemoveFromHierarchy();
                LogVerbose($"Detached {childId} from {parentId}");
            }
        }
        
        private void SetProperty(string[] data)
        {
            if (data.Length < 3)
            {
                Debug.LogError($"[UIElementBridge.SetProperty] Data array too short (length={data.Length}), expected at least 3");
                return;
            }

            var elementId = data[0];
            var propertyName = data[1];
            var value = data[2];

            // Resolve element name to ID if needed
            elementId = ResolveElementIdOrName(elementId);

            if (!_elements.TryGetValue(elementId, out var element))
            {
                Debug.LogWarning($"[UIElementBridge] No element found with ID: {elementId} to set property '{propertyName}'");
                return;
            }

            // Try to use the generated SetProperty method if the element supports it
            if (element is IUIPropertySetter propertySetter)
            {
                if (propertySetter.SetProperty(propertyName, value))
                    return; // Property was handled by the generated method
            }

            // Fallback: Handle common UI element types and properties directly
            try
            {
                switch (element)
                {
                    case Label label when propertyName == "text":
                        label.text = value;
                        return;

                    case TextField textField when propertyName == "value":
                        textField.value = value;
                        return;

                    case TextField textField when propertyName == "text":
                        textField.value = value; // TextField uses 'value' not 'text'
                        return;

                    case Button button when propertyName == "text":
                        button.text = value;
                        return;

                    case Toggle toggle when propertyName == "value":
                        toggle.value = bool.Parse(value);
                        return;

                    case Toggle toggle when propertyName == "label":
                        toggle.label = value;
                        return;

                    case Slider slider when propertyName == "value":
                        slider.value = float.Parse(value);
                        return;

                    case Slider slider when propertyName == "minvalue":
                        slider.lowValue = float.Parse(value);
                        return;

                    case Slider slider when propertyName == "maxvalue":
                        slider.highValue = float.Parse(value);
                        return;

                    case SliderInt sliderInt when propertyName == "value":
                        sliderInt.value = int.Parse(value);
                        return;

                    case MinMaxSlider minMaxSlider when propertyName == "value":
                        var parts = value.Split(',');
                        if (parts.Length == 2)
                        {
                            minMaxSlider.value = new Vector2(float.Parse(parts[0]), float.Parse(parts[1]));
                        }
                        return;

                    case ScrollView scrollView when propertyName == "horizontalScrolling":
                        scrollView.horizontalScrollerVisibility = (value == "1" || value.ToLower() == "true")
                            ? ScrollerVisibility.Auto
                            : ScrollerVisibility.Hidden;
                        return;

                    case ScrollView scrollView when propertyName == "verticalScrolling":
                        scrollView.verticalScrollerVisibility = (value == "1" || value.ToLower() == "true")
                            ? ScrollerVisibility.Auto
                            : ScrollerVisibility.Hidden;
                        return;
                }

                // Handle common properties that apply to all VisualElements
                if (propertyName == "enabled")
                {
                    element.SetEnabled(value == "1" || value.ToLower() == "true");
                    return;
                }

                if (propertyName == "visible")
                {
                    element.style.display = (value == "1" || value.ToLower() == "true") ? DisplayStyle.Flex : DisplayStyle.None;
                    return;
                }

                // Generic reflection fallback for properties not handled above
                var propertyInfo = element.GetType().GetProperty(propertyName);
                if (propertyInfo != null && propertyInfo.CanWrite)
                {
                    var targetType = propertyInfo.PropertyType;
                    object convertedValue = ConvertValue(value, targetType);
                    propertyInfo.SetValue(element, convertedValue);
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIElementBridge.SetProperty] Error setting property '{propertyName}' on {element.GetType().Name}: {ex.Message}");
                return;
            }

            // If we get here, the property wasn't handled by any method
            Debug.LogWarning($"[UIElementBridge] Unhandled property '{propertyName}' for element type '{element.GetType().Name}' with value '{value}'");
        }
        
        private void GetProperty(string[] data)
        {
            if (data.Length < 2) return;

            var elementId = data[0];
            var propertyName = data[1];

            // Resolve element name to ID if needed
            elementId = ResolveElementIdOrName(elementId);

            if (!_elements.TryGetValue(elementId, out var element))
            {
                Debug.LogWarning($"[UIElementBridge] No element found with ID: {elementId} to get property '{propertyName}'");
                return;
            }
            
            // Get the property value based on property name and element type
            var propertyValue = GetElementProperty(element, propertyName);
            
            // Trigger EventBus event for Visual Scripting to receive the value
            // Format: UIProperty_{elementId}_{propertyName} with the property value
            var eventName = $"UIProperty_{elementId}_{propertyName}";
            Unity.VisualScripting.EventBus.Trigger(eventName, new Unity.VisualScripting.CustomEventArgs(eventName, new object[] { propertyValue }));
            
            LogVerbose($"Got property '{propertyName}' = '{propertyValue}' from element {elementId}, triggered event {eventName}");
        }
        
        private object GetElementProperty(VisualElement element, string propertyName)
        {
            // Handle common UI properties
            switch (propertyName.ToLower())
            {
                case "text":
                    if (element is UnityEngine.UIElements.Label label)
                        return label.text ?? "";
                    if (element is TextField textField)
                        return textField.value ?? "";
                    break;
                    
                case "value":
                    if (element is Slider slider)
                        return slider.value;
                    if (element is Toggle toggle)
                        return toggle.value ? "1" : "0";
                    if (element is TextField textFieldValue)
                        return textFieldValue.value ?? "";
                    if (element is DropdownField dropdown)
                        return dropdown.value ?? "";
                    break;
                    
                case "enabled":
                    return element.enabledSelf ? "1" : "0";
                    
                case "visible":
                    return element.style.display == UnityEngine.UIElements.DisplayStyle.Flex ? "1" : "0";
                    
                case "checked":
                    if (element is Toggle toggleChecked)
                        return toggleChecked.value ? "1" : "0";
                    break;
                    
                case "name":
                    return element.name ?? "";
                    
                case "tooltip":
                    return element.tooltip ?? "";
                    
                // Add more properties as needed
                default:
                    Debug.LogWarning($"[UIElementBridge] Unhandled get property '{propertyName}' for element type '{element.GetType().Name}'");
                    return null;
            }
            
            return null;
        }
        
        private void SetElementValue(VisualElement element, string value)
        {
            switch (element)
            {
                case TextField textField:
                    textField.value = value;
                    break;
                case Toggle toggle:
                    toggle.value = value == "1";
                    break;
                case Slider slider:
                    slider.value = float.Parse(value);
                    break;
                case DropdownField dropdown:
                    dropdown.value = value;
                    break;
            }
        }
        
        private void SetStyle(string[] data)
        {
            if (data.Length < 3) return;
            
            var elementId = data[0];
            var styleNameString = data[1];
            var value = data[2];
            
            if (!_elements.TryGetValue(elementId, out var element)) return;
            
            // Convert string to enum
            var styleProperty = UIStylePropertyHelper.FromUSSName(styleNameString);
            
            // Apply style based on property enum
            switch (styleProperty)
            {
                // Layout Properties (Flexbox)
                case UIStyleProperty.AlignContent:
                    element.style.alignContent = ParseEnum<Align>(value);
                    break;
                case UIStyleProperty.AlignItems:
                    element.style.alignItems = ParseEnum<Align>(value);
                    break;
                case UIStyleProperty.AlignSelf:
                    element.style.alignSelf = ParseEnum<Align>(value);
                    break;
                case UIStyleProperty.FlexBasis:
                    element.style.flexBasis = ParseLength(value);
                    break;
                case UIStyleProperty.FlexDirection:
                    element.style.flexDirection = ParseEnum<FlexDirection>(value);
                    break;
                case UIStyleProperty.FlexGrow:
                    element.style.flexGrow = ParseFloat(value);
                    break;
                case UIStyleProperty.FlexShrink:
                    element.style.flexShrink = ParseFloat(value);
                    break;
                case UIStyleProperty.FlexWrap:
                    element.style.flexWrap = ParseEnum<Wrap>(value);
                    break;
                case UIStyleProperty.JustifyContent:
                    element.style.justifyContent = ParseEnum<Justify>(value);
                    break;
                    
                // Size Properties
                case UIStyleProperty.Width:
                    element.style.width = ParseLength(value);
                    break;
                case UIStyleProperty.Height:
                    element.style.height = ParseLength(value);
                    break;
                case UIStyleProperty.MinWidth:
                    element.style.minWidth = ParseLength(value);
                    break;
                case UIStyleProperty.MinHeight:
                    element.style.minHeight = ParseLength(value);
                    break;
                case UIStyleProperty.MaxWidth:
                    element.style.maxWidth = ParseLength(value);
                    break;
                case UIStyleProperty.MaxHeight:
                    element.style.maxHeight = ParseLength(value);
                    break;
                    
                // Position Properties
                case UIStyleProperty.Position:
                    element.style.position = ParseEnum<Position>(value);
                    break;
                case UIStyleProperty.Left:
                    element.style.left = ParseLength(value);
                    break;
                case UIStyleProperty.Top:
                    element.style.top = ParseLength(value);
                    break;
                case UIStyleProperty.Right:
                    element.style.right = ParseLength(value);
                    break;
                case UIStyleProperty.Bottom:
                    element.style.bottom = ParseLength(value);
                    break;
                    
                // Margin Properties
                case UIStyleProperty.Margin:
                    var margin = ParseSpacing(value);
                    element.style.marginTop = margin[0];
                    element.style.marginRight = margin[1];
                    element.style.marginBottom = margin[2];
                    element.style.marginLeft = margin[3];
                    break;
                case UIStyleProperty.MarginTop:
                    element.style.marginTop = ParseLength(value);
                    break;
                case UIStyleProperty.MarginRight:
                    element.style.marginRight = ParseLength(value);
                    break;
                case UIStyleProperty.MarginBottom:
                    element.style.marginBottom = ParseLength(value);
                    break;
                case UIStyleProperty.MarginLeft:
                    element.style.marginLeft = ParseLength(value);
                    break;
                    
                // Padding Properties
                case UIStyleProperty.Padding:
                    var padding = ParseSpacing(value);
                    element.style.paddingTop = padding[0];
                    element.style.paddingRight = padding[1];
                    element.style.paddingBottom = padding[2];
                    element.style.paddingLeft = padding[3];
                    break;
                case UIStyleProperty.PaddingTop:
                    element.style.paddingTop = ParseLength(value);
                    break;
                case UIStyleProperty.PaddingRight:
                    element.style.paddingRight = ParseLength(value);
                    break;
                case UIStyleProperty.PaddingBottom:
                    element.style.paddingBottom = ParseLength(value);
                    break;
                case UIStyleProperty.PaddingLeft:
                    element.style.paddingLeft = ParseLength(value);
                    break;
                    
                // Border Properties
                case UIStyleProperty.BorderWidth:
                    var borderWidth = ParseFloat(value);
                    element.style.borderTopWidth = borderWidth;
                    element.style.borderRightWidth = borderWidth;
                    element.style.borderBottomWidth = borderWidth;
                    element.style.borderLeftWidth = borderWidth;
                    break;
                case UIStyleProperty.BorderTopWidth:
                    element.style.borderTopWidth = ParseFloat(value);
                    break;
                case UIStyleProperty.BorderRightWidth:
                    element.style.borderRightWidth = ParseFloat(value);
                    break;
                case UIStyleProperty.BorderBottomWidth:
                    element.style.borderBottomWidth = ParseFloat(value);
                    break;
                case UIStyleProperty.BorderLeftWidth:
                    element.style.borderLeftWidth = ParseFloat(value);
                    break;
                case UIStyleProperty.BorderRadius:
                    var radius = ParseLength(value);
                    element.style.borderTopLeftRadius = radius;
                    element.style.borderTopRightRadius = radius;
                    element.style.borderBottomLeftRadius = radius;
                    element.style.borderBottomRightRadius = radius;
                    break;
                case UIStyleProperty.BorderTopLeftRadius:
                    element.style.borderTopLeftRadius = ParseLength(value);
                    break;
                case UIStyleProperty.BorderTopRightRadius:
                    element.style.borderTopRightRadius = ParseLength(value);
                    break;
                case UIStyleProperty.BorderBottomLeftRadius:
                    element.style.borderBottomLeftRadius = ParseLength(value);
                    break;
                case UIStyleProperty.BorderBottomRightRadius:
                    element.style.borderBottomRightRadius = ParseLength(value);
                    break;
                    
                // Border Color Properties
                case UIStyleProperty.BorderColor:
                    var borderColor = ParseColor(value);
                    element.style.borderTopColor = borderColor;
                    element.style.borderRightColor = borderColor;
                    element.style.borderBottomColor = borderColor;
                    element.style.borderLeftColor = borderColor;
                    break;
                case UIStyleProperty.BorderTopColor:
                    element.style.borderTopColor = ParseColor(value);
                    break;
                case UIStyleProperty.BorderRightColor:
                    element.style.borderRightColor = ParseColor(value);
                    break;
                case UIStyleProperty.BorderBottomColor:
                    element.style.borderBottomColor = ParseColor(value);
                    break;
                case UIStyleProperty.BorderLeftColor:
                    element.style.borderLeftColor = ParseColor(value);
                    break;
                    
                // Background Properties
                case UIStyleProperty.BackgroundColor:
                    element.style.backgroundColor = ParseColor(value);
                    break;
                case UIStyleProperty.BackgroundImage:
                    SetBackgroundImage(element, value);
                    break;
                    
                // Color Properties
                case UIStyleProperty.Color:
                    element.style.color = ParseColor(value);
                    break;
                case UIStyleProperty.Opacity:
                    element.style.opacity = ParseFloat(value);
                    break;
                    
                // Text Properties
                case UIStyleProperty.FontSize:
                    element.style.fontSize = ParseLength(value);
                    break;
                case UIStyleProperty.FontStyle:
                    element.style.unityFontStyleAndWeight = ParseEnum<FontStyle>(value);
                    break;
                case UIStyleProperty.FontWeight:
                    element.style.unityFontStyleAndWeight = ParseEnum<FontStyle>(value);
                    break;
                case UIStyleProperty.TextAlign:
                    element.style.unityTextAlign = ParseEnum<TextAnchor>(value);
                    break;
                case UIStyleProperty.WhiteSpace:
                    element.style.whiteSpace = ParseEnum<WhiteSpace>(value);
                    break;
                case UIStyleProperty.TextOverflow:
                    element.style.textOverflow = ParseEnum<TextOverflow>(value);
                    break;
                    
                // Display Properties
                case UIStyleProperty.Display:
                    element.style.display = ParseEnum<DisplayStyle>(value);
                    break;
                case UIStyleProperty.Visibility:
                    element.style.visibility = ParseEnum<Visibility>(value);
                    break;
                case UIStyleProperty.Overflow:
                    element.style.overflow = ParseEnum<Overflow>(value);
                    break;
                    
                // Unity-specific Text Properties
                case UIStyleProperty.UnityTextAlign:
                    element.style.unityTextAlign = ParseEnum<TextAnchor>(value);
                    break;
                case UIStyleProperty.UnityTextOutlineColor:
                    element.style.unityTextOutlineColor = ParseColor(value);
                    break;
                case UIStyleProperty.UnityTextOutlineWidth:
                    element.style.unityTextOutlineWidth = ParseFloat(value);
                    break;
                    
                default:
                    Debug.LogWarning($"[UIElementBridge] Unsupported style property: {styleProperty} ({styleNameString})");
                    break;
            }
            
            LogVerbose($"Set style {styleProperty} = '{value}' on element {elementId}");
        }

        private void GetStyle(string[] data)
        {
            if (data.Length < 2) return;

            var elementId = data[0];
            var styleNameString = data[1];

            if (!_elements.TryGetValue(elementId, out var element))
            {
                Debug.LogWarning($"[UIElementBridge] No element found with ID: {elementId} to get style '{styleNameString}'");
                return;
            }

            // Convert string to enum
            var styleProperty = UIStylePropertyHelper.FromUSSName(styleNameString);

            // Get style value from resolved style
            string styleValue = GetStyleValue(element, styleProperty);

            // Trigger EventBus event for Visual Scripting to receive the value
            // Format: UIStyle_{elementId}_{styleName} with the style value
            var eventName = $"UIStyle_{elementId}_{styleNameString}";
            Unity.VisualScripting.EventBus.Trigger(eventName, new Unity.VisualScripting.CustomEventArgs(eventName, new object[] { styleValue }));

            LogVerbose($"Got style '{styleNameString}' = '{styleValue}' from element {elementId}, triggered event {eventName}");
        }

        private string GetStyleValue(VisualElement element, UIStyleProperty property)
        {
            var style = element.resolvedStyle;

            switch (property)
            {
                // Layout Properties (Flexbox)
                case UIStyleProperty.AlignContent:
                    return style.alignContent.ToString().ToLower();
                case UIStyleProperty.AlignItems:
                    return style.alignItems.ToString().ToLower();
                case UIStyleProperty.AlignSelf:
                    return style.alignSelf.ToString().ToLower();
                case UIStyleProperty.FlexBasis:
                    return FormatStyleFloat(style.flexBasis);
                case UIStyleProperty.FlexDirection:
                    return FormatFlexDirection(style.flexDirection);
                case UIStyleProperty.FlexGrow:
                    return FormatStyleFloat(style.flexGrow);
                case UIStyleProperty.FlexShrink:
                    return FormatStyleFloat(style.flexShrink);
                case UIStyleProperty.FlexWrap:
                    return FormatWrap(style.flexWrap);
                case UIStyleProperty.JustifyContent:
                    return FormatJustify(style.justifyContent);

                // Size Properties
                case UIStyleProperty.Width:
                    return FormatStyleLength(style.width);
                case UIStyleProperty.Height:
                    return FormatStyleLength(style.height);
                case UIStyleProperty.MinWidth:
                    return FormatStyleFloat(style.minWidth);
                case UIStyleProperty.MinHeight:
                    return FormatStyleFloat(style.minHeight);
                case UIStyleProperty.MaxWidth:
                    return FormatStyleFloat(style.maxWidth);
                case UIStyleProperty.MaxHeight:
                    return FormatStyleFloat(style.maxHeight);

                // Position Properties
                case UIStyleProperty.Position:
                    return style.position.ToString().ToLower();
                case UIStyleProperty.Left:
                    return FormatStyleLength(style.left);
                case UIStyleProperty.Top:
                    return FormatStyleLength(style.top);
                case UIStyleProperty.Right:
                    return FormatStyleLength(style.right);
                case UIStyleProperty.Bottom:
                    return FormatStyleLength(style.bottom);

                // Margin Properties
                case UIStyleProperty.MarginTop:
                    return FormatStyleLength(style.marginTop);
                case UIStyleProperty.MarginRight:
                    return FormatStyleLength(style.marginRight);
                case UIStyleProperty.MarginBottom:
                    return FormatStyleLength(style.marginBottom);
                case UIStyleProperty.MarginLeft:
                    return FormatStyleLength(style.marginLeft);

                // Padding Properties
                case UIStyleProperty.PaddingTop:
                    return FormatStyleLength(style.paddingTop);
                case UIStyleProperty.PaddingRight:
                    return FormatStyleLength(style.paddingRight);
                case UIStyleProperty.PaddingBottom:
                    return FormatStyleLength(style.paddingBottom);
                case UIStyleProperty.PaddingLeft:
                    return FormatStyleLength(style.paddingLeft);

                // Border Properties
                case UIStyleProperty.BorderTopWidth:
                    return style.borderTopWidth.ToString();
                case UIStyleProperty.BorderRightWidth:
                    return style.borderRightWidth.ToString();
                case UIStyleProperty.BorderBottomWidth:
                    return style.borderBottomWidth.ToString();
                case UIStyleProperty.BorderLeftWidth:
                    return style.borderLeftWidth.ToString();
                case UIStyleProperty.BorderTopLeftRadius:
                    return FormatStyleLength(style.borderTopLeftRadius);
                case UIStyleProperty.BorderTopRightRadius:
                    return FormatStyleLength(style.borderTopRightRadius);
                case UIStyleProperty.BorderBottomLeftRadius:
                    return FormatStyleLength(style.borderBottomLeftRadius);
                case UIStyleProperty.BorderBottomRightRadius:
                    return FormatStyleLength(style.borderBottomRightRadius);

                // Border Color Properties
                case UIStyleProperty.BorderTopColor:
                    return ColorUtility.ToHtmlStringRGBA(style.borderTopColor);
                case UIStyleProperty.BorderRightColor:
                    return ColorUtility.ToHtmlStringRGBA(style.borderRightColor);
                case UIStyleProperty.BorderBottomColor:
                    return ColorUtility.ToHtmlStringRGBA(style.borderBottomColor);
                case UIStyleProperty.BorderLeftColor:
                    return ColorUtility.ToHtmlStringRGBA(style.borderLeftColor);

                // Background Properties
                case UIStyleProperty.BackgroundColor:
                    return "#" + ColorUtility.ToHtmlStringRGBA(style.backgroundColor);

                // Color Properties
                case UIStyleProperty.Color:
                    return "#" + ColorUtility.ToHtmlStringRGBA(style.color);
                case UIStyleProperty.Opacity:
                    return style.opacity.ToString();

                // Text Properties
                case UIStyleProperty.FontSize:
                    return FormatStyleLength(style.fontSize);
                case UIStyleProperty.FontStyle:
                    return style.unityFontStyleAndWeight.ToString().ToLower();
                case UIStyleProperty.FontWeight:
                    return style.unityFontStyleAndWeight.ToString().ToLower();
                case UIStyleProperty.UnityTextAlign:
                    return FormatTextAnchor(style.unityTextAlign);
                case UIStyleProperty.WhiteSpace:
                    return style.whiteSpace.ToString().ToLower();

                // Display Properties
                case UIStyleProperty.Display:
                    return style.display.ToString().ToLower();
                case UIStyleProperty.Visibility:
                    return style.visibility.ToString().ToLower();

                default:
                    Debug.LogWarning($"[UIElementBridge] Unsupported get style property: {property}");
                    return "";
            }
        }

        private string FormatStyleLength(StyleLength length)
        {
            if (length.keyword != StyleKeyword.Undefined && length.keyword != StyleKeyword.Null)
            {
                return length.keyword.ToString().ToLower();
            }

            var value = length.value;
            if (value.unit == LengthUnit.Pixel)
                return $"{value.value}px";
            else if (value.unit == LengthUnit.Percent)
                return $"{value.value}%";
            else
                return value.value.ToString();
        }

        private string FormatStyleFloat(StyleFloat styleFloat)
        {
            if (styleFloat.keyword != StyleKeyword.Undefined && styleFloat.keyword != StyleKeyword.Null)
            {
                return styleFloat.keyword.ToString().ToLower();
            }

            return styleFloat.value.ToString();
        }

        private string FormatFlexDirection(FlexDirection direction)
        {
            return direction switch
            {
                FlexDirection.Column => "column",
                FlexDirection.ColumnReverse => "column-reverse",
                FlexDirection.Row => "row",
                FlexDirection.RowReverse => "row-reverse",
                _ => "column"
            };
        }

        private string FormatWrap(Wrap wrap)
        {
            return wrap switch
            {
                Wrap.NoWrap => "nowrap",
                Wrap.Wrap => "wrap",
                Wrap.WrapReverse => "wrap-reverse",
                _ => "nowrap"
            };
        }

        private string FormatJustify(Justify justify)
        {
            return justify switch
            {
                Justify.FlexStart => "flex-start",
                Justify.FlexEnd => "flex-end",
                Justify.Center => "center",
                Justify.SpaceBetween => "space-between",
                Justify.SpaceAround => "space-around",
                _ => "flex-start"
            };
        }

        private string FormatTextAnchor(TextAnchor anchor)
        {
            return anchor switch
            {
                TextAnchor.UpperLeft => "left",
                TextAnchor.UpperCenter => "center",
                TextAnchor.UpperRight => "right",
                TextAnchor.MiddleLeft => "left",
                TextAnchor.MiddleCenter => "center",
                TextAnchor.MiddleRight => "right",
                TextAnchor.LowerLeft => "left",
                TextAnchor.LowerCenter => "center",
                TextAnchor.LowerRight => "right",
                _ => "left"
            };
        }

        private StyleLength ParseLength(string value)
        {
            if (string.IsNullOrEmpty(value))
                return new StyleLength(StyleKeyword.Initial);
            
            value = value.Trim().ToLower();
            
            // Handle CSS keywords (only those supported by Unity)
            switch (value)
            {
                case "auto":
                    return new StyleLength(StyleKeyword.Auto);
                case "initial":
                    return new StyleLength(StyleKeyword.Initial);
                case "inherit":
                    // Unity doesn't support inherit, fallback to initial
                    return new StyleLength(StyleKeyword.Initial);
                case "none":
                    return new StyleLength(StyleKeyword.None);
                case "max-content":
                case "min-content":
                case "fit-content":
                    // Unity doesn't directly support these, fallback to auto
                    return new StyleLength(StyleKeyword.Auto);
            }
            
            // Parse numeric values with units
            var match = System.Text.RegularExpressions.Regex.Match(value, @"^(-?\d*\.?\d+)(px|%|em|rem|vw|vh|vmin|vmax|cm|mm|in|pt|pc)?$");
            if (match.Success)
            {
                var numericValue = float.Parse(match.Groups[1].Value);
                var unit = match.Groups[2].Value;
                
                switch (unit)
                {
                    case "":
                    case "px":
                        return new StyleLength(numericValue);
                    
                    case "%":
                        return new StyleLength(Length.Percent(numericValue));
                    
                    case "em":
                    case "rem":
                        // Convert em/rem to pixels (assuming 16px base font size)
                        return new StyleLength(numericValue * 16f);
                    
                    case "vw":
                        // Viewport width percentage (assuming 1920px viewport)
                        return new StyleLength(numericValue * 1920f / 100f);
                    
                    case "vh":
                        // Viewport height percentage (assuming 1080px viewport)
                        return new StyleLength(numericValue * 1080f / 100f);
                    
                    case "vmin":
                        // Smaller of vw or vh
                        var minViewport = Mathf.Min(1920f, 1080f);
                        return new StyleLength(numericValue * minViewport / 100f);
                    
                    case "vmax":
                        // Larger of vw or vh
                        var maxViewport = Mathf.Max(1920f, 1080f);
                        return new StyleLength(numericValue * maxViewport / 100f);
                    
                    case "cm":
                        // Centimeters to pixels (96 DPI)
                        return new StyleLength(numericValue * 37.795f);
                    
                    case "mm":
                        // Millimeters to pixels
                        return new StyleLength(numericValue * 3.7795f);
                    
                    case "in":
                        // Inches to pixels
                        return new StyleLength(numericValue * 96f);
                    
                    case "pt":
                        // Points to pixels
                        return new StyleLength(numericValue * 1.333f);
                    
                    case "pc":
                        // Picas to pixels
                        return new StyleLength(numericValue * 16f);
                    
                    default:
                        return new StyleLength(numericValue);
                }
            }
            
            // Fallback: try to parse as float
            if (float.TryParse(value, out float fallbackValue))
            {
                return new StyleLength(fallbackValue);
            }
            
            return new StyleLength(StyleKeyword.Initial);
        }
        
        private StyleColor ParseColor(string value)
        {
            if (string.IsNullOrEmpty(value))
                return new StyleColor(StyleKeyword.Initial);
            
            value = value.Trim().ToLower();
            
            // Handle CSS keywords (only those supported by Unity)
            switch (value)
            {
                case "transparent":
                    return new StyleColor(new Color(0, 0, 0, 0));
                case "initial":
                    return new StyleColor(StyleKeyword.Initial);
                case "inherit":
                    // Unity doesn't support inherit, fallback to initial
                    return new StyleColor(StyleKeyword.Initial);
                case "currentcolor":
                    return new StyleColor(StyleKeyword.Initial); // Fallback
            }
            
            // Handle named colors
            Color namedColor = ParseNamedColor(value);
            if (namedColor != Color.clear)
            {
                return new StyleColor(namedColor);
            }
            
            // Handle hex colors (#RGB, #RRGGBB)
            if (ColorUtility.TryParseHtmlString(value, out Color hexColor))
            {
                return new StyleColor(hexColor);
            }
            
            // Handle rgba() format
            var rgbaMatch = System.Text.RegularExpressions.Regex.Match(value, @"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+))?\s*\)");
            if (rgbaMatch.Success)
            {
                var r = int.Parse(rgbaMatch.Groups[1].Value) / 255f;
                var g = int.Parse(rgbaMatch.Groups[2].Value) / 255f;
                var b = int.Parse(rgbaMatch.Groups[3].Value) / 255f;
                var a = rgbaMatch.Groups[4].Success ? float.Parse(rgbaMatch.Groups[4].Value) : 1f;
                return new StyleColor(new Color(r, g, b, a));
            }
            
            // Handle hsla() format (basic conversion)
            var hslaMatch = System.Text.RegularExpressions.Regex.Match(value, @"hsla?\(\s*(\d+)\s*,\s*(\d+)%\s*,\s*(\d+)%\s*(?:,\s*([\d.]+))?\s*\)");
            if (hslaMatch.Success)
            {
                var h = int.Parse(hslaMatch.Groups[1].Value) / 360f;
                var s = int.Parse(hslaMatch.Groups[2].Value) / 100f;
                var l = int.Parse(hslaMatch.Groups[3].Value) / 100f;
                var a = hslaMatch.Groups[4].Success ? float.Parse(hslaMatch.Groups[4].Value) : 1f;
                
                // Convert HSL to RGB
                var rgbColor = HSLToRGB(h, s, l);
                rgbColor.a = a;
                return new StyleColor(rgbColor);
            }
            
            // Fallback to white
            return new StyleColor(Color.white);
        }
        
        private Color ParseNamedColor(string colorName)
        {
            return colorName switch
            {
                "red" => Color.red,
                "green" => Color.green,
                "blue" => Color.blue,
                "white" => Color.white,
                "black" => Color.black,
                "yellow" => Color.yellow,
                "cyan" => Color.cyan,
                "magenta" => Color.magenta,
                "orange" => new Color(1f, 0.5f, 0f, 1f),
                "purple" => new Color(0.5f, 0f, 0.5f, 1f),
                "pink" => new Color(1f, 0.75f, 0.8f, 1f),
                "brown" => new Color(0.6f, 0.3f, 0f, 1f),
                "gray" or "grey" => Color.gray,
                "silver" => new Color(0.75f, 0.75f, 0.75f, 1f),
                "gold" => new Color(1f, 0.84f, 0f, 1f),
                _ => Color.clear // Indicates not found
            };
        }
        
        private Color HSLToRGB(float h, float s, float l)
        {
            float r, g, b;
            
            if (s == 0f)
            {
                r = g = b = l; // Achromatic
            }
            else
            {
                float hue2rgb(float p, float q, float t)
                {
                    if (t < 0f) t += 1f;
                    if (t > 1f) t -= 1f;
                    if (t < 1f/6f) return p + (q - p) * 6f * t;
                    if (t < 1f/2f) return q;
                    if (t < 2f/3f) return p + (q - p) * (2f/3f - t) * 6f;
                    return p;
                }
                
                var q = l < 0.5f ? l * (1f + s) : l + s - l * s;
                var p = 2f * l - q;
                r = hue2rgb(p, q, h + 1f/3f);
                g = hue2rgb(p, q, h);
                b = hue2rgb(p, q, h - 1f/3f);
            }
            
            return new Color(r, g, b, 1f);
        }
        
        /// <summary>
        /// Sets background image from URL or CSS value
        /// </summary>
        private async void SetBackgroundImage(VisualElement element, string value)
        {
            if (string.IsNullOrEmpty(value) || value.ToLower() == "none")
            {
                // Clear background image
                element.style.backgroundImage = new StyleBackground(StyleKeyword.None);
                return;
            }
            
            // Parse CSS url() format: url("https://example.com/image.png")
            string imageUrl = ParseBackgroundImageUrl(value);
            if (string.IsNullOrEmpty(imageUrl))
            {
                Debug.LogWarning($"[UIElementBridge] Invalid background-image value: {value}");
                return;
            }
            
            try
            {
                var texture = await GetOrDownloadTexture(imageUrl);
                if (texture != null)
                {
                    element.style.backgroundImage = new StyleBackground(texture);
                    LogVerbose($"Set background image from URL: {imageUrl}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIElementBridge] Failed to load background image from {imageUrl}: {e.Message}");
            }
        }
        
        /// <summary>
        /// Parses background-image CSS value to extract URL
        /// </summary>
        private string ParseBackgroundImageUrl(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            
            // Handle url("...") format
            var urlMatch = System.Text.RegularExpressions.Regex.Match(value, @"url\s*\(\s*['""]?([^'""()]+)['""]?\s*\)");
            if (urlMatch.Success)
            {
                return urlMatch.Groups[1].Value.Trim();
            }
            
            // Handle direct URL (fallback)
            if (value.StartsWith("http://") || value.StartsWith("https://") || value.StartsWith("data:"))
            {
                return value.Trim();
            }
            
            return null;
        }
        
        /// <summary>
        /// Gets texture from cache or downloads it using Get.Texture
        /// </summary>
        private static async Task<Texture2D> GetOrDownloadTexture(string url)
        {
            // Check cache first
            if (_textureCache.TryGetValue(url, out var cachedTexture))
            {
                return cachedTexture;
            }
            
            // Check if already downloading
            if (_downloadingTextures.TryGetValue(url, out var downloadingTask))
            {
                return await downloadingTask;
            }
            
            // Start download
            var downloadTask = DownloadTexture(url);
            _downloadingTextures[url] = downloadTask;
            
            try
            {
                var texture = await downloadTask;
                
                // Cache the result
                if (texture != null)
                {
                    _textureCache[url] = texture;
                }
                
                return texture;
            }
            finally
            {
                // Remove from downloading queue
                _downloadingTextures.Remove(url);
            }
        }
        
        /// <summary>
        /// Downloads texture using the existing Get.Texture method
        /// </summary>
        private static async Task<Texture2D> DownloadTexture(string url)
        {
            try
            {
                LogVerbose($"Downloading texture from: {url}");
                var texture = await Get.Texture(url);
                return texture;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIElementBridge] Failed to download texture from {url}: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Clears the texture cache to free memory
        /// </summary>
        public static void ClearTextureCache()
        {
            foreach (var texture in _textureCache.Values)
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
            _textureCache.Clear();
            _downloadingTextures.Clear();
            LogVerbose("Cleared texture cache");
        }
        
        private float[] ParseSpacing(string value)
        {
            if (string.IsNullOrEmpty(value))
                return new float[] { 0, 0, 0, 0 };
            
            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            // Parse each part as a length and extract the numeric value
            float[] ParsedValues = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var styleLength = ParseLength(parts[i]);
                // Extract the float value from StyleLength
                if (styleLength.keyword == StyleKeyword.Auto || 
                    styleLength.keyword == StyleKeyword.Initial)
                {
                    ParsedValues[i] = 0f;
                }
                else
                {
                    ParsedValues[i] = styleLength.value.value;
                }
            }
            
            // Apply CSS shorthand rules: top, right, bottom, left
            if (ParsedValues.Length == 1)
            {
                // All sides the same
                var v = ParsedValues[0];
                return new[] { v, v, v, v };
            }
            else if (ParsedValues.Length == 2)
            {
                // Vertical | Horizontal
                var vertical = ParsedValues[0];
                var horizontal = ParsedValues[1];
                return new[] { vertical, horizontal, vertical, horizontal };
            }
            else if (ParsedValues.Length == 3)
            {
                // Top | Horizontal | Bottom
                var top = ParsedValues[0];
                var horizontal = ParsedValues[1];
                var bottom = ParsedValues[2];
                return new[] { top, horizontal, bottom, horizontal };
            }
            else if (ParsedValues.Length == 4)
            {
                // Top | Right | Bottom | Left
                return new[] { ParsedValues[0], ParsedValues[1], ParsedValues[2], ParsedValues[3] };
            }
            
            return new float[] { 0, 0, 0, 0 };
        }
        
        private float ParseFloat(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0f;
            
            // Remove common units
            value = value.Replace("px", "").Replace("em", "").Replace("rem", "");
            
            if (float.TryParse(value, out float result))
                return result;
                
            return 0f;
        }
        
        private StyleEnum<T> ParseEnum<T>(string value) where T : struct, System.Enum
        {
            if (string.IsNullOrEmpty(value))
                return new StyleEnum<T>(StyleKeyword.Initial);
                
            // Handle CSS keywords (only those supported by Unity)
            switch (value.ToLower())
            {
                case "auto":
                    return new StyleEnum<T>(StyleKeyword.Auto);
                case "initial":
                    return new StyleEnum<T>(StyleKeyword.Initial);
                case "none":
                    return new StyleEnum<T>(StyleKeyword.None);
                case "inherit":
                    // Unity doesn't support inherit, fallback to initial
                    return new StyleEnum<T>(StyleKeyword.Initial);
            }
            
            // Try to parse as enum
            if (System.Enum.TryParse<T>(value, true, out T enumValue))
            {
                return new StyleEnum<T>(enumValue);
            }
            
            // Handle specific enum conversions
            if (typeof(T) == typeof(Align))
            {
                return value.ToLower() switch
                {
                    "flex-start" => new StyleEnum<T>((T)(object)Align.FlexStart),
                    "flex-end" => new StyleEnum<T>((T)(object)Align.FlexEnd),
                    "center" => new StyleEnum<T>((T)(object)Align.Center),
                    "stretch" => new StyleEnum<T>((T)(object)Align.Stretch),
                    _ => new StyleEnum<T>(StyleKeyword.Initial)
                };
            }
            else if (typeof(T) == typeof(FlexDirection))
            {
                return value.ToLower() switch
                {
                    "row" => new StyleEnum<T>((T)(object)FlexDirection.Row),
                    "row-reverse" => new StyleEnum<T>((T)(object)FlexDirection.RowReverse),
                    "column" => new StyleEnum<T>((T)(object)FlexDirection.Column),
                    "column-reverse" => new StyleEnum<T>((T)(object)FlexDirection.ColumnReverse),
                    _ => new StyleEnum<T>(StyleKeyword.Initial)
                };
            }
            else if (typeof(T) == typeof(Justify))
            {
                return value.ToLower() switch
                {
                    "flex-start" => new StyleEnum<T>((T)(object)Justify.FlexStart),
                    "flex-end" => new StyleEnum<T>((T)(object)Justify.FlexEnd),
                    "center" => new StyleEnum<T>((T)(object)Justify.Center),
                    "space-between" => new StyleEnum<T>((T)(object)Justify.SpaceBetween),
                    "space-around" => new StyleEnum<T>((T)(object)Justify.SpaceAround),
                    _ => new StyleEnum<T>(StyleKeyword.Initial)
                };
            }
            else if (typeof(T) == typeof(Position))
            {
                return value.ToLower() switch
                {
                    "relative" => new StyleEnum<T>((T)(object)Position.Relative),
                    "absolute" => new StyleEnum<T>((T)(object)Position.Absolute),
                    _ => new StyleEnum<T>(StyleKeyword.Initial)
                };
            }
            else if (typeof(T) == typeof(DisplayStyle))
            {
                return value.ToLower() switch
                {
                    "flex" => new StyleEnum<T>((T)(object)DisplayStyle.Flex),
                    "none" => new StyleEnum<T>((T)(object)DisplayStyle.None),
                    _ => new StyleEnum<T>(StyleKeyword.Initial)
                };
            }
            else if (typeof(T) == typeof(Visibility))
            {
                return value.ToLower() switch
                {
                    "visible" => new StyleEnum<T>((T)(object)Visibility.Visible),
                    "hidden" => new StyleEnum<T>((T)(object)Visibility.Hidden),
                    _ => new StyleEnum<T>(StyleKeyword.Initial)
                };
            }
            else if (typeof(T) == typeof(TextAnchor))
            {
                return value.ToLower() switch
                {
                    "left" => new StyleEnum<T>((T)(object)TextAnchor.UpperLeft),
                    "center" => new StyleEnum<T>((T)(object)TextAnchor.MiddleCenter),
                    "right" => new StyleEnum<T>((T)(object)TextAnchor.UpperRight),
                    _ => new StyleEnum<T>(StyleKeyword.Initial)
                };
            }
            
            return new StyleEnum<T>(StyleKeyword.Initial);
        }
        
        private void CallMethod(string[] data)
        {
            if (data.Length < 2) return;
            
            var elementId = data[0];
            var methodName = data[1];
            var args = data.Skip(2).ToArray();
            
            if (!_elements.TryGetValue(elementId, out var element)) return;
            
            // Try to use the generated method dispatcher first
            if (element is IUIMethodDispatcher dispatcher)
            {
                try
                {
                    if (dispatcher.DispatchMethod(methodName, args))
                    {
                        LogVerbose($"Dispatched method: {methodName} on {elementId}");
                        return; // Method was handled by the dispatcher
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[UIElementBridge] Error dispatching method {methodName} on {elementId}: {e.Message}");
                    return;
                }
            }
        }
        
        private void RegisterEvent(string[] data)
        {
            if (data.Length < 2) return;

            var elementId = data[0];
            var eventTypeString = data[1];

            LogVerbose($"RegisterEvent called with elementId='{elementId}', eventType='{eventTypeString}'");

            // Resolve element name to ID if needed
            var originalElementId = elementId;
            elementId = ResolveElementIdOrName(elementId);
            LogVerbose($"After resolution: '{originalElementId}' -> '{elementId}'");

            if (!_elements.TryGetValue(elementId, out var element))
            {
                Debug.LogWarning($"[UIElementBridge] Element '{elementId}' not found in _elements dictionary. Available elements: {string.Join(", ", _elements.Keys)}");
                return;
            }

            LogVerbose($"Found element '{elementId}' of type {element.GetType().Name}");
            
            // Convert string to enum
            var eventType = UIEventTypeHelper.FromEventName(eventTypeString);
            var callbackKey = (elementId, eventType);
            
            // Check if callback is already registered
            if (_registeredCallbacks.ContainsKey(callbackKey))
            {
                Debug.LogWarning($"[UIElementBridge] Event {eventType} already registered for element {elementId}");
                return;
            }
            
            // Register Unity event callbacks that will send messages back to TypeScript
            switch (eventType)
            {
                // Basic events
                case UIEventType.Click:
                    {
                        EventCallback<ClickEvent> callback = evt => SendUIEvent(elementId, eventType, evt);
                        element.RegisterCallback(callback);
                        _registeredCallbacks[callbackKey] = callback;
                    }
                    break;
                    
                // Mouse events
                case UIEventType.MouseDown:
                    {
                        EventCallback<MouseDownEvent> callback = evt => SendUIEvent(elementId, eventType, evt);
                        element.RegisterCallback(callback);
                        _registeredCallbacks[callbackKey] = callback;
                    }
                    break;
                case UIEventType.MouseUp:
                    {
                        EventCallback<MouseUpEvent> callback = evt => SendUIEvent(elementId, eventType, evt);
                        element.RegisterCallback(callback);
                        _registeredCallbacks[callbackKey] = callback;
                    }
                    break;
                case UIEventType.MouseEnter:
                    {
                        EventCallback<MouseEnterEvent> callback = evt => SendUIEvent(elementId, eventType, evt);
                        element.RegisterCallback(callback);
                        _registeredCallbacks[callbackKey] = callback;
                    }
                    break;
                case UIEventType.MouseLeave:
                    {
                        EventCallback<MouseLeaveEvent> callback = evt => SendUIEvent(elementId, eventType, evt);
                        element.RegisterCallback(callback);
                        _registeredCallbacks[callbackKey] = callback;
                    }
                    break;
                case UIEventType.MouseMove:
                    {
                        EventCallback<MouseMoveEvent> callback = evt => SendUIEvent(elementId, eventType, evt);
                        element.RegisterCallback(callback);
                        _registeredCallbacks[callbackKey] = callback;
                    }
                    break;
                case UIEventType.MouseOver:
                    {
                        EventCallback<MouseOverEvent> callback = evt => SendUIEvent(elementId, eventType, evt);
                        element.RegisterCallback(callback);
                        _registeredCallbacks[callbackKey] = callback;
                    }
                    break;
                case UIEventType.MouseOut:
                    {
                        EventCallback<MouseOutEvent> callback = evt => SendUIEvent(elementId, eventType, evt);
                        element.RegisterCallback(callback);
                        _registeredCallbacks[callbackKey] = callback;
                    }
                    break;
                case UIEventType.Wheel:
                    {
                        EventCallback<WheelEvent> callback = evt => SendUIEvent(elementId, eventType, evt);
                        element.RegisterCallback(callback);
                        _registeredCallbacks[callbackKey] = callback;
                    }
                    break;
                    
                // Keyboard events
                case UIEventType.KeyDown:
                    {
                        EventCallback<KeyDownEvent> callback = evt => SendUIEvent(elementId, eventType, evt);
                        element.RegisterCallback(callback);
                        _registeredCallbacks[callbackKey] = callback;
                    }
                    break;
                case UIEventType.KeyUp:
                    {
                        EventCallback<KeyUpEvent> callback = evt => SendUIEvent(elementId, eventType, evt);
                        element.RegisterCallback(callback);
                        _registeredCallbacks[callbackKey] = callback;
                    }
                    break;
                    
                // Focus events
                case UIEventType.Focus:
                    {
                        EventCallback<FocusEvent> callback = evt => SendUIEvent(elementId, eventType, evt);
                        element.RegisterCallback(callback);
                        _registeredCallbacks[callbackKey] = callback;
                    }
                    break;
                case UIEventType.Blur:
                    {
                        EventCallback<BlurEvent> callback = evt => SendUIEvent(elementId, eventType, evt);
                        element.RegisterCallback(callback);
                        _registeredCallbacks[callbackKey] = callback;
                    }
                    break;
                case UIEventType.FocusIn:
                    {
                        EventCallback<FocusInEvent> callback = evt => SendUIEvent(elementId, eventType, evt);
                        element.RegisterCallback(callback);
                        _registeredCallbacks[callbackKey] = callback;
                    }
                    break;
                case UIEventType.FocusOut:
                    {
                        EventCallback<FocusOutEvent> callback = evt => SendUIEvent(elementId, eventType, evt);
                        element.RegisterCallback(callback);
                        _registeredCallbacks[callbackKey] = callback;
                    }
                    break;
                    
                // Input events
                case UIEventType.Change:
                    {
                        // Register multiple change event types based on element type
                        if (element is TextField || element is DropdownField)
                        {
                            EventCallback<ChangeEvent<string>> stringCallback = evt => SendUIEvent(elementId, eventType, evt);
                            element.RegisterCallback(stringCallback);
                            _registeredCallbacks[callbackKey] = stringCallback;
                        }
                        else if (element is Slider)
                        {
                            EventCallback<ChangeEvent<float>> floatCallback = evt => SendUIEvent(elementId, eventType, evt);
                            element.RegisterCallback(floatCallback);
                            _registeredCallbacks[callbackKey] = floatCallback;
                        }
                        else if (element is Toggle || element is RadioButton)
                        {
                            EventCallback<ChangeEvent<bool>> boolCallback = evt => SendUIEvent(elementId, eventType, evt);
                            element.RegisterCallback(boolCallback);
                            _registeredCallbacks[callbackKey] = boolCallback;
                        }
                        else if (element is SliderInt || element is RadioButtonGroup)
                        {
                            EventCallback<ChangeEvent<int>> intCallback = evt => SendUIEvent(elementId, eventType, evt);
                            element.RegisterCallback(intCallback);
                            _registeredCallbacks[callbackKey] = intCallback;
                        }
                        else if (element is MinMaxSlider)
                        {
                            EventCallback<ChangeEvent<Vector2>> vector2Callback = evt => SendUIEvent(elementId, eventType, evt);
                            element.RegisterCallback(vector2Callback);
                            _registeredCallbacks[callbackKey] = vector2Callback;
                        }
                        else
                        {
                            // Fallback to string type for unknown elements
                            EventCallback<ChangeEvent<string>> callback = evt => SendUIEvent(elementId, eventType, evt);
                            element.RegisterCallback(callback);
                            _registeredCallbacks[callbackKey] = callback;
                        }
                    }
                    break;
                 
                default:
                    Debug.LogWarning($"[UIElementBridge] Unsupported event type: {eventType}");
                    break;
            }
            
            LogVerbose($"Registered {eventType} event for element {elementId}");
        }
        
        private void UnregisterEvent(string[] data)
        {
            if (data.Length < 2) return;
            
            var elementId = data[0];
            var eventTypeString = data[1];
            
            if (!_elements.TryGetValue(elementId, out var element)) return;
            
            // Convert string to enum
            var eventType = UIEventTypeHelper.FromEventName(eventTypeString);
            var callbackKey = (elementId, eventType);
            
            // Check if callback is registered
            if (!_registeredCallbacks.TryGetValue(callbackKey, out var callback))
            {
                Debug.LogWarning($"[UIElementBridge] No registered callback found for {eventType} event on element {elementId}");
                return;
            }
            
            // Unregister Unity event callbacks based on event type
            switch (eventType)
            {
                // Basic events
                case UIEventType.Click:
                    if (callback is EventCallback<ClickEvent> clickCallback)
                        element.UnregisterCallback(clickCallback);
                    break;
                    
                // Mouse events
                case UIEventType.MouseDown:
                    if (callback is EventCallback<MouseDownEvent> mouseDownCallback)
                        element.UnregisterCallback(mouseDownCallback);
                    break;
                case UIEventType.MouseUp:
                    if (callback is EventCallback<MouseUpEvent> mouseUpCallback)
                        element.UnregisterCallback(mouseUpCallback);
                    break;
                case UIEventType.MouseEnter:
                    if (callback is EventCallback<MouseEnterEvent> mouseEnterCallback)
                        element.UnregisterCallback(mouseEnterCallback);
                    break;
                case UIEventType.MouseLeave:
                    if (callback is EventCallback<MouseLeaveEvent> mouseLeaveCallback)
                        element.UnregisterCallback(mouseLeaveCallback);
                    break;
                case UIEventType.MouseMove:
                    if (callback is EventCallback<MouseMoveEvent> mouseMoveCallback)
                        element.UnregisterCallback(mouseMoveCallback);
                    break;
                case UIEventType.MouseOver:
                    if (callback is EventCallback<MouseOverEvent> mouseOverCallback)
                        element.UnregisterCallback(mouseOverCallback);
                    break;
                case UIEventType.MouseOut:
                    if (callback is EventCallback<MouseOutEvent> mouseOutCallback)
                        element.UnregisterCallback(mouseOutCallback);
                    break;
                case UIEventType.Wheel:
                    if (callback is EventCallback<WheelEvent> wheelCallback)
                        element.UnregisterCallback(wheelCallback);
                    break;
                    
                // Keyboard events
                case UIEventType.KeyDown:
                    if (callback is EventCallback<KeyDownEvent> keyDownCallback)
                        element.UnregisterCallback(keyDownCallback);
                    break;
                case UIEventType.KeyUp:
                    if (callback is EventCallback<KeyUpEvent> keyUpCallback)
                        element.UnregisterCallback(keyUpCallback);
                    break;
                    
                // Focus events
                case UIEventType.Focus:
                    if (callback is EventCallback<FocusEvent> focusCallback)
                        element.UnregisterCallback(focusCallback);
                    break;
                case UIEventType.Blur:
                    if (callback is EventCallback<BlurEvent> blurCallback)
                        element.UnregisterCallback(blurCallback);
                    break;
                case UIEventType.FocusIn:
                    if (callback is EventCallback<FocusInEvent> focusInCallback)
                        element.UnregisterCallback(focusInCallback);
                    break;
                case UIEventType.FocusOut:
                    if (callback is EventCallback<FocusOutEvent> focusOutCallback)
                        element.UnregisterCallback(focusOutCallback);
                    break;
                    
                // Input events
                case UIEventType.Change:
                    // Try to unregister different change event types
                    if (callback is EventCallback<ChangeEvent<string>> stringChangeCallback)
                        element.UnregisterCallback(stringChangeCallback);
                    else if (callback is EventCallback<ChangeEvent<float>> floatChangeCallback)
                        element.UnregisterCallback(floatChangeCallback);
                    else if (callback is EventCallback<ChangeEvent<bool>> boolChangeCallback)
                        element.UnregisterCallback(boolChangeCallback);
                    else if (callback is EventCallback<ChangeEvent<int>> intChangeCallback)
                        element.UnregisterCallback(intChangeCallback);
                    else if (callback is EventCallback<ChangeEvent<Vector2>> vector2ChangeCallback)
                        element.UnregisterCallback(vector2ChangeCallback);
                    break;
                    
                    
                default:
                    Debug.LogWarning($"[UIElementBridge] Unsupported event type for unregistration: {eventType}");
                    break;
            }
            
            // Remove from stored callbacks
            _registeredCallbacks.Remove(callbackKey);
            LogVerbose($"Unregistered {eventType} event for element {elementId}");
        }
        
        private void SetFocus(string[] data)
        {
            if (data.Length < 1) return;
            
            var elementId = data[0];
            if (_elements.TryGetValue(elementId, out var element))
            {
                element.Focus();
            }
        }
        
        private void ClearFocus(string[] data)
        {
            if (data.Length < 1) return;
            
            var elementId = data[0];
            if (_elements.TryGetValue(elementId, out var element))
            {
                element.Blur();
            }
        }
        
        private void ProcessBatchUpdate(string[] data)
        {
            // Process multiple updates in a single frame
            foreach (var update in data)
            {
                var updateParts = update.Split(MessageDelimiters.TERTIARY);
                if (updateParts.Length >= 2)
                {
                    ProcessCommand(updateParts[0], updateParts.Skip(1).ToArray());
                }
            }
        }
        
        private void SendUIEvent(string elementId, UIEventType eventType, EventBase evt)
        {
            LogVerbose($"SendUIEvent called for element '{elementId}', eventType '{eventType}', evt type {evt.GetType().Name}");

            // Send event back to TypeScript
            var eventTypeName = eventType.ToEventName();

            // Build event data as JSON object based on event type
            var eventDataJson = BuildEventDataJson(evt);

            var message = $"{UICommands.UI_EVENT}{MessageDelimiters.PRIMARY}{elementId}{MessageDelimiters.PRIMARY}{eventTypeName}{MessageDelimiters.PRIMARY}{eventDataJson}";

            // Send UI events directly to TypeScript without panel ID prefix
            // TypeScript doesn't need panel ID for element routing
            SendToJavaScript(message);

            // Trigger generic OnUIEvent for all event types (for OnUIEvent visual scripting node)
            var eventPrefix = ConvertEventTypeToPrefix(eventType);
            var genericEventName = $"{eventPrefix}_{elementId}";
            LogVerbose($"Triggering generic OnUIEvent with name '{genericEventName}'");
            Unity.VisualScripting.EventBus.Trigger("OnUIEvent", new Unity.VisualScripting.CustomEventArgs(genericEventName, new object[] { evt }));

            // Also trigger specific EventBus events for Visual Scripting (for specialized nodes like OnUIClick)
            TriggerVisualScriptingEvent(elementId, eventType, evt);
        }

        /// <summary>
        /// Converts UIEventType to the event prefix format used in event names (e.g., Click -> UIClick)
        /// </summary>
        private string ConvertEventTypeToPrefix(UIEventType eventType)
        {
            return eventType switch
            {
                UIEventType.Click => "UIClick",
                UIEventType.Change => "UIChange",
                UIEventType.MouseDown => "UIMouseDown",
                UIEventType.MouseUp => "UIMouseUp",
                UIEventType.MouseMove => "UIMouseMove",
                UIEventType.MouseEnter => "UIMouseEnter",
                UIEventType.MouseLeave => "UIMouseLeave",
                UIEventType.Focus => "UIFocus",
                UIEventType.Blur => "UIBlur",
                UIEventType.KeyDown => "UIKeyDown",
                UIEventType.KeyUp => "UIKeyUp",
                _ => $"UI{eventType}"
            };
        }
        
        private void TriggerVisualScriptingEvent(string elementId, UIEventType eventType, EventBase evt)
        {
            switch (eventType)
            {
                case UIEventType.Click:
                    if (evt is ClickEvent clickEvt)
                    {
                        var eventName = $"UIClick_{elementId}";
                        var mousePosition = new UnityEngine.Vector2(clickEvt.localPosition.x, clickEvt.localPosition.y);
                        var mouseButton = clickEvt.button;
                        
                        Unity.VisualScripting.EventBus.Trigger("OnUIClick", new Unity.VisualScripting.CustomEventArgs(eventName, new object[] { mousePosition, mouseButton }));
                        
                        LogVerbose($"Triggered OnUIClick event for element {elementId} at position {mousePosition} with button {mouseButton}");
                    }
                    break;
                    
                case UIEventType.Change:
                    if (evt is ChangeEvent<string> changeEvt)
                    {
                        var eventName = $"UIChange_{elementId}";
                        var newValue = changeEvt.newValue;
                        var oldValue = changeEvt.previousValue;

                        Unity.VisualScripting.EventBus.Trigger("OnUIChange", new Unity.VisualScripting.CustomEventArgs(eventName, new object[] { newValue, oldValue }));

                        LogVerbose($"Triggered OnUIChange event for element {elementId}: '{oldValue}' -> '{newValue}'");
                    }
                    // Handle other change event types
                    else if (evt is ChangeEvent<float> floatChangeEvt)
                    {
                        var eventName = $"UIChange_{elementId}";
                        var newValue = floatChangeEvt.newValue;
                        var oldValue = floatChangeEvt.previousValue;

                        Unity.VisualScripting.EventBus.Trigger("OnUIChange", new Unity.VisualScripting.CustomEventArgs(eventName, new object[] { newValue, oldValue }));

                        LogVerbose($"Triggered OnUIChange event for element {elementId}: {oldValue} -> {newValue}");
                    }
                    else if (evt is ChangeEvent<bool> boolChangeEvt)
                    {
                        var eventName = $"UIChange_{elementId}";
                        var newValue = boolChangeEvt.newValue ? "1" : "0";
                        var oldValue = boolChangeEvt.previousValue ? "1" : "0";

                        LogVerbose($"About to trigger OnUIChange event for bool element {elementId}: {oldValue} -> {newValue}, eventName: '{eventName}'");
                        Unity.VisualScripting.EventBus.Trigger("OnUIChange", new Unity.VisualScripting.CustomEventArgs(eventName, new object[] { newValue, oldValue }));
                        LogVerbose($"EventBus.Trigger completed for OnUIChange event");
                    }
                    else if (evt is ChangeEvent<int> intChangeEvt)
                    {
                        var eventName = $"UIChange_{elementId}";
                        var newValue = intChangeEvt.newValue;
                        var oldValue = intChangeEvt.previousValue;

                        Unity.VisualScripting.EventBus.Trigger("OnUIChange", new Unity.VisualScripting.CustomEventArgs(eventName, new object[] { newValue, oldValue }));

                        LogVerbose($"Triggered OnUIChange event for element {elementId}: {oldValue} -> {newValue}");
                    }
                    else if (evt is ChangeEvent<Vector2> vector2ChangeEvt)
                    {
                        var eventName = $"UIChange_{elementId}";
                        var newValue = vector2ChangeEvt.newValue;
                        var oldValue = vector2ChangeEvt.previousValue;

                        Unity.VisualScripting.EventBus.Trigger("OnUIChange", new Unity.VisualScripting.CustomEventArgs(eventName, new object[] { newValue, oldValue }));

                        LogVerbose($"Triggered OnUIChange event for element {elementId}: ({oldValue.x},{oldValue.y}) -> ({newValue.x},{newValue.y})");
                    }
                    break;

                // Mouse events
                case UIEventType.MouseDown:
                    if (evt is MouseDownEvent mouseDownEvt)
                    {
                        var eventName = $"UIMouseDown_{elementId}";
                        var mousePosition = new Vector2(mouseDownEvt.localMousePosition.x, mouseDownEvt.localMousePosition.y);
                        var mouseButton = mouseDownEvt.button;

                        Unity.VisualScripting.EventBus.Trigger("OnUIMouseEvent",
                            new Unity.VisualScripting.CustomEventArgs(eventName, new object[] { mousePosition, mouseButton }));

                        LogVerbose($"Triggered OnUIMouseEvent (MouseDown) for element {elementId} at position {mousePosition}");
                    }
                    break;

                case UIEventType.MouseUp:
                    if (evt is MouseUpEvent mouseUpEvt)
                    {
                        var eventName = $"UIMouseUp_{elementId}";
                        var mousePosition = new Vector2(mouseUpEvt.localMousePosition.x, mouseUpEvt.localMousePosition.y);
                        var mouseButton = mouseUpEvt.button;

                        Unity.VisualScripting.EventBus.Trigger("OnUIMouseEvent",
                            new Unity.VisualScripting.CustomEventArgs(eventName, new object[] { mousePosition, mouseButton }));

                        LogVerbose($"Triggered OnUIMouseEvent (MouseUp) for element {elementId} at position {mousePosition}");
                    }
                    break;

                case UIEventType.MouseMove:
                    if (evt is MouseMoveEvent mouseMoveEvt)
                    {
                        var eventName = $"UIMouseMove_{elementId}";
                        var mousePosition = new Vector2(mouseMoveEvt.localMousePosition.x, mouseMoveEvt.localMousePosition.y);
                        var mouseButton = mouseMoveEvt.button;

                        Unity.VisualScripting.EventBus.Trigger("OnUIMouseEvent",
                            new Unity.VisualScripting.CustomEventArgs(eventName, new object[] { mousePosition, mouseButton }));

                        LogVerbose($"Triggered OnUIMouseEvent (MouseMove) for element {elementId} at position {mousePosition}");
                    }
                    break;

                case UIEventType.MouseEnter:
                    if (evt is MouseEnterEvent mouseEnterEvt)
                    {
                        var eventName = $"UIMouseEnter_{elementId}";
                        var mousePosition = new Vector2(mouseEnterEvt.localMousePosition.x, mouseEnterEvt.localMousePosition.y);
                        var mouseButton = mouseEnterEvt.button;

                        Unity.VisualScripting.EventBus.Trigger("OnUIMouseEvent",
                            new Unity.VisualScripting.CustomEventArgs(eventName, new object[] { mousePosition, mouseButton }));

                        LogVerbose($"Triggered OnUIMouseEvent (MouseEnter) for element {elementId} at position {mousePosition}");
                    }
                    break;

                case UIEventType.MouseLeave:
                    if (evt is MouseLeaveEvent mouseLeaveEvt)
                    {
                        var eventName = $"UIMouseLeave_{elementId}";
                        var mousePosition = new Vector2(mouseLeaveEvt.localMousePosition.x, mouseLeaveEvt.localMousePosition.y);
                        var mouseButton = mouseLeaveEvt.button;

                        Unity.VisualScripting.EventBus.Trigger("OnUIMouseEvent",
                            new Unity.VisualScripting.CustomEventArgs(eventName, new object[] { mousePosition, mouseButton }));

                        LogVerbose($"Triggered OnUIMouseEvent (MouseLeave) for element {elementId} at position {mousePosition}");
                    }
                    break;

                case UIEventType.MouseOver:
                    if (evt is MouseOverEvent mouseOverEvt)
                    {
                        var eventName = $"UIMouseOver_{elementId}";
                        var mousePosition = new Vector2(mouseOverEvt.localMousePosition.x, mouseOverEvt.localMousePosition.y);
                        var mouseButton = mouseOverEvt.button;

                        Unity.VisualScripting.EventBus.Trigger("OnUIMouseEvent",
                            new Unity.VisualScripting.CustomEventArgs(eventName, new object[] { mousePosition, mouseButton }));

                        LogVerbose($"Triggered OnUIMouseEvent (MouseOver) for element {elementId} at position {mousePosition}");
                    }
                    break;

                case UIEventType.MouseOut:
                    if (evt is MouseOutEvent mouseOutEvt)
                    {
                        var eventName = $"UIMouseOut_{elementId}";
                        var mousePosition = new Vector2(mouseOutEvt.localMousePosition.x, mouseOutEvt.localMousePosition.y);
                        var mouseButton = mouseOutEvt.button;

                        Unity.VisualScripting.EventBus.Trigger("OnUIMouseEvent",
                            new Unity.VisualScripting.CustomEventArgs(eventName, new object[] { mousePosition, mouseButton }));

                        LogVerbose($"Triggered OnUIMouseEvent (MouseOut) for element {elementId} at position {mousePosition}");
                    }
                    break;

                // Add more event types as needed
                default:
                    // Don't trigger Visual Scripting events for unsupported types
                    break;
            }
        }
        
        private string BuildEventDataJson(EventBase evt)
        {
            // Build JSON manually for better control and Unity compatibility
            var jsonBuilder = new System.Text.StringBuilder();
            jsonBuilder.Append("{");
            
            // Add common event properties
            jsonBuilder.Append($"\"timestamp\":{evt.timestamp},");
            jsonBuilder.Append($"\"eventTypeId\":{evt.eventTypeId}");
            
            // Add event-specific data based on event type
            switch (evt)
            {
                case ChangeEvent<string> changeEvt:
                    jsonBuilder.Append($",\"newValue\":\"{EscapeJsonString(changeEvt.newValue)}\"");
                    jsonBuilder.Append($",\"previousValue\":\"{EscapeJsonString(changeEvt.previousValue)}\"");
                    break;
                    
                case KeyDownEvent keyDown:
                    jsonBuilder.Append($",\"keyCode\":{(int)keyDown.keyCode}");
                    jsonBuilder.Append($",\"character\":\"{EscapeJsonString(keyDown.character.ToString())}\"");
                    jsonBuilder.Append($",\"altKey\":{keyDown.altKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"ctrlKey\":{keyDown.ctrlKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"shiftKey\":{keyDown.shiftKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"commandKey\":{keyDown.commandKey.ToString().ToLower()}");
                    break;
                    
                case KeyUpEvent keyUp:
                    jsonBuilder.Append($",\"keyCode\":{(int)keyUp.keyCode}");
                    jsonBuilder.Append($",\"character\":\"{EscapeJsonString(keyUp.character.ToString())}\"");
                    jsonBuilder.Append($",\"altKey\":{keyUp.altKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"ctrlKey\":{keyUp.ctrlKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"shiftKey\":{keyUp.shiftKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"commandKey\":{keyUp.commandKey.ToString().ToLower()}");
                    break;
                    
                case MouseDownEvent mouseDown:
                    jsonBuilder.Append($",\"localMousePosition\":{{\"x\":{mouseDown.localMousePosition.x},\"y\":{mouseDown.localMousePosition.y}}}");
                    jsonBuilder.Append($",\"mousePosition\":{{\"x\":{mouseDown.mousePosition.x},\"y\":{mouseDown.mousePosition.y}}}");
                    jsonBuilder.Append($",\"button\":{mouseDown.button}");
                    jsonBuilder.Append($",\"clickCount\":{mouseDown.clickCount}");
                    jsonBuilder.Append($",\"altKey\":{mouseDown.altKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"ctrlKey\":{mouseDown.ctrlKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"shiftKey\":{mouseDown.shiftKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"commandKey\":{mouseDown.commandKey.ToString().ToLower()}");
                    break;
                    
                case MouseUpEvent mouseUp:
                    jsonBuilder.Append($",\"localMousePosition\":{{\"x\":{mouseUp.localMousePosition.x},\"y\":{mouseUp.localMousePosition.y}}}");
                    jsonBuilder.Append($",\"mousePosition\":{{\"x\":{mouseUp.mousePosition.x},\"y\":{mouseUp.mousePosition.y}}}");
                    jsonBuilder.Append($",\"button\":{mouseUp.button}");
                    jsonBuilder.Append($",\"altKey\":{mouseUp.altKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"ctrlKey\":{mouseUp.ctrlKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"shiftKey\":{mouseUp.shiftKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"commandKey\":{mouseUp.commandKey.ToString().ToLower()}");
                    break;
                    
                case ClickEvent click:
                    jsonBuilder.Append($",\"localMousePosition\":{{\"x\":{click.localPosition.x},\"y\":{click.localPosition.y}}}");
                    jsonBuilder.Append($",\"mouseDelta\":{{\"x\":{click.deltaPosition.x},\"y\":{click.deltaPosition.y}}}");
                    jsonBuilder.Append($",\"mousePosition\":{{\"x\":{click.position.x},\"y\":{click.position.y}}}");
                    jsonBuilder.Append($",\"button\":{click.button}");
                    jsonBuilder.Append($",\"clickCount\":{click.clickCount}");
                    jsonBuilder.Append($",\"altKey\":{click.altKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"ctrlKey\":{click.ctrlKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"shiftKey\":{click.shiftKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"commandKey\":{click.commandKey.ToString().ToLower()}");
                    break;
                    
                case MouseMoveEvent mouseMove:
                    jsonBuilder.Append($",\"localMousePosition\":{{\"x\":{mouseMove.localMousePosition.x},\"y\":{mouseMove.localMousePosition.y}}}");
                    jsonBuilder.Append($",\"mousePosition\":{{\"x\":{mouseMove.mousePosition.x},\"y\":{mouseMove.mousePosition.y}}}");
                    jsonBuilder.Append($",\"mouseDelta\":{{\"x\":{mouseMove.mouseDelta.x},\"y\":{mouseMove.mouseDelta.y}}}");
                    break;
                    
                case MouseEnterEvent mouseEnter:
                    jsonBuilder.Append($",\"localMousePosition\":{{\"x\":{mouseEnter.localMousePosition.x},\"y\":{mouseEnter.localMousePosition.y}}}");
                    jsonBuilder.Append($",\"mousePosition\":{{\"x\":{mouseEnter.mousePosition.x},\"y\":{mouseEnter.mousePosition.y}}}");
                    break;
                    
                case MouseLeaveEvent mouseLeave:
                    jsonBuilder.Append($",\"localMousePosition\":{{\"x\":{mouseLeave.localMousePosition.x},\"y\":{mouseLeave.localMousePosition.y}}}");
                    jsonBuilder.Append($",\"mousePosition\":{{\"x\":{mouseLeave.mousePosition.x},\"y\":{mouseLeave.mousePosition.y}}}");
                    break;
                    
                case WheelEvent wheel:
                    jsonBuilder.Append($",\"delta\":{{\"x\":{wheel.delta.x},\"y\":{wheel.delta.y}}}");
                    jsonBuilder.Append($",\"localMousePosition\":{{\"x\":{wheel.localMousePosition.x},\"y\":{wheel.localMousePosition.y}}}");
                    jsonBuilder.Append($",\"mousePosition\":{{\"x\":{wheel.mousePosition.x},\"y\":{wheel.mousePosition.y}}}");
                    jsonBuilder.Append($",\"altKey\":{wheel.altKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"ctrlKey\":{wheel.ctrlKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"shiftKey\":{wheel.shiftKey.ToString().ToLower()}");
                    jsonBuilder.Append($",\"commandKey\":{wheel.commandKey.ToString().ToLower()}");
                    break;
                    
                case FocusEvent focusEvt:
                    jsonBuilder.Append($",\"direction\":\"{focusEvt.direction.ToString()}\"");
                    break;
                    
                case BlurEvent blurEvt:
                    jsonBuilder.Append($",\"direction\":\"{blurEvt.direction.ToString()}\"");
                    break;
            }
            
            jsonBuilder.Append("}");
            return jsonBuilder.ToString();
        }
        
        private string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
        
        private void SendToJavaScript(string message)
        {
            if (banterLink != null)
            {
                banterLink.Send(message);
                LogVerbose($"Sent to JS: {message}");
            }
            else
            {
                Debug.LogWarning($"[UIElementBridge] BanterLink not found, cannot send: {message}");
            }
        }
        
        // Public API for other systems
        public VisualElement GetElement(string elementId)
        {
            return _elements.TryGetValue(elementId, out var element) ? element : null;
        }

        /// <summary>
        /// Gets the registered element ID for a given VisualElement using reverse lookup
        /// </summary>
        /// <param name="element">The VisualElement to find the ID for</param>
        /// <returns>The registered element ID, or null if not found</returns>
        public string GetElementId(VisualElement element)
        {
            return element != null && _elementToId.TryGetValue(element, out var id) ? id : null;
        }

        /// <summary>
        /// Resolves an element identifier (either ID or name) to a registered element ID
        /// Priority: If it's already a registered ID, return it. Otherwise try to resolve as a name.
        /// </summary>
        /// <param name="elementIdOrName">Either an element ID or an element name</param>
        /// <returns>The registered element ID, or the input value if not found</returns>
        public string ResolveElementIdOrName(string elementIdOrName)
        {
            if (string.IsNullOrEmpty(elementIdOrName))
                return elementIdOrName;

            // Check if it's already a registered ID (O(1))
            if (HasElement(elementIdOrName))
                return elementIdOrName;

            // Try to find by name in the visual tree
            var element = mainDocument?.rootVisualElement?.Q(elementIdOrName);
            if (element != null)
            {
                // Get the registered ID for this element
                var registeredId = GetElementId(element);
                if (!string.IsNullOrEmpty(registeredId))
                    return registeredId;
            }

            // Fallback: return input as-is
            return elementIdOrName;
        }

        public bool HasElement(string elementId)
        {
            return !string.IsNullOrEmpty(elementId) && _elements.ContainsKey(elementId);
        }
        
        public void RegisterDocument(string name, UIDocument document)
        {
            _documents[name] = document;
            if (document.rootVisualElement != null)
            {
                var docId = $"doc_{name}";
                _elements[docId] = document.rootVisualElement;
                _elementToId[document.rootVisualElement] = docId;
            }
        }

        /// <summary>
        /// Processes a UXML document tree and registers all elements for code access
        /// This allows using prefabbed UXML files while maintaining full element access
        /// </summary>
        /// <param name="document">The UIDocument containing the UXML tree</param>
        /// <param name="prefix">Optional prefix for auto-generated element IDs</param>
        /// <returns>Dictionary mapping visual elements to their assigned IDs</returns>
        public Dictionary<VisualElement, string> ProcessUXMLTree(UIDocument document, string prefix = "uxml")
        {
            return Banter.UI.Core.UIXMLTreeProcessor.ProcessDocument(this, document, prefix);
        }

        /// <summary>
        /// Processes a visual element tree and registers all elements for code access
        /// </summary>
        /// <param name="rootElement">The root visual element to start processing from</param>
        /// <param name="prefix">Optional prefix for auto-generated element IDs</param>
        /// <returns>Dictionary mapping visual elements to their assigned IDs</returns>
        public Dictionary<VisualElement, string> ProcessVisualElementTree(VisualElement rootElement, string prefix = "uxml")
        {
            return Banter.UI.Core.UIXMLTreeProcessor.ProcessTree(this, rootElement, prefix);
        }

        /// <summary>
        /// Converts a string value to the target type for property setting
        /// </summary>
        private object ConvertValue(string value, Type targetType)
        {
            try
            {
                if (targetType == typeof(string))
                    return value;

                if (targetType == typeof(int))
                    return int.Parse(value);

                if (targetType == typeof(float))
                    return float.Parse(value);

                if (targetType == typeof(double))
                    return double.Parse(value);

                if (targetType == typeof(bool))
                    return bool.Parse(value);

                if (targetType == typeof(Vector2))
                {
                    var parts = value.Split(',');
                    if (parts.Length == 2)
                        return new Vector2(float.Parse(parts[0]), float.Parse(parts[1]));
                }

                if (targetType == typeof(Vector3))
                {
                    var parts = value.Split(',');
                    if (parts.Length == 3)
                        return new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
                }

                if (targetType == typeof(Color))
                {
                    if (ColorUtility.TryParseHtmlString(value, out Color color))
                        return color;
                }

                // Fallback: try Convert.ChangeType
                return Convert.ChangeType(value, targetType);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIElementBridge.ConvertValue] Failed to convert '{value}' to {targetType.Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets a summary of all currently registered elements
        /// </summary>
        /// <returns>Summary containing element counts and types</returns>
        public string GetRegisteredElementsSummary()
        {
            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"Registered Elements: {_elements.Count}");

            var typeCounts = new Dictionary<string, int>();
            foreach (var element in _elements.Values)
            {
                if (element != null)
                {
                    var typeName = element.GetType().Name;
                    typeCounts[typeName] = typeCounts.GetValueOrDefault(typeName, 0) + 1;
                }
            }

            summary.AppendLine("Element Types:");
            foreach (var kvp in typeCounts.OrderByDescending(x => x.Value))
            {
                summary.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }

            return summary.ToString();
        }
    }
}

