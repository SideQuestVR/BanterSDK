using Unity.VisualScripting;
using BS;
using UnityEngine;

namespace BS.VisualScripting
{
    /// <summary>
    /// Attaches an object to the local player, with its BS Attached Object's settings (bone, offsets, seat). The same as
    /// that component's Attach for "me". Always the local player: the client has no way to attach to anyone else.
    /// </summary>
    [UnitTitle("Attach To Player")]
    [UnitShortTitle("Attach To Player")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class AttachToPlayer : Unit
    {
        const string LocalPlayer = "me";

        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        [NullMeansSelf]
        public ValueInput target;

        [DoNotSerialize]
        public ValueOutput done;

        protected virtual bool Detaches => false;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var go = flow.GetValue<GameObject>(target);
                var attached = go != null ? go.GetComponent<BSAttachedObject>() : null;
                if (attached == null)
                {
                    Debug.LogWarning($"[VisualScripting] {(Detaches ? "Detach From" : "Attach To")} Player: '{(go != null ? go.name : "null")}' has no BS Attached Object.", go);
                }
                else if (Detaches)
                {
                    attached._Detach(LocalPlayer);
                }
                else
                {
                    attached._Attach(LocalPlayer);
                }
                flow.SetValue(done, attached != null);
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            target = ValueInput<GameObject>("Object", null).NullMeansSelf();
            done = ValueOutput<bool>(Detaches ? "Detached" : "Attached");

            Requirement(target, inputTrigger);
            Assignment(inputTrigger, done);
            Succession(inputTrigger, outputTrigger);
        }
    }

    /// <summary>Takes an object off the local player, or the local player off it: undoes <see cref="AttachToPlayer"/>.</summary>
    [UnitTitle("Detach From Player")]
    [UnitShortTitle("Detach From Player")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class DetachFromPlayer : AttachToPlayer
    {
        protected override bool Detaches => true;
    }
}
