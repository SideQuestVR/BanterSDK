# Media & Content Components

## GLTF

Loads 3D models in glTF/GLB format.

<div class="docs-tabs">

```js
obj.AddComponent(new BS.GLTF({
    url: "https://example.com/model.glb",
    generateMipMaps: false,
    addColliders: false,        // Auto-generate colliders
    nonConvexColliders: false,  // Use mesh colliders
    slippery: false,            // Low friction
    climbable: false,           // VR climbing surface
    legacyRotate: false,
    childrenLayer: 0            // Layer for child objects
}));
```

![BS GLTF in the Unity Inspector](../images/components/gltf.png)

</div>

## VideoPlayer

Plays video on a surface.

<div class="docs-tabs">

```js
const video = obj.AddComponent(new BS.VideoPlayer({
    url: "https://example.com/video.mp4",
    volume: 1,
    loop: true,
    playOnAwake: true,
    skipOnDrop: true,
    waitForFirstFrame: true
}));
```

![BS Video Player in the Unity Inspector](../images/components/video-player.png)

</div>

**Properties:**

```js
video.time = 30;        // Seek to 30 seconds
video.isPlaying;        // Read current state
video.isLooping;
```

**Methods:**

```js
video.Play();
video.Pause();
video.Stop();
video.PlayToggle();   // Toggle between play and pause
video.MuteToggle();   // Toggle mute
```

## Browser

Embedded web browser on a surface.

<div class="docs-tabs">

```js
const browser = obj.AddComponent(new BS.Browser({
    url: "https://example.com",
    mipMaps: 4,
    pixelsPerUnit: 1200,
    pageWidth: 1280,
    pageHeight: 720,
    actions: ""             // Startup actions
}));
```

![BS Browser in the Unity Inspector](../images/components/browser.png)

</div>

**Methods:**

```js
browser.ToggleInteraction(true);
browser.ToggleKeyboard(true);   // Enable or disable keyboard input for the browser
browser.RunActions("click2d,0.5,0.5");
```

**Size:** the page is `pageWidth` × `pageHeight` pixels, drawn at 1300 pixels per metre, so 1280 × 720 is about 0.98 × 0.55 m. Set them when you create the browser or any time later: the page lays itself out again at the new size, and the browser grows or shrinks with it. A browser created from a script starts at 1024 × 576. `pixelsPerUnit` isn't used.

```js
browser.pageWidth = 1920;
browser.pageHeight = 1080;
```

**Texture:** every browser's page is also a texture, so materials can show it on any surface. Its reference is `asset_browser_` followed by the browser's `unityId`, which is set once Unity has linked the browser. A material using it keeps up when a resize replaces the texture.

```js
await browser.Async();   // until Unity has linked it, so unityId is set
screen.AddComponent(new BS.Material({
    shaderName: "Unlit/Diffuse",
    texture: "asset_browser_" + browser.unityId
}));
```

### The space's own browser

The page your space runs in is a browser too, though nothing shows it unless you use its texture:

- **Texture:** materials can use it as `asset_browser_world`, and the Visual Scripting event **On World Browser Texture** outputs it when it first paints and again after a resize.
- **Size:** 1024 × 768 by default. The Visual Scripting node **Set World Browser Size** changes it, for example to 1920 × 1080 for a 16:9 screen; 0 × 0 puts the default back, and so does loading the next space. Page scripts can't resize it.

## StreetView

Google Street View panorama viewer.

<div class="docs-tabs">

```js
obj.AddComponent(new BS.StreetView({
    panoId: "CAoSLEFGM..."  // Street View panorama ID
}));
```

![BS Street View in the Unity Inspector](../images/components/street-view.png)

</div>

## Portal

Creates a portal to another space.

<div class="docs-tabs">

```js
obj.AddComponent(new BS.Portal({
    url: "https://my-world.worldspace.host",
    instance: "instance-id"
}));
```

![BS Portal in the Unity Inspector](../images/components/portal.png)

</div>
