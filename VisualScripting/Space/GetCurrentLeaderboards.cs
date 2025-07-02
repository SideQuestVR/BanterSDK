#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("Get the Current Leaderboard")]
    [UnitShortTitle("GetCurrentLeaderboard")]
    [UnitCategory("Banter\\Leaderboard")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetCurrentLeaderboard : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;
    
        [DoNotSerialize]
        public ControlOutput outputTrigger;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() =>
                {
                    BanterScene.Instance().events.OnGetLeaderBoard.Invoke();
                }, $"{nameof(GetCurrentLeaderboard)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
        }
    }
}
#endif
