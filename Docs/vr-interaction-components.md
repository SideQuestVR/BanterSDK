# VR Interaction Components

## Grababble

Makes an object grabbable in VR with full input control.

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

## GrabHandle

Simple grab point on an object.

```js
obj.AddComponent(new BS.GrabHandle({
    grabType: BS.BSGrabType.Default,
    grabRadius: 0.01
}));
```

## HeldEvents

Handles input events while an object is held.

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

## AttachedObject

Attaches object to player body parts.

```js
const attached = obj.AddComponent(new BS.AttachedObject({
    attachmentType: BS.AttachmentType.RightHand
}));
```

**Methods:**

```js
attached.Attach("user-uid");   // Attach to the user with this uid
attached.Detach("user-uid");   // Detach from that user
```
