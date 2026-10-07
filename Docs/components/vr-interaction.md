# VR Interaction Components

## Grababble

Makes an object grabbable in VR with full input control.

<div class="docs-tabs">

```js
obj.AddComponent(new BS.Grababble({
    grabType: BS.BSGrabType.Default,
    grabRadius: 0.01,
    gunTriggerSensitivity: 0.5,
    gunTriggerFireRate: 0.1,
    gunTriggerAutoFire: false,
    // Input blocking while held
    blockLeftPrimary: false,
    blockLeftSecondary: false,
    blockRightPrimary: false,
    blockRightSecondary: false,
    blockLeftThumbstick: false,
    blockLeftThumbstickClick: false,
    blockRightThumbstick: false,
    blockRightThumbstickClick: false,
    blockLeftTrigger: false,
    blockRightTrigger: false
}));
```

![BS Grababble in the Unity Inspector](../images/components/grababble.png)

</div>

## GrabHandle

Simple grab point on an object.

<div class="docs-tabs">

```js
obj.AddComponent(new BS.GrabHandle({
    grabType: BS.BSGrabType.Default,
    grabRadius: 0.01
}));
```

![BS Grab Handle in the Unity Inspector](../images/components/grab-handle.png)

</div>

## HeldEvents

Handles input events while an object is held.

<div class="docs-tabs">

```js
obj.AddComponent(new BS.HeldEvents({
    sensitivity: 0.5,
    fireRate: 0.1,
    auto: false,           // Auto-fire
    blockLeftPrimary: false,
    blockLeftSecondary: false,
    blockRightPrimary: false,
    blockRightSecondary: false,
    blockLeftThumbstick: false,
    blockLeftThumbstickClick: false,
    blockRightThumbstick: false,
    blockRightThumbstickClick: false,
    blockLeftTrigger: false,
    blockRightTrigger: false
}));
```

![BS Held Events in the Unity Inspector](../images/components/held-events.png)

</div>

## AttachedObject

Attaches object to player body parts.

<div class="docs-tabs">

```js
const attached = obj.AddComponent(new BS.AttachedObject({
    attachmentType: BS.AttachmentType.RightHand
}));
```

![BS Attached Object in the Unity Inspector](../images/components/attached-object.png)

</div>

**Methods:**

```js
attached.Attach("user-uid");   // Attach to the user with this uid
attached.Detach("user-uid");   // Detach from that user
```
