//

#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine.Networking;

namespace Banter.VisualScripting
{
    [UnitTitle("UnEscape Url")]
    [UnitShortTitle("UnEscape Url")]
    [UnitCategory("Banter\\Utils")]
    [TypeIcon(typeof(BanterObjectId))]
    public class UnEscapeUrl : Unit
    {
        [DoNotSerialize]
        public ValueInput inputString;

        [DoNotSerialize]
        public ValueOutput outputString;

        private string fileContents;

        protected override void Definition()
        {
            inputString = ValueInput<string>("Input Url");
            outputString = ValueOutput("Output Url", (flow) => UnityWebRequest.UnEscapeURL(flow.GetValue<string>(inputString)));
        }
    }
}
#endif
