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
    [UnitTitle("Set a Score on a Leaderboard")]
    [UnitShortTitle("SetScore")]
    [UnitCategory("Banter\\Leaderboard")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetScore : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;
    
        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput board;

        [DoNotSerialize]
        public ValueInput sort;

        [DoNotSerialize]
        public ValueInput score;

        [DoNotSerialize]
        public ValueInput unique;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _board = flow.GetValue<string>(board);
                var _sort = flow.GetValue<SortType>(sort);
                var _score = flow.GetValue<float>(score);
                var _unique = flow.GetValue<bool>(unique);
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() => {
                    BanterScene.Instance().events.OnLeaderBoardScore.Invoke(_board, _score, _sort == SortType.ASC ? "asc" : "desc", _unique);
                }, $"{nameof(SetScore)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            board = ValueInput("Board", "");
            sort = ValueInput("Sort", SortType.ASC);
            score = ValueInput("Score", 0f);
            unique = ValueInput("Unique", false);
        }
    }
}
#endif
