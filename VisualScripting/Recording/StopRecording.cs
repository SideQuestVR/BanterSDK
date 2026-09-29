using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEngine;

namespace BS.VisualScripting
{
    /// <summary>
    /// Stop this client's recording. The file is finalised in the background; the saved (or
    /// failed) outcome arrives on <see cref="OnRecordingStateChanged"/>.
    /// </summary>
    /// <remarks>
    /// <c>Error</c> is empty when the stop was accepted, the app's code otherwise, or
    /// <c>pending</c> if the app has not answered yet. Port keys are serialized into saved graphs:
    /// never rename them.
    /// </remarks>
    [UnitTitle("Stop Recording")]
    [UnitShortTitle("Stop Recording")]
    [UnitCategory("BS\\Recording")]
    [TypeIcon(typeof(Camera))]
    public class StopRecording : Unit
    {
        [DoNotSerialize] public ControlInput inputTrigger;
        [DoNotSerialize] public ControlOutput outputTrigger;
        [DoNotSerialize] public ValueOutput error;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", flow =>
            {
                var reply = RecordingUnitSupport.RequestNow(RecordingUnitSupport.RecordingExtension, "stop", new JObject());
                flow.SetValue(error, RecordingUnitSupport.ErrorOf(reply));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            error = ValueOutput<string>("Error");

            Succession(inputTrigger, outputTrigger);
            Assignment(inputTrigger, error);
        }
    }
}
