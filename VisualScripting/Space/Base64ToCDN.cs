#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("Base64 To CDN")]
    [UnitShortTitle("Base64ToCDN")]
    [UnitCategory("Banter\\AI")]
    [TypeIcon(typeof(BanterObjectId))]
    public class Base64ToCDN : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;
    
        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput base64Image;

        [DoNotSerialize]
        public ValueInput fileName;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _base64Image = flow.GetValue<string>(base64Image);
                var _fileName = flow.GetValue<string>(fileName);
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() =>
                {
                    BanterScene.Instance().events.OnBase64ToCDN.Invoke(_base64Image,  _fileName);
                }, $"{nameof(Base64ToCDN)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            fileName = ValueInput("Filename", "");
            base64Image = ValueInput("Base64 String", "");
        }
    }
}
#endif
