# Enums & Constants

These are the enums on `window.BS`. Apart from `BS.PlayerTag` and `BS.UIPropertyName`, whose members are
strings, each member is a number (the value in the comments), and where an enum mirrors one of Unity's, the
numbers are Unity's own.

| Enum | Used for |
|------|----------|
| [`BS.ComponentType`](#componenttype-bsct) (`BS.CT`) | Component types, for `GetComponent()` |
| [`BS.PropertyName`](#propertyname-bspn) (`BS.PN`) | Property ids, for queries and watches |
| [`BS.BSLayers`](#bslayers-bsl) (`BS.L`) | Layer numbers |
| [`BS.ForceMode`](#forcemode) | Rigidbody forces |
| [`BS.HandSide`](#handside) | Which hand |
| [`BS.ButtonType`](#buttontype) | Controller buttons |
| [`BS.KeyCode`](#keycode) | Keyboard keys |
| [`BS.GeometryType`](#geometrytype), [`BS.ParametricGeometryType`](#parametricgeometrytype) | Geometry shapes |
| [`BS.MaterialSide`](#materialside) | Which side of a surface renders |
| [`BS.LightType`](#lighttype), [`BS.LightShadows`](#lightshadows) | Lights |
| [`BS.HorizontalAlignment`](#horizontalalignment), [`BS.VerticalAlignment`](#verticalalignment) | Text alignment |
| [`BS.CollisionDetectionMode`](#collisiondetectionmode) | Rigidbody collision quality |
| [`BS.PhysicsMaterialCombine`](#physicsmaterialcombine) | Physics material friction and bounce |
| [`BS.ConfigurableJointMotion`](#configurablejointmotion), [`BS.RotationDriveMode`](#rotationdrivemode) | Joints |
| [`BS.BSGrabType`](#bsgrabtype) | Grab handles |
| [`BS.AttachmentType`](#attachment-enums), [`BS.PhysicsAttachmentPoint`](#attachment-enums), [`BS.AvatarAttachmentType`](#attachment-enums), [`BS.AvatarBoneName`](#attachment-enums) | Attached objects |
| [`BS.PlayerTag`](global-functions.md#global-functions) | The tags on the local player (strings) |
| `BS.LegacyAttachmentPosition` | [Legacy APIs](legacy-apis.md) |
| `BS.UIElementType`, `BS.UIPropertyName` | The [UI system](../components/ui-system.md) |

`BS.UICommands` is exported too, but it's the UI system's own plumbing.

## ComponentType (BS.CT)

Used with `GetComponent()`. Shorthand: `BS.CT`

```js
BS.CT.Transform
BS.CT.Rigidbody
BS.CT.BoxCollider
BS.CT.SphereCollider
BS.CT.CapsuleCollider
BS.CT.MeshCollider
BS.CT.AudioSource
BS.CT.GLTF
BS.CT.Material
BS.CT.Text
BS.CT.Light
// ... and more
```

## ForceMode

Physics force application:

```js
BS.ForceMode.Force          // 0: Continuous force (affected by mass)
BS.ForceMode.Impulse        // 1: Instant force (affected by mass)
BS.ForceMode.VelocityChange // 2: Direct velocity change (ignores mass)
BS.ForceMode.Acceleration   // 5: Continuous acceleration (ignores mass)
```

## HandSide

VR controller hand:

```js
BS.HandSide.LEFT   // 0
BS.HandSide.RIGHT  // 1
```

## ButtonType

Controller buttons:

```js
BS.ButtonType.TRIGGER          // 0
BS.ButtonType.GRIP             // 1
BS.ButtonType.PRIMARY          // 2: A/X button
BS.ButtonType.SECONDARY        // 3: B/Y button
BS.ButtonType.THUMBSTICKCLICK  // 4: pressing the thumbstick down
```

## KeyCode

Keyboard keys, for the `key-press` event. The names and numbers are Unity's `KeyCode`:

```js
BS.KeyCode.Space    // 32
BS.KeyCode.Return   // 13
BS.KeyCode.Escape   // 27
BS.KeyCode.A        // 97 (A to Z are 97 to 122)
BS.KeyCode.Alpha1   // 49 (the 1 key above the letters)
BS.KeyCode.UpArrow  // 273
BS.KeyCode.F1       // 282
// ... every Unity KeyCode
```

## GeometryType

Procedural geometry shapes:

```js
BS.GeometryType.BoxGeometry           // 0
BS.GeometryType.CircleGeometry        // 1
BS.GeometryType.ConeGeometry          // 2
BS.GeometryType.CylinderGeometry      // 3
BS.GeometryType.PlaneGeometry         // 4
BS.GeometryType.RingGeometry          // 5
BS.GeometryType.SphereGeometry        // 6
BS.GeometryType.TorusGeometry         // 7
BS.GeometryType.TorusKnotGeometry     // 8
BS.GeometryType.ParametricGeometry    // 9
BS.GeometryType.CapsuleGeometry       // 10
BS.GeometryType.DodecahedronGeometry  // 11
BS.GeometryType.IcosahedronGeometry   // 12
BS.GeometryType.OctahedronGeometry    // 13
BS.GeometryType.TetrahedronGeometry   // 14
BS.GeometryType.LatheGeometry         // 15
BS.GeometryType.TubeGeometry          // 16
BS.GeometryType.ExtrudeGeometry       // 17
BS.GeometryType.ShapeGeometry         // 18
```

Every geometry defaults to a mesh that fits inside a 1×1×1m box centred on the pivot, so shapes
can be mixed without rescaling each one by hand.

`radius` is the shape's **overall** radius. For `TorusGeometry` and `TorusKnotGeometry` that means
the outer radius rather than three.js's ring radius, so that one field means the same thing for
every shape.

The last four types are driven by author-supplied geometry rather than scalar parameters:

```js
// A square with a circular hole. "M" starts a contour, "H" switches to holes.
const shapePoints = JSON.stringify({ commands: [
  { type: 'M', x: -0.5, y: -0.5 }, { type: 'L', x: 0.5, y: -0.5 },
  { type: 'L', x: 0.5, y: 0.5 },   { type: 'L', x: -0.5, y: 0.5 }, { type: 'Z' },
  { type: 'H' },
  { type: 'M', x: 0.25, y: 0 },
  { type: 'A', x: 0, y: 0, radiusX: 0.25, radiusY: 0.25, startAngle: 0, endAngle: 6.283185 },
]});

// A closed loop for a tube to sweep along.
const curvePoints = JSON.stringify({
  type: 'CatmullRom', closed: true, curveType: 'centripetal',
  points: [{ x: -0.4, y: 0, z: -0.4 }, { x: 0.4, y: 0.2, z: -0.4 },
           { x: 0.4, y: 0, z: 0.4 },   { x: -0.4, y: -0.2, z: 0.4 }],
});
```

Command letters are `M` moveTo, `L` lineTo, `C` cubic bezier, `Q` quadratic bezier, `S` spline
through, `A` arc/ellipse, `Z` close, `H` begin holes. Curve `type` is `CatmullRom`, `Line` or
`Path`.

`ExtrudeGeometry` does not bevel, and there are no edge or wireframe geometry types.

## ParametricGeometryType

The surfaces a `ParametricGeometry` can draw:

```js
BS.ParametricGeometryType.Klein     // 0
BS.ParametricGeometryType.Apple     // 1
BS.ParametricGeometryType.Fermet    // 2
BS.ParametricGeometryType.Catenoid  // 3
BS.ParametricGeometryType.Helicoid  // 4
BS.ParametricGeometryType.Horn      // 5
BS.ParametricGeometryType.Mobius    // 6
BS.ParametricGeometryType.Mobius3d  // 7
BS.ParametricGeometryType.Natica    // 8
BS.ParametricGeometryType.Pillow    // 9
BS.ParametricGeometryType.Scherk    // 10
BS.ParametricGeometryType.Snail     // 11
BS.ParametricGeometryType.Spiral    // 12
BS.ParametricGeometryType.Spring    // 13
BS.ParametricGeometryType.Custom    // 14
```

## PropertyName (BS.PN)

Property identifiers for watching/querying. Shorthand: `BS.PN`

```js
BS.PN.position
BS.PN.localPosition
BS.PN.rotation
BS.PN.localRotation
BS.PN.localScale
BS.PN.eulerAngles
BS.PN.localEulerAngles
BS.PN.velocity
BS.PN.angularVelocity
BS.PN.text
BS.PN.fontSize
// ... and more
```

## BSLayers (BS.L)

Physics/rendering layers. Shorthand: `BS.L`

```js
BS.L.Default       // 0
BS.L.TransparentFX // 1
BS.L.IgnoreRaycast // 2 (Unity's "Ignore Raycast")
BS.L.Water         // 4
BS.L.UI            // 5
BS.L.UserLayer1    // 3
BS.L.UserLayer2    // 6
BS.L.UserLayer3    // 7
// ... through UserLayer12 (16)
BS.L.UserLayer13   // 17
BS.L.UserLayer14   // 18
BS.L.UserLayer15   // 19
BS.L.Grabbable     // 20
BS.L.Invisible     // 21
BS.L.Menu          // 22
BS.L.CharacterColliders     // 23
BS.L.CharacterHandColliders // 24
```

What each layer does is in [Layers](../building-in-unity/layers.md). Only objects on `UI` and `Menu`
can be clicked.

## MaterialSide

Which side of geometry to render:

```js
BS.MaterialSide.Front   // 0
BS.MaterialSide.Back    // 1
BS.MaterialSide.Double  // 2
```

## LightType

Light source types:

```js
BS.LightType.Spot         // 0: Flashlight-like (a Light's default)
BS.LightType.Directional  // 1: Sun-like
BS.LightType.Point        // 2: Bulb-like
BS.LightType.Rectangle    // 3
BS.LightType.Disc         // 4
BS.LightType.Pyramid      // 5
BS.LightType.Box          // 6
BS.LightType.Tube         // 7
```

Only `Spot`, `Directional` and `Point` light anything while the world runs. The rest are Unity's other
light types: `Rectangle` and `Disc` are area lights, which only count when lighting is baked, and the last
three don't work in the client's render pipeline.

## LightShadows

Shadow quality:

```js
BS.LightShadows.None  // 0
BS.LightShadows.Hard  // 1
BS.LightShadows.Soft  // 2
```

## HorizontalAlignment

Text horizontal alignment:

```js
BS.HorizontalAlignment.Left    // 0
BS.HorizontalAlignment.Center  // 1
BS.HorizontalAlignment.Right   // 2
```

## VerticalAlignment

Text vertical alignment:

```js
BS.VerticalAlignment.Top     // 0
BS.VerticalAlignment.Center  // 1
BS.VerticalAlignment.Bottom  // 2
```

## CollisionDetectionMode

Physics collision quality:

```js
BS.CollisionDetectionMode.Discrete               // 0
BS.CollisionDetectionMode.Continuous             // 1
BS.CollisionDetectionMode.ContinuousDynamic      // 2
BS.CollisionDetectionMode.ContinuousSpeculative  // 3
```

## PhysicsMaterialCombine

How two touching colliders' friction or bounciness combine:

```js
BS.PhysicsMaterialCombine.Average   // 0
BS.PhysicsMaterialCombine.Multiply  // 1
BS.PhysicsMaterialCombine.Minimum   // 2
BS.PhysicsMaterialCombine.Maximum   // 3
```

## ConfigurableJointMotion

Joint axis constraints:

```js
BS.ConfigurableJointMotion.Locked   // 0
BS.ConfigurableJointMotion.Limited  // 1
BS.ConfigurableJointMotion.Free     // 2
```

## RotationDriveMode

How a ConfigurableJoint drives rotation:

```js
BS.RotationDriveMode.XYAndZ  // 0
BS.RotationDriveMode.Slerp   // 1
```

## BSGrabType

The shape of a grab handle's grip:

```js
BS.BSGrabType.Point     // 0
BS.BSGrabType.Cylinder  // 1
BS.BSGrabType.Ball      // 2
BS.BSGrabType.Soft      // 3
```

## Attachment Enums

Used by `AttachedObject`; see [Attaching Objects to Users](../javascript-api/user-multiplayer.md#attaching-objects-to-users).

```js
BS.AttachmentType.Physics        // 0: held by a physics joint
BS.AttachmentType.NonPhysics     // 1: follows the point

BS.PhysicsAttachmentPoint.Head       // 0
BS.PhysicsAttachmentPoint.LeftHand   // 1
BS.PhysicsAttachmentPoint.RightHand  // 2
BS.PhysicsAttachmentPoint.Torso      // 3

BS.AvatarAttachmentType.AttachToAvatar  // 0: the object goes on the player
BS.AvatarAttachmentType.AvatarAttachTo  // 1: the player goes on the object

BS.AvatarBoneName.HEAD    // 0
BS.AvatarBoneName.NECK    // 1
BS.AvatarBoneName.HIPS    // 2
BS.AvatarBoneName.SPINE   // 3
BS.AvatarBoneName.CHEST   // 4
// LEFTARM_SHOULDER, _UPPER, _LOWER, _HAND: 5 to 8; RIGHTARM_ the same: 9 to 12
// LEFTLEG_UPPER, _LOWER, _FOOT, _TOES: 13 to 16; RIGHTLEG_ the same: 17 to 20
// LEFTARM_HAND_PINKY1 to LEFTARM_HAND_THUMB3: 21 to 35; RIGHTARM_HAND_ the same: 36 to 50
```
