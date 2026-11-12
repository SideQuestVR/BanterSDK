using Unity.VisualScripting;
using BS;
using BS.UI.Bridge;
using BS.UI.Core;
using UnityEngine;
using BS.VisualScripting.UI.Helpers;

namespace BS.VisualScripting
{
    [UnitTitle("Get UI Style")]
    [UnitShortTitle("Get UI Style")]
    [UnitCategory("BS\\UI\\Styles")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetUIStyle : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueInput styleProperty;

        [DoNotSerialize]
        public ValueOutput elementIdOutput;

        [DoNotSerialize]
        public ValueOutput styleValue;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);
                var property = flow.GetValue<UIStyleProperty>(styleProperty);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogWarning("[GetUIStyle] Element ID/Name is null or empty.");
                    flow.SetValue(elementIdOutput, "");
                    flow.SetValue(styleValue, null);
                    return outputTrigger;
                }

                // Set element ID output for chaining
                flow.SetValue(elementIdOutput, elemId);

                flow.SetValue(styleValue, null); // kept when the bridge doesn't answer (e.g. the element is gone)

                // Convert the UIStyleProperty enum to its USS property name
                var propertyName = property.ToUSSName();

                var hook = new EventHook($"UIStyle_{elemId}_{propertyName}");
                System.Action<CustomEventArgs> onValue = args =>
                {
                    if (args.arguments != null && args.arguments.Length > 0)
                    {
                        flow.SetValue(styleValue, args.arguments[0]?.ToString());
#if BANTER_UI_DEBUG
                        Debug.Log($"[GetUIStyle] Received style value: '{args.arguments[0]}' for {hook.name}");
#endif
                    }
                };

                try
                {
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[GetUIStyle] Could not resolve panel for element '{elemId}'");
                        return outputTrigger;
                    }

                    // The bridge answers inline, inside HandleMessage, so `flow` is still the caller's flow when
                    // onValue runs and the value lands on this node's output (a new Flow would never be read).
                    EventBus.Register<CustomEventArgs>(hook, onValue);

                    // Format: panelId|GET_UI_STYLE|elementId§styleName
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.GET_UI_STYLE}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}{propertyName}";
                    UIElementBridge.HandleMessage(message);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUIStyle] Failed to get UI style: {e.Message}");
                }
                finally
                {
                    // Always unregistered (a no-op if never registered): a leftover handler would hold this flow.
                    EventBus.Unregister(hook, onValue);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            styleProperty = ValueInput("Style Property", UIStyleProperty.BackgroundColor);
            elementIdOutput = ValueOutput<string>("Element ID");
            styleValue = ValueOutput<string>("Style Value");
        }
    }
}
