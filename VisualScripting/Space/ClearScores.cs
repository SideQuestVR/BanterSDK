#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using BS;
using System.Linq;
using BS.Utilities.Async;

namespace BS.VisualScripting
{
    [UnitTitle("Clear Scores on a Leaderboard")]
    [UnitShortTitle("ClearScores")]
    [UnitCategory("BS\\Leaderboard")]
    [TypeIcon(typeof(BSObjectId))]
    public class ClearScores : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;
    
        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput board;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _board = flow.GetValue<string>(board);
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() =>
                {
                    BSScene.Instance().events.OnLeaderBoardClear.Invoke(_board);
                }, $"{nameof(ClearScores)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            board = ValueInput("Board", "");
        }
    }
}
#endif
