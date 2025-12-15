# Banter SDK Reference

JavaScript SDK for creating 3D VR spaces. All APIs under `BS` namespace.

---

## Scene

```
BS.BanterScene.GetInstance() -> BanterScene
```

### Properties
- `objects: {[id]: GameObject}` - All GameObjects
- `components: {[id]: Component}` - All Components
- `users: {[uid]: UserData}` - Connected users
- `localUser: UserData` - Local player
- `unityLoaded: boolean` - Unity ready
- `spaceState: {public: {}, protected: {}}` - Shared state

### Methods
```
Find(name: string) -> GameObject
FindByPath(path: string) -> GameObject
Instantiate(obj: GameObject, pos?: Vector3, rot?: Quaternion, parent?: GameObject) -> GameObject
SetSettings(settings: SceneSettings)
SetLoadPromise(promise: Promise)

// State
SetPublicSpaceProps(props: {[key]: string})
SetProtectedSpaceProps(props: {[key]: string})
SetUserProps(props: {[key]: string}, userId: string)
OneShot(data: any, allInstances?: boolean)

// Physics
Gravity(vector: Vector3)
TimeScale(scale: number)
Raycast(origin: Vector3, direction: Vector3, distance: number, layerMask?: number) // hit objects receive "intersection" event
PlayerSpeed(isFast: boolean)

// Player
SetCanMove(value: boolean)
SetCanRotate(value: boolean)
SetCanCrouch(value: boolean)
SetCanTeleport(value: boolean)
SetCanGrapple(value: boolean)
SetCanJump(value: boolean)
SetCanGrab(value: boolean)
TeleportTo(point: Vector3, rotation: number, stopVelocity: boolean, isSpawn?: boolean)
AddPlayerForce(force: Vector3, mode: ForceMode)
SendHapticImpulse(amplitude: number, duration: number, hand: HandSide)

// Input Blocking (block to handle yourself)
SetBlockLeftThumbstick(value: boolean)
SetBlockRightThumbstick(value: boolean)
SetBlockLeftThumbstickClick(value: boolean)
SetBlockRightThumbstickClick(value: boolean)
SetBlockLeftPrimary(value: boolean)
SetBlockRightPrimary(value: boolean)
SetBlockLeftSecondary(value: boolean)
SetBlockRightSecondary(value: boolean)
SetBlockLeftTrigger(value: boolean)
SetBlockRightTrigger(value: boolean)

// Controller Input Events (use with blocking)
"button-pressed" -> {detail: {button: ButtonType, side: HandSide}}
"button-released" -> {detail: {button: ButtonType, side: HandSide}}
"controller-axis-update" -> {detail: {hand: HandSide, x: number, y: number}}  // thumbstick, -1 to 1
"trigger-axis-update" -> {detail: {hand: HandSide, value: number}}  // 0 to 1

// Browser/Media
OpenPage(url: string)
SendBrowserMessage(value: string)
DeepLink(url: string, msg: string)
SelectFile(type: SelectFileType)
Base64ToCDN(base64: string, fileName: string)
ObjectTextureToBase64(obj: GameObject, materialIndex: number) -> string
YtInfo(videoId: string)

// Voice
StartTTS(voiceDetection: boolean)
StopTTS(id: string)

// AI
AiImage(prompt: string, ratio: AiImageRatio)
AiModel(base64: string, simplify: AiModelSimplify, textureSize: number)

// Utility
WaitForEndOfFrame()
WaitForUnityLoaded()
```

### SceneSettings
```
// General
EnableDevTools: boolean (false)
EnableTeleport: boolean (true)
EnableForceGrab: boolean (false)
EnableSpiderMan: boolean (false)
EnableHandHold: boolean (true)
EnableRadar: boolean (false)
EnableNametags: boolean (true)
EnablePortals: boolean (true)
EnableGuests: boolean (true)
EnableQuaternionPose: boolean (false)
EnableControllerExtras: boolean (false)
EnableFriendPositionJoin: boolean (true)
EnableDefaultTextures: boolean (true)
EnableAvatars: boolean (true)
MaxOccupancy: number (20)
RefreshRate: number (72)
ClippingPlane: Vector2 (0.02, 1500)
SpawnPoint: Vector4 (x,y,z,yRotation)
SettingsLocked: boolean (false)

// Physics
PhysicsMoveSpeed: number (4)
PhysicsMoveAcceleration: number (4.6)
PhysicsAirControlSpeed: number (3.8)
PhysicsAirControlAcceleration: number (6)
PhysicsDrag: number (0)
PhysicsFreeFallAngularDrag: number (6)
PhysicsJumpStrength: number (1)
PhysicsHandPositionStrength: number (1)
PhysicsHandRotationStrength: number (1)
PhysicsHandSpringiness: number (10)
PhysicsGrappleRange: number (512)
PhysicsGrappleReelSpeed: number (1)
PhysicsGrappleSpringiness: number (10)
PhysicsGorillaMode: boolean (false)
PhysicsSettingsLocked: boolean (false)
```

### Scene Events
```
"loaded" -> {}  // scene settled, objects enumerated
"unity-loaded" -> {}  // Unity fully loaded, loading screen gone
"user-joined" -> {detail: UserData}
"user-left" -> {detail: UserData}
"key-press" -> {detail: {key: KeyCode}}
"space-state-changed" -> {detail: {changes: [{property, oldValue, newValue}]}}
"one-shot" -> {detail: {fromId, fromAdmin, data}}
"menu-browser-message" -> {detail: any}
"transcription" -> {detail: {id, message}}
"voice-started" -> {}
"aframe-trigger" -> {detail: {data}}
```
Note: Controller input events (button-pressed, button-released, controller-axis-update, trigger-axis-update) documented in Input Blocking section above.

### Component & GameObject Events
```
// On Component or GameObject:
component.On("loaded", callback)  // asset finished loading
component.On("progress", (e) => e.detail.progress)  // 0-1 loading progress
component.On("unity-linked", (e) => e.detail.unityId)  // linked to Unity
gameObject.On("object-update", (e) => e.detail)  // component IDs updated

// Property:
component.isLoaded: boolean  // true when asset loaded
```

---

## GameObject

```
new BS.GameObject(config: GameObjectConfig) -> GameObject
```

### GameObjectConfig
```
{
  name: string              // Required
  id?: string
  layer?: number
  active?: boolean          // Default: true
  tag?: string
  localPosition?: Vector3
  localEulerAngles?: Vector3
  localRotation?: Quaternion
  localScale?: Vector3
  parent?: GameObject
}
```

### Properties (get/set, auto-sync)
```
name: string
layer: number
active: boolean
tag: string
parent: string | number
id: string | number         // Read-only
path: string                // Read-only
transform: Transform        // Read-only
components: {[id]: Component}
meta: any
```

### Methods
```
// Transform
SetPosition(pos: Vector3 | x, y?, z?)
SetLocalPosition(pos: Vector3 | x, y?, z?)
SetEulerAngles(angles: Vector3 | x, y?, z?)
SetLocalEulerAngles(angles: Vector3 | x, y?, z?)
SetRotation(rot: Quaternion | x, y?, z?, w?)
SetLocalRotation(rot: Quaternion | x, y?, z?, w?)
SetLocalScale(scale: Vector3 | x, y?, z?)
SetTransform(transform: Transform)
WatchTransform(types: PN[], callback: (transform) => void)  // PN = PropertyName

// Hierarchy
SetParent(parent: GameObject, worldPositionStays?: boolean)
Find(path: string) -> GameObject
Traverse(callback: (obj: GameObject) => void, descendants?: boolean)

// Components
AddComponent(component: Component) -> Component
GetComponent(type: CT) -> Component | Transform  // CT = ComponentType

// Other
SetLayer(layer: number)
SetActive(active: boolean)
SetTag(tag: string)
SetName(name: string)
SetNetworkId(id: string)
GetBounds(isCollider: boolean) -> {center: Vector3, size: Vector3}
Destroy()
```

### GameObject Events
```
"click" -> {detail: {point: Vector3, normal: Vector3}}
"grab" -> {detail: {point: Vector3, normal: Vector3, side: HandSide}}
"drop" -> {detail: {side: HandSide}}
"collision-enter" -> {detail: {name, tag, collider, point, normal, user?}}
"collision-exit" -> {detail: {name, tag, collider, object}}
"trigger-enter" -> {detail: {name, tag, collider, object}}
"trigger-exit" -> {detail: {name, tag, collider, object}}
"intersection" -> {detail: {point: Vector3, normal: Vector3}} // from Raycast
"browser-message" -> {detail: any}
```

---

## Components

All: `obj.AddComponent(new BS.ComponentName({...}))`

### Physics

**BanterRigidbody**
```
Config: {velocity?: Vector3, angularVelocity?: Vector3, mass?: number(1), drag?: number(0), angularDrag?: number(0.05), isKinematic?: boolean(false), useGravity?: boolean(true), centerOfMass?: Vector3, collisionDetectionMode?: CollisionDetectionMode, freezePositionX?: boolean, freezePositionY?: boolean, freezePositionZ?: boolean, freezeRotationX?: boolean, freezeRotationY?: boolean, freezeRotationZ?: boolean}

Methods:
AddForce(force: Vector3, mode: ForceMode)
AddForceValues(x, y, z, mode: ForceMode)
AddRelativeForce(force: Vector3, mode: ForceMode)
AddForceAtPosition(force: Vector3, position: Vector3, mode: ForceMode)
AddTorque(torque: Vector3, mode: ForceMode)
AddTorqueValues(x, y, z, mode: ForceMode)
AddRelativeTorque(torque: Vector3, mode: ForceMode)
AddExplosionForce(force, position: Vector3, radius, upMod, mode: ForceMode)
MovePosition(position: Vector3)
MoveRotation(rotation: Quaternion)
Sleep()
WakeUp()
ResetCenterOfMass()
ResetInertiaTensor()

Properties (get/set): velocity, angularVelocity, mass, drag, angularDrag, isKinematic, useGravity
```

**BoxCollider**
```
Config: {isTrigger?: boolean(false), center?: Vector3(0,0,0), size?: Vector3(1,1,1)}
```

**SphereCollider**
```
Config: {isTrigger?: boolean(false), radius?: number(0.5)}
```

**CapsuleCollider**
```
Config: {isTrigger?: boolean(false), radius?: number(0.5), height?: number(2)}
```

**MeshCollider**
```
Config: {isTrigger?: boolean(false), convex?: boolean(true)}
```

**BanterColliderEvents**
```
Config: {} (no params, enables collision/trigger events)
```

**BanterPhysicMaterial**
```
Config: {dynamicFriction?: number(0.6), staticFriction?: number(0.6)}
```

### Joints

**CharacterJoint**
```
Config: {anchor?: Vector3, axis?: Vector3, swingAxis?: Vector3, connectedAnchor?: Vector3, autoConfigureConnectedAnchor?: boolean(true), enableProjection?: boolean(false), projectionDistance?: number, projectionAngle?: number, breakForce?: number(Infinity), breakTorque?: number(Infinity), enableCollision?: boolean(false), connectedBody?: string}
```

**FixedJoint**
```
Config: {anchor?: Vector3, connectedAnchor?: Vector3, autoConfigureConnectedAnchor?: boolean(true), breakForce?: number(Infinity), breakTorque?: number(Infinity), enableCollision?: boolean(false), connectedBody?: string}
```

**HingeJoint**
```
Config: {anchor?: Vector3, axis?: Vector3(0,1,0), connectedAnchor?: Vector3, autoConfigureConnectedAnchor?: boolean(true), useLimits?: boolean(false), useMotor?: boolean(false), useSpring?: boolean(false), breakForce?: number(Infinity), breakTorque?: number(Infinity), enableCollision?: boolean(false), connectedBody?: string}

IMPORTANT: connectedBody is the rigidbody.id on the other GameObject. Without it, hinge connects to world space. Always specify connectedBody!
```

**SpringJoint**
```
Config: {anchor?: Vector3, connectedAnchor?: Vector3, autoConfigureConnectedAnchor?: boolean(true), spring?: number(10), damper?: number(0), minDistance?: number(0), maxDistance?: number(1), tolerance?: number(0.025), breakForce?: number(Infinity), breakTorque?: number(Infinity), enableCollision?: boolean(false), connectedBody?: string}
```

**ConfigurableJoint**
```
Config: {targetPosition?: Vector3, targetRotation?: Quaternion, targetVelocity?: Vector3, targetAngularVelocity?: Vector3, xMotion?: ConfigurableJointMotion, yMotion?: ConfigurableJointMotion, zMotion?: ConfigurableJointMotion, angularXMotion?: ConfigurableJointMotion, angularYMotion?: ConfigurableJointMotion, angularZMotion?: ConfigurableJointMotion, anchor?: Vector3, axis?: Vector3, secondaryAxis?: Vector3, connectedAnchor?: Vector3, autoConfigureConnectedAnchor?: boolean(true), configuredInWorldSpace?: boolean(false), swapBodies?: boolean(false), breakForce?: number(Infinity), breakTorque?: number(Infinity), enableCollision?: boolean(false), connectedBody?: string}
```

### Rendering

**Light**
```
Config: {type?: LightType(Point), color?: Vector4(1,1,1,1), intensity?: number(1), range?: number(10), spotAngle?: number(30), innerSpotAngle?: number(21.8), shadows?: LightShadows(None)}
```

**BanterMaterial**
```
Config: {shaderName?: string("Unlit/Diffuse"), texture?: string(url), color?: Vector4(1,1,1,1), side?: MaterialSide(Front), generateMipMaps?: boolean(false)}
```

**BanterText**
```
Config: {text?: string, color?: Vector4(1,1,1,1), fontSize?: number(2), horizontalAlignment?: HorizontalAlignment(Center), verticalAlignment?: VerticalAlignment(Middle), richText?: boolean(true), enableWordWrapping?: boolean(true), rectTransformSizeDelta?: Vector2}
```

**BanterBillboard**
```
Config: {smoothing?: number(0), enableXAxis?: boolean(true), enableYAxis?: boolean(true), enableZAxis?: boolean(false)}
```

**BanterMirror**
```
Config: {}
```

**BanterInvertedMesh**
```
Config: {}
```

### Geometry Primitives

**BanterBox**
```
Config: {width?: number(1), height?: number(1), depth?: number(1), widthSegments?: number(1), heightSegments?: number(1), depthSegments?: number(1)}
```

**BanterSphere**
```
Config: {radius?: number(1), widthSegments?: number(16), heightSegments?: number(16), phiStart?: number(0), phiLength?: number(2π), thetaStart?: number(0), thetaLength?: number(π)}
```

**BanterPlane**
```
Config: {width?: number(1), height?: number(1), widthSegments?: number(1), heightSegments?: number(1)}
// facing -Z
```

**BanterCylinder**
```
Config: {radiusTop?: number(1), radiusBottom?: number(1), height?: number(1), radialSegments?: number(8), heightSegments?: number(1), openEnded?: boolean(false), thetaStart?: number(0), thetaLength?: number(2π)}
// curved side faces -Z
```

**BanterCone**
```
Config: {radius?: number(1), height?: number(1), radialSegments?: number(8), heightSegments?: number(1), openEnded?: boolean(false), thetaStart?: number(0), thetaLength?: number(2π)}
```

**BanterCircle**
```
Config: {radius?: number(1), segments?: number(32), thetaStart?: number(0), thetaLength?: number(2π)}
```

**BanterTorus**
```
Config: {radius?: number(1), tube?: number(0.4), radialSegments?: number(8), tubularSegments?: number(16), arc?: number(2π)}
```

**BanterTorusKnot**
```
Config: {radius?: number(1), tube?: number(0.4), tubularSegments?: number(64), radialSegments?: number(8), p?: number(2), q?: number(3)}
```

**Parametric Shapes**
```
BanterKlein, BanterMobius, BanterMobius3d, BanterCatenoid, BanterHelicoid, BanterFermet, BanterNatica, BanterScherk, BanterSnail, BanterSpiral, BanterSpring
Config: {stacks?: number, slices?: number, ...shape-specific}
```

### Audio

**BanterAudioSource**
```
Config: {volume?: number(1), pitch?: number(1), mute?: boolean(false), loop?: boolean(false), playOnAwake?: boolean(true), bypassEffects?: boolean(false), bypassListenerEffects?: boolean(false), bypassReverbZones?: boolean(false), spatialBlend?: number(1)}

Methods:
Play()
PlayOneShot(index: number)
PlayOneShotFromUrl(url: string)
```

### Media

**BanterGLTF**
```
Config: {url: string, generateMipMaps?: boolean(false), addColliders?: boolean(false), nonConvexColliders?: boolean(false), slippery?: boolean(false), climbable?: boolean(false), legacyRotate?: boolean(false), childrenLayer?: number}
```

**BanterAssetBundle**
```
Config: {windowsUrl?: string, androidUrl?: string, osxUrl?: string, linuxUrl?: string, iosUrl?: string, vosUrl?: string, isScene?: boolean(false), legacyShaderFix?: boolean(false)}
```

**BanterVideoPlayer**
```
Config: {url?: string, volume?: number(1), loop?: boolean(false), playOnAwake?: boolean(true), skipOnDrop?: boolean(true), waitForFirstFrame?: boolean(true)}

Properties: time, isPlaying, isLooping
Methods: Play(), Pause(), Stop()
```

**BanterBrowser**
```
Config: {url?: string, mipMaps?: number(4), pixelsPerUnit?: number(1200), pageWidth?: number(1280), pageHeight?: number(720), actions?: string}

Methods:
ToggleInteraction(enabled: boolean)
RunActions(actions: string)
```

**BanterStreetView**
```
Config: {panoId: string}
```

**BanterPortal**
```
Config: {url: string, instance?: string}
```

### VR Interaction

**BanterGrababble**
```
Config: {grabType?: BanterGrabType, grabRadius?: number(0.01), gunTriggerSensitivity?: number(0.5), gunTriggerFireRate?: number(0.1), gunTriggerAutoFire?: boolean(false), blockLeftPrimary?: boolean, blockLeftSecondary?: boolean, blockRightPrimary?: boolean, blockRightSecondary?: boolean, blockLeftThumbstick?: boolean, blockLeftThumbstickClick?: boolean, blockRightThumbstick?: boolean, blockRightThumbstickClick?: boolean, blockLeftTrigger?: boolean, blockRightTrigger?: boolean}
```

**BanterGrabHandle**
```
Config: {grabType?: BanterGrabType, grabRadius?: number(0.01)}
```

**BanterHeldEvents**
```
Config: {sensitivity?: number(0.5), fireRate?: number(0.1), auto?: boolean(false), blockLeftPrimary?: boolean, blockLeftSecondary?: boolean, blockRightPrimary?: boolean, blockRightSecondary?: boolean, blockLeftThumbstick?: boolean, blockLeftThumbstickClick?: boolean, blockRightThumbstick?: boolean, blockRightThumbstickClick?: boolean, blockLeftTrigger?: boolean, blockRightTrigger?: boolean}
```

**BanterAttachedObject**
```
Config: {attachmentType: AttachmentType}
```

### Special

**BanterKitItem**
```
Config: {path: string}
```

**BanterSyncedObject**
```
Config: {}
```

**BanterWorldObject**
```
Config: {}
```

**BanterAvatarPedestal**
```
Config: {}
```

---

## UI System

### BanterUIPanel
```
Config: {resolution?: Vector2(800,600), screenSpace?: boolean(false), enableHaptics?: boolean(true), clickHaptic?: Vector2, enterHaptic?: Vector2, exitHaptic?: Vector2, enableSounds?: boolean(false), clickSoundUrl?: string, enterSoundUrl?: string, exitSoundUrl?: string}
```

### UIElement (Base)
```
Properties: id, type, panel, parent, children, enabled, visible

Hierarchy:
AppendChild(child: UIElement)
RemoveChild(child: UIElement)
InsertBefore(child: UIElement, reference?: UIElement)

Properties:
SetProperty(name: PN, value: any)  // PN = PropertyName
GetProperty(name: PN) -> any
SetProperties(names: PN[])

Styles:
SetStyle(name: string, value: any)
GetStyle(name: string) -> any
SetStyles(styles: {[name]: value})
style.propertyName = value

Events:
OnClick(handler), OnMouseDown(handler), OnMouseUp(handler), OnMouseEnter(handler), OnMouseLeave(handler), OnMouseMove(handler), OnKeyDown(handler), OnKeyUp(handler), OnFocus(handler), OnBlur(handler), OnChange(handler), OnWheel(handler)
AddEventListener(type, handler), RemoveEventListener(type, handler)

Query:
QuerySelector(selector: string) -> UIElement
QuerySelectorAll(selector: string) -> UIElement[]

Lifecycle:
Destroy()
```

### UI Components
```
UIButton, UILabel, UISlider, UIToggle, UIScrollView, UIVisualElement
```

### Style Properties
```
// Layout
alignContent, alignItems, justifyContent, flexBasis, flexDirection, flexGrow, flexShrink, flexWrap

// Size
width, height, minWidth, minHeight, maxWidth, maxHeight

// Position
position, left, top, right, bottom

// Spacing
margin, marginLeft, marginRight, marginTop, marginBottom
padding, paddingLeft, paddingRight, paddingTop, paddingBottom

// Borders
borderWidth, borderLeftWidth, borderRightWidth, borderTopWidth, borderBottomWidth
borderRadius, borderTopLeftRadius, borderTopRightRadius, borderBottomLeftRadius, borderBottomRightRadius
borderColor, borderLeftColor, borderRightColor, borderTopColor, borderBottomColor

// Background
backgroundColor, backgroundImage, backgroundSize, backgroundRepeat, backgroundPosition

// Text
color, fontSize, fontStyle, fontWeight, lineHeight, textAlign, textOverflow, whiteSpace, wordWrap, letterSpacing

// Display
display, visibility, overflow, opacity

// Transform
rotate, scale, translate, transformOrigin

// Other
cursor, transitionProperty, transitionDuration, transitionTimingFunction, transitionDelay
```

---

## Math Types

### Vector2
```
new BS.Vector2(x?: number, y?: number)

Properties: x, y
Methods: Set(x,y), Add(v), Subtract(v), Multiply(n), MultiplyVectors(v)
```

### Vector3
```
new BS.Vector3(x?: number, y?: number, z?: number)

Properties: x, y, z
Methods: Set(x,y,z), Add(v), AddNew(v), Subtract(v), SubtractNew(v), Multiply(n), MultiplyNew(n), MultiplyVectors(v), Divide(n), DivideNew(n), Length(), Normalize(), NormalizeNew(), Cross(v), CrossVectors(a,b), Dot(a,b), ApplyQuaternion(q), Angle(v), SignedAngle(v,axis), SqrMagnitude()
```

### Vector4
```
new BS.Vector4(x?: number, y?: number, z?: number, w?: number(1))

Properties: x, y, z, w
Methods: Set(x,y,z,w), Add(v), Multiply(n), MultiplyVectors(v)
```

### Quaternion
```
new BS.Quaternion(x?: number, y?: number, z?: number, w?: number(1))

Properties: x, y, z, w
Methods: SetFromEuler({x,y,z,order?}), GetEuler() -> Vector3
```

---

## Enums

**Shorthands:** `BS.CT` = ComponentType, `BS.PN` = PropertyName, `BS.L` = BanterLayers

### ComponentType (BS.CT)
```
Transform, BanterRigidbody, BoxCollider, SphereCollider, CapsuleCollider, MeshCollider, BanterAudioSource, BanterGLTF, BanterMaterial, BanterText, Light, BanterBrowser, BanterVideoPlayer, BanterAssetBundle, ...
```

### PropertyName (BS.PN)
```
position, localPosition, rotation, localRotation, localScale, eulerAngles, localEulerAngles, velocity, angularVelocity, text, fontSize, ...
```

### ForceMode
```
Force, Impulse, VelocityChange, Acceleration
```

### HandSide
```
LEFT, RIGHT
```

### ButtonType
```
TRIGGER, GRIP, PRIMARY, SECONDARY
```

### GeometryType
```
BoxGeometry, CircleGeometry, ConeGeometry, CylinderGeometry, PlaneGeometry, RingGeometry, SphereGeometry, TorusGeometry, TorusKnotGeometry, ParametricGeometry
```

### BanterLayers (BS.L)
```
UserLayer1(3), UserLayer2(6), UserLayer3(7), UserLayer4(8), UserLayer5(9), UserLayer6(10), UserLayer7(11), UserLayer8(12), UserLayer9(13), UserLayer10(14), UserLayer11(15), UserLayer12(16), NetworkPlayer(17), RPMAvatarHead(18), RPMAvatarBody(19), Grabbable(20), HandColliders(21), WalkingLegs(22), PhysicsPlayer(23)
```

### MaterialSide
```
Front, Back, Double
```

### LightType
```
Directional, Point, Spot
```

### LightShadows
```
None, Hard, Soft
```

### HorizontalAlignment
```
Left, Center, Right
```

### VerticalAlignment
```
Top, Middle, Bottom
```

### CollisionDetectionMode
```
Discrete, Continuous, ContinuousDynamic, ContinuousSpeculative
```

### ConfigurableJointMotion
```
Locked, Limited, Free
```

### AttachmentType
```
Head, LeftHand, RightHand, LeftFoot, RightFoot, Chest, Back
```

### AiImageRatio
```
_1_1, _3_2, _4_3, _16_9, _21_9, _2_3, _3_4, _9_16, _9_21
```

### AiModelSimplify
```
low, med, high
```

---

## UserData

```
Properties: id, uid, name, color, isLocal, props: {[key]: string}

Methods:
Attach(object: GameObject, type: AttachmentType)
```

---

## Event Listening

```
obj.On(eventName: string, callback: (e) => void)
obj.Off(eventName: string, callback: (e) => void)
obj.AddEventListener(eventName, callback)
obj.RemoveEventListener(eventName, callback)
```

---

## Patterns

### Create object with component
```js
const obj = new BS.GameObject({name: "X", localPosition: new BS.Vector3(0,1,0)});
obj.AddComponent(new BS.BanterSphere({radius: 1}));
obj.AddComponent(new BS.BanterMaterial({color: new BS.Vector4(1,0,0,1)}));
```

### Physics object
```js
const obj = new BS.GameObject({name: "Ball"});
obj.AddComponent(new BS.BanterSphere({radius: 0.5}));
obj.AddComponent(new BS.SphereCollider({radius: 0.5}));
obj.AddComponent(new BS.BanterRigidbody({mass: 1}));
obj.AddComponent(new BS.BanterColliderEvents());
obj.On("collision-enter", (e) => console.log(e.detail.name));
```

### Grabbable object
```js
const obj = new BS.GameObject({name: "Pickup"});
obj.AddComponent(new BS.BanterGrababble({grabType: BS.BanterGrabType.Default}));
obj.On("grab", (e) => console.log("Grabbed by", e.detail.side));
obj.On("drop", (e) => console.log("Dropped"));
```

### Scene initialization
```js
const scene = BS.BanterScene.GetInstance();
scene.On("unity-loaded", () => {
    // Scene ready
});
```

### Get component & watch transform
```js
const rb = obj.GetComponent(BS.CT.BanterRigidbody);
obj.WatchTransform([BS.PN.position, BS.PN.rotation], (t) => console.log(t.position));
```

---

