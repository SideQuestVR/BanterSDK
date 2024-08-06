#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;

namespace Banter.VisualScripting
{
    [UnitTitle("Teleport To Location")]
    [UnitShortTitle("Teleport")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class TeleportTo : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput targetPosition;

        [DoNotSerialize]
        public ValueInput targetRotation;

        [DoNotSerialize]
        public ValueInput stopVelocity;
        [DoNotSerialize]
        public ValueInput isSpawn;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var position = flow.GetValue<Vector3>(targetPosition);
                var rotation = flow.GetValue<float>(targetRotation);
                var stop = flow.GetValue<bool>(stopVelocity);
                var spawn = flow.GetValue<bool>(isSpawn);
                BanterScene.Instance().mainThread?.Enqueue(() =>
                {
                    BanterScene.Instance().events.OnTeleport.Invoke(position, new Vector3(0f, rotation, 0f), stop, spawn);
                });
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            targetPosition = ValueInput("Position", Vector3.zero);
            targetRotation = ValueInput("Rotation", 0f);
            stopVelocity = ValueInput("Stop Velocity", false);
            isSpawn = ValueInput("Is Spawn", false);
        }
    }
}
#endif
