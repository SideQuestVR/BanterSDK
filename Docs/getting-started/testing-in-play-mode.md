# Testing in Play Mode

Press **Play** and your world runs in the Editor: your page loads, its scripts run against the open scene, and you move around it with a desktop player. You don't need to build, upload or sign in first.

## What happens when you press Play

- **The open scene is the world.** Play mode runs the scene you have open, not a built bundle, so there's nothing to build first.
- **Your page loads from your project.** The SDK serves `Assets/WebRoot` from your machine at `http://localhost:42068/`, and the space browser opens it a couple of seconds after Play starts. Every file in the folder is served, so stylesheets, images and extra scripts all work here. Only some of them are published, though: see [What gets uploaded](../building-in-unity/publishing.md#what-gets-uploaded).
- **Nothing needs adding to the scene.** If the scene has no BSStarterUpper (the object that runs the page and links it to Unity), Play adds one and the Console says `BSStarterUpper not found, adding one.` Builds leave it out. If `Assets/WebRoot/index.html` doesn't exist yet, Play creates a starter page.
- **A desktop player appears** at the world origin and is moved to one of your active [spawn points](../building-in-unity/easy-prefabs.md#spawn-point-and-spawn-range-bsspawn), if the scene has any.

Changes to `index.html` show the next time you press Play. Your page's address in Play mode is `http://localhost:42068/`, not your world's address, so code that builds URLs from `location` should allow for both.

## Moving around

The desktop player flies like the Scene view camera. Hold the **right mouse button** to look and move:

| Input | What it does |
|---|---|
| Right mouse button (hold) | Look around with the mouse. The cursor hides while you hold it and comes back where it was. |
| W, A, S, D | Fly forward, left, back and right, in the direction you're looking. |
| E, Q | Fly up, down. |
| Shift | Four times faster. |
| Scroll, while holding the right button | Change the flying speed (it starts at 3 m/s; 0.1 to 60 m/s). |
| Scroll, with the right button up | Move forward or back 0.5 m a notch (2 m with Shift). Over a web page or UI panel, the scroll goes to the page instead. |

The desktop player has no gravity and no collisions: it flies through walls and floors. Its eyes are 1.6 m above its feet.

## Clicking, grabbing and typing

The **left mouse button** acts on the nearest thing under the cursor, up to 30 m away:

| Under the cursor | What happens |
|---|---|
| A web page or UI panel (BS Browsers, BS UI panels, Unity canvases) | It gets the click, hover and scroll, as on a desktop. |
| A collider on the **Grabbable** layer (20) | You grab it. |
| A collider on the **UI** (5) or **Menu** (22) layer | It's clicked when you let go of the button over the same object: the page's `click` event and Visual Scripting's **On Click** fire. |

Colliders on other layers can't be clicked, as in the app (see [Layers](../building-in-unity/layers.md)). A solid collider in front of a grabbable object blocks the grab; trigger colliders don't, unless they're on the Grabbable, UI or Menu layer.

**Holding something:** the object follows the cursor at the distance you grabbed it. Scroll to push it away or pull it in (0.3 to 30 m), and let go of the left button to drop it; flick the mouse as you let go to throw it. You can fly while holding it. The page's `grab` and `release` events fire as in the app, for the right hand.

While you hold an object, these keys are the right controller's buttons, for the object's [Held Events](../components/vr-interaction.md#heldevents):

| Key | Controller input |
|---|---|
| 3 | Trigger |
| Space | Primary button |
| Tab | Secondary button |
| 4 | Thumbstick click |
| Arrow keys | Thumbstick |

**Typing:** once a text field in a web page has focus, type on your keyboard. Keys don't reach pages while you hold the right mouse button, so flying never types into a page.

**Seats:** click a seat to sit on it. **Space** stands you up (unless you're holding something: then Space is its primary button), and so does flying with W, A, S, D, Q or E, unless the seat turns those off. See [Seat](../building-in-unity/easy-prefabs.md#seat-bsseat).

**Triggers, teleporters and portals:** the desktop player carries a trigger collider tagged `BSLocalCharacter`, so spawn points, teleporters, trigger volumes and portals notice it. It has none of the head, hand and feet tags; only the app's player carries those. A portal doesn't take you anywhere in Play mode: the Console says where it would go.

## Scene cameras and audio

The desktop player brings its own camera (60° field of view) and Audio Listener. When Play starts, and whenever a scene loads, the SDK switches off every other enabled camera tagged **MainCamera** (the Console says `[Desktop] Disabled scene camera '…'`) and every other Audio Listener. Cameras with other tags keep running.

In the app the player brings their camera and listener too, so remove your scene's: the Builder's checklist warns about scene cameras that draw to the screen and about Audio Listeners, with a button to fix each.

## Page logs and developer tools

- **The Unity Console** shows your page's `console.log` lines, starting `[Ora JS]`. Lines from BS Browser pages appear there too.
- **Page developer tools:** while the world is playing, open **Creator SDK > Setup** and press **Open** on **Page developer tools** (under **Tools**). You get Chrome's developer tools for your world's page: its console, with any script errors, its network requests and its elements. The button only works in Play mode; if the Console says `The page hasn't started yet`, wait for the world to load and try again. These tools are for your world's page, not for BS Browsers.

## What works in Play mode

Play mode runs your page, scene and graphs for real, but some things only the app does:

| | In Play mode | In the app |
|---|---|---|
| Your page's scripts, snippets and Visual Scripting graphs | Yes | Yes |
| Clicking (UI and Menu layers), grabbing, held-object buttons | With the mouse and keys above | With controllers |
| BS Browsers, UI panels, mirrors, physics | Yes | Yes |
| Spawn points, teleporters, seats, trigger volumes | Yes | Yes |
| `TeleportTo`, and `SetCanMove`, `SetCanRotate`, `SetCanGrab`, `SetBlockLeftThumbstick` | Yes, on the desktop player | Yes |
| Portals | The Console says where it would go | Takes the player there |
| [Scene settings](../javascript-api/scene-settings.md) | Only the clipping planes | Yes |
| [Controller events](../javascript-api/scene-settings.md#controller-input-events) (`button-pressed`, `button-released`, `controller-axis-update`, `trigger-axis-update`) and `key-press` | No | Controller events with VR controllers only; `key-press` on desktop |
| `pose-update` | Only with local multiplayer on | Yes |
| Other players, space state, user state, one-shots | Only with local multiplayer on: see [Playing without local multiplayer](../multiplayer/local-testing.md#playing-without-local-multiplayer) | Yes: see [Multiplayer](../multiplayer/overview.md) |
| `AddPlayerForce`, haptics, toasts | No effect | Yes |
| [Script Graph Bridge](../visual-scripting/script-graph-bridge.md) | No | Yes |
| Player tags | `BSLocalCharacter` only | All five |

## If nothing responds

- **The mouse and keys do nothing:** the project's **Active Input Handling** has to be **Both**. The Setup panel's item sets it; Unity restarts to apply it.
- **Clicks do nothing:** the object needs a collider on the UI or Menu layer, and nothing else in front of it.

More in [Troubleshooting](../reference/troubleshooting.md).
