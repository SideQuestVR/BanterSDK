#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("Add Force To Player")]
    [UnitShortTitle("AddForceToPlayer")]
    [UnitCategory("Banter\\Space")]
    [TypeIcon(typeof(Rigidbody))]
    public class AddForceToPlayer : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;
        
        [DoNotSerialize]
        public ControlOutput outputTrigger;
        
        [DoNotSerialize]
        public ValueInput force;
        
        [DoNotSerialize]
        public ValueInput forceMode;

        protected override void Definition()
        {
            // Define the control flow: when triggered, perform the force application
            inputTrigger = ControlInput("In", (flow) =>
            {
                // Get the force vector and the force mode from the node inputs
                Vector3 forceVector = flow.GetValue<Vector3>(force);
                ForceMode mode = flow.GetValue<ForceMode>(forceMode);
                
                // Ensure that the code runs on the main thread
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
                    // Retrieve the player's Rigidbody using its tag
                    Rigidbody playerRigidbody = GameObject.FindGameObjectWithTag("__BA_LocalPlayerFeet")?.GetComponent<Rigidbody>();
                    if (playerRigidbody != null)
                    {
                        playerRigidbody.AddForce(forceVector, mode);
                    }
                    else
                    {
                        Debug.LogWarning("Player Rigidbody not found with tag '__BA_LocalPlayerFeet'.");
                    }
                });
                return outputTrigger;
            });
            
            outputTrigger = ControlOutput("Out");
            
            // Define the inputs: a force vector and a force mode, with default values.
            force = ValueInput("Force", Vector3.zero);
            forceMode = ValueInput("Force Mode", ForceMode.Force);
        }
    }
}
#endif
