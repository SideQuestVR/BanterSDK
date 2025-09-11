#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Get UI Panel")]
    [UnitShortTitle("Get UI Panel")]
    [UnitCategory("Banter\\UI\\Panel")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUIPanel : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput panelId;

        [DoNotSerialize]
        public ValueOutput panelReference;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var id = flow.GetValue<int>(panelId);

                // Find panel with matching ID in the scene
                var allPanels = Object.FindObjectsOfType<BanterUIPanel>();
                BanterUIPanel foundPanel = null;

                foreach (var panel in allPanels)
                {
                    if (panel.PanelId == id)
                    {
                        foundPanel = panel;
                        break;
                    }
                }

                flow.SetValue(panelReference, foundPanel);
                flow.SetValue(success, foundPanel != null);

                if (foundPanel == null)
                {
                    Debug.LogWarning($"[GetUIPanel] No UI Panel found with ID: {id}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            panelId = ValueInput("Panel ID", 0);
            panelReference = ValueOutput<BanterUIPanel>("Panel");
            success = ValueOutput<bool>("Found");
        }
    }
}
#endif