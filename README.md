# Banter Creator Documentation
<sup>Powered by `BullShcript`<sup>TM</sup><br><sup>([bullshc.rip/t](https://bullshc.rip/t))</sup></sup>

<img src="https://cdn.sidequestvr.com/file/606701/unity_uv0yc00xq7-ezgifcom-video-to-gif-converter.gif" style="float: right; max-width: 300px; margin: 20px; margin-top: 0;"/>

Here you will find all the information you need to start building spaces and avatars in Banter. You should also [join our discord server](https://discord.gg/bantaverse) to interact with others interested in building spaces in Banter.

## Avatar Creation

We have a new avatar system out in beta, including an extension for blender to make building avatars as easy as possible. 

<details><summary>Click Here For More Info</summary>

  Get your avatar ready for the Bantaverse. This plugin will help you set up your avatars with the correct LOD levels, prepare materials, and upload directly to your Banter account.

  ### Blender Plugin Features
  1. Generate a Banter-compliant armature if your avatar needs one.
  1. Assign your local avatar meshes, and designate your head mesh.
  1. Auto-generate performance-friendly LODs, complete with atlas material.
  1. Validate your avatar against Banter requirements.
  1. Export and/or upload your avatar to Banter!
  
  Our blender plugin is currently pending review over on the [blender extensions marketplace](https://extensions.blender.org/approval-queue/sidequest-banter-avatars). You can grab it there or from our [github](https://github.com/SideQuestVR/banter-blender-plugin/releases).
  
  ### How To Use
  
  1. In the sidebar tray, locate the 'BANTER' tab.
  1. Assign the local armature to the slot, or generate a new one.
  1. Assign your local avatar's meshes to the local avatar section by selecting the objects and clicking 'Use Selected Objects'
  1. Assign the head mesh of your local avatar. We will turn this off in Banter so you can see properly!
  1. Assign or generate LODs of your avatar. Shapekeys are only allowed on LOD0.
  1. Press 'Run Validation' and fix any errors. If possible, a quick fix button will generate a new LOD per the requirements.
  1. Link with your SideQuest account to export and upload your new avatar.
</details>

## Space Creation

It's recommended to use `Unity` for making spaces on Banter so we will cover how to get started with `Unity`. The great news is that we have built out a great set of tools in the form of the `Banter Unity Plugin` to make building for Banter with Unity even more powerful. With this plugin you can build your space inside the `Unity Editor`, including testing and running your scripts and running your space almost like it does in the build at runtime. You can even use `VR` in the `Unity Editor` for testing things like two handed interactions in first person `VR`.

There are two primary ways you can add scripting in Banter, using [Unity Visual Scripting](https://unity.com/features/unity-visual-scripting) or using [JavaScript](https://en.wikipedia.org/wiki/JavaScript) or a combination of the two! With 
options like these you have an incredible amount of power to build out awesome spaces and games!

Making spaces in Banter is easy and fun. You can get going quickly with a simple example, or dive right into a world of possibilities with professional workflows using tools such as `Unity` and `Blender`.

Here are the steps required to get started with building spaces in Banter with `Unity`. 

 * Download and install `Unity v2022.3.39f1` and select both android and windows build support to be installed.
 * You also need to have [git-scm](https://git-scm.com/downloads/win) installed right now, until we release the plugin on OpenUPM. 
 * Download the Banter plugin through the Package Manager in Unity
   * Open the Package Manager through `Window` > `Package Manager`
   * Add a package with the + icon on the top left.
   * Select `Add package from git URL`.
   * Enter the following URL `https://github.com/SideQuestVR/BanterSDK.git` and click Add.
 * Make a space over `https://sidequestvr.com/account/create-space`
 * Note down the space slug from the banter tab in the above space in the `Mange Space` section. 
 * Sign in on the Banter Unity Plugin, and enter the space-slug (without bant.ing) into the `Upload` tab.
 * Finally tick the Auto Upload checkbox on the `Build` tab. When your space is ready, select `Build & Upload`. 
   

Now you can jump into Banter and test your space! If you want your space to be featured, you can submit it on our [discord](https://discord.gg/bantaverse) or via our [service portal](https://sdq.st/banter-help).


### Unity Scenes in Banter

In the past loading a scene into banter was tricky, but we have made things easier with the new SDK. Now you can avoid a lot of the tricky parts and just build and upload your scene to Banter.
In case you want to expand the basic example here is some more info around how you can do that in the `index.html`, or you can skip to the next section since our Unity plugin handles this for you!.

<details><summary>Click Here For More Info</summary>

 Unity uses asset bundles to store your scene which we then load into Banter via the `index.html`. Now you only need a small amount of HTML in your `index.html` to load the bundles, and we add it in automatically for you when you install the Banter Unity Plugin, so you can skip to the next section if you want!

 ```html
<html android-bundle windows-bundle><head>
```
If the space is not hosted on SideQuest i.e. the address does not end with `*.bant.ing` then you can expand this to the following (replace `example.com`): 
```html
<html android-bundle="https://example.com/android.banter" windows-bundle="https://example.com/windows.banter"><head>
``` 
You can also do this with JavaScript using code like this (`*.bant.ing`): 
```js
BS.LoadAssetBundles();
```
or by including the asset bundle addresses like this (replace `example.com`):
```js
BS.LoadAssetBundles("https://example.com/android.banter", "https://example.com/windows.banter");
```
This will be the default code in the `index.html` file in new projects, so if you're unsure about any of the above it can be safely ignored and everything should be fine.

</details>

You can make any object in your scene visible to `JavaScript` by adding a `BanterObjectId` component to it inside of Unity. This works for normal asset bundles as well as for kit (prefab) based bundles too. Objects that are created from `JavaScript` will have a `BanterObjectId` added automatically and will be exposed automatically. In order to access these object, listen for the `unity-loaded` event in `JavaScript` before you try to use methods like `scene.Find`.

**PLEASE NOTE** It's generally good to wrap all your banter code in the following, its kinda like our `DOMContentLoaded` or `window.onload` event for Banter.

```js
BS.BanterScene.GetInstance().On("unity-loaded", async () => {
    // do things here.
});

```


### Running Your Scene in the Unity Editor

Now you can run your unity scene inside the unity editor before you upload it to Banter. To do this you must add a `BanterStarterUpper` component into your scene. Create an empty game object in unity and add the `BanterStarterUpper` component. Now when you hit play Banter will run a headless browser inside the unity editor so it can run and test your `BS` code without having to upload your space! That allows for a huge amount of debugging capability inside the unity editor. 

### Visual Scripting

Creating spaces in Banter is fun, but you can add even more excitement with scripting. While Banter primarily uses JavaScript, you can also use Unity's Visual Scripting to enhance your spaces with complex logic. This allows you to create a variety of cool games and functionalities in Banter.
Some visual scripting nodes are not permitted. We've added a check in the Banter Unity plugin to alert you in the console if any disallowed nodes are used. This check runs every time you hit play in the editor. A more user-friendly UI for this feature is coming in a future update.
If an unsupported node is detected, an error will print to the console. You can use this tool: [UVS Finder](https://github.com/myanko/uvs-finder.git) to identify and remove unsupported nodes. Install it by clicking "Add package from GIT URL" in the package manager. Then, search for and delete the unsupported node from your script graph using the NodeFinder window.
For more guides and tutorials, please see some of these third party resources. 

[Unity Visual Scripting Guide by NotSlot](https://www.youtube.com/watch?v=JYkFm1Sc3v8&list=PLqqkaa8OrxkHxJHcGATpq-MCJnjX5hUBj)


### Banter SDK Contributions

Since our SDK is going to be open source, there are two ways that new functionality can be added. 
The first way is that we add functionality when we need it in Banter, which makes sense. The second is if
a creator wants to add functionality to Banter. In that case we have a process for accepting contributions. 
To find out more, check out our [Contribution Guidelines](/CONTRIBUTING.md) page.
<!--
## Avatar Creation

Creating avatars in Banter can be a lot of fun. We recommend using `Blender` for creating avatars, we've even made a plugin to make building avatars in `Blender` a breeze. You can also create avatars using `Ready Player Me` though those avatars won't have a lot of the extra features you get when making Banter avatars in `Blender`.

Right now we are working on a brand new avatar system. It should allow us to bring more avatar features and custom options whilst also squeezing every last bit of performance out of the avatar system at scale.
-->
### More Resources

You can also find more info and tutorials here: 

<!-- * [Banter Space Tutorials](https://www.youtube.com/watch?v=dQw4w9WgXcQ)
 * [Banter Avatar Tutorials](https://www.youtube.com/watch?v=dQw4w9WgXcQ) -->
 * [Unity Getting Started Tutorials](https://www.youtube.com/watch?v=j48LtUkZRjU&list=PLPV2KyIb3jR5QFsefuO2RlAgWEz6EvVi6)
 * [JavaScript Tutorials](https://www.javascript.com/)
 * [Blender Tutorials](https://www.youtube.com/watch?v=B0J27sf9N1Y)

Now, continue reading for more info and a detailed specification on Banter's latest SDKv2.

## BullShcript SDK Reference

Here is a full specification of the Banter SDK in `JavaScript`. BullShcript is just JavaScript, it's just an april fools joke - for more info see here: [bullshc.rip/t](https://bullshc.rip/t).

### Banter Scene

The scene in Banter is the top level object that everything is stored inside. It stores all the GameObjects in the scene as-well as data and settings. It also handles all of the interactions with the scene hierarchy in `Unity`, and all of the low level messaging between `Unity` and `JavaScript` - though you don't need to worry about that.

<details><summary>Click Here For More Info</summary>

 To get a reference to the scene from anywhere:

 ```js
const scene = BS.BanterScene.getInstance();
```

 <b>Properties</b>
 
 `objects` - The list of `GameObject` objects in the scene, the basic building blocks of the space. 
 
 `components` - The `Component` functionality objects that are attached to all the `GameObject`s.
 
 `users` - All the `User` objects in the scene. These change when users join or leave the space.
 
 `localUser` - The local `User` object. This is set once the user joins the room, which happens after loading has completed. 
 
 `unityLoaded` - The unity scene is fully loaded, i.e. the loading screen is finished and the user is in the world. 
 
 `spaceState` - The current state of the space, an object containing all space state properties.   

 <b>Methods</b>

 ```js
// SetLoadPromise - Set a promise which will delay loading until the promise resolves, also await that promise.
// Normally loading will begin 500ms after you stop adding objects to the scene (debounced).
await scene.SetLoadPromise(callback: Promise<void>);
```
```js
// SetSettings - Set settings for the current space like spawn position, portals, guest access etc.
const settings = new BS.SceneSettings();
 
settings.EnableDevTools = true;
settings.EnableTeleport = true;
settings.EnableForceGrab = false;
settings.EnableSpiderMan = false;
settings.EnablePortals = true;
settings.EnableGuests = true;
settings.EnableQuaternionPose = false;
settings.EnableControllerExtras = false;
settings.EnableFriendPositionJoin = true;
settings.EnableDefaultTextures = true;
settings.EnableAvatars = true;
settings.MaxOccupancy = 20;
settings.RefreshRate = 72;
settings.ClippingPlane = new BS.Vector2(0.02, 1500);
settings.SpawnPoint = new BS.Vector4(0, 10, 0, 90); // x,y,z is position. w is Y rotation
 
await scene.SetSettings(settings);
```
```js
// Find - Find a GameObject in the scene by name - returns the first result. 
const gameObject = await scene.Find(name: string);
```
```js
// FindByPath - Find a GameObject in the scene by full path - returns the first result. 
const gameObject = await scene.FindByPath(path: string);
```
```js
// OpenPage - Open a page in the users menu browser.
await scene.OpenPage(url: string);
```
```js
// StartTTS - Start text-to-speech for the user, this will display a toast message to the user. If selected, wait to start detection until the user speaks.
await scene.StartTTS(voiceDetection: boolean);
```
```js
// StopTTS - Stop voice detection to wait for results. Specify an id to receive back with the response if multiple requests are pending.
await scene.StopTTS(id: string);
```
```js
// SendBrowserMessage - Send a post message to the browser in the menu. See Browser Communication for more info.
await scene.SendBrowserMessage(id: string);
```
```js
// Gravity - Set the gravity of the space as a Vector3. Default is (0,-9.8,0).
await scene.Gravity(vector: Vector3);
```
```js
// TimeScale - Set the timescale of the space as a float. Default is 1.
await scene.TimeScale(scale: number);
```
```js
// PlayerSpeed - Set the player speed to 'Normal' or 'Fast'.
await scene.PlayerSpeed(isFast: boolean);
```
```js
// TeleportTo - Teleport the player to a certain position and rotation on the y axis. 
// You can control if the velocity is stopped, for a portal-like teleport effect and 
//specify if the teleport is a spawn/respawn to respect the user's last spawn point.
await scene.TeleportTo(point: Vector3, rotation: number, stopVelocity: boolean, isSpawn: boolean = false);
```
```js
// SetPublicSpaceProps - Set a property on the space that will persist and be synced to all players. Like oneshot but includes persistance.
scene.SetPublicSpaceProps(props: {[key: string]: string});
```
```js
// SetProtectedSpaceProps - Set a property on the space that will persist and be synced to all players. The same as SetPublicSpaceProps except only admins/mods can set these.
scene.SetProtectedSpaceProps(props: {[key: string]: string});
```
```js
// SetUserProps - Set a property on the user that will persist until that user leaves the space. This will be synced to other players too.
scene.SetUserProps(props: {[key: string]: string}, id: string);
```
```js
// WaitForEndOfFrame - Wait until the end of the current frame
await scene.WaitForEndOfFrame();
```
```js
// Instantiate - Create a new GameObject from an existing GameObject cloning all components too. 
const newGameObject = await scene.Instantiate(object: GameObject);
```
```js
// Raycast - Cast a ray from an origin point in a certain direction to find objects that intersect. 
await scene.Raycast(origin: Vector3, direction: Vector3, distance: number, layerMask: number = 0);
```
```js
// OneShot - Send a message to all users in the space, or just the current instance. 
await scene.OneShot(data: any, allInstances = true);
```
```js
// QueryComponents - Query a bunch of properties on a bunch of components.
const transform = await gameObject.AddComponent(new BS.Transform());
const query = new BS.ComponentQuery();
query.Add(transform, [BS.PropertyName.position, BS.PropertyName.rotation]);
await scene.QueryComponents(query);
// Now the properties should be the latest values.
```
 
<b>Events</b>
 
 ```js
// loaded - Fired when the scene has settled and all objects are enumerated. This can be fired as a result of a custom load promise, or once no more objects have been added for 500ms.
scene.On("loaded", () => {
  // Do something.
})
```
```js
// unity-loaded - Fired when the scene is fully loaded and all assets have been fully downloaded. This fires as the loading screen disappears.
scene.On("unity-loaded", () => {
  // Do something.
})
```
```js
// transcription - Fired when the text-to-speech engine has finished transcribing
scene.On("transcription", e => {
  // Do something with e.detail.id and e.detail.message
})
```
```js
// button-pressed - Fired when the user presses a button on the controller.
scene.On("button-pressed", e => {
  // Do something with e.detail.button and e.detail.side
  // See BS.ButtonType and BS.HandSide for options. 
})
```
```js
// button-released - Fired when the user releases a button on the controller.
scene.On("button-released", e => {
  // Do something with e.detail.button and e.detail.side
  // See BS.ButtonType and BS.HandSide for options. 
})
```
```js
// menu-browser-message - Fired when the space receives a message from the page in the users browser.  
scene.On("menu-browser-message", e => {
  // Do something with e.detail
})
```
```js
// user-joined - Fired when a user joins the space.  
scene.On("user-joined", e => {
  // Do something with e.detail which is a User object.
})
```
```js
// user-left - Fired when a user leaves the space.  
scene.On("user-left", e => {
  // Do something with e.detail which is a User object.
})
```
```js
// space-state-changed - Fired when a space-state property changes.  
scene.On("space-state-changed", e => {
  // Do something with e.detail.changes which is a list of changed properties.
})
```
```js
// one-shot - Fired when a one-shot message is received from a user in the space.
scene.On("one-shot", e => {
  // Do something with e.detail.fromId, e.detail.fromAdmin and e.detail.data which is the ID and admin status of the user who sent the one-shot message, and the message itself.
})
```
```js
// aframe-trigger - Fired when a message is received from an AframeTrigger.cs unity script in the scene. 
scene.On("aframe-trigger", e => {
  // Do something with e.detail.data which is a message received from the trigger.
})
```
```js
// key-press - Fired when a key is pressed on the keyboard. See BS.KeyCode for a full list. 
scene.On("key-press", e => {
  // Do something with e.detail.key which is the key that was pressed.
})
```
```js
// voice-started - Fired when text-to-speech begins to listen to the voice.
scene.On("voice-started", e => {
  // Do something.
})
```
</details>

### Game Object

A GameObject can be thought of as the same as a `Unity` GameObject, it's the basic building block of creating a scene in `Unity`.

<details><summary>Click Here For More Info</summary>
 
 <b>Properties</b>
 
 `name` - The name this object was given when created. (read-only)
 `path` - The path of this object in the hierarchy or sub-hierarchy. (read-only)
 `active` - The active status of this object. (read-only)
 `components` - A list of the components on this object.
 `id` - The id of this object. Before the equivalent unity object is created this is a random UUID but then this becomes the InstanceID of the Unity GameObject.
 `parent` - The id of this object's parent.
 `layer` - This objects current layer.
 `hasUnity` - A flag to tell if this object's Unity equivalent GameObject has been created yet.
 `scene` - A reference to the BanterScene.
 `meta` - A generic object to store user metadata against this object. 

 <b>Methods</b>
 ```js
// Async - Get a promise that resolves the GameObject when the object has been created in Unity. 
// Useful when you create a GameObject via the constructor. 
const gameObject = await new BS.GameObject().Async();
// Alternatively you can use await BS.CreateGameObject().
const gameObject = await new BS.CreateGameObject();
```
 
 ```js
// SetActive - Enable/Disable this object and its children. 
gameObject.SetActive(active: boolean);
```
```js
// SetParent - Set the parent of this GameObject.
await gameObject.SetParent(parent: GameObject, worldPositionStays: boolean = true);
```
```js
// AddComponent - Add a Banter Component to the object such as BanterAudioSource and return that component. See the menu for a full list.
const component = await gameObject.AddComponent(component: Component);
```
```js
// SetLayer - Set the layer of this GameObject. This corresponds to the layer in unity. See the Layers section in the menu for a full list.
await gameObject.SetLayer(layer: number);
```
```js
// GetComponent - Grabs the component by type such as BS.ComponentType.BanterAudioSource.
const component = gameObject.GetComponent(type: ComponentType);
```
```js
// Find - Get a child GameObject by name or path.
const newGameObject = await gameObject.Find(path: string);
```
```js
// Destroy - Destroy this GameObject and all its children and all components below.
gameObject.Destroy();
```
```js
// Traverse - Iterate through all children of this object recursively or iterate through all parents (decendants) of this GameObject until you reach the top.
gameObject.Traverse(callback: (o: GameObject) => void, decendants: boolean);
```
 
 <b>Code Example</b>
 
 ```js
const gameObject = await new BS.GameObject("My Game Object").Async();
```

 <b>Events</b>
 
 ```js
// click - Fired when a user clicks on the object. 
gameObject.On("click", e => {
  // Do something with e.detail.point and e.detail.normal.
})
```
```js
// grab - Fired when a user grabs the object. 
gameObject.On("grab", e => {
  // Do something with e.detail.point (Vector3) e.detail.normal (Vector3) and e.detail.side (BS.HandSide).
})
```
```js
// drop - Fired when a user drops the object. 
gameObject.On("drop", e => {
  // Do something with e.detail.side (BS.HandSide).
})
```
```js
// collision-enter - Fired when something collides with the object at first. 
gameObject.On("collision-enter", e => {
  // Do something with e.detail which contains the following properties: name, tag, collider, point, normal, user. The User property is only set if the collision happens with one of the colliders on a user.
})
```
```js
// collision-exit - Fired when something collides with the object at first. 
gameObject.On("collision-exit", e => {
  // Do something with e.detail which contains the following properties: name, tag, collider, object.
})
```
```js
// trigger-enter - Fired when something hits a trigger. 
gameObject.On("trigger-enter", e => {
  // Do something with e.detail which contains the following properties: name, tag, collider, object.
})
```
```js
// trigger-exit - Fired when something hits a trigger. 
gameObject.On("trigger-exit", e => {
  // Do something with e.detail which contains the following properties: name, tag, collider, object.
})
```
```js
// browser-message - Fired when a message is received from a browser in the space.  
gameObject.On("browser-message", e => {
  // Do something with e.detail.
})
```
</details>

### Banter Constants/Enums

THe below are some constants/enums used for various thing sin the banter SDK.

<details><summary>ComponentType</summary>

 A list of the different component types used for `GetComponent`:

 ```js
enum BS.ComponentType{
    BanterAssetBundle,
    BanterAudioSource,
    BanterBillboard,
    BoxCollider,
    BanterBrowser,
    CapsuleCollider,
    BanterColliderEvents,
    ConfigurableJoint,
    BanterGeometry,
    BanterGLTF,
    BanterInvertedMesh,
    BanterKitItem,
    BanterMaterial,
    MeshCollider,
    BanterMirror,
    BanterPhysicMaterial,
    BanterPortal,
    BanterRigidbody,
    SphereCollider,
    BanterStreetView,
    BanterText,
    Transform,
    BanterVideoPlayer,
}
```
</details>

<details><summary>PropertyName</summary>

 A list of all property names used by different components in banter:

 ```js
enum BS.PropertyName {
    hasUnity,
    windowsUrl,
    osxUrl,
    linuxUrl,
    androidUrl,
    iosUrl,
    vosUrl,
    isScene,
    legacyShaderFix,
    volume,
    pitch,
    mute,
    loop,
    bypassEffects,
    bypassListenerEffects,
    bypassReverbZones,
    playOnAwake,
    smoothing,
    enableXAxis,
    enableYAxis,
    enableZAxis,
    isTrigger,
    center,
    size,
    url,
    mipMaps,
    pixelsPerUnit,
    pageWidth,
    pageHeight,
    actions,
    radius,
    height,
    targetPosition,
    autoConfigureConnectedAnchor,
    xMotion,
    yMotion,
    zMotion,
    geometryType,
    parametricType,
    width,
    depth,
    widthSegments,
    heightSegments,
    depthSegments,
    segments,
    thetaStart,
    thetaLength,
    phiStart,
    phiLength,
    radialSegments,
    openEnded,
    radiusTop,
    radiusBottom,
    innerRadius,
    outerRadius,
    thetaSegments,
    phiSegments,
    tube,
    tubularSegments,
    arc,
    p,
    q,
    stacks,
    slices,
    detail,
    parametricPoints,
    generateMipMaps,
    addColliders,
    nonConvexColliders,
    slippery,
    climbable,
    legacyRotate,
    path,
    shaderName,
    texture,
    color,
    side,
    convex,
    dynamicFriction,
    staticFriction,
    instance,
    mass,
    drag,
    angularDrag,
    isKinematic,
    useGravity,
    centerOfMass,
    collisionDetectionMode,
    freezePositionX,
    freezePositionY,
    freezePositionZ,
    freezeRotationX,
    freezeRotationY,
    freezeRotationZ,
    velocity,
    angularVelocity,
    panoId,
    text,
    horizontalAlignment,
    verticalAlignment,
    fontSize,
    richText,
    enableWordWrapping,
    rectTransformSizeDelta,
    lerpPosition,
    lerpRotation,
    position,
    localPosition,
    rotation,
    localRotation,
    localScale,
    eulerAngles,
    localEulerAngles,
    up,
    forward,
    right,
    skipOnDrop,
    time,
    waitForFirstFrame,
}
```
</details>

<details><summary>BanterLayers</summary>

 A list of all layers you can use on GameObjects in Banter:

 ```js
enum BS.BanterLayers {
    UserLayer1 = 3,
    UserLayer2 = 6,
    UserLayer3 = 7,
    UserLayer4 = 8,
    UserLayer5 = 9,
    UserLayer6 = 10,
    UserLayer7 = 11,
    UserLayer8 = 12,
    UserLayer9 = 13,
    UserLayer10 = 14,
    UserLayer11 = 15,
    UserLayer12 = 16,
    NetworkPlayer = 17,
    RPMAvatarHead = 18,
    RPMAvatarBody = 19,
    Grabbable = 20,
    HandColliders = 21,
    WalkingLegs = 22,
    PhysicsPlayer = 23,
    BanterInternal1_DONTUSE = 24,
    BanterInternal2_DONTUSE = 25,
    BanterInternal3_DONTUSE = 26,
    BanterInternal4_DONTUSE = 27,
    BanterInternal5_DONTUSE = 28,
    BanterInternal6_DONTUSE = 29,
    BanterInternal7_DONTUSE = 30,
    BanterInternal8_DONTUSE = 31,
}
```
</details>

<details><summary>ButtonType</summary>

 The button types for each controller:

 ```js
enum BS.ButtonType{
    TRIGGER,
    GRIP,
    PRIMARY,
    SECONDARY,
}
```
</details>

<details><summary>GeometryType</summary>

 The different geometry types:

 ```js
enum BS.GeometryType{
    BoxGeometry,
    CircleGeometry,
    ConeGeometry,
    CylinderGeometry,
    PlaneGeometry,
    RingGeometry,
    SphereGeometry,
    TorusGeometry,
    TorusKnotGeometry,
    ParametricGeometry,
}
```
</details>

<details><summary>ParametricGeometryType</summary>

 The different parametric geometry types:

 ```js
enum BS.ParametricGeometryType{
    Klein,
    Apple,
    Fermet,
    Catenoid,
    Helicoid,
    Horn,
    Mobius,
    Mobius3d,
    Natica,
    Pillow,
    Scherk,
    Snail,
    Spiral,
    Spring,
    Custom,
}
```
</details>

<details><summary>HandSide</summary>

 The sides of the hands, left and right:

 ```js
enum BS.HandSide{
    LEFT,
    RIGHT,
}
```
</details>

<details><summary>ForceMode</summary>

 The types of rigid body force:

 ```js
enum BS.ForceMode{
    Force,
    Impulse,
    VelocityChange,
    Acceleration,
}
```
</details>

<details><summary>MaterialSide</summary>

 The sides to render a meterial on:

 ```js
enum BS.MaterialSide{
    Front,
    Back,
    Double,
}
```
</details>

<details><summary>VerticalAlignment</summary>

 The alignment of text:

 ```js
enum BS.VerticalAlignment{
    Top,
    Center,
    Bottom,
}
```
</details>

<details><summary>HorizontalAlignment</summary>

 The alignment of text:

 ```js
enum BS.HorizontalAlignment{
    Left,
    Center,
    Right,
}
```
</details>

### Components

Components are the real functionality for GameObjects, they can add almost any kind of capability or asset to a GameObject. 
 

#### Common to all Components


**Properties**

- `gameObject` 
- `scene`



#### Banter Asset Bundle
Load an asset bundle into Banter which contains a scene or a collection of prefabs.

**Properties**
- `windowsUrl` - The URL to the Windows asset bundle.
- `osxUrl` - The URL to the OSX asset bundle.
- `linuxUrl` - The URL to the Linux asset bundle.
- `androidUrl` - The URL to the Android asset bundle.
- `iosUrl` - The URL to the iOS asset bundle.
- `vosUrl` - The URL to the Vision OS asset bundle.
- `isScene` - If the asset bundle is a scene or a collection of prefabs.
- `legacyShaderFix` - If the asset bundle requires a legacy shader/lighting fix like we had in the past with A-FRAME.

**Code Example**
```js
    const windowsUrl = "https://example.bant.ing/windows.banter";
    const osxUrl = null; // Not implemented yet...
    const linuxUrl = null; // Not implemented yet...
    const androidUrl = "https://example.bant.ing/android.banter";
    const iosUrl = null; // Not implemented yet...
    const vosUrl = null; // Not implemented yet...
    const isScene = true;
    const legacyShaderFix = false;
    const gameObject = new BS.GameObject("MyAssetBundle"); 
    const assetBundle = await gameObject.AddComponent(new BS.BanterAssetBundle(windowsUrl, osxUrl, linuxUrl, androidUrl, iosUrl, vosUrl, isScene, legacyShaderFix));
```



#### Banter Audio Source
Load an audio file from a URL, or from a list of files in the editor.

**Properties**
- `volume` - The volume of the audio source.
- `pitch` - The pitch of the audio source.
- `mute` - Is the audio source muted?
- `loop` - Should the audio source loop?
- `bypassEffects` - Bypass effects?
- `bypassListenerEffects` - Bypass listener effects?
- `bypassReverbZones` - Bypass reverb zones?
- `playOnAwake` - Should the audio source play on awake?

**Methods**
```js
// PlayOneShot - Play a clip from the list of clips.
audioSource.PlayOneShot(index: number);
```
```js
// PlayOneShotFromUrl - Play a clip from a URL.
audioSource.PlayOneShotFromUrl(url: string);
```
```js
// Play - Play the current audio clip.
audioSource.Play();
```

**Code Example**
```js
    const volume = 1;
    const pitch = 1;
    const mute = false;
    const loop = false;
    const bypassEffects = false;
    const bypassListenerEffects = false;
    const bypassReverbZones = false;
    const playOnAwake = false;

    const gameObject = new BS.GameObject("MyAudioSource"); 
    const audioSource = await gameObject.AddComponent(new BS.BanterAudioSource(volume, pitch, mute, loop, bypassEffects, bypassListenerEffects, bypassReverbZones, playOnAwake));
    // ...
    audioSource.Play();
    // ...
    audioSource.PlayOneShot(0);
    // ...
    audioSource.PlayOneShotFromUrl("https://example.com/music.mp3");
```



#### Banter Billboard
Make an object look at the player camera.

**Properties**
- `smoothing` - The smoothing of the billboard.
- `enableXAxis` - Enable the X axis.
- `enableYAxis` - Enable the Y axis.
- `enableZAxis` - Enable the Z axis.

**Code Example**
```js
    const smoothing = 0;
    const enableXAxis = true;
    const enableYAxis = true;
    const enableZAxis = true;
    const gameObject = new BS.GameObject("MyBillboard"); 
    const billBoard = await gameObject.AddComponent(new BS.BanterBillboard(smoothing, enableXAxis, enableYAxis, enableZAxis));

```



#### Box Collider
Add a box shaped physics collider to the object.

**Properties**
- `isTrigger` - If the collider is a trigger.
- `center` - The center of the box.
- `size` - The size of the box.

**Code Example**
```js
    const isTrigger = false;
    const center = new BS.Vector3(0,0,0);
    const size = new BS.Vector3(1,1,1);
    const gameObject = new GameObject("MyBoxCollider"); 
    const boxCollider = await gameObject.AddComponent(new BS.BoxCollider(isTrigger, center, size));
```


#### Banter Browser
A browser component that can be added to a GameObject to display a webpage.

**Properties**
- `url` - The URL of the webpage to display.
- `mipMaps` - The number of mipmaps to use.
- `pixelsPerUnit` - The number of pixels per unit.
- `actions` - A list of actions to run after the page has loaded.

**Methods**
- `ToggleInteraction(enabled: boolean)` - Toggles the interaction of the browser.
- `RunActions(actions: string)` - Runs a list of actions on the browser.

**Code Example**
```js
    const url = "https://www.google.com";
    const mipMaps = 4;
    const pixelsPerUnit = 1200;
    const actions = "click2d,0.5,0.5";
    const gameObject = new BS.GameObject("MyBrowser"); 
    const pageWidth = 1280;
    const pageHeight = 720;
    const browser = await gameObject.AddComponent(new BS.BanterBrowser(url, mipMaps, pixelsPerUnit, pageWidth, pageHeight, actions));
    // ...
    browser.ToggleInteraction(true);
    // ...
    browser.RunActions("click2d,0.5,0.5");
```



#### Capsule Collider
Add a capsule shaped physics collider to the object.

**Properties**
- `isTrigger` - If the collider is a trigger.
- `radius` - The radius of the capsule.
- `height` - The height of the capsule.

**Code Example**
```js
    const isTrigger = false;
    const radius = 0.5;
    const height = 2;
    const gameObject = new GameObject("MyCapsuleCollider"); 
    const capsuleCollider = await gameObject.AddComponent(new BS.CapsuleCollider(isTrigger, radius, height));
```


#### Banter Collider Events
Fire collision and trigger events on the object. This component is required for the `trigger-enter` etc events to work.

**Code Example**
```js
    const gameObject = new BS.GameObject("MyBanterColliderEvents"); 
    const events = await gameObject.AddComponent(new BS.BanterColliderEvents());
```


#### Banter Configurable Joint
A configurable joint allows you to create a joint between two rigidbodies and control the motion/options of the joint.

**Properties**
- `targetPosition` - The target position of the joint.
- `autoConfigureConnectedAnchor` - If the connected anchor should be auto configured.
- `xMotion` - The x motion of the joint.
- `yMotion` - The y motion of the joint.
- `zMotion` - The z motion of the joint.

**Code Example**
```js
    const targetPosition = new BS.Vector3(0,0,0);
    const autoConfigureConnectedAnchor = false;
    const xMotion = 0;
    const yMotion = 0;
    const zMotion = 0;

    const gameObject = new BS.GameObject("MyConfigurableJoint"); 
    const configurableJoint = await gameObject.AddComponent(new BS.BanterConfigurableJoint(targetPosition, autoConfigureConnectedAnchor, xMotion, yMotion, zMotion));
```



#### Banter Geometry
Add a geometry shape to the object. This can be a box, circle, cone, cylinder, plane, ring, sphere, torus, torus knot, or a custom parametric shape.

**Properties**
- `geometryType` - The type of primitive shape to generate.
- `parametricType` - The type of parametric shape to generate if using ParametricGeometry.
- `width` - The width of the shape.
- `height` - The height of the shape.
- `depth` - The depth of the shape.
- `widthSegments` - The number of width segments to divide the shape into.
- `heightSegments` - The number of height segments to divide the shape into.
- `depthSegments` - The number of depth segments to divide the shape into.
- `radius` - The radius of the shape.
- `segments` - The number of segments to divide the shape into.
- `thetaStart` - The starting x angle of the shape.
- `thetaLength` - The ending x angle of the shape.
- `phiStart` - The starting y angle of the shape.
- `phiLength` - The ending y angle of the shape.
- `radialSegments` - The number of radial segments to divide the shape into.
- `openEnded` - Whether the object is open on the end or not.
- `radiusTop` - The radius of the top of the shape.
- `radiusBottom` - The radius of the bottom of the shape.
- `innerRadius` - The inner radius of the shape.
- `outerRadius` - The outer radius of the shape.
- `thetaSegments` - The number of angular x segments to divide the shape into.
- `phiSegments` - The number of angular y segments to divide the shape into.
- `tube` - The tube size.
- `tubularSegments` - The number of tubular segments to divide the tube into.
- `arc` - The arc of the shape.
- `p` - The number of p segments to divide the shape into.
- `q` - The number of q segments to divide the shape into.
- `stacks` - The number of stacks to divide the shape into.
- `slices` - The number of slices to divide the shape into.
- `detail` - The detail of the shape.
- `parametricPoints` - The points of the parametric shape.

**Code Example**
```js
    const geometryType = BS.GeometryType.BOxGeometry;
    const parametricType = null;
    const width = 1;
    const height = 1;
    const depth = 1;
    const widthSegments = 1;
    const heightSegments = 1;
    const depthSegments = 1;
    const radius = 1;
    const segments = 24;
    const thetaStart = 0;
    const thetaLength = 6.283185;
    const phiStart = 0;
    const phiLength = 6.283185;
    const radialSegments = 8;
    const openEnded = false;
    const radiusTop = 1;
    const radiusBottom = 1;
    const innerRadius = 0.3;
    const outerRadius = 1;
    const thetaSegments = 24;
    const phiSegments = 8;
    const tube = 0.4;
    const tubularSegments = 16;
    const arc = 6.283185;
    const p = 2;
    const q = 3;
    const stacks = 5;
    const slices = 5;
    const detail = 0;
    const parametricPoints = "";
    const gameObject = new BS.GameObject("MyGeometry");
    const geometry = await gameObject.AddComponent(new BS.BanterGeometry(geometryType, parametricType, width, height, depth, widthSegments, heightSegments, depthSegments, radius, segments, thetaStart, thetaLength, phiStart, phiLength, radialSegments, openEnded, radiusTop, radiusBottom, innerRadius, outerRadius, thetaSegments, phiSegments, tube, tubularSegments, arc, p, q, stacks, slices, detail, parametricPoints));
```


#### Banter GLTF
Add a 3D Model from a glb file to the scene.

**Properties**
- `url` - The url of the glb file.
- `generateMipMaps` - If mipmaps should be generated.
- `addColliders` - If colliders should be added.
- `nonConvexColliders` - If colliders should be non convex.
- `slippery` - If colliders should be slippery.
- `climbable` - If colliders should be climbable.
- `legacyRotate` - If the model should be rotated to match the old GLTF forward direction.

**Code Example**
```js
    const url = "https://example.com/model.glb";
    const generateMipMaps = false;
    const addColliders = false;
    const nonConvexColliders = false;
    const slippery = false;
    const climbable = false;
    const legacyRotate = false;
    const gameObject = new BS.GameObject("MyGLTF"); 
    const gltf = await gameObject.AddComponent(new BS.BanterGLTF(url, generateMipMaps, addColliders, nonConvexColliders, slippery, climbable, legacyRotate));
```


#### Banter Inverted Mesh
Invert the mesh of a GameObject. This is useful for creating inverted colliders.

**Code Example**
```js
    const gameObject = new BS.GameObject("MyInvertedMesh"); 
    const invertedMesh = await gameObject.AddComponent(new BS.BanterInvertedMesh());
```


#### Banter Kit Item
Add a kit item to the scene. This component will wait until all asset bundles are loaded in the scene before trying to instantiate a kit item (prefab).

**Properties**
- `path` - The location of the prefab in the kit object - the same as the path in the asset bundle (always lower case).

**Code Example**
```js
    const path = "assets/prefabs/mykititem.prefab";
    const gameObject = new BS.GameObject("MyKitItem"); 
    const kitItem = await gameObject.AddComponent(new BS.BanterKitItem(path));
```



#### Banter Material
Add a material to the object. This component will add a material to the object and set the shader, texture, color and side of the material.

**Properties**
- `texture` - The texture to use for the material.
- `color` - The color of the material.
- `shaderName` - The name of the shader to use.
- `side` - The side of the material to render.
- `generateMipMaps` - Whether to generate mipmaps for the texture.

**Code Example**
```js
    const texture = "https://cdn.glitch.global/7bdd46d4-73c4-47a1-b156-10440ceb99fb/GridBox_Default.png?v=1708022523716";
    const color = new BS.Vector4(1,1,1,1);
    const shaderName = "Unlit/Diffuse";
    const side = BS.MaterialSide.Front;
    const generateMipMaps = false;

    const gameObject = new BS.GameObject("MyMaterial"); 
    const material = await gameObject.AddComponent(new BS.BanterMaterial(shaderName, texture, color, side, generateMipMaps));

```



#### Mesh Collider
Add a mesh shaped physics collider to the object. This requires a mesh to be supplied, or used from a MeshFilter component.

**Properties**
- `isTrigger` - If the collider is a trigger.
- `convex` - If the collider is convex i.e. has no holes or concave parts.

**Code Example**
```js
    const isTrigger = false;
    const convex = true;
    const gameObject = new BS.GameObject("MyMeshCollider"); 
    const meshCollider = await gameObject.AddComponent(new BS.MeshCollider(isTrigger, convex));
```


#### Banter Mirror
Add a mirror to the object.

**Code Example**
```js
    const gameObject = new BS.GameObject("MyMirror");
    const mirror = await gameObject.AddComponent(new BS.BanterMirror());
```


#### Banter Physic Material
This component will add a physic material to the object and set the dynamic and static friction of the material.

**Properties**
- `dynamicFriction` - The dynamic friction of the material.
- `staticFriction` - The static friction of the material.

**Code Example**
```js
    const dynamicFriction = 1;
    const staticFriction = 1;
    const gameObject = new BS.GameObject("MyPhysicMaterial");
    const physicMaterial = await gameObject.AddComponent(new BS.BanterPhysicMaterial(dynamicFriction, staticFriction));
```


#### Banter Portal
This component will add a portal to the object and set the url and instance of the portal.

**Properties**
- `url` - The url of the space to link to.
- `instance` - The instance of the space to link to.

**Code Example**
```js
    const url = "https://banter.host/space/5f9b4";
    const instance = "5f9b4";
    const gameObject = new BS.GameObject("MyPortal");
    const portal = await gameObject.AddComponent(new BS.BanterPortal(url, instance));
```



#### Banter Rigidbody
This component will add a rigidbody to the object and set the mass, drag, angular drag, is kinematic, use gravity, center of mass, collision detection mode, velocity, angular velocity, freeze position and freeze rotation of the rigidbody.

**Properties**
- `mass` - The mass of the rigidbody.
- `drag` - The drag of the rigidbody.
- `angularDrag` - The angular drag of the rigidbody.
- `isKinematic` - Whether the rigidbody is kinematic.
- `useGravity` - Whether the rigidbody uses gravity.
- `centerOfMass` - The center of mass of the rigidbody.
- `collisionDetectionMode` - The collision detection mode of the rigidbody.
- `velocity` - The velocity of the rigidbody.
- `angularVelocity` - The angular velocity of the rigidbody.
- `freezePositionX` - Whether to freeze the position on the x axis.
- `freezePositionY` - Whether to freeze the position on the y axis.
- `freezePositionZ` - Whether to freeze the position on the z axis.
- `freezeRotationX` - Whether to freeze the rotation on the x axis.
- `freezeRotationY` - Whether to freeze the rotation on the y axis.
- `freezeRotationZ` - Whether to freeze the rotation on the z axis.

**Methods**
```js
// AddForce - Add a force to the rigidbody.
rigidBody.AddForce(force: BS.Vector3, mode: BS.ForceMode);
```
```js
// MovePosition - Move the position of the rigidbody.
rigidBody.MovePosition(position: BS.Vector3);
```
```js
// MoveRotation - Move the rotation of the rigidbody.
rigidBody.MoveRotation(rotation: BS.Quaternion);
```
```js
// AddForceValues - Add a force to the rigidbody.
rigidBody.AddForceValues(x: float, y: float, z: float, mode: BS.ForceMode);
```
```js
// Sleep - Put the rigidbody to sleep.
rigidBody.Sleep();
```
```js
// AddExplosionForce - Add an explosion force to the rigidbody.
rigidBody.AddExplosionForce(explosionForce: float, explosionPosition: BS.Vector3, explosionRadius: float, upwardsModifier: float, mode: BS.ForceMode);
```


**Code Example**
```js
    const mass = 1;
    const drag = 0;
    const angularDrag = 0.05;
    const isKinematic = false;
    const useGravity = true;
    const centerOfMass = new BS.Vector3(0,0,0);
    const collisionDetectionMode = BS.CollisionDetectionMode.Continuous;
    const velocity = new BS.Vector3(0,0,0);
    const angularVelocity = new BS.Vector3(0,0,0);
    const freezePositionX = false;
    const freezePositionY = false;
    const freezePositionZ = false;
    const freezeRotationX = false;
    const freezeRotationY = false;
    const freezeRotationZ = false;
    const gameObject = new BS.GameObject("MyRigidbody");
    const rigidBody = await gameObject.AddComponent(new BS.BanterRigidbody(mass, drag, angularDrag, isKinematic, useGravity, centerOfMass, collisionDetectionMode, freezePositionX, freezePositionY, freezePositionZ, freezeRotationX, freezeRotationY, freezeRotationZ, velocity, angularVelocity));
```


#### Sphere Collider
Add a sphere shaped physics collider to the object.

**Properties**
- `isTrigger` - If the collider is a trigger.
- `radius` - The radius of the sphere.

**Code Example**
```js
    const isTrigger = false;
    const radius = 0.5;
    const gameObject = new BS.GameObject("MySphereCollider"); 
    const sphereCollider = await gameObject.AddComponent(new BS.SphereCollider(isTrigger, radius));
```


#### Banter StreetView
This component will add a streetview dome to the object and set the panoId of the streetview.

**Properties**
    - `panoId` - The panoId of the streetview.

**Code Example**
```js
    const panoId = "CAoSLEFGM";
    const gameObject = new BS.GameObject("MyStreetView");
    const streetView = await gameObject.AddComponent(new BS.BanterStreetView(panoId));
```


#### Banter Text
This component will add a text component to the object and set the text, color, alignment, font size, rich text, word wrapping and size delta of the text.

**Properties**
- `text` - The text to display.
- `color` - The color of the text.
- `horizontalAlignment` - The horizontal alignment of the text.
- `verticalAlignment` - The vertical alignment of the text.
- `fontSize` - The font size of the text.
- `richText` - Whether the text is rich text.
- `enableWordWrapping` - Whether to enable word wrapping.
- `rectTransformSizeDelta` - The size delta of the text.

**Code Example**
```js
    const text = "Hello World";
    const color = new BS.Vector4(1,1,1,1);
    const horizontalAlignment = BS.HorizontalAlignment.Center;
    const verticalAlignment = BS.VerticalAlignment.Middle;
    const fontSize = 20;
    const richText = true;
    const enableWordWrapping = true;
    const rectTransformSizeDelta = new BS.Vector2(20,5);
    const gameObject = new BS.GameObject("MyText");
    const text = await gameObject.AddComponent(new BS.BanterText(text, color, horizontalAlignment, verticalAlignment, fontSize, richText, enableWordWrapping, rectTransformSizeDelta));
```


#### Transform
Add a transform to the object. Every object has a transform by default in Unity, this component sets up tracking for the transform properties.

**Properties**
- `position` - The position of the object.
- `localPosition` - The local position of the object.
- `rotation` - The rotation of the object.
- `localRotation` - The local rotation of the object.
- `localScale` - The local scale of the object.
- `eulerAngles` - The euler angles of the object.
- `localEulerAngles` - The local euler angles of the object.
- `up` - The up vector of the object.
- `forward` - The forward vector of the object.
- `right` - The right vector of the object.
- `lerpPosition` - Whether to lerp the position of the object.
- `lerpRotation` - Whether to lerp the rotation of the object.

**Code Example**
```js
    const gameObject = new BS.GameObject("MyTransform");
    const transform = await gameObject.AddComponent(new BS.Transform());
    transform.position = new BS.Vector3(1,1,1);
```


#### Banter Video Player
This component will add a video player to the object and set the url, volume, loop, playOnAwake, skipOnDrop and waitForFirstFrame of the video player.

**Properties**
- `url` - The url of the video to play.
- `volume` - The volume of the video.
- `loop` - Whether the video should loop.
- `playOnAwake` - Whether the video should play on awake.
- `skipOnDrop` - Whether the video should skip on drop.
- `waitForFirstFrame` - Whether the video should wait for the first frame.

**Code Example**
```js
    const url = "https://cdn.glitch.global/7bdd46d4-73c4-47a1-b156-10440ceb99fb/GridBox_Default.mp4?v=1708022523716";
    const volume = 0.5;
    const loop = true;
    const playOnAwake = true;
    const skipOnDrop = true;
    const waitForFirstFrame = true;
    const gameObject = new BS.GameObject("MyVideoPlayer");
    const videoPlayer = await gameObject.AddComponent(new BS.BanterVideoPlayer(url, volume, loop, playOnAwake, skipOnDrop, waitForFirstFrame));
```
    

### Math

Use these to set properties on Banter Components.

#### Vector2

A two dimensional vector, x and y.

```js
const uv = new BS.Vector2(0,1);

```

#### Vector3

A three dimensional vector, x, y and z.

```js
const position = new BS.Vector3(0,1,0);

```

#### Vector4

A four dimensional vector, x, y, z and w.

```js
const color = new BS.Vector4(0,0,0,1);
```

#### Quaternion

A rotational object involving imaginary numbers invented by a mad Irish mathematician. 

```js
const rotation = new BS.Quaternion(0,0,0,1);
```
