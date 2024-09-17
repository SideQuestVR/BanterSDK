#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;


namespace Banter.VisualScripting
{
    [UnitTitle("Space Setting")]
    [UnitShortTitle("Space Setting")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SpaceSettingType : Unit
    {

      
        [DoNotSerialize]
        public ValueOutput outputTrigger;
        [DoNotSerialize]
        public ValueInput spiderman;
        [DoNotSerialize]
        public ValueInput teleport;
        [DoNotSerialize]
        public ValueInput forceGrab;
        [DoNotSerialize]
        public ValueInput allowPortals;
        [DoNotSerialize]
        public ValueInput allowGuests;
        [DoNotSerialize]
        public ValueInput positionJoin;
        [DoNotSerialize]
        public ValueInput allowAvatars;
        [DoNotSerialize]
        public ValueInput spawnPoint;


        protected override void Definition()
        {

           
            outputTrigger = ValueOutput("SettingsFlow", (flow) => {
                var spiderValue = flow.GetValue<bool>(spiderman);
                var teleportValue = flow.GetValue<bool>(teleport);
                var forceGrabValue = flow.GetValue<bool>(forceGrab);
                var allowPortalsValue = flow.GetValue<bool>(allowPortals);
                var allowGuestsValue = flow.GetValue<bool>(allowGuests);
                var positionJoinValue = flow.GetValue<bool>(positionJoin);
                var allowAvatarsValue = flow.GetValue<bool>(allowAvatars);
                return new SceneSettings()
                {
                    //enableSpiderman = spiderValue;
                    enableSpiderman = spiderValue,
                    enableTeleport = teleportValue,
                    enableAllowAvatars = allowAvatarsValue,
                    enableAllowGuests = allowGuestsValue,
                    enableAllowPortals = allowPortalsValue,
                    enableForceGrab = forceGrabValue,
                    enablePositionJoin = positionJoinValue,
    };
            });
            spiderman = ValueInput("Enable Spiderman", false);
            teleport = ValueInput("Enable Teleport", false);
            forceGrab = ValueInput("Enable Force Grab", false);
            allowPortals = ValueInput("Enable Portals", false);
            allowGuests = ValueInput("Enable Guests", false);
            positionJoin = ValueInput("Enable Friend Position Join", false);
            allowAvatars = ValueInput("Enable Avatars", false);
        }
    }
}


#endif