using Unity.VisualScripting;
using BS;
using UnityEngine;

namespace BS.VisualScripting
{
    [UnitTitle("Get UI Panel")]
    [UnitShortTitle("Get UI Panel")]
    [UnitCategory("BS\\UI\\Panel")]
    [TypeIcon(typeof(BSObjectId))]
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

            panel = ValueOutput<BSUIPanel>("Panel", (flow) => {
                var target = flow.GetValue<GameObject>(gameObject);

                BSUIPanel foundPanel = null;

                if (target != null)
                {
                    // Try to get the panel component from the GameObject
                    foundPanel = target.GetComponent<BSUIPanel>();
                }
                else
                {
                    // If no GameObject specified, get the first panel in the scene
                    foundPanel = Object.FindObjectOfType<BSUIPanel>();
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
