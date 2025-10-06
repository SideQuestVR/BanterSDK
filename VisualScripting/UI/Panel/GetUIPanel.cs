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
        [PortLabelHidden]
        [NullMeansSelf]
        public ValueInput gameObject;

        [DoNotSerialize]
        public ValueOutput panel;

        protected override void Definition()
        {
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();

            panel = ValueOutput<BanterUIPanel>("Panel", (flow) => {
                var target = flow.GetValue<GameObject>(gameObject);

                BanterUIPanel foundPanel = null;

                if (target != null)
                {
                    // Try to get the panel component from the GameObject
                    foundPanel = target.GetComponent<BanterUIPanel>();
                }
                else
                {
                    // If no GameObject specified, get the first panel in the scene
                    foundPanel = Object.FindObjectOfType<BanterUIPanel>();
                }

                if (foundPanel == null)
                {
                    Debug.LogWarning($"[GetUIPanel] No UI Panel found on GameObject: {(target?.name ?? "null")}");
                }

                return foundPanel;
            });
        }
    }
}
#endif