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
    [UnitCategory("Banter\\Networking")]
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
        public ValueInput room;

        [DoNotSerialize]
        public ValueInput score;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _board = flow.GetValue<string>(board);
                var _room = flow.GetValue<string>(room);
                var _sort = flow.GetValue<SortType>(sort);
                var _score = flow.GetValue<float>(score);
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
                    BanterScene.Instance().events.OnLeaderBoardScore.Invoke(_room, _board, _score, _sort == SortType.ASC ? "asc" : "desc");
                });
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            board = ValueInput("Board", "");
            room = ValueInput("Room", "");
            sort = ValueInput("Sort", SortType.ASC);
            score = ValueInput("Score", 0f);
        }
    }
}
#endif
