using Unity.VisualScripting;
using UnityEngine;
using BS;

namespace BS.VisualScripting
{
    /// <summary>
    /// Fires once the local player has been teleported: by a script, a portal, a seat, arriving, respawning from the
    /// menu, or the app catching a fall out of the world. Is Spawn is true for the last three (and for a Teleport To
    /// Location with Is Spawn), so a world can tell a respawn from the rest.
    /// </summary>
    [UnitTitle("On Player Teleported")]
    [UnitShortTitle("Player Teleported")]
    [UnitCategory("Events\\BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class OnPlayerTeleported : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueOutput position;

        [DoNotSerialize]
        public ValueOutput rotation;

        [DoNotSerialize]
        public ValueOutput isSpawn;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnPlayerTeleported");
        }

        protected override void Definition()
        {
            base.Definition();
            position = ValueOutput<Vector3>("Position");
            rotation = ValueOutput<Vector3>("Rotation");
            isSpawn = ValueOutput<bool>("Is Spawn");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            return true;
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(position, data.arguments[0]);
            flow.SetValue(rotation, data.arguments[1]);
            flow.SetValue(isSpawn, data.arguments[2]);
        }
    }
}
