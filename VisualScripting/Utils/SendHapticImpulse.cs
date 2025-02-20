#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using UnityEngine.XR;
using Banter.SDK;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("Send Haptic Impulse")]
    [UnitShortTitle("Haptics")]
    [UnitCategory("Banter\\Utils")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SendHapticImpulse : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;
        
        [DoNotSerialize]
        public ControlOutput outputTrigger;
        
        // Amplitude of the haptic impulse (0 to 1)
        [DoNotSerialize]
        public ValueInput amplitude;
        
        // Duration of the haptic impulse in seconds
        [DoNotSerialize]
        public ValueInput duration;
        
        // XRNode representing which hand/controller to target (e.g., XRNode.LeftHand or XRNode.RightHand)
        [DoNotSerialize]
        public ValueInput hand;

        protected override void Definition()
        {
            // Define the control input trigger
            inputTrigger = ControlInput("In", (flow) =>
            {
                // Get input values from the node
                float amp = flow.GetValue<float>(amplitude);
                float dur = flow.GetValue<float>(duration);
                XRNode handNode = flow.GetValue<XRNode>(hand);

                // Enqueue on the main thread to ensure thread safety when accessing XR devices
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
                    InputDevice device = InputDevices.GetDeviceAtXRNode(handNode);
                    if (device.isValid)
                    {
                        if (device.TryGetHapticCapabilities(out HapticCapabilities capabilities) && capabilities.supportsImpulse)
                        {
                            // Send haptic impulse on channel 0
                            device.SendHapticImpulse(0, amp, dur);
                        }
                        else
                        {
                            Debug.LogWarning("The device does not support haptic impulses.");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("Invalid XR device for node: " + handNode);
                    }
                });
                return outputTrigger;
            });
            
            outputTrigger = ControlOutput("Out");

            // Define node inputs with default values
            amplitude = ValueInput("Amplitude", 0.5f);
            duration = ValueInput("Duration", 0.1f);
            hand = ValueInput("Hand", XRNode.RightHand);
        }
    }
}
#endif
