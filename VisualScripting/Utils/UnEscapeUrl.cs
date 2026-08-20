//

using UnityEngine;
using Unity.VisualScripting;
using BS;
using UnityEngine.Networking;

namespace BS.VisualScripting
{
    [UnitTitle("UnEscape Url")]
    [UnitShortTitle("UnEscape Url")]
    [UnitCategory("BS\\Utils")]
    [TypeIcon(typeof(BSObjectId))]
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
