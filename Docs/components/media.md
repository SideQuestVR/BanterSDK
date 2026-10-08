# Media & Content Components

## GLTF

Loads a 3D model from a binary glTF (`.glb`) file on the web. The model is added as a child of the object, facing the object's blue axis (+Z).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `url` | string | "" | Address of the `.glb` file |
| `addColliders` | boolean | false | Give every mesh in the model a Mesh Collider (convex unless `nonConvexColliders` is on) |
| `nonConvexColliders` | boolean | false | Make those colliders follow the meshes exactly (non-convex). Turning it on also adds the colliders |
| `slippery` | boolean | false | Give those colliders zero friction. Turning it on also adds the colliders |
| `climbable` | boolean | false | Put the meshes with those colliders on the Grabbable layer (20), so players can climb the model. Only with colliders turned on |
| `legacyRotate` | boolean | false | Turn the model 180° around Y, for models made for the older forward direction |
| `childrenLayer` | number | 0 | Layer for every part of the model (0 = Default) |
| `generateMipMaps` | boolean | false | Ignored: models always get mipmaps |

<div class="docs-tabs">

```js
await obj.AddComponent(new BS.GLTF({
    url: "https://example.com/model.glb",
    addColliders: true,       // so players can stand on it
    nonConvexColliders: true, // exact collision for scenery that doesn't move
    climbable: false,
    legacyRotate: false,
    childrenLayer: 0
}));
```

![BS GLTF in the Unity Inspector](../images/components/gltf.png)

</div>

Changing `url` loads the new model in place of the old one; a change made while a model is still loading is ignored. The model's parts are separate objects, so a click on one of their colliders isn't reported to the GLTF's object. For a clickable model, leave the model's colliders off and give the object itself a collider, on the UI layer (see [Layers](../building-in-unity/layers.md)).

**Node-name markers:** you can mark parts of the model in your 3D tool by putting these in a node's name. They work whatever the properties above say:

| Name contains | Effect |
|---|---|
| `sq-collider` | The node gets a Mesh Collider |
| `sq-nonconvexcollider` | The node gets a non-convex Mesh Collider (nodes with a mesh only) |
| `sq-climbable` | The node goes on the Grabbable layer (20), so it can be climbed |

## VideoPlayer

Plays a video on the object's own mesh: give the object a shape first, such as a [BS.Plane](geometry-primitives.md#plane) with a [BS.Material](rendering.md#material). **GameObject > BS > Objects > Video Player** sets one up for you.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `url` | string | "" | Address of the video file |
| `volume` | number | 0.5 | Volume, 0 to 1 |
| `loop` | boolean | false | Start again at the end |
| `playOnAwake` | boolean | true | Play as soon as the `url` is set; when off, the video is only loaded and waits for `PlayToggle()` |
| `skipOnDrop` | boolean | true | Skip frames to keep up when playback falls behind |
| `waitForFirstFrame` | boolean | true | Wait for the first frame before playback starts |
| `time` | number | 0 | Playback position in seconds; set it to seek |
| `isPlaying`, `isLooping`, `isPrepared`, `isMuted` | boolean | — | Read-only: the player's state |
| `duration` | number | — | Read-only: the video's length in seconds |

<div class="docs-tabs">

```js
await obj.AddComponent(new BS.Plane({ width: 1.6, height: 0.9 }));
await obj.AddComponent(new BS.Material({ color: new BS.Vector4(1, 1, 1, 1) }));
const video = await obj.AddComponent(new BS.VideoPlayer({
    url: "https://example.com/video.mp4",
    volume: 1,
    loop: true,
    playOnAwake: false    // load it, then start it from a button
}));
```

![BS Video Player in the Unity Inspector](../images/components/video-player.png)

</div>

**Methods:**

```js
video.PlayToggle();   // Play, or pause if playing. Does nothing until the video is loaded (isPrepared)
video.MuteToggle();   // Mute or unmute
video.Stop();         // Stop and go back to the start
```

**Properties:**

```js
video.time = 30;                                          // Seek to 30 seconds
video.url = "https://example.com/next.mp4";               // Switch videos; keeps playing if it was

const playing = await video.GetProperty(BS.PN.isPlaying); // Read the live state
video.WatchProperties([BS.PN.time]);                      // Keep video.time up to date (about once a second)
```

The read-only properties keep the value they had when your script last asked, so read them with `GetProperty` (or `time` with `WatchProperties`) rather than straight off the component.

The sound goes straight to the player's audio device, the same wherever they stand. In the Inspector, **Route Audio Through Audio Source** sends it through an Audio Source on the object instead, so it comes from the screen and is included in in-app recordings; set it before the video starts.

## Browser

A web page on a flat panel in your world. Everything browsers can do (messages, textures, actions, the space's own browser) is in the [Browser](../browser/overview.md) section.

<div class="docs-tabs">

```js
const browser = await obj.AddComponent(new BS.Browser({
    url: "https://example.com",
    pageWidth: 1280,        // pixels; drawn at 1300 pixels per metre
    pageHeight: 720,
    actions: ""             // JSON actions to run when it starts
}));
```

![BS Browser in the Unity Inspector](../images/components/browser.png)

</div>

**Methods:**

```js
browser.ToggleInteraction(false);   // stop clicks and scrolling reaching the page (true turns them back on)
browser.RunActions(JSON.stringify({ actions: [{ actionType: "postmessage", strParam1: "hello" }] }));
```

`pageWidth` and `pageHeight` can change at any time; a browser created from a script starts at 1024 × 576. `mipMaps` and `pixelsPerUnit` aren't used. See [The BS Browser](../browser/bs-browser.md) for the action types, events and sizes.

## StreetView

Google Street View panorama viewer: surrounds the object with the panorama.

<div class="docs-tabs">

```js
await obj.AddComponent(new BS.StreetView({
    panoId: "CAoSLEFGM..."  // Street View panorama ID
}));
```

![BS Street View in the Unity Inspector](../images/components/street-view.png)

</div>

Changing `panoId` loads the new panorama.

## Portal

A doorway to another space. It looks the space up and shows its name and icon, and when the local player walks into it, the app takes them there. In SDK Play Mode you stay in your world, and the Console says where the portal would have gone. See [Portal](../building-in-unity/easy-prefabs.md#portal-bsportal) for its size and placement.

<div class="docs-tabs">

```js
await obj.AddComponent(new BS.Portal({
    url: "https://my-world.worldspace.host"   // the other space's address
}));
```

![BS Portal in the Unity Inspector](../images/components/portal.png)

</div>
