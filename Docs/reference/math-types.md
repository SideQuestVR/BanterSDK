# Math Types

Vector2, Vector3, Vector4 and Quaternion: the types the API uses for positions, rotations, scales and colours.

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

// Basic operations
v.Set(1, 2, 3);
v.Add(new BS.Vector3(1, 1, 1));
v.Subtract(new BS.Vector3(1, 1, 1));
v.Multiply(2);
v.MultiplyVectors(new BS.Vector3(2, 3, 4));
v.Divide(2);

// Vector math
const length = v.Length();
v.Normalize();
const normalized = v.NormalizeNew();  // Returns new vector
const sqrMag = v.SqrMagnitude();

// Cross and dot product
v.Cross(new BS.Vector3(0, 1, 0));
const dot = BS.Vector3.Dot(v, other);

// Angles
const angle = v.Angle(other);                    // Unsigned angle in degrees
const signedAngle = v.SignedAngle(other, axis);  // Signed angle around axis

// Quaternion rotation
v.ApplyQuaternion(quaternion);

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

## Quaternion

Rotation representation (avoids gimbal lock).

```js
const q = new BS.Quaternion(0, 0, 0, 1);  // Identity (no rotation)

// Set from Euler angles (degrees)
q.SetFromEuler({ x: 45, y: 90, z: 0 });

// Get Euler angles back
const euler = q.GetEuler();  // Returns Vector3 in degrees

// Components
q.x = 0;
q.y = 0.707;
q.z = 0;
q.w = 0.707;
```
