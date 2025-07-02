#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("Teleport To Location")]
    [UnitShortTitle("Teleport")]
    [UnitCategory("Banter\\User")]
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
        public ValueInput targetRotationVector;

        [DoNotSerialize]
        public ValueInput stopVelocity;
        [DoNotSerialize]
        public ValueInput isSpawn;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var position = flow.GetValue<Vector3>(targetPosition);
                var rotation = flow.GetValue<float>(targetRotation);
                var rotationVec = flow.GetValue<Vector3>(targetRotationVector);
                var stop = flow.GetValue<bool>(stopVelocity);
                var spawn = flow.GetValue<bool>(isSpawn);
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() =>
                {
                    BanterScene.Instance().events.OnTeleport.Invoke(position, rotation > 0 ? new Vector3(0f, rotation, 0f) : rotationVec, stop, spawn);
                }, $"{nameof(TeleportTo)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            targetPosition = ValueInput("Position", Vector3.zero);
            targetRotation = ValueInput("Rotation", 0f);
            targetRotationVector = ValueInput("Rotation Vector", Vector3.zero);
            stopVelocity = ValueInput("Stop Velocity", false);
            isSpawn = ValueInput("Is Spawn", false);
        }
    }
}
#endif
