#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using System;

namespace Banter.VisualScripting
{
    
[Serializable]
public class Score{
    public string id;
    public string name;
    public float score;
}

[Serializable]
public class Board{
    public Score[] scores;
    public string sort;
}


[Serializable]
public class UpdateScores{
    public string board;
    public Board scores;
}


    [UnitTitle("On Leaderboard Update Received")]
    [UnitShortTitle("On Leaderboard Update")]
    [UnitCategory("Events\\Banter\\Networking")]
    [TypeIcon(typeof(BanterObjectId))]
    public class LeaderboardUpdate : EventUnit<CustomEventArgs>
    {

        [DoNotSerialize]
        public ValueOutput board;
        [DoNotSerialize]
        public ValueOutput scores;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnLeaderBoard");
        }

        protected override void Definition()
        {
            base.Definition();

            board = ValueOutput<string>("Board");
            scores = ValueOutput<Score[]>("Scores");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            return true;//data.name == flow.GetValue<string>(id)?.Trim();
        }

        // Setting the value on our port.
        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            var updateScores = (UpdateScores)data.arguments[0];
            flow.SetValue(board, updateScores.board);
            flow.SetValue(scores, updateScores.scores.scores);
        }
    }

    [UnitTitle("On Leaderboard Error Received")]
    [UnitShortTitle("On Leaderboard Error")]
    [UnitCategory("Events\\Banter\\Networking")]
    [TypeIcon(typeof(BanterObjectId))]
    public class LeaderboardError : EventUnit<CustomEventArgs>
    {

        [DoNotSerialize]
        public ValueOutput result;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnLeaderBoardError");
        }

        protected override void Definition()
        {
            base.Definition();
            // Setting the value on our port.
            // id = ValueInput("Return ID", string.Empty);

            result = ValueOutput<UpdateScores>("Error Data");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            return true;//data.name == flow.GetValue<string>(id)?.Trim();
        }

        // Setting the value on our port.
        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(result, data.arguments[0]);
        }
    }
}
#endif
