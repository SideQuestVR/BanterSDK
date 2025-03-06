#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    enum SortType
    {
        ASC,
        DESC
    }
    [UnitTitle("Clear Scores on a Leaderboard")]
    [UnitShortTitle("ClearScores")]
    [UnitCategory("Banter\\Networking")]
    [TypeIcon(typeof(BanterObjectId))]
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
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
                    BanterScene.Instance().events.OnLeaderBoardClear.Invoke(_board);
                });
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            board = ValueInput("Board", "");
        }
    }
}
#endif
