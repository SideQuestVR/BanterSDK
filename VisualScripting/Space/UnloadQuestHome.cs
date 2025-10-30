#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("Unload Quest Home")]
    [UnitShortTitle("Unload Quest Home")]
    [UnitCategory("Banter\\Space")]
    [TypeIcon(typeof(BanterObjectId))]
    public class UnloadQuestHome : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput questHomeObject;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _questHomeObject = flow.GetValue<GameObject>(questHomeObject);

                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() => {
                    if (_questHomeObject != null)
                    {
                        // Destroy the Quest Home GameObject
                        Object.Destroy(_questHomeObject);
                        Debug.Log("UnloadQuestHome: Quest Home unloaded");
                    }
                    else
                    {
                        Debug.LogWarning("UnloadQuestHome: Quest Home object is null");
                    }
                }, $"{nameof(UnloadQuestHome)}.{nameof(Definition)}"));

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            questHomeObject = ValueInput<GameObject>("Quest Home Object", null);
        }
    }
}
#endif
