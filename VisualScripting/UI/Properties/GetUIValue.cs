using Unity.VisualScripting;
using BS;
using BS.UI.Bridge;
using BS.UI.Core;
using BS.VisualScripting.UI.Helpers;
using UnityEngine;

namespace BS.VisualScripting
{
    [UnitTitle("Get UI Value")]
    [UnitShortTitle("Get UI Value")]
    [UnitCategory("BS\\UI\\Properties\\Value")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetUIValue : Unit
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
        public ValueOutput value;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogWarning("[GetUIValue] Element ID/Name is null or empty.");
                    flow.SetValue(value, 0f);
                    return outputTrigger;
                }

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "GetUIValue"))
                {
                    flow.SetValue(value, 0f);
                    return outputTrigger;
                }

                flow.SetValue(value, 0f); // kept when the bridge doesn't answer (e.g. the element is gone)

                var hook = new EventHook($"UIProperty_{elemId}_value");
                System.Action<CustomEventArgs> onValue = args =>
                {
                    if (args.arguments != null && args.arguments.Length > 0)
                    {
                        flow.SetValue(value, ToFloat(args.arguments[0]));
#if BANTER_UI_DEBUG
                        Debug.Log($"[GetUIValue] Received value: '{args.arguments[0]}' for {hook.name}");
#endif
                    }
                };

                try
                {
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[GetUIValue] Could not resolve panel for element '{elemId}'");
                        return outputTrigger;
                    }

                    // The bridge answers inline, inside HandleMessage, so `flow` is still the caller's flow when
                    // onValue runs and the value lands on this node's output (a new Flow would never be read).
                    EventBus.Register<CustomEventArgs>(hook, onValue);

                    // Format: panelId|GET_UI_PROPERTY|elementId§propertyName
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.GET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}value";
                    UIElementBridge.HandleMessage(message);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUIValue] Failed to get UI value: {e.Message}");
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
            value = ValueOutput<float>("Value");
        }

        // The bridge answers a Slider with a boxed float, a Toggle with "1"/"0" and a text field with its text.
        // Strings are read invariant: the bridge and the page always write numbers with a '.'.
        private static float ToFloat(object raw)
        {
            switch (raw)
            {
                case float f:
                    return f;
                case string s:
                    // The bridge's own numbers use '.', but a TextField's or Dropdown's text is what the player typed,
                    // in their own culture. NumberStyles.Float has no thousands separator, so neither reading is 10x off.
                    if (NumberFormat.TryParse(s, out var parsed))
                        return parsed;
                    return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out parsed) ? parsed : 0f;
                case System.IConvertible convertible:
                    try
                    {
                        return convertible.ToSingle(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch (System.Exception e) when (e is System.InvalidCastException || e is System.FormatException || e is System.OverflowException)
                    {
                        return 0f;
                    }
                default:
                    return 0f;
            }
        }
    }
}
