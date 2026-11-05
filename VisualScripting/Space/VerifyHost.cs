using Unity.VisualScripting;
using UnityEngine;

namespace BS.VisualScripting
{
    /// <summary>
    /// The graph twin of <see cref="BSVerifyHost"/>: checks the address the world is running from and runs the outcome
    /// at any other. The check waits for the space to load, so the graph carries on later, from Verified or Wrong Host,
    /// in a new flow: values from before this node are gone by then, so pass them through variables. Verified also runs
    /// when the check couldn't tell, which logs a warning.
    /// </summary>
    [UnitTitle("Verify Host")]
    [UnitShortTitle("Verify Host")]
    [UnitCategory("BS\\Space")]
    [TypeIcon(typeof(BSObjectId))]
    public class VerifyHost : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput verified;

        [DoNotSerialize]
        public ControlOutput wrongHost;

        [DoNotSerialize]
        public ValueInput url;

        [DoNotSerialize]
        public ValueInput match;

        [DoNotSerialize]
        public ValueInput outcome;

        [DoNotSerialize]
        public ValueInput teleportPosition;

        [DoNotSerialize]
        public ValueInput worldUrl;

        [DoNotSerialize]
        public ValueInput errorMessage;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var reference = flow.stack.ToReference();
                // On the graph's machine, so the check goes when the graph does.
                flow.stack.component.StartCoroutine(VerifyHostCheck.Run(
                    flow.GetValue<string>(url),
                    flow.GetValue<VerifyHostMatch>(match),
                    flow.GetValue<VerifyHostOutcome>(outcome),
                    flow.GetValue<Vector3>(teleportPosition),
                    flow.GetValue<string>(worldUrl),
                    flow.GetValue<string>(errorMessage),
                    isWrongHost =>
                    {
                        if (reference.isValid)
                        {
                            // A coroutine flow, so a Wait node can follow.
                            Flow.New(reference).StartCoroutine(isWrongHost ? wrongHost : verified);
                        }
                    }));
                return null;
            });
            verified = ControlOutput("Verified");
            wrongHost = ControlOutput("Wrong Host");
            url = ValueInput("URL", string.Empty);
            match = ValueInput("Match", VerifyHostMatch.Domain);
            outcome = ValueInput("Outcome", VerifyHostOutcome.TeleportToPosition);
            teleportPosition = ValueInput("Teleport Position", Vector3.zero);
            worldUrl = ValueInput("World URL", string.Empty);
            errorMessage = ValueInput("Error Message", VerifyHostCheck.DefaultErrorMessage);

            Requirement(url, inputTrigger);
            Requirement(match, inputTrigger);
            Requirement(outcome, inputTrigger);
            Requirement(teleportPosition, inputTrigger);
            Requirement(worldUrl, inputTrigger);
            Requirement(errorMessage, inputTrigger);
            Succession(inputTrigger, verified);
            Succession(inputTrigger, wrongHost);
        }
    }
}
