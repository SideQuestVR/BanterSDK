using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Banter.SDK;

namespace Banter.UI.Bridge
{
    public class UIElementBridge : MonoBehaviour
    {
        private static UIElementBridge _instance;
        public static UIElementBridge Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("UIElementBridge");
                    _instance = go.AddComponent<UIElementBridge>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
        
        private Dictionary<string, VisualElement> _elements = new Dictionary<string, VisualElement>();
        private Dictionary<string, UIDocument> _documents = new Dictionary<string, UIDocument>();
        private UIDocument _mainDocument;
        private BanterLink _banterLink;
        
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
        
        // Message delimiters (matching TypeScript)
        private const char PRIMARY_DELIMITER = '¶';
        private const char SECONDARY_DELIMITER = '§';
        private const char TERTIARY_DELIMITER = '|';
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Cache BanterLink reference
            _banterLink = FindObjectOfType<BanterLink>();
            if (_banterLink == null)
            {
                Debug.LogWarning("[UIElementBridge] BanterLink not found on Awake, will retry on first message");
            }
            
            InitializeMainDocument();
        }
        
        private void InitializeMainDocument()
        {
            // Create main UI document if it doesn't exist
            var uiDocumentGO = GameObject.Find("UIDocument");
            if (uiDocumentGO == null)
            {
                uiDocumentGO = new GameObject("UIDocument");
                _mainDocument = uiDocumentGO.AddComponent<UIDocument>();
            }
            else
            {
                _mainDocument = uiDocumentGO.GetComponent<UIDocument>();
            }
            
            // Register root element
            if (_mainDocument != null && _mainDocument.rootVisualElement != null)
            {
                _elements["root"] = _mainDocument.rootVisualElement;
            }
        }
        
        /// <summary>
        /// Check if a message is a UI command
        /// </summary>
        /// <param name="message">The message to check</param>
        /// <returns>True if the message is a UI command</returns>
        public static bool IsUICommand(string message)
        {
            // Extract command from message (everything before first delimiter)
            int delimiterIndex = message.IndexOf(PRIMARY_DELIMITER);
            string command = delimiterIndex > 0 ? message.Substring(0, delimiterIndex) : message;
            return _uiCommandPrefixes.Contains(command);
        }
        
        public void HandleMessage(string message)
        {
            try
            {
                var parts = message.Split(PRIMARY_DELIMITER);
                if (parts.Length < 1) return;
                
                var command = parts[0];
                var data = parts.Length > 1 ? parts.Skip(1).ToArray() : new string[0];
                
                ProcessCommand(command, data);
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
                    
                case UICommands.SET_UI_STYLE:
                    SetStyle(data);
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
            if (data.Length < 3) return;
            
            var elementId = data[0];
            var elementType = data[1];
            var parentId = data[2];
            
            // Create the appropriate VisualElement based on type
            VisualElement element = CreateElementByType(elementType);
            
            if (element != null)
            {
                _elements[elementId] = element;
                
                // Attach to parent if specified
                if (parentId != "null" && _elements.TryGetValue(parentId, out var parent))
                {
                    parent.Add(element);
                }
                
                Debug.Log($"[UIElementBridge] Created element: {elementId} of type {elementType}");
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
                    10 => new Button(), // Button
                    11 => new Label(), // Label
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
                element.RemoveFromHierarchy();
                _elements.Remove(elementId);
                
                Debug.Log($"[UIElementBridge] Destroyed element: {elementId}");
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
                
                Debug.Log($"[UIElementBridge] Attached {childId} to {parentId}");
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
                Debug.Log($"[UIElementBridge] Detached {childId} from {parentId}");
            }
        }
        
        private void SetProperty(string[] data)
        {
            if (data.Length < 3) return;
            
            var elementId = data[0];
            var propertyName = data[1];
            var value = data[2];
            
            if (!_elements.TryGetValue(elementId, out var element)) return;
            
            // Parse property enum value
            if (int.TryParse(propertyName, out int propValue))
            {
                switch (propValue)
                {
                    case 0: // name
                        element.name = value;
                        break;
                    case 1: // text
                        if (element is TextElement textElement)
                            textElement.text = value;
                        break;
                    case 2: // tooltip
                        element.tooltip = value;
                        break;
                    case 3: // value
                        SetElementValue(element, value);
                        break;
                    case 4: // enabled
                        element.SetEnabled(value == "1");
                        break;
                    case 5: // visible
                        element.visible = value == "1";
                        break;
                    case 6: // focusable
                        element.focusable = value == "1";
                        break;
                    case 7: // tabIndex
                        element.tabIndex = int.Parse(value);
                        break;
                }
            }
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
            var styleName = data[1];
            var value = data[2];
            
            if (!_elements.TryGetValue(elementId, out var element)) return;
            
            // Apply style based on property name
            switch (styleName)
            {
                case "width":
                    element.style.width = ParseLength(value);
                    break;
                case "height":
                    element.style.height = ParseLength(value);
                    break;
                case "backgroundColor":
                    element.style.backgroundColor = ParseColor(value);
                    break;
                case "color":
                    element.style.color = ParseColor(value);
                    break;
                case "fontSize":
                    element.style.fontSize = float.Parse(value);
                    break;
                case "margin":
                    var margin = ParseSpacing(value);
                    element.style.marginTop = margin[0];
                    element.style.marginRight = margin[1];
                    element.style.marginBottom = margin[2];
                    element.style.marginLeft = margin[3];
                    break;
                case "padding":
                    var padding = ParseSpacing(value);
                    element.style.paddingTop = padding[0];
                    element.style.paddingRight = padding[1];
                    element.style.paddingBottom = padding[2];
                    element.style.paddingLeft = padding[3];
                    break;
            }
        }
        
        private StyleLength ParseLength(string value)
        {
            if (value.EndsWith("px"))
            {
                return float.Parse(value.Replace("px", ""));
            }
            else if (value.EndsWith("%"))
            {
                return Length.Percent(float.Parse(value.Replace("%", "")));
            }
            else if (value == "auto")
            {
                return StyleKeyword.Auto;
            }
            
            return float.Parse(value);
        }
        
        private StyleColor ParseColor(string value)
        {
            if (ColorUtility.TryParseHtmlString(value, out Color color))
            {
                return color;
            }
            return Color.white;
        }
        
        private float[] ParseSpacing(string value)
        {
            var parts = value.Split(' ');
            if (parts.Length == 1)
            {
                var v = float.Parse(parts[0].Replace("px", ""));
                return new[] { v, v, v, v };
            }
            else if (parts.Length == 2)
            {
                var v = float.Parse(parts[0].Replace("px", ""));
                var h = float.Parse(parts[1].Replace("px", ""));
                return new[] { v, h, v, h };
            }
            else if (parts.Length == 4)
            {
                return parts.Select(p => float.Parse(p.Replace("px", ""))).ToArray();
            }
            
            return new float[] { 0, 0, 0, 0 };
        }
        
        private void CallMethod(string[] data)
        {
            if (data.Length < 2) return;
            
            var elementId = data[0];
            var methodName = data[1];
            var args = data.Skip(2).ToArray();
            
            if (!_elements.TryGetValue(elementId, out var element)) return;
            
            switch (methodName)
            {
                case "Focus":
                    element.Focus();
                    break;
                case "Blur":
                    element.Blur();
                    break;
                case "ScrollTo":
                    if (element is ScrollView scrollView && args.Length >= 1)
                    {
                        scrollView.scrollOffset = new Vector2(0, float.Parse(args[0]));
                    }
                    break;
            }
        }
        
        private void RegisterEvent(string[] data)
        {
            if (data.Length < 2) return;
            
            var elementId = data[0];
            var eventType = data[1];
            
            if (!_elements.TryGetValue(elementId, out var element)) return;
            
            // Register Unity event callbacks that will send messages back to TypeScript
            switch (eventType)
            {
                case "click":
                    element.RegisterCallback<ClickEvent>(evt => SendUIEvent(elementId, "click", evt));
                    break;
                case "change":
                    element.RegisterCallback<ChangeEvent<string>>(evt => SendUIEvent(elementId, "change", evt));
                    break;
                case "focus":
                    element.RegisterCallback<FocusEvent>(evt => SendUIEvent(elementId, "focus", evt));
                    break;
                case "blur":
                    element.RegisterCallback<BlurEvent>(evt => SendUIEvent(elementId, "blur", evt));
                    break;
            }
        }
        
        private void UnregisterEvent(string[] data)
        {
            // TODO: Implement event unregistration (requires storing callbacks)
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
                var updateParts = update.Split(TERTIARY_DELIMITER);
                if (updateParts.Length >= 2)
                {
                    ProcessCommand(updateParts[0], updateParts.Skip(1).ToArray());
                }
            }
        }
        
        private void SendUIEvent(string elementId, string eventType, EventBase evt)
        {
            // Send event back to TypeScript
            var message = $"{UICommands.UI_EVENT}{PRIMARY_DELIMITER}{elementId}{SECONDARY_DELIMITER}{eventType}";
            
            // Add event data if needed
            if (evt is ChangeEvent<string> changeEvt)
            {
                message += $"{SECONDARY_DELIMITER}{changeEvt.newValue}";
            }
            
            // Send via BanterBridge or similar mechanism
            SendToJavaScript(message);
        }
        
        private void SendToJavaScript(string message)
        {
            // Use cached BanterLink reference, with fallback
            if (_banterLink == null)
            {
                _banterLink = FindObjectOfType<BanterLink>();
            }
            
            if (_banterLink != null)
            {
                _banterLink.Send(message);
                Debug.Log($"[UIElementBridge] Sent to JS: {message}");
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
        
        public void RegisterDocument(string name, UIDocument document)
        {
            _documents[name] = document;
            if (document.rootVisualElement != null)
            {
                _elements[$"doc_{name}"] = document.rootVisualElement;
            }
        }
    }
}