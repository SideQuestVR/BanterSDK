# Troubleshooting

Common problems, what causes them, and how to fix them. Many are caught before you publish by the Builder's [checklist](../building-in-unity/publishing.md#the-checklist): run **Re-check** in the Builder first.

## Clicking and grabbing

### An object can't be clicked

- **It's on the wrong layer.** Only colliders on the **UI** (5) and **Menu** (22) layers can be clicked, in the app and in Play mode. Put the object, or a trigger collider around it, on the UI layer. From JavaScript, set `layer` when you create the object (see [Layers](../building-in-unity/layers.md)).
- **It has no collider.** Clicks hit colliders, not meshes. Add one; a trigger works.
- **You're listening on the wrong object.** The click goes to the object the collider is on. If the collider is on a child, listen on the child.
- **Something is in front of it.** In Play mode the nearest web page, UI panel or grabbable object under the cursor gets the click instead, and the click only counts if you let go of the button over the same object.

### An object can't be grabbed

- **Its collider isn't on the Grabbable layer (20).** Hands only grab colliders on that layer. The checklist moves [grab handles](../components/vr-interaction.md#grabhandle) there for you.
- **A grab handle has no collider of its own.** A `BSGrabHandle` is held through a collider on the same object.
- **It uses `BSGrababble`, placed in the editor.** `BSGrababble` only sets itself up when a page script sets its properties. Use a preset from **GameObject > BS > Grab** instead ([Grab presets](../building-in-unity/easy-prefabs.md#grab-presets)).
- **In Play mode, something solid is in front of it**, or a page script called `scene.SetCanGrab(false)`.

### A seat can't be sat on

A seat is sat on by clicking a collider on it, so the same rules apply: a collider on the seat or its children, on the UI layer. The checklist's **Seats** check finds seats with nothing to click and moves their colliders to the UI layer.

## Scripts

### "… is not a function" on the line after `AddComponent` or `Find`

`AddComponent`, `Find`, `FindByPath`, `Instantiate`, `GetBounds` and `Async()` return Promises. Without `await` you get the Promise, not the component or object, and the next line fails:

```js
scene.On("unity-loaded", async () => {
    const ball = new BS.GameObject({ name: "Ball" });

    // Fails with "rb.AddForce is not a function": rb is a Promise
    // const rb = ball.AddComponent(new BS.Rigidbody({ mass: 1 }));

    // Works
    const rb = await ball.AddComponent(new BS.Rigidbody({ mass: 1 }));
    rb.AddForce(new BS.Vector3(0, 5, 0), BS.ForceMode.Impulse);
});
```

`await` only works inside an `async` function, so mark your event handlers `async`. `Find` and `FindByPath` resolve to `undefined` when no object matches.

### I can't see my script's errors

Open the page's developer tools while the world is playing: **Creator SDK > Setup**, then **Open** on **Page developer tools**. Errors show in its console. Your `console.log` lines also appear in the Unity Console, starting `[Ora JS]`. See [Testing in Play Mode](../getting-started/testing-in-play-mode.md#page-logs-and-developer-tools).

### My C# scripts don't run in the published world

A built world carries no code: only the scene and its assets. Your own `MonoBehaviour` scripts work in Play mode, where your project's code is loaded, but the app loads them as missing scripts, which do nothing. Script the world with JavaScript in its page or with Visual Scripting instead.

### The build stops: "Visual Scripting node type(s) aren't allowed in spaces"

The app only runs graphs whose nodes are on its allow list, so the checklist blocks the build and lists the node types it found. Remove or replace those nodes. Graph assets anywhere in the project count, as do graphs in prefabs, not only the scene's: delete graph assets you don't use, such as ones from imported samples. When the project loads, the Console also reports them, starting `[VisualScripting] Found elements that are not allowed for Visual Scripting`. See [Visual Scripting](../visual-scripting/overview.md#build-validation).

### Snippets or scripts from another scene run in my world

Every scene in a project shares the one page, `Assets/WebRoot/index.html`, so snippets placed in one scene, and page code written for it, are published with every world built from the project. Keep one scene per project. The Setup panel's **One scene per project** item and the checklist's matching warning list the other scenes.

## Publishing

### Files work in Play mode but are missing once published

Play mode serves the whole `Assets/WebRoot` folder, but the Builder uploads only two web files, `index.html` and one script file (plus the built scene). Put CSS and JavaScript inside `index.html` or `script.js`, load files from a full `https://` address, or upload them on your world's **Edit world > Assets** page on altvr.app. See [What gets uploaded](../building-in-unity/publishing.md#what-gets-uploaded).

### My script changes don't show, or the wrong script runs

`Assets/WebRoot` has both `script.js` and `bullshcript.js`. A world has one script file, served under both names, so the Builder uploads only `script.js` (the log says so), and a page that loads `bullshcript.js` gets `script.js` too. Move what `bullshcript.js` does into `script.js` and delete `bullshcript.js`.

### My changes don't show in the app

- **They weren't uploaded.** **Build** on its own doesn't upload unless you're signed in with **Upload after building** ticked (the button then says **Build & upload**). For page changes, use **Upload HTML + JS**.
- **The old build was uploaded.** **Upload all** sends the `asset.world` already in `Assets/WebRoot`. Build again after changing the scene.
- **You're still in the world.** Players get the new version when they next load it. Rejoin.
- **The app's copy of your scene is recent.** The app keeps a copy of your world's built scene and trusts a copy it checked in the last five minutes. Wait a few minutes and rejoin. The page and script are never cached.

In Play mode, changes to `index.html` show the next time you press **Play**.

### "We couldn't find that world, so we opened the browser instead."

The world's page made nothing within about 30 seconds. Usually the `<html>` tag in `index.html` has lost its `world-asset` attribute, which is what loads your scene: put it back (`<html world-asset>`) and upload the page again. See [Page attributes](../building-in-unity/publishing.md#page-attributes).

### Nobody else can get into my world

Worlds the Builder creates are Private. On altvr.app, open your world's page, choose **Edit world** and change **Who can join**. See [Visiting your world](../building-in-unity/publishing.md#visiting-your-world).

### Builder messages

| Message | What to do |
|---|---|
| `No world selected, please select or create a world.` | Pick a world in the **World** dropdown, or press **Create one**. |
| `Blocked by the checklist: …` | Fix the issues marked **Blocks the build** in the checklist. |
| `File not found, skipping: …asset.world` | Build before **Upload all**. |
| `FAILED UPLOADING …` | The upload of that file failed; the world keeps its previous copy. Try again. A file over its [size limit](../building-in-unity/publishing.md#what-gets-uploaded) (300 MB for the built scene, 10 MB for the page and the script) always fails. |

## It looks or behaves wrong in the app

### Pink materials

- **Everywhere:** the material uses one of Unity's Built-in pipeline shaders (Standard, Legacy Shaders, Mobile, Nature…), its shader is missing or broken, or a material slot is empty. The checklist's **Materials** check lists them. Switch to a URP shader such as **Universal Render Pipeline/Lit**, or select the materials and use **Edit > Rendering > Materials > Convert Selected Built-in Materials to URP**.
- **Only on Quest, or only on Windows:** the world has no shaders for the graphics API that device uses. Run the Setup panel's **Graphics APIs** item: Auto Graphics API on Android (Quest runs Vulkan), and Direct3D 11, Direct3D 12 and Vulkan on Windows. Then build again.

### Tags don't match in the app

A built world stores each object's tag as its position in the tag list, and the app reads that position against its own list. So the project must have exactly the app's tags, in the app's order: the Setup panel's **SDK layers and tags** item sets them up. The checklist's **Tags** check finds objects using tags the app doesn't have and renames or removes them. If a removed tag comes back, an object still uses it; the checklist lists it. See [Tags](../building-in-unity/tags.md).

### Objects behave oddly on some layers

Layers 25 and up belong to the app, which uses them for its own rendering and physics. The checklist's **Layers** check finds objects on them and moves them to Default. See [Layers](../building-in-unity/layers.md).

### Players stand on air, or get stuck on a floor

A big static mesh collider has **Convex** ticked, so players collide with a rough hull instead of the surface they see. Untick **Convex** on floors, walls and other static geometry; the checklist's **Convex mesh colliders** check finds them (4 m or wider) and unticks them for you. Only colliders on moving rigidbodies and triggers need **Convex**.

### A Unity component works in Play mode but not in the app

The app only has the code for the Unity packages it includes. AI Navigation (NavMesh), Splines, Cinemachine 3, Terrain and Visual Scripting are included. A component from a package the app doesn't have arrives as a missing script and does nothing.

### A scene camera covers the player's view

The player brings their own camera, so a scene camera that draws to the screen draws the world a second time over it. Give the camera a Render Texture (for screens and mirrors) or disable it; the checklist's **Cameras and audio listeners** check does that for you. Play mode hides the problem, because it switches off scene cameras tagged MainCamera.

### Players arrive at the world origin

The scene has no active spawn point. Add one with **GameObject > BS > Player > Spawn Point** ([Spawn Point and Spawn Range](../building-in-unity/easy-prefabs.md#spawn-point-and-spawn-range-bsspawn)).

### A teleporter keeps teleporting the player

Its destination is inside its own trigger, so the player lands back in it and is teleported again whenever the cooldown ends. Move the destination outside the trigger.

## Play mode

### The mouse and keyboard do nothing

The project's **Active Input Handling** has to be **Both**: the desktop player, mouse grabbing, UI clicks and typing into pages need it. Run the item in the Setup panel; Unity restarts to apply it.

### Page developer tools won't open

They only work while the world is playing. If the Console says `The page hasn't started yet`, wait for the world to load and press **Open** again.

### Something does nothing in Play mode

Some features only the app provides: controller and key events, scene settings other than the clipping planes, portals, `AddPlayerForce`, haptics, toasts and the Script Graph Bridge. Other players, space state, user state and one-shots need local multiplayer turned on. See [What works in Play mode](../getting-started/testing-in-play-mode.md#what-works-in-play-mode) and [Testing Multiplayer Locally](../multiplayer/local-testing.md).
