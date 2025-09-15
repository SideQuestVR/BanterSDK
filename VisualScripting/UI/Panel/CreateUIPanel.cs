#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Core;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Create UI Panel")]
    [UnitShortTitle("Create UI Panel")]
    [UnitCategory("Banter\\UI\\Panel")]
    [TypeIcon(typeof(BanterObjectId))]
    public class CreateUIPanel : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput resolution;

        [DoNotSerialize]
        public ValueInput screenSpace;

        [DoNotSerialize]
        [PortLabelHidden]
        [NullMeansSelf]
        public ValueInput gameObject;

        [DoNotSerialize]
        public ValueOutput panelReference;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var target = flow.GetValue<GameObject>(gameObject);
                var res = flow.GetValue<Vector2>(resolution);
                var isScreenSpace = flow.GetValue<bool>(screenSpace);
                Debug.Log("[CreateUIPanel] Attempting to create UI Panel...");
                try
                {
                    // Get or add BanterUIPanel component
                    var panel = target.GetComponent<BanterUIPanel>();
                    if (panel == null)
                    {
                        panel = target.AddComponent<BanterUIPanel>();
                    }

                    // Use the new AcquirePanelId method which handles pool management internally
                    int acquiredPanelId = panel.AcquirePanelId();
                    if (acquiredPanelId == -1)
                    {
                        Debug.LogError("[CreateUIPanel] Failed to acquire panel ID from pool. All panels in use.");
                        flow.SetValue(panelReference, null);
                        flow.SetValue(success, false);
                        return outputTrigger;
                    }

                    // Set other panel properties
                    panel.Resolution = res;
                    panel.ScreenSpace = isScreenSpace;

                    Debug.Log($"[CreateUIPanel] Created panel with auto-acquired ID: {acquiredPanelId}");
                    
                    flow.SetValue(panelReference, panel);
                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUIPanel] Failed to create UI Panel: {e.Message}");
                    flow.SetValue(panelReference, null);
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            resolution = ValueInput("Resolution", new Vector2(512, 512));
            screenSpace = ValueInput("Screen Space", false);
            panelReference = ValueOutput<BanterUIPanel>("Panel");
            success = ValueOutput<bool>("Success");
        }
    }
}
#endif