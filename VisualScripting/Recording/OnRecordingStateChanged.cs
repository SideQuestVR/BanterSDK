using Unity.VisualScripting;
using UnityEngine;

namespace BS.VisualScripting
{
    /// <summary>
    /// This client's recorder changed state: started, stopping, saved, failed.
    /// </summary>
    /// <remarks>
    /// Fed by the app's <c>recording</c>/<c>state</c> host-extension event (the same one a page
    /// sees through <c>BS.Recording.on("state")</c>). <c>State</c> is the recorder state name —
    /// <c>Idle</c>, <c>Starting</c>, <c>Recording</c>, <c>Finalizing</c> or <c>Faulted</c>;
    /// <c>Take Id</c> the take it concerns (empty when none); <c>Error</c> why a take failed (empty
    /// otherwise); <c>JSON</c> the whole event payload, for fields added later. Port keys are
    /// serialized into saved graphs: never rename them.
    /// </remarks>
    [UnitTitle("On Recording State Changed")]
    [UnitShortTitle("Recording State Changed")]
    [UnitCategory("Events\\BS\\Recording")]
    [TypeIcon(typeof(Camera))]
    public class OnRecordingStateChanged : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize] public ValueOutput state;
        [DoNotSerialize] public ValueOutput takeId;
        [DoNotSerialize] public ValueOutput error;
        [DoNotSerialize] public ValueOutput json;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference) => new EventHook(RecordingUnitSupport.StateHook);

        protected override void Definition()
        {
            base.Definition();
            state = ValueOutput<string>("State");
            takeId = ValueOutput<string>("Take Id");
            error = ValueOutput<string>("Error");
            json = ValueOutput<string>("JSON");
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(state, data.arguments[0]);
            flow.SetValue(takeId, data.arguments[1]);
            flow.SetValue(error, data.arguments[2]);
            flow.SetValue(json, data.arguments[3]);
        }
    }
}
