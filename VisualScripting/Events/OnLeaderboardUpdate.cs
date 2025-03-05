#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{

    [UnitTitle("On Leaderboard Update Received")]
    [UnitShortTitle("On Leaderboard Update")]
    [UnitCategory("Events\\Banter\\Networking")]
    [TypeIcon(typeof(BanterObjectId))]
    public class LeaderboardUpdate : EventUnit<CustomEventArgs>
    {

        [DoNotSerialize]
        public ValueOutput result;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnLeaderBoard");
        }

        protected override void Definition()
        {
            base.Definition();
            // Setting the value on our port.
            // id = ValueInput("Return ID", string.Empty);

            result = ValueOutput<UpdateScores>("Board Data");
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
