
using SideQuest.FlexaBody;
using BS;
using Unity.VisualScripting;
using UnityEngine;

[RequireComponent(typeof(BSObjectId))]
public class ControllerHeldEvents : Controllable {

    public BSPlayerEvents banterEvents;
    public override void OnTrigger(float input) {
        EventBus.Trigger("OnTrigger", new CustomEventArgs(gameObject.GetInstanceID().ToString(), new object[] { input, handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT }));
        if (banterEvents) banterEvents.onTrigger?.Invoke(input, handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT);
    }

    public override void OnGunTrigger() {
        EventBus.Trigger("OnGunTrigger", new CustomEventArgs(gameObject.GetInstanceID().ToString(), new object[] { handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT }));
        if (banterEvents) banterEvents.onGunTrigger?.Invoke(handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT);
    }

    public override void OnPrimaryDown() {
        EventBus.Trigger("OnPrimaryDown", new CustomEventArgs(gameObject.GetInstanceID().ToString(), new object[] { handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT }));
        if (banterEvents) banterEvents.onPrimaryDown?.Invoke(handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT);
    }

    public override void OnPrimaryUp() {
        EventBus.Trigger("OnPrimaryUp", new CustomEventArgs(gameObject.GetInstanceID().ToString(), new object[] { handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT }));
        if (banterEvents) banterEvents.onPrimaryup?.Invoke(handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT);
    }

    public override void OnSecondaryDown() {
        EventBus.Trigger("OnSecondaryDown", new CustomEventArgs(gameObject.GetInstanceID().ToString(), new object[] { handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT }));
        if (banterEvents) banterEvents.onSecondaryDown?.Invoke(handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT);
    }

    public override void OnSecondaryUp() {
        EventBus.Trigger("OnSecondaryUp", new CustomEventArgs(gameObject.GetInstanceID().ToString(), new object[] { handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT }));
        if (banterEvents) banterEvents.onSecondaryUp?.Invoke(handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT);
    }

    public override void OnThumbClickDown() {
        EventBus.Trigger("OnThumbClickDown", new CustomEventArgs(gameObject.GetInstanceID().ToString(), new object[] { handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT }));
        if (banterEvents) banterEvents.onThumbstickClickDown?.Invoke(handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT);
    }

    public override void OnThumbClickUp() {
        EventBus.Trigger("OnThumbClickUp", new CustomEventArgs(gameObject.GetInstanceID().ToString(), new object[] { handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT }));
        if (banterEvents) banterEvents.onThumbstickClickUp?.Invoke(handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT);
    }

    public override void OnThumbstick(Vector2 input) {
        EventBus.Trigger("OnThumbstick", new CustomEventArgs(gameObject.GetInstanceID().ToString(), new object[] { input, handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT }));
        if (banterEvents) banterEvents.onThumbstick?.Invoke(input, handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT);
    }

    public override void OnGrab() {
        EventBus.Trigger("OnGrab", new CustomEventArgs(gameObject.GetInstanceID().ToString(), new object[] { handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT }));
        if (banterEvents) banterEvents.onGrab?.Invoke(handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT);
    }

    public override void OnRelease() {
        EventBus.Trigger("OnRelease", new CustomEventArgs(gameObject.GetInstanceID().ToString(), new object[] { handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT }));
        if (banterEvents) banterEvents.onRelease?.Invoke( handID == HandID.Left ? HandSide.LEFT : HandSide.RIGHT);
    }
}