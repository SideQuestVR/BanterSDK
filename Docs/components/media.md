# Media & Content Components

Bring outside content into a world: glTF models, asset bundles, video, web browsers, Street View and portals.

## GLTF

Loads 3D models in glTF/GLB format.

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

## AssetBundle

Loads Unity asset bundles (for advanced content).

```js
obj.AddComponent(new BS.AssetBundle({
    windowsUrl: "https://example.com/windows.bundle",
    androidUrl: "https://example.com/android.bundle",
    osxUrl: null,
    linuxUrl: null,
    iosUrl: null,
    vosUrl: null,               // Vision OS
    isScene: false,             // Load as scene vs prefabs
    legacyShaderFix: false
}));
```

## VideoPlayer

Plays video on a surface.

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

**Methods:**

```js
browser.ToggleInteraction(true);
browser.ToggleKeyboard(true);   // Enable or disable keyboard input for the browser
browser.RunActions("click2d,0.5,0.5");
```

## StreetView

Google Street View panorama viewer.

```js
obj.AddComponent(new BS.StreetView({
    panoId: "CAoSLEFGM..."  // Street View panorama ID
}));
```

## Portal

Creates a portal to another space.

```js
obj.AddComponent(new BS.Portal({
    url: "https://my-world.worldspace.host",
    instance: "instance-id"
}));
```
