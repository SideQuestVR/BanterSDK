# Performance

A world has to download, fit in memory and keep a steady frame rate on a Quest as well as on a Windows PC. The Quest has far less to spare, so build for it first. This page covers the tools that measure a world and the things that cost the most.

## Measuring

### Scene stats

When you drop a scene into the Builder, it shows a one-line summary under the scene's path:

`250K triangles (40 meshes) · 180.0 MB texture memory (65 textures)`

It's counted from the assets the scene uses, without opening it: each mesh and each texture counts once, however many times the scene uses it, and the texture figure is what they take in the Editor as they're imported now. Treat it as a quick check, not a measurement of what a headset draws.

The checklist's **Scene size** check warns when the scene's assets go over about **1,000,000 triangles** or **1,024 MB of textures**. They're rough budgets for a whole world on Quest, not targets: the further under them a world stays, the faster it loads and the more room it leaves.

### The Bundle Analyzer

**Analyze bundle** in the Builder opens the Bundle Analyzer on the open scene (save it first). It lists every asset the scene pulls into the build, with an estimated size for the platform you pick: **Android** for Quest, **Standalone** for Windows. Use it to find what makes a world big:

- **The list** can be searched and filtered by type; click a row to find the asset in the Project window.
- **Size by Type** shows which kinds of asset take the space.
- **The block view** draws each asset as a block sized by its estimate; click one to find it.
- **The status line** gives an estimated final size for the whole build. Each asset's estimate comes from its import settings for that platform, then the total is cut to about a quarter for the compression the build applies on top. That last step is a rough average, so treat the total as a ballpark.

This is download size. Memory is a different number: the build is compressed for the download, and audio set to **Decompress On Load**, for example, grows once it's loaded.

### In Play mode and in the headset

Unity's Profiler, Frame Debugger and the Game view's **Stats** work in Play mode, and show where the time goes in your scene and scripts. But Play mode runs on your PC, so it can't tell you how a Quest copes. Before you share a world, visit it on a Quest ([Visiting your world](../building-in-unity/publishing.md#visiting-your-world)).

## Download and memory

- **Textures:** the checklist's **Android textures** check warns about textures bigger than **2048 px** on Android, and about uncompressed ones of 1024 px or more: uncompressed textures take several times the memory of ASTC. In each texture's import settings, set the **Android** tab's **Max Size** to 2048 or less and leave it compressed.
- **Audio:** the **Audio** check warns about clips over **10 seconds** set to PCM or **Decompress On Load**, which are held in memory uncompressed (three minutes of stereo music is about 30 MB). Use Vorbis with **Compressed In Memory**, or **Streaming** for music.
- **Lightmaps** are textures too, and count like any other.
- **Detail only one platform needs:** a [Platform Filter](../building-in-unity/platform-filter.md) leaves objects out of the Quest build, or out of the Windows one.
- **Everything the scene references is built in**, for both platforms. Remove what you don't use.

The built world can be up to 300 MB ([Size limits](../building-in-unity/publishing.md#what-gets-uploaded)), but players wait for every megabyte when they join.

## Rendering on Quest and Windows

The app renders differently on each:

| | Quest | Windows |
|---|---|---|
| Rendering path | Forward | Forward+ |
| Extra lights (beyond the main directional light) | At most 4 per object | No per-object limit |
| Shadows | Only the main directional light casts them: hard shadows, one cascade, a 1024 px shadow map | Every shadow-casting light; soft shadows and four cascades available |
| Graphics API | Vulkan (OpenGL ES 3 as a fallback) | Direct3D 11, Direct3D 12 or Vulkan |

What follows from that:

- **Bake your lighting.** Baked lights cost nothing per frame. A realtime point or spot light lights every object it touches every frame, and on a Quest it casts no shadows and counts towards each object's limit of four. Bake in Linear color space (the Setup panel sets it), or baked lighting comes out too bright or too dark in the app.
- **Mirrors are expensive.** Every [Mirror](../components/rendering.md#mirror) draws the whole scene twice more each frame (once per eye) into its own textures, `renderTextureSize` pixels square (1024 by default), whether or not anyone is looking at it. Use few, keep their textures small, and turn a mirror's object off when nobody is near it.
- **Scene cameras that draw to the screen** draw the whole world again on top of the player's view. The checklist finds and disables them.

## Browsers

Every [BS Browser](../browser/bs-browser.md) is a whole Chromium browser, with its own memory and CPU, and its page is copied into a texture every frame:

- **On Windows** the page is uploaded from memory every frame, even when it isn't on screen: width × height × 4 bytes, so about 3.7 MB a frame for a 1280 × 720 page.
- **On Quest** the copy happens on the graphics chip (with Vulkan), which is cheaper, but still happens every frame.

A few browsers are fine; dozens are not. Keep pages no bigger than they need to be, remove a browser you've finished with, and don't rely on `ToggleInteraction(false)`: it stops clicks, not drawing. See [The BS Browser](../browser/bs-browser.md#performance).

Your world's own page, the [space browser](../browser/space-browser.md), is copied the same way, at 1024 × 768 unless you change it. **Set World Browser Size** makes that copy bigger, up to 3840 × 2160 ([Change its size](../browser/space-browser.md#change-its-size)), so only enlarge it if you show the page on a surface.

## Physics and syncing

- **Synced objects:** the owner of each [Synced Object](../components/special.md#syncedobject) sends its position and rotation up to 30 times a second. Players more than 10 m from it get fewer updates, down to 2 a second beyond 150 m. Sync the objects players use or watch, not every prop.
- **Mesh colliders:** a simple box, sphere or capsule collider is cheaper than a mesh collider. A mesh collider on a moving Rigidbody has to be convex; on static floors and walls it shouldn't be (the checklist's **Convex mesh colliders** check explains why).
- **Rigidbodies** at rest go to sleep and cost little until something wakes them.

## Scripts

- **Page scripts talk to Unity in messages.** Creating objects, setting properties and calling methods from JavaScript each send a message from the page to Unity. A handful a frame is nothing; changing hundreds of objects every frame from JavaScript is not. Leave per-frame motion to Unity (Rigidbodies, animation, Visual Scripting) and send the page only what changes.
- **Visual Scripting** graphs run every frame from an **On Update** node: keep those graphs small, and use events where you can.
- **[AO Baking](../components/special.md#aobaking)** runs on each player's device when it bakes: it merges the child meshes into one and works out their shading there. Bake small groups, once.
