using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEngine;

namespace BS.VisualScripting
{
    /// <summary>
    /// Put a camera feed on the newsroom program bus (what "program" monitors show and program
    /// recordings capture), optionally lining up the next one on preview.
    /// </summary>
    /// <remarks>
    /// Feed ids are BSCameraFeed ids. Leave <c>Preview Feed Id</c> empty to keep the current
    /// preview. Every client's program monitors follow the cut; the new state arrives on
    /// <see cref="OnProgramChanged"/>. <c>Error</c> is empty when the cut was accepted, the app's
    /// error code otherwise, or <c>pending</c> if the app has not answered yet. Port keys are
    /// serialized into saved graphs: never rename them.
    /// </remarks>
    [UnitTitle("Cut To Feed")]
    [UnitShortTitle("Cut To Feed")]
    [UnitCategory("BS\\Recording")]
    [TypeIcon(typeof(Camera))]
    public class CutToFeed : Unit
    {
        [DoNotSerialize] public ControlInput inputTrigger;
        [DoNotSerialize] public ControlOutput outputTrigger;
        [DoNotSerialize] public ValueInput programFeedId;
        [DoNotSerialize] public ValueInput previewFeedId;
        [DoNotSerialize] public ValueOutput error;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", flow =>
            {
                var body = new JObject { ["program"] = flow.GetValue<string>(programFeedId) ?? "" };
                var preview = flow.GetValue<string>(previewFeedId);
                if (!string.IsNullOrEmpty(preview)) body["preview"] = preview;

                var reply = RecordingUnitSupport.RequestNow(RecordingUnitSupport.NewsroomExtension, "cut", body);
                flow.SetValue(error, RecordingUnitSupport.ErrorOf(reply));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");

            programFeedId = ValueInput("Program Feed Id", string.Empty);
            previewFeedId = ValueInput("Preview Feed Id", string.Empty);
            error = ValueOutput<string>("Error");

            Requirement(programFeedId, inputTrigger);
            Requirement(previewFeedId, inputTrigger);
            Succession(inputTrigger, outputTrigger);
            Assignment(inputTrigger, error);
        }
    }
}
