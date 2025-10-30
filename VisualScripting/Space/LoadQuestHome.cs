#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using Banter.Utilities.Async;

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

                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() => {
                    // Create a new GameObject with BanterQuestHome component
                    GameObject questHomeGo = new GameObject("QuestHome");
                    var questHomeComponent = questHomeGo.AddComponent<BanterQuestHome>();

                    // Set properties
                    questHomeComponent.Url = _url;
                    questHomeComponent.AddColliders = _addColliders;
                    questHomeComponent.LegacyShaderFix = _legacyShaderFix;

                    // Store the GameObject for output
                    flow.SetValue(questHomeObject, questHomeGo);

                    Debug.Log($"LoadQuestHome: Started loading Quest Home from {_url}");
                }, $"{nameof(LoadQuestHome)}.{nameof(Definition)}"));

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
