#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
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

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var panel = flow.GetValue<BanterUIPanel>(panelReference);

                if (panel == null)
                {
                    Debug.LogWarning("[DestroyUIPanel] Panel reference is null.");
                    return outputTrigger;
                }

                try
                {
                    // Destroy the panel component (panel IDs are handled internally)
                    Object.Destroy(panel);

#if BANTER_UI_DEBUG
                    Debug.Log($"[DestroyUIPanel] Destroyed panel component");
#endif
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[DestroyUIPanel] Failed to destroy UI Panel: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            panelReference = ValueInput<BanterUIPanel>("Panel");
        }
    }
}
#endif
