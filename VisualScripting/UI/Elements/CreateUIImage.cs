#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Create UI Image")]
    [UnitShortTitle("Create UI Image")]
    [UnitCategory("Banter\\UI\\Elements\\Display")]
    [TypeIcon(typeof(BanterObjectId))]
    public class CreateUIImage : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        [PortLabelHidden]
        [NullMeansSelf]
        public ValueInput gameObject;

        [DoNotSerialize]
        public ValueInput parentElementId;

        [DoNotSerialize]
        public ValueInput parentElementName;

        [DoNotSerialize]
        public ValueInput texture;

        [DoNotSerialize]
        public ValueInput sprite;

        [DoNotSerialize]
        public ValueInput tintColor;

        [DoNotSerialize]
        public ValueInput scaleMode;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueOutput imageId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var target = flow.GetValue<GameObject>(gameObject);
                var panel = target?.GetComponent<BanterUIPanel>();
                var parentId = flow.GetValue<string>(parentElementId);
                var parentName = flow.GetValue<string>(parentElementName);
                var tex = flow.GetValue<Texture2D>(texture);
                var spr = flow.GetValue<Sprite>(sprite);
                var tint = flow.GetValue<Color>(tintColor);
                var scale = flow.GetValue<ScaleMode>(scaleMode);
                var elemId = flow.GetValue<string>(elementId);
                var elemName = flow.GetValue<string>(elementName);

                if (panel == null)
                {
                    Debug.LogWarning("[CreateUIImage] BanterUIPanel component not found on GameObject.");
                    flow.SetValue(imageId, "");
                    return outputTrigger;
                }

                if (!panel.ValidateForUIOperation("CreateUIImage"))
                {
                    flow.SetValue(imageId, "");
                    return outputTrigger;
                }

                try
                {
                    // Generate unique element ID if not provided
                    var imageElementId = string.IsNullOrEmpty(elemId) ? $"ui_image_{System.Guid.NewGuid().ToString("N")[..8]}" : elemId;

                    // Use UICommands to send CREATE_UI_ELEMENT command
                    // Create a VisualElement and set background image
                    var panelIdStr = panel.GetFormattedPanelId();
                    var elementType = "0"; // UIElementType.VisualElement = 0
                    string resolvedParentId = UIElementResolverHelper.ResolveElementIdOrName(parentId, parentName);
                    var parentElementIdStr = string.IsNullOrEmpty(resolvedParentId) ? "root" : resolvedParentId;

                    // Format: panelId|CREATE_UI_ELEMENT|elementId§elementType§parentId
                    var message = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.CREATE_UI_ELEMENT}{MessageDelimiters.PRIMARY}{imageElementId}{MessageDelimiters.SECONDARY}{elementType}{MessageDelimiters.SECONDARY}{parentElementIdStr}";

                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    // Set element name if provided
                    if (!string.IsNullOrEmpty(elemName))
                    {
                        var nameMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{imageElementId}{MessageDelimiters.SECONDARY}name{MessageDelimiters.SECONDARY}{elemName}";
                        UIElementBridge.HandleMessage(nameMessage);
                    }

                    // Set background image using SetUIBackground approach (direct element manipulation)
                    var imagePanel = UIPanelExtensions.GetPanelByElementId(imageElementId);
                    if (imagePanel != null)
                    {
                        var bridge = GetUIElementBridge(imagePanel);
                        if (bridge != null)
                        {
                            var element = bridge.GetElement(imageElementId);
                            if (element != null)
                            {
                                // Set background image
                                if (spr != null)
                                {
                                    element.style.backgroundImage = new StyleBackground(spr);
                                }
                                else if (tex != null)
                                {
                                    element.style.backgroundImage = new StyleBackground(tex);
                                }

                                // Set tint color
                                element.style.unityBackgroundImageTintColor = tint;

                                // Set scale mode
                                element.style.unityBackgroundScaleMode = (ScaleMode)scale;

                                Debug.Log($"[CreateUIImage] Created image element '{imageElementId}' with background");
                            }
                        }
                    }

                    flow.SetValue(imageId, imageElementId);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUIImage] Failed to create UI image: {e.Message}");
                    flow.SetValue(imageId, "");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            parentElementId = ValueInput("Parent Element ID", "");
            parentElementName = ValueInput("Parent Element Name", "");
            texture = ValueInput<Texture2D>("Texture", null);
            sprite = ValueInput<Sprite>("Sprite", null);
            tintColor = ValueInput("Tint Color", Color.white);
            scaleMode = ValueInput("Scale Mode", ScaleMode.ScaleToFit);
            elementId = ValueInput("Element ID", "");
            elementName = ValueInput("Element Name", "");
            imageId = ValueOutput<string>("Element ID");
        }

        /// <summary>
        /// Gets the UIElementBridge from a BanterUIPanel using reflection
        /// </summary>
        private UIElementBridge GetUIElementBridge(BanterUIPanel panel)
        {
            try
            {
                var panelType = typeof(BanterUIPanel);
                var bridgeField = panelType.GetField("uiElementBridge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return bridgeField?.GetValue(panel) as UIElementBridge;
            }
            catch
            {
                return null;
            }
        }
    }
}
#endif
