#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
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

                    // Set panel properties - panel IDs are managed internally
                    panel.Resolution = res;
                    panel.ScreenSpace = isScreenSpace;

                    Debug.Log($"[CreateUIPanel] Created panel successfully");

                    flow.SetValue(panelReference, panel);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CreateUIPanel] Failed to create UI Panel: {e.Message}");
                    flow.SetValue(panelReference, null);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            resolution = ValueInput("Resolution", new Vector2(512, 512));
            screenSpace = ValueInput("Screen Space", false);
            panelReference = ValueOutput<BanterUIPanel>("Panel");
        }
    }
}
#endif