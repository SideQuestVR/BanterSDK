#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Set Scene Settings")]
    [UnitShortTitle("Set Scene Setting")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetSceneSettings : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput spiderman;
       

       
        
        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                
                var spiderValue = flow.GetValue<SceneSettings>(spiderman);

                BanterScene.Instance().settings.EnableSpiderMan = spiderValue.enableSpiderman;
                BanterScene.Instance().settings.EnableTeleport = spiderValue.enableTeleport;
                BanterScene.Instance().settings.EnableForceGrab = spiderValue.enableForceGrab;
                BanterScene.Instance().settings.EnablePortals = spiderValue.enableAllowPortals;
                BanterScene.Instance().settings.EnableGuests = spiderValue.enableAllowGuests;
                BanterScene.Instance().settings.EnableFriendPositionJoin = spiderValue.enablePositionJoin;
                BanterScene.Instance().settings.EnableAvatars = spiderValue.enableAllowAvatars;
                BanterScene.Instance().settings.SpawnPoint = spiderValue.spawnPoint;





                return outputTrigger;
            });
            spiderman = ValueInput("Scene Settings", new SceneSettings());
            
        }
    }
}
#endif
