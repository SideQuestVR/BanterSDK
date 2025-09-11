#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Core;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Destroy UI Panel")]
    [UnitShortTitle("Destroy UI Panel")]
    [UnitCategory("Banter\\UI\\Panel")]
    [TypeIcon(typeof(BanterObjectId))]
    public class DestroyUIPanel : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput panelReference;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var panel = flow.GetValue<BanterUIPanel>(panelReference);

                if (panel == null)
                {
                    Debug.LogWarning("[DestroyUIPanel] Panel reference is null.");
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                try
                {
                    // Release panel ID back to pool before destroying
                    int panelId = panel.PanelId;
                    UIPanelPool.ReleasePanel(panelId);
                    
                    // Destroy the panel component
                    Object.Destroy(panel);
                    
                    Debug.Log($"[DestroyUIPanel] Released panel ID {panelId} and destroyed panel component");
                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[DestroyUIPanel] Failed to destroy UI Panel: {e.Message}");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            panelReference = ValueInput<BanterUIPanel>("Panel");
            success = ValueOutput<bool>("Success");
        }
    }
}
#endif