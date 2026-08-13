#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using BS;
using System.Linq;
using BS.Utilities.Async;

namespace BS.VisualScripting
{
    [UnitTitle("Get the voice volume of the Local User")]
    [UnitShortTitle("GetVoiceVolume")]
    [UnitCategory("Banter\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetVoiceVolume : Unit
    {
        [DoNotSerialize]
        public ValueOutput volume;

        protected override void Definition()
        {
            volume = ValueOutput<float>("Volume", flow => {
                return BSStarterUpper.voiceVolume;
            });
        }
    }
}
#endif
