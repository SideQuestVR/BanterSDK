# Joint Components

## CharacterJoint

Human-like joint with swing and twist limits.

```js
obj.AddComponent(new BS.CharacterJoint({
    anchor: new BS.Vector3(0, 0, 0),
    axis: new BS.Vector3(1, 0, 0),
    swingAxis: new BS.Vector3(0, 1, 0),
    connectedAnchor: new BS.Vector3(0, 0, 0),
    autoConfigureConnectedAnchor: true,
    enableProjection: false,
    projectionDistance: 0.1,
    projectionAngle: 180,
    breakForce: Infinity,
    breakTorque: Infinity,
    enableCollision: false,
    connectedBody: "other-object-id"
}));
```

## FixedJoint

Locks two objects together.

```js
obj.AddComponent(new BS.FixedJoint({
    anchor: new BS.Vector3(0, 0, 0),
    connectedAnchor: new BS.Vector3(0, 0, 0),
    autoConfigureConnectedAnchor: true,
    breakForce: Infinity,
    breakTorque: Infinity,
    enableCollision: false,
    connectedBody: "other-object-id"
}));
```

## HingeJoint

Rotates around a single axis (like a door).

**IMPORTANT:** The `connectedBody` is the `rigidbody.id` on the other GameObject. Without it, the hinge connects to world space. You must link joints and their connected bodies together!

```js
obj.AddComponent(new BS.HingeJoint({
    anchor: new BS.Vector3(0, 0, 0),
    axis: new BS.Vector3(0, 1, 0),
    connectedAnchor: new BS.Vector3(0, 0, 0),
    autoConfigureConnectedAnchor: true,
    useLimits: true,
    limits: new BS.JointLimits({
        bounciness: 0,           // Bounce amount when hitting limit
        bounceMinVelocity: 0,    // Min velocity for bounce
        contactDistance: 0,      // Contact distance
        min: -45,                // Min angle in degrees
        max: 45                  // Max angle in degrees
    }),
    useMotor: false,
    useSpring: false,
    breakForce: Infinity,
    breakTorque: Infinity,
    enableCollision: false,
    connectedBody: otherRigidbody.id  // Always specify this!
}));
```

## SpringJoint

Elastic connection between objects.

```js
obj.AddComponent(new BS.SpringJoint({
    anchor: new BS.Vector3(0, 0, 0),
    connectedAnchor: new BS.Vector3(0, 0, 0),
    autoConfigureConnectedAnchor: true,
    spring: 10,          // Spring force
    damper: 0,           // Damping
    minDistance: 0,
    maxDistance: 1,
    tolerance: 0.025,
    breakForce: Infinity,
    breakTorque: Infinity,
    enableCollision: false,
    connectedBody: "other-object-id"
}));
```

## ConfigurableJoint

Fully customizable joint with per-axis control.

```js
obj.AddComponent(new BS.ConfigurableJoint({
    targetPosition: new BS.Vector3(0, 0, 0),
    targetRotation: new BS.Quaternion(0, 0, 0, 1),
    targetVelocity: new BS.Vector3(0, 0, 0),
    targetAngularVelocity: new BS.Vector3(0, 0, 0),
    xMotion: BS.ConfigurableJointMotion.Free,
    yMotion: BS.ConfigurableJointMotion.Free,
    zMotion: BS.ConfigurableJointMotion.Free,
    angularXMotion: BS.ConfigurableJointMotion.Free,
    angularYMotion: BS.ConfigurableJointMotion.Free,
    angularZMotion: BS.ConfigurableJointMotion.Free,
    anchor: new BS.Vector3(0, 0, 0),
    axis: new BS.Vector3(1, 0, 0),
    secondaryAxis: new BS.Vector3(0, 1, 0),
    connectedAnchor: new BS.Vector3(0, 0, 0),
    autoConfigureConnectedAnchor: true,
    configuredInWorldSpace: false,
    swapBodies: false,
    breakForce: Infinity,
    breakTorque: Infinity,
    enableCollision: false,
    connectedBody: "other-object-id"
}));
```
