using UnityEngine;
using Unity.VisualScripting;
using BS;
using System.Linq;
using BS.Utilities.Async;

namespace BS.VisualScripting
{
    [UnitTitle("Get the Current Leaderboard")]
    [UnitShortTitle("GetCurrentLeaderboard")]
    [UnitCategory("BS\\Leaderboard")]
    [TypeIcon(typeof(BSObjectId))]
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
                    BSScene.Instance().events.OnGetLeaderBoard.Invoke();
                }, $"{nameof(GetCurrentLeaderboard)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
        }
    }
}
