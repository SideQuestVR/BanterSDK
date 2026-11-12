using Unity.VisualScripting;
using UnityEngine;

namespace BS.VisualScripting
{
    /// <summary>
    /// The newsroom program changed: a cut, a new preview, or a new program take.
    /// </summary>
    /// <remarks>
    /// Fed by the app's <c>newsroom</c>/<c>program</c> host-extension event (the same one a page
    /// sees through <c>BS.Newsroom.on("program")</c>), on every client in the room.
    /// <c>Program Feed Id</c> and <c>Preview Feed Id</c> are BSCameraFeed ids (empty when unset);
    /// <c>Take Id</c> is the newsroom take the program belongs to (empty when nothing is being
    /// recorded); <c>JSON</c> is the whole event payload, for fields added later. Port keys are
    /// serialized into saved graphs: never rename them.
    /// </remarks>
    [UnitTitle("On Program Changed")]
    [UnitShortTitle("Program Changed")]
    [UnitCategory("Events\\BS\\Recording")]
    [TypeIcon(typeof(Camera))]
    public class OnProgramChanged : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize] public ValueOutput programFeedId;
        [DoNotSerialize] public ValueOutput previewFeedId;
        [DoNotSerialize] public ValueOutput takeId;
        [DoNotSerialize] public ValueOutput json;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference) => new EventHook(RecordingUnitSupport.ProgramHook);

        protected override void Definition()
        {
            base.Definition();
            programFeedId = ValueOutput<string>("Program Feed Id");
            previewFeedId = ValueOutput<string>("Preview Feed Id");
            takeId = ValueOutput<string>("Take Id");
            json = ValueOutput<string>("JSON");
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(programFeedId, data.arguments[0]);
            flow.SetValue(previewFeedId, data.arguments[1]);
            flow.SetValue(takeId, data.arguments[2]);
            flow.SetValue(json, data.arguments[3]);
        }
    }
}
