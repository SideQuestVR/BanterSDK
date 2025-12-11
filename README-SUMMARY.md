# Banter SDK Quick Reference

JS SDK for 3D VR spaces. All APIs: `BS.*`

## Core Pattern
```js
const scene = BS.BanterScene.GetInstance();
const obj = new BS.GameObject({name: "X", localPosition: new BS.Vector3(0,1,0)});
obj.AddComponent(new BS.ComponentName({prop: value}));
obj.On("event", (e) => {});
```

## Shorthands
`BS.CT` = ComponentType, `BS.PN` = PropertyName, `BS.L` = BanterLayers

## Table of Contents

| Section | Key APIs |
|---------|----------|
| **Scene** | `GetInstance()`, `Find()`, `SetSettings()`, `Raycast()`, `TeleportTo()` |
| **GameObject** | `new GameObject()`, `AddComponent()`, `SetPosition()`, `SetParent()` |
| **Physics** | `BanterRigidbody`, `BoxCollider`, `SphereCollider`, `BanterColliderEvents` |
| **Joints** | `HingeJoint`, `SpringJoint`, `FixedJoint`, `ConfigurableJoint` |
| **Rendering** | `BanterMaterial`, `Light`, `BanterText`, `BanterBillboard` |
| **Geometry** | `BanterBox`, `BanterSphere`, `BanterPlane`, `BanterCylinder` |
| **Media** | `BanterGLTF`, `BanterVideoPlayer`, `BanterBrowser`, `BanterAssetBundle` |
| **Audio** | `BanterAudioSource` |
| **VR** | `BanterGrababble`, `BanterAttachedObject`, `BanterHeldEvents` |
| **UI** | `BanterUIPanel`, `UIButton`, `UILabel`, `UISlider`, `UIToggle` |

## Essential Events

**Scene:**
- `"loaded"` - scene ready
- `"unity-loaded"` - Unity ready
- `"user-joined"` / `"user-left"` - multiplayer

**GameObject:**
- `"click"`, `"grab"`, `"drop"` - interaction
- `"collision-enter"`, `"trigger-enter"` - physics

**Component:**
- `"loaded"` - asset loaded (GLTF, video, etc.)
- `"progress"` - loading progress (0-1)

**Controller (with input blocking):**
- `"button-pressed"` / `"button-released"` - buttons
- `"controller-axis-update"` - thumbstick x,y
- `"trigger-axis-update"` - trigger value

## Common Components

**Visual:**
```
BanterGLTF({url})
BanterSphere({radius}), BanterBox({width, height, depth})
BanterMaterial({color: Vector4, texture: url})
BanterText({text, fontSize, color})
Light({type, color, intensity})
```

**Physics:**
```
BanterRigidbody({mass, useGravity, isKinematic})
BoxCollider({size}), SphereCollider({radius}), MeshCollider({convex})
BanterColliderEvents({})  // enables collision events
```

**Interaction:**
```
BanterGrababble({grabType})  // make grabbable
BanterAttachedObject({attachmentType})  // attach to player
```

**Media:**
```
BanterVideoPlayer({url, loop, volume})
BanterBrowser({url, pageWidth, pageHeight})
BanterAudioSource({volume, loop, spatialBlend})
```

## Math Types
```
Vector2(x, y)
Vector3(x, y, z)
Vector4(x, y, z, w)  // also for colors (r,g,b,a)
Quaternion(x, y, z, w)
```

## Key Enums
```
ForceMode: Force, Impulse, VelocityChange, Acceleration
HandSide: LEFT, RIGHT
ButtonType: TRIGGER, GRIP, PRIMARY, SECONDARY
LightType: Point, Spot, Directional
AttachmentType: Head, LeftHand, RightHand, Chest, Back
```

## Full Reference
See [README-LLM.md](README-LLM.md) for complete API details.
