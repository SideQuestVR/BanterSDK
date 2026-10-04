# Enums & Constants

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
BS.ForceMode.Force          // Continuous force (affected by mass)
BS.ForceMode.Impulse        // Instant force (affected by mass)
BS.ForceMode.VelocityChange // Direct velocity change (ignores mass)
BS.ForceMode.Acceleration   // Continuous acceleration (ignores mass)
```

## HandSide

VR controller hand:

```js
BS.HandSide.LEFT
BS.HandSide.RIGHT
```

## ButtonType

Controller buttons:

```js
BS.ButtonType.TRIGGER
BS.ButtonType.GRIP
BS.ButtonType.PRIMARY    // A/X button
BS.ButtonType.SECONDARY  // B/Y button
```

## GeometryType

Procedural geometry shapes:

```js
BS.GeometryType.BoxGeometry
BS.GeometryType.CircleGeometry
BS.GeometryType.ConeGeometry
BS.GeometryType.CylinderGeometry
BS.GeometryType.PlaneGeometry
BS.GeometryType.RingGeometry
BS.GeometryType.SphereGeometry
BS.GeometryType.TorusGeometry
BS.GeometryType.TorusKnotGeometry
BS.GeometryType.ParametricGeometry
BS.GeometryType.CapsuleGeometry
BS.GeometryType.DodecahedronGeometry
BS.GeometryType.IcosahedronGeometry
BS.GeometryType.OctahedronGeometry
BS.GeometryType.TetrahedronGeometry
BS.GeometryType.LatheGeometry
BS.GeometryType.TubeGeometry
BS.GeometryType.ExtrudeGeometry
BS.GeometryType.ShapeGeometry
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

**No numeric property can be `0`:** a zero falls back to that property's default.

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

What each layer does is in [Layers](layers.md). The old names still work but are deprecated:
`NetworkPlayer` (17), `RPMAvatarHead` (18) and `RPMAvatarBody` (19) are user layers now,
`HandColliders` is 21 (now `Invisible`), and `PhysicsPlayer` is `CharacterColliders`.

## MaterialSide

Which side of geometry to render:

```js
BS.MaterialSide.Front
BS.MaterialSide.Back
BS.MaterialSide.Double
```

## LightType

Light source types:

```js
BS.LightType.Directional  // Sun-like
BS.LightType.Point        // Bulb-like
BS.LightType.Spot         // Flashlight-like
```

## LightShadows

Shadow quality:

```js
BS.LightShadows.None
BS.LightShadows.Hard
BS.LightShadows.Soft
```

## HorizontalAlignment

Text horizontal alignment:

```js
BS.HorizontalAlignment.Left
BS.HorizontalAlignment.Center
BS.HorizontalAlignment.Right
```

## VerticalAlignment

Text vertical alignment:

```js
BS.VerticalAlignment.Top
BS.VerticalAlignment.Middle
BS.VerticalAlignment.Bottom
```

## CollisionDetectionMode

Physics collision quality:

```js
BS.CollisionDetectionMode.Discrete
BS.CollisionDetectionMode.Continuous
BS.CollisionDetectionMode.ContinuousDynamic
BS.CollisionDetectionMode.ContinuousSpeculative
```

## ConfigurableJointMotion

Joint axis constraints:

```js
BS.ConfigurableJointMotion.Locked
BS.ConfigurableJointMotion.Limited
BS.ConfigurableJointMotion.Free
```
