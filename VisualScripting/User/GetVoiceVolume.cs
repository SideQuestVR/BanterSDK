using UnityEngine;
using Unity.VisualScripting;
using BS;
using System.Linq;
using BS.Utilities.Async;

namespace BS.VisualScripting
{
    /// <summary>
    /// The local player's voice as the room hears it: a linear peak from 0 to 1, and 0 while muted or not connected.
    /// The client keeps it current. In SDK Play there's no voice, so it stays 0.
    /// </summary>
    [UnitTitle("Get the voice volume of the Local User")]
    [UnitShortTitle("GetVoiceVolume")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetVoiceVolume : Unit
    {
        [DoNotSerialize]
        public ValueOutput volume;

        protected override void Definition()
        {
            volume = ValueOutput<float>("Volume", flow =>
            {
                return BSStarterUpper.voiceVolume;
            });
        }
    }
}
