#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Load Quest Home")]
    [UnitShortTitle("Load Quest Home")]
    [UnitCategory("Banter\\Space")]
    [TypeIcon(typeof(BanterObjectId))]
    public class LoadQuestHome : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput url;

        [DoNotSerialize]
        public ValueInput addColliders;

        [DoNotSerialize]
        public ValueInput legacyShaderFix;

        [DoNotSerialize]
        public ValueOutput questHomeObject;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _url = flow.GetValue<string>(url);
                var _addColliders = flow.GetValue<bool>(addColliders);
                var _legacyShaderFix = flow.GetValue<bool>(legacyShaderFix);

                // Create GameObject and component immediately (synchronously on main thread)
                // Visual Scripting already runs on the main thread, no need for task scheduler
                GameObject questHomeGo = new GameObject("QuestHome");
                var questHomeComponent = questHomeGo.AddComponent<BanterQuestHome>();

                // Set properties (BanterQuestHome will handle async loading internally)
                questHomeComponent.Url = _url;
                questHomeComponent.AddColliders = _addColliders;
                questHomeComponent.LegacyShaderFix = _legacyShaderFix;

                // Store the GameObject for output (must be synchronous so next node can use it)
                flow.SetValue(questHomeObject, questHomeGo);

                Debug.Log($"[LoadQuestHome] Started loading Quest Home from {_url}");

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            url = ValueInput("URL", "https://sidequestvr.com/app/167567/canyon-environment");
            addColliders = ValueInput("Add Colliders", true);
            legacyShaderFix = ValueInput("Legacy Shader Fix", true);
            questHomeObject = ValueOutput<GameObject>("Quest Home Object");
        }
    }
}
#endif
