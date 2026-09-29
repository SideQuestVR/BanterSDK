using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEngine;

namespace BS.VisualScripting
{
    /// <summary>
    /// Start a local recording on this client: a camera feed, a texture (by asset reference, e.g. a
    /// browser's <c>asset_browser_&lt;id&gt;</c>), a camera, or the default view.
    /// </summary>
    /// <remarks>
    /// Sends <c>recording/start</c> to the app. <c>Take Id</c> is the new take's id and
    /// <c>Error</c> is empty on success; on failure <c>Error</c> holds the app's error code
    /// (<c>app_unavailable</c> in a build without recording), or <c>pending</c> if the app has not
    /// answered by the time the flow continues — the outcome then arrives on
    /// <see cref="OnRecordingStateChanged"/>, which fires for every start and stop anyway.
    ///
    /// <c>Preset</c> is a quality preset name — <c>Default</c>, <c>High</c>, <c>Ultra</c> or
    /// <c>Low</c> — or empty for the user's own setting.
    ///
    /// <c>Texture Ref</c> names a texture in the asset registry (<c>asset_...</c>); a browser's
    /// texture is <see cref="BSBrowser.TextureAssetId"/>. It is followed when the browser resizes.
    /// Precedence: Feed Id, then Texture Ref, then Camera.
    ///
    /// Port keys are serialized into saved graphs: never rename them.
    /// </remarks>
    [UnitTitle("Start Recording")]
    [UnitShortTitle("Start Recording")]
    [UnitCategory("BS\\Recording")]
    [TypeIcon(typeof(Camera))]
    public class StartRecording : Unit
    {
        [DoNotSerialize] public ControlInput inputTrigger;
        [DoNotSerialize] public ControlOutput outputTrigger;

        /// <summary>A BSCameraFeed id to record ("program" for the newsroom program). Optional.</summary>
        [DoNotSerialize] public ValueInput feedId;

        /// <summary>A texture reference (asset_..., e.g. a browser's asset_browser_&lt;id&gt;) to record when no feed is named. Optional.</summary>
        [DoNotSerialize] public ValueInput textureRef;

        /// <summary>A GameObject with a Camera to record, when neither a feed nor a texture is named. Optional.</summary>
        [DoNotSerialize] public ValueInput camera;

        /// <summary>A quality preset name (Default, High, Ultra, Low); empty for the user's own setting.</summary>
        [DoNotSerialize] public ValueInput preset;

        /// <summary>A title for the take; empty for the default.</summary>
        [DoNotSerialize] public ValueInput title;

        [DoNotSerialize] public ValueOutput takeId;
        [DoNotSerialize] public ValueOutput error;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", flow =>
            {
                var body = new JObject();
                var feed = flow.GetValue<string>(feedId);
                if (!string.IsNullOrEmpty(feed)) body["feedId"] = feed;
                var texture = flow.GetValue<string>(textureRef);
                if (!string.IsNullOrEmpty(texture)) body["texture"] = texture;
                var cameraObject = flow.GetValue<GameObject>(camera);
                if (cameraObject != null) body["cameraObject"] = cameraObject.GetInstanceID();
                var presetName = flow.GetValue<string>(preset);
                if (!string.IsNullOrEmpty(presetName)) body["preset"] = presetName;
                var takeTitle = flow.GetValue<string>(title);
                if (!string.IsNullOrEmpty(takeTitle)) body["title"] = takeTitle;

                var reply = RecordingUnitSupport.RequestNow(RecordingUnitSupport.RecordingExtension, "start", body, cameraObject);
                flow.SetValue(takeId, RecordingUnitSupport.StringOf(reply, "takeId"));
                flow.SetValue(error, RecordingUnitSupport.ErrorOf(reply));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");

            feedId = ValueInput("Feed Id", string.Empty);
            textureRef = ValueInput("Texture Ref", string.Empty);
            camera = ValueInput<GameObject>("Camera", null);
            preset = ValueInput("Preset", string.Empty);
            title = ValueInput("Title", string.Empty);

            takeId = ValueOutput<string>("Take Id");
            error = ValueOutput<string>("Error");

            Requirement(feedId, inputTrigger);
            Requirement(textureRef, inputTrigger);
            Requirement(camera, inputTrigger);
            Requirement(preset, inputTrigger);
            Requirement(title, inputTrigger);
            Succession(inputTrigger, outputTrigger);
            Assignment(inputTrigger, takeId);
            Assignment(inputTrigger, error);
        }
    }
}
