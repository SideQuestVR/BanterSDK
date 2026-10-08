# The Space Browser

Your world's page, `Assets/WebRoot/index.html`, runs in the **space browser**: one browser per space, loaded from your world's URL when someone joins (in SDK Play Mode, from your project). It loads your scene and runs your JavaScript, which gets `window.BS` to drive the world.

Players don't see this browser: it works behind the scenes. You can still show its pixels on any surface, change its size, and run JavaScript in it from Visual Scripting.

## Show it in the world

Its texture reference is `asset_browser_world`. Type it into a BS Material's **Texture** field, and that object shows your page live. Anything the page draws appears there: HTML, a canvas, video. See [Textures](textures.md).

## Change its size

The space browser is 1024 × 768 pixels by default. To fill a 16:9 screen, use the Visual Scripting node **Set World Browser Size** (`BS > Space`):

- **Width** and **Height** in pixels, 1920 × 1080 unless you change them, kept within 320–3840 × 180–2160.
- **0 × 0** puts the default size back, and so does loading the next space.
- Materials showing `asset_browser_world` pick up the resized texture by themselves.

A typical graph: **On Start** → **Set World Browser Size** (1920 × 1080), plus a screen object whose BS Material's texture is `asset_browser_world`. Page scripts can't resize the space browser.

## Run JavaScript in it from Visual Scripting

Graphs can run code in your page and get the answer back:

| Node | What it does |
|---|---|
| **Inject BullSchript** (`BS > Browser`) | Runs the JavaScript in its `BullSchript` input in your page. Give it a `Return ID` to get the result back. |
| **On BullSchript Callback Received** (`Events > BS > Browser`) | Fires with the result for the matching `Return ID`. `Data` is the result as JSON text, so a string arrives in quotes. |
| **Read BullSchript from File** | Reads a `.js` file in your project as text, to pass to **Inject BullSchript**. |

Your page can also reach a graph by itself: `scene.SendToVisualScripting(id, data)` fires **On BullSchript Callback Received** with that `id` and `data` as JSON text.

## Debugging

In Play mode, open the page's DevTools from the Setup panel (**Creator SDK > Setup**): **Tools > Page developer tools > Open**. The page's `console.log` lines also appear in the Unity Console, starting `[Ora JS]`.
