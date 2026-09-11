using Unity.VisualScripting;
using BS;
using BS.UI.Bridge;
using BS.UI.Core;
using UnityEngine;
using BS.VisualScripting.UI.Helpers;

namespace BS.VisualScripting
{
    /// <summary>
    /// Creates a SideQuest colour picker element (type 108) on a panel. The element itself lives
    /// in com.sidequest.color-picker, which registers its factory with the UI bridge; without
    /// that package the bridge makes a bare VisualElement, exactly as it does for any unknown
    /// type. Everything here is plain messages, so this node has no dependency on the package.
    /// </summary>
    [UnitTitle("Create UI Color Picker")]
    [UnitShortTitle("Create UI Color Picker")]
    [UnitCategory("BS\\UI\\Elements\\Controls")]
    [TypeIcon(typeof(BSObjectId))]
    public class CreateUIColorPicker : Unit
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
        public ValueInput initialColor;

        [DoNotSerialize]
        public ValueInput presetsOnly;

        [DoNotSerialize]
        public ValueInput presets;

        [DoNotSerialize]
        public ValueInput showAlpha;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueOutput colorPickerId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var target = flow.GetValue<GameObject>(gameObject);
                var panel = target?.GetComponent<BSUIPanel>();
                var parentId = flow.GetValue<string>(parentElementId);
                var parentName = flow.GetValue<string>(parentElementName);
                var color = flow.GetValue<Color>(initialColor);
                var simple = flow.GetValue<bool>(presetsOnly);
                var presetList = flow.GetValue<string>(presets);
                var alpha = flow.GetValue<bool>(showAlpha);
                var elemId = flow.GetValue<string>(elementId);
                var elemName = flow.GetValue<string>(elementName);

                if (panel == null)
                {
                    Debug.LogWarning("[CreateUIColorPicker] BSUIPanel component not found on GameObject.");
                    flow.SetValue(colorPickerId, "");
                    return outputTrigger;
                }

                if (!panel.ValidateForUIOperation("CreateUIColorPicker"))
                {
                    flow.SetValue(colorPickerId, "");
                    return outputTrigger;
                }

                try
                {
                    var pickerElementId = string.IsNullOrEmpty(elemId) ? $"ui_colorpicker_{System.Guid.NewGuid().ToString("N")[..8]}" : elemId;

                    var panelIdStr = panel.GetFormattedPanelId();
                    var elementType = ((int)UIElementTypeVS.ColorPicker).ToString();
                    string resolvedParentId = UIElementResolverHelper.ResolveElementIdOrName(parentId, parentName);
                    var parentElementIdStr = string.IsNullOrEmpty(resolvedParentId) ? "root" : resolvedParentId;

                    // Format: panelId|CREATE_UI_ELEMENT|elementId§elementType§parentId
                    var message = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.CREATE_UI_ELEMENT}{MessageDelimiters.PRIMARY}{pickerElementId}{MessageDelimiters.SECONDARY}{elementType}{MessageDelimiters.SECONDARY}{parentElementIdStr}";
                    UIElementBridge.HandleMessage(message);

                    void SetProperty(string property, string value)
                    {
                        var propertyMessage = $"{panelIdStr}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{pickerElementId}{MessageDelimiters.SECONDARY}{property}{MessageDelimiters.SECONDARY}{value}";
                        UIElementBridge.HandleMessage(propertyMessage);
                    }

                    if (!string.IsNullOrEmpty(elemName)) SetProperty("name", elemName);
                    SetProperty("value", "#" + ColorUtility.ToHtmlStringRGBA(color));
                    if (simple) SetProperty("mode", "presets");
                    if (!string.IsNullOrEmpty(presetList)) SetProperty("presets", presetList);
                    if (!alpha) SetProperty("showAlpha", "0");

                    flow.SetValue(colorPickerId, pickerElementId);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUIColorPicker] Failed to create UI color picker: {e.Message}");
                    flow.SetValue(colorPickerId, "");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            parentElementId = ValueInput("Parent Element ID", "");
            parentElementName = ValueInput("Parent Element Name", "");
            initialColor = ValueInput("Initial Color", Color.white);
            presetsOnly = ValueInput("Presets Only", false);
            presets = ValueInput("Presets (colour codes)", "");
            showAlpha = ValueInput("Show Alpha", true);
            elementId = ValueInput("Element ID", "");
            elementName = ValueInput("Element Name", "");
            colorPickerId = ValueOutput<string>("Element ID");
        }
    }
}
