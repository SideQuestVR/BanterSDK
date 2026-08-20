using UnityEngine;
using Unity.VisualScripting;
using BS;

namespace BS.VisualScripting
{
    [UnitTitle("Read BullSchript from File")]
    [UnitShortTitle("Read BS")]
    [UnitSubtitle("it's not JavaScript, it's BullSchript!")]
    [UnitCategory("BS\\Browser")]
    [TypeIcon(typeof(BSObjectId))]
    public class ReadJsFromFile : Unit
    {
        [DoNotSerialize]
        public ValueInput textAsset;

        [DoNotSerialize]
        public ValueOutput jsCode;

        private string fileContents;

        protected override void Definition()
        {
            textAsset = ValueInput<TextAsset>("File");
            jsCode = ValueOutput("BullSchript", (flow) => 
            {
                var asset = flow.GetValue<TextAsset>(textAsset);
                if (asset == null)
                {
                    return string.Empty;
                }

                fileContents = asset.text;
                return fileContents;
            });
        }
    }
}
