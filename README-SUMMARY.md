# Banter SDK Quick Reference

JS SDK for 3D VR spaces. All APIs: `BS.*`

Use the `get-child-objects` tool to browse the scene, starting with 0 for the parent id you can explore your way through the scene hierarchy. 

Always start with seeing the exisitng space first to see what is there, add a floor with a collider if need be and then folllow the users instructions. 

## Core Pattern
```js
const scene = BS.BanterScene.GetInstance();
const obj = new BS.GameObject({name: "X", localPosition: new BS.Vector3(0,1,0)});
obj.AddComponent(new BS.ComponentName({prop: value}));
obj.On("event", (e) => {});

const obj2 = new BS.GameObject({name: "Y", localPosition: new BS.Vector3(0,0,0)});
obj2.SetParent(obj, false); // Worldpositionstays - true by default
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
BanterSphere({radius}), BanterBox({width, height, depth}), BanterCylinder({radiusTop, radiusBottom})  // curved side faces -Z
BanterPlane({width, height}) // facing -Z
BanterTorus() // facing -Z
BanterMaterial({color: Vector4, texture: url})
BanterText({text, fontSize, color})
Light({type, color, intensity})
```

**Physics:**
```
BanterRigidbody({mass, useGravity, isKinematic})
BoxCollider({size}), SphereCollider({radius}), MeshCollider({convex})
BanterColliderEvents({})  // enables collision events
HingeJoint({connectedBody}) // IMPORTANT! ALWAYS REMEMBER THIS PART! the connectedBody is the rigidbody on the other game obejct, without this then the hinge connects to world space. You must link joints and their connected bodies together. You can specify the connectedBody with rigidBody.id and banter will connect the body if it exists. The moving part of the hinge should not be kinematic, or else it cant move. Make sure to set the limits to something sensible. 

DO NOT MAKE A HINGE WITHOUT A CONNECTED BODY!!!
```

**Interaction:**
```
BanterGrababble({grabType})  // make grabbable
BanterAttachedObject({attachmentType})  // attach to player
```

**Media:**
```
BanterGLTF({url})
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

## MCP Tools

| Tool | Purpose |
|------|---------|
| `instructions` | Read first - essential context |
| `search-docs` | Search docs (fuse.js extended search), empty query returns all lines |
| `get-browsers` | List browser instances (returns id, injection status) |
| `execute-javascript` | Run JS in browser - wrap in anonymous function, avoid let/const/var |
| `tail-logs` | Search logs by level (info,warning,error,debug) and query |
| `reload-browser` | Reload browser by id |
| `toggle-devtools` | Open/close Chrome devtools |
| `mouse-input` | Send mouse events (mouseDown/Up/Move/Wheel, x, y, button) |
| `key-input` | Send keyboard events (key, modifiers: control/alt/meta/shift) |
| `get-child-objects` | Get scene objects by parentId ('0' for root) |
| `see` | Grab a frame from main camera with custom position/rotation |
| `see-all-round` | Grab six frames from different angles for 360° view |

## Full Reference
See [README-LLM.md](README-LLM.md) for complete API details.
