# Physics Components

## Rigidbody

Adds physics simulation to an object.

```js
const rb = obj.AddComponent(new BS.Rigidbody({
    mass: 1,                    // Weight (default: 1)
    drag: 0,                    // Linear drag (default: 0)
    angularDrag: 0.05,          // Rotational drag (default: 0.05)
    useGravity: true,           // Affected by gravity (default: true)
    isKinematic: false,         // Ignore physics forces (default: false)
    centerOfMass: new BS.Vector3(0, 0, 0),
    velocity: new BS.Vector3(0, 0, 0),
    angularVelocity: new BS.Vector3(0, 0, 0),
    collisionDetectionMode: BS.CollisionDetectionMode.Continuous,
    freezePositionX: false,
    freezePositionY: false,
    freezePositionZ: false,
    freezeRotationX: false,
    freezeRotationY: false,
    freezeRotationZ: false
}));
```

**Methods:**

```js
// Apply forces
rb.AddForce(new BS.Vector3(0, 10, 0), BS.ForceMode.Impulse);
rb.AddForceValues(0, 10, 0, BS.ForceMode.Force);
rb.AddRelativeForce(new BS.Vector3(0, 0, 10), BS.ForceMode.Force);
rb.AddForceAtPosition(force, position, BS.ForceMode.Impulse);

// Apply torque (rotation force)
rb.AddTorque(new BS.Vector3(0, 5, 0), BS.ForceMode.Force);
rb.AddTorqueValues(0, 5, 0, BS.ForceMode.Force);
rb.AddRelativeTorque(new BS.Vector3(0, 5, 0), BS.ForceMode.Force);

// Explosion force
rb.AddExplosionForce(100, explosionCenter, 10, 1, BS.ForceMode.Impulse);

// Kinematic movement
rb.MovePosition(new BS.Vector3(0, 5, 0));
rb.MoveRotation(new BS.Quaternion(0, 0, 0, 1));

// Sleep state
rb.Sleep();
rb.WakeUp();

// Reset
rb.ResetCenterOfMass();
rb.ResetInertiaTensor();
```

**Properties (get/set):**

```js
rb.velocity = new BS.Vector3(0, 5, 0);
rb.angularVelocity = new BS.Vector3(0, 1, 0);
rb.mass = 2;
rb.drag = 0.1;
rb.useGravity = false;
rb.isKinematic = true;
```

## BoxCollider

Box-shaped collision volume.

```js
obj.AddComponent(new BS.BoxCollider({
    isTrigger: false,                      // Trigger mode (no physics response)
    center: new BS.Vector3(0, 0, 0),       // Offset from object center
    size: new BS.Vector3(1, 1, 1)          // Box dimensions
}));
```

## SphereCollider

Sphere-shaped collision volume.

```js
obj.AddComponent(new BS.SphereCollider({
    isTrigger: false,
    radius: 0.5        // Sphere radius (default: 0.5)
}));
```

## CapsuleCollider

Capsule-shaped collision volume (cylinder with hemisphere ends).

```js
obj.AddComponent(new BS.CapsuleCollider({
    isTrigger: false,
    radius: 0.5,       // Capsule radius (default: 0.5)
    height: 2          // Total height including caps (default: 2)
}));
```

## MeshCollider

Uses the object's mesh for collision (more expensive).

```js
obj.AddComponent(new BS.MeshCollider({
    isTrigger: false,
    convex: true       // Required for rigidbody interaction
}));
```

## ColliderEvents

Enables collision and trigger events on the GameObject. Required for `collision-enter`, `collision-exit`, `trigger-enter`, `trigger-exit` events.

```js
obj.AddComponent(new BS.ColliderEvents());
```

## PhysicMaterial

Controls surface friction.

```js
obj.AddComponent(new BS.PhysicMaterial({
    dynamicFriction: 0.6,  // Friction when moving
    staticFriction: 0.6    // Friction when stationary
}));
```

## PhysicsMaterial

Full surface material: friction, bounce, and how the two combine between touching surfaces. The older `PhysicMaterial` only exposes the two friction values — use `PhysicsMaterial` when you also need bounciness and combine control.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `dynamicFriction` | number | 1 | Friction while the object is moving |
| `staticFriction` | number | 1 | Friction while the object is at rest |
| `bounciness` | number | 1 | How bouncy the surface is |
| `frictionCombine` | number | 0 | How friction of two touching surfaces combines |
| `bounceCombine` | number | 0 | How bounciness of two touching surfaces combines |

```js
obj.AddComponent(new BS.PhysicsMaterial({
    dynamicFriction: 0.4,
    staticFriction: 0.6,
    bounciness: 0.8,
    frictionCombine: 0,     // 0 Average, 1 Multiply, 2 Minimum, 3 Maximum
    bounceCombine: 3
}));
```
