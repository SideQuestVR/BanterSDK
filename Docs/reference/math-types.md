# Math Types

## Vector2

2D vector for UV coordinates, UI sizes, etc.

```js
const v = new BS.Vector2(1, 2);

v.x = 3;
v.y = 4;

v.Set(5, 6);
v.Add(new BS.Vector2(1, 1));
v.Subtract(new BS.Vector2(1, 1));
v.Multiply(2);
v.MultiplyVectors(new BS.Vector2(2, 3));
```

## Vector3

3D vector for positions, directions, scales.

```js
const v = new BS.Vector3(1, 2, 3);

v.x = 4;
v.y = 5;
v.z = 6;

// Basic operations (these change v and return it, so they chain)
v.Set(1, 2, 3);
v.Add(new BS.Vector3(1, 1, 1));
v.Subtract(new BS.Vector3(1, 1, 1));
v.Multiply(2);
v.MultiplyVectors(new BS.Vector3(2, 3, 4));
v.Divide(2);

// Vector math
const length = v.Length();
v.Normalize();                        // Changes v
const normalized = v.NormalizeNew();  // Returns new vector
const sqrMag = v.SqrMagnitude();

// Cross and dot product
v.Cross(new BS.Vector3(0, 1, 0));     // Changes v: v becomes v × (0, 1, 0)
const cross = new BS.Vector3().CrossVectors(a, b); // a × b in a new vector
const dot = BS.Vector3.Dot(v, other);

// Angles
const angle = v.Angle(other);                    // Unsigned angle in degrees
const signedAngle = v.SignedAngle(other, axis);  // Signed angle around axis

// Quaternion rotation
v.ApplyQuaternion(quaternion);        // Changes v: rotates it

// Non-mutating versions
const added = v.AddNew(other);
const subtracted = v.SubtractNew(other);
const multiplied = v.MultiplyNew(2);
const divided = v.DivideNew(2);
```

## Vector4

4D vector for colors (RGBA), quaternions, etc.

```js
const v = new BS.Vector4(1, 0, 0, 1);  // Red, full opacity

v.x = 0;  // R
v.y = 1;  // G
v.z = 0;  // B
v.w = 1;  // A

v.Set(0.5, 0.5, 0.5, 1);
v.Add(new BS.Vector4(0.1, 0.1, 0.1, 0));
v.Multiply(0.5);
```

`w` is `1` when you leave it out: `new BS.Vector4()` is `(0, 0, 0, 1)`.

## Quaternion

Rotation representation (avoids gimbal lock).

```js
const q = new BS.Quaternion(0, 0, 0, 1);  // Identity (no rotation)

// Set from Euler angles (degrees), the same way as Unity's Quaternion.Euler
q.SetFromEuler({ x: 45, y: 90, z: 0 });
q.SetFromEuler(new BS.Vector3(45, 90, 0)); // a Vector3 works too

// Get Euler angles back, as Unity's eulerAngles gives them
const euler = q.GetEuler();  // Vector3 in degrees, each 0–360

// Components
q.x = 0;
q.y = 0.707;
q.z = 0;
q.w = 0.707;
```

`SetFromEuler` applies the angles in Unity's order, z, then x, then y, so `SetFromEuler({ x, y, z })` gives
the same rotation as `Quaternion.Euler(x, y, z)` in Unity and the `eulerAngles` the Inspector shows.
`GetEuler` is its inverse and, like Unity, returns each angle between 0 and 360 (so -20 comes back as
340). For another order, pass it as `order`: `q.SetFromEuler({ x, y, z, order: "XYZ" })`
(`"XYZ"`, `"YXZ"`, `"ZXY"`, `"ZYX"`, `"YZX"` or `"XZY"`; the default is `"YXZ"`).

A Quaternion is a `Vector4` underneath, so it has `Add`, `Multiply` and the rest, but they work on the
four numbers: there's no quaternion multiplication.
