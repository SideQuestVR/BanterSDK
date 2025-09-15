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
        public ValueInput gameObjectRef;

        [DoNotSerialize]
        public ValueOutput panelReference;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var gameObj = flow.GetValue<GameObject>(gameObjectRef);

                BanterUIPanel foundPanel = null;
                
                if (gameObj != null)
                {
                    // Try to get the panel component from the GameObject
                    foundPanel = gameObj.GetComponent<BanterUIPanel>();
                }
                else
                {
                    // If no GameObject specified, get the first panel in the scene
                    foundPanel = Object.FindObjectOfType<BanterUIPanel>();
                }

                flow.SetValue(panelReference, foundPanel);
                flow.SetValue(success, foundPanel != null);

                if (foundPanel == null)
                {
                    Debug.LogWarning($"[GetUIPanel] No UI Panel found on GameObject: {(gameObj?.name ?? "null")}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            gameObjectRef = ValueInput<GameObject>("GameObject", null);
            panelReference = ValueOutput<BanterUIPanel>("Panel");
            success = ValueOutput<bool>("Found");
        }
    }
}
#endif