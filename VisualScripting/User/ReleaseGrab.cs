using Unity.VisualScripting;
using BS;
using UnityEngine;

namespace BS.VisualScripting
{
    /// <summary>Which of the local player's hands Release Grab lets go with.</summary>
    public enum GrabHandSide
    {
        Both,
        Left,
        Right,
    }

    /// <summary>
    /// Lets go of what the local player holds, as if they had let go themselves: the object's held events and the
    /// release event fire, and a synced object is dropped for everyone.
    /// </summary>
    [UnitTitle("Release Grab")]
    [UnitShortTitle("Release Grab")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class ReleaseGrab : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput target;

        [DoNotSerialize]
        public ValueInput hand;

        [DoNotSerialize]
        public ValueOutput released;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var _target = flow.GetValue<GameObject>(target);
                var _hand = flow.GetValue<GrabHandSide>(hand);
                var side = _hand == GrabHandSide.Left ? (int)HandSide.LEFT : _hand == GrabHandSide.Right ? (int)HandSide.RIGHT : -1;
                flow.SetValue(released, BSScene.Instance().data.ReleaseGrab(_target, side));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            // Empty: whatever the hand holds.
            target = ValueInput<GameObject>("Object", null).AllowsNull();
            hand = ValueInput("Hand", GrabHandSide.Both);
            released = ValueOutput<bool>("Released");

            Requirement(target, inputTrigger);
            Requirement(hand, inputTrigger);
            Assignment(inputTrigger, released);
            Succession(inputTrigger, outputTrigger);
        }
    }
}
