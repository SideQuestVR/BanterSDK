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
    public class LoadJSFromFile : Unit
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

            jsCode = ValueOutput<string>("BullSchript", (flow) => fileContents);
        }
    }

    [UnitTitle("Inject BullSchript")]
    [UnitShortTitle("Inject BS")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class InjectJS : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput jsCode;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var code = flow.GetValue<string>(jsCode);
                BanterScene.Instance().link.pipe.Send(APICommands.INJECT_BS + MessageDelimiters.PRIMARY + code);

                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            jsCode = ValueInput<string>("BullSchript", string.Empty);
        }
    }
}
#endif
