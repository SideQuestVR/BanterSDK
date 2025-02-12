#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("Get the voice volume of the Local User")]
    [UnitShortTitle("GetVoiceVolume")]
    [UnitCategory("Banter\\User")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetVoiceVolume : Unit
    {
        [DoNotSerialize]
        public ValueOutput volume;

        protected override void Definition()
        {
            volume = ValueOutput<float>("Volume", flow => {
                return BanterStarterUpper.voiceVolume;
            });
        }
    }
}
#endif
