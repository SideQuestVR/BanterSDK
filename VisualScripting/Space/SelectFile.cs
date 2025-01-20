#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("Select file (GLB/JPG/PNG)")]
    [UnitShortTitle("SelectFile")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SelectFile : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;
    
        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput type;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _type = flow.GetValue<SelectFileType>(type);
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
                    BanterScene.Instance().events.OnSelectFile.Invoke(_type);
                });
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            type = ValueInput("Type", SelectFileType.GLB);
        }
    }
}
#endif
