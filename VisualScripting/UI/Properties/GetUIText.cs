using Unity.VisualScripting;
using BS;
using BS.UI.Bridge;
using BS.UI.Core;
using BS.VisualScripting.UI.Helpers;
using UnityEngine;

namespace BS.VisualScripting
{
    [UnitTitle("Get UI Text")]
    [UnitShortTitle("Get UI Text")]
    [UnitCategory("BS\\UI\\Properties\\Text")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetUIText : Unit
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
        public ValueOutput textValue;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogWarning("[GetUIText] Element ID/Name is null or empty.");
                    flow.SetValue(textValue, "");
                    return outputTrigger;
                }

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "GetUIText"))
                {
                    flow.SetValue(textValue, "");
                    return outputTrigger;
                }

                flow.SetValue(textValue, ""); // kept when the bridge doesn't answer (e.g. the element is gone)

                var hook = new EventHook($"UIProperty_{elemId}_text");
                System.Action<CustomEventArgs> onValue = args =>
                {
                    if (args.arguments != null && args.arguments.Length > 0)
                    {
                        flow.SetValue(textValue, args.arguments[0]?.ToString() ?? "");
#if BANTER_UI_DEBUG
                        Debug.Log($"[GetUIText] Received text: '{args.arguments[0]}' for {hook.name}");
#endif
                    }
                };

                try
                {
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[GetUIText] Could not resolve panel for element '{elemId}'");
                        return outputTrigger;
                    }

                    // The bridge answers inline, inside HandleMessage, so `flow` is still the caller's flow when
                    // onValue runs and the value lands on this node's output (a new Flow would never be read).
                    EventBus.Register<CustomEventArgs>(hook, onValue);

                    // Format: panelId|GET_UI_PROPERTY|elementId§propertyName
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.GET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}text";
                    UIElementBridge.HandleMessage(message);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUIText] Failed to get UI text: {e.Message}");
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
            textValue = ValueOutput<string>("Text");
        }
    }
}
