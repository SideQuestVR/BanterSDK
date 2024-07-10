#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Read BullSchript from File")]
    [UnitShortTitle("Read BS")]
    [UnitSubtitle("it's not JavaScript, it's BullSchript!")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class ReadJsFromFile : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput textAsset;

        [DoNotSerialize]
        public ValueOutput jsCode;

        private string fileContents;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => { 
                Debug.Log("Loading JS");
                fileContents = flow.GetValue<TextAsset>(textAsset).text;
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");

            textAsset = ValueInput<TextAsset>("BS file", null);

            jsCode = ValueOutput("BullSchript", (flow) => fileContents);
        }
    }
}
#endif