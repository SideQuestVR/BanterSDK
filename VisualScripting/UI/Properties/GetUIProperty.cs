using Unity.VisualScripting;
using BS;
using BS.UI.Bridge;
using BS.UI.Core;
using BS.VisualScripting.UI.Helpers;
using UnityEngine;
using System.Collections;

namespace BS.VisualScripting
{
    [UnitTitle("Get UI Property")]
    [UnitShortTitle("Get UI Property")]
    [UnitCategory("BS\\UI\\Properties")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetUIProperty : Unit
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
        public ValueInput propertyName;

        [DoNotSerialize]
        public ValueOutput elementIdOutput;

        [DoNotSerialize]
        public ValueOutput propertyValue;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);
                var propName = flow.GetValue<UIPropertyNameVS>(propertyName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogWarning("[GetUIProperty] Element ID/Name is null or empty.");
                    flow.SetValue(elementIdOutput, "");
                    flow.SetValue(propertyValue, null);
                    return outputTrigger;
                }

                // Set element ID output for chaining
                flow.SetValue(elementIdOutput, elemId);

                flow.SetValue(propertyValue, null); // kept when the bridge doesn't answer (e.g. the element is gone)

                // Convert enum to property name
                var propNameStr = GetPropertyName(propName);

                var hook = new EventHook($"UIProperty_{elemId}_{propNameStr}");
                System.Action<CustomEventArgs> onValue = args =>
                {
                    if (args.arguments != null && args.arguments.Length > 0)
                    {
                        flow.SetValue(propertyValue, args.arguments[0]);
#if BANTER_UI_DEBUG
                        Debug.Log($"[GetUIProperty] Received property value: '{args.arguments[0]}' for {hook.name}");
#endif
                    }
                };

                try
                {
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[GetUIProperty] Could not resolve panel for element '{elemId}'");
                        return outputTrigger;
                    }

                    // The bridge answers inline, inside HandleMessage, so `flow` is still the caller's flow when
                    // onValue runs and the value lands on this node's output (a new Flow would never be read).
                    EventBus.Register<CustomEventArgs>(hook, onValue);

                    // Format: panelId|GET_UI_PROPERTY|elementId§propertyName
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.GET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}{propNameStr}";
                    UIElementBridge.HandleMessage(message);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUIProperty] Failed to get UI property: {e.Message}");
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
            propertyName = ValueInput("Property", UIPropertyNameVS.Text);
            elementIdOutput = ValueOutput<string>("Element ID");
            propertyValue = ValueOutput<object>("Value");
        }

        private string GetPropertyName(UIPropertyNameVS propName)
        {
            return propName switch
            {
                UIPropertyNameVS.Checked => "checked",
                UIPropertyNameVS.Elasticity => "elasticity",
                UIPropertyNameVS.Enabled => "enabled",
                UIPropertyNameVS.Horizontalscrolling => "horizontalscrolling",
                UIPropertyNameVS.Maxvalue => "maxvalue",
                UIPropertyNameVS.Minvalue => "minvalue",
                UIPropertyNameVS.Name => "name",
                UIPropertyNameVS.Scrolldecelerationrate => "scrolldecelerationrate",
                UIPropertyNameVS.Scrollposition => "scrollposition",
                UIPropertyNameVS.Text => "text",
                UIPropertyNameVS.Tooltip => "tooltip",
                UIPropertyNameVS.Value => "value",
                UIPropertyNameVS.Verticalscrolling => "verticalscrolling",
                UIPropertyNameVS.Visible => "visible",
                _ => "text"
            };
        }
    }
}
